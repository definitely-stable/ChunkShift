#!/usr/bin/env python3
"""Independent matched original HDiffPatch evaluation *report* audit.

The producer applies the real patches and checks the target SHA; this auditor
does not re-apply ephemeral patch binaries. It refuses partial/mismatched rows,
a substituted frozen H0 index, invalid source/run identities, and fabricated
whole-file CSP-RFC factor claims.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
from collections import defaultdict
from pathlib import Path

PAIRS_SHA = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
SOURCE_COMMIT = "3b9dca715ca492873bf2c49e22e5d5b7d2a78620"
ARCHIVE_SHA = "628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558"
H0_SOURCE_COMMIT = "8983eb11faa3e7c2df17b74d201249a19dd3dc13"
H0_INDEX_SHA = "73be4009cda2e8f4af6a302f4863be04039b2ae327379bfb582eed91fe66a953"
H0_TOTAL = 26363364
COUNT = 844
FAMILIES = {"node-linux-x64": 531, "node-win-x64": 283, "tzdata": 28, "chunkshift-source": 2}
CALIBRATION_RUN = 41333408342
CALIBRATION_ARTIFACT_SHA = "e42db34baf9e5bb1c5374ac151d6d2ebf2a9862377d2964e6d1d0a058411548b"
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
HEX40 = re.compile(r"[0-9a-f]{40}\Z")


def require(flag: bool, reason: str) -> None:
    if not flag:
        raise ValueError(reason)


def original_rows(pairs: dict) -> list[dict]:
    require(pairs.get("schema") == "chunkshift.patch-pairs.v1", "corpus schema")
    rows = []
    for pair in pairs["pairs"]:
        if pair["family"] not in FAMILIES:
            continue
        for item in pair["changed"]:
            rows.append({
                "family": pair["family"], "base": pair["base"],
                "target": pair["target"], "path": item["path"],
                "baseSha256": item["baseSha256"], "baseBytes": item["baseSize"],
                "targetSha256": item["targetSha256"], "targetBytes": item["targetSize"],
            })
    require(len(rows) == COUNT, "original evaluation cardinality")
    return rows


def key(row: dict, *, h0: bool = False) -> tuple:
    return (row["family"], row["baseVersion" if h0 else "base"],
            row["targetVersion" if h0 else "target"], row["path"])


def audit(report: dict, expected: list[dict], h0: dict, calibration: dict) -> dict:
    require(calibration.get("schema") ==
            "chunkshift.patch-gap-001-hdiff-original-calibration-retained.v1",
            "calibration ledger schema")
    require(calibration.get("source", {}).get("pairsSha256") == PAIRS_SHA
            and calibration["source"]["protocolCommit"] == PROTOCOL
            and calibration["source"]["hdiffSourceCommit"] == SOURCE_COMMIT
            and calibration["source"]["hdiffOfficialLinuxZipSha256"] == ARCHIVE_SHA,
            "admitted calibration source/protocol pin")
    require(calibration.get("run", {}).get("workflowRunId") == CALIBRATION_RUN
            and calibration["run"]["reportAuditStepPassed"] is True
            and calibration["run"]["measurementStepsPassed"] is True
            and calibration["run"]["artifactUploadPassed"] is True
            and calibration["run"]["runAttempt"] == 1
            and calibration.get("referenceArtifact", {}).get("zipSha256")
            == CALIBRATION_ARTIFACT_SHA, "admitted original calibration provenance")
    require(calibration.get("readyForEvaluation") is False
            and calibration.get("rfcSizeGate") == "NOT_APPLICABLE",
            "calibration is not an RFC or statistical holdout verdict")
    require(report.get("schema") == "chunkshift.patch-gap-hdiff-reference.v1"
            and report.get("status") == "ORIGINAL_REFERENCE_MEASURED"
            and report.get("role") == "evaluation"
            and report.get("rfcSizeGate") == "NOT_APPLICABLE",
            "reference-only evaluation status")
    require(report.get("pairsSha256") == PAIRS_SHA
            and report.get("protocolCommit") == PROTOCOL
            and report.get("hdiffpatchSourceCommit") == SOURCE_COMMIT
            and report.get("archiveSha256") == ARCHIVE_SHA,
            "HDiff source and protocol lock")
    binaries = report.get("binarySha256")
    require(isinstance(binaries, dict) and set(binaries) == {"hdiffz", "hpatchz"}
            and all(isinstance(x, str) and HEX64.fullmatch(x) for x in binaries.values()),
            "SHA-pinned upstream binary digests")
    environment = report.get("environment")
    require(isinstance(environment, dict)
            and isinstance(environment.get("sourceGitCommit"), str)
            and HEX40.fullmatch(environment["sourceGitCommit"]),
            "current tested Git SHA")
    run_id = report.get("runId")
    require(isinstance(run_id, str) and re.fullmatch(
        r"PATCH-GAP-001/RUN-20[0-9]{6}-[0-9]+-[0-9a-f]{7}-linux-x64-hdiff-evaluation",
        run_id) is not None
        and run_id.endswith("-" + environment["sourceGitCommit"][:7] +
                            "-linux-x64-hdiff-evaluation"),
        "eval RunId bound to current Git SHA")

    require(h0.get("schema") == "chunkshift.patch-gap-g1-byte-study.v1"
            and h0.get("datasetRole") == "evaluation"
            and h0.get("datasetSha256") == PAIRS_SHA
            and h0.get("sourceCommit") == H0_SOURCE_COMMIT
            and h0.get("protocolCommit") == PROTOCOL,
            "original frozen H0 evaluation index")
    original_h0 = h0.get("files")
    require(isinstance(original_h0, list) and len(original_h0) == COUNT,
            "844-file original H0 index")
    by_key = {}
    for row in original_h0:
        k = key(row, h0=True)
        require(k not in by_key, "duplicate H0 file")
        require(type(row.get("h0PatchBytes")) is int and row["h0PatchBytes"] > 0
                and isinstance(row.get("h0PatchSha256"), str)
                and HEX64.fullmatch(row["h0PatchSha256"]),
                "H0 physical CSP patch identity")
        by_key[k] = row
    require(sum(x["h0PatchBytes"] for x in original_h0) == H0_TOTAL,
            "frozen H0 total")
    rows = report.get("files")
    require(isinstance(rows, list) and len(rows) == len(expected) == COUNT,
            "complete 844-file original evaluation")
    families = defaultdict(lambda: {"files": 0, "targetBytes": 0,
                                    "h0Bytes": 0, "memoryBytes": 0, "streamBytes": 0})
    counts = {"memory": {"win": 0, "tie": 0, "loss": 0},
              "stream": {"win": 0, "tie": 0, "loss": 0}}
    patch_hash = hashlib.sha256()
    seen = set()
    for i, (row, source) in enumerate(zip(rows, expected)):
        require(isinstance(row, dict), f"evaluation row {i} object")
        for field, value in source.items():
            require(row.get(field) == value, f"evaluation row {i} original {field}")
        require(row.get("oldSha256") == source["baseSha256"]
                and row.get("targetSha256") == source["targetSha256"]
                and row.get("targetBytes") == source["targetBytes"],
                f"evaluation row {i} actual source/target identities")
        k = key(source)
        require(k not in seen, "duplicate reference file")
        seen.add(k)
        prior = by_key.get(k)
        require(prior is not None, "reference absent from frozen H0")
        require(prior.get("baseSha256") == source["baseSha256"]
                and prior.get("targetSha256") == source["targetSha256"]
                and prior.get("targetBytes") == source["targetBytes"],
                "reference/H0 unmatched source, target or length")
        lanes = row.get("lanes")
        require(isinstance(lanes, dict) and set(lanes) == {"memory", "stream"},
                f"evaluation row {i} both frozen modes")
        fam = families[row["family"]]
        fam["files"] += 1
        fam["targetBytes"] += source["targetBytes"]
        fam["h0Bytes"] += prior["h0PatchBytes"]
        for lane, mode in (("memory", "-m-4"), ("stream", "-s-64")):
            d = lanes[lane]
            require(isinstance(d, dict) and d.get("matchMode") == mode
                    and d.get("applyCache") == "-s-8m"
                    and d.get("exactReconstruction") is True,
                    f"evaluation row {i} {lane} CLI and apply correctness")
            size = d.get("patchBytes")
            require(type(size) is int and size > 0
                    and isinstance(d.get("patchSha256"), str)
                    and HEX64.fullmatch(d["patchSha256"]),
                    f"evaluation row {i} {lane} physical patch")
            for field in ("createSeconds", "applySeconds"):
                timing = d.get(field)
                require(type(timing) in (float, int)
                        and math.isfinite(timing) and timing > 0,
                        f"evaluation row {i} {lane} {field}")
            fam[lane + "Bytes"] += size
            side = "win" if size < prior["h0PatchBytes"] else (
                "tie" if size == prior["h0PatchBytes"] else "loss")
            counts[lane][side] += 1
        patch_hash.update(json.dumps({
            "source": source,
            "h0CspBytes": prior["h0PatchBytes"],
            "patches": {lane: {"bytes": lanes[lane]["patchBytes"],
                              "sha256": lanes[lane]["patchSha256"]}
                        for lane in ("memory", "stream")},
        }, separators=(",", ":"), sort_keys=True).encode("utf-8") + b"\n")
    require(seen == set(by_key), "original H0 membership mismatch")
    require({k: v["files"] for k, v in families.items()} == FAMILIES,
            "original family membership")
    require(sum(x["h0Bytes"] for x in families.values()) == H0_TOTAL,
            "original matched whole-split H0 total")
    totals = {lane: sum(x[lane + "Bytes"] for x in families.values())
              for lane in ("memory", "stream")}
    require(report.get("totals") == totals, "producer/reference physical totals")
    return {
        "schema": "chunkshift.patch-gap-hdiff-original-evaluation-audit.v1",
        "status": "ORIGINAL_EVALUATION_REPORT_AUDIT_PASS__REFERENCE_ONLY",
        "role": "evaluation", "runId": run_id,
        "sourceGitCommit": environment["sourceGitCommit"],
        "pairsSha256": PAIRS_SHA, "h0IndexSha256": H0_INDEX_SHA,
        "calibrationRunId": CALIBRATION_RUN,
        "calibrationArtifactSha256": CALIBRATION_ARTIFACT_SHA,
        "fileCount": COUNT, "familyCount": len(families),
        "targetBytes": sum(x["targetBytes"] for x in families.values()),
        "h0CspBytes": H0_TOTAL, "referencePatchBytes": totals,
        "savingsBytes": {k: H0_TOTAL - v for k, v in totals.items()},
        "fractionalSavings": {k: (H0_TOTAL - v) / H0_TOTAL for k, v in totals.items()},
        "filesVsH0": counts, "families": dict(sorted(families.items())),
        "matchedPhysicalRowsSha256": patch_hash.hexdigest(),
        "rfcSizeGate": "NOT_APPLICABLE",
        "independentBinaryApplyReplayed": False,
        "readyToCloseIssue183": False, "readyToCloseGate251": False,
        "limits": "Whole-file reference only; frozen evaluation split previously observed; producer performed exact binary apply, separate audit checks original H0 and manifest, not archived patch binaries.",
    }


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--report", type=Path, required=True)
    p.add_argument("--pairs", type=Path, required=True)
    p.add_argument("--h0-index", type=Path, required=True)
    p.add_argument("--calibration-ledger", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    raw = args.pairs.read_bytes()
    require(hashlib.sha256(raw).hexdigest() == PAIRS_SHA, "original pairs.json digest")
    h0raw = args.h0_index.read_bytes()
    require(hashlib.sha256(h0raw).hexdigest() == H0_INDEX_SHA,
            "original H0 g1-index.json digest")
    result = audit(json.loads(args.report.read_text(encoding="utf-8")),
                   original_rows(json.loads(raw)), json.loads(h0raw),
                   json.loads(args.calibration_ledger.read_text(encoding="utf-8")))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n",
                           encoding="utf-8")
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
