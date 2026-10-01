#!/usr/bin/env python3
"""Run PATCH-ENC-005's frozen five-round paired create procedure.

This is infrastructure, not a decision evaluator. It executes H0 brackets and
the requested frozen candidate lanes, records every raw PatchLab result/trace,
checks the 25% bracket-noise rule, and reports per-round wall/CPU ratios.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

FROZEN_PHASE_A = (
    "H4-L1-R2",
    "H7-L1-R2-E75",
    "H9-L9-K4-C16-R1M",
    "H9-L12-K4-C16-R1M",
    "H9-L15-K4-C16-R1M",
)


def rotated(lanes: list[str], round_index: int) -> list[str]:
    if not lanes:
        return []
    offset = round_index % len(lanes)
    return lanes[offset:] + lanes[:offset]


def bracket_mean(left: float, right: float) -> float:
    return (left + right) / 2.0


def bracket_is_noisy(left: float, right: float) -> bool:
    mean = bracket_mean(left, right)
    if mean <= 0:
        return True
    return abs(left - right) > 0.25 * mean


def patch_sha_map(result: dict) -> dict[tuple[str, str, str, str], str]:
    return {
        (item["family"], item["base"], item["target"], item["path"]): item["patchSha256"]
        for item in result["files"]
    }


def require_same_patch_bytes(label: str, left: dict, right: dict) -> None:
    if patch_sha_map(left) != patch_sha_map(right):
        raise ValueError(f"{label}: patch SHA-256 mismatch")


def aggregate(result: dict) -> dict[str, float | int]:
    files = result["files"]
    cpu = 0.0
    apply_wall = 0.0
    apply_cpu = 0.0
    apply_reads = 0
    apply_bytes = 0

    for item in files:
        metrics = item.get("createMetrics")
        if metrics is None:
            raise ValueError("paired timing requires measured createMetrics")
        apply = item.get("applyMetrics")
        if apply is None:
            raise ValueError("paired timing requires measured five-repeat applyMetrics")
        samples = apply.get("samples")
        if not isinstance(samples, list) or len(samples) != 5:
            raise ValueError("paired timing requires exactly five apply samples per file")

        cpu += float(metrics["cpuSeconds"])
        apply_wall += float(apply["medianWallSeconds"])
        apply_cpu += float(apply["medianCpuSeconds"])
        apply_reads += int(apply["medianBaseReads"])
        apply_bytes += int(apply["medianBaseBytesRead"])

    return {
        "wallSeconds": sum(float(item["createSeconds"]) for item in files),
        "cpuSeconds": cpu,
        "patchBytes": sum(int(item["patchBytes"]) for item in files),
        "applyWallSeconds": apply_wall,
        "applyCpuSeconds": apply_cpu,
        "applyBaseReads": apply_reads,
        "applyBaseBytesRead": apply_bytes,
    }


def invoke(
    args: argparse.Namespace,
    lane: str,
    name: str,
    *,
    traces: bool,
) -> tuple[Path, dict]:
    invocation = args.output / name
    invocation.mkdir(parents=True, exist_ok=False)
    output = invocation / "run.json"
    command = [
        args.dotnet,
        "run",
        "--project",
        str(args.project),
        "-c",
        "Release",
        "--no-build",
        "--",
        "patch-lab",
        "run",
        "--corpus",
        str(args.corpus),
        "--lane",
        lane,
        "--output",
        str(output),
        "--workers",
        "1",
        "--execution",
        "h2-w2",
        "--apply-repeats",
        "5",
        "--apply-check-only",
        "--run-id",
        args.run_id,
    ]

    if args.families:
        command += ["--families", args.families]

    if args.work:
        command += ["--work", str(args.work)]

    if traces:
        command += [
            "--trace-dir",
            str(invocation / "traces"),
            "--experiment-id",
            "PATCH-ENC-005",
            "--protocol-commit",
            args.protocol_commit,
            "--source-commit",
            args.source_commit,
            "--platform",
            args.platform,
            "--dataset-role",
            args.dataset_role,
        ]

    subprocess.run(command, check=True)
    return output, json.loads(output.read_text(encoding="utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--protocol-commit", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--platform", required=True)
    parser.add_argument(
        "--dataset-role",
        choices=("calibration", "evaluation", "confirmation"),
        required=True,
    )
    parser.add_argument("--families")
    parser.add_argument("--work", type=Path)
    parser.add_argument("--lanes", default=",".join(FROZEN_PHASE_A))
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument(
        "--project",
        type=Path,
        default=Path("benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"),
    )
    args = parser.parse_args()

    lanes = [lane.strip() for lane in args.lanes.split(",") if lane.strip()]
    unknown = [lane for lane in lanes if lane not in FROZEN_PHASE_A]

    if unknown or len(set(lanes)) != len(lanes) or not lanes:
        parser.error(
            "--lanes must be a non-empty unique subset of frozen Phase-A lanes; "
            f"invalid={unknown}"
        )
    if "H7-L1-R2-E75" in lanes and "H4-L1-R2" not in lanes:
        parser.error("H7-L1-R2-E75 requires H4-L1-R2 for the frozen byte oracle")

    args.output.mkdir(parents=True, exist_ok=False)

    # Warm-up is deliberately excluded from every recorded ratio.
    invoke(args, "csp", "warmup-h0", traces=False)
    rounds: list[dict] = []
    invalid = False
    expected_patch_shas: dict[str, dict[tuple[str, str, str, str], str]] = {}

    for round_index in range(5):
        round_number = round_index + 1
        start_path, start = invoke(
            args,
            "csp",
            f"round-{round_number}-h0-start",
            traces=True,
        )
        baseline_shas = patch_sha_map(start)
        if "csp" in expected_patch_shas and expected_patch_shas["csp"] != baseline_shas:
            raise ValueError("H0 patch SHA-256 changed across measured rounds")
        expected_patch_shas.setdefault("csp", baseline_shas)
        candidates: list[dict] = []
        candidate_results: dict[str, dict] = {}

        for lane in rotated(lanes, round_index):
            path, result = invoke(
                args,
                lane,
                f"round-{round_number}-{lane}",
                traces=True,
            )
            lane_shas = patch_sha_map(result)
            if lane in expected_patch_shas and expected_patch_shas[lane] != lane_shas:
                raise ValueError(f"{lane}: patch SHA-256 changed across measured rounds")
            expected_patch_shas.setdefault(lane, lane_shas)
            candidate_results[lane] = result
            candidates.append(
                {
                    "lane": lane,
                    "result": str(path.relative_to(args.output)),
                    "aggregate": aggregate(result),
                }
            )

        end_path, end = invoke(
            args,
            "csp",
            f"round-{round_number}-h0-end",
            traces=True,
        )
        require_same_patch_bytes("H0 bracket", start, end)
        if "H7-L1-R2-E75" in candidate_results:
            require_same_patch_bytes(
                "H7/H4 frozen byte oracle",
                candidate_results["H4-L1-R2"],
                candidate_results["H7-L1-R2-E75"],
            )
        start_aggregate = aggregate(start)
        end_aggregate = aggregate(end)
        noisy = bracket_is_noisy(
            float(start_aggregate["wallSeconds"]),
            float(end_aggregate["wallSeconds"]),
        )
        invalid |= noisy
        wall_denominator = bracket_mean(
            float(start_aggregate["wallSeconds"]),
            float(end_aggregate["wallSeconds"]),
        )
        cpu_denominator = bracket_mean(
            float(start_aggregate["cpuSeconds"]),
            float(end_aggregate["cpuSeconds"]),
        )

        for candidate in candidates:
            aggregate_candidate = candidate["aggregate"]
            candidate["wallRatio"] = (
                float(aggregate_candidate["wallSeconds"]) / wall_denominator
            )
            candidate["cpuRatio"] = (
                float(aggregate_candidate["cpuSeconds"]) / cpu_denominator
                if cpu_denominator > 0
                else None
            )

        rounds.append(
            {
                "round": round_number,
                "candidateOrder": [candidate["lane"] for candidate in candidates],
                "h0StartResult": str(start_path.relative_to(args.output)),
                "h0EndResult": str(end_path.relative_to(args.output)),
                "h0Start": start_aggregate,
                "h0End": end_aggregate,
                "bracketNoisy": noisy,
                "candidates": candidates,
            }
        )

    document = {
        "schema": "chunkshift.patch-enc-005-paired.v1",
        "experimentId": "PATCH-ENC-005",
        "runId": args.run_id,
        "protocolCommit": args.protocol_commit.lower(),
        "sourceCommit": args.source_commit.lower(),
        "platform": args.platform,
        "datasetRole": args.dataset_role,
        "rounds": rounds,
        "valid": not invalid,
        "invalidReason": "h0-bracket-noise" if invalid else None,
    }
    (args.output / "paired.json").write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    # A noisy dispatch is evidence-invalid, not a candidate failure.
    return 1 if invalid else 0


if __name__ == "__main__":
    sys.exit(main())
