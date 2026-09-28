#!/usr/bin/env python3
"""Materialize the frozen Patching corpus (PATCH-PREFREEZE-001).

Reconstructs the file trees named by docs/benchmarks/patch-corpus/patch-corpus.json:
downloads each upstream release asset (or takes it from --sources), verifies it
against source-assets.sha256, extracts it with the documented path rule and
writes <root>/pairs.json, the list of changed, added, removed and identical
files of every adjacent version pair. The SHA-256 of pairs.json is the corpus
lock; --lock compares it with corpus-lock.json.

No patch is created here.

  materialize_patch_corpus.py --root DIR [--sources DIR] [--download] [--lock]
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile
from pathlib import Path

CHUNK = 1024 * 1024
USER_AGENT = "ChunkShift-patch-corpus-materializer/1"
REPO = Path(__file__).resolve().parents[2]
CORPUS = REPO / "docs" / "benchmarks" / "patch-corpus"
# 2000-01-01T00:00:00Z: a fixed mtime keeps the source tar independent of
# commit times and of the git version.
SOURCE_MTIME = 946684800


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(CHUNK), b""):
            digest.update(block)
    return digest.hexdigest()


def load_checksums() -> dict[str, str]:
    result: dict[str, str] = {}
    for line in (CORPUS / "source-assets.sha256").read_text(encoding="utf-8").splitlines():
        if line.strip():
            digest, name = line.split(maxsplit=1)
            result[name.strip()] = digest
    return result


def fetch(url: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(1, 4):
        try:
            with urllib.request.urlopen(request, timeout=300) as response, destination.open("wb") as output:
                shutil.copyfileobj(response, output, length=CHUNK)
            return
        except OSError:
            if attempt == 3:
                raise


def source_file(family: dict, version: str, sources: Path, download: bool, checksums: dict[str, str]) -> Path:
    name = family["source"].replace("{v}", version)
    path = sources / name
    if not path.exists():
        if not download:
            raise SystemExit(f"missing source {path} (use --download)")
        sources.mkdir(parents=True, exist_ok=True)
        fetch(family["url"].replace("{v}", version), path)
    actual = sha256_file(path)
    if checksums.get(name) != actual:
        raise SystemExit(f"{name}: SHA-256 {actual} does not match source-assets.sha256")
    return path


def normalize(family: dict, version: str, name: str) -> str | None:
    parts = [part for part in name.replace("\\", "/").split("/") if part]
    if family["path"] == "strip-first":
        parts = parts[1:]
    elif family["path"] == "replace-version":
        parts = ["{version}" if part == version else part for part in parts]
    return "/".join(parts) or None


def extract(family: dict, version: str, archive: Path, tree: Path) -> None:
    seen: set[str] = set()

    def write(name: str, data: bytes) -> None:
        relative = normalize(family, version, name)
        if relative is None:
            return
        # A case-insensitive file system would merge such names silently and
        # the corpus would differ between Windows and Linux.
        if relative.lower() in seen:
            raise SystemExit(f"{archive.name}: names differing only in case: {relative}")
        seen.add(relative.lower())
        target = tree / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)

    if family["layout"] == "zip":
        with zipfile.ZipFile(archive) as package:
            for entry in package.infolist():
                if not entry.is_dir():
                    write(entry.filename, package.read(entry))
    else:
        # Symbolic and hard links are not content; they are skipped.
        with tarfile.open(archive) as package:
            for entry in package.getmembers():
                if entry.isfile():
                    write(entry.name, package.extractfile(entry).read())


def git(*args: str) -> bytes:
    return subprocess.run(["git", "-C", str(REPO), *args], check=True, capture_output=True).stdout


def source_tar(commit: str) -> bytes:
    """Build an uncompressed tar of a commit's tree with fixed metadata."""

    listing = git("ls-tree", "-r", "-z", "--full-tree", commit).split(b"\0")
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w", format=tarfile.GNU_FORMAT) as package:
        for line in filter(None, listing):
            meta, path = line.split(b"\t", 1)
            mode, kind, blob = meta.decode().split()
            if kind != "blob":
                continue
            info = tarfile.TarInfo(path.decode("utf-8"))
            info.mtime = SOURCE_MTIME
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            if mode == "120000":
                info.type = tarfile.SYMTYPE
                info.linkname = git("cat-file", "blob", blob).decode("utf-8")
                info.mode = 0o777
                package.addfile(info)
                continue
            data = git("cat-file", "blob", blob)
            info.size = len(data)
            info.mode = 0o755 if mode == "100755" else 0o644
            package.addfile(info, io.BytesIO(data))
    return output.getvalue()


def files_of(tree: Path) -> dict[str, tuple[int, str]]:
    return {
        path.relative_to(tree).as_posix(): (path.stat().st_size, sha256_file(path))
        for path in sorted(tree.rglob("*"))
        if path.is_file()
    }


def pair_record(family: str, base: str, target: str, root: Path) -> dict:
    base_files = files_of(root / "tree" / family / base)
    target_files = files_of(root / "tree" / family / target)
    changed, identical_bytes, identical = [], 0, 0
    for path in sorted(set(base_files) & set(target_files)):
        (base_size, base_sha), (target_size, target_sha) = base_files[path], target_files[path]
        if base_sha == target_sha:
            identical += 1
            identical_bytes += target_size
        else:
            changed.append({"path": path, "baseSize": base_size, "baseSha256": base_sha,
                            "targetSize": target_size, "targetSha256": target_sha})
    return {
        "family": family, "base": base, "target": target,
        "changed": changed,
        "added": [{"path": p, "size": target_files[p][0], "sha256": target_files[p][1]}
                  for p in sorted(set(target_files) - set(base_files))],
        "removed": sorted(set(base_files) - set(target_files)),
        "identicalFiles": identical, "identicalBytes": identical_bytes,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--sources", type=Path)
    parser.add_argument("--download", action="store_true")
    parser.add_argument("--lock", action="store_true", help="require the lock in corpus-lock.json")
    args = parser.parse_args()

    manifest = json.loads((CORPUS / "patch-corpus.json").read_text(encoding="utf-8"))
    checksums = load_checksums()
    sources = args.sources or args.root / "sources"
    tree_root = args.root / "tree"
    if tree_root.exists():
        shutil.rmtree(tree_root)

    pairs = []
    for family in manifest["families"]:
        versions = family["versions"]
        for version in versions:
            tree = tree_root / family["id"] / version
            tree.mkdir(parents=True)
            if family["layout"] == "git-tree-tar":
                (tree / "source.tar").write_bytes(source_tar(version))
            else:
                extract(family, version, source_file(family, version, sources, args.download, checksums), tree)
        adjacent = family.get("pairs") or list(zip(versions, versions[1:]))
        pairs.extend(pair_record(family["id"], base, target, args.root) for base, target in adjacent)

    document = json.dumps({"schema": "chunkshift.patch-pairs.v1", "pairs": pairs}, indent=1, sort_keys=True) + "\n"
    (args.root / "pairs.json").write_text(document, encoding="utf-8", newline="\n")
    lock = sha256_bytes(document.encode("utf-8"))
    for pair in pairs:
        changed = sum(item["targetSize"] for item in pair["changed"])
        print(f"{pair['family']} {pair['base']}->{pair['target']}: {len(pair['changed'])} changed "
              f"({changed / CHUNK:.1f} MiB), {len(pair['added'])} added, {len(pair['removed'])} removed, "
              f"{pair['identicalFiles']} identical")
    print(f"pairsSha256={lock}")

    if args.lock:
        expected = json.loads((CORPUS / "corpus-lock.json").read_text(encoding="utf-8"))["pairsSha256"]
        if lock != expected:
            print(f"corpus lock mismatch: expected {expected}", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
