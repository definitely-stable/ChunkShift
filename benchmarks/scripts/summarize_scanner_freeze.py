#!/usr/bin/env python3
"""Validate and compact the final ChunkShift scanner API freeze evidence."""

from __future__ import annotations

import argparse
import json
import statistics
from pathlib import Path
from typing import Any

EXPECTED_LAUNCHES = 5
EXPECTED_ACTUAL_PER_LAUNCH = 10
EXPECTED_WARMUP_PER_LAUNCH = 6

EXPECTED_TYPES = {
    "ScannerApiFreezeProfileBenchmarks",
    "ScannerApiFreezeCorpusBenchmarks",
    "ScannerApiFreezeShortReadBenchmarks",
    "ScannerApiFreezeAsyncBenchmarks",
    "ScannerApiFreezeFileBenchmarks",
}

EXPECTED_METHODS = {
    "DirectKernel",
    "PublicValueTask",
    "PublicTaskPrototype",
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

    for launch, samples in values.items():
        if len(samples) != expected_per_launch:
            raise ValueError(
                f"{label}: launch {launch} expected {expected_per_launch} "
                f"measurements, found {len(samples)}."
            )


def fmt_ns(value: float) -> str:
    if value >= 1_000_000:
        return f"{value / 1_000_000:.3f} ms"
    if value >= 1_000:
        return f"{value / 1_000:.3f} us"
    return f"{value:.1f} ns"


def fmt_ratio(value: float) -> str:
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
        "launch_mean_ns_per_op": {
            str(launch): statistics.fmean(samples)
            for launch, samples in sorted(actual.items())
        },
        "actual_ns_per_op": {
            str(launch): samples for launch, samples in sorted(actual.items())
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
            raise ValueError(f"{key}: missing methods {sorted(missing)}.")

        direct = methods["DirectKernel"]
        value_task = methods["PublicValueTask"]

        for case in methods.values():
            case["ratio_to_direct_by_launch"] = {}
            case["ratio_to_valuetask_by_launch"] = {}

            for launch in map(str, range(1, EXPECTED_LAUNCHES + 1)):
                current = case["launch_mean_ns_per_op"][launch]
                direct_mean = direct["launch_mean_ns_per_op"][launch]
                value_task_mean = value_task["launch_mean_ns_per_op"][launch]

                case["ratio_to_direct_by_launch"][launch] = current / direct_mean
                case["ratio_to_valuetask_by_launch"][launch] = (
                    current / value_task_mean
                )


def write_markdown(
    output: Path,
    environment: dict[str, Any],
    commit: str,
    arch: str,
    cases: list[dict[str, Any]],
) -> None:
    lines = [
        "# Scanner API final freeze evidence",
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
        "## Aggregate",
        "",
        "| Case | Mean | P50 | P95 | Alloc/op |",
        "| --- | ---: | ---: | ---: | ---: |",
    ]

    for case in cases:
        label = case["type"].replace("ScannerApiFreeze", "").replace(
            "Benchmarks", ""
        )
        if case["parameters"]:
            label += f" {case['parameters']}"
        label += f" / {case['method']}"

        aggregate = case["aggregate"]
        lines.append(
            f"| {label} | "
            f"{fmt_ns(float(aggregate['mean_ns_per_op']))} | "
            f"{fmt_ns(float(aggregate['median_p50_ns_per_op']))} | "
            f"{fmt_ns(float(aggregate['p95_ns_per_op']))} | "
            f"{case['memory']['bytes_allocated_per_operation']} |"
        )

    lines.extend(
        [
            "",
            "## Task vs ValueTask by independent launch",
            "",
            "| Scenario | L1 | L2 | L3 | L4 | L5 |",
            "| --- | ---: | ---: | ---: | ---: | ---: |",
        ]
    )

    groups: dict[tuple[str, str], dict[str, dict[str, Any]]] = {}
    for case in cases:
        groups.setdefault(
            (case["type"], case["parameters"]), {}
        )[case["method"]] = case

    for (type_name, parameters), methods in sorted(groups.items()):
        ratios = methods["PublicTaskPrototype"]["ratio_to_valuetask_by_launch"]
        scenario = type_name.replace("ScannerApiFreeze", "").replace(
            "Benchmarks", ""
        )
        if parameters:
            scenario += f" {parameters}"

        lines.append(
            f"| {scenario} | "
            + " | ".join(
                fmt_ratio(ratios[str(launch)])
                for launch in range(1, EXPECTED_LAUNCHES + 1)
            )
            + " |"
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
            f"Missing expected freeze benchmark types: {sorted(missing_types)}."
        )

    cases = [build_case(benchmark) for benchmark in benchmarks]
    add_ratios(cases)

    environment = document.get("HostEnvironmentInfo", {})
    compact = {
        "schemaVersion": 1,
        "kind": "chunkshift.scanner-api.freeze-evidence",
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
