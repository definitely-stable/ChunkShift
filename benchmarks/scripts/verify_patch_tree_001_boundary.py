#!/usr/bin/env python3
"""Independent structural/accounting verifier for PATCH-TREE-001 boundary attribution.

Input is real calibration-only result plus original immutable pairs and exact
inventory. Logical ChunkIds are NOT exported: this verifier authenticates
the population and independently recomputes all reported envelope bounds and
length/overlap equations, not exact replay of producer's chunking algorithm.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

LOCK = Path(__file__).resolve().parents[2] / "docs/benchmarks/patch-corpus/corpus-lock.json"
PAIRS_SHA = json.loads(LOCK.read_text(encoding="utf-8"))["pairsSha256"]
CAL = {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64"}
HEX = re.compile(r"[a-f0-9]{64}\Z")
SCHEMA = "chunkshift.patch-tree-t1-boundary-attribution.v1"
STATUS = "CALIBRATION_DIAGNOSTIC_NOT_DECISION"
FIELDS = (
    "standaloneOld", "oldPreservedSameOffset",
    "oldAvailableAnywhereInVirtualTree", "virtualContainedWithinFile",
    "virtualCrossBoundaryOverlap", "targetChunks", "targetExactInSamePathOld",
    "targetExactInVirtualTree",
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError("PATCH-TREE-001 boundary verifier: " + message)


def uint(value: object, name: str) -> int:
    require(type(value) is int and value >= 0, "invalid unsigned integer: " + name)
    return value


def strict_json(raw: bytes) -> dict:
    def no_duplicates(items: list[tuple[str, object]]) -> dict:
        result = {}
        for key, value in items:
            require(key not in result, "duplicate JSON key: " + key)
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=no_duplicates)


def ident(item: dict) -> tuple[str, str, str]:
    return item["family"], item["baseVersion"], item["targetVersion"]


def frozen_ident(item: dict) -> tuple[str, str, str]:
    return item["family"], item["base"], item["target"]


def check_layout(layout: dict) -> dict[str, dict]:
    require(layout["schema"] == "chunkshift.patch-tree-layout.v1" and
            layout["order"] == "NFC UTF-8 byte order",
            "layout schema/order")
    require(uint(layout["fileCount"], "fileCount") == len(layout["files"]),
            "layout count")
    require(HEX.fullmatch(layout["layoutSha256"]) is not None, "layout sha")
    sha = hashlib.sha256()
    position = 0
    result: dict[str, dict] = {}
    last = None
    for f in layout["files"]:
        path = f["path"]
        raw = path.encode("utf-8")
        require(last is None or last < raw, "noncanonical layout path order")
        last = raw
        require(path not in result, "duplicate layout path")
        offset = uint(f["offset"], "file offset")
        length = uint(f["length"], "file length")
        require(offset == position, "noncontiguous layout")
        require(HEX.fullmatch(f["sha256"]) is not None, "file sha")
        position += length
        require(position < (1 << 63), "virtual source exceeds Int64")
        sha.update(len(raw).to_bytes(4, "little"))
        sha.update(raw)
        sha.update(length.to_bytes(8, "little"))
        sha.update(bytes.fromhex(f["sha256"]))
        result[path] = f
    require(uint(layout["totalBytes"], "totalBytes") == position and
            layout["layoutSha256"] == sha.hexdigest(), "independent layout digest")
    return result


def verify(doc: dict, original: dict, inventory: dict) -> None:
    require(doc["schema"] == SCHEMA and doc["status"] == STATUS and
            doc["pairsSha256"] == PAIRS_SHA, "result provenance/schema")
    require(original["schema"] == "chunkshift.patch-pairs.v1", "source pair schema")
    require(inventory["schema"] == "chunkshift.patch-tree-inventory.v1" and
            inventory["status"] == "INVENTORY_ONLY_NOT_PATCH_EVIDENCE" and
            inventory["pairsSha256"] == PAIRS_SHA,
            "inventory provenance/schema")
    frozen = [p for p in original["pairs"] if p["family"] in CAL]
    require(len(frozen) > 0 and len(frozen) == len(doc["pairs"]) ==
            len(inventory["pairs"]) == uint(inventory["pairCount"], "pairCount"),
            "complete calibration population")
    for actual, source, inv in zip(doc["pairs"], frozen, inventory["pairs"]):
        expected_identity = frozen_ident(source)
        require(ident(actual) == ident(inv) == expected_identity,
                "frozen calibration pair identity and order")
        require(inv["datasetRole"] == "calibration", "held-out inventory")
        require(actual["family"] in CAL, "held-out result")
        require(HEX.fullmatch(actual["oldTreeManifestId"]) is not None,
                "invalid actual CSM identity")
        base = check_layout(inv["base"])
        target = check_layout(inv["target"])
        old_bytes = uint(actual["oldTreeBytes"], "oldTreeBytes")
        old_chunks = uint(actual["oldTreeChunks"], "oldTreeChunks")
        require(old_bytes == inv["base"]["totalBytes"], "old tree length")
        require((old_bytes == 0) == (old_chunks == 0), "empty tree chunk count")
        require(old_chunks <= 4_194_304, "base record cap")
        changed = {f["path"]: f for f in source["changed"]}
        added = {f["path"]: f for f in source["added"]}
        removed = set(source["removed"])
        require(len(changed) == len(source["changed"]) and
                len(added) == len(source["added"]) and
                len(removed) == len(source["removed"]), "duplicate frozen role")
        require(set(base) - set(target) == removed and
                set(target) - set(base) == set(added) and
                {x for x in set(base) & set(target)
                 if base[x]["sha256"] != target[x]["sha256"]} == set(changed),
                "original pair classification")
        for path, frozen_file in changed.items():
            require(base[path]["length"] == frozen_file["baseSize"] and
                    base[path]["sha256"] == frozen_file["baseSha256"] and
                    target[path]["length"] == frozen_file["targetSize"] and
                    target[path]["sha256"] == frozen_file["targetSha256"],
                    "changed exact file identity")
        for path, frozen_file in added.items():
            require(target[path]["length"] == frozen_file["size"] and
                    target[path]["sha256"] == frozen_file["sha256"],
                    "added exact file identity")
        unchanged = set(base) & set(target) - set(changed)
        require(len(unchanged) == source["identicalFiles"] and
                sum(base[x]["length"] for x in unchanged) == source["identicalBytes"],
                "unchanged file identity")

        rows = actual["files"]
        require(len(rows) == len(base) + len(added), "incomplete file population")
        paths = [row["path"] for row in rows]
        require(paths == sorted(paths, key=lambda x: x.encode("utf-8")) and
                len(paths) == len(set(paths)) and set(paths) == set(base) | set(added),
                "missing/reordered/duplicate file rows")
        for row in rows:
            path = row["path"]
            old = base.get(path)
            new = target.get(path)
            kind = ("added" if path in added else
                    "changed" if path in changed else
                    "unchanged" if path in unchanged else "removed")
            require(row["kind"] == kind, "row role")
            old_length = old["length"] if old else 0
            target_length = new["length"] if new and kind in ("added", "changed") else 0
            require(uint(row["oldFileBytes"], "oldFileBytes") == old_length and
                    uint(row["targetBytes"], "targetBytes") == target_length,
                    "row lengths")
            metrics = {}
            for field in FIELDS:
                metric = row[field]
                metrics[field] = (uint(metric["count"], field + " count"),
                                  uint(metric["bytes"], field + " bytes"))
                require(metric["count"] <= old_chunks + len(base) + len(added) +
                        4_194_304, "implausible metric count")
            count = lambda x: metrics[x][0]
            size = lambda x: metrics[x][1]
            require(size("standaloneOld") == old_length and
                    (old_length == 0) == (count("standaloneOld") == 0),
                    "standalone old coverage")
            require(count("oldPreservedSameOffset") <= count("oldAvailableAnywhereInVirtualTree") <= count("standaloneOld") and
                    size("oldPreservedSameOffset") <= size("oldAvailableAnywhereInVirtualTree") <= old_length,
                    "old identity containment")
            require(size("virtualContainedWithinFile") +
                    size("virtualCrossBoundaryOverlap") == old_length,
                    "virtual source exact byte covering")
            require(count("virtualCrossBoundaryOverlap") <= 2,
                    "crossing old-file boundary count")
            require(size("targetChunks") == target_length and
                    (target_length == 0) == (count("targetChunks") == 0),
                    "target complete coverage")
            for field in ("targetExactInSamePathOld", "targetExactInVirtualTree"):
                require(count(field) <= count("targetChunks") and
                        size(field) <= target_length, "target reuse upper bound")
            if kind in ("added", "removed", "unchanged"):
                require(metrics["targetExactInSamePathOld"] == (0, 0) and
                        (kind not in ("removed", "unchanged") or metrics["targetChunks"] == (0, 0)),
                        "non-changed same-path target reuse")
            for field in FIELDS:
                require(count(field) == 0 if size(field) == 0 else
                        count(field) > 0, "zero byte/count metric mismatch: " + field)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--result", required=True, type=Path)
    parser.add_argument("--pairs", required=True, type=Path)
    parser.add_argument("--inventory", required=True, type=Path)
    args = parser.parse_args()
    data = args.pairs.read_bytes()
    require(hashlib.sha256(data).hexdigest() == PAIRS_SHA,
            "frozen original pairs SHA256")
    verify(strict_json(args.result.read_bytes()), strict_json(data),
           strict_json(args.inventory.read_bytes()))
    print("PATCH-TREE-001 boundary attribution STRUCTURAL PASS / CALIBRATION ONLY / NOT DECISION")


if __name__ == "__main__":
    main()
