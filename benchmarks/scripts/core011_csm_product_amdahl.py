#!/usr/bin/env python3
"""CORE011-CSM-001 G1-F product-call Amdahl controls, fail-closed."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics
import subprocess

CASES = tuple((size, op)
              for size in (1, 8)
              for op in ("verify-pair", "plan", "create", "apply"))
SCHEMA = "chunkshift.core011.csm-g1f-product-amdahl.v1"
ROUNDS = 5
REPETITIONS = 4


def check_number(value, label):
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
        raise ValueError("invalid " + label)
    return value


def validate(folder: Path, run_id: str):
    if not run_id.startswith("CORE011-CSM-001/G1F-"):
        raise ValueError("invalid RunId")
    rows = []
    physical_identity = {}
    checkout = None
    for size, operation in CASES:
        path = folder / f"{size}mib-{operation}.json"
        raw = path.read_bytes()
        case = json.loads(raw)
        if (case.get("schema") != SCHEMA or
                case.get("experimentId") != "CORE011-CSM-001" or
                case.get("runId") != run_id or
                case.get("verdict") != "NO_VERDICT_EXPLORATORY"):
            raise ValueError("schema/run/verdict mismatch: " + str(path))
        sha = case.get("checkoutSha")
        if not sha or not isinstance(sha, str) or (
                checkout is not None and checkout != sha):
            raise ValueError("mixed or absent checkout SHAs")
        checkout = sha
        c, f = case["conditions"], case["fixture"]
        if (c.get("operation") != operation or c.get("sizeMiB") != size or
                c.get("rounds") != ROUNDS or
                c.get("repetitions") != REPETITIONS or
                c.get("workers") != 1 or
                c.get("suite") != "blake3" or
                c.get("includeBlockIndex") is not True or
                c.get("contentRetained") is not True or
                c.get("allContentAndPatchPreparedOutsideTimedRegion") is not True or
                c.get("applyUsesDiskPublication") is not True):
            raise ValueError("wrong product workload conditions")
        for name in ("baseContentSha256", "targetContentSha256",
                     "baseCsmSha256", "targetCsmSha256", "patchSha256"):
            digest = f.get(name, "")
            if not isinstance(digest, str) or len(digest) != 64 or any(
                    x not in "0123456789abcdef" for x in digest):
                raise ValueError("missing/malformed product corpus hash")
        for name in ("baseChunks", "targetChunks", "baseManifestBytes",
                     "targetManifestBytes", "patchBytes", "reusedChunks",
                     "missingChunks"):
            check_number(f.get(name), name)
        if (f["reusedChunks"] < 1 or f["missingChunks"] < 1 or
                f["reusedChunks"] + f["missingChunks"] != f["targetChunks"]):
            raise ValueError("not a mixed-reuse patch workload")
        if (f["baseChunks"] < 1 or
                f["baseManifestBytes"] < 100 or f["targetManifestBytes"] < 100 or
                f["patchBytes"] < 100):
            raise ValueError("incomplete physical source data")
        if size in physical_identity and f != physical_identity[size]:
            raise ValueError("inconsistent corpus within same size")
        physical_identity[size] = f

        budget = case.get("estimatedPlanReaderBufferBudgetBytes")
        if budget != (295056 if operation == "plan" else None):
            raise ValueError("incorrect hypothetical Plan buffer budget")

        samples = case.get("samples")
        if not isinstance(samples, list) or len(samples) != ROUNDS:
            raise ValueError("wrong round count")
        for i, x in enumerate(samples, 1):
            if x.get("roundNumber") != i or x.get("completedOperations") != REPETITIONS:
                raise ValueError("wrong completed operation count")
            for key in ("batchWallMilliseconds", "batchCpuMilliseconds",
                        "processAllocatedBytes", "gen0", "gen1", "gen2",
                        "managedHeapAfterBytes", "workingSetAfterBytes",
                        "lohSizeAfterLastGcBytes"):
                check_number(x.get(key), key)
            if x["batchWallMilliseconds"] <= 0:
                raise ValueError("nonpositive timed batch")
        for key in ("postGcManagedBytes", "postGcLohLastGcBytes", "postGcWorkingSetBytes"):
            check_number(case.get(key), key)

        alloc = [x["processAllocatedBytes"]/REPETITIONS for x in samples]
        wall = [x["batchWallMilliseconds"] for x in samples]
        median_alloc = statistics.median(alloc)
        rows.append({
            "sizeMiB": size, "operation": operation,
            "baseChunks": f["baseChunks"], "targetChunks": f["targetChunks"],
            "reusedChunks": f["reusedChunks"], "missingChunks": f["missingChunks"],
            "targetCsmSha256": f["targetCsmSha256"], "patchSha256": f["patchSha256"],
            "sampleSha256": hashlib.sha256(raw).hexdigest(),
            "completedOperations": ROUNDS * REPETITIONS,
            "medianAllocatedBytesPerCall": median_alloc,
            "medianBatchWallMs": statistics.median(wall),
            "medianBatchOperationsPerSecond": statistics.median(
                [REPETITIONS*1000/t for t in wall]),
            "totalGen2": sum(x["gen2"] for x in samples),
            "hypotheticalPlanReaderBytes": budget,
            "hypotheticalPlanAllocationCeilingFraction":
                min(1, budget/median_alloc) if budget is not None and median_alloc else None,
            "postGcManagedBytes": case["postGcManagedBytes"],
            "rounds": samples,
        })
    return {
        "schema": "chunkshift.core011.csm-g1f-product-evaluator.v1",
        "experimentId": "CORE011-CSM-001",
        "runId": run_id, "checkoutSha": checkout,
        "caseCount": 8, "roundCount": 8*ROUNDS,
        "completedCalls": 8*ROUNDS*REPETITIONS,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "warnings": [
            "Paired verify is an independent full traversal, NOT an additive component of PlanAsync.",
            "Plan has two reader buffers; allocation ceiling assumes removing both with no replacement overhead.",
            "Create includes a physical patch byte oracle; apply includes disk publication and per-round SHA outside timing.",
            "Workload intentionally retains content; post-GC RSS cannot be compared to G1-E source-free controls.",
            "No precise single-call latency or inferred CPU Amdahl from different separate operations.",
            "Apply is a deliberately small mixed-reuse synthetic corpus, not a representative real patch distribution.",
        ],
        "cases": rows,
    }


def run(folder: Path, run_id: str):
    folder.mkdir(parents=True, exist_ok=True)
    for i, (size, operation) in enumerate(CASES, 1):
        print(f"[{i}/8] G1-F size={size} MiB operation={operation}", flush=True)
        cmd = [
            "dotnet", "run", "--project",
            "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
            "--no-build", "-c", "Release", "--",
            "core011-csm-product",
            "--operation", operation,
            "--size-mib", str(size),
            "--rounds", str(ROUNDS),
            "--repetitions", str(REPETITIONS),
            "--run-id", run_id,
            "--output", str(folder / f"{size}mib-{operation}.json")
        ]
        subprocess.run(cmd, check=True, timeout=400)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--evaluate-only", action="store_true")
    args = parser.parse_args()
    if not args.evaluate_only:
        run(args.output_dir, args.run_id)
    summary = validate(args.output_dir, args.run_id)
    (args.output_dir/"summary.json").write_text(
        json.dumps(summary, indent=2)+"\n", encoding="utf-8")
    print(f"G1-F pass {summary['caseCount']} cases, "
          f"{summary['roundCount']} rounds, "
          f"{summary['completedCalls']} public calls; NO_VERDICT_EXPLORATORY")


if __name__ == "__main__":
    main()
