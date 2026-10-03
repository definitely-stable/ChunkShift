#!/usr/bin/env python3
"""Validation and compact projection of PATCH-ENC-005 raw apply evidence."""

from __future__ import annotations

import math


PLATFORMS = {"linux-x64", "linux-arm64", "win-x64"}


def median_five(values):
    if not isinstance(values, list) or len(values) != 5:
        raise ValueError("apply evidence requires exactly five samples")
    return sorted(values)[2]


def positive(value, label):
    number = float(value)
    if not math.isfinite(number) or number <= 0:
        raise ValueError(f"{label}: expected finite positive value")
    return number


def nonnegative(value, label):
    number = float(value)
    if not math.isfinite(number) or number < 0:
        raise ValueError(f"{label}: expected finite non-negative value")
    return number


def validate_platform_os(platform, os_description, label):
    normalized = str(os_description or "").strip().lower()
    if platform.startswith("linux-"):
        # .NET RuntimeInformation.OSDescription on the pinned GitHub Ubuntu
        # runners is distribution-branded ("Ubuntu 24.04.x LTS"), not "Linux".
        if "linux" not in normalized and "ubuntu" not in normalized:
            raise ValueError(f"{label}: expected Linux environment")
    elif platform == "win-x64":
        if "windows" not in normalized:
            raise ValueError(f"{label}: expected Windows environment")
    else:
        raise ValueError(f"{label}: unsupported platform {platform}")
    return str(os_description or "")


def validate_environment(platform, environment, source_commit, label):
    if platform not in PLATFORMS or not isinstance(environment, dict):
        raise ValueError(f"{label}: invalid apply environment")
    expected_arch = "arm64" if platform == "linux-arm64" else "x64"
    if (
        str(environment.get("gitCommit") or "").lower() != source_commit
        or int(environment.get("processorCount") or 0) < 2
        or str(environment.get("processArchitecture") or "").lower() != expected_arch
        or str(environment.get("osArchitecture") or "").lower() != expected_arch
    ):
        raise ValueError(f"{label}: apply environment binding mismatch")
    validate_platform_os(platform, environment.get("osDescription"), label)
    if not str(environment.get("frameworkDescription") or "").strip():
        raise ValueError(f"{label}: framework provenance is missing")
    if not str(environment.get("processorDescription") or "").strip():
        raise ValueError(f"{label}: processor provenance is missing")
    return environment


def validate(apply_evidence, accepted, platform, source_commit, lanes, label):
    if not isinstance(apply_evidence, dict) or set(apply_evidence) != set(lanes):
        raise ValueError(f"{label}: applyEvidence lane set mismatch")

    wall_totals = {}
    compact = {}
    common_environment = None

    for lane in lanes:
        item = apply_evidence[lane]
        aggregate = item.get("aggregate")
        files = item.get("files")
        environment = validate_environment(
            platform,
            item.get("environment"),
            source_commit,
            f"{label}/{lane}",
        )
        if common_environment is None:
            common_environment = environment
        elif environment != common_environment:
            raise ValueError(f"{label}: apply environment differs between lanes")
        if not isinstance(aggregate, dict) or not isinstance(files, list) or not files:
            raise ValueError(f"{label}/{lane}: raw apply evidence is incomplete")

        seen = set()
        compact_files = []
        wall_total = 0.0
        cpu_total = 0.0
        reads_total = 0
        bytes_total = 0

        for row in files:
            key = (row["family"], row["base"], row["target"], row["path"])
            if key in seen:
                raise ValueError(f"{label}/{lane}: duplicate apply file {key}")
            seen.add(key)
            patch_sha = str(row.get("patchSha256", "")).lower()
            if accepted[lane].get(key) != patch_sha:
                raise ValueError(f"{label}/{lane}/{key}: apply patch SHA mismatch")
            samples = row.get("samples")
            if not isinstance(samples, list) or len(samples) != 5:
                raise ValueError(f"{label}/{lane}/{key}: five apply samples required")

            wall = []
            cpu = []
            reads = []
            bytes_read = []
            compact_samples = []
            for sample in samples:
                swall = positive(sample.get("wallSeconds"), f"{label}/{lane}/{key}/wall")
                scpu = nonnegative(sample.get("cpuSeconds"), f"{label}/{lane}/{key}/cpu")
                sreads = int(sample.get("baseReads", -1))
                sbytes = int(sample.get("baseBytesRead", -1))
                sseeks = int(sample.get("baseSeeks", -1))
                if min(sreads, sbytes, sseeks) < 0:
                    raise ValueError(f"{label}/{lane}/{key}: invalid apply counters")
                wall.append(swall)
                cpu.append(scpu)
                reads.append(sreads)
                bytes_read.append(sbytes)
                compact_samples.append(
                    {
                        "wallSeconds": swall,
                        "cpuSeconds": scpu,
                        "baseReads": sreads,
                        "baseBytesRead": sbytes,
                        "baseSeeks": sseeks,
                    }
                )

            wall_total += median_five(wall)
            cpu_total += median_five(cpu)
            reads_total += int(median_five(reads))
            bytes_total += int(median_five(bytes_read))
            compact_files.append(
                {
                    "family": key[0],
                    "base": key[1],
                    "target": key[2],
                    "path": key[3],
                    "patchSha256": patch_sha,
                    "samples": compact_samples,
                }
            )

        if seen != set(accepted[lane]):
            raise ValueError(f"{label}/{lane}: apply file set differs from accepted timing")
        if not math.isclose(float(aggregate.get("wallSeconds")), wall_total, rel_tol=1e-12, abs_tol=1e-12):
            raise ValueError(f"{label}/{lane}: apply wall aggregate does not recompute")
        if not math.isclose(float(aggregate.get("cpuSeconds")), cpu_total, rel_tol=1e-12, abs_tol=1e-12):
            raise ValueError(f"{label}/{lane}: apply CPU aggregate does not recompute")
        if int(aggregate.get("baseReads", -1)) != reads_total:
            raise ValueError(f"{label}/{lane}: apply baseReads aggregate does not recompute")
        if int(aggregate.get("baseBytesRead", -1)) != bytes_total:
            raise ValueError(f"{label}/{lane}: apply baseBytesRead aggregate does not recompute")

        compact_files.sort(key=lambda row: (row["family"], row["base"], row["target"], row["path"]))
        wall_totals[lane] = wall_total
        compact[lane] = {
            "aggregate": {
                "wallSeconds": wall_total,
                "cpuSeconds": cpu_total,
                "baseReads": reads_total,
                "baseBytesRead": bytes_total,
            },
            "files": compact_files,
        }

    if common_environment is None:
        raise ValueError(f"{label}: no apply environment")
    return wall_totals, compact, common_environment
