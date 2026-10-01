#!/usr/bin/env python3
"""Materialize PATCH-ENC-005's untouched Go/Python confirmation pair list.

This script only downloads, verifies, safely extracts and inventories files. It
never invokes ChunkShift, PatchLab, an encoder or a selector.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import stat
import sys
import tarfile
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

CHUNK = 1024 * 1024
USER_AGENT = "ChunkShift-PATCH-ENC-005-confirmation/1"
REPO = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = REPO / "docs/benchmarks/patch-enc-005-confirmation/manifest.json"
DEFAULT_LOCK = REPO / "docs/benchmarks/patch-enc-005-confirmation/pair-list.lock.json"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(CHUNK), b""):
            digest.update(block)
    return digest.hexdigest()


def fetch(url: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".part")

    with urllib.request.urlopen(request, timeout=300) as response, temporary.open("wb") as output:
        shutil.copyfileobj(response, output, length=CHUNK)

    temporary.replace(destination)


def checked_source(asset: dict, sources: Path, download: bool) -> Path:
    path = sources / asset["name"]

    if not path.exists():
        if not download:
            raise SystemExit(f"missing source {path} (use --download)")
        fetch(asset["url"], path)

    actual = sha256_file(path)
    expected = asset["sha256"]

    if actual != expected:
        raise SystemExit(
            f"{asset['name']}: SHA-256 {actual} does not match frozen {expected}"
        )

    return path


def normalized_member(name: str, strip_first: bool) -> str | None:
    if "\\" in name:
        name = name.replace("\\", "/")

    path = PurePosixPath(name)

    if path.is_absolute() or any(part in ("", ".", "..") for part in path.parts):
        raise SystemExit(f"unsafe archive path: {name!r}")

    parts = list(path.parts)

    if strip_first:
        if len(parts) <= 1:
            return None
        parts = parts[1:]

    relative = PurePosixPath(*parts)

    if relative.is_absolute() or ".." in relative.parts:
        raise SystemExit(f"unsafe normalized path: {name!r}")

    return relative.as_posix()


def write_entry(
    tree: Path,
    archive_name: str,
    member_name: str,
    data: bytes,
    strip_first: bool,
    seen: dict[str, str],
) -> None:
    relative = normalized_member(member_name, strip_first)

    if relative is None:
        return

    folded = relative.casefold()

    if folded in seen:
        raise SystemExit(
            f"{archive_name}: duplicate/case-colliding paths: {seen[folded]!r} and {relative!r}"
        )

    seen[folded] = relative
    target = tree.joinpath(*PurePosixPath(relative).parts)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)


def extract(family: dict, archive: Path, tree: Path) -> None:
    seen: dict[str, str] = {}
    strip_first = bool(family["stripFirst"])

    if family["layout"] == "zip":
        with zipfile.ZipFile(archive) as package:
            for entry in package.infolist():
                if entry.is_dir():
                    continue

                mode = entry.external_attr >> 16

                if stat.S_ISLNK(mode):
                    continue

                write_entry(
                    tree,
                    archive.name,
                    entry.filename,
                    package.read(entry),
                    strip_first,
                    seen,
                )
        return

    if family["layout"] != "tar.gz":
        raise SystemExit(f"unsupported layout {family['layout']!r}")

    with tarfile.open(archive, mode="r:gz") as package:
        for entry in package.getmembers():
            # isfile excludes directories, symlinks and hard links.
            if not entry.isfile():
                continue

            stream = package.extractfile(entry)

            if stream is None:
                raise SystemExit(f"{archive.name}: regular file {entry.name!r} has no payload")

            write_entry(
                tree,
                archive.name,
                entry.name,
                stream.read(),
                strip_first,
                seen,
            )


def files_of(tree: Path) -> dict[str, tuple[int, str]]:
    return {
        path.relative_to(tree).as_posix(): (path.stat().st_size, sha256_file(path))
        for path in sorted(tree.rglob("*"))
        if path.is_file()
    }


def pair_record(family: dict, root: Path) -> dict:
    base, target = family["base"], family["target"]
    base_files = files_of(root / "tree" / family["id"] / base)
    target_files = files_of(root / "tree" / family["id"] / target)
    changed: list[dict] = []
    identical_files = 0
    identical_bytes = 0

    for path in sorted(base_files.keys() & target_files.keys()):
        base_size, base_sha = base_files[path]
        target_size, target_sha = target_files[path]

        if base_sha == target_sha:
            identical_files += 1
            identical_bytes += target_size
        else:
            changed.append(
                {
                    "path": path,
                    "baseSize": base_size,
                    "baseSha256": base_sha,
                    "targetSize": target_size,
                    "targetSha256": target_sha,
                }
            )

    return {
        "family": family["id"],
        "base": base,
        "target": target,
        "changed": changed,
        "added": [
            {"path": path, "size": target_files[path][0], "sha256": target_files[path][1]}
            for path in sorted(target_files.keys() - base_files.keys())
        ],
        "removed": sorted(base_files.keys() - target_files.keys()),
        "identicalFiles": identical_files,
        "identicalBytes": identical_bytes,
    }


def serialize_pairs(pairs: list[dict]) -> bytes:
    text = json.dumps(
        {"schema": "chunkshift.patch-pairs.v1", "pairs": pairs},
        indent=1,
        sort_keys=True,
    ) + "\n"
    return text.encode("utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--sources", type=Path)
    parser.add_argument("--download", action="store_true")
    parser.add_argument("--lock", type=Path, default=DEFAULT_LOCK)
    parser.add_argument("--write-lock", type=Path)
    args = parser.parse_args()

    manifest_bytes = args.manifest.read_bytes()
    manifest = json.loads(manifest_bytes)
    sources = args.sources or args.root / "sources"
    tree_root = args.root / "tree"

    if tree_root.exists():
        shutil.rmtree(tree_root)

    pairs: list[dict] = []

    for family in manifest["families"]:
        for version in (family["base"], family["target"]):
            tree = tree_root / family["id"] / version
            tree.mkdir(parents=True, exist_ok=True)
            archive = checked_source(family["assets"][version], sources, args.download)
            extract(family, archive, tree)

        pairs.append(pair_record(family, args.root))

    pairs_bytes = serialize_pairs(pairs)
    pairs_path = args.root / "pairs.json"
    pairs_path.write_bytes(pairs_bytes)
    pairs_sha256 = hashlib.sha256(pairs_bytes).hexdigest()
    manifest_sha256 = hashlib.sha256(manifest_bytes).hexdigest()

    lock = {
        "schema": "chunkshift.patch-enc-005-confirmation-lock.v1",
        "experimentId": "PATCH-ENC-005",
        "manifestSha256": manifest_sha256,
        "pairsSha256": pairs_sha256,
    }

    print(f"manifestSha256={manifest_sha256}")
    print(f"pairsSha256={pairs_sha256}")

    if args.write_lock is not None:
        args.write_lock.parent.mkdir(parents=True, exist_ok=True)
        args.write_lock.write_text(
            json.dumps(lock, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    if args.lock is not None:
        expected = json.loads(args.lock.read_text(encoding="utf-8"))

        if expected != lock:
            print("confirmation pair-list lock mismatch", file=sys.stderr)
            print(json.dumps(lock, indent=2, sort_keys=True), file=sys.stderr)
            return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
