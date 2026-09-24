#!/usr/bin/env python3
"""Validate and summarize the A1-F08 boundary-scan evidence.

Inputs (produced by the benchmark-lab ``boundary-scan-f08`` job):

* ``--harness DIR``: ``round<R>-<variant>-<target>.txt`` files, each holding
  the one ``key=value`` line of ``ChunkShift.Benchmarks f08``, and, when the
  PMU is available, the matching ``.perf.csv`` from ``perf stat -x,``;
* ``--bdn DIR`` (repeatable, one per BenchmarkDotNet round): directories
  searched for ``*BoundaryScanBenchmarks*-report-full.json``;
* ``--pmu available|unavailable``: result of the job's PMU probe.

Every expected run must be present and self-consistent, or the script fails.
The decision rule was fixed before measuring: the loop is called
latency-bound when Scan / ChainFloor <= 1.15 in every BDN round and every
harness round; "not latency-bound" when it is above 1.15 in all of them;
anything else is "inconclusive".
"""

from __future__ import annotations

import argparse
import json
import re
import statistics
from pathlib import Path
from typing import Any

VARIANTS = ["Scan", "ScanLocals", "ScalarLocals", "ChainFloor", "NoChain"]
HARNESS_VARIANTS = VARIANTS + ["Calibrate"]
TARGETS = [65536, 262144]
EVENTS = ["cycles:u", "instructions:u", "branches:u", "branch-misses:u"]
THRESHOLD = 1.15
# Two dependent single-cycle ALU operations per calibration step.
CALIBRATION_CYCLES_PER_STEP = 2.0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--harness", type=Path, required=True)
    parser.add_argument("--bdn", type=Path, action="append", required=True)
    parser.add_argument("--pmu", choices=["available", "unavailable"], required=True)
    parser.add_argument("--rounds", type=int, required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--arch", required=True)
    parser.add_argument("--json", dest="json_output", type=Path, required=True)
    parser.add_argument("--markdown", dest="markdown_output", type=Path, required=True)
    return parser.parse_args()


def parse_line(path: Path) -> dict[str, str]:
    lines = [line for line in path.read_text().splitlines() if line.startswith("variant=")]
    if len(lines) != 1:
        raise ValueError(f"{path}: expected exactly one result line, found {len(lines)}.")
    return dict(item.split("=", 1) for item in lines[0].split())


def parse_perf(path: Path) -> dict[str, float]:
    counts: dict[str, float] = {}
    for line in path.read_text().splitlines():
        if not line or line.startswith("#"):
            continue
        fields = line.split(",")
        if len(fields) < 5 or fields[2] not in EVENTS:
            continue
        value, event, enabled_pct = fields[0], fields[2], fields[4]
        if not re.fullmatch(r"[0-9]+(\.[0-9]+)?", value):
            raise ValueError(f"{path}: {event} was not counted ({value}).")
        if float(enabled_pct or 0) < 100.0:
            raise ValueError(f"{path}: {event} was multiplexed ({enabled_pct}% enabled).")
        counts[event] = float(value)
    missing = [event for event in EVENTS if event not in counts]
    if missing:
        raise ValueError(f"{path}: missing counters {missing}.")
    return counts


def load_harness(directory: Path, rounds: int, pmu: bool) -> dict[tuple[str, int], list[dict[str, Any]]]:
    runs: dict[tuple[str, int], list[dict[str, Any]]] = {}
    checksums: dict[tuple[str, int], str] = {}

    for round_number in range(1, rounds + 1):
        for variant in HARNESS_VARIANTS:
            for target in TARGETS:
                stem = directory / f"round{round_number}-{variant}-{target}"
                line = parse_line(stem.with_suffix(".txt"))
                if line["variant"] != variant or int(line["target"]) != target:
                    raise ValueError(f"{stem}: result line names {line['variant']}/{line['target']}.")
                if line["counted"] != ("yes" if pmu else "no"):
                    raise ValueError(f"{stem}: counted={line['counted']} but PMU is {'available' if pmu else 'unavailable'}.")

                key = (variant, target)
                if checksums.setdefault(key, line["checksum"]) != line["checksum"]:
                    raise ValueError(f"{stem}: checksum differs from an earlier round.")

                run: dict[str, Any] = {
                    "round": round_number,
                    "bytes": int(line["bytes"]),
                    "ns_per_byte": float(line["ns-per-byte"]),
                }
                if pmu:
                    counts = parse_perf(Path(f"{stem}.perf.csv"))
                    run["cycles_per_byte"] = counts["cycles:u"] / run["bytes"]
                    run["ipc"] = counts["instructions:u"] / counts["cycles:u"]
                    run["instructions_per_byte"] = counts["instructions:u"] / run["bytes"]
                    run["branch_miss_rate"] = counts["branch-misses:u"] / counts["branches:u"]
                runs.setdefault(key, []).append(run)

    return runs


def load_bdn(directories: list[Path]) -> list[dict[tuple[str, int], float]]:
    rounds = []
    for directory in directories:
        medians: dict[tuple[str, int], float] = {}
        for report in sorted(directory.rglob("*BoundaryScanBenchmarks*-report-full.json")):
            for benchmark in json.loads(report.read_text())["Benchmarks"]:
                target = int(re.search(r"TargetSize=(\d+)", benchmark["Parameters"]).group(1))
                key = (benchmark["Method"], target)
                if key in medians:
                    raise ValueError(f"{directory}: {key} reported twice.")
                medians[key] = benchmark["Statistics"]["Median"]
        expected = {(variant, target) for variant in VARIANTS for target in TARGETS}
        if set(medians) != expected:
            raise ValueError(f"{directory}: expected {sorted(expected)}, found {sorted(medians)}.")
        rounds.append(medians)
    return rounds


def decide(ratios: list[float]) -> str:
    if all(ratio <= THRESHOLD for ratio in ratios):
        return "latency-bound"
    if all(ratio > THRESHOLD for ratio in ratios):
        return "not latency-bound"
    return "inconclusive"


def main() -> int:
    args = parse_args()
    pmu = args.pmu == "available"
    harness = load_harness(args.harness, args.rounds, pmu)
    bdn = load_bdn(args.bdn)

    result: dict[str, Any] = {
        "commit": args.commit,
        "arch": args.arch,
        "pmu": args.pmu,
        "threshold": THRESHOLD,
        "targets": {},
    }

    md = [
        f"# A1-F08 boundary-scan evidence — {args.arch}",
        "",
        f"Commit `{args.commit}`. PMU: **{args.pmu}**."
        + ("" if pmu else " Cycles below are an **estimate** from the calibration chain"
           f" (assumed {CALIBRATION_CYCLES_PER_STEP:g} cycles per step), not a measurement."),
        "",
        f"Decision rule (fixed before measuring): Scan / ChainFloor <= {THRESHOLD} in every "
        "BDN and harness round means latency-bound.",
        "",
    ]

    for target in TARGETS:
        calibrate = statistics.median(run["ns_per_byte"] for run in harness[("Calibrate", target)])
        estimated_ghz = CALIBRATION_CYCLES_PER_STEP / calibrate

        bdn_ratios = [medians[("Scan", target)] / medians[("ChainFloor", target)] for medians in bdn]
        harness_by_round = {
            variant: {run["round"]: run for run in harness[(variant, target)]}
            for variant in HARNESS_VARIANTS
        }
        harness_ratios = [
            harness_by_round["Scan"][r]["ns_per_byte"] / harness_by_round["ChainFloor"][r]["ns_per_byte"]
            for r in range(1, args.rounds + 1)
        ]
        decision = decide(bdn_ratios + harness_ratios)

        rows = []
        for variant in HARNESS_VARIANTS:
            runs = harness[(variant, target)]
            ns = statistics.median(run["ns_per_byte"] for run in runs)
            row: dict[str, Any] = {
                "variant": variant,
                "harness_ns_per_byte_median": ns,
                "harness_ns_per_byte_min": min(run["ns_per_byte"] for run in runs),
                "harness_ns_per_byte_max": max(run["ns_per_byte"] for run in runs),
                "bdn_ns_per_byte": [
                    medians[(variant, target)] / 16_777_216 for medians in bdn
                ] if variant != "Calibrate" else None,
            }
            if pmu:
                row["cycles_per_byte_measured"] = statistics.median(run["cycles_per_byte"] for run in runs)
                row["ipc"] = statistics.median(run["ipc"] for run in runs)
                row["instructions_per_byte"] = statistics.median(run["instructions_per_byte"] for run in runs)
                row["branch_miss_rate"] = statistics.median(run["branch_miss_rate"] for run in runs)
            else:
                row["cycles_per_byte_estimated"] = ns * estimated_ghz
            rows.append(row)

        result["targets"][str(target)] = {
            "decision": decision,
            "scan_over_chain_floor_bdn": bdn_ratios,
            "scan_over_chain_floor_harness": harness_ratios,
            "estimated_ghz_from_calibration": estimated_ghz,
            "variants": rows,
        }

        md += [
            f"## Target {target // 1024} KiB — {decision}",
            "",
            "Scan / ChainFloor: BDN rounds "
            + ", ".join(f"{ratio:.3f}" for ratio in bdn_ratios)
            + "; harness rounds "
            + ", ".join(f"{ratio:.3f}" for ratio in harness_ratios)
            + ".",
            "",
        ]
        if pmu:
            md += [
                "| Variant | ns/B (harness median, min–max) | ns/B (BDN rounds) | cycles/B | IPC | instr/B | branch-miss rate |",
                "|---|---|---|---|---|---|---|",
            ]
        else:
            md += [
                "| Variant | ns/B (harness median, min–max) | ns/B (BDN rounds) | cycles/B (estimated) |",
                "|---|---|---|---|",
            ]
        for row in rows:
            bdn_cell = (
                ", ".join(f"{value:.4f}" for value in row["bdn_ns_per_byte"])
                if row["bdn_ns_per_byte"] else "—"
            )
            cells = [
                row["variant"],
                f"{row['harness_ns_per_byte_median']:.4f} ({row['harness_ns_per_byte_min']:.4f}–{row['harness_ns_per_byte_max']:.4f})",
                bdn_cell,
            ]
            if pmu:
                cells += [
                    f"{row['cycles_per_byte_measured']:.3f}",
                    f"{row['ipc']:.2f}",
                    f"{row['instructions_per_byte']:.2f}",
                    f"{row['branch_miss_rate']:.5f}",
                ]
            else:
                cells.append(f"{row['cycles_per_byte_estimated']:.3f}")
            md.append("| " + " | ".join(cells) + " |")
        md.append("")

    clocks = ", ".join(
        f"{result['targets'][str(t)]['estimated_ghz_from_calibration']:.2f} GHz" for t in TARGETS
    )
    md += [
        "Calibrate is a chain of XOR+ADD steps; its row is per step, not per byte. "
        + ("Its measured cycles per step check the 2-cycle assumption." if pmu else
           f"Estimated clock: {clocks}."),
        "",
    ]

    args.json_output.write_text(json.dumps(result, indent=2) + "\n")
    args.markdown_output.write_text("\n".join(md))
    print("\n".join(md))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
