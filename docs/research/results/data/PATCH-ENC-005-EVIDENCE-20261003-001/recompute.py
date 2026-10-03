#!/usr/bin/env python3
"""Recompute this partial calibration's findings without CI artifacts or lab code."""
import argparse
import hashlib
import json
from pathlib import Path

LANES = ("csp", "H4-L1-R2", "H7-L1-R2-E75", "H9-L9-K4-C16-R1M",
         "H9-L12-K4-C16-R1M", "H9-L15-K4-C16-R1M")
PLATFORMS = {"linux-x64", "linux-arm64", "win-x64"}


def recompute(compact, files_path):
    if (compact["schema"] != "chunkshift.patch-enc-005.incomplete-capture.v1"
            or compact["workflowConclusion"] != "failure"
            or compact["datasetRole"] != "calibration"
            or not compact["missing"]):
        raise ValueError("unexpected capture identity or completeness")
    raw = files_path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != compact["filesSha256"]:
        raise ValueError("files.jsonl digest mismatch")
    files = [json.loads(line) for line in raw.splitlines()]
    keys = [tuple(row[k] for k in ("family", "base", "target", "path")) for row in files]
    if len(files) != compact["fileCount"] or len(set(keys)) != len(keys):
        raise ValueError("file population mismatch")
    totals = {lane: sum(row["lanes"][lane]["patchBytes"] for row in files) for lane in LANES}
    mismatches = [dict(zip(("family", "base", "target", "path"), key))
                  for key, row in zip(keys, files)
                  if row["lanes"]["H4-L1-R2"]["patchSha256"] !=
                  row["lanes"]["H7-L1-R2-E75"]["patchSha256"]]
    if not mismatches:
        raise ValueError("expected H7/H4 counterexample is missing")
    if set(compact["partialTiming"]) != PLATFORMS or set(compact["memory"]) != PLATFORMS:
        raise ValueError("platform coverage mismatch")
    for platform, documents in compact["partialTiming"].items():
        if len(documents) != 8:
            raise ValueError("expected warmup and one complete round")
        for document in documents.values():
            lane = document["lane"]
            patch_map = [
                {**dict(zip(("family", "base", "target", "path"), key)),
                 "patchSha256": row["lanes"][lane]["patchSha256"]}
                for key, row in sorted(zip(keys, files))
            ]
            encoded = json.dumps(patch_map, sort_keys=True, separators=(",", ":")).encode()
            if hashlib.sha256(encoded).hexdigest() != document["patchMapSha256"]:
                raise ValueError(f"{platform}/{lane}: canonical patch map mismatch")
            if document["aggregate"]["patchBytes"] != totals[lane]:
                raise ValueError(f"{platform}/{lane}: byte total mismatch")
    memory_maxima = {}
    memory_population = None
    for platform, lanes in compact["memory"].items():
        if set(lanes) != set(LANES):
            raise ValueError("memory lane population mismatch")
        for lane, document in lanes.items():
            expected_run = f"PATCH-ENC-005/RUN-20261003-002-{compact['sourceCommit']}-{platform}"
            if (document["runId"] != expected_run
                    or document["environment"]["gitCommit"] != compact["sourceCommit"]
                    or document["corpusPairsSha256"] != compact["corpusPairsSha256"]
                    or document["lane"] != lane
                    or document["population"] != "max-base-target"):
                raise ValueError("memory identity mismatch")
            population = {tuple(row[k] for k in ("family", "base", "target", "path"))
                          for row in document["files"]}
            if (len(population) != len(document["files"])
                    or not population.issubset(set(keys))
                    or any(max(row["baseSize"], row["targetSize"]) < 1048576 for row in document["files"])):
                raise ValueError("invalid memory population")
            if memory_population is None:
                memory_population = population
            elif population != memory_population:
                raise ValueError("memory population differs across lanes/platforms")
        memory_maxima[platform] = {
            lane: {
                "fileCount": len(document["files"]),
                "createPeakOverIdleBytes": max(0, max(row["createPeakBytes"] for row in document["files"]) - document["idleBaselineBytes"]),
                "applyPeakOverIdleBytes": max(0, max(row["applyPeakBytes"] for row in document["files"]) - document["idleBaselineBytes"]),
            }
            for lane, document in lanes.items()
        }
    return {
        "experimentId": "PATCH-ENC-005", "evidenceId": compact["evidenceId"],
        "calibrationStatus": "INCOMPLETE", "productionDecision": None, "finalists": [],
        "reason": "H7/H4 byte oracle stopped round one; required decision evidence is missing",
        "h7ByteOracle": {"status": "FAIL", "mismatchCount": len(mismatches),
                         "fileCount": len(files), "mismatches": mismatches},
        "roundOnePatchBytes": totals, "memoryMaxima": memory_maxima,
        "missing": compact["missing"],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compact", required=True, type=Path)
    parser.add_argument("--expected-verdict", type=Path)
    args = parser.parse_args()
    compact = json.loads(args.compact.read_text(encoding="utf-8"))
    verdict = recompute(compact, args.compact.parent / "files.jsonl")
    if args.expected_verdict:
        expected = json.loads(args.expected_verdict.read_text(encoding="utf-8"))
        if verdict != expected:
            raise ValueError("verdict mismatch")
        print(f"Verified INCOMPLETE; H7/H4 mismatches: {verdict['h7ByteOracle']['mismatchCount']}")
    else:
        print(json.dumps(verdict, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
