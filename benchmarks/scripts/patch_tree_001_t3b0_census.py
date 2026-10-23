#!/usr/bin/env python3
"""PATCH-TREE-001 T3-B0: exact cross-path census on locked calibration only.

Measures no new CSPs; the known original T0 warm total is sourced from the
committed independent F1 ledger and never rebranded as a new physical run.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import patch_tree_001_inventory as inventory
import patch_tree_001_t3_exact as t3

HERE = Path(__file__).resolve().parents[2]
LOCK = HERE / "docs/benchmarks/patch-corpus/corpus-lock.json"
CORPUS = HERE / "docs/benchmarks/patch-corpus/patch-corpus.json"
F1 = HERE / "benchmarks/experiments/patch-tree-001-f1-original-evidence-20261016.json"
SCHEMA = "chunkshift.patch-tree-t3b0-exact-calibration.v1"
STATUS = "CALIBRATION_EXACT_CENSUS_MODEL_ONLY_NO_ADOPT"
EXPECTED_FAMILIES = ("dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64")
EXPECTED_VERSIONS = ("10.0.10", "10.0.11", "10.0.12")
EXPECTED_PAIRS = tuple(
    (f, a, b) for f in EXPECTED_FAMILIES
    for a, b in zip(EXPECTED_VERSIONS, EXPECTED_VERSIONS[1:])
)
EXPECTED_PAIRS_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
EXPECTED_F1_RUN = 38773779715
EXPECTED_T0_RUN = 38142160679


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError("PATCH-TREE-001/T3B0: " + message)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def input_lock(root: Path, inv: dict, raw_pairs: bytes, f1: dict) -> None:
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    require(lock["pairsSha256"] == EXPECTED_PAIRS_SHA256, "frozen source lock drift")
    require(hashlib.sha256(raw_pairs).hexdigest() == EXPECTED_PAIRS_SHA256,
            "original pairs bytes hash mismatch")
    require(sha256(CORPUS) == lock["manifestSha256"], "corpus manifest SHA-256 mismatch")
    require(sha256(HERE / "docs/benchmarks/patch-corpus/source-assets.sha256") ==
            lock["sourceAssetsSha256"], "source asset checksum list drift")
    expected = [(p["family"], p["base"], p["target"])
                for p in json.loads(raw_pairs)["pairs"] if p["family"] in EXPECTED_FAMILIES]
    actual = [(p["family"], p["baseVersion"], p["targetVersion"])
              for p in inv.get("pairs", [])]
    require(expected == list(EXPECTED_PAIRS) == actual, "calibration pairs or order differ")
    require(inv.get("schema") == "chunkshift.patch-tree-inventory.v1" and
            inv.get("status") == "INVENTORY_ONLY_NOT_PATCH_EVIDENCE" and
            inv.get("pairCount") == 4 and inv.get("pairsSha256") == EXPECTED_PAIRS_SHA256,
            "locked inventory envelope")
    require(all(p["datasetRole"] == "calibration" for p in inv["pairs"]),
            "sealed holdout appeared in inventory")
    require(f1["schema"] == "chunkshift.patch-tree-f1-original-evidence.v1" and
            f1["f1"]["runId"] == EXPECTED_F1_RUN and
            f1["priorOriginalPhysical"]["runId"] == EXPECTED_T0_RUN and
            f1["totals"]["t0WarmBytes"] == 14_758_982 and
            f1["totals"]["commonTreeMetadataBytes"] == 185_436 and
            f1["totals"]["t0RawAddedControlBytes"] == 2_713_272,
            "original physical control/metadata ledger drift")
    for p, original in zip(inv["pairs"], (x for x in json.loads(raw_pairs)["pairs"]
                                          if x["family"] in EXPECTED_FAMILIES)):
        inventory.verify_pair(original, p["base"], p["target"])
        # Source inventory is generated after checksummed original materialization.
        # This second independent walk checks that the bytes still match it.
        for side, version in (("base", p["baseVersion"]), ("target", p["targetVersion"])):
            tree = root / "tree" / p["family"] / version
            reread = inventory.layout(tree)
            require(reread == p[side], "actual materialized " + side + " content drift")



def scan_pair(item: dict, frozen: dict) -> dict:
    base = item["base"]
    target = item["target"]
    plan, map_bytes = t3.plan(base, target)
    added = [p for p in target["files"]
             if p["path"] not in {x["path"] for x in base["files"]}]
    old_index: dict[tuple[int, str], list[str]] = {}
    for p in base["files"]:
        old_index.setdefault((p["length"], p["sha256"]), []).append(p["path"])
    still = {p["path"] for p in target["files"]}
    candidates = [p for p in added if old_index.get((p["length"], p["sha256"]))]
    strict = sum(1 for p in candidates if any(x not in still for x in
                 old_index[(p["length"], p["sha256"])]))
    require(plan["addedCount"] == len(frozen["added"]) ==
            len(added), "added path count mismatch")
    require(plan["t0AddedRawBytes"] == sum(e["size"] for e in frozen["added"]),
            "original added raw size mismatch")
    # Inspect real source content for every selected match, not only
    # for metadata candidates (the inventory itself is rehashed above).
    old_by_path = {x["path"]: x for x in base["files"]}
    for m in plan["matches"]:
        src = old_by_path[m["sourcePath"]]
        require(src["length"] == m["length"] and src["sha256"] == m["sha256"],
                "selected old source mismatch")
    row = dict(
        family=item["family"], baseVersion=item["baseVersion"],
        targetVersion=item["targetVersion"],
        baseLayoutSha256=base["layoutSha256"],
        targetLayoutSha256=target["layoutSha256"],
        changedCount=len(frozen["changed"]), removedCount=len(frozen["removed"]),
        unchangedCount=frozen["identicalFiles"], addedCount=len(added),
        exactCandidateCount=len(candidates), strictMoveCount=strict,
        copyOrAmbiguousCount=len(candidates) - strict,
        mappedCount=len(plan["matches"]),
        addedRawBytes=plan["t0AddedRawBytes"],
        mappedContentBytes=plan["t0AddedRawBytes"] - plan["t3AddedRawBytes"],
        mapPhysicalBytes=len(map_bytes),
        netSavedBytes=plan["addedLaneNetSavingsBytes"],
        referenceSha256=plan["referenceSha256"],
        matches=plan["matches"])
    require(row["netSavedBytes"] == row["mappedContentBytes"] -
            row["mapPhysicalBytes"], "physical reference accounting")
    return row

def census(root: Path, inv: dict, pairs_bytes: bytes, f1: dict) -> dict:
    input_lock(root, inv, pairs_bytes, f1)
    original = [x for x in json.loads(pairs_bytes)["pairs"]
                if x["family"] in EXPECTED_FAMILIES]
    rows = []
    totals = {k: 0 for k in ("addedCount", "removedCount", "changedCount",
                            "unchangedCount", "exactCandidateCount", "strictMoveCount",
                            "copyOrAmbiguousCount", "mappedCount", "addedRawBytes",
                            "mappedContentBytes", "mapPhysicalBytes", "netSavedBytes")}
    for item, frozen in zip(inv["pairs"], original):
        row = scan_pair(item, frozen)
        for key in totals:
            totals[key] += row[key]
        rows.append(row)
    require(totals["addedCount"] == 2 and totals["changedCount"] == 1049,
            "frozen original calibration population mismatch")
    require(totals["addedRawBytes"] == f1["totals"]["t0RawAddedControlBytes"],
            "original T0 added raw total mismatch")
    require(totals["netSavedBytes"] >= 0 and
            totals["netSavedBytes"] <= totals["addedRawBytes"], "invalid exact savings")
    baseline = f1["totals"]["t0WarmBytes"]
    return {
        "schema": SCHEMA, "status": STATUS,
        "originalPairsSha256": EXPECTED_PAIRS_SHA256,
        "originalF1RunId": EXPECTED_F1_RUN, "originalT0RunId": EXPECTED_T0_RUN,
        "population": "original 4 dotnet calibration transitions only",
        "model": "T0 warm original physical ledger minus T3A exact added-path differential; NOT a new measured CSP/update-set",
        "baselineT0WarmPhysicalBytes": baseline,
        "commonTreeMetadataBytesUnchanged": f1["totals"]["commonTreeMetadataBytes"],
        "modelledT3WarmLowerBoundBytes": baseline - totals["netSavedBytes"],
        "modelledWarmSavingsBasisPoints": totals["netSavedBytes"] * 10000 // baseline,
        "totals": totals, "pairs": rows,
        "limitations": [
            "Only full exact added-path references, not changed-and-renamed files",
            "No new CSP, performance, installed tree/publication, holdout, or security experiment",
            "T0 warm baseline is inherited from prior source-locked original physical run",
            "Research binary map is not a shipping artifact; no remote/base-index cost is assumed",
            "Complete target-tree manifest bytes unchanged; no production tree manifest qualified",
        ],
    }


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--root", type=Path, required=True)
    p.add_argument("--inventory", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    raw = (args.root / "pairs.json").read_bytes()
    doc = census(args.root, json.loads(args.inventory.read_text(encoding="utf-8")),
                 raw, json.loads(F1.read_text(encoding="utf-8")))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(doc, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("T3B0 CALIBRATION_ONLY; net exact added-path savings =",
          doc["totals"]["netSavedBytes"], "bytes; no T2/T3 or production ADOPT.")


if __name__ == "__main__":
    main()
