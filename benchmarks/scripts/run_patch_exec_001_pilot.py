#!/usr/bin/env python3
"""PATCH-EXEC-001 G1 synthetic-only same-host batch scheduling pilot.

Pre-decision calibration. Never claim production speedup or memory acceptance.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import sys

from compare_patch_batch_create import compare

SCHEMA = "chunkshift.patch-exec-001-g1-pilot.v1"
FAMILY = "exec-g1-synthetic"
CASES = (
    ("f1-h0", "h0", 1),
    ("f1-h2w2", "h2-w2", 1),
    ("f2-h0", "h0", 2),
    ("f2-h2w2", "h2-w2", 2),
    ("f4-h0", "h0", 4),
    ("f4-h2w2", "h2-w2", 4),
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def round_order(round_index):
    """An order-inverted schedule, with later rotations to reduce drift."""
    order = list(CASES)
    shift = (round_index // 2) % len(order)
    order = order[shift:] + order[:shift]
    if round_index % 2:
        order.reverse()
    return order


def generate_corpus(root, file_count, bytes_per_file):
    """Materialize fully deterministic independent source/target file pairs."""
    if not 2 <= file_count <= 128 or not 131072 <= bytes_per_file <= 1048576:
        raise ValueError("pilot requires 2..128 files of 131072..1048576 bytes")
    root = Path(root)
    root.mkdir(parents=True, exist_ok=False)
    changed = []
    for i in range(file_count):
        key = f"PATCH-EXEC-001-G1/v1/{i:04d}".encode("ascii")
        if i % 3 == 0:
            unit = hashlib.shake_256(b"repeat-base/" + key).digest(128)
            original = (unit * ((bytes_per_file + 127) // 128))[:bytes_per_file]
        elif i % 3 == 1:
            original = hashlib.shake_256(b"random-base/" + key).digest(bytes_per_file)
        else:
            unit = hashlib.shake_256(b"mixed-base/" + key).digest(4096)
            original = (unit * ((bytes_per_file + 4095) // 4096))[:bytes_per_file]
        left, right = bytes_per_file // 4, bytes_per_file * 3 // 4
        replacement = hashlib.shake_256(b"target-middle/" + key).digest(right - left)
        target = original[:left] + replacement + original[right:]
        relative = f"bin/{i:04d}.bin"
        for version, data in (("v1", original), ("v2", target)):
            path = root / "tree" / FAMILY / version / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        changed.append({
            "path": relative,
            "baseSize": len(original),
            "baseSha256": sha(original),
            "targetSize": len(target),
            "targetSha256": sha(target),
        })
    pairs = {
        "schema": "chunkshift.patch-pairs.v1",
        "pairs": [{
            "family": FAMILY, "base": "v1", "target": "v2",
            "changed": changed, "added": [], "removed": [],
            "identicalFiles": 0, "identicalBytes": 0,
        }],
    }
    payload = (json.dumps(pairs, sort_keys=True, indent=2) + "\n").encode("utf-8")
    (root / "pairs.json").write_bytes(payload)
    return sha(payload)


def runner_command(project, corpus, output, execution, outer, batch, run_id):
    command = [
        "dotnet", "run", "--project", str(project), "-c", "Release",
        "--no-build", "--", "patch-lab", "run",
        "--corpus", str(corpus), "--work", str(corpus / "work"),
        "--lane", "csp", "--output", str(output),
        "--execution", execution, "--workers", str(outer),
        "--run-id", run_id,
    ]
    command += (["--no-apply", "--batch-create"] if batch
                else ["--apply-repeats", "1", "--apply-check-only"])
    return command


def run_command(command):
    proc = subprocess.run(command, capture_output=True, text=True, check=False)
    if proc.returncode:
        raise RuntimeError(
            f"patch-lab exit {proc.returncode}: {command!r}\n"
            f"stdout tail:\n{proc.stdout[-5000:]}\nstderr tail:\n{proc.stderr[-5000:]}")
    if not Path(command[command.index("--output") + 1]).is_file():
        raise RuntimeError("patch-lab exited successfully without writing output")


def execute(args):
    commit = os.environ.get("GITHUB_SHA", "")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError("pilot requires a 40-hex GitHub checkout SHA")
    output = Path(args.output).resolve()
    corpus = Path(args.corpus).resolve()
    if output.exists():
        raise ValueError("output directory must not exist (no stale results)")
    output.mkdir(parents=True)
    corpus_digest = generate_corpus(corpus, args.files, args.bytes)
    project = Path(args.project).resolve()
    if not project.is_file():
        raise ValueError("missing benchmark project")
    run_id = f"PATCH-EXEC-001-G1/{os.environ.get('GITHUB_RUN_ID', 'local')}/{commit}"
    oracle_path = output / "apply-verified-h0.json"
    run_command(runner_command(project, corpus, oracle_path, "h0", 1, False, run_id))
    raw_oracle = json.loads(oracle_path.read_text(encoding="utf-8"))
    if raw_oracle["corpusPairsSha256"] != corpus_digest:
        raise ValueError("corpus digest changed during H0 oracle execution")
    rounds = []
    raw_files = {}
    for round_index in range(args.rounds):
        paths = {}
        for name, execution, outer in round_order(round_index):
            path = output / f"r{round_index}-{name}.json"
            run_command(runner_command(
                project, corpus, path, execution, outer, True, run_id))
            paths[name] = path
        baseline = paths["f1-h0"]
        candidates = [paths[x[0]] for x in CASES if x[0] != "f1-h0"]
        # Reuse the independent fail-closed comparison, which validates
        # complete per-file physical CSP digests against apply-verified H0.
        report = compare(oracle_path, baseline, candidates)
        rows = {}
        for name, _, _ in CASES:
            data = json.loads(paths[name].read_text(encoding="utf-8"))
            if data["corpusPairsSha256"] != corpus_digest:
                raise ValueError("pilot observed a modified corpus")
            rows[name] = {
                "elapsedSeconds": data["elapsedSeconds"],
                "processCpuSeconds": data["batchCreate"]["processCpuSeconds"],
                "processAllocatedBytes": data["batchCreate"]["processAllocatedBytes"],
                "processLifetimeHighWaterBytes": data["batchCreate"]["processPeakWorkingSetBytes"],
                "relativeSpeedupVsF1H0": (
                    report["baselineWallSeconds"] / data["elapsedSeconds"]),
                "path": paths[name].name,
            }
        rounds.append({"round": round_index, "order": [x[0] for x in round_order(round_index)],
                       "cases": rows, "status": "COMPARED_EXACT_SHA"})
    summary = {
        "schema": SCHEMA, "status": "SYNTHETIC_CALIBRATION_ONLY_NO_DECISION",
        "gitCommit": commit, "githubRunId": os.environ.get("GITHUB_RUN_ID"),
        "githubRunAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "os": platform.platform(), "processorCount": os.cpu_count(),
        "runtime": sys.version, "corpusPairsSha256": corpus_digest,
        "files": args.files, "bytesPerFile": args.bytes,
        "roundCount": args.rounds, "warmCsmCache": True,
        "scope": "end-to-end-manifest-hash-plan-create-readback-patch-hash",
        "noClaim": "not an encoder-only speedup, peak RSS budget, or real update-set result",
        "rounds": rounds,
        "medians": {},
    }
    for name, _, _ in CASES:
        walls = [item["cases"][name]["elapsedSeconds"] for item in rounds]
        summary["medians"][name] = {
            "wallSeconds": statistics.median(walls),
            "minSeconds": min(walls), "maxSeconds": max(walls),
            "speedupVsF1H0Median": (
                statistics.median(item["cases"]["f1-h0"]["elapsedSeconds"]
                                  for item in rounds) / statistics.median(walls)),
        }
    # Only write a success summary after ALL subprocesses and comparators pass.
    (output / "summary.json").write_text(
        json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({"status": summary["status"], "medians": summary["medians"]}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--files", type=int, default=12)
    parser.add_argument("--bytes", type=int, default=393216)
    parser.add_argument("--rounds", type=int, default=2)
    args = parser.parse_args()
    if not 2 <= args.rounds <= 5:
        parser.error("rounds must be in 2..5 (synthetic exploratory limit)")
    execute(args)


if __name__ == "__main__":
    main()
