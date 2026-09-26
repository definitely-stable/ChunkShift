#!/usr/bin/env python3
"""Run and summarize the pre-registered #8 CDC runtime guardrail.

This script intentionally does not choose a profile. It executes the accepted
protocol's runtime-veto measurement with the existing `lab --isolate` runner,
retains every raw process result, and reports process-level dispersion.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import statistics
import subprocess
import sys
from typing import Any, Iterable

LAB_SCHEMA_VERSION = 7
MANIFEST_SCHEMA_VERSION = 2
ALGORITHM = "fastcdc.gear.chunkshift.v1"
HASH_SUITE = "chunkshift.blake3-256.v1"

WORKLOADS = ("runtime-game-pak-64m", "runtime-db-vm-64m")
TARGETS = (65536, 131072, 262144)

PROFILE_BY_TARGET = {
    65536: (
        "fastcdc.gear.candidate.v1.m16384.t65536.x262144",
        "054e6ced561558147f9c35dc66c64142fd4562d21132f0dc51e00544c04200a0",
    ),
    131072: (
        "fastcdc.gear.candidate.v1.m32768.t131072.x524288",
        "74d375951d3cd4d165fdad5866c6de0b7a9231c794f16444930ac5acdd65b3da",
    ),
    262144: (
        "fastcdc.gear.candidate.v1.m65536.t262144.x1048576",
        "d8fc289d93f8f8b6308498891831638743cce8dde789417f25cf5f3d8f7fbae4",
    ),
}

ID_BY_KEY = {
    (65536, "runtime-game-pak-64m"): "runtime-game-pak-64m-64k",
    (65536, "runtime-db-vm-64m"): "runtime-db-vm-64m-64k",
    (131072, "runtime-game-pak-64m"): "runtime-game-pak-64m-128k",
    (131072, "runtime-db-vm-64m"): "runtime-db-vm-64m-128k",
    (262144, "runtime-game-pak-64m"): "runtime-game-pak-64m-256k",
    (262144, "runtime-db-vm-64m"): "runtime-db-vm-64m-256k",
}

# Ten runs cannot put each of three candidates in each of three positions an
# equal integer number of times. This schedule is therefore near-balanced:
# each target occupies the three candidate-group positions 3/3/4 times, and
# all six permutations occur at least once. Reverse orders are interleaved to
# reduce monotonic host drift.
SCHEDULE = (
    (65536, 131072, 262144),
    (262144, 131072, 65536),
    (65536, 262144, 131072),
    (131072, 262144, 65536),
    (131072, 65536, 262144),
    (262144, 65536, 131072),
    (65536, 262144, 131072),
    (262144, 131072, 65536),
    (131072, 65536, 262144),
    (262144, 65536, 131072),
)


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=False) + "\n", encoding="utf-8")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def schedule_sha256() -> str:
    canonical = json.dumps(SCHEDULE, separators=(",", ":")).encode("ascii")
    return hashlib.sha256(canonical).hexdigest()


def kib(target: int) -> int:
    return target // 1024


CORPUS_EXPECTED = {
    "runtime-game-pak-64m": ("game/pak-like assets", "game-pak-like", 67108864, 1001, 1),
    "runtime-db-vm-64m": ("DB/VM/data files", "db-vm-data-like", 67108864, 1004, 1),
}


def validate_corpus(path: Path) -> dict[str, Any]:
    corpus = load_json(path)
    if corpus.get("schemaVersion") != 1:
        raise ValueError(f"{path}: expected corpus schemaVersion 1")

    entries = corpus.get("entries")
    if not isinstance(entries, list) or len(entries) != 2:
        raise ValueError(f"{path}: expected exactly 2 runtime corpus entries")

    seen: set[str] = set()
    for entry in entries:
        corpus_id = entry.get("id")
        if corpus_id not in CORPUS_EXPECTED:
            raise ValueError(f"{path}: unexpected runtime corpus id {corpus_id!r}")
        if corpus_id in seen:
            raise ValueError(f"{path}: duplicate runtime corpus id {corpus_id}")
        seen.add(corpus_id)

        expected_category, expected_generator, expected_size, expected_seed, expected_version = CORPUS_EXPECTED[corpus_id]
        exact = {
            "category": expected_category,
            "generator": expected_generator,
            "sizeBytes": expected_size,
            "seed": expected_seed,
            "generatorVersion": expected_version,
        }
        for field, expected in exact.items():
            if entry.get(field) != expected:
                raise ValueError(
                    f"{corpus_id}: {field}={entry.get(field)!r}, expected {expected!r}"
                )
        provenance = entry.get("provenance")
        if not isinstance(provenance, str) or not provenance.strip():
            raise ValueError(f"{corpus_id}: provenance must be recorded")

    if seen != set(CORPUS_EXPECTED):
        raise ValueError(f"{path}: runtime corpus is incomplete")

    return corpus

def validate_schedule() -> dict[int, list[int]]:
    if len(SCHEDULE) != 10:
        raise ValueError(f"runtime schedule must contain exactly 10 runs, found {len(SCHEDULE)}")

    all_permutations = set()
    counts = {target: [0, 0, 0] for target in TARGETS}

    for index, order in enumerate(SCHEDULE, start=1):
        if tuple(sorted(order)) != TARGETS:
            raise ValueError(f"run {index}: order is not a permutation of the three targets: {order}")
        all_permutations.add(order)
        for position, target in enumerate(order):
            counts[target][position] += 1

    if len(all_permutations) != 6:
        raise ValueError("runtime schedule must contain all six target permutations at least once")

    for target, positions in counts.items():
        if max(positions) - min(positions) > 1:
            raise ValueError(f"{kib(target)} KiB position counts are not near-balanced: {positions}")

    return counts


def validate_manifest(path: Path) -> dict[str, Any]:
    manifest = load_json(path)
    if manifest.get("schemaVersion") != MANIFEST_SCHEMA_VERSION:
        raise ValueError(f"{path}: expected schemaVersion {MANIFEST_SCHEMA_VERSION}")

    experiments = manifest.get("experiments")
    if not isinstance(experiments, list) or len(experiments) != 6:
        raise ValueError(f"{path}: expected exactly 6 runtime experiments")

    seen_ids: set[str] = set()
    seen_keys: set[tuple[int, str]] = set()

    for experiment in experiments:
        experiment_id = experiment.get("id")
        if not isinstance(experiment_id, str) or not experiment_id:
            raise ValueError("runtime experiment id must be a non-empty string")
        if experiment_id in seen_ids:
            raise ValueError(f"duplicate runtime experiment id: {experiment_id}")
        seen_ids.add(experiment_id)

        target = experiment.get("chunkSize")
        workload = experiment.get("corpusId")
        key = (target, workload)
        if key not in ID_BY_KEY:
            raise ValueError(f"{experiment_id}: unexpected target/workload pair {key}")
        if key in seen_keys:
            raise ValueError(f"duplicate runtime target/workload pair {key}")
        seen_keys.add(key)

        expected_id = ID_BY_KEY[key]
        expected_profile, expected_fingerprint = PROFILE_BY_TARGET[target]

        exact = {
            "id": expected_id,
            "algorithm": ALGORITHM,
            "profileId": expected_profile,
            "profileFingerprint": expected_fingerprint,
            "hashSuite": HASH_SUITE,
            "mutation": None,
        }
        for field, expected in exact.items():
            if experiment.get(field) != expected:
                raise ValueError(
                    f"{experiment_id}: {field}={experiment.get(field)!r}, expected {expected!r}"
                )

    if seen_keys != set(ID_BY_KEY):
        missing = sorted(set(ID_BY_KEY) - seen_keys)
        raise ValueError(f"runtime manifest is missing target/workload pairs: {missing}")

    validate_schedule()
    return manifest


def expected_ids(order: Iterable[int]) -> list[str]:
    return [ID_BY_KEY[(target, workload)] for target in order for workload in WORKLOADS]


def ordered_manifest(base: dict[str, Any], order: tuple[int, int, int]) -> dict[str, Any]:
    by_key = {
        (experiment["chunkSize"], experiment["corpusId"]): experiment
        for experiment in base["experiments"]
    }
    return {
        "schemaVersion": MANIFEST_SCHEMA_VERSION,
        "experiments": [
            by_key[(target, workload)]
            for target in order
            for workload in WORKLOADS
        ],
    }


def validate_lab_run(
    run: dict[str, Any],
    order: tuple[int, int, int],
    commit: str,
) -> None:
    if run.get("schemaVersion") != LAB_SCHEMA_VERSION:
        raise ValueError(f"lab schema changed: expected {LAB_SCHEMA_VERSION}, found {run.get('schemaVersion')}")

    measurement = run.get("measurement") or {}
    if measurement.get("processIsolation") != "per-experiment":
        raise ValueError("runtime evidence must be produced by lab --isolate")
    measurement_iterations = measurement.get("measurementIterations")
    if measurement_iterations != 5:
        raise ValueError(f"expected 5 within-process measurement iterations, found {measurement_iterations}")

    environment = run.get("environment") or {}
    if environment.get("gitCommit") != commit:
        raise ValueError(
            f"runtime run commit {environment.get('gitCommit')!r} does not match expected {commit!r}"
        )

    results = run.get("results")
    expected = expected_ids(order)
    if [result.get("experimentId") for result in results or []] != expected:
        raise ValueError("runtime result order does not match the pre-registered candidate schedule")

    for result in results:
        experiment_id = result["experimentId"]
        target = next(target for target in TARGETS if f"-{kib(target)}k" in experiment_id)
        expected_profile, expected_fingerprint = PROFILE_BY_TARGET[target]

        if result.get("algorithm") != ALGORITHM:
            raise ValueError(f"{experiment_id}: unexpected algorithm")
        if result.get("profileId") != expected_profile:
            raise ValueError(f"{experiment_id}: unexpected profile id")
        if result.get("profileFingerprint") != expected_fingerprint:
            raise ValueError(f"{experiment_id}: unexpected profile fingerprint")
        if result.get("hashSuite") != HASH_SUITE:
            raise ValueError(f"{experiment_id}: unexpected hash suite")
        if result.get("mutation") is not None:
            raise ValueError(f"{experiment_id}: runtime guardrail must use identity workloads")

        evidence = result.get("evidence") or {}
        if evidence.get("sourceChunkSequenceSha256") != evidence.get("sourceStreamingChunkSequenceSha256"):
            raise ValueError(f"{experiment_id}: scalar/streaming source chunk sequence differs")
        if evidence.get("targetChunkSequenceSha256") != evidence.get("targetStreamingChunkSequenceSha256"):
            raise ValueError(f"{experiment_id}: scalar/streaming target chunk sequence differs")

        streaming = result.get("streaming") or {}
        samples = streaming.get("samples")
        if not isinstance(samples, list) or len(samples) != measurement_iterations:
            raise ValueError(f"{experiment_id}: missing within-process streaming samples")


def percentile_linear(values: list[float], p: float) -> float:
    if not values:
        raise ValueError("cannot compute a percentile of an empty sequence")
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    rank = (len(ordered) - 1) * p
    low = math.floor(rank)
    high = math.ceil(rank)
    if low == high:
        return ordered[low]
    fraction = rank - low
    return ordered[low] + (ordered[high] - ordered[low]) * fraction


def describe(values: list[float]) -> dict[str, Any]:
    if not values:
        raise ValueError("cannot summarize an empty sequence")
    median = statistics.median(values)
    deviations = [abs(value - median) for value in values]
    q1 = percentile_linear(values, 0.25)
    q3 = percentile_linear(values, 0.75)
    return {
        "count": len(values),
        "median": median,
        "iqr": q3 - q1,
        "mad": statistics.median(deviations),
        "min": min(values),
        "max": max(values),
        "values": values,
    }


def process_metrics(result: dict[str, Any]) -> dict[str, float]:
    streaming = result["streaming"]
    metrics = result["metrics"]
    measured_gib = metrics["measuredBytes"] / float(1 << 30)
    if measured_gib <= 0:
        raise ValueError(f"{result['experimentId']}: measured bytes must be positive")

    streaming_peaks = [
        sample["processPeakRssBytes"]
        for sample in streaming["samples"]
    ]
    peak_rss = max([metrics["processPeakRssBytes"], *streaming_peaks])

    return {
        "wallSeconds": streaming["wallSeconds"],
        "throughputGiBPerSecond": streaming["giBPerSecond"],
        "cpuSecondsPerGiB": streaming["cpuSeconds"] / measured_gib,
        "allocatedBytesPerGiB": streaming["allocatedBytes"] / measured_gib,
        "bytesCopiedPerInputByte": streaming["bytesCopiedPerInputByte"],
        "processPeakRssBytes": float(peak_rss),
    }


def load_raw_runs(output_dir: Path, commit: str) -> list[dict[str, Any]]:
    raw_dir = output_dir / "raw"
    paths = sorted(raw_dir.glob("run-??.json"))
    if len(paths) != 10:
        raise ValueError(f"expected 10 raw runtime runs in {raw_dir}, found {len(paths)}")

    runs: list[dict[str, Any]] = []
    for index, (path, order) in enumerate(zip(paths, SCHEDULE), start=1):
        expected_name = f"run-{index:02d}.json"
        if path.name != expected_name:
            raise ValueError(f"expected {expected_name}, found {path.name}")
        run = load_json(path)
        validate_lab_run(run, order, commit)
        runs.append(run)
    return runs


def summarize(
    output_dir: Path,
    corpus_manifest: Path,
    experiment_manifest: Path,
    arch: str,
    commit: str,
) -> dict[str, Any]:
    runs = load_raw_runs(output_dir, commit)
    position_counts = validate_schedule()

    values: dict[tuple[str, int], dict[str, list[float]]] = {}
    per_run: list[dict[tuple[str, int], dict[str, float]]] = []

    for run in runs:
        one_run: dict[tuple[str, int], dict[str, float]] = {}
        for result in run["results"]:
            experiment_id = result["experimentId"]
            target = next(target for target in TARGETS if f"-{kib(target)}k" in experiment_id)
            workload = result["corpusId"]
            key = (workload, target)
            one = process_metrics(result)
            one_run[key] = one
            bucket = values.setdefault(key, {name: [] for name in one})
            for name, value in one.items():
                bucket[name].append(value)
        per_run.append(one_run)

    rows: list[dict[str, Any]] = []
    for workload in WORKLOADS:
        for target in TARGETS:
            key = (workload, target)
            rows.append(
                {
                    "workload": workload,
                    "target": target,
                    "profileId": PROFILE_BY_TARGET[target][0],
                    "profileFingerprint": PROFILE_BY_TARGET[target][1],
                    "metrics": {
                        name: describe(samples)
                        for name, samples in values[key].items()
                    },
                }
            )

    ratio_rows: list[dict[str, Any]] = []
    pairs = ((65536, 131072), (65536, 262144), (131072, 262144))
    for workload in WORKLOADS:
        for numerator, denominator in pairs:
            for metric in (
                "throughputGiBPerSecond",
                "cpuSecondsPerGiB",
                "allocatedBytesPerGiB",
                "processPeakRssBytes",
            ):
                ratios = []
                for one_run in per_run:
                    left = one_run[(workload, numerator)][metric]
                    right = one_run[(workload, denominator)][metric]
                    if right == 0:
                        raise ValueError(f"zero denominator for {workload} {metric}")
                    ratios.append(left / right)
                ratio_rows.append(
                    {
                        "workload": workload,
                        "numeratorTarget": numerator,
                        "denominatorTarget": denominator,
                        "metric": metric,
                        "distribution": describe(ratios),
                        "aboveOne": sum(value > 1 for value in ratios),
                        "belowOne": sum(value < 1 for value in ratios),
                        "equalOne": sum(value == 1 for value in ratios),
                    }
                )

    first = runs[0]["environment"]
    summary = {
        "schemaVersion": 1,
        "purpose": "#8 runtime guardrail; runtime may veto but does not select the profile",
        "arch": arch,
        "commit": commit,
        "labSchemaVersion": LAB_SCHEMA_VERSION,
        "corpusManifestSha256": sha256_file(corpus_manifest),
        "experimentManifestSha256": sha256_file(experiment_manifest),
        "scheduleSha256": schedule_sha256(),
        "runs": len(runs),
        "withinProcessSamplesPerExperiment": 5,
        "processLevelUnit": "one lab --isolate child result; its streaming metric is the median of five within-process samples",
        "dispersion": "cross-process median, linear-interpolation IQR, median absolute deviation, min/max; raw values retained",
        "environment": first,
        "positionCounts": {str(kib(target)): counts for target, counts in position_counts.items()},
        "rows": rows,
        "pairedRatios": ratio_rows,
    }
    write_json(output_dir / "runtime-summary.json", summary)
    write_markdown(output_dir / "runtime-summary.md", summary)
    return summary


def fmt(value: float) -> str:
    if abs(value) >= 1000:
        return f"{value:,.1f}"
    if abs(value) >= 10:
        return f"{value:.3f}"
    return f"{value:.4f}"


def write_markdown(path: Path, summary: dict[str, Any]) -> None:
    lines = [
        f"# #8 CDC runtime guardrail — {summary['arch']}",
        "",
        f"- commit: `{summary['commit']}`",
        f"- runs: **{summary['runs']}** process-isolated batches",
        f"- corpus manifest SHA-256: `{summary['corpusManifestSha256']}`",
        f"- experiment manifest SHA-256: `{summary['experimentManifestSha256']}`",
        f"- schedule SHA-256: `{summary['scheduleSha256']}`",
        "- runtime is a veto/guardrail, not a profile-selection score",
        "",
        "Each cell is the median across 10 independent child-process results; ± is the cross-process IQR.",
        "Peak RSS uses the maximum process-lifetime peak observed in the reference or streaming samples of that isolated child.",
        "",
        "| workload | target | throughput GiB/s | CPU s/GiB | allocated MiB/GiB | copied B/B | peak RSS MiB |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]

    for row in summary["rows"]:
        metrics = row["metrics"]
        throughput = metrics["throughputGiBPerSecond"]
        cpu = metrics["cpuSecondsPerGiB"]
        allocated = metrics["allocatedBytesPerGiB"]
        copied = metrics["bytesCopiedPerInputByte"]
        rss = metrics["processPeakRssBytes"]
        lines.append(
            "| {workload} | {target} KiB | {tp} ± {tp_iqr} | {cpu} ± {cpu_iqr} | "
            "{alloc} ± {alloc_iqr} | {copied} ± {copied_iqr} | {rss} ± {rss_iqr} |".format(
                workload=row["workload"],
                target=kib(row["target"]),
                tp=fmt(throughput["median"]),
                tp_iqr=fmt(throughput["iqr"]),
                cpu=fmt(cpu["median"]),
                cpu_iqr=fmt(cpu["iqr"]),
                alloc=fmt(allocated["median"] / (1 << 20)),
                alloc_iqr=fmt(allocated["iqr"] / (1 << 20)),
                copied=fmt(copied["median"]),
                copied_iqr=fmt(copied["iqr"]),
                rss=fmt(rss["median"] / (1 << 20)),
                rss_iqr=fmt(rss["iqr"] / (1 << 20)),
            )
        )

    lines.extend(
        [
            "",
            "## Paired process-level ratios",
            "",
            "Ratios are numerator/denominator within the same batch and workload. "
            "For throughput, >1 is faster; for CPU, allocation and RSS, >1 is more expensive.",
            "",
            "| workload | comparison | metric | median ratio | IQR | >1 / <1 |",
            "|---|---|---|---:|---:|---:|",
        ]
    )

    friendly = {
        "throughputGiBPerSecond": "throughput",
        "cpuSecondsPerGiB": "CPU/GiB",
        "allocatedBytesPerGiB": "alloc/GiB",
        "processPeakRssBytes": "peak RSS",
    }
    for row in summary["pairedRatios"]:
        dist = row["distribution"]
        lines.append(
            f"| {row['workload']} | {kib(row['numeratorTarget'])}/{kib(row['denominatorTarget'])} KiB "
            f"| {friendly[row['metric']]} | {dist['median']:.4f} | {dist['iqr']:.4f} "
            f"| {row['aboveOne']} / {row['belowOne']} |"
        )

    lines.extend(
        [
            "",
            "No automatic runtime winner or percentage threshold is applied. "
            "The #8 decision compares any reproducible runtime cost with the already-frozen quality evidence.",
            "",
        ]
    )
    path.write_text("\n".join(lines), encoding="utf-8")


def run_guardrail(args: argparse.Namespace) -> None:
    experiment_manifest = Path(args.experiments).resolve()
    corpus = Path(args.corpus).resolve()
    dll = Path(args.dll).resolve()
    output_dir = Path(args.output_dir).resolve()

    if not args.commit.strip():
        raise ValueError("--commit must be a non-empty tested commit SHA")
    if not corpus.is_file():
        raise ValueError(f"corpus manifest does not exist: {corpus}")
    if not dll.is_file():
        raise ValueError(f"benchmark DLL does not exist: {dll}")

    validate_corpus(corpus)
    base = validate_manifest(experiment_manifest)
    validate_schedule()

    if output_dir.exists() and any(output_dir.iterdir()):
        raise ValueError(f"output directory must be empty: {output_dir}")

    raw_dir = output_dir / "raw"
    manifests_dir = output_dir / "manifests"
    logs_dir = output_dir / "logs"
    raw_dir.mkdir(parents=True, exist_ok=True)
    manifests_dir.mkdir(parents=True, exist_ok=True)
    logs_dir.mkdir(parents=True, exist_ok=True)

    metadata = {
        "schemaVersion": 1,
        "commit": args.commit,
        "arch": args.arch,
        "corpusManifest": str(corpus),
        "corpusManifestSha256": sha256_file(corpus),
        "experimentManifest": str(experiment_manifest),
        "experimentManifestSha256": sha256_file(experiment_manifest),
        "schedule": [[kib(target) for target in order] for order in SCHEDULE],
        "scheduleSha256": schedule_sha256(),
        "positionCounts": {
            str(kib(target)): counts
            for target, counts in validate_schedule().items()
        },
    }
    write_json(output_dir / "execution.json", metadata)

    env = os.environ.copy()
    env["GITHUB_SHA"] = args.commit

    for index, order in enumerate(SCHEDULE, start=1):
        ordered = ordered_manifest(base, order)
        run_manifest = manifests_dir / f"run-{index:02d}.json"
        run_output = raw_dir / f"run-{index:02d}.json"
        run_log = logs_dir / f"run-{index:02d}.log"
        write_json(run_manifest, ordered)

        command = [
            args.dotnet,
            str(dll),
            "lab",
            "--corpus",
            str(corpus),
            "--experiments",
            str(run_manifest),
            "--output",
            str(run_output),
            "--isolate",
        ]
        print(
            f"run {index:02d}/10: "
            + " -> ".join(f"{kib(target)} KiB" for target in order),
            flush=True,
        )
        with run_log.open("w", encoding="utf-8") as log:
            completed = subprocess.run(
                command,
                stdout=log,
                stderr=subprocess.STDOUT,
                env=env,
                timeout=args.batch_timeout_seconds,
                check=False,
                text=True,
            )
        if completed.returncode != 0:
            raise RuntimeError(
                f"runtime batch {index:02d} failed with exit code {completed.returncode}; see {run_log}"
            )

        run = load_json(run_output)
        validate_lab_run(run, order, args.commit)

    summary = summarize(output_dir, corpus, experiment_manifest, args.arch, args.commit)
    print((output_dir / "runtime-summary.md").read_text(encoding="utf-8"))
    print(f"summary rows: {len(summary['rows'])}", flush=True)


def compare_architectures(args: argparse.Namespace) -> None:
    x64_dir = Path(args.x64_dir).resolve()
    arm64_dir = Path(args.arm64_dir).resolve()
    output = Path(args.output).resolve()

    x64_summary = load_json(x64_dir / "runtime-summary.json")
    arm_summary = load_json(arm64_dir / "runtime-summary.json")

    for field in ("commit", "corpusManifestSha256", "experimentManifestSha256", "scheduleSha256", "runs"):
        if x64_summary.get(field) != arm_summary.get(field):
            raise ValueError(f"cross-architecture {field} differs")

    if x64_summary.get("arch") != "x64" or arm_summary.get("arch") != "arm64":
        raise ValueError("cross-architecture inputs are not labelled x64/arm64")

    compared = 0
    for index, order in enumerate(SCHEDULE, start=1):
        left = load_json(x64_dir / "raw" / f"run-{index:02d}.json")
        right = load_json(arm64_dir / "raw" / f"run-{index:02d}.json")
        left_by_id = {r["experimentId"]: r for r in left["results"]}
        right_by_id = {r["experimentId"]: r for r in right["results"]}

        expected = expected_ids(order)
        if list(left_by_id) != expected or list(right_by_id) != expected:
            raise ValueError(f"run {index:02d}: result order differs from the schedule")

        for experiment_id in expected:
            l = left_by_id[experiment_id]
            r = right_by_id[experiment_id]
            if l["definitionFingerprint"] != r["definitionFingerprint"]:
                raise ValueError(f"{experiment_id}: definition fingerprint differs across architectures")
            for field in (
                "sourceSha256",
                "targetSha256",
                "sourceStreamingChunkSequenceSha256",
                "targetStreamingChunkSequenceSha256",
            ):
                if l["evidence"][field] != r["evidence"][field]:
                    raise ValueError(
                        f"run {index:02d} {experiment_id}: evidence {field} differs across architectures"
                    )
            compared += 1

    lines = [
        "# #8 CDC runtime cross-architecture evidence",
        "",
        f"- commit: `{x64_summary['commit']}`",
        f"- schedule SHA-256: `{x64_summary['scheduleSha256']}`",
        f"- compared process results: **{compared}**",
        "- x64 and arm64 used the same experiment definitions, source/target bytes, and streaming chunk-sequence digests",
        "- performance numbers remain architecture-specific and are not pooled",
        "",
    ]
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))


def self_test() -> None:
    counts = validate_schedule()
    if sorted(counts[65536]) != [3, 3, 4]:
        raise AssertionError(counts)
    if sorted(counts[131072]) != [3, 3, 4]:
        raise AssertionError(counts)
    if sorted(counts[262144]) != [3, 3, 4]:
        raise AssertionError(counts)

    sample = [float(value) for value in range(1, 11)]
    stats = describe(sample)
    if stats["median"] != 5.5 or abs(stats["iqr"] - 4.5) > 1e-12 or stats["mad"] != 2.5:
        raise AssertionError(stats)

    print("runtime guardrail self-test passed")


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate_parser = subparsers.add_parser("validate")
    validate_parser.add_argument("--corpus", required=True)
    validate_parser.add_argument("--experiments", required=True)

    run_parser = subparsers.add_parser("run")
    run_parser.add_argument("--dotnet", default="dotnet")
    run_parser.add_argument("--dll", required=True)
    run_parser.add_argument("--corpus", required=True)
    run_parser.add_argument("--experiments", required=True)
    run_parser.add_argument("--output-dir", required=True)
    run_parser.add_argument("--commit", required=True)
    run_parser.add_argument("--arch", choices=("x64", "arm64"), required=True)
    run_parser.add_argument("--batch-timeout-seconds", type=int, default=1800)

    compare_parser = subparsers.add_parser("compare")
    compare_parser.add_argument("--x64-dir", required=True)
    compare_parser.add_argument("--arm64-dir", required=True)
    compare_parser.add_argument("--output", required=True)

    subparsers.add_parser("self-test")

    args = parser.parse_args()

    try:
        if args.command == "validate":
            corpus = Path(args.corpus).resolve()
            manifest = Path(args.experiments).resolve()
            validate_corpus(corpus)
            validate_manifest(manifest)
            counts = validate_schedule()
            print(f"corpus SHA-256: {sha256_file(corpus)}")
            print(f"manifest SHA-256: {sha256_file(manifest)}")
            print(f"schedule SHA-256: {schedule_sha256()}")
            print("position counts: " + json.dumps({str(kib(k)): v for k, v in counts.items()}, sort_keys=True))
        elif args.command == "run":
            run_guardrail(args)
        elif args.command == "compare":
            compare_architectures(args)
        elif args.command == "self-test":
            self_test()
        else:
            raise AssertionError(args.command)
    except (ValueError, RuntimeError, OSError, subprocess.SubprocessError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
