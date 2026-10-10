#!/usr/bin/env python3
"""PATCH-DOTNET-001: audited, zero-patch, immutable-role corpus materializer.

No patch or semantic transform is created; only source hashes, byte-exact
version-pair membership and source provenance are inventoried.
Usage:
  python benchmarks/scripts/patch_dotnet_001_corpus.py validate --plan FILE
  python benchmarks/scripts/patch_dotnet_001_corpus.py materialize --plan FILE --sources DIR --root DIR
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import stat
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path, PurePosixPath
from urllib.parse import urlparse

SCHEMA = "chunkshift.patch-dotnet-corpus-plan.v1"
PAIR_SCHEMA = "chunkshift.patch-pairs.v1"
AUDIT_SCHEMA = "chunkshift.patch-dotnet-inventory.v1"
CHUNK = 1024 * 1024
MAX_ENTRY = 1 << 30
MAX_ARCHIVE_UNPACKED = 12 << 30
ALLOWED_ROLES = {"calibration", "evaluation", "negative-control"}
ALLOWED_RULES = {"replace-dotnet10-version-components", "strip-first", "as-is"}
HEX = re.compile(r"^[0-9a-f]+$")
ID = re.compile(r"^[a-z][a-z0-9-]{0,79}$")
DOTNET_VERSION = re.compile(r"^10\.0\.[0-9]+$")
PRODUCTS = {"aspnetcore-runtime", "dotnet-runtime", "dotnet-sdk",
            "windowsdesktop-runtime", "node"}


class CorpusError(ValueError):
    pass


def fail(message: str) -> None:
    raise CorpusError(message)


def digest_file(path: Path, kind: str) -> str:
    h = hashlib.new(kind)
    with path.open("rb") as f:
        for block in iter(lambda: f.read(CHUNK), b""):
            h.update(block)
    return h.hexdigest()


def sha256_file(path: Path) -> str:
    return digest_file(path, "sha256")


def encode_json(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, indent=2, ensure_ascii=True) + "\n").encode("utf-8")


def validate_plan(data: object) -> list[dict]:
    if not isinstance(data, dict) or data.get("schema") != SCHEMA or data.get("experimentId") != "PATCH-DOTNET-001":
        fail("CORPUS_PLAN_SCHEMA")
    groups = data.get("groups")
    if not isinstance(groups, list) or not groups:
        fail("CORPUS_GROUPS_EMPTY")
    ids: set[str] = set()
    assets_seen: dict[str, tuple[str, str]] = {}
    role_by_product: dict[str, str] = {}
    counts = {role: 0 for role in ALLOWED_ROLES}

    for group in groups:
        if not isinstance(group, dict):
            fail("CORPUS_GROUP_OBJECT")
        gid, role, product = (group.get(k) for k in ("id", "role", "product"))
        if not isinstance(gid, str) or not ID.fullmatch(gid) or gid in ids:
            fail("CORPUS_GROUP_ID_DUPLICATE_OR_UNSAFE")
        ids.add(gid)
        if role not in ALLOWED_ROLES or product not in PRODUCTS:
            fail("CORPUS_ROLE_OR_PRODUCT")
        if product == "node" and role != "negative-control":
            fail("CORPUS_NATIVE_CONTROL_LEAK")
        if product != "node" and role == "negative-control":
            fail("CORPUS_DOTNET_NEGATIVE_CONTROL_NOT_PROVEN")
        if product in role_by_product and role_by_product[product] != role:
            fail("CORPUS_PRODUCT_FAMILY_LEAKAGE")
        role_by_product[product] = role
        counts[role] += 1
        if group.get("pathRule") not in ALLOWED_RULES or group.get("format") not in {"zip", "tar.gz"}:
            fail("CORPUS_EXTRACTION_RULE")
        if not isinstance(group.get("rid"), str) or not ID.fullmatch(group["rid"]):
            fail("CORPUS_RID")
        base, target = group.get("base"), group.get("target")
        if not all(isinstance(v, str) and v and "/" not in v and "\\" not in v and ".." not in v
                   for v in (base, target)) or base == target:
            fail("CORPUS_VERSION_PAIR")
        assets = group.get("assets")
        if not isinstance(assets, list) or len(assets) != 2:
            fail("CORPUS_TWO_ASSETS_REQUIRED")
        if [a.get("version") for a in assets if isinstance(a, dict)] != [base, target]:
            fail("CORPUS_ASSET_VERSION_ORDER")
        for asset in assets:
            name, url, algorithm, digest = (asset.get(k) for k in
                                            ("name", "url", "digestAlgorithm", "digest"))
            if not isinstance(name, str) or name in {"", ".", ".."} or Path(name).name != name \
                    or "/" in name or "\\" in name:
                fail("CORPUS_ASSET_NAME")
            if not name.endswith(".zip" if group["format"] == "zip" else ".tar.gz"):
                fail("CORPUS_ASSET_FORMAT")
            if not isinstance(url, str):
                fail("CORPUS_ASSET_URL")
            parsed = urlparse(url)
            if parsed.scheme != "https" or parsed.hostname not in {
                "builds.dotnet.microsoft.com", "nodejs.org"
            } or parsed.path.rsplit("/", 1)[-1] != name or parsed.query or parsed.fragment:
                fail("CORPUS_ASSET_SOURCE_PROVENANCE")
            if algorithm not in {"sha256", "sha512"} or not isinstance(digest, str) \
                    or len(digest) != {"sha256": 64, "sha512": 128}[algorithm] \
                    or not HEX.fullmatch(digest):
                fail("CORPUS_ASSET_DIGEST")
            identity = (algorithm, digest)
            if name in assets_seen and assets_seen[name] != identity:
                fail("CORPUS_ASSET_COLLISION")
            assets_seen[name] = identity

    if counts["calibration"] < 1 or counts["evaluation"] < 2 or counts["negative-control"] < 1:
        fail("CORPUS_INSUFFICIENT_PREDECLARED_COHORTS")
    if "dotnet-sdk" not in role_by_product or role_by_product["dotnet-sdk"] != "evaluation":
        fail("CORPUS_SDK_HOLDOUT_REQUIRED")
    return sorted(groups, key=lambda x: x["id"])


def relative_member(name: str, rule: str) -> str | None:
    if not isinstance(name, str) or not name or "\x00" in name or "\\" in name or name.startswith("/"):
        fail("CORPUS_UNSAFE_ARCHIVE_PATH")
    raw = name.split("/")
    if any(p == ".." or ":" in p for p in raw):
        fail("CORPUS_ARCHIVE_TRAVERSAL")
    parts = [p for p in raw if p not in {"", "."}]
    if rule == "strip-first":
        parts = parts[1:]
    elif rule == "replace-dotnet10-version-components":
        parts = ["{version}" if DOTNET_VERSION.fullmatch(p) else p for p in parts]
    if not parts:
        return None
    normalized = "/".join(parts)
    if PurePosixPath(normalized).is_absolute() or any(p in {"", ".", ".."} for p in parts):
        fail("CORPUS_UNSAFE_NORMALIZED_PATH")
    return normalized


def verified_assets(groups: list[dict], sources: Path) -> dict[str, dict]:
    checks: dict[str, dict] = {}
    for group in groups:
        for asset in group["assets"]:
            name = asset["name"]
            if name in checks:
                continue
            path = sources / name
            if not path.is_file() or path.is_symlink():
                fail(f"CORPUS_ASSET_MISSING_OR_SYMLINK:{name}")
            actual = digest_file(path, asset["digestAlgorithm"])
            if actual != asset["digest"]:
                fail(f"CORPUS_SOURCE_DIGEST_MISMATCH:{name}")
            checks[name] = {
                "name": name,
                "algorithm": asset["digestAlgorithm"],
                "digest": actual,
                "sha256": sha256_file(path),
                "bytes": path.stat().st_size,
                "url": asset["url"],
            }
    return checks


def extract_archive(source: Path, group: dict, dest: Path) -> dict:
    seen: set[str] = set()
    total = 0
    files = 0
    skipped_links = 0

    def write(name: str, stream, size: int) -> None:
        nonlocal total, files
        relative = relative_member(name, group["pathRule"])
        if relative is None:
            return
        if size < 0 or size > MAX_ENTRY or total + size > MAX_ARCHIVE_UNPACKED:
            fail("CORPUS_UNPACKED_BYTES_LIMIT")
        key = relative.casefold()
        if key in seen:
            fail(f"CORPUS_DUPLICATE_ARCHIVE_PATH:{relative}")
        seen.add(key)
        target = dest.joinpath(*relative.split("/"))
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as output:
            copied = 0
            while True:
                block = stream.read(min(CHUNK, size - copied))
                if not block:
                    break
                copied += len(block)
                output.write(block)
            if copied != size:
                fail("CORPUS_ARCHIVE_MEMBER_TRUNCATED")
        total += copied
        files += 1

    if group["format"] == "zip":
        with zipfile.ZipFile(source) as archive:
            for item in archive.infolist():
                if item.is_dir():
                    continue
                mode = (item.external_attr >> 16) & 0o170000
                if mode == stat.S_IFLNK:
                    skipped_links += 1
                    continue
                if mode not in {0, stat.S_IFREG}:
                    fail("CORPUS_UNSUPPORTED_ZIP_MEMBER")
                with archive.open(item) as reader:
                    write(item.filename, reader, item.file_size)
    else:
        with tarfile.open(source, mode="r:gz") as archive:
            for item in archive:
                if item.isdir():
                    continue
                if item.issym() or item.islnk():
                    skipped_links += 1
                    continue
                if not item.isfile():
                    fail("CORPUS_UNSUPPORTED_TAR_MEMBER")
                stream = archive.extractfile(item)
                if stream is None:
                    fail("CORPUS_UNREADABLE_TAR_MEMBER")
                with stream:
                    write(item.name, stream, item.size)
    if files == 0:
        fail("CORPUS_EMPTY_ARCHIVE")
    return {"files": files, "bytes": total, "skippedLinks": skipped_links}


def walk_files(root: Path) -> dict[str, tuple[int, str]]:
    found = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            fail("CORPUS_EXTRACTED_SYMLINK")
        if path.is_file():
            found[path.relative_to(root).as_posix()] = (path.stat().st_size, sha256_file(path))
    return found


def pair_record(group: dict, root: Path) -> dict:
    left = walk_files(root / "tree" / group["id"] / group["base"])
    right = walk_files(root / "tree" / group["id"] / group["target"])
    both = sorted(left.keys() & right.keys())
    changed = [
        {"path": name, "baseSize": left[name][0], "baseSha256": left[name][1],
         "targetSize": right[name][0], "targetSha256": right[name][1]}
        for name in both if left[name][1] != right[name][1]
    ]
    return {
        "family": group["id"], "base": group["base"], "target": group["target"],
        "role": group["role"], "product": group["product"], "rid": group["rid"],
        "changed": changed,
        "added": [{"path": n, "size": right[n][0], "sha256": right[n][1]}
                  for n in sorted(right.keys() - left.keys())],
        "removed": sorted(left.keys() - right.keys()),
        "identicalFiles": len(both) - len(changed),
        "identicalBytes": sum(right[n][0] for n in both if left[n][1] == right[n][1]),
    }


def materialize(raw: bytes, groups: list[dict], sources: Path, root: Path) -> dict:
    checks = verified_assets(groups, sources)  # Verify every source BEFORE any extraction.
    if root.exists():
        fail("CORPUS_DESTINATION_ALREADY_EXISTS")
    root.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".patch-dotnet-stage-", dir=root.parent) as scratch:
        stage = Path(scratch)
        extraction = []
        for group in groups:
            for asset in group["assets"]:
                dest = stage / "tree" / group["id"] / asset["version"]
                dest.mkdir(parents=True)
                stats = extract_archive(sources / asset["name"], group, dest)
                extraction.append({"group": group["id"], "version": asset["version"], **stats})
        pairs = {"schema": PAIR_SCHEMA, "pairs": [pair_record(g, stage) for g in groups]}
        pair_bytes = encode_json(pairs)
        (stage / "pairs.json").write_bytes(pair_bytes)
        audit = {
            "schema": AUDIT_SCHEMA,
            "status": "INVENTORY_ONLY_NOT_DECISION_EVIDENCE",
            "experimentId": "PATCH-DOTNET-001",
            "planSha256": hashlib.sha256(raw).hexdigest(),
            "pairsSha256": hashlib.sha256(pair_bytes).hexdigest(),
            "sourceAssets": sorted(checks.values(), key=lambda a: a["name"]),
            "extractions": extraction,
            "roles": {g["id"]: g["role"] for g in groups},
        }
        (stage / "audit.json").write_bytes(encode_json(audit))
        os.replace(stage, root)
    return audit


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=["validate", "materialize"])
    parser.add_argument("--plan", type=Path, default=Path("docs/benchmarks/patch-dotnet-001/corpus-plan.v1.json"))
    parser.add_argument("--sources", type=Path)
    parser.add_argument("--root", type=Path)
    args = parser.parse_args()
    try:
        raw = args.plan.read_bytes()
        groups = validate_plan(json.loads(raw))
        if args.command == "validate":
            print("PATCH-DOTNET-001 plan PRE-REGISTERED (no decision evidence): " +
                  hashlib.sha256(raw).hexdigest())
            return 0
        if args.sources is None or args.root is None:
            fail("CORPUS_MATERIALIZE_REQUIRES_SOURCES_AND_ROOT")
        audit = materialize(raw, groups, args.sources.resolve(), args.root.resolve())
        print("PATCH-DOTNET-001 INVENTORY_ONLY " + audit["pairsSha256"])
        return 0
    except (CorpusError, OSError, json.JSONDecodeError, zipfile.BadZipFile, tarfile.TarError,
            EOFError, OverflowError) as exc:
        parser.error(str(exc))


if __name__ == "__main__":
    sys.exit(main())
