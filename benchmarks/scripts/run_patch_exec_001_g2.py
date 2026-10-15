#!/usr/bin/env python3
"""PATCH-EXEC-001 G2-A real update-set calibration, never production decision."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import statistics
import sys

from compare_patch_batch_create import compare
from run_patch_exec_001_pilot import CASES, round_order, run_command, runner_command
from prepare_patch_exec_001_g2 import (EXPECTED_SELECTED_SHA256, FROZEN_PAIRS_SHA256,
                                       SCHEMA as POPULATION_SCHEMA)

SCHEMA = "chunkshift.patch-exec-001-g2a-real-calibration.v1"
ROUNDS = 5
MODALITIES = ("warm-csm", "cold-csm")


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def execute(args):
    if args.modality not in MODALITIES:
        raise ValueError("unsupported modality")
    commit = os.environ.get("GITHUB_SHA", "")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError("G2-A requires exact checked-out GitHub commit")
    if os.environ.get("GITHUB_REF") != "refs/heads/main":
        raise ValueError("real G2-A is authorized only on main")
    if os.environ.get("GITHUB_RUN_ATTEMPT") != "1":
        raise ValueError("rerun is not accepted as a new first-attempt calibration")
    root = Path(args.subset).resolve()
    output = Path(args.output).resolve()
    if output.exists():
        raise ValueError("G2-A output directory must not exist")
    population_path = root / "selection.json"
    population = json.loads(population_path.read_text(encoding="utf-8"))
    if (population.get("schema") != POPULATION_SCHEMA or
            population.get("role") != "train" or
            population.get("frozenOriginalPairsSha256") != FROZEN_PAIRS_SHA256 or
            population.get("fileCount") != 16 or
            population.get("baseAndTargetFilesVerified") != 32):
        raise ValueError("not a frozen 16-file G2-A training population")
    selected_digest = digest(root / "pairs.json")
    if selected_digest != EXPECTED_SELECTED_SHA256["train"] or selected_digest != population.get("selectedPairsSha256"):
        raise ValueError("G2-A selected pairs digest mismatch")
    project = Path(args.project).resolve()
    if not project.is_file():
        raise ValueError("missing real benchmark project")
    output.mkdir(parents=True, exist_ok=False)
    run_id = (f"PATCH-EXEC-001/G2A/{args.modality}/"
              f"{os.environ['GITHUB_RUN_ID']}/{commit}")
    oracle_path = output / "apply-verified-h0.json"
    run_command(runner_command(project, root, oracle_path, "h0", 1, False, run_id))
    oracle = json.loads(oracle_path.read_text(encoding="utf-8"))
    if oracle.get("corpusPairsSha256") != selected_digest or len(oracle.get("files", [])) != 16:
        raise ValueError("wrong oracle cohort or population digest")
    results = []
    for round_index in range(ROUNDS):
        paths = {}
        for name, execution, outer in round_order(round_index):
            if args.modality == "cold-csm":
                # CSM cache only; do not claim cleared OS page cache or disk.
                work_dir = root / "work"
                if work_dir.exists():
                    shutil.rmtree(work_dir)
                if work_dir.exists():
                    raise ValueError("cold-csm manifest cache could not be removed")
            path = output / f"r{round_index}-{name}.json"
            run_command(runner_command(project, root, path, execution, outer, True, run_id))
            paths[name] = path
        report = compare(oracle_path, paths["f1-h0"],
                         [paths[name] for name, _, _ in CASES if name != "f1-h0"])
        if report["fileCount"] != 16:
            raise ValueError("incomplete per-file H0 comparison")
        samples = {}
        for name, _, outer in CASES:
            data = json.loads(paths[name].read_text(encoding="utf-8"))
            if data.get("corpusPairsSha256") != selected_digest or data["workers"] != outer:
                raise ValueError("unexpected G2-A file cohort or outer worker config")
            metrics = data["batchCreate"]
            samples[name] = {
                "seconds": data["elapsedSeconds"],
                "cpuSeconds": metrics["processCpuSeconds"],
                "allocatedBytes": metrics["processAllocatedBytes"],
                "processLifetimePeakWorkingSetBytes": metrics["processPeakWorkingSetBytes"],
                "raw": paths[name].name,
            }
        results.append({"round": round_index,
                        "order": [row[0] for row in round_order(round_index)],
                        "comparisonStatus": report["comparisonStatus"], "cases": samples})
    totals = {}
    for name, execution, outer in CASES:
        walls = [r["cases"][name]["seconds"] for r in results]
        cpus = [r["cases"][name]["cpuSeconds"] for r in results]
        rss = [r["cases"][name]["processLifetimePeakWorkingSetBytes"] for r in results]
        totals[name] = {
            "execution": execution, "outerFiles": outer,
            "medianWallSeconds": statistics.median(walls),
            "minWallSeconds": min(walls), "maxWallSeconds": max(walls),
            "medianCpuSeconds": statistics.median(cpus),
            "maxProcessLifetimePeakWorkingSetBytes": max(rss),
        }
    # This is a FIXED-LANE calibration: selecting its best result after
    # inspecting performance does NOT constitute an adaptive-scheduler gain.
    fastest = min(totals, key=lambda name: totals[name]["medianWallSeconds"])
    baseline = totals["f1-h2w2"]["medianWallSeconds"]
    verdict = {
        "schema": SCHEMA, "status": "UNSEALED_REAL_FIXED_LANES_CALIBRATION_NO_ADOPT",
        "scope": "manifest-hash-plan-create-readback-sha256",
        "population": population, "populationSha256": selected_digest,
        "frozenSourcePairsSha256": FROZEN_PAIRS_SHA256,
        "modality": args.modality, "rounds": ROUNDS, "gitCommit": commit,
        "githubRunId": os.environ["GITHUB_RUN_ID"],
        "githubRunAttempt": os.environ["GITHUB_RUN_ATTEMPT"],
        "comparisons": "all 16 physical CSPs match apply-verified H0 in every case",
        "coldCsmDefinition": "delete only manifest-cache directory between batch cases",
        "memoryCaveat": "process lifetime high-water, NOT per-create idle-subtracted RSS",
        "comparisonCaveat": "selection after timing does not beat best fixed safe baseline",
        "bestObservedFixedCase": fastest,
        "observedImprovementVsF1H2W2": 1 - totals[fastest]["medianWallSeconds"] / baseline,
        "cases": totals, "samples": results,
    }
    # Successful output is atomic at the report level: no result until all 30
    # batch cases and all oracle comparisons are complete.
    (output / "summary.json").write_text(json.dumps(
        verdict, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": verdict["status"],
                      "bestObservedFixedCase": fastest,
                      "cases": totals}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--subset", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--modality", choices=MODALITIES, required=True)
    execute(parser.parse_args())


if __name__ == "__main__":
    main()
