#!/usr/bin/env python3
"""PATCH-TREE-001 T3-B2A: frozen structural rename candidate policy.

Research-only. Only deleted sources vs added targets; never scan unchanged
files, rank resemblance, probe chunks or modify shipping CSP. Selection is
NOT authorization and produces no patch or persisted update-set map.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
from collections import Counter, defaultdict
from pathlib import Path

import patch_tree_001_inventory as inv
import patch_tree_001_t3_exact as t3
import patch_tree_001_t3b0_census as b0

SCHEMA = "chunkshift.patch-tree-t3b2a-structural-candidates.v1"
STATUS = "LAB_ONLY_STRUCTURAL_PRESELECTION_NO_PATCH_OR_ADOPT"
MAX_FILES = 100_000
MAX_PATH_BYTES = 4_096
MIN_BYTES = 65_536
MAX_BYTES = 64 * 1024 * 1024
MAX_SOURCE_POOL = 8
# Strict ASCII filename grammar, with a required separating underscore.
# Prefix is interpreted as opaque data; extension is case-sensitive.
FILENAME = re.compile(
    r"(?P<stem>[A-Za-z0-9][A-Za-z0-9_.+-]{0,127}_)"
    r"(?P<version>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\.dll\Z",
    re.ASCII,
)
MAX_VERSION_PART = 2_147_483_647


def require(ok: bool, why: str) -> None:
    if not ok:
        raise ValueError("PATCH-TREE-001/T3B2A: " + why)


def signature(relative: str) -> tuple[str, str, tuple[int, ...]] | None:
    inv.canonical_path(relative)
    parent, _, name = relative.rpartition("/")
    match = FILENAME.fullmatch(name)
    if match is None:
        return None
    parts = tuple(int(part) for part in match["version"].split("."))
    if any(part > MAX_VERSION_PART for part in parts):
        return None
    return parent, match["stem"], parts


def locator_floor(src_path: str, dst_path: str) -> int:
    """Exact *B1 diagnostic record* length, assuming a 32-byte BASE ManifestId.

    Cost of obtaining/authenticating/pinning source and CSM is not included.
    """
    return 5 + 4 + 4 + len(dst_path.encode("utf-8")) + 4 + len(src_path.encode("utf-8")) + 16 + 32 * 3


def select_pair(base: dict, target: dict, frozen: dict) -> dict:
    """Pure audited inventory plan.  All ambiguous clusters fall back.

    This is intentionally a low-recall structural policy, not a similarity
    oracle. Only added target paths and *removed* old paths may participate.
    """
    old = t3.validate_layout(base)
    new = t3.validate_layout(target)
    require(len(old) <= MAX_FILES and len(new) <= MAX_FILES, "file cap")
    inv.verify_pair(frozen, base, target)
    old_map = {e["path"]: e for e in old}
    new_map = {e["path"]: e for e in new}
    removed = sorted(frozen["removed"], key=lambda x: x.encode("utf-8"))
    added = sorted((x["path"] for x in frozen["added"]), key=lambda x: x.encode("utf-8"))
    source_index: dict[tuple[str, str], list[tuple[str, tuple[int, ...]]]] = defaultdict(list)
    for name in removed:
        s = signature(name)
        file = old_map[name]
        if s is not None and MIN_BYTES <= file["length"] <= MAX_BYTES:
            source_index[(s[0], s[1])].append((name, s[2]))
    # Input is already in canonical path order. Never sort a potentially huge
    # bucket and never perform an unbounded per-target candidate scan.
    possible: dict[str, str] = {}
    fallback: dict[str, str] = {}
    candidates_by_source: dict[str, list[str]] = defaultdict(list)
    for dst in added:
        t = signature(dst)
        f = new_map[dst]
        if t is None:
            fallback[dst] = "nonversioned_filename"
            continue
        if f["length"] < MIN_BYTES or f["length"] > MAX_BYTES:
            fallback[dst] = "target_size_out_of_bounds"
            continue
        pool = source_index.get((t[0], t[1]), ())
        if len(pool) > MAX_SOURCE_POOL:
            fallback[dst] = "source_pool_limit"
            continue
        matches = []
        for src, version in pool:
            prior = old_map[src]
            if version >= t[2] or prior["sha256"] == f["sha256"]:
                continue
            # Bounded size proxy only; this is NOT a similarity score.
            if max(prior["length"], f["length"]) > 2 * min(prior["length"], f["length"]):
                continue
            matches.append(src)
        if len(matches) != 1:
            fallback[dst] = "no_unique_structural_source" if not matches else "ambiguous_source_candidates"
            continue
        possible[dst] = matches[0]
        candidates_by_source[matches[0]].append(dst)

    # A single old file cannot be silently consumed as several independent
    # rename sources. Do not pick a convenient target in the collision case.
    for source, destinations in candidates_by_source.items():
        if len(destinations) > 1:
            for dst in destinations:
                possible.pop(dst, None)
                fallback[dst] = "source_claimed_by_multiple_targets"

    rows = []
    for dst in added:
        src = possible.get(dst)
        if src is None:
            continue
        a, z = old_map[src], new_map[dst]
        rows.append(dict(
            sourcePath=src, targetPath=dst,
            sourceSha256=a["sha256"], targetSha256=z["sha256"],
            sourceLength=a["length"], targetLength=z["length"],
            sourceVersion=list(signature(src)[2]), targetVersion=list(signature(dst)[2]),
            diagnosticLocatorFloorBytes=locator_floor(src, dst),
            patchStatus="NOT_ATTEMPTED__REQUIRES_REAL_CSP_AND_COST_ADMISSION",
        ))
    require(len(rows) + len(fallback) == len(added), "target partition")
    require(len(set(x["sourcePath"] for x in rows)) == len(rows), "one-to-one invariant")
    return {
        "schema": SCHEMA, "status": STATUS, "baseLayoutSha256": base["layoutSha256"],
        "targetLayoutSha256": target["layoutSha256"],
        "removedCount": len(removed), "addedCount": len(added),
        "selectedCount": len(rows),
        "selectedRawTargetBytes": sum(x["targetLength"] for x in rows),
        "diagnosticLocatorFloorBytes": sum(x["diagnosticLocatorFloorBytes"] for x in rows),
        "fallBackCount": len(fallback),
        "fallbackReasons": dict(sorted(Counter(fallback.values()).items())),
        "fallbackTargets": [dict(targetPath=dst, reason=fallback[dst])
                            for dst in added if dst in fallback],
        "candidates": rows,
    }


def evaluate_original(root: Path, inventory: dict, original: bytes) -> dict:
    # Reuse source-lock and actual source byte rehash from accepted B0.
    f1 = json.loads(b0.F1.read_text(encoding="utf-8"))
    b0.input_lock(root, inventory, original, f1)
    fixed = [p for p in json.loads(original)["pairs"] if
             p["family"] in b0.EXPECTED_FAMILIES]
    require(len(fixed) == 4, "four frozen calibration transitions")
    rows = []
    for item, frozen in zip(inventory["pairs"], fixed):
        result = select_pair(item["base"], item["target"], frozen)
        rows.append(dict(family=frozen["family"],
                         baseVersion=frozen["base"], targetVersion=frozen["target"],
                         **result))
    return {
        "schema": "chunkshift.patch-tree-t3b2a-original-calibration.v1",
        "status": "ORIGINAL_CALIBRATION_CANDIDATE_PRESELECTION_ONLY_NO_ADOPT",
        "originalPairsSha256": b0.EXPECTED_PAIRS_SHA256,
        "decision": "NO_CSP_BUILT_NO_COST_OR_PRODUCTION_VERDICT",
        "totals": {k: sum(row[k] for row in rows) for k in
                   ("removedCount", "addedCount", "selectedCount", "selectedRawTargetBytes",
                    "diagnosticLocatorFloorBytes", "fallBackCount")},
        "pairs": rows,
    }


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--corpus", type=Path, required=True)
    p.add_argument("--inventory", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    data = evaluate_original(args.corpus,
        json.loads(args.inventory.read_text(encoding="utf-8")),
        (args.corpus / "pairs.json").read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, sort_keys=True, indent=2) + "\n",
                           encoding="utf-8")
    print("T3B2A selected", data["totals"]["selectedCount"],
          "source candidates from original locked calibration; no CSP/ADOPT.")


if __name__ == "__main__":
    main()
