#!/usr/bin/env python3
"""Independently recompute PATCH-ENC-005 Phase-A calibration selection from compact.json."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
LANES = (
    "H4-L1-R2",
    "H7-L1-R2-E75",
    "H9-L9-K4-C16-R1M",
    "H9-L12-K4-C16-R1M",
    "H9-L15-K4-C16-R1M",
)
CREATE_BOUND_BYTES = 96 * 1024 * 1024
APPLY_BOUND_BYTES = 64 * 1024 * 1024


def recompute(compact: dict) -> dict:
    if compact.get("schema") != "chunkshift.patch-enc-005-phase-a-compact.v1":
        raise ValueError("unexpected compact schema")
    if compact.get("datasetRole") != "calibration":
        raise ValueError("only calibration evidence is accepted")

    patch_bytes = compact["patchBytes"]
    h0 = int(patch_bytes["csp"])
    if h0 <= 0:
        raise ValueError("H0 patch bytes must be positive")
    for platform in PLATFORMS:
        h0_memory = compact["platforms"][platform]["memory"]["csp"]
        if (
            int(h0_memory["createPeakOverIdleBytes"]) > CREATE_BOUND_BYTES
            or int(h0_memory["applyPeakOverIdleBytes"]) > APPLY_BOUND_BYTES
        ):
            raise ValueError(f"{platform}: H0 violates frozen memory bounds")

    rows = {}
    for lane in LANES:
        lane_patch_bytes = int(patch_bytes[lane])
        b = lane_patch_bytes / h0
        wall = {}
        cpu = {}
        apply = {}
        create_memory = {}
        apply_memory = {}
        for platform in PLATFORMS:
            platform_data = compact["platforms"][platform]
            timing = platform_data["timing"][lane]
            wall[platform] = float(timing["wallRatioMedian"])
            cpu[platform] = float(timing["cpuRatioMedian"])
            h0_apply = float(platform_data["applyWallSeconds"]["csp"])
            apply[platform] = float(platform_data["applyWallSeconds"][lane]) / h0_apply
            create_memory[platform] = int(
                platform_data["memory"][lane]["createPeakOverIdleBytes"]
            )
            apply_memory[platform] = int(
                platform_data["memory"][lane]["applyPeakOverIdleBytes"]
            )

        apply_ok = all(value <= 1.10 for value in apply.values())
        memory_ok = (
            all(value <= CREATE_BOUND_BYTES for value in create_memory.values())
            and all(value <= APPLY_BOUND_BYTES for value in apply_memory.values())
        )
        speed = lane_patch_bytes * 100 <= h0 * 102 and all(
            value <= 0.50 for value in wall.values()
        )
        size = lane_patch_bytes * 100 <= h0 * 97 and all(
            value <= 1.50 for value in wall.values()
        )
        branches = [name for name, ok in (("speed", speed), ("size", size)) if ok]
        rows[lane] = {
            "byteRatio": b,
            "maxWallRatio": max(wall.values()),
            "maxCpuRatio": max(cpu.values()),
            "qualificationBranches": branches,
            "eligible": apply_ok and memory_ok and bool(branches),
        }

    eligible = [lane for lane in LANES if rows[lane]["eligible"]]
    pareto = []
    for lane in eligible:
        row = rows[lane]
        dominated = False
        for other in eligible:
            if other == lane:
                continue
            candidate = rows[other]
            no_worse = (
                candidate["byteRatio"] <= row["byteRatio"]
                and candidate["maxWallRatio"] <= row["maxWallRatio"]
                and candidate["maxCpuRatio"] <= row["maxCpuRatio"]
            )
            strictly_better = (
                candidate["byteRatio"] < row["byteRatio"]
                or candidate["maxWallRatio"] < row["maxWallRatio"]
                or candidate["maxCpuRatio"] < row["maxCpuRatio"]
            )
            if no_worse and strictly_better:
                dominated = True
                break
        if not dominated:
            pareto.append(lane)

    speed_finalist = None
    size_finalist = None
    if pareto:
        speed_finalist = min(
            pareto,
            key=lambda lane: (
                rows[lane]["maxWallRatio"],
                rows[lane]["byteRatio"],
                rows[lane]["maxCpuRatio"],
                lane,
            ),
        )
        size_finalist = min(
            pareto,
            key=lambda lane: (
                rows[lane]["byteRatio"],
                rows[lane]["maxWallRatio"],
                rows[lane]["maxCpuRatio"],
                lane,
            ),
        )
    finalists = []
    for lane in (speed_finalist, size_finalist):
        if lane is not None and lane not in finalists:
            finalists.append(lane)

    return {
        "pareto": pareto,
        "speedFinalist": speed_finalist,
        "sizeFinalist": size_finalist,
        "finalists": finalists,
        "status": "READY_FOR_FIXED_EVALUATION" if finalists else "REJECT",
        "qualificationBranches": {
            lane: rows[lane]["qualificationBranches"] for lane in finalists
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--compact", type=Path, required=True)
    parser.add_argument("--expected-verdict", type=Path)
    args = parser.parse_args()

    compact = json.loads(args.compact.read_text(encoding="utf-8"))
    actual = recompute(compact)
    if args.expected_verdict is not None:
        expected = json.loads(args.expected_verdict.read_text(encoding="utf-8"))
        comparison = {
            "pareto": expected.get("pareto"),
            "speedFinalist": expected.get("speedFinalist"),
            "sizeFinalist": expected.get("sizeFinalist"),
            "finalists": expected.get("finalists"),
            "status": expected.get("status"),
            "qualificationBranches": {
                lane: expected["lanes"][lane]["qualificationBranches"]
                for lane in expected.get("finalists", [])
            },
        }
        if actual != comparison:
            raise SystemExit(
                "independent recomputation differs from verdict:\n"
                + json.dumps({"actual": actual, "expected": comparison}, indent=2, sort_keys=True)
            )
    print(json.dumps(actual, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
