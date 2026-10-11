#!/usr/bin/env python3
"""PATCH-DOTNET-001 calibration D0/D3 evidence adapter. Never touches holdout."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path

import patch_dotnet_001_d3_oracle as oracle

LAB_SCHEMA = "chunkshift.patch-lab.v1"
SCAN_SCHEMA = "chunkshift.patch-dotnet-d3-scanner-evidence.v1"
GITHUB_SHA_SIZE = 40


def dump(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, indent=2) + "\n").encode()


def prepare(root: Path) -> dict:
    pairs = oracle.load_locked_pairs(root / "v2-pairs.json")
    target = root / "pairs.json"
    oracle.demand(not target.exists(), "D0_PROJECTION_ALREADY_EXISTS")
    base = {"schema": "chunkshift.patch-pairs.v1", "pairs": [
        {"family": p["family"], "base": p["base"], "target": p["target"],
         "changed": [{k: f[k] for k in
                      ("path", "baseSize", "baseSha256", "targetSize", "targetSha256")}
                     for f in p["changed"]]}
        for p in pairs["pairs"]
    ]}
    target.write_bytes(dump(base))
    return {"schema": base["schema"], "projectedPairsSha256": oracle.hash_bytes(target.read_bytes()),
            "sourcePairsSha256": oracle.PAIRS_SHA, "changedFiles": 1242}


def combine(root: Path, lab_path: Path, scanner_path: Path, output: Path,
            commit: str, run_id: int) -> dict:
    oracle.demand(not output.exists(), "D0_COMBINATION_OVERWRITE")
    pairs = oracle.load_locked_pairs(root / "v2-pairs.json")
    projected_path = root / "pairs.json"
    projection = json.loads(projected_path.read_text())
    oracle.demand(projection["schema"] == "chunkshift.patch-pairs.v1" and
                  projection["pairs"] == [{k: p[k] for k in ("family", "base", "target", "changed")}
                  for p in projection["pairs"]], "D0_PROJECTION_SCHEMA")
    expected_projection = {"schema": "chunkshift.patch-pairs.v1", "pairs": [
        {"family": p["family"], "base": p["base"], "target": p["target"],
         "changed": [{k: f[k] for k in
                      ("path", "baseSize", "baseSha256", "targetSize", "targetSha256")}
                     for f in p["changed"]]}
        for p in pairs["pairs"]
    ]}
    oracle.demand(projection == expected_projection, "D0_PROJECTION_SOURCE_DRIFT")
    lab_data = lab_path.read_bytes()
    scan_data = scanner_path.read_bytes()
    lab, scan = json.loads(lab_data), json.loads(scan_data)
    oracle.demand(lab.get("schema") == LAB_SCHEMA and lab.get("lane") == "csp" and
                  lab.get("corpusPairsSha256") == oracle.hash_bytes(projected_path.read_bytes()) and
                  lab.get("applyRepeats") == 1 and lab.get("applyCheck") == "seq",
                  "D0_LAB_PROVENANCE_DRIFT")
    oracle.demand(lab.get("execution") is None and
                  isinstance(lab.get("files"), list), "D0_POLICY_DRIFT")
    oracle.demand(scan.get("schema") == SCAN_SCHEMA and
                  scan.get("sourcePairsSha256") == oracle.PAIRS_SHA and
                  scan.get("role") == "calibration" and
                  isinstance(scan.get("rows"), list), "D3_SCAN_PROVENANCE_DRIFT")
    if len(commit) != GITHUB_SHA_SIZE or any(c not in "0123456789abcdef" for c in commit) \
            or not isinstance(run_id, int) or run_id < 1:
        raise oracle.OracleError("D0_COLLECTOR_GITHUB_RUN_PROVENANCE")

    d0rows: dict[tuple[str, str, str, str], dict] = {}
    for item in lab["files"]:
        key = tuple(item[k] for k in ("family", "base", "target", "path"))
        oracle.demand(key not in d0rows and isinstance(item.get("patchSha256"), str) and
                      len(item["patchSha256"]) == 64 and item.get("applySeconds") is not None and
                      type(item.get("patchBytes")) is int and item["patchBytes"] > 0,
                      "D0_LAB_UNVERIFIED_FILE")
        d0rows[key] = item
    scanned: dict[tuple[str, str, str, str], dict] = {}
    for item in scan["rows"]:
        key = tuple(item[k] for k in ("family", "base", "target", "path"))
        oracle.demand(key not in scanned, "D3_SCAN_DUPLICATE")
        scanned[key] = item
    rows = []
    for p in pairs["pairs"]:
        for f in p["changed"]:
            k = (p["family"], p["base"], p["target"], f["path"])
            oracle.demand(k in d0rows and k in scanned, "D0_D3_MISSING_PAIR")
            d, s = d0rows[k], scanned[k]
            oracle.demand(d["targetSize"] == f["targetSize"] and all(
                s.get(z) == f[z] for z in
                ("baseSha256", "targetSha256", "baseSize", "targetSize")),
                "D0_D3_CONTENT_MISMATCH")
            rows.append({
                "family": p["family"], "base": p["base"], "target": p["target"], "path": f["path"],
                "baseSha256": f["baseSha256"], "targetSha256": f["targetSha256"],
                "baseSize": f["baseSize"], "targetSize": f["targetSize"],
                "d0PhysicalBytes": d["patchBytes"],
                "d0AppliedTargetSha256": f["targetSha256"],  # PatchLab seq apply verified SHA, all repeats
                "scannerStatus": s["scannerStatus"],
                "baseSlotCount": s["baseSlotCount"], "targetSlotCount": s["targetSlotCount"],
                "baseReconstructedSha256": s["baseReconstructedSha256"],
                "targetReconstructedSha256": s["targetReconstructedSha256"],
            })
    oracle.demand(len(d0rows) == len(scanned) == len(rows) == 1242, "D0_D3_EXTRANEOUS_ROWS")
    report = {
        "schema": oracle.INPUT_SCHEMA, "experimentId": "PATCH-DOTNET-001",
        "role": "calibration", "sourcePairsSha256": oracle.PAIRS_SHA,
        "sourcePlanSha256": oracle.CALIBRATION_PLAN_SHA,
        "d0PolicyId": "CSP_DEFAULT_H0",
        "baselineEvidence": {"sha256": oracle.hash_bytes(lab_data), "gitCommit": commit, "runId": run_id},
        "scannerEvidence": {"sha256": oracle.hash_bytes(scan_data), "gitCommit": commit, "runId": run_id},
        "rows": rows,
    }
    _ = oracle.evaluate(report, pairs)  # fail closed BEFORE publishing
    output.write_bytes(dump(report))
    return {"changedRows": len(rows), "baselineSha256": oracle.hash_bytes(lab_data),
            "scannerSha256": oracle.hash_bytes(scan_data)}


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("mode", choices=["prepare", "combine"])
    p.add_argument("--root", type=Path, required=True)
    p.add_argument("--lab", type=Path)
    p.add_argument("--scan", type=Path)
    p.add_argument("--output", type=Path)
    p.add_argument("--commit", default=os.environ.get("GITHUB_SHA", ""))
    p.add_argument("--run-id", type=int, default=int(os.environ.get("GITHUB_RUN_ID", "0")))
    args = p.parse_args()
    try:
        if args.mode == "prepare":
            print(json.dumps(prepare(args.root), sort_keys=True))
        else:
            if not args.lab or not args.scan or not args.output:
                p.error("combine requires --lab, --scan and --output")
            print(json.dumps(combine(args.root, args.lab, args.scan, args.output,
                                     args.commit, args.run_id), sort_keys=True))
    except (oracle.OracleError, ValueError, OSError, TypeError, KeyError) as exc:
        p.error(str(exc))


if __name__ == "__main__":
    main()
