#!/usr/bin/env python3
"""Independently validate the *report* from frozen HDiffPatch calibration.

This checks identities, coverage and byte arithmetic against the SHA-locked
pairs.json. It does not independently replay binary patches (not retained).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
from pathlib import Path

PAIRS_SHA = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
ARCHIVE_SHA = "628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558"
SOURCE_COMMIT = "3b9dca715ca492873bf2c49e22e5d5b7d2a78620"
PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
H0_CALIBRATION_BYTES = 11860274
CAL_FAMILIES = {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64"}
SHA_RE = re.compile(r"[0-9a-f]{64}\Z")
COMMIT_RE = re.compile(r"[0-9a-f]{40}\Z")


def require(ok: bool, label: str) -> None:
    if not ok:
        raise ValueError(label)


def load_expected(pairs_path: Path) -> list[dict]:
    raw = pairs_path.read_bytes()
    require(hashlib.sha256(raw).hexdigest() == PAIRS_SHA, "frozen pairs SHA mismatch")
    doc = json.loads(raw)
    require(doc.get("schema") == "chunkshift.patch-pairs.v1", "pairs schema")
    expected: list[dict] = []
    for pair in doc["pairs"]:
        if pair["family"] not in CAL_FAMILIES:
            continue
        for item in pair["changed"]:
            expected.append({
                "family": pair["family"], "base": pair["base"],
                "target": pair["target"], "path": item["path"],
                "baseSha256": item["baseSha256"],
                "baseBytes": item["baseSize"],
                "targetSha256": item["targetSha256"],
                "targetBytes": item["targetSize"],
            })
    require(len(expected) == 1049, "expected original calibration file count")
    return expected


def validate(report: dict, expected: list[dict]) -> dict:
    require(report.get("schema") == "chunkshift.patch-gap-hdiff-reference.v1", "schema")
    require(report.get("status") == "ORIGINAL_REFERENCE_MEASURED", "producer status")
    require(report.get("role") == "calibration", "calibration role")
    require(report.get("rfcSizeGate") == "NOT_APPLICABLE", "whole-file RFC gate")
    require(report.get("pairsSha256") == PAIRS_SHA, "report corpus lock")
    require(report.get("protocolCommit") == PROTOCOL, "protocol")
    require(report.get("hdiffpatchSourceCommit") == SOURCE_COMMIT, "upstream source")
    require(report.get("archiveSha256") == ARCHIVE_SHA, "original archive")
    require(bool(re.fullmatch(r"PATCH-GAP-001/RUN-[0-9]{8}-[0-9]+-[0-9a-f]{7}-linux-x64-hdiff-calibration",
                              str(report.get("runId", "")))), "RunId")
    binaries = report.get("binarySha256")
    require(isinstance(binaries, dict) and set(binaries) == {"hdiffz", "hpatchz"}
            and all(isinstance(v, str) and SHA_RE.fullmatch(v) for v in binaries.values()),
            "pinned binary digests")
    env = report.get("environment")
    require(isinstance(env, dict) and isinstance(env.get("sourceGitCommit"), str)
            and COMMIT_RE.fullmatch(env["sourceGitCommit"]), "tested Git HEAD")
    require(report["runId"].split("-")[-5] == env["sourceGitCommit"][:7]
            if False else report["runId"].endswith(
                "-" + env["sourceGitCommit"][:7] + "-linux-x64-hdiff-calibration"),
            "RunId Git HEAD binding")

    actual = report.get("files")
    require(isinstance(actual, list) and len(actual) == len(expected) == 1049,
            "complete 1,049-file original calibration population")
    totals = {"memory": 0, "stream": 0}
    signature = hashlib.sha256()
    for idx, (row, source) in enumerate(zip(actual, expected)):
        require(isinstance(row, dict), f"row {idx} type")
        for field, val in source.items():
            require(row.get(field) == val, f"row {idx} frozen identity {field}")
        require(row.get("oldSha256") == source["baseSha256"], f"row {idx} old SHA")
        require(row.get("targetSha256") == source["targetSha256"], f"row {idx} target SHA")
        require(row.get("targetBytes") == source["targetBytes"], f"row {idx} target length")
        lanes = row.get("lanes")
        require(isinstance(lanes, dict) and set(lanes) == {"memory", "stream"},
                f"row {idx} exact required lanes")
        for lane, mode in (("memory", "-m-4"), ("stream", "-s-64")):
            datum = lanes[lane]
            require(isinstance(datum, dict) and datum.get("matchMode") == mode
                    and datum.get("applyCache") == "-s-8m"
                    and datum.get("exactReconstruction") is True,
                    f"row {idx} {lane} frozen CLI and exactness")
            size = datum.get("patchBytes")
            digest = datum.get("patchSha256")
            require(type(size) is int and size > 0, f"row {idx} {lane} physical bytes")
            require(isinstance(digest, str) and SHA_RE.fullmatch(digest),
                    f"row {idx} {lane} patch SHA")
            for timing in ("createSeconds", "applySeconds"):
                value = datum.get(timing)
                require(type(value) in (float, int) and math.isfinite(value) and value > 0,
                        f"row {idx} {lane} {timing}")
            totals[lane] += size
        signature.update(json.dumps({
            "source": source,
            "patches": {lane: {
                "bytes": lanes[lane]["patchBytes"],
                "sha256": lanes[lane]["patchSha256"],
            } for lane in ("memory", "stream")},
        }, sort_keys=True, separators=(",", ":")).encode("utf-8") + b"\n")
    require(report.get("totals") == totals, "producer totals differ from independent sum")
    return {
        "schema": "chunkshift.patch-gap-hdiff-calibration-report-audit.v1",
        "status": "ORIGINAL_CALIBRATION_REPORT_AUDIT_PASS__NOT_FINAL_REFERENCE_EVIDENCE",
        "role": "calibration",
        "runId": report["runId"],
        "sourceGitCommit": env["sourceGitCommit"],
        "pairsSha256": PAIRS_SHA,
        "files": 1049,
        "targetBytes": sum(x["targetBytes"] for x in expected),
        "h0CalibrationBytes": H0_CALIBRATION_BYTES,
        "referencePatchBytes": totals,
        "referenceMinusH0Bytes": {k: v - H0_CALIBRATION_BYTES for k, v in totals.items()},
        "physicalRowsSha256": signature.hexdigest(),
        "rfcSizeGate": "NOT_APPLICABLE",
        "readyForEvaluation": False,
        "readyToCloseIssue183": False,
        "independentBinaryApplyReplayed": False,
        "limits": "Report/manifest recomputation only; original patch binaries and raw source independently re-applied are not retained by this verifier.",
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--pairs", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    outcome = validate(json.loads(args.report.read_text(encoding="utf-8")),
                       load_expected(args.pairs))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(outcome, indent=2, sort_keys=True) + "\n",
                           encoding="utf-8")
    print(json.dumps(outcome, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
