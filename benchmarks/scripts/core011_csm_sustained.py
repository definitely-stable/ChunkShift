#!/usr/bin/env python3
"""CORE011-CSM-001/G1-E: sustained public CSM metadata controls; no production changes.

A batch completes W * repetitions operations. Wall time is per BATCH; it is
not an individual-operation latency measurement, especially at W > 1.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics
import subprocess

CASES = tuple((size, op, workers)
              for size in (1, 384)
              for op in ("manifest-verify", "enumerate")
              for workers in (1, 4, 16))
FULL_MANIFEST_SHA = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"
FULL_SOURCE_SHA = "ed785261f102a852b63b197d383441a884bb95bb2712cef8d268a3dba722479d"
ROUNDS = 5
REPETITIONS = 16


def numeric(value, what):
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
        raise ValueError(f"invalid nonnegative numeric {what}")
    return value


def validate(folder: Path, run_id: str):
    if not run_id.startswith("CORE011-CSM-001/G1E-"):
        raise ValueError("invalid G1-E RunId")
    results = []
    identities = {}
    checkout = None
    for size, operation, workers in CASES:
        path = folder / f"size-{size}-{operation}-w{workers}.json"
        raw = path.read_bytes()
        row = json.loads(raw)
        if row.get("schema") != "chunkshift.core011.csm-multiblock-metadata.v1":
            raise ValueError(f"bad scenario schema: {path}")
        if row.get("experimentId") != "CORE011-CSM-001" or row.get("runId") != run_id:
            raise ValueError(f"unmatched RunId: {path}")
        if row.get("verdict") != "NO_VERDICT_EXPLORATORY":
            raise ValueError(f"premature verdict: {path}")
        sha = row.get("checkoutSha")
        if not isinstance(sha, str) or not sha:
            raise ValueError("missing checkout SHA")
        if checkout is None:
            checkout = sha
        if sha != checkout:
            raise ValueError("mixed checkouts in isolated process matrix")
        c, fixture = row["conditions"], row["fixture"]
        if (c.get("operation") != operation or c.get("suite") != "blake3" or
                c.get("includeBlockIndex") is not True or
                c.get("workers") != workers or c.get("rounds") != ROUNDS or
                c.get("repetitions") != REPETITIONS or
                c.get("traceEvents") is not False or
                c.get("memoryOnly") is not True or
                c.get("fixtureAndContentHashOutsideTimedRegion") is not True):
            raise ValueError(f"wrong workload: {path}")
        if (fixture.get("sizeMiB") != size or
                fixture.get("payloadCollectedBeforeMeasurement") is not True):
            raise ValueError(f"source not collected before measurement: {path}")
        chunks = fixture.get("chunkCount")
        expected_chunks = 5052 if size == 384 else 11
        if type(chunks) is not int or chunks != expected_chunks:
            raise ValueError(f"physical chunk corpus drift: {path}")
        if fixture.get("minCblkBlocksBy4096EntryCap") != (chunks + 4095) // 4096:
            raise ValueError(f"CBLK threshold inconsistent: {path}")
        if size == 384 and (fixture.get("manifestSha256") != FULL_MANIFEST_SHA or
                            fixture.get("contentSha256") != FULL_SOURCE_SHA or
                            fixture.get("manifestBytes") != 182366):
            raise ValueError(f"384-MiB physical CSM drift: {path}")
        ident = (fixture.get("contentSha256"), fixture.get("manifestSha256"),
                 fixture.get("manifestBytes"), chunks)
        if size in identities and identities[size] != ident:
            raise ValueError(f"corpus differs between lanes: {path}")
        identities[size] = ident
        samples = row.get("samples")
        if not isinstance(samples, list) or len(samples) != ROUNDS:
            raise ValueError(f"missing/extra rounds: {path}")
        for round_number, sample in enumerate(samples, 1):
            if sample.get("round") != round_number:
                raise ValueError(f"incorrect round ordering: {path}")
            if (sample.get("completedOperations") != workers * REPETITIONS or
                    sample.get("validatedChunks") != chunks * workers * REPETITIONS):
                raise ValueError(f"incomplete public CSM oracle: {path}")
            active = sample.get("maxActiveOperations")
            if type(active) is not int or not 1 <= active <= workers:
                raise ValueError(f"invalid worker gate: {path}")
            for key in ("wallMilliseconds", "processCpuMilliseconds",
                        "processAllocatedBytes", "gen0Collections",
                        "gen1Collections", "gen2Collections",
                        "managedHeapBeforeBytes", "managedHeapAfterBytes",
                        "workingSetAfterBytes", "lastGcLohSizeAfterBytes",
                        "lastGcIndex"):
                numeric(sample.get(key), key)
            if sample["wallMilliseconds"] <= 0:
                raise ValueError(f"nonpositive wall measurement: {path}")
        post = row.get("retention", {})
        for key in ("managedBytesAfterExplicitGc",
                    "lastGcLohSizeAfterBytes", "lastGcHeapSizeBytes",
                    "workingSetAfterGcBytes"):
            numeric(post.get(key), key)
        if numeric(row.get("managedBytesAfterWarmupGc"), "warm GC") < 0:
            raise ValueError("invalid warm GC")
        allocations = [s["processAllocatedBytes"]/s["completedOperations"] for s in samples]
        walls = [s["wallMilliseconds"] for s in samples]
        throughput = [1000*s["completedOperations"]/s["wallMilliseconds"] for s in samples]
        results.append({
            "sizeMiB": size, "operation": operation, "workers": workers,
            "chunks": chunks, "manifestBytes": fixture["manifestBytes"],
            "manifestSha256": fixture["manifestSha256"],
            "sourceSha256": fixture["contentSha256"],
            "scenarioSha256": hashlib.sha256(raw).hexdigest(),
            "roundCount": ROUNDS, "operationsPerRound": workers*REPETITIONS,
            "allocPerOperationMedianBytes": statistics.median(allocations),
            "batchWallMedianMs": statistics.median(walls),
            "batchWallMadMs": statistics.median(
                abs(t-statistics.median(walls)) for t in walls),
            "completedOperationsPerSecondMedian": statistics.median(throughput),
            "gen2Total": sum(s["gen2Collections"] for s in samples),
            "maxActiveObserved": max(s["maxActiveOperations"] for s in samples),
            "postGcManagedBytes": post["managedBytesAfterExplicitGc"],
            "postGcLohLastGcBytes": post["lastGcLohSizeAfterBytes"],
            "samples": samples,
        })
    return {
        "schema": "chunkshift.core011.csm-g1e-sustained-summary.v1",
        "experimentId": "CORE011-CSM-001",
        "runId": run_id, "checkoutSha": checkout,
        "caseCount": len(CASES), "sampleCount": len(CASES)*ROUNDS,
        "matrix": "1MiB-vs-384MiB x verify-enumerate x 1-4-16-workers",
        "limitations": [
            "Batch wall is not individual-operation latency.",
            "Process allocation includes scheduling/benchmark scaffolding.",
            "Gen2 depends on process allocation and runtime GC heuristics.",
            "Last-GC LOH size is not LOH allocation volume.",
            "Post-GC resident working set includes prior fixture construction.",
            "Shared hosted runner does not provide stable p95 or CPU affinity.",
            "No controlled pooling candidate or production Amdahl verdict.",
        ],
        "verdict": "NO_VERDICT_EXPLORATORY", "lanes": results,
    }


def run(folder: Path, run_id: str):
    folder.mkdir(parents=True, exist_ok=True)
    for i, (size, operation, workers) in enumerate(CASES, 1):
        target = folder / f"size-{size}-{operation}-w{workers}.json"
        cmd = ["dotnet", "run", "--project",
               "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
               "-c", "Release", "--no-build", "--",
               "core011-csm-multiblock",
               "--operation", operation, "--suite", "blake3", "--bidx", "true",
               "--workers", str(workers), "--size-mib", str(size),
               "--min-chunks", str(4097 if size == 384 else 1),
               "--rounds", str(ROUNDS), "--repetitions", str(REPETITIONS),
               "--run-id", run_id, "--output", str(target)]
        print(f"[{i}/{len(CASES)}] size={size} {operation} workers={workers}",
              flush=True)
        subprocess.run(cmd, check=True, timeout=600)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--evaluate-only", action="store_true")
    args = parser.parse_args()
    if not args.evaluate_only:
        run(args.output_dir, args.run_id)
    summary = validate(args.output_dir, args.run_id)
    (args.output_dir / "summary.json").write_text(
        json.dumps(summary, indent=2)+"\n", encoding="utf-8")
    print(f"Validated {summary['sampleCount']} rounds in "
          f"{summary['caseCount']} fresh processes; NO_VERDICT_EXPLORATORY")


if __name__ == "__main__":
    main()
