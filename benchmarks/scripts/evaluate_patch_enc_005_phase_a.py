#!/usr/bin/env python3
"""Compile PATCH-ENC-005 Phase-A calibration evidence into a frozen decision record."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import shutil

import patch_enc_005_apply_evidence as apply_evidence_validator
from pathlib import Path

EXPERIMENT_ID = "PATCH-ENC-005"
PROTOCOL_COMMIT = "96fd9b296d6998cac397e61041f22df51e6dd43c"
DATASET_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
LANES = (
    "H4-L1-R2",
    "H7-L1-R2-E75",
    "H9-L9-K4-C16-R1M",
    "H9-L12-K4-C16-R1M",
    "H9-L15-K4-C16-R1M",
)
ALL_LANES = ("csp", *LANES)
CREATE_BOUND_BYTES = 96 * 1024 * 1024
APPLY_BOUND_BYTES = 64 * 1024 * 1024
RUN_RE = re.compile(
    r"^PATCH-ENC-005/RUN-(\d{8})-(\d{3})-([0-9a-f]{40})-"
    r"(linux-x64|linux-arm64|win-x64)$"
)


def canonical_bytes(value: object) -> bytes:
    return json.dumps(
        value, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"{path}: expected a JSON object")
    return value


def parse_bindings(values: list[str], name: str) -> dict[str, Path]:
    result: dict[str, Path] = {}
    for value in values:
        platform, separator, raw_path = value.partition("=")
        if not separator or platform not in PLATFORMS or not raw_path:
            raise ValueError(f"{name} must use platform=path; got {value!r}")
        if platform in result:
            raise ValueError(f"{name}: duplicate platform {platform}")
        result[platform] = Path(raw_path)
    if set(result) != set(PLATFORMS):
        raise ValueError(f"{name}: expected exactly {PLATFORMS}")
    return result


def median_five(values: list[float]) -> float:
    if len(values) != 5:
        raise ValueError("frozen timing requires exactly five ratios")
    return sorted(values)[2]


def positive(value: object, label: str) -> float:
    number = float(value)
    if not math.isfinite(number) or number <= 0:
        raise ValueError(f"{label}: expected finite positive value")
    return number


def projection(document: dict, label: str) -> dict[str, dict[tuple[str, str, str, str], str]]:
    rows_by_lane = document.get("acceptedPatchShas")
    if not isinstance(rows_by_lane, dict) or set(rows_by_lane) != set(ALL_LANES):
        raise ValueError(f"{label}: acceptedPatchShas lane set mismatch")
    result: dict[str, dict[tuple[str, str, str, str], str]] = {}
    common: set[tuple[str, str, str, str]] | None = None
    for lane in ALL_LANES:
        lane_rows = rows_by_lane[lane]
        if not isinstance(lane_rows, list) or not lane_rows:
            raise ValueError(f"{label}/{lane}: empty patch projection")
        lane_map: dict[tuple[str, str, str, str], str] = {}
        for row in lane_rows:
            key = (row["family"], row["base"], row["target"], row["path"])
            digest = str(row["patchSha256"]).lower()
            if len(digest) != 64 or any(ch not in "0123456789abcdef" for ch in digest):
                raise ValueError(f"{label}/{lane}/{key}: invalid patch SHA-256")
            if key in lane_map:
                raise ValueError(f"{label}/{lane}: duplicate file identity")
            lane_map[key] = digest
        if common is None:
            common = set(lane_map)
        elif set(lane_map) != common:
            raise ValueError(f"{label}/{lane}: file set mismatch")
        result[lane] = lane_map
    return result


def safe_child(root: Path, relative: str) -> Path:
    root = root.resolve()
    candidate = (root / relative).resolve()
    if candidate != root and root not in candidate.parents:
        raise ValueError(f"evidence path escapes artifact root: {relative}")
    return candidate


def compact_file_rows(
    paired_path: Path,
    document: dict,
    accepted: dict[str, dict[tuple[str, str, str, str], str]],
) -> list[dict]:
    trace = document.get("traceCorrectness")
    if not isinstance(trace, dict) or set(trace) != set(ALL_LANES):
        raise ValueError(f"{paired_path}: traceCorrectness lane set mismatch")
    result: list[dict] = []
    for lane in ALL_LANES:
        lane_trace = trace[lane]
        run_path = safe_child(paired_path.parent, str(lane_trace.get("result", "")))
        run = load_json(run_path)
        if run.get("schema") != "chunkshift.patch-lab.v1" or run.get("lane") != lane:
            raise ValueError(f"{run_path}: trace run identity mismatch")
        run_rows: dict[tuple[str, str, str, str], dict] = {}
        for row in run.get("files", []):
            key = (row["family"], row["base"], row["target"], row["path"])
            if key in run_rows:
                raise ValueError(f"{run_path}: duplicate file")
            run_rows[key] = row
        if set(run_rows) != set(accepted[lane]):
            raise ValueError(f"{run_path}: file set differs from accepted timing")

        correctness_rows = lane_trace.get("correctness")
        if not isinstance(correctness_rows, list):
            raise ValueError(f"{paired_path}/{lane}: correctness rows missing")
        correctness = {
            (row["family"], row["base"], row["target"], row["path"]): row
            for row in correctness_rows
        }
        if len(correctness) != len(correctness_rows) or set(correctness) != set(run_rows):
            raise ValueError(f"{paired_path}/{lane}: correctness file set mismatch")

        for key in sorted(run_rows):
            run_row = run_rows[key]
            patch_sha = str(run_row.get("patchSha256", "")).lower()
            if patch_sha != accepted[lane][key]:
                raise ValueError(f"{paired_path}/{lane}/{key}: patch SHA mismatch")
            patch_bytes = int(run_row.get("patchBytes", -1))
            check = correctness[key]
            target_sha = str(check.get("targetSha256", "")).lower()
            decoder_sha = str(check.get("decoderOutputSha256", "")).lower()
            if (
                patch_bytes < 0
                or check.get("verdict") != "valid"
                or len(target_sha) != 64
                or target_sha != decoder_sha
            ):
                raise ValueError(f"{paired_path}/{lane}/{key}: correctness oracle failed")
            result.append(
                {
                    "lane": lane,
                    "family": key[0],
                    "base": key[1],
                    "target": key[2],
                    "path": key[3],
                    "patchBytes": patch_bytes,
                    "patchSha256": patch_sha,
                    "targetSha256": target_sha,
                    "correctness": "valid",
                }
            )
    return result


def compact_aggregate(value: dict, label: str) -> dict:
    result = {
        "wallSeconds": positive(value.get("wallSeconds"), f"{label}/wall"),
        "cpuSeconds": positive(value.get("cpuSeconds"), f"{label}/cpu"),
        "patchBytes": int(value.get("patchBytes", -1)),
        "allocatedBytes": int(value.get("allocatedBytes", -1)),
        "baseReads": int(value.get("baseReads", -1)),
        "baseBytesRead": int(value.get("baseBytesRead", -1)),
        "baseSeeks": int(value.get("baseSeeks", -1)),
    }
    if any(
        result[name] < 0
        for name in ("patchBytes", "allocatedBytes", "baseReads", "baseBytesRead", "baseSeeks")
    ):
        raise ValueError(f"{label}: aggregate counters must be non-negative")
    return result


def validate_environment(
    platform: str,
    environment: object,
    source_commit: str,
    label: str,
) -> dict:
    if not isinstance(environment, dict):
        raise ValueError(f"{label}: environment snapshot is missing")
    if (
        str(environment.get("gitCommit") or "").lower() != source_commit
        or int(environment.get("processorCount") or 0) < 2
    ):
        raise ValueError(f"{label}: environment source/processor binding mismatch")

    expected_arch = {
        "linux-x64": "x64",
        "linux-arm64": "arm64",
        "win-x64": "x64",
    }[platform]
    process_arch = str(environment.get("processArchitecture") or "").lower()
    os_arch = str(environment.get("osArchitecture") or "").lower()
    os_description = str(environment.get("osDescription") or "")
    framework = str(environment.get("frameworkDescription") or "")
    processor = str(environment.get("processorDescription") or "")
    if process_arch != expected_arch or os_arch != expected_arch:
        raise ValueError(f"{label}: architecture does not match platform {platform}")
    if platform.startswith("linux-"):
        if "linux" not in os_description.lower():
            raise ValueError(f"{label}: OS does not match Linux platform")
    elif "windows" not in os_description.lower():
        raise ValueError(f"{label}: OS does not match Windows platform")
    if not framework.strip() or not processor.strip():
        raise ValueError(f"{label}: runtime/processor provenance is incomplete")
    return environment


def validate_paired(platform: str, path: Path, source_commit: str) -> tuple[dict, list[dict]]:
    document = load_json(path)
    label = f"{platform}:{path}"
    if document.get("schema") != "chunkshift.patch-enc-005-paired.v2":
        raise ValueError(f"{label}: unexpected schema")
    if (
        document.get("experimentId") != EXPERIMENT_ID
        or document.get("protocolCommit") != PROTOCOL_COMMIT
        or str(document.get("sourceCommit", "")).lower() != source_commit
        or document.get("platform") != platform
        or document.get("datasetRole") != "calibration"
        or document.get("datasetSha256") != DATASET_SHA256
        or document.get("status") != "VALID"
    ):
        raise ValueError(f"{label}: evidence identity mismatch")

    run_id = str(document.get("runId", ""))
    match = RUN_RE.fullmatch(run_id)
    if match is None or match.group(3) != source_commit or match.group(4) != platform:
        raise ValueError(f"{label}: invalid RunId")
    if str(document.get("githubRunAttempt", "")) != "1":
        raise ValueError(f"{label}: GitHub workflow re-run evidence is not allowed")

    accepted_timing = document.get("acceptedTiming")
    if not isinstance(accepted_timing, dict):
        raise ValueError(f"{label}: acceptedTiming missing")
    rounds = accepted_timing.get("rounds")
    summaries = accepted_timing.get("summaries")
    if not isinstance(rounds, list) or len(rounds) != 5 or not isinstance(summaries, list):
        raise ValueError(f"{label}: frozen five-round evidence missing")

    summary_by_lane = {row.get("lane"): row for row in summaries}
    if set(summary_by_lane) != set(LANES):
        raise ValueError(f"{label}: timing lane set mismatch")

    h0_bytes: set[int] = set()
    lane_bytes: dict[str, set[int]] = {lane: set() for lane in LANES}
    computed_wall: dict[str, list[float]] = {lane: [] for lane in LANES}
    computed_cpu: dict[str, list[float]] = {lane: [] for lane in LANES}
    compact_rounds: list[dict] = []

    for expected_round, round_item in enumerate(rounds, 1):
        if int(round_item.get("round", -1)) != expected_round:
            raise ValueError(f"{label}: round ordinal mismatch")
        h0_start = compact_aggregate(round_item["h0Start"], f"{label}/round-{expected_round}/h0-start")
        h0_end = compact_aggregate(round_item["h0End"], f"{label}/round-{expected_round}/h0-end")
        h0_bytes.update((h0_start["patchBytes"], h0_end["patchBytes"]))

        wall_denominator = (h0_start["wallSeconds"] + h0_end["wallSeconds"]) / 2.0
        cpu_denominator = (h0_start["cpuSeconds"] + h0_end["cpuSeconds"]) / 2.0
        noisy = abs(h0_start["wallSeconds"] - h0_end["wallSeconds"]) > 0.25 * wall_denominator
        if round_item.get("bracketNoisy") is not noisy:
            raise ValueError(f"{label}: bracket noise flag does not recompute")
        if noisy:
            raise ValueError(f"{label}: accepted timing contains a noisy bracket")

        candidates = round_item.get("candidates")
        if not isinstance(candidates, list):
            raise ValueError(f"{label}: candidate rows missing")
        seen: set[str] = set()
        compact_candidates: list[dict] = []
        for candidate in candidates:
            lane = candidate.get("lane")
            if lane not in LANES or lane in seen:
                raise ValueError(f"{label}: invalid/duplicate candidate lane")
            seen.add(lane)
            aggregate = compact_aggregate(
                candidate["aggregate"], f"{label}/round-{expected_round}/{lane}"
            )
            lane_bytes[lane].add(aggregate["patchBytes"])
            wall_ratio = aggregate["wallSeconds"] / wall_denominator
            cpu_ratio = aggregate["cpuSeconds"] / cpu_denominator
            if not math.isclose(wall_ratio, float(candidate.get("wallRatio")), rel_tol=1e-12, abs_tol=1e-12):
                raise ValueError(f"{label}/{lane}: round wall ratio does not recompute")
            if not math.isclose(cpu_ratio, float(candidate.get("cpuRatio")), rel_tol=1e-12, abs_tol=1e-12):
                raise ValueError(f"{label}/{lane}: round CPU ratio does not recompute")
            computed_wall[lane].append(wall_ratio)
            computed_cpu[lane].append(cpu_ratio)
            compact_candidates.append(
                {
                    "lane": lane,
                    "aggregate": aggregate,
                    "wallRatio": wall_ratio,
                    "cpuRatio": cpu_ratio,
                }
            )
        if seen != set(LANES):
            raise ValueError(f"{label}: each round must contain all Phase-A lanes")

        compact_rounds.append(
            {
                "round": expected_round,
                "candidateOrder": [row["lane"] for row in compact_candidates],
                "h0Start": h0_start,
                "h0End": h0_end,
                "candidates": compact_candidates,
            }
        )

    if len(h0_bytes) != 1 or any(len(values) != 1 for values in lane_bytes.values()):
        raise ValueError(f"{label}: patch bytes changed between measured rounds")
    patch_bytes = {"csp": next(iter(h0_bytes))}
    patch_bytes.update({lane: next(iter(values)) for lane, values in lane_bytes.items()})

    timing: dict[str, dict] = {}
    for lane in LANES:
        row = summary_by_lane[lane]
        walls = [positive(value, f"{label}/{lane}/wall") for value in row.get("wallRatios", [])]
        cpus = [positive(value, f"{label}/{lane}/cpu") for value in row.get("cpuRatios", [])]
        if len(walls) != 5 or len(cpus) != 5:
            raise ValueError(f"{label}/{lane}: five wall/cpu ratios required")
        for actual, expected in zip(walls, computed_wall[lane], strict=True):
            if not math.isclose(actual, expected, rel_tol=1e-12, abs_tol=1e-12):
                raise ValueError(f"{label}/{lane}: summary wall ratios differ from raw rounds")
        for actual, expected in zip(cpus, computed_cpu[lane], strict=True):
            if not math.isclose(actual, expected, rel_tol=1e-12, abs_tol=1e-12):
                raise ValueError(f"{label}/{lane}: summary CPU ratios differ from raw rounds")
        wall_median = median_five(computed_wall[lane])
        cpu_median = median_five(computed_cpu[lane])
        if not math.isclose(wall_median, float(row.get("wallRatioMedian")), rel_tol=1e-12, abs_tol=1e-12):
            raise ValueError(f"{label}/{lane}: wall median does not recompute")
        if not math.isclose(cpu_median, float(row.get("cpuRatioMedian")), rel_tol=1e-12, abs_tol=1e-12):
            raise ValueError(f"{label}/{lane}: CPU median does not recompute")
        timing[lane] = {
            "wallRatios": computed_wall[lane],
            "cpuRatios": computed_cpu[lane],
            "wallRatioMedian": wall_median,
            "cpuRatioMedian": cpu_median,
        }

    timing_environment = validate_environment(
        platform,
        document.get("timingEnvironment"),
        source_commit,
        f"{label}/timing",
    )

    accepted = projection(document, label)
    apply_wall, apply_raw, apply_environment = apply_evidence_validator.validate(
        document.get("applyEvidence"),
        accepted,
        platform,
        source_commit,
        ALL_LANES,
        label,
    )

    if accepted["H7-L1-R2-E75"] != accepted["H4-L1-R2"]:
        raise ValueError(f"{label}: H7 is not byte-identical to H4")
    files = compact_file_rows(path, document, accepted)
    sums = {lane: 0 for lane in ALL_LANES}
    for row in files:
        sums[row["lane"]] += int(row["patchBytes"])
    if sums != patch_bytes:
        raise ValueError(f"{label}: aggregate patch bytes do not match compact per-file rows")

    return {
        "runId": run_id,
        "runDate": match.group(1),
        "runSequence": match.group(2),
        "githubRunId": str(document.get("githubRunId", "")),
        "githubRunNumber": str(document.get("githubRunNumber", "")),
        "patchBytes": patch_bytes,
        "rounds": compact_rounds,
        "timing": timing,
        "applyWallSeconds": apply_wall,
        "applyRaw": apply_raw,
        "timingEnvironment": timing_environment,
        "applyEnvironment": apply_environment,
        "acceptedPatchShas": accepted,
    }, files

def validate_memory(
    platform: str,
    directory: Path,
    source_commit: str,
    expected_run_id: str,
) -> tuple[dict[str, dict], dict, str]:
    result: dict[str, dict] = {}
    common_environment = None
    common_population: list[dict] | None = None

    for lane in ALL_LANES:
        path = directory / f"{lane}.json"
        document = load_json(path)
        label = f"{platform}:{path}"
        if (
            document.get("schema") != "chunkshift.patch-lab-memory.v1"
            or document.get("lane") != lane
            or document.get("runId") != expected_run_id
            or document.get("corpusPairsSha256") != DATASET_SHA256
            or document.get("execution") != "h2-w2"
            or document.get("population") != "max-base-target"
            or document.get("applyCheck") != "boundary"
        ):
            raise ValueError(f"{label}: memory identity/configuration mismatch")

        environment = validate_environment(
            platform,
            document.get("environment"),
            source_commit,
            f"{label}/memory",
        )
        if common_environment is None:
            common_environment = environment
        elif environment != common_environment:
            raise ValueError(f"{label}: memory environment differs between lanes")

        idle = int(document.get("idleBaselineBytes", -1))
        files = document.get("files")
        if idle <= 0 or not isinstance(files, list) or not files:
            raise ValueError(f"{label}: incomplete memory population")

        create: list[int] = []
        apply: list[int] = []
        compact_files: list[dict] = []
        population_rows: list[dict] = []
        seen_files: set[tuple[str, str, str, str]] = set()
        for row in files:
            key = (row["family"], row["base"], row["target"], row["path"])
            if key in seen_files:
                raise ValueError(f"{label}: duplicate memory file {key}")
            seen_files.add(key)
            create_peak = int(row.get("createPeakBytes", -1))
            apply_peak = int(row.get("applyPeakBytes", -1))
            base_size = int(row.get("baseSize", -1))
            target_size = int(row.get("targetSize", -1))
            if (
                create_peak < idle
                or apply_peak < idle
                or max(base_size, target_size) < 1048576
            ):
                raise ValueError(f"{label}: invalid memory population row {key}")
            create_over_idle = create_peak - idle
            apply_over_idle = apply_peak - idle
            create.append(create_over_idle)
            apply.append(apply_over_idle)
            identity = {
                "family": key[0],
                "base": key[1],
                "target": key[2],
                "path": key[3],
                "baseSize": base_size,
                "targetSize": target_size,
            }
            population_rows.append(identity)
            compact_files.append(
                {
                    **identity,
                    "createPeakOverIdleBytes": create_over_idle,
                    "applyPeakOverIdleBytes": apply_over_idle,
                }
            )

        population_rows.sort(
            key=lambda row: (row["family"], row["base"], row["target"], row["path"])
        )
        compact_files.sort(
            key=lambda row: (row["family"], row["base"], row["target"], row["path"])
        )
        if common_population is None:
            common_population = population_rows
        elif population_rows != common_population:
            raise ValueError(f"{label}: memory population differs between lanes")

        result[lane] = {
            "fileCount": len(files),
            "idleBaselineBytes": idle,
            "createPeakOverIdleBytes": max(create),
            "applyPeakOverIdleBytes": max(apply),
            "files": compact_files,
        }

    assert common_environment is not None and common_population is not None
    population_sha256 = hashlib.sha256(canonical_bytes(common_population)).hexdigest()
    return result, common_environment, population_sha256

def validate_cross_platform(
    path: Path,
    source_commit: str,
    maps: dict[str, dict[str, dict[tuple[str, str, str, str], str]]],
) -> None:
    document = load_json(path)
    if (
        document.get("schema") != "chunkshift.patch-enc-005-cross-platform.v1"
        or document.get("experimentId") != EXPERIMENT_ID
        or document.get("protocolCommit") != PROTOCOL_COMMIT
        or str(document.get("sourceCommit", "")).lower() != source_commit
        or document.get("dataset") != f"calibration:{DATASET_SHA256}"
        or document.get("platforms") != sorted(PLATFORMS)
        or document.get("patchBytesEqual") is not True
    ):
        raise ValueError("cross-platform gate identity/verdict mismatch")
    expected = canonical_bytes(document.get("acceptedPatchShas"))
    for platform in PLATFORMS:
        serialized = {
            lane: [
                {
                    "family": key[0],
                    "base": key[1],
                    "target": key[2],
                    "path": key[3],
                    "patchSha256": digest,
                }
                for key, digest in sorted(maps[platform][lane].items())
            ]
            for lane in sorted(maps[platform])
        }
        if canonical_bytes(serialized) != expected:
            raise ValueError(f"cross-platform patch map differs from {platform}")


def evaluate(compact: dict) -> dict:
    if (
        compact.get("schema") != "chunkshift.patch-enc-005-phase-a-compact.v1"
        or compact.get("experimentId") != EXPERIMENT_ID
        or compact.get("protocolCommit") != PROTOCOL_COMMIT
        or compact.get("datasetRole") != "calibration"
        or compact.get("datasetSha256") != DATASET_SHA256
    ):
        raise ValueError("compact evidence identity does not match frozen PATCH-ENC-005 calibration")
    patch_bytes = compact["patchBytes"]
    h0_bytes = int(patch_bytes["csp"])
    if h0_bytes <= 0:
        raise ValueError("H0 patch bytes must be positive")
    for platform in PLATFORMS:
        h0_memory = compact["platforms"][platform]["memory"]["csp"]
        if (
            int(h0_memory["createPeakOverIdleBytes"]) > CREATE_BOUND_BYTES
            or int(h0_memory["applyPeakOverIdleBytes"]) > APPLY_BOUND_BYTES
        ):
            raise ValueError(f"{platform}: H0 violates frozen memory bounds")

    lanes: dict[str, dict] = {}

    for lane in LANES:
        lane_patch_bytes = int(patch_bytes[lane])
        byte_ratio = lane_patch_bytes / h0_bytes
        wall = {
            platform: float(compact["platforms"][platform]["timing"][lane]["wallRatioMedian"])
            for platform in PLATFORMS
        }
        cpu = {
            platform: float(compact["platforms"][platform]["timing"][lane]["cpuRatioMedian"])
            for platform in PLATFORMS
        }
        wall_rounds = {
            platform: [
                float(value)
                for value in compact["platforms"][platform]["timing"][lane]["wallRatios"]
            ]
            for platform in PLATFORMS
        }
        if any(len(values) != 5 for values in wall_rounds.values()):
            raise ValueError(f"{lane}: frozen five-round wall ratios are missing")
        speed_round_passes = {
            platform: sum(value <= 0.50 for value in values)
            for platform, values in wall_rounds.items()
        }
        size_round_passes = {
            platform: sum(value <= 1.50 for value in values)
            for platform, values in wall_rounds.items()
        }
        apply_ratio = {}
        create_memory = {}
        apply_memory = {}
        for platform in PLATFORMS:
            platform_data = compact["platforms"][platform]
            h0_apply = float(platform_data["applyWallSeconds"]["csp"])
            apply_ratio[platform] = float(platform_data["applyWallSeconds"][lane]) / h0_apply
            create_memory[platform] = int(platform_data["memory"][lane]["createPeakOverIdleBytes"])
            apply_memory[platform] = int(platform_data["memory"][lane]["applyPeakOverIdleBytes"])

        apply_ok = all(value <= 1.10 for value in apply_ratio.values())
        memory_ok = (
            all(value <= CREATE_BOUND_BYTES for value in create_memory.values())
            and all(value <= APPLY_BOUND_BYTES for value in apply_memory.values())
        )
        speed = (
            lane_patch_bytes * 100 <= h0_bytes * 102
            and all(value <= 0.50 for value in wall.values())
            and all(value >= 4 for value in speed_round_passes.values())
        )
        size = (
            lane_patch_bytes * 100 <= h0_bytes * 97
            and all(value <= 1.50 for value in wall.values())
            and all(value >= 4 for value in size_round_passes.values())
        )
        branches = [name for name, ok in (("speed", speed), ("size", size)) if ok]
        eligible = apply_ok and memory_ok and bool(branches)
        reasons = []
        if not apply_ok:
            reasons.append("apply>1.10")
        if not memory_ok:
            reasons.append("memory-bound")
        if not branches:
            reasons.append("no-qualification-branch")
        lanes[lane] = {
            "patchBytes": lane_patch_bytes,
            "byteRatio": byte_ratio,
            "wallRatioByPlatform": wall,
            "cpuRatioByPlatform": cpu,
            "speedWallRoundPassesByPlatform": speed_round_passes,
            "sizeWallRoundPassesByPlatform": size_round_passes,
            "applyRatioByPlatform": apply_ratio,
            "createPeakOverIdleBytesByPlatform": create_memory,
            "applyPeakOverIdleBytesByPlatform": apply_memory,
            "maxWallRatio": max(wall.values()),
            "maxCpuRatio": max(cpu.values()),
            "qualificationBranches": branches,
            "applyOk": apply_ok,
            "memoryOk": memory_ok,
            "eligible": eligible,
            "pareto": False,
            "reasons": reasons,
        }

    eligible = [lane for lane in LANES if lanes[lane]["eligible"]]
    pareto = []
    for lane in eligible:
        row = lanes[lane]
        dominated = False
        for other in eligible:
            if other == lane:
                continue
            candidate = lanes[other]
            no_worse = (
                candidate["byteRatio"] <= row["byteRatio"]
                and candidate["maxWallRatio"] <= row["maxWallRatio"]
                and candidate["maxCpuRatio"] <= row["maxCpuRatio"]
            )
            better = (
                candidate["byteRatio"] < row["byteRatio"]
                or candidate["maxWallRatio"] < row["maxWallRatio"]
                or candidate["maxCpuRatio"] < row["maxCpuRatio"]
            )
            if no_worse and better:
                dominated = True
                break
        if not dominated:
            pareto.append(lane)
            lanes[lane]["pareto"] = True

    speed_finalist = None
    size_finalist = None
    if pareto:
        speed_finalist = min(
            pareto,
            key=lambda lane: (
                lanes[lane]["maxWallRatio"],
                lanes[lane]["byteRatio"],
                lanes[lane]["maxCpuRatio"],
                lane,
            ),
        )
        size_finalist = min(
            pareto,
            key=lambda lane: (
                lanes[lane]["byteRatio"],
                lanes[lane]["maxWallRatio"],
                lanes[lane]["maxCpuRatio"],
                lane,
            ),
        )
    finalists = []
    for lane in (speed_finalist, size_finalist):
        if lane is not None and lane not in finalists:
            finalists.append(lane)

    return {
        "schema": "chunkshift.patch-enc-005-phase-a-calibration-verdict.v1",
        "experimentId": EXPERIMENT_ID,
        "protocolCommit": PROTOCOL_COMMIT,
        "sourceCommit": compact["sourceCommit"],
        "datasetRole": "calibration",
        "datasetSha256": DATASET_SHA256,
        "h0PatchBytes": h0_bytes,
        "lanes": lanes,
        "pareto": pareto,
        "speedFinalist": speed_finalist,
        "sizeFinalist": size_finalist,
        "finalists": finalists,
        "status": "READY_FOR_FIXED_EVALUATION" if finalists else "REJECT",
    }


def build_compact(
    paired_paths: dict[str, Path],
    memory_paths: dict[str, Path],
    cross_platform: Path,
    source_commit: str,
) -> tuple[dict, list[dict]]:
    platforms: dict[str, dict] = {}
    maps: dict[str, dict] = {}
    canonical_files = None
    identity = None
    patch_bytes = None

    for platform in PLATFORMS:
        paired, files = validate_paired(platform, paired_paths[platform], source_commit)
        current_identity = (
            paired["runDate"],
            paired["runSequence"],
            paired["githubRunId"],
            paired["githubRunNumber"],
        )
        if identity is None:
            identity = current_identity
        elif current_identity != identity:
            raise ValueError("paired evidence identity differs across platforms")
        if patch_bytes is None:
            patch_bytes = paired["patchBytes"]
        elif patch_bytes != paired["patchBytes"]:
            raise ValueError("aggregate patch bytes differ across platforms")

        files = sorted(
            files,
            key=lambda row: (
                row["lane"], row["family"], row["base"], row["target"], row["path"]
            ),
        )
        if canonical_files is None:
            canonical_files = files
        elif files != canonical_files:
            raise ValueError(f"per-file compact evidence differs across platforms ({platform})")

        maps[platform] = paired["acceptedPatchShas"]
        memory, memory_environment, population_sha256 = validate_memory(
            platform, memory_paths[platform], source_commit, paired["runId"]
        )
        platforms[platform] = {
            "runId": paired["runId"],
            "timingEnvironment": paired["timingEnvironment"],
            "applyEnvironment": paired["applyEnvironment"],
            "memoryEnvironment": memory_environment,
            "memoryPopulationSha256": population_sha256,
            "rounds": paired["rounds"],
            "timing": paired["timing"],
            "applyWallSeconds": paired["applyWallSeconds"],
            "applyRaw": paired["applyRaw"],
            "memory": memory,
        }

    assert identity is not None and patch_bytes is not None and canonical_files is not None
    population_hashes = {
        platforms[platform]["memoryPopulationSha256"] for platform in PLATFORMS
    }
    if len(population_hashes) != 1:
        raise ValueError("memory population differs across platforms")
    cross = load_json(cross_platform)
    validate_cross_platform(cross_platform, source_commit, maps)
    if (
        str(cross.get("date", "")) != identity[0]
        or str(cross.get("sequence", "")) != identity[1]
        or str(cross.get("githubRunId", "")) != identity[2]
        or str(cross.get("githubRunNumber", "")) != identity[3]
        or str(cross.get("githubRunAttempt", "")) != "1"
    ):
        raise ValueError("cross-platform gate run identity differs from paired evidence")
    return {
        "schema": "chunkshift.patch-enc-005-phase-a-compact.v1",
        "experimentId": EXPERIMENT_ID,
        "protocolCommit": PROTOCOL_COMMIT,
        "sourceCommit": source_commit,
        "datasetRole": "calibration",
        "datasetSha256": DATASET_SHA256,
        "runDate": identity[0],
        "runSequence": identity[1],
        "githubRunId": identity[2],
        "githubRunNumber": identity[3],
        "patchBytes": patch_bytes,
        "platforms": platforms,
        "byteOracles": {
            "H7-L1-R2-E75==H4-L1-R2": True,
            "crossPlatformPatchSha256Equal": True,
        },
    }, canonical_files


def write_outputs(output: Path, compact: dict, files: list[dict]) -> None:
    output.mkdir(parents=True, exist_ok=False)
    compact_path = output / "compact.json"
    compact_path.write_bytes(canonical_bytes(compact) + b"\n")

    files_path = output / "files.jsonl"
    with files_path.open("w", encoding="utf-8", newline="\n") as stream:
        for row in files:
            stream.write(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")

    recompute_source = Path(__file__).with_name("recompute_patch_enc_005_phase_a.py")
    if not recompute_source.is_file():
        raise ValueError(f"missing independent recomputation script {recompute_source}")
    recompute_path = output / "recompute.py"
    shutil.copyfile(recompute_source, recompute_path)

    verdict = evaluate(compact)
    verdict["compactSha256"] = sha256_file(compact_path)
    verdict["filesSha256"] = sha256_file(files_path)
    verdict["recomputeSha256"] = sha256_file(recompute_path)
    verdict_path = output / "verdict.json"
    verdict_path.write_bytes(canonical_bytes(verdict) + b"\n")

    selection = {
        "schema": "chunkshift.patch-enc-005-fixed-evaluation-selection.v1",
        "experimentId": EXPERIMENT_ID,
        "protocolCommit": PROTOCOL_COMMIT,
        "sourceCommit": compact["sourceCommit"],
        "calibrationVerdictSha256": sha256_file(verdict_path),
        "calibrationCompactSha256": verdict["compactSha256"],
        "recomputeSha256": verdict["recomputeSha256"],
        "status": verdict["status"],
        "finalists": [
            {
                "lane": lane,
                "qualificationBranches": verdict["lanes"][lane]["qualificationBranches"],
                "byteOracleDependency": "H4-L1-R2" if lane == "H7-L1-R2-E75" else None,
            }
            for lane in verdict["finalists"]
        ],
    }
    (output / "fixed-evaluation-selection.json").write_bytes(
        canonical_bytes(selection) + b"\n"
    )

    lines = [
        "# PATCH-ENC-005 Phase-A calibration",
        "",
        f"- source: \`{compact['sourceCommit']}\`",
        f"- status: **{verdict['status']}**",
        f"- H0 bytes: {verdict['h0PatchBytes']}",
        f"- Pareto: {', '.join(verdict['pareto']) if verdict['pareto'] else 'none'}",
        f"- finalists: {', '.join(verdict['finalists']) if verdict['finalists'] else 'none'}",
        "",
        "| lane | b | max wall | max CPU | max apply | create MiB | apply MiB | branch | eligible |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |",
    ]
    for lane in LANES:
        row = verdict["lanes"][lane]
        lines.append(
            f"| {lane} | {row['byteRatio']:.6f} | {row['maxWallRatio']:.4f} | "
            f"{row['maxCpuRatio']:.4f} | {max(row['applyRatioByPlatform'].values()):.4f} | "
            f"{max(row['createPeakOverIdleBytesByPlatform'].values()) / 1048576:.2f} | "
            f"{max(row['applyPeakOverIdleBytesByPlatform'].values()) / 1048576:.2f} | "
            f"{','.join(row['qualificationBranches']) or '—'} | "
            f"{'yes' if row['eligible'] else 'no'} |"
        )
    (output / "summary.md").write_text(
        "\n".join(lines) + "\n", encoding="utf-8", newline="\n"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--paired", action="append", default=[], help="platform=paired.json")
    parser.add_argument("--memory", action="append", default=[], help="platform=memory-directory")
    parser.add_argument("--cross-platform", type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    source_commit = args.source_commit.lower()
    if len(source_commit) != 40 or any(ch not in "0123456789abcdef" for ch in source_commit):
        parser.error("--source-commit must be a lowercase full 40-hex commit")
    try:
        paired = parse_bindings(args.paired, "--paired")
        memory = parse_bindings(args.memory, "--memory")
        compact, files = build_compact(
            paired, memory, args.cross_platform, source_commit
        )
        write_outputs(args.output, compact, files)
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as error:
        parser.error(str(error))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
