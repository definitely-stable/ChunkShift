#!/usr/bin/env python3
"""Launch and strictly evaluate a bounded, non-decision CORE011-CSM-001 GC pilot."""

import argparse
import hashlib
import json
import math
import statistics
import subprocess
from pathlib import Path

SCHEMA = "chunkshift.core011.csm-gc-isolate.v1"
OPS = ("create", "manifest-verify", "enumerate", "content-verify")
# Deliberately a diagnostic subset, not a factorial/A-B sweep. Hash/BIDX
# are confounded between families; no cross-family speedup claim is allowed.
CASES = tuple(
    (suite, bidx, workers, operation)
    for suite, bidx, worker_levels in (
        ("blake3", True, (1, 4)), ("sha256", False, (1,))
    )
    for workers in worker_levels
    for operation in OPS
)


def evaluate(directory: Path, run_id: str, rounds: int, repetitions: int, size_mib: int):
    files = sorted(directory.glob("scenario-*.json"))
    if len(files) != len(CASES):
        raise ValueError(f"expected {len(CASES)} isolated scenarios, got {len(files)}")
    cases = {}
    source_hash = None
    manifest_by_suite = {}
    raw_hashes = {}
    for path in files:
        raw = path.read_bytes()
        document = json.loads(raw)
        if document.get("schema") != SCHEMA or document.get("experimentId") != "CORE011-CSM-001":
            raise ValueError(f"invalid schema/experiment in {path}")
        if document.get("runId") != run_id or not document.get("checkoutSha"):
            raise ValueError(f"invalid provenance in {path}")
        conditions = document["conditions"]
        key = (
            conditions["hashSuite"],
            conditions["includeBlockIndex"],
            conditions["workers"],
            conditions["operation"],
        )
        if key not in CASES or key in cases:
            raise ValueError(f"unexpected or duplicate scenario: {key}")
        if conditions["rounds"] != rounds or conditions["repetitions"] != repetitions:
            raise ValueError("unfrozen repetitions or rounds")
        corpus = document["corpus"]
        if corpus["contentMiB"] != size_mib or corpus["profileId"] != "fastcdc.gear.chunkshift.v1.64k":
            raise ValueError("wrong corpus or chunking profile")
        sha = corpus.get("contentSha256", "")
        if len(sha) != 64 or any(x not in "0123456789abcdef" for x in sha):
            raise ValueError("missing content digest")
        if source_hash is None:
            source_hash = sha
        elif source_hash != sha:
            raise ValueError("corpus differs between scenarios")
        suite_key = key[:2]
        identity = (corpus["manifestSha256"], corpus["manifestBytes"])
        if len(identity[0]) != 64 or identity[1] <= 0:
            raise ValueError("invalid CSM identity")
        if suite_key in manifest_by_suite and manifest_by_suite[suite_key] != identity:
            raise ValueError("CSM fixture bytes differ within suite/BIDX scenario")
        manifest_by_suite[suite_key] = identity
        samples = document.get("samples")
        if not isinstance(samples, list) or len(samples) != rounds:
            raise ValueError("invalid round count")
        for i, sample in enumerate(samples, 1):
            if sample.get("round") != i:
                raise ValueError("out-of-order or missing round")
            for metric in ("wallMilliseconds", "processCpuMilliseconds"):
                value = sample.get(metric)
                if type(value) not in (float, int) or not math.isfinite(value) or value < 0:
                    raise ValueError(f"invalid {metric}")
            for metric in (
                "processAllocatedBytes", "workingSetAfterBytes",
                "peakProcessWorkingSetBytes", "privateMemoryAfterBytes",
                "gen0Collections", "gen1Collections", "gen2Collections",
                "lohLastGcSizeAfterBytes", "lohLastGcIndex", "heapLastGcSizeBytes",
                "maxActiveOperations", "completedOperations", "validatedChunks",
            ):
                value = sample.get(metric)
                if type(value) is not int or value < 0:
                    raise ValueError(f"invalid {metric}")
            if (sample["completedOperations"] != key[2] * repetitions or
                    sample["validatedChunks"] < sample["completedOperations"] or
                    not 1 <= sample["maxActiveOperations"] <= key[2]):
                raise ValueError("incomplete worker batch or invalid active counter")
        cases[key] = document
        raw_hashes[path.name] = hashlib.sha256(raw).hexdigest()

    result = []
    for key in CASES:
        samples = cases[key]["samples"]
        wall = [s["wallMilliseconds"] for s in samples]
        med = statistics.median(wall)
        result.append({
            "suite": key[0],
            "bidx": key[1],
            "workers": key[2],
            "operation": key[3],
            "wallMedianMs": med,
            "wallMinMs": min(wall),
            "wallMaxMs": max(wall),
            "allocationMedianBytes": statistics.median(
                s["processAllocatedBytes"] for s in samples),
            "gen2Total": sum(s["gen2Collections"] for s in samples),
            "peakProcessWorkingSetBytesMax": max(
                s["peakProcessWorkingSetBytes"] for s in samples),
            "manifestBytes": cases[key]["corpus"]["manifestBytes"],
            "sampleCount": rounds,
            "rounds": samples,
        })
    return {
        "schema": "chunkshift.core011.csm-gc-pilot-summary.v1",
        "experimentId": "CORE011-CSM-001",
        "runId": run_id,
        "sampleCount": len(CASES) * rounds,
        "sourceSha256": source_hash,
        "checkoutSha": next(iter(cases.values()))["checkoutSha"],
        "decision": "NO_VERDICT_EXPLORATORY",
        "limitations": [
            "8-MiB-memory-only-single-node-pilot",
            "suite-and-BIDX-confounded-across-families",
            "LOH-reports-last-GC-size-not-allocation",
            "working-set-process-lifetime-peak-not-per-round-peak",
            "whole-process-allocation-and-CPU",
            "Task.Run-overhead-included-no-controlled-CPU-affinity",
        ],
        "rawFileSha256": raw_hashes,
        "scenarios": result,
    }


def run_matrix(directory: Path, run_id: str, size_mib: int, rounds: int, repetitions: int):
    directory.mkdir(parents=True, exist_ok=True)
    for i, (suite, bidx, workers, operation) in enumerate(CASES):
        destination = directory / f"scenario-{i:02d}.json"
        cmd = [
            "dotnet", "run", "--project",
            "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
            "-c", "Release", "--no-build", "--", "core011-csm-gc",
            "--suite", suite, "--bidx", str(bidx).lower(),
            "--workers", str(workers), "--operation", operation,
            "--size-mib", str(size_mib), "--rounds", str(rounds),
            "--repetitions", str(repetitions),
            "--run-id", run_id, "--output", str(destination),
        ]
        print(f"[{i+1}/{len(CASES)}] {suite} bidx={bidx} workers={workers} operation={operation}", flush=True)
        subprocess.run(cmd, check=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--size-mib", type=int, default=8)
    parser.add_argument("--rounds", type=int, default=2)
    parser.add_argument("--repetitions", type=int, default=2)
    parser.add_argument("--evaluate-only", action="store_true")
    args = parser.parse_args()
    if not 1 <= args.size_mib <= 64 or not 1 <= args.rounds <= 10 or not 1 <= args.repetitions <= 20:
        parser.error("pilot controls out of bounded range")
    if not args.evaluate_only:
        run_matrix(args.output_dir, args.run_id, args.size_mib, args.rounds, args.repetitions)
    summary = evaluate(args.output_dir, args.run_id, args.rounds, args.repetitions, args.size_mib)
    result = args.output_dir / "summary.json"
    result.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"Validated {summary['sampleCount']} rounds over {len(CASES)} isolated scenarios; "
          f"{summary['decision']}; summary={result}")


if __name__ == "__main__":
    main()
