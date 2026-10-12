#!/usr/bin/env python3
"""CORE011-CSM-001 G1-D launch on GitHub-hosted .NET 10, no local runner required."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0/ChunkShift.Benchmarks.dll"
ANALYZER = ROOT / "benchmarks/tools/ChunkShift.CsmTraceAnalyzer/bin/Release/net10.0/ChunkShift.CsmTraceAnalyzer.dll"
EXPECTED_CSM_SHA = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"


def execute(outdir: Path, trace_tool: Path, size: int, workers: int, run_id: str):
    outdir.mkdir(parents=True, exist_ok=True)
    min_chunks = 4097 if size == 384 else 1
    case = outdir / "scenario.json"
    trace = outdir / "trace.nettrace"
    evaluated = outdir / "event-summary.json"
    cmd = [
        str(trace_tool), "collect", "--profile", "gc-verbose",
        "--providers", "ChunkShift-Core011-CSM-Trace:0xFFFFFFFFFFFFFFFF:5",
        "--buffersize", "256", "--format", "NetTrace",
        "--output", str(trace), "--show-child-io", "--",
        "dotnet", "exec", str(APP), "core011-csm-multiblock",
        "--operation", "manifest-verify", "--suite", "blake3",
        "--bidx", "true", "--workers", str(workers),
        "--rounds", "3", "--repetitions", "4",
        "--size-mib", str(size), "--min-chunks", str(min_chunks),
        "--trace-events", "true", "--run-id", run_id,
        "--output", str(case),
    ]
    subprocess.run(cmd, cwd=ROOT, check=True, timeout=1800)
    if not case.is_file() or not trace.is_file() or trace.stat().st_size < 1024:
        raise ValueError("trace or exact CSM scenario absent/empty")
    subprocess.run(
        ["dotnet", "exec", str(ANALYZER), "--trace", str(trace),
         "--scenario", str(case), "--output", str(evaluated)],
        cwd=ROOT, check=True, timeout=120)
    scenario = json.loads(case.read_text(encoding="utf-8"))
    result = json.loads(evaluated.read_text(encoding="utf-8"))
    if scenario["conditions"]["workers"] != workers or len(scenario["samples"]) != 3:
        raise ValueError("scenario worker/round mismatch")
    if scenario["fixture"]["sizeMiB"] != size or not scenario["fixture"]["payloadCollectedBeforeMeasurement"]:
        raise ValueError("content not isolated")
    if size == 384:
        if (scenario["fixture"]["chunkCount"] != 5052 or
                scenario["fixture"]["manifestSha256"] != EXPECTED_CSM_SHA):
            raise ValueError("physical CSM identity drifted")
    if len(result["rounds"]) != 3 or result["eventsLost"] != 0:
        raise ValueError("incomplete EventPipe attribution")
    if result["positiveControl"]["lohTicks"] < 1:
        raise ValueError("positive control unexpectedly silent")
    return {
        "workerCount": workers, "sizeMiB": size,
        "chunks": scenario["fixture"]["chunkCount"],
        "runId": run_id,
        "nettraceSha256": hashlib.sha256(trace.read_bytes()).hexdigest(),
        "scenarioSha256": hashlib.sha256(case.read_bytes()).hexdigest(),
        "eventSummarySha256": hashlib.sha256(evaluated.read_bytes()).hexdigest(),
        "positiveControlLohTicks": result["positiveControl"]["lohTicks"],
        "roundLohTicks": [x["diagnostic"]["lohTicks"] for x in result["rounds"]],
        "rounds": 3, "repetitions": 4,
        "verdict": "NO_VERDICT_EXPLORATORY",
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output-dir", required=True, type=Path)
    p.add_argument("--trace-tool", required=True, type=Path)
    p.add_argument("--size-mib", choices=[1, 384], type=int, required=True)
    p.add_argument("--workers", choices=["1", "1,16"], required=True)
    p.add_argument("--run-id", required=True)
    a = p.parse_args()
    if not a.run_id.startswith("CORE011-CSM-001/"):
        p.error("RunId must belong to CORE011-CSM-001")
    if not APP.is_file() or not ANALYZER.is_file() or not a.trace_tool.is_file():
        p.error("Release binaries / pinned dotnet-trace tool not built")
    workers = [int(x) for x in a.workers.split(",")]
    report = []
    for count in workers:
        report.append(execute(a.output_dir / f"worker-{count}", a.trace_tool,
                              a.size_mib, count, a.run_id))
    output = {
        "schema": "chunkshift.core011.csm-loh-trace-run.v1",
        "runId": a.run_id,
        "checkoutSha": os.environ.get("GITHUB_SHA", "local"),
        "prHeadSha": os.environ.get("PR_HEAD_SHA"),
        "traceToolPinned": "10.0.750501",
        "analyzerPackagePinned": "3.2.8",
        "lanes": report,
        "verdict": "NO_VERDICT_EXPLORATORY",
    }
    (a.output_dir / "run-summary.json").write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print("Validated EventPipe trace lanes", len(report), flush=True)


if __name__ == "__main__":
    main()
