#!/usr/bin/env python3
"""PATCH-TREE-001 T3-A: bounded exact-content cross-path reference oracle.

Only lab inventory/diagnostics: no shipping tree map, CSP changes or publisher.
One path's content digest must be independently checked against actual bytes
before a mapped source can be used, not merely trusted from the inventory.
"""
from __future__ import annotations

import hashlib
import json
import stat
import struct
from pathlib import Path
from typing import Any

import patch_tree_001_inventory as inventory

SCHEMA = "chunkshift.patch-tree-t3a-exact.v1"
STATUS = "LAB_ONLY_EXACT_MAPPING_NO_PRODUCT_OR_HOLDOUT_VERDICT"
MAX_ENTRIES = 100_000
MAX_PATH_BYTES = 4096
MAX_SIDECAR_BYTES = 16 * 1024 * 1024
MAGIC = b"PT3A\x00"  # research cost ledger, NOT a proposed wire format


def fail(reason: str) -> None:
    raise ValueError("PATCH-TREE-001/T3A: " + reason)


def _integer(value: Any, name: str) -> int:
    if type(value) is not int or value < 0 or value > inventory.MAX_OFFSET:
        fail("invalid " + name)
    return value


def _digest(value: Any) -> str:
    if not isinstance(value, str) or len(value) != 64 or any(c not in "0123456789abcdef" for c in value):
        fail("noncanonical SHA-256")
    return value


def validate_layout(data: dict) -> list[dict]:
    """Independently verify inventory's canonical layout identity and bounds."""
    if not isinstance(data, dict) or data.get("schema") != inventory.SCHEMA or data.get("order") != "NFC UTF-8 byte order":
        fail("unsupported inventory layout")
    files = data.get("files")
    if not isinstance(files, list) or len(files) > MAX_ENTRIES or _integer(data.get("fileCount"), "file count") != len(files):
        fail("file count mismatch")
    total = _integer(data.get("totalBytes"), "total size")
    previous = None
    seen_folded = set()
    offset = 0
    h = hashlib.sha256()
    for entry in files:
        if not isinstance(entry, dict):
            fail("invalid file entry")
        name = inventory.canonical_path(entry.get("path"))
        encoded = name.encode("utf-8")
        if len(encoded) > MAX_PATH_BYTES or (previous is not None and previous >= encoded):
            fail("noncanonical path ordering or length")
        folded = name.casefold()
        if folded in seen_folded:
            fail("casefold collision")
        seen_folded.add(folded)
        length = _integer(entry.get("length"), "file length")
        if _integer(entry.get("offset"), "file offset") != offset or length > inventory.MAX_OFFSET - offset:
            fail("noncontiguous or overflowing virtual offsets")
        hash_hex = _digest(entry.get("sha256"))
        h.update(struct.pack("<I", len(encoded)))
        h.update(encoded)
        h.update(struct.pack("<Q", length))
        h.update(bytes.fromhex(hash_hex))
        offset += length
        previous = encoded
    if offset != total or h.hexdigest() != data.get("layoutSha256"):
        fail("layout identity mismatch")
    return files


def _record(target: dict, source: dict) -> bytes:
    dst = target["path"].encode("utf-8")
    src = source["path"].encode("utf-8")
    return (struct.pack("<I", len(dst)) + dst + struct.pack("<I", len(src)) + src
            + struct.pack("<Q", target["length"]) + bytes.fromhex(target["sha256"]))


def plan(base: dict, target: dict) -> tuple[dict, bytes]:
    """Emit deterministic, physically costed diagnostic references for added paths.

    Same-path changed/unchanged files are never reinterpreted as T3 mappings.
    Only full exact (length,SHA-256) matches across different paths qualify;
    filename resemblance and heuristics are intentionally excluded.
    """
    old_files = validate_layout(base)
    new_files = validate_layout(target)
    old_by_path = {e["path"]: e for e in old_files}
    pool: dict[tuple[int, str], list[dict]] = {}
    for entry in old_files:
        pool.setdefault((entry["length"], entry["sha256"]), []).append(entry)
    for candidates in pool.values():
        candidates.sort(key=lambda e: (len(e["path"].encode("utf-8")), e["path"].encode("utf-8")))

    added = [e for e in new_files if e["path"] not in old_by_path]
    raw_before = sum(e["length"] for e in added)
    chosen: list[tuple[dict, dict, bytes]] = []
    cumulative_size = len(MAGIC) + 4
    for entry in added:
        candidates = pool.get((entry["length"], entry["sha256"]), [])
        # Candidate can also be an old file retained at another new path;
        # this is exact cross-path reuse, not necessarily a physical rename.
        if not candidates:
            continue
        old = candidates[0]
        binary = _record(entry, old)
        if entry["length"] > len(binary):
            cumulative_size += len(binary)
            if cumulative_size > MAX_SIDECAR_BYTES:
                fail("exact reference ledger exceeds fixed cap")
            chosen.append((entry, old, binary))
    sidecar = b""
    if chosen:
        candidate = MAGIC + struct.pack("<I", len(chosen)) + b"".join(row[2] for row in chosen)
        if len(candidate) > MAX_SIDECAR_BYTES:
            fail("exact reference ledger exceeds fixed cap")
        # Entire sidecar must beat raw bytes, including its fixed header.
        if sum(row[0]["length"] for row in chosen) > len(candidate):
            sidecar = candidate
        else:
            chosen.clear()

    matches = [dict(targetPath=new["path"], sourcePath=old["path"],
                    length=new["length"], sha256=new["sha256"])
               for new, old, _ in chosen]
    saved_raw = sum(m["length"] for m in matches)
    after_raw = raw_before - saved_raw
    summary = {
        "schema": SCHEMA, "status": STATUS,
        "baseLayoutSha256": base["layoutSha256"],
        "targetLayoutSha256": target["layoutSha256"],
        "referenceSha256": hashlib.sha256(sidecar).hexdigest(),
        "addedCount": len(added), "exactMappedCount": len(matches),
        "t0AddedRawBytes": raw_before, "t3AddedRawBytes": after_raw,
        "t3ReferenceBytes": len(sidecar),
        "addedLaneNetSavingsBytes": saved_raw - len(sidecar),
        "matches": matches,
    }
    if summary["addedLaneNetSavingsBytes"] < 0 or after_raw < 0:
        raise AssertionError("internal physical ledger underflow")
    return summary, sidecar


def _verified_copy(root: Path, relative: str, expected_length: int,
                   expected_sha: str, dest: Path) -> None:
    """Bounded streaming fixture copy with independent source-byte SHA-256."""
    parts = inventory.canonical_path(relative).split("/")
    if not root.is_dir() or root.is_symlink():
        fail("untrusted fixture root")
    parent = root
    for part in parts[:-1]:
        parent = parent / part
        if not stat.S_ISDIR(parent.lstat().st_mode):
            fail("symlink/non-directory fixture parent")
    path = parent / parts[-1]
    if not stat.S_ISREG(path.lstat().st_mode):
        fail("symlink/nonregular fixture file")
    before = path.lstat()
    h = hashlib.sha256()
    copied = 0
    with path.open("rb") as src, dest.open("xb") as dst:
        for block in iter(lambda: src.read(inventory.BLOCK), b""):
            dst.write(block)
            h.update(block)
            copied += len(block)
            if copied > expected_length:
                fail("source longer than inventory length")
    after = path.lstat()
    if (copied != expected_length or h.hexdigest() != expected_sha or
        (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) !=
        (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns)):
        fail("source bytes or metadata changed since inventory")


def reconstruct_fixture(old_root: Path, new_root: Path, stage_root: Path,
                        old_layout: dict, new_layout: dict, summary: dict) -> None:
    """Synthetic complete-target oracle: CSP-controlled paths are copied raw.

    This proves only exact reference semantics; changed same-path files are
    intentionally supplied as raw fixture controls, NOT charged as patch bytes.
    The path/namespace is a trusted disposable fixture, not a safe publisher.
    """
    validate_layout(old_layout)
    target_files = validate_layout(new_layout)
    expected_summary, _ = plan(old_layout, new_layout)
    if summary != expected_summary:
        fail("mapping differs from canonical recomputation")
    if stage_root.exists():
        fail("staging target must be newly created")
    by_target = {m["targetPath"]: m for m in summary["matches"]}
    stage_root.mkdir(parents=True)
    for e in target_files:
        m = by_target.get(e["path"])
        out = stage_root.joinpath(*e["path"].split("/"))
        out.parent.mkdir(parents=True, exist_ok=True)
        _verified_copy(old_root if m else new_root,
                       m["sourcePath"] if m else e["path"],
                       e["length"], e["sha256"], out)
    # Full-tree byte oracle, not merely a positive flag from the mapper.
    if inventory.layout(stage_root) != new_layout:
        fail("complete staged fixture differs from target layout")


def main() -> None:
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--old-tree", type=Path, required=True)
    parser.add_argument("--new-tree", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--fixture-only", action="store_true", required=True,
                        help="Acknowledge synthetic, explicitly trusted lab fixtures")
    args = parser.parse_args()
    if not args.fixture_only:
        fail("fixture-only is mandatory")
    base, target = inventory.layout(args.old_tree), inventory.layout(args.new_tree)
    result, sidecar = plan(base, target)
    # The input inventory is derived from actual bytes here, not supplied by untrusted JSON.
    # This does not constitute a stable snapshot against concurrent mutations.
    result["note"] = "Diagnostic added-path delta only; changed-file CSP and common tree metadata not measured"
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    # Do not persist the diagnostic binary map as an accidentally public format.
    assert result["t3ReferenceBytes"] == len(sidecar)


if __name__ == "__main__":
    main()
