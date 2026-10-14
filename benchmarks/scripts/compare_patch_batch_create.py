#!/usr/bin/env python3
"""Fail-closed, same-host PATCH-EXEC-001 batch result comparison (not a decision)."""

import argparse
import json
import math
import re
from pathlib import Path

BATCH = "chunkshift.patch-batch-create.v1"
LEGACY = "chunkshift.patch-lab.v1"
RESULT = "chunkshift.patch-batch-create-comparison.v1"
DIGEST = re.compile(r"[a-fA-F0-9]{64}\\Z")
KEY = ("family", "base", "target", "path")
FIELDS = ("targetSize", "targetChunks", "patchBytes", "patchSha256",
          "tcsmBytes", "uniqueMissingBytes", "payloadEntries",
          "rawEntries", "zstdEntries", "dictionaryEntries")
HOST_FIELDS = ("osDescription", "osArchitecture", "processArchitecture",
               "framework", "processorCount")


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def load(path, expected):
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    require(data.get("schema") == expected, f"{path}: wrong schema")
    require(isinstance(data.get("files"), list) and bool(data["files"]),
            f"{path}: empty population")
    require(isinstance(data.get("environment"), dict),
            f"{path}: missing environment")
    result = {}
    for row in data["files"]:
        identity = tuple(row.get(k) for k in KEY)
        require(all(isinstance(v, str) and v for v in identity),
                f"{path}: invalid file identity")
        require(identity not in result, f"{path}: duplicate file identity {identity}")
        sha = row.get("patchSha256")
        require(isinstance(sha, str) and DIGEST.fullmatch(sha) is not None,
                f"{path}: missing patch SHA for {identity}")
        require(isinstance(row.get("patchBytes"), int) and row["patchBytes"] > 0,
                f"{path}: invalid patch size {identity}")
        result[identity] = row
    if expected == BATCH:
        require(data.get("applyRepeats") == 0,
                f"{path}: batch is not create-only")
        metrics = data.get("batchCreate")
        require(isinstance(metrics, dict) and
                metrics.get("workload") == "manifest-digest-plan-create-readback-sha256",
                f"{path}: missing batch metrics")
        require(isinstance(data.get("workers"), int) and data["workers"] > 0,
                f"{path}: invalid outer worker count")
        require(data.get("execution") in
                ("h0", "h1", "h2-w1", "h2-w2", "h2-w4", "h2-w8",
                 "h3-w1", "h3-w2", "h3-w4", "h3-w8"),
                f"{path}: invalid execution")
        for metric in ("processCpuSeconds", "processAllocatedBytes",
                       "processPeakWorkingSetBytes"):
            value = metrics.get(metric)
            require(isinstance(value, (int, float)) and not isinstance(value, bool)
                    and math.isfinite(value) and value >= 0,
                    f"{path}: bad {metric}")
        require(metrics["processPeakWorkingSetBytes"] > 0,
                f"{path}: missing process high water")
        require(all(row.get("createMetrics") is None for row in result.values()),
                f"{path}: falsely attributed per-file process counters")
    else:
        require(data.get("applyRepeats", 0) > 0 and
                all(isinstance(row.get("applyMetrics"), dict)
                    for row in result.values()),
                f"{path}: H0 oracle must be apply-verified")
        require(data.get("execution") == "h0" and data.get("workers") == 1,
                f"{path}: oracle must be single-file H0")
    return data, result


def compare(oracle_path, baseline_path, candidates):
    oracle, oracle_files = load(oracle_path, LEGACY)
    baseline, baseline_files = load(baseline_path, BATCH)
    checks = [(baseline_path, baseline, baseline_files)]
    checks += [(path, *load(path, BATCH)) for path in candidates]
    for path, data, files in checks:
        for field in ("corpusPairsSha256", "lane", "policy"):
            require(data.get(field) == oracle.get(field),
                    f"{path}: {field} changed vs oracle")
        for field in HOST_FIELDS:
            require(data["environment"].get(field) == baseline["environment"].get(field),
                    f"{path}: host {field} mismatch")
        require(set(files) == set(oracle_files),
                f"{path}: missing or extra files")
        for key in oracle_files:
            for field in FIELDS:
                require(files[key].get(field) == oracle_files[key].get(field),
                        f"{path}: {key} {field} differs from apply-verified H0")
        value = data.get("elapsedSeconds")
        require(isinstance(value, (int, float)) and
                not isinstance(value, bool) and math.isfinite(value) and value > 0,
                f"{path}: invalid wall time")

    output = {"schema": RESULT, "comparisonStatus": "SAME_BUILD_EQUAL_NOT_ADOPT",
              "fileCount": len(oracle_files), "baseline": str(baseline_path),
              "baselineWallSeconds": baseline["elapsedSeconds"], "candidates": []}
    for path, data, _ in checks[1:]:
        output["candidates"].append({
            "path": str(path), "outerWorkers": data["workers"],
            "execution": data["execution"], "wallSeconds": data["elapsedSeconds"],
            "relativeSpeedup": baseline["elapsedSeconds"] / data["elapsedSeconds"],
            "processCpuSeconds": data["batchCreate"]["processCpuSeconds"],
            "processAllocatedBytes": data["batchCreate"]["processAllocatedBytes"],
            "processLifetimeHighWaterBytes": data["batchCreate"]["processPeakWorkingSetBytes"],
        })
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--oracle", required=True, help="apply-verified sequential H0 patch-lab.v1")
    parser.add_argument("--baseline", required=True, help="batch-create H0 outer=1")
    parser.add_argument("--output")
    parser.add_argument("candidates", nargs="+")
    args = parser.parse_args()
    report = compare(args.oracle, args.baseline, args.candidates)
    encoded = json.dumps(report, indent=2, sort_keys=True) + "\n"
    if args.output:
        Path(args.output).write_text(encoded, encoding="utf-8")
    else:
        print(encoded, end="")


if __name__ == "__main__":
    main()
