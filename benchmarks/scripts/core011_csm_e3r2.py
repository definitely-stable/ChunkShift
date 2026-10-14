#!/usr/bin/env python3
"""E3-R2-A: independent hosted, source-free, eager-only memory baseline pilot."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
E3_R0 = ROOT / "benchmarks/scripts/core011_csm_e3_guard.py"
G1J = ROOT / "benchmarks/scripts/core011_csm_g1j_contract.py"
CONTRACT = ROOT / "benchmarks/experiments/core011-csm-e3r2-calibration.v1.json"
R0_PARENT = "58fe8b8c9e5eb90f17fecddd218b5bf366d0f106"
READERSHA = "a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
RUNID = re.compile(r"^CORE011-CSM-004/RUN-[0-9]{8}-[0-9]+-[0-9a-f]{8}-r[01]-linux-x64$")
CELLS = ((1, 1, 30), (1, 16, 30), (384, 1, 30), (384, 16, 300))
REQUIRED_ROUND = ("allocatedBytes", "allocatedBytesPerOperation", "gcPauseMs",
                  "gen2", "wallMs", "cpuMs", "workingSetBytes",
                  "peakWorkingSetBytes", "privateBytes", "maxActiveReaders")
METRICS = [
    "allocatedBytes", "allocatedBytesPerOperation", "gcPauseMs", "gen2",
    "wallMs", "cpuMs", "workingSetBytes", "peakWorkingSetBytes",
    "privateBytes", "maxActiveReaders",
]


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


e3 = load_module(E3_R0, "e3r2_contract_parent")
g1j = load_module(G1J, "e3r2_g1j_fixture_preflight")


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(cond, message):
    if not cond:
        raise ValueError(message)


def calibration_contract(obj):
    require(obj.get("schema") == "chunkshift.core011.csm-e3r2-calibration.v1" and
            obj.get("experimentId") == "CORE011-CSM-004" and
            obj.get("ownerIssue") == 154 and
            obj.get("protocolParentSha") == R0_PARENT and
            obj.get("mode") == "baseline-only" and obj.get("variant") == "eager" and
            obj.get("decisionRunsAllowed") is False and obj.get("budgetsLocked") is False,
            "R2 baseline-only decision gate changed")
    require(obj.get("builder") ==
            "CsmG1jIsolatedHarness.Build via external builder OS process" and
            obj.get("builderProvenance") ==
            "synthetic; g1j helper builderRunId is not a decision run" and
            obj.get("runnerPlatform") == "ubuntu-24.04" and
            obj.get("replicates") == [0, 1] and
            obj.get("sizesMiB") == [1, 384] and
            obj.get("workers") == [1, 16] and
            obj.get("rounds") == 8 and
            obj.get("repetitionsBySize") == {"1": 64, "384": 8} and
            obj.get("idleSecondsByCell") ==
            {"1-1": 30, "1-16": 30, "384-1": 30, "384-16": 300} and
            obj.get("hashSuite") == "blake3" and obj.get("bidx") is True,
            "R2 calibration scenarios changed")
    require(obj.get("requiredMetrics") == METRICS and
            obj.get("requiredNaturalIdlePhases") == ["idle-0-natural", "idle-30-natural"] and
            obj.get("requiredLongIdlePhase") == "idle-300-natural",
            "R2 metric/idle monitoring changed")
    require(obj.get("mandatoryIsolation") == {
        "distinctBuilderAndConsumerPid": True,
        "sourceFileAbsent": True,
        "independentFreshConsumerPerCell": True,
        "exactCsmSha256": True,
        "checkoutShaPinned": True,
        "readerUnmodified": True,
    }, "R2 builder and consumer isolation weakened")
    require(obj.get("forbidden") == {
        "candidateMeasurements": True, "decisionThresholds": True,
        "productionChanges": True, "conflationWithG1JDecision": True,
    }, "R2 no-decision guard weakened")
    require(type(obj.get("limitations")) is list and len(obj["limitations"]) == 3,
            "R2 data limitations removed")
    return {"mode": "baseline-only", "decisionRunsAllowed": False}


def positive_number(value, field, *, allow_zero=False):
    require(type(value) in (int, float) and math.isfinite(value) and
            (value >= 0 if allow_zero else value > 0),
            "missing/nonfinite R2 metric " + field)


def validate_cell(obj, *, run_id, checkout, replicate, descriptor, workers, idle):
    require(obj.get("schema") == "chunkshift.core011.e3r2-baseline-cell.v1" and
            obj.get("experimentId") == "CORE011-CSM-004" and
            obj.get("runId") == run_id and
            obj.get("checkoutSha") == checkout and
            obj.get("variant") == "eager" and
            obj.get("role") == "fresh-source-free-consumer" and
            obj.get("verdict") == "BASELINE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT",
            "non-baseline or counterfeit R2 consumer")
    require(obj.get("builderRunId") == descriptor["runId"] and
            obj.get("builderPid") == descriptor["builderPid"] and
            type(obj.get("consumerPid")) is int and
            obj["consumerPid"] > 0 and obj["consumerPid"] != obj["builderPid"] and
            obj.get("consumerCreatedSource") is False and
            obj.get("sourceFileExists") is False,
            "R2 consumer builder/source separation failed")
    require(obj.get("fixtureSha256") == descriptor["manifestSha256"] and
            obj.get("fixtureBytes") == descriptor["manifestBytes"] and
            obj.get("sourceContentBytes") == descriptor["contentBytes"] and
            obj.get("chunks") == descriptor["chunkCount"] and
            obj.get("cblkCount") == descriptor["cblkCount"] and
            obj.get("suite") == descriptor["hashSuite"] and
            obj.get("bidx") == descriptor["hasBlockIndex"] and
            obj.get("workers") == workers and obj.get("idleSeconds") == idle and
            obj.get("rounds") == 8 and
            obj.get("repetitionsPerWorker") ==
            (64 if descriptor["contentBytes"] == 1048576 else 8),
            "R2 fixture or cell identity mismatch")
    require(isinstance(obj.get("runtime"), str) and obj["runtime"] and
            isinstance(obj.get("os"), str) and obj["os"] and
            obj.get("architecture") == "X64" and
            type(obj.get("environmentProcessorCount")) is int and
            obj["environmentProcessorCount"] >= 1,
            "R2 runtime/host identity missing")
    rounds = obj.get("samples")
    require(type(rounds) is list and len(rounds) == 8, "missing R2 baseline rounds")
    for index, sample in enumerate(rounds):
        require(sample.get("round") == index and
                sample.get("operations") == workers * obj["repetitionsPerWorker"] and
                type(sample.get("maxActiveReaders")) is int and
                1 <= sample["maxActiveReaders"] <= workers,
                "R2 batch completed work/concurrency mismatch")
        for key in REQUIRED_ROUND:
            positive_number(sample.get(key), key, allow_zero=key in
                            ("allocatedBytes", "gen2", "gcPauseMs", "cpuMs", "privateBytes"))
    snapshots = obj.get("snapshots")
    expected = ["fixture-loaded", "after-three-warmups",
                "idle-0-natural", "idle-30-natural"]
    if idle == 300:
        expected.append("idle-300-natural")
    expected.append("forced-gc-diagnostic-not-natural-idle")
    require(type(snapshots) is list and
            [x.get("phase") for x in snapshots] == expected,
            "natural-idle/forced-GC samples missing or conflated")
    previous_uptime = -1
    for snap in snapshots:
        for key in ("workingSetBytes", "peakWorkingSetBytes",
                    "processPrivateBytes", "managedHeapBytes",
                    "lastGcHeapSizeBytes", "lastGcLohSizeBytes",
                    "totalGcPauseMs", "uptimeMs"):
            positive_number(snap.get(key), key, allow_zero=True)
        require(snap["peakWorkingSetBytes"] >= snap["workingSetBytes"],
                "peak smaller than observed working set")
        require(snap["uptimeMs"] >= previous_uptime, "nonmonotonic runtime clock")
        previous_uptime = snap["uptimeMs"]
    require(snapshots[3]["uptimeMs"] - snapshots[2]["uptimeMs"] >= 29000,
            "30-second natural idle not actually observed")
    if idle == 300:
        require(snapshots[4]["uptimeMs"] - snapshots[2]["uptimeMs"] >= 295000,
                "300-second natural idle not actually observed")
    return {
        "replicate": replicate, "sizeMiB": descriptor["contentBytes"] // (1024 * 1024),
        "workers": workers, "consumerPid": obj["consumerPid"],
        "builderPid": obj["builderPid"],
        "fixtureSha256": obj["fixtureSha256"],
        "allocationBytesPerOperation": [s["allocatedBytesPerOperation"] for s in rounds],
        "cpuMs": [s["cpuMs"] for s in rounds],
        "wallMs": [s["wallMs"] for s in rounds],
        "gen2": [s["gen2"] for s in rounds],
        "gcPauseMs": [s["gcPauseMs"] for s in rounds],
        "idleWorkingSetBytes": [s["workingSetBytes"] for s in snapshots if
                                s["phase"].startswith("idle-")],
        "idlePhases": [s["phase"] for s in snapshots if s["phase"].startswith("idle-")],
    }


def command(args, cwd, log, *, timeout=1100):
    with log.open("w", encoding="utf-8") as out:
        try:
            p = subprocess.run([str(x) for x in args], cwd=cwd,
                               stdout=out, stderr=subprocess.STDOUT,
                               timeout=timeout, check=False)
        except subprocess.TimeoutExpired as exc:
            raise ValueError("R2 process timed out: " + str(log)) from exc
    if p.returncode:
        raise ValueError("R2 process exit " + str(p.returncode) +
                         "\n" + "\n".join(log.read_text(errors="replace").splitlines()[-35:]))
    print("PASS " + " ".join(str(x) for x in args), flush=True)


def preflight(repo, replicate, run_id):
    require(os.environ.get("GITHUB_ACTIONS") == "true",
            "R2 source-free calibration is GitHub-hosted only")
    require(replicate in (0, 1) and RUNID.fullmatch(run_id),
            "invalid E3-R2 run ID or replicate")
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo,
                                   text=True).strip()
    require(os.environ.get("GITHUB_SHA") == head and run_id.split("-")[-4] ==
            head[:8], "unfrozen R2 checkout")
    subprocess.run(["git", "merge-base", "--is-ancestor", R0_PARENT, head],
                   cwd=repo, check=True, timeout=30)
    obj = json.loads(CONTRACT.read_text())
    calibration_contract(obj)
    r0 = json.loads(
        (repo / "benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
    e3.validate_contract(r0)
    e3.check_historical_contract(repo)
    original = (repo / r0["baseline"]["readerPath"]).read_bytes()
    e3.validate_source_pin(r0, original)
    require(hashlib.sha256(original).hexdigest() == READERSHA,
            "source reader changed during baseline-only calibration")
    return head


def summarize(output, run_id, checkout, replicate):
    fixtures = {}
    records = []
    consumer_pids = set()
    for size in (1, 384):
        fixture = output / ("fixture-" + str(size))
        descriptor = json.loads((fixture / "fixture.json").read_text())
        g1j.validate_manifest_only_fixture(descriptor, fixture)
        require(descriptor["builderCheckoutSha"] == checkout and
                descriptor["hashSuite"] == "blake3" and
                descriptor["hasBlockIndex"] is True and
                descriptor["contentBytes"] == size * 1048576 and
                not (fixture / "source.bin").exists(),
                "R2 builder provenance or source-free boundary failed")
        if size == 384:
            require(descriptor["chunkCount"] == 5052 and
                    descriptor["cblkCount"] == 2,
                    "real two-CBLK physical fixture disappeared")
        fixtures[size] = descriptor
    for size, workers, idle in CELLS:
        name = str(size) + "-w" + str(workers)
        result_file = output / (name + ".json")
        obj = json.loads(result_file.read_text())
        lane = validate_cell(obj, run_id=run_id, checkout=checkout,
                             replicate=replicate, descriptor=fixtures[size],
                             workers=workers, idle=idle)
        require(lane["consumerPid"] not in consumer_pids,
                "consumer process unexpectedly reused")
        consumer_pids.add(lane["consumerPid"])
        lane["rawSha256"] = sha(result_file)
        records.append(lane)
    require(len(records) == 4 and len(consumer_pids) == 4,
            "missing independent process cells")
    return {
        "schema": "chunkshift.core011.e3r2-baseline-summary.v1",
        "experimentId": "CORE011-CSM-004", "runId": run_id,
        "checkoutSha": checkout, "replicate": replicate,
        "mode": "baseline-only", "decisionRunsAllowed": False,
        "budgetsLocked": False, "verdict": "CALIBRATION_ONLY_NO_PRODUCT_VERDICT",
        "builderCount": 2, "consumerCount": 4, "cells": records,
        "limitations": [
            "Synthetic corpus and eager baseline only; zero candidate measurements.",
            "Four separate fresh processes, two separate builder processes, per independent hosted runner.",
            "No numeric budget chosen or ADOPT/REJECT decision; frozen R0 remains preregistration.",
            "Linux Process.PrivateMemorySize64 not interchangeable with private resident pages.",
            "Only 384MiB/16-worker cell observes natural idle to 300 seconds.",
        ],
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--output-dir", required=True, type=Path)
    ap.add_argument("--replicate", required=True, type=int, choices=(0, 1))
    ap.add_argument("--run-id", required=True)
    ap.add_argument("--evaluate-only", action="store_true")
    args = ap.parse_args()
    out = args.output_dir.resolve()
    repo = Path.cwd()
    try:
        if not args.evaluate_only:
            checkout = preflight(repo, args.replicate, args.run_id)
            out.mkdir(parents=True, exist_ok=False)
            project = "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"
            command(["dotnet", "restore", project], repo, out / "restore.log", timeout=600)
            command(["dotnet", "build", project, "-c", "Release", "--no-restore"],
                    repo, out / "build.log", timeout=600)
            prefix = ["dotnet", "run", "--no-build", "-c", "Release", "--project",
                      project, "--"]
            for size in (1, 384):
                fixture = out / ("fixture-" + str(size))
                fixture.mkdir()
                builder_id = ("CORE011-CSM-001/G1J1-E3R2-" + str(size) +
                              "-r" + str(args.replicate) + "-" + str(os.getpid()))
                command(prefix + [
                    "core011-g1j1-build", "--output-dir", fixture,
                    "--size-mib", size, "--suite", "blake3",
                    "--bidx", "true", "--run-id", builder_id
                ], repo, out / ("builder-" + str(size) + ".log"), timeout=1000)
                descriptor = json.loads((fixture / "fixture.json").read_text())
                g1j.validate_manifest_only_fixture(descriptor, fixture)
                require(not (fixture / "source.bin").exists(), "source leaked from builder")
                for csize, workers, idle in CELLS:
                    if csize != size:
                        continue
                    command(prefix + [
                        "core011-e3r2-baseline", "--fixture-dir", fixture,
                        "--output", out / (str(size) + "-w" + str(workers) + ".json"),
                        "--workers", workers, "--repetitions", 64 if size == 1 else 8,
                        "--idle-seconds", idle, "--run-id", args.run_id,
                    ], repo, out / (str(size) + "-w" + str(workers) + ".log"), timeout=750)
        else:
            checkout = subprocess.check_output(["git", "rev-parse", "HEAD"],
                                               cwd=repo, text=True).strip()
        result = summarize(out, args.run_id, checkout, args.replicate)
        (out / "summary.json").write_text(json.dumps(result, indent=2) + "\n")
        print("E3-R2 BASELINE-ONLY PASS 2 builders / 4 fresh consumers / "
              "32 rounds / no product verdict", flush=True)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print("E3-R2 FAIL-CLOSED: " + str(error), file=sys.stderr)
        raise SystemExit(2) from error


if __name__ == "__main__":
    main()
