#!/usr/bin/env python3
"""Validate and compact ChunkShift scanner Phase 2 BenchmarkDotNet evidence."""

from __future__ import annotations

import argparse
import json
import math
import statistics
from pathlib import Path
from typing import Any

EXPECTED_LAUNCHES = 3
EXPECTED_ACTUAL_PER_LAUNCH = 10
EXPECTED_WARMUP_PER_LAUNCH = 6

EXPECTED_TYPES = {
    "ScannerApiPhase2CoreBenchmarks",
    "ScannerApiPhase2FileBenchmarks",
    "ScannerApiPhase2AsyncBenchmarks",
}

EXPECTED_METHODS = {
    "DirectGenericSink",
    "LegacyValueTaskWrapper",
    "FusedValueTaskCore",
    "FusedTaskCore",
    "PublicEndToEndValueTask",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--arch", required=True)
    parser.add_argument("--json", dest="json_output", required=True, type=Path)
    parser.add_argument("--markdown", dest="markdown_output", required=True, type=Path)
    return parser.parse_args()


def operation_ns(measurement: dict[str, Any]) -> float:
    operations = int(measurement["Operations"])
    if operations <= 0:
        raise ValueError("Benchmark measurement Operations must be positive.")
    return float(measurement["Nanoseconds"]) / operations


def launch_values(
    measurements: list[dict[str, Any]],
    stage: str,
) -> dict[int, list[float]]:
    result: dict[int, list[float]] = {}

    for measurement in measurements:
        if (
            measurement.get("IterationMode") == "Workload"
            and measurement.get("IterationStage") == stage
        ):
            launch = int(measurement["LaunchIndex"])
            result.setdefault(launch, []).append(operation_ns(measurement))

    return result


def validate_launch_shape(
    values: dict[int, list[float]],
    expected_per_launch: int,
    label: str,
) -> None:
    expected_launches = set(range(1, EXPECTED_LAUNCHES + 1))

    if set(values) != expected_launches:
        raise ValueError(
            f"{label}: expected launches {sorted(expected_launches)}, "
            f"found {sorted(values)}."
        )

    for launch, launch_measurements in values.items():
        if len(launch_measurements) != expected_per_launch:
            raise ValueError(
                f"{label}: launch {launch} expected {expected_per_launch} "
                f"measurements, found {len(launch_measurements)}."
            )


def fmt_ns(value: float) -> str:
    if value >= 1_000_000:
        return f"{value / 1_000_000:.3f} ms"
    if value >= 1_000:
        return f"{value / 1_000:.3f} us"
    return f"{value:.1f} ns"


def fmt_ratio(value: float | None) -> str:
    if value is None:
        return "-"
    return f"{(value - 1.0) * 100:+.2f}%"


def build_case(benchmark: dict[str, Any]) -> dict[str, Any]:
    actual = launch_values(benchmark["Measurements"], "Actual")
    warmup = launch_values(benchmark["Measurements"], "Warmup")

    label = (
        f"{benchmark['Type']}.{benchmark['Method']}"
        f"({benchmark.get('Parameters') or ''})"
    )

    validate_launch_shape(actual, EXPECTED_ACTUAL_PER_LAUNCH, label + " actual")
    validate_launch_shape(warmup, EXPECTED_WARMUP_PER_LAUNCH, label + " warmup")

    launch_means = {
        str(launch): statistics.fmean(values)
        for launch, values in sorted(actual.items())
    }

    stats = benchmark["Statistics"]
    memory = benchmark["Memory"]

    return {
        "type": benchmark["Type"],
        "method": benchmark["Method"],
        "parameters": benchmark.get("Parameters") or "",
        "aggregate": {
            "n_after_outlier_processing": stats.get("N"),
            "mean_ns_per_op": stats.get("Mean"),
            "median_p50_ns_per_op": stats.get("Percentiles", {}).get("P50"),
            "p95_ns_per_op": stats.get("Percentiles", {}).get("P95"),
            "stddev_ns_per_op": stats.get("StandardDeviation"),
            "standard_error_ns_per_op": stats.get("StandardError"),
            "min_ns_per_op": stats.get("Min"),
            "max_ns_per_op": stats.get("Max"),
            "confidence_interval": stats.get("ConfidenceInterval"),
            "outliers_ns_per_op": stats.get("AllOutliers", []),
        },
        "memory": {
            "bytes_allocated_per_operation": memory.get(
                "BytesAllocatedPerOperation"
            ),
            "gen0_collections": memory.get("Gen0Collections"),
            "gen1_collections": memory.get("Gen1Collections"),
            "gen2_collections": memory.get("Gen2Collections"),
            "total_operations": memory.get("TotalOperations"),
        },
        "launch_mean_ns_per_op": launch_means,
        "actual_ns_per_op": {
            str(launch): values for launch, values in sorted(actual.items())
        },
    }


def add_ratios(cases: list[dict[str, Any]]) -> None:
    groups: dict[tuple[str, str], dict[str, dict[str, Any]]] = {}

    for case in cases:
        key = (case["type"], case["parameters"])
        groups.setdefault(key, {})[case["method"]] = case

    for key, methods in groups.items():
        missing = EXPECTED_METHODS - set(methods)
        if missing:
            raise ValueError(
                f"{key}: missing expected methods {sorted(missing)}."
            )

        direct = methods["DirectGenericSink"]
        value_task = methods["FusedValueTaskCore"]

        for method_name, case in methods.items():
            ratios_to_direct: dict[str, float] = {}
            ratios_to_value_task: dict[str, float] = {}

            for launch in map(str, range(1, EXPECTED_LAUNCHES + 1)):
                current = case["launch_mean_ns_per_op"][launch]
                direct_mean = direct["launch_mean_ns_per_op"][launch]
                value_task_mean = value_task["launch_mean_ns_per_op"][launch]

                ratios_to_direct[launch] = current / direct_mean
                ratios_to_value_task[launch] = current / value_task_mean

            case["ratio_to_direct_by_launch"] = ratios_to_direct
            case["ratio_to_fused_valuetask_by_launch"] = ratios_to_value_task


def write_markdown(
    output: Path,
    environment: dict[str, Any],
    commit: str,
    arch: str,
    cases: list[dict[str, Any]],
) -> None:
    lines = [
        "# Scanner API Phase 2 compact evidence",
        "",
        f"- Commit: `{commit}`",
        f"- Matrix architecture: `{arch}`",
        f"- Host architecture: `{environment.get('Architecture')}`",
        f"- CPU: `{environment.get('ProcessorName')}`",
        f"- Runtime: `{environment.get('RuntimeVersion')}`",
        f"- BenchmarkDotNet: `{environment.get('BenchmarkDotNetVersion')}`",
        f"- Policy: {EXPECTED_LAUNCHES} launches × "
        f"{EXPECTED_ACTUAL_PER_LAUNCH} measured × "
        f"{EXPECTED_WARMUP_PER_LAUNCH} warmup",
        "",
        "| Case | Mean | P50 | P95 | Alloc/op | L1 vs direct | L2 vs direct | L3 vs direct |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
    ]

    for case in cases:
        label = case["type"].replace("ScannerApiPhase2", "").replace(
            "Benchmarks", ""
        )
        params = case["parameters"]
        if params:
            label += f" {params}"
        label += f" / {case['method']}"

        aggregate = case["aggregate"]
        ratios = case["ratio_to_direct_by_launch"]

        lines.append(
            "| "
            + " | ".join(
                [
                    label,
                    fmt_ns(float(aggregate["mean_ns_per_op"])),
                    fmt_ns(float(aggregate["median_p50_ns_per_op"])),
                    fmt_ns(float(aggregate["p95_ns_per_op"])),
                    str(case["memory"]["bytes_allocated_per_operation"]),
                    fmt_ratio(ratios["1"]),
                    fmt_ratio(ratios["2"]),
                    fmt_ratio(ratios["3"]),
                ]
            )
            + " |"
        )

    lines.extend(
        [
            "",
            "## Task vs ValueTask by launch",
            "",
            "| Scenario | Launch 1 Task vs ValueTask | Launch 2 | Launch 3 |",
            "| --- | ---: | ---: | ---: |",
        ]
    )

    groups: dict[tuple[str, str], dict[str, dict[str, Any]]] = {}
    for case in cases:
        groups.setdefault(
            (case["type"], case["parameters"]), {}
        )[case["method"]] = case

    for (type_name, parameters), methods in sorted(groups.items()):
        task = methods["FusedTaskCore"]
        ratios = task["ratio_to_fused_valuetask_by_launch"]
        scenario = type_name.replace("ScannerApiPhase2", "").replace(
            "Benchmarks", ""
        )
        if parameters:
            scenario += f" {parameters}"

        lines.append(
            f"| {scenario} | {fmt_ratio(ratios['1'])} | "
            f"{fmt_ratio(ratios['2'])} | {fmt_ratio(ratios['3'])} |"
        )

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    args = parse_args()
    document = json.loads(args.input.read_text(encoding="utf-8"))

    benchmarks = document.get("Benchmarks", [])
    present_types = {benchmark["Type"] for benchmark in benchmarks}
    missing_types = EXPECTED_TYPES - present_types
    if missing_types:
        raise ValueError(
            f"Missing expected Phase 2 benchmark types: {sorted(missing_types)}."
        )

    cases = [build_case(benchmark) for benchmark in benchmarks]
    add_ratios(cases)

    environment = document.get("HostEnvironmentInfo", {})
    compact = {
        "schemaVersion": 1,
        "kind": "chunkshift.scanner-api.phase2-evidence",
        "commit": args.commit,
        "matrixArchitecture": args.arch,
        "policy": {
            "launchCount": EXPECTED_LAUNCHES,
            "warmupCountPerLaunch": EXPECTED_WARMUP_PER_LAUNCH,
            "measuredCountPerLaunch": EXPECTED_ACTUAL_PER_LAUNCH,
        },
        "environment": environment,
        "cases": cases,
    }

    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    args.json_output.write_text(
        json.dumps(compact, indent=2) + "\n",
        encoding="utf-8",
    )

    write_markdown(
        args.markdown_output,
        environment,
        args.commit,
        args.arch,
        cases,
    )


if __name__ == "__main__":
    main()
