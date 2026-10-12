#!/usr/bin/env python3
"""Frozen CORE011-CSM-001 multi-CBLK metadata-only runner and fail-closed evaluator."""

import argparse
import hashlib
import json
import math
import statistics
import subprocess
from pathlib import Path

SCHEMA = "chunkshift.core011.csm-multiblock-metadata.v1"
OPS = ("manifest-verify", "enumerate")
# Eight factorial, same-worker cells (suite x BIDX x operation), then
# concurrency on the same BLAKE3/BIDX=true fixture at 4 and 16 workers.
CASES = tuple(
    (suite, bidx, 1, operation)
    for suite in ("blake3", "sha256")
    for bidx in (False, True)
    for operation in OPS
) + tuple(
    ("blake3", True, workers, operation)
    for workers in (4, 16)
    for operation in OPS
)


def validate(directory, run_id, size_mib, rounds, repetitions, min_chunks):
    paths = sorted(directory.glob("case-*.json"))
    if len(paths) != len(CASES):
        raise ValueError(f"expected {len(CASES)} cases, found {len(paths)}")
    found = {}
    content_hash = None
    manifest_hash_by_options = {}
    file_sha = {}
    checkout_sha = None
    pr_head_sha = None

    for path in paths:
        raw = path.read_bytes()
        row = json.loads(raw)
        if row.get("schema") != SCHEMA or row.get("experimentId") != "CORE011-CSM-001":
            raise ValueError(f"wrong experiment/schema in {path.name}")
        if row.get("runId") != run_id or not row.get("checkoutSha"):
            raise ValueError("RunId or checkout identity mismatch")
        if checkout_sha is None:
            checkout_sha = row["checkoutSha"]
            pr_head_sha = row.get("pullRequestHeadSha")
        elif (checkout_sha != row["checkoutSha"] or
              pr_head_sha != row.get("pullRequestHeadSha")):
            raise ValueError("different source revisions in same matrix")
        c = row.get("conditions", {})
        key = (c.get("suite"), c.get("includeBlockIndex"),
               c.get("workers"), c.get("operation"))
        if key not in CASES or key in found:
            raise ValueError("duplicate or unexpected case")
        if (c.get("rounds") != rounds or c.get("repetitions") != repetitions or
                c.get("minChunks") != min_chunks or
                c.get("memoryOnly") is not True or
                c.get("fixtureAndContentHashOutsideTimedRegion") is not True):
            raise ValueError("protocol controls were changed")

        f = row.get("fixture", {})
        if f.get("sizeMiB") != size_mib or f.get("seed") != "0xC4855A112026":
            raise ValueError("content fixture changed")
        if (f.get("profileId") != "fastcdc.gear.chunkshift.v1.64k" or
                f.get("payloadCollectedBeforeMeasurement") is not True):
            raise ValueError("source payload still retained or profile changed")
        chunks = f.get("chunkCount")
        if (type(chunks) is not int or chunks < min_chunks or
                f.get("minCblkBlocksBy4096EntryCap") != (chunks + 4095) // 4096 or
                f["minCblkBlocksBy4096EntryCap"] < 2):
            raise ValueError("multi-CBLK entry-cap threshold not crossed")
        if type(f.get("manifestBytes")) is not int or f["manifestBytes"] < 65536:
            raise ValueError("unexpectedly small multi-CBLK manifest")
        digest = f.get("contentSha256", "")
        manifest_digest = f.get("manifestSha256", "")
        if not all(
            len(h) == 64 and all(char in "0123456789abcdef" for char in h)
            for h in (digest, manifest_digest)
        ):
            raise ValueError("invalid content or physical manifest SHA-256")
        if content_hash is None:
            content_hash = digest
        elif digest != content_hash:
            raise ValueError("not the same input corpus")
        identity = (manifest_digest, f["manifestBytes"], chunks)
        option = (key[0], key[1])
        if option in manifest_hash_by_options and manifest_hash_by_options[option] != identity:
            raise ValueError("manifest identity differs for identical suite/BIDX options")
        manifest_hash_by_options[option] = identity

        samples = row.get("samples")
        if not isinstance(samples, list) or len(samples) != rounds:
            raise ValueError("incomplete round count")
        for i, sample in enumerate(samples, 1):
            if sample.get("round") != i:
                raise ValueError("missing/duplicated round")
            for field in ("wallMilliseconds", "processCpuMilliseconds"):
                v = sample.get(field)
                if type(v) not in (int, float) or not math.isfinite(v) or v < 0:
                    raise ValueError("invalid latency/CPU metric")
            fields = (
                "processAllocatedBytes", "managedHeapBeforeBytes",
                "managedHeapAfterBytes", "workingSetAfterBytes",
                "processLifetimePeakWorkingSetBytes", "gen0Collections",
                "gen1Collections", "gen2Collections", "lastGcLohSizeAfterBytes",
                "lastGcIndex", "completedOperations", "validatedChunks",
                "maxActiveOperations",
            )
            if any(type(sample.get(field)) is not int or sample[field] < 0
                   for field in fields):
                raise ValueError("invalid memory/GC/counter data")
            if (sample["completedOperations"] != key[2] * repetitions or
                    sample["validatedChunks"] != chunks * key[2] * repetitions or
                    not 1 <= sample["maxActiveOperations"] <= key[2]):
                raise ValueError("incomplete reader, missed chunks or missing workers")
        retention = row.get("retention", {})
        for field in ("managedBytesAfterExplicitGc", "lastGcLohSizeAfterBytes",
                      "lastGcHeapSizeBytes", "lastGcIndex",
                      "workingSetAfterGcBytes"):
            if type(retention.get(field)) is not int or retention[field] < 0:
                raise ValueError("missing post-timing retention counter")
        if (row.get("verdict") != "NO_VERDICT_EXPLORATORY" or
                type(row.get("managedBytesAfterWarmupGc")) is not int or
                type(row.get("lohSizeAfterWarmupGcBytes")) is not int):
            raise ValueError("unexpected decision or GC baseline")
        found[key] = row
        file_sha[path.name] = hashlib.sha256(raw).hexdigest()

    report = []
    for suite, bidx, workers, op in CASES:
        key = (suite, bidx, workers, op)
        samples = found[key]["samples"]
        wall = [s["wallMilliseconds"] for s in samples]
        center = statistics.median(wall)
        report.append({
            "suite": suite, "bidx": bidx, "workers": workers,
            "operation": op,
            "chunks": found[key]["fixture"]["chunkCount"],
            "manifestBytes": found[key]["fixture"]["manifestBytes"],
            "wallMedianMs": center,
            "wallMADMs": statistics.median(abs(t - center) for t in wall),
            "wallMinMs": min(wall),
            "wallMaxMs": max(wall),
            "allocationMedianBytes": statistics.median(
                s["processAllocatedBytes"] for s in samples),
            "gen2DuringTimedRounds": sum(s["gen2Collections"] for s in samples),
            "lohSizeAfterWarmupGcBytes": found[key]["lohSizeAfterWarmupGcBytes"],
            "lohSizeAfterExplicitPostGcBytes":
                found[key]["retention"]["lastGcLohSizeAfterBytes"],
            "postGcManagedBytes":
                found[key]["retention"]["managedBytesAfterExplicitGc"],
            "samples": samples,
        })
    return {
        "schema": "chunkshift.core011.csm-multiblock-summary.v1",
        "experimentId": "CORE011-CSM-001",
        "runId": run_id, "checkoutSha": checkout_sha,
        "pullRequestHeadSha": pr_head_sha,
        "contentSha256": content_hash,
        "caseCount": len(CASES), "sampleCount": len(CASES) * rounds,
        "minChunks": min_chunks,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "limitations": [
            "LOH-last-GC-size-is-not-LOH-allocation-volume",
            "post-GC-retention-cannot-identify-specific-buffer-owner",
            "managed-heap-snapshots-are-process-level",
            "two-rounds-are-diagnostic-only",
            "OS-file-IO-unmeasured; source-released-before-timing",
            "worker-overlap-not-equivalent-to-CPU-parallelism",
        ],
        "rawScenarioSha256": file_sha,
        "lanes": report,
    }


def run(directory, run_id, size_mib, rounds, repetitions, min_chunks):
    directory.mkdir(parents=True, exist_ok=True)
    for i, (suite, bidx, workers, operation) in enumerate(CASES):
        path = directory / f"case-{i:02d}.json"
        cmd = [
            "dotnet", "run", "--project",
            "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
            "-c", "Release", "--no-build", "--",
            "core011-csm-multiblock",
            "--size-mib", str(size_mib),
            "--min-chunks", str(min_chunks),
            "--rounds", str(rounds),
            "--repetitions", str(repetitions),
            "--suite", suite,
            "--bidx", str(bidx).lower(),
            "--workers", str(workers),
            "--operation", operation,
            "--run-id", run_id,
            "--output", str(path),
        ]
        print(f"[{i+1}/{len(CASES)}] {suite} bidx={bidx} workers={workers} operation={operation}", flush=True)
        subprocess.run(cmd, check=True)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output-dir", required=True, type=Path)
    p.add_argument("--run-id", required=True)
    p.add_argument("--size-mib", type=int, default=384)
    p.add_argument("--min-chunks", type=int, default=4097)
    p.add_argument("--rounds", type=int, default=2)
    p.add_argument("--repetitions", type=int, default=4)
    p.add_argument("--evaluate-only", action="store_true")
    a = p.parse_args()
    if (a.size_mib != 384 or a.min_chunks != 4097 or
            a.rounds != 2 or a.repetitions != 4):
        p.error("multiblock hosted experiment is frozen at 384MiB,4097,2,4")
    if not a.evaluate_only:
        run(a.output_dir, a.run_id, a.size_mib, a.rounds,
            a.repetitions, a.min_chunks)
    result = validate(a.output_dir, a.run_id, a.size_mib,
                      a.rounds, a.repetitions, a.min_chunks)
    destination = a.output_dir / "summary.json"
    destination.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Validated {result['sampleCount']} rounds / {result['caseCount']} "
          f"independent cases; {result['verdict']}")


if __name__ == "__main__":
    main()
