#!/usr/bin/env python3
"""PATCH-TREE-001 Phase A: immutable, exact virtual tree-concatenation inventory.

Research-only. No CSP/CSM bytes are emitted. A layout digest is NOT ManifestId.
Only regular files from the frozen materialized patch corpus are admissible.
"""
from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import os
import re
import stat
import struct
import unicodedata
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
LOCK = REPO / "docs/benchmarks/patch-corpus/corpus-lock.json"
CORPUS = REPO / "docs/benchmarks/patch-corpus/patch-corpus.json"
SCHEMA = "chunkshift.patch-tree-layout.v1"
MAX_OFFSET = (1 << 63) - 1
MAX_READ = 1 << 20
BLOCK = 1 << 20
_SAFE_LABEL = re.compile(r"[A-Za-z0-9][A-Za-z0-9.+_-]*\Z")
_WIN_DEVICE = {"CON", "PRN", "AUX", "NUL", *("COM" + str(i) for i in range(1, 10)),
               *("LPT" + str(i) for i in range(1, 10))}


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError("PATCH-TREE-001: " + message)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def label(value: str) -> str:
    require(isinstance(value, str) and _SAFE_LABEL.fullmatch(value) is not None,
            "unsafe family/version component")
    require(value not in (".", ".."), "unsafe component")
    return value


def canonical_path(value: str) -> str:
    require(isinstance(value, str) and value and not value.startswith("/"),
            "empty/absolute path")
    require(unicodedata.normalize("NFC", value) == value, "non-NFC path")
    require("\\" not in value and "\x00" not in value, "backslash/NUL path")
    for component in value.split("/"):
        require(component not in ("", ".", "..") and not component.endswith((" ", ".")),
                "ambiguous path component")
        require(not any(ord(c) < 32 or c == ":" for c in component),
                "control/colon path component")
        require(component.split(".")[0].upper() not in _WIN_DEVICE,
                "reserved device path")
    return value


def file_hash(path: Path) -> tuple[int, str]:
    before = path.lstat()
    require(stat.S_ISREG(before.st_mode), "non-regular file: " + str(path))
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(BLOCK), b""):
            digest.update(block)
    after = path.lstat()
    require(stat.S_ISREG(after.st_mode) and before.st_dev == after.st_dev
            and before.st_ino == after.st_ino
            and before.st_size == after.st_size
            and before.st_mtime_ns == after.st_mtime_ns,
            "file changed during inventory: " + str(path))
    return before.st_size, digest.hexdigest()


def layout(tree: Path) -> dict:
    require(tree.is_dir() and not tree.is_symlink(), "missing/symlink tree root")
    names: list[tuple[bytes, str, Path]] = []
    seen: set[str] = set()
    for parent, dirs, files in os.walk(tree, followlinks=False):
        # Check directories as well as files, including links to directories.
        for name in dirs:
            folder = Path(parent) / name
            require(stat.S_ISDIR(folder.lstat().st_mode), "symlink/special directory")
            relative = canonical_path(folder.relative_to(tree).as_posix())
            folded = relative.casefold()
            require(folded not in seen, "casefold collision")
            seen.add(folded)
        for name in files:
            file = Path(parent) / name
            relative = canonical_path(file.relative_to(tree).as_posix())
            folded = relative.casefold()
            require(folded not in seen, "casefold collision")
            seen.add(folded)
            names.append((relative.encode("utf-8"), relative, file))
    names.sort(key=lambda row: row[0])
    entries: list[dict] = []
    offset = 0
    digest = hashlib.sha256()
    for utf8, name, path in names:
        size, content_hash = file_hash(path)
        require(offset + size <= MAX_OFFSET, "virtual base exceeds Int64 offset")
        digest.update(struct.pack("<I", len(utf8)))
        digest.update(utf8)
        digest.update(struct.pack("<Q", size))
        digest.update(bytes.fromhex(content_hash))
        entries.append({"path": name, "offset": offset, "length": size,
                        "sha256": content_hash})
        offset += size
    return {"schema": SCHEMA, "order": "NFC UTF-8 byte order",
            "fileCount": len(entries), "totalBytes": offset,
            "layoutSha256": digest.hexdigest(), "files": entries}


def read_at(tree: Path, directory: dict, offset: int, length: int) -> bytes:
    """Bounded positional virtual-concatenation read; never materializes the tree."""
    total = directory["totalBytes"]
    require(isinstance(offset, int) and isinstance(length, int)
            and offset >= 0 and 0 <= length <= MAX_READ
            and offset <= total and length <= total - offset, "invalid virtual read")
    if not length:
        return b""
    entries = directory["files"]
    ends = [item["offset"] + item["length"] for item in entries]
    result = bytearray()
    position = offset
    while len(result) < length:
        index = bisect.bisect_right(ends, position)
        require(index < len(entries), "virtual read truncated")
        entry = entries[index]
        require(entry["offset"] <= position < entry["offset"] + entry["length"],
                "virtual read has a gap")
        take = min(length - len(result),
                   entry["offset"] + entry["length"] - position)
        # A layout loaded from disk is not automatically trustworthy: recheck
        # the path and every ancestor before opening a positional source.
        name = canonical_path(entry["path"])
        require(tree.is_dir() and not tree.is_symlink(), "invalid virtual tree root")
        parent = tree
        for part in name.split("/")[:-1]:
            parent = parent / part
            require(stat.S_ISDIR(parent.lstat().st_mode),
                    "symlink/special virtual source directory")
        path = parent / name.split("/")[-1]
        require(stat.S_ISREG(path.lstat().st_mode), "virtual source not regular")
        with path.open("rb") as source:
            source.seek(position - entry["offset"])
            data = source.read(take)
        require(len(data) == take, "short virtual source read")
        result.extend(data)
        position += take
    return bytes(result)


def _map(directory: dict) -> dict[str, dict]:
    return {item["path"]: item for item in directory["files"]}


def verify_pair(pair: dict, base: dict, target: dict) -> None:
    """Independently reproduce the frozen changed/added/removed classification."""
    old, new = _map(base), _map(target)
    changed = []
    unchanged = []
    for path in sorted(old.keys() & new.keys()):
        a, b = old[path], new[path]
        if a["sha256"] == b["sha256"]:
            unchanged.append(b)
        else:
            changed.append({"path": path, "baseSize": a["length"],
                            "baseSha256": a["sha256"], "targetSize": b["length"],
                            "targetSha256": b["sha256"]})
    added = [{"path": p, "size": new[p]["length"], "sha256": new[p]["sha256"]}
             for p in sorted(new.keys() - old.keys())]
    require(changed == pair["changed"] and added == pair["added"]
            and sorted(old.keys() - new.keys()) == pair["removed"]
            and len(unchanged) == pair["identicalFiles"]
            and sum(f["length"] for f in unchanged) == pair["identicalBytes"],
            "frozen pair classification differs from exact tree content")


def inventory(root: Path, expected_pairs_sha: str) -> dict:
    pair_path = root / "pairs.json"
    require(pair_path.is_file() and sha256(pair_path.read_bytes()) == expected_pairs_sha,
            "frozen pairs.json SHA-256 mismatch")
    pairs = json.loads(pair_path.read_text(encoding="utf-8"))
    require(pairs.get("schema") == "chunkshift.patch-pairs.v1", "pairs schema")
    families = json.loads(CORPUS.read_text(encoding="utf-8"))["families"]
    roles = {family["id"]: family["split"] for family in families}
    cache: dict[tuple[str, str], dict] = {}
    result = []
    for pair in pairs["pairs"]:
        family, base, target = (label(pair[key]) for key in ("family", "base", "target"))
        require(family in roles and base != target, "unknown family or degenerate pair")
        for version in (base, target):
            key = family, version
            if key not in cache:
                cache[key] = layout(root / "tree" / family / version)
        verify_pair(pair, cache[family, base], cache[family, target])
        result.append({"family": family, "baseVersion": base,
                       "targetVersion": target, "datasetRole": roles[family],
                       "base": cache[family, base], "target": cache[family, target]})
    return {"schema": "chunkshift.patch-tree-inventory.v1",
            "pairsSha256": expected_pairs_sha,
            "status": "INVENTORY_ONLY_NOT_PATCH_EVIDENCE",
            "pairCount": len(result), "pairs": result}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    expected = json.loads(LOCK.read_text(encoding="utf-8"))["pairsSha256"]
    document = inventory(args.root, expected)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2, sort_keys=True,
                                     ensure_ascii=False) + "\n", encoding="utf-8")
    print("PATCH-TREE-001 inventory PASS; no CSP results measured")


if __name__ == "__main__":
    main()
