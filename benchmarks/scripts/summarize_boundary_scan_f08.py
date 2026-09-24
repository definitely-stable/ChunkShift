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
anything else is "inconclusive". BDN rounds enter the rule only when they
agree with each other and with the harness; otherwise they are reported and
excluded.

A secondary analysis, added after the first results, is labeled as such:
F01 = Scan / ScanLocals, F01+F03 = Scan / ScalarLocals, and the F07 gate
ScalarLocals / ChainFloor <= 1.15 (both hash almost the same bytes, whereas
Scan also walks each chunk's unhashed [0, Minimum) prefix).
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
# Two dependent single-cycle ALU operations per calibration step. With a PMU
# the premise is checked: cycles within 10% of 2 and at most 6 instructions
# per step (xor, add, counter, compare/branch).
CALIBRATION_CYCLES_PER_STEP = 2.0
CALIBRATION_CYCLE_TOLERANCE = 0.10
CALIBRATION_MAX_INSTRUCTIONS_PER_STEP = 6.0
# BDN rounds must agree with each other and with the harness to be used.
BDN_ROUND_SPREAD_LIMIT = 1.10
BDN_HARNESS_TOLERANCE = 0.15


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


def load_harness(
    directory: Path, rounds: int, pmu: bool
) -> tuple[dict[tuple[str, int], list[dict[str, Any]]], dict[int, float]]:
    runs: dict[tuple[str, int], list[dict[str, Any]]] = {}
    checksums: dict[tuple[str, int], str] = {}
    unhashed: dict[int, str] = {}

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
                if unhashed.setdefault(target, line["unhashed-fraction"]) != line["unhashed-fraction"]:
                    raise ValueError(f"{stem}: unhashed fraction differs from another run of this target.")

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

    return runs, {target: float(value) for target, value in unhashed.items()}


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


def ratio_line(label: str, values: list[float]) -> str:
    return f"{label}: " + ", ".join(f"{value:.3f}" for value in values)


def main() -> int:
    args = parse_args()
    pmu = args.pmu == "available"
    harness, unhashed = load_harness(args.harness, args.rounds, pmu)
    bdn = load_bdn(args.bdn)
    rounds = range(1, args.rounds + 1)

    def per_round(variant: str, target: int, field: str) -> dict[int, float]:
        return {run["round"]: run[field] for run in harness[(variant, target)]}

    def median(variant: str, target: int, field: str) -> float:
        return statistics.median(per_round(variant, target, field).values())

    # Calibration premise: checked where counters exist, never assumed valid.
    if pmu:
        calibration_problems = []
        for target in TARGETS:
            cycles = median("Calibrate", target, "cycles_per_byte")
            instructions = median("Calibrate", target, "instructions_per_byte")
            if abs(cycles - CALIBRATION_CYCLES_PER_STEP) / CALIBRATION_CYCLES_PER_STEP > CALIBRATION_CYCLE_TOLERANCE:
                calibration_problems.append(f"{target}: {cycles:.2f} cycles/step")
            if instructions > CALIBRATION_MAX_INSTRUCTIONS_PER_STEP:
                calibration_problems.append(f"{target}: {instructions:.2f} instructions/step")
        calibration = "premise failed (" + "; ".join(calibration_problems) + ")" if calibration_problems else "premise validated"
    else:
        calibration = "unvalidated (no PMU)"

    # BDN rounds must agree with each other and with the harness.
    bdn_problems = []
    for variant in VARIANTS:
        for target in TARGETS:
            values = [medians[(variant, target)] / 16_777_216 for medians in bdn]
            if max(values) / min(values) > BDN_ROUND_SPREAD_LIMIT:
                bdn_problems.append(f"{variant}/{target}: rounds {', '.join(f'{v:.4f}' for v in values)} ns/B")
            reference = median(variant, target, "ns_per_byte")
            for value in values:
                if abs(value - reference) / reference > BDN_HARNESS_TOLERANCE:
                    bdn_problems.append(f"{variant}/{target}: BDN {value:.4f} vs harness {reference:.4f} ns/B")
    bdn_used = not bdn_problems

    result: dict[str, Any] = {
        "commit": args.commit,
        "arch": args.arch,
        "pmu": args.pmu,
        "threshold": THRESHOLD,
        "calibration": calibration,
        "bdn_used_in_decision": bdn_used,
        "bdn_problems": bdn_problems,
        "targets": {},
    }

    md = [
        f"# A1-F08 boundary-scan evidence — {args.arch}",
        "",
        f"Commit `{args.commit}`. PMU: **{args.pmu}**. Calibration: **{calibration}**.",
        "",
        f"Decision rule (fixed before measuring): Scan / ChainFloor <= {THRESHOLD} in every "
        "BDN and harness round means latency-bound. BDN rounds are "
        + ("**used**." if bdn_used else "**excluded: BDN rounds disagree** (" + "; ".join(bdn_problems) + ")."),
        "",
    ]

    for target in TARGETS:
        harness_ratios = [
            per_round("Scan", target, "ns_per_byte")[r] / per_round("ChainFloor", target, "ns_per_byte")[r]
            for r in rounds
        ]
        bdn_ratios = [medians[("Scan", target)] / medians[("ChainFloor", target)] for medians in bdn]
        decision = decide(harness_ratios + (bdn_ratios if bdn_used else []))

        def ratios(numerator: str, denominator: str) -> list[float]:
            return [
                per_round(numerator, target, "ns_per_byte")[r] / per_round(denominator, target, "ns_per_byte")[r]
                for r in rounds
            ]

        f01 = ratios("Scan", "ScanLocals")
        f01_f03 = ratios("Scan", "ScalarLocals")
        f07_gate = ratios("ScalarLocals", "ChainFloor")
        f07 = decide(f07_gate)

        rows = []
        for variant in HARNESS_VARIANTS:
            runs = harness[(variant, target)]
            row: dict[str, Any] = {
                "variant": variant,
                "harness_ns_per_byte_median": median(variant, target, "ns_per_byte"),
                "harness_ns_per_byte_min": min(run["ns_per_byte"] for run in runs),
                "harness_ns_per_byte_max": max(run["ns_per_byte"] for run in runs),
                "bdn_ns_per_byte": [
                    medians[(variant, target)] / 16_777_216 for medians in bdn
                ] if variant != "Calibrate" else None,
            }
            if pmu:
                row["cycles_per_byte_measured"] = median(variant, target, "cycles_per_byte")
                row["ipc"] = median(variant, target, "ipc")
                row["instructions_per_byte"] = median(variant, target, "instructions_per_byte")
                row["branch_miss_rate"] = median(variant, target, "branch_miss_rate")
            rows.append(row)

        result["targets"][str(target)] = {
            "decision": decision,
            "unhashed_fraction": unhashed[target],
            "scan_over_chain_floor_harness": harness_ratios,
            "scan_over_chain_floor_bdn": bdn_ratios,
            "secondary": {
                "f01_scan_over_scan_locals": f01,
                "f01_f03_scan_over_scalar_locals": f01_f03,
                "f07_gate_scalar_locals_over_chain_floor": f07_gate,
                "f07_gate": f07,
            },
            "variants": rows,
        }

        md += [
            f"## Target {target // 1024} KiB — {decision}",
            "",
            ratio_line("Scan / ChainFloor, harness rounds", harness_ratios) + ".",
            ratio_line("Scan / ChainFloor, BDN rounds", bdn_ratios)
            + ("." if bdn_used else " (excluded)."),
            "",
            f"Unhashed prefix [0, Minimum): {unhashed[target]:.2%} of the data (walked by Scan/ScanLocals, "
            "skipped by ScalarLocals/ChainFloor/NoChain).",
            "",
            "Secondary analysis (added after the first results, harness rounds):",
            "",
            "- " + ratio_line("F01, Scan / ScanLocals", f01),
            "- " + ratio_line("F01+F03, Scan / ScalarLocals", f01_f03),
            "- " + ratio_line(f"F07 gate, ScalarLocals / ChainFloor (<= {THRESHOLD} means chain-bound after F01+F03)", f07_gate)
            + f" → {f07}",
            "",
        ]
        if pmu:
            md += [
                "| Variant | ns/B (harness median, min–max) | ns/B (BDN rounds) | cycles/B | IPC | instr/B | branch-miss rate |",
                "|---|---|---|---|---|---|---|",
            ]
        else:
            md += [
                "| Variant | ns/B (harness median, min–max) | ns/B (BDN rounds) |",
                "|---|---|---|",
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
            md.append("| " + " | ".join(cells) + " |")
        md.append("")

    md += [
        "Calibrate is a chain of XOR+ADD steps; its row is per step, not per byte. "
        "No cycles are derived from it without a PMU: the estimate is unvalidated.",
        "",
    ]

    args.json_output.write_text(json.dumps(result, indent=2) + "\n")
    args.markdown_output.write_text("\n".join(md))
    print("\n".join(md))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
