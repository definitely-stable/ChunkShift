#!/usr/bin/env python3
"""Summarize PATCH-APPLY-003: the overlapped re-chunk check on apply.

Reads one directory per platform holding the `chunkshift.patch-lab-apply-check.v1`
files of `patch-lab apply-check` (kinds prepare, time, concurrent, memory),
verifies the corpus lock and the recorded commit, computes the statistics of
docs/benchmarks/PATCH-APPLY-003-PROTOCOL.md section 5 and applies the frozen
rules of section 6:

  rule 1  A2 (overlap): upper 95 % CI bound of the wall overhead <= 10 % on at
          least two of linux-x64, linux-arm64, win-x64, with verified outputs
          and the 64 MiB memory bound on every measured platform
  rule 2  A1a: needed when the CPU overhead of the overlapped check at eight
          concurrent applies exceeds 25 % on at least two platforms
  rule 3  #168 option 1 reopens if rule 1 rejects A2, or later if rule 2 still holds after A1a

The lanes come from the evidence. Lanes off, seq, overlap are the rule-1 run:
all three rules are evaluated from it. Lanes off, overlap, boundary are the run
that evaluates rule 2 again on the built A1a (lane boundary): rule 1 is taken
from the verdict of the rule-1 run (--prior-verdict), which must have required
A1a, rule 2 reads lane boundary, and rule 3 follows.

A rule whose inputs are missing reports "not evaluated", never a pass. Ratios
are printed in percent with two decimals.

Usage:
  summarize_apply_check.py PLATFORM_DIR [PLATFORM_DIR ...]
      --corpus-lock docs/benchmarks/patch-corpus/corpus-lock.json
      --output verdict.json --markdown summary.md
      [--prior-verdict apply-check-verdict.json]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import random
import statistics
import sys
from pathlib import Path

SCHEMA = "chunkshift.patch-lab-apply-check.v1"
SUMMARY_SCHEMA = "chunkshift.patch-lab-apply-check-summary.v1"
EXPERIMENT = "PATCH-APPLY-003"

RULE1_LANES = ("off", "seq", "overlap")
BOUNDARY_LANES = ("off", "overlap", "boundary")
LANE_SETS = (RULE1_LANES, BOUNDARY_LANES)
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")

REPETITIONS = 10
BOOTSTRAP_RESAMPLES = 10_000
BOOTSTRAP_SEED = 20260928
CPU_FLOOR_SECONDS = 0.020
LARGE_FILE_BYTES = 1024 * 1024

WALL_OVERHEAD_MAX = 0.10
CPU_OVERHEAD_MAX = 0.25
RULE2_CONCURRENCY = 8
MEMORY_LIMIT_BYTES = 64 * 1024 * 1024
PLATFORMS_REQUIRED = 2

COMPONENTS = ("csmParse", "reread", "boundary", "hash", "manifestId", "check")

NOT_EVALUATED = "not evaluated"
MIB = 1024 * 1024


def check_lanes(lanes: tuple[str, ...]) -> tuple[str, ...]:
    return tuple(lane for lane in lanes if lane != "off")


def rule2_lane(lanes: tuple[str, ...]) -> str:
    return "boundary" if lanes == BOUNDARY_LANES else "overlap"


class SummaryError(Exception):
    """Evidence that cannot be summarized; the process exits with 1."""


def load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise SummaryError(f"{path}: {error}") from error


def load_lock(path: Path) -> str:
    value = load_json(path).get("pairsSha256")
    if not isinstance(value, str) or len(value) != 64:
        raise SummaryError(f"{path}: no pairsSha256")
    return value


def platform_label(environment: dict) -> str | None:
    description = (environment.get("osDescription") or "").lower()
    architecture = (environment.get("osArchitecture") or "").lower()
    system = "win" if "windows" in description else "linux" if description else None
    arch = {"x64": "x64", "arm64": "arm64"}.get(architecture)
    return f"{system}-{arch}" if system and arch else None


def file_key(record: dict) -> tuple[str, str, str, str]:
    return (record["family"], record["base"], record["target"], record["path"])


def median(values: list[float]) -> float:
    return statistics.median(values)


def percentile(sorted_values: list[float], fraction: float) -> float:
    """Linear-interpolation percentile of an already sorted sample."""
    if not sorted_values:
        raise SummaryError("percentile of an empty sample")
    position = (len(sorted_values) - 1) * fraction
    lower = int(position)
    upper = min(lower + 1, len(sorted_values) - 1)
    weight = position - lower
    return sorted_values[lower] * (1 - weight) + sorted_values[upper] * weight


def bootstrap(values: list, statistic, rng: random.Random, resamples: int) -> tuple[float, float]:
    """Percentile-bootstrap 95 % interval of statistic(sample)."""
    count = len(values)
    estimates = sorted(statistic(rng.choices(values, k=count)) for _ in range(resamples))
    return percentile(estimates, 0.025), percentile(estimates, 0.975)


def ratio_stat(values: list[float], rng: random.Random, resamples: int) -> dict | None:
    if not values:
        return None
    low, high = bootstrap(values, median, rng, resamples)
    return {"median": median(values), "ciLow": low, "ciHigh": high, "count": len(values)}


def sum_stat(pairs: list[tuple[float, float]], rng: random.Random, resamples: int) -> dict | None:
    """Corpus-sum overhead sum(lane) / sum(off) - 1, bootstrapped over files."""
    if not pairs or sum(off for _, off in pairs) <= 0:
        return None

    def statistic(sample):
        denominator = sum(off for _, off in sample)
        return sum(lane for lane, _ in sample) / denominator - 1 if denominator > 0 else 0.0

    low, high = bootstrap(pairs, statistic, rng, resamples)
    return {"value": statistic(pairs), "ciLow": low, "ciHigh": high, "count": len(pairs)}


# --- reading -----------------------------------------------------------------


def read_platform(directory: Path, pairs_sha256: str) -> dict:
    if not directory.is_dir():
        raise SummaryError(f"{directory}: platform directory does not exist")

    time_docs: dict[int, dict] = {}
    concurrent_docs: dict[int, dict] = {}
    memory = None
    prepare = None
    documents = []

    for path in sorted(directory.rglob("*.json")):
        document = load_json(path)
        if document.get("schema") != SCHEMA:
            continue
        if document.get("corpusPairsSha256") != pairs_sha256:
            raise SummaryError(
                f"{path}: corpusPairsSha256 {document.get('corpusPairsSha256')!r} does not match the corpus lock")
        kind = document.get("kind")
        if kind == "time":
            repetition = int(document["repetition"])
            if repetition in time_docs:
                raise SummaryError(f"{path}: time repetition {repetition} appears twice")
            time_docs[repetition] = document
        elif kind == "concurrent":
            repetition = int(document["repetition"])
            if repetition in concurrent_docs:
                raise SummaryError(f"{path}: concurrent repetition {repetition} appears twice")
            concurrent_docs[repetition] = document
        elif kind == "memory":
            if memory is not None:
                raise SummaryError(f"{path}: a second memory file appears")
            memory = document
        elif kind == "prepare":
            if prepare is not None:
                raise SummaryError(f"{path}: a second prepare file appears")
            prepare = document
        else:
            raise SummaryError(f"{path}: unknown kind {kind!r}")
        documents.append(document)

    if not documents:
        raise SummaryError(f"{directory}: no {SCHEMA} documents")

    commits = sorted({(document.get("environment") or {}).get("gitCommit") or "" for document in documents})
    if "" in commits:
        raise SummaryError(f"{directory}: a file does not record environment.gitCommit")
    if len(commits) != 1:
        raise SummaryError(f"{directory}: mixed evidence, gitCommit values {commits}")

    labels = sorted({platform_label(document.get("environment") or {}) or "" for document in documents})
    if "" in labels or len(labels) != 1 or labels[0] not in PLATFORMS:
        raise SummaryError(f"{directory}: invalid or mixed platforms {labels}")
    platform = labels[0]

    processors = {(document.get("environment") or {}).get("processorCount") for document in documents}
    descriptions = {(document.get("environment") or {}).get("processorDescription") or "" for document in documents}
    if len(processors) != 1 or next(iter(processors)) is None or int(next(iter(processors))) <= 0:
        raise SummaryError(f"{directory}: missing or mixed processorCount values {sorted(str(v) for v in processors)}")
    if "" in descriptions or len(descriptions) != 1:
        raise SummaryError(f"{directory}: missing or mixed processorDescription values")

    run_ids = sorted({document.get("runId") or "" for document in documents})
    if "" in run_ids or len(run_ids) != 1:
        raise SummaryError(f"{directory}: missing or mixed RunIds {run_ids}")
    run_id = run_ids[0]
    commit = commits[0]
    prefix = f"{EXPERIMENT}/RUN-"
    if not run_id.startswith(prefix):
        raise SummaryError(f"{directory}: RunId {run_id!r} is not a {EXPERIMENT} run")
    parts = run_id[len(prefix):].split("-", 3)
    if len(parts) != 4:
        raise SummaryError(f"{directory}: RunId {run_id!r} does not match the frozen format")
    run_date, workflow_run_number, run_commit, run_platform = parts
    if len(run_date) != 8 or not run_date.isdigit() or not workflow_run_number.isdigit():
        raise SummaryError(f"{directory}: RunId {run_id!r} has an invalid date or workflow run number")
    if run_commit != commit[:7]:
        raise SummaryError(f"{directory}: RunId {run_id!r} does not bind commit {commit[:7]}")
    if run_platform != platform:
        raise SummaryError(f"{directory}: RunId {run_id!r} does not bind platform {platform}")

    return {
        "directory": str(directory),
        "platform": platform,
        "lanes": platform_lanes(directory, time_docs, concurrent_docs, memory),
        "gitCommit": commit,
        "runIds": [run_id],
        "processorCount": int(next(iter(processors))),
        "processorDescription": next(iter(descriptions)),
        "workflowRunNumber": workflow_run_number,
        "runDate": run_date,
        "time": time_docs,
        "concurrent": concurrent_docs,
        "memory": memory,
        "prepare": prepare,
    }


def platform_lanes(directory: Path, time_docs: dict[int, dict], concurrent_docs: dict[int, dict],
                   memory: dict | None) -> tuple[str, ...]:
    """The lane set every time, concurrent and memory record of a platform names."""
    seen: set[frozenset] = set()
    for document in time_docs.values():
        for record in document.get("files") or []:
            seen.add(frozenset((record.get("runs") or {}).keys()))
    for document in concurrent_docs.values():
        levels: dict[int, set[str]] = {}
        for entry in document.get("passes") or []:
            levels.setdefault(int(entry["concurrency"]), set()).add(entry["lane"])
        seen.update(frozenset(lanes) for lanes in levels.values())
    for record in (memory or {}).get("files") or []:
        seen.add(frozenset((record.get("applyPeakBytes") or {}).keys()))
    if not seen:
        return RULE1_LANES
    if len(seen) != 1:
        raise SummaryError(f"{directory}: mixed lane sets {sorted(sorted(lanes) for lanes in seen)}")
    names = next(iter(seen))
    for lanes in LANE_SETS:
        if names == frozenset(lanes):
            return lanes
    raise SummaryError(f"{directory}: unknown lane set {sorted(names)}")


# --- statistics ----------------------------------------------------------------


def _time_point(per_file: dict, keys: list[tuple], lane: str, metric: str,
                repetition_indices: list[int], sizes: dict[tuple, int] | None = None,
                minimum_size: int | None = None, corpus_sum: bool = False,
                cpu_floor: bool = False) -> float | None:
    ratios = []
    pairs = []
    for key in keys:
        if minimum_size is not None and (sizes or {}).get(key, 0) < minimum_size:
            continue
        entry = per_file[key][metric]
        off = median([entry["off"][index] for index in repetition_indices])
        lane_value = median([entry[lane][index] for index in repetition_indices])
        if off <= 0 or (cpu_floor and off < CPU_FLOOR_SECONDS):
            continue
        ratios.append(lane_value / off - 1)
        pairs.append((lane_value, off))
    if corpus_sum:
        denominator = sum(off for _, off in pairs)
        return (sum(value for value, _ in pairs) / denominator - 1) if denominator > 0 else None
    return median(ratios) if ratios else None


def _eligible_count(per_file: dict, keys: list[tuple], metric: str,
                    repetition_indices: list[int], sizes: dict[tuple, int] | None = None,
                    minimum_size: int | None = None, cpu_floor: bool = False) -> int:
    count = 0
    for key in keys:
        if minimum_size is not None and (sizes or {}).get(key, 0) < minimum_size:
            continue
        off = median([per_file[key][metric]["off"][index] for index in repetition_indices])
        if off <= 0 or (cpu_floor and off < CPU_FLOOR_SECONDS):
            continue
        count += 1
    return count


def _block_interval(per_file: dict, keys: list[tuple], lane: str, metric: str,
                    repetitions: int, rng: random.Random, resamples: int,
                    sizes: dict[tuple, int] | None = None,
                    minimum_size: int | None = None, corpus_sum: bool = False,
                    cpu_floor: bool = False) -> dict | None:
    indices = list(range(repetitions))
    point = _time_point(per_file, keys, lane, metric, indices, sizes, minimum_size, corpus_sum, cpu_floor)
    if point is None:
        return None
    estimates = []
    for _ in range(resamples):
        sample = rng.choices(indices, k=repetitions)
        estimate = _time_point(per_file, keys, lane, metric, sample, sizes, minimum_size, corpus_sum, cpu_floor)
        if estimate is not None:
            estimates.append(estimate)
    if not estimates:
        return None
    estimates.sort()
    return {
        "value" if corpus_sum else "median": point,
        "ciLow": percentile(estimates, 0.025),
        "ciHigh": percentile(estimates, 0.975),
        "count": _eligible_count(
            per_file, keys, metric, indices, sizes, minimum_size, cpu_floor),
        "repetitions": repetitions,
        "resamplingUnit": "paired-repetition-block",
    }


def time_statistics(time_docs: dict[int, dict], repetitions: int, rng: random.Random, resamples: int,
                    lanes: tuple[str, ...] = RULE1_LANES) -> dict:
    expected = set(range(repetitions))
    present = set(time_docs)
    complete = present == expected
    if not time_docs:
        return {"complete": False, "repetitions": [], "reason": "no time files"}

    per_file: dict[tuple, dict] = {}
    sizes: dict[tuple, int] = {}
    outputs_verified = all(document.get("outputsVerified") is True for document in time_docs.values())
    canonical_keys = None
    for repetition in sorted(time_docs):
        document = time_docs[repetition]
        keys = set()
        for record in document.get("files") or []:
            key = file_key(record)
            keys.add(key)
            sizes[key] = int(record["targetSize"])
            entry = per_file.setdefault(key, {"wall": {lane: [] for lane in lanes},
                                              "cpu": {lane: [] for lane in lanes},
                                              "components": {name: {"wall": [], "cpu": []} for name in COMPONENTS}})
            for lane in lanes:
                runs = (record.get("runs") or {}).get(lane) or []
                if len(runs) != 2:
                    raise SummaryError(f"repetition {repetition}: {key} has {len(runs)} runs of lane {lane}")
                entry["wall"][lane].append(sum(float(run["wallSeconds"]) for run in runs) / 2)
                entry["cpu"][lane].append(sum(float(run["cpuSeconds"]) for run in runs) / 2)
            decomposition = record.get("decomposition") or {}
            for name in COMPONENTS:
                cost = decomposition.get(name) or {}
                entry["components"][name]["wall"].append(float(cost.get("wallSeconds", 0.0)))
                entry["components"][name]["cpu"].append(float(cost.get("cpuSeconds", 0.0)))
        if canonical_keys is None:
            canonical_keys = keys
        elif keys != canonical_keys:
            raise SummaryError(f"repetition {repetition}: the file set differs from the other repetitions")

    keys = sorted(per_file)
    values = {
        key: {
            metric: {lane: median(per_file[key][metric][lane]) for lane in lanes}
            for metric in ("wall", "cpu")
        }
        for key in keys
    }

    result: dict = {
        "complete": complete,
        "repetitions": sorted(present),
        "files": len(keys),
        "outputsVerified": outputs_verified,
        "resamplingUnit": "paired-repetition-block",
        "lanes": {},
        "compactFiles": [
            {
                "family": key[0], "base": key[1], "target": key[2], "path": key[3],
                "targetSize": sizes[key],
                "wallMedianSeconds": values[key]["wall"],
                "cpuMedianSeconds": values[key]["cpu"],
            }
            for key in keys
        ],
    }

    if complete:
        for lane in check_lanes(lanes):
            file_ratios = [values[key]["wall"][lane] / values[key]["wall"]["off"] - 1
                           for key in keys if values[key]["wall"]["off"] > 0]
            result["lanes"][lane] = {
                "wallMedianOfRatios": _block_interval(
                    per_file, keys, lane, "wall", repetitions, rng, resamples, sizes=sizes),
                "wallCorpusSum": _block_interval(
                    per_file, keys, lane, "wall", repetitions, rng, resamples, sizes=sizes, corpus_sum=True),
                "wallLargeFilesMedianOfRatios": _block_interval(
                    per_file, keys, lane, "wall", repetitions, rng, resamples,
                    sizes=sizes, minimum_size=LARGE_FILE_BYTES),
                "cpuMedianOfRatios": _block_interval(
                    per_file, keys, lane, "cpu", repetitions, rng, resamples, cpu_floor=True),
                "cpuCorpusSum": _block_interval(
                    per_file, keys, lane, "cpu", repetitions, rng, resamples, corpus_sum=True),
                "fileBootstrapDiagnostic": ratio_stat(file_ratios, random.Random(BOOTSTRAP_SEED), resamples),
            }
    else:
        result["reason"] = f"expected repetitions 0..{repetitions - 1}, found {sorted(present)}"

    result["totals"] = {
        metric: {lane: sum(values[key][metric][lane] for key in keys) for lane in lanes}
        for metric in ("wall", "cpu")
    }

    components = {}
    for name in COMPONENTS:
        components[name] = {
            metric: sum(median(per_file[key]["components"][name][metric]) for key in keys)
            for metric in ("wall", "cpu")
        }
    check_wall = components["check"]["wall"]
    for name in COMPONENTS:
        components[name]["shareOfCheckWall"] = components[name]["wall"] / check_wall if check_wall > 0 else None
    parts = sum(components[name]["wall"] for name in COMPONENTS if name != "check")
    components["residual"] = {"wall": check_wall - parts,
                              "cpu": components["check"]["cpu"] - sum(
                                  components[name]["cpu"] for name in COMPONENTS if name != "check")}
    result["decomposition"] = components
    return result


def concurrent_statistics(docs: dict[int, dict], repetitions: int, rng: random.Random, resamples: int,
                          lanes: tuple[str, ...] = RULE1_LANES) -> dict:
    if not docs:
        return {"complete": False, "reason": "no concurrent files"}

    complete = set(docs) == set(range(repetitions))
    by_level: dict[int, dict[str, list[dict]]] = {}
    for repetition in sorted(docs):
        passes = docs[repetition].get("passes") or []
        grouped: dict[int, dict[str, list[dict]]] = {}
        for entry in passes:
            grouped.setdefault(int(entry["concurrency"]), {}).setdefault(entry["lane"], []).append(entry)
        for level, passes_by_lane in grouped.items():
            for lane in lanes:
                runs = passes_by_lane.get(lane) or []
                if len(runs) != 2:
                    raise SummaryError(f"concurrent repetition {repetition}, c={level}: {len(runs)} passes of {lane}")
            record = by_level.setdefault(level, {"wall": {lane: [] for lane in lanes},
                                                 "cpu": {lane: [] for lane in lanes}})
            for lane in lanes:
                record["wall"][lane].append(sum(float(run["wallSeconds"]) for run in passes_by_lane[lane]) / 2)
                record["cpu"][lane].append(sum(float(run["cpuSeconds"]) for run in passes_by_lane[lane]) / 2)

    levels = {}
    for level in sorted(by_level):
        record = by_level[level]
        levels[str(level)] = {}
        for lane in check_lanes(lanes):
            cpu = [lane_value / off - 1 for lane_value, off in zip(record["cpu"][lane], record["cpu"]["off"]) if off > 0]
            wall = [lane_value / off - 1 for lane_value, off in zip(record["wall"][lane], record["wall"]["off"]) if off > 0]
            levels[str(level)][lane] = {
                "cpuOverhead": ratio_stat(cpu, rng, resamples),
                "wallOverhead": ratio_stat(wall, rng, resamples),
            }
        levels[str(level)]["medianSeconds"] = {
            metric: {lane: median(record[metric][lane]) for lane in lanes} for metric in ("wall", "cpu")
        }
    compact = []
    for repetition in sorted(docs):
        grouped: dict[int, dict[str, list[dict]]] = {}
        for entry in docs[repetition].get("passes") or []:
            grouped.setdefault(int(entry["concurrency"]), {}).setdefault(entry["lane"], []).append(entry)
        compact.append({
            "repetition": repetition,
            "levels": {
                str(level): {
                    lane: {
                        "wallSeconds": sum(float(run["wallSeconds"]) for run in passes_by_lane[lane]) / len(passes_by_lane[lane]),
                        "cpuSeconds": sum(float(run["cpuSeconds"]) for run in passes_by_lane[lane]) / len(passes_by_lane[lane]),
                    }
                    for lane in lanes
                }
                for level, passes_by_lane in sorted(grouped.items())
            },
        })
    return {
        "complete": complete,
        "repetitions": sorted(docs),
        "resamplingUnit": "paired-repetition-block",
        "levels": levels,
        "compactRepetitions": compact,
    }


def memory_statistics(document: dict | None, lanes: tuple[str, ...] = RULE1_LANES) -> dict:
    if document is None:
        return {"verdict": None, "reason": "no memory file"}
    baseline = int(document.get("idleBaselineBytes") or 0)
    records = document.get("files") or []
    if baseline <= 0 or not records:
        return {"verdict": None, "reason": "memory file has no records or baseline"}
    worst = {}
    for lane in lanes:
        excess = [(int(record["applyPeakBytes"][lane]) - baseline, record) for record in records]
        value, record = max(excess, key=lambda item: item[0])
        worst[lane] = {"excessBytes": value, "path": record["path"], "family": record["family"]}
    within = worst["overlap"]["excessBytes"] <= MEMORY_LIMIT_BYTES
    environment = document.get("memoryEnvironment") or {}
    result = {
        "idleBaselineBytes": baseline,
        "records": len(records),
        "worst": worst,
        "limitBytes": MEMORY_LIMIT_BYTES,
        "verdict": None if environment else within,
        "reason": "allocator or GC variables set; not evaluated" if environment else None,
        "compactFiles": [
            {
                "family": record["family"],
                "path": record["path"],
                "targetSize": int(record["targetSize"]),
                "excessBytes": {
                    lane: int(record["applyPeakBytes"][lane]) - baseline
                    for lane in lanes
                },
            }
            for record in records
        ],
    }
    if "boundary" in lanes:
        # Record only: no frozen rule bounds the boundary lane's memory.
        result["boundaryWithinBound"] = (
            None if environment else worst["boundary"]["excessBytes"] <= MEMORY_LIMIT_BYTES)
    return result


def alignment_statistics(document: dict | None) -> dict | None:
    if document is None:
        return None
    families: dict[str, dict[str, int]] = {}
    for record in document.get("files") or []:
        family = families.setdefault(record["family"], {"targetBytes": 0, "sameOffsetBytes": 0, "alignedBytes": 0})
        family["targetBytes"] += int(record["targetSize"])
        family["sameOffsetBytes"] += int(record["sameOffsetBytes"])
        family["alignedBytes"] += int(record["alignedBytes"])
    corpus = {name: sum(family[name] for family in families.values())
              for name in ("targetBytes", "sameOffsetBytes", "alignedBytes")}
    return {"families": families, "corpus": corpus}


# --- rules -----------------------------------------------------------------------


def load_prior_verdict(path: Path) -> dict:
    """The rules of the rule-1 run, which a boundary run carries forward."""
    try:
        data = path.read_bytes()
    except OSError as error:
        raise SummaryError(f"{path}: {error}") from error
    document = load_json(path)
    if document.get("schema") != SUMMARY_SCHEMA or document.get("experiment") != EXPERIMENT:
        raise SummaryError(f"{path}: not a {EXPERIMENT} summary")
    rules = document.get("rules") or {}
    rule1 = (rules.get("rule1") or {}).get("verdict")
    rule2 = (rules.get("rule2") or {}).get("verdict")
    if not isinstance(rule1, str) or not isinstance(rule2, str):
        raise SummaryError(f"{path}: no rule 1 or rule 2 verdict")
    platforms = document.get("platforms") or []
    for platform in platforms:
        if tuple(platform.get("lanes") or RULE1_LANES) != RULE1_LANES:
            raise SummaryError(f"{path}: the prior verdict is not a rule-1 run")
    return {
        "sha256": hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest(),
        "rule1": rule1,
        "rule2": rule2,
        "gitCommits": sorted({platform.get("gitCommit") or "" for platform in platforms}),
        "runIds": sorted(run_id for platform in platforms for run_id in platform.get("runIds") or []),
    }


def evaluate(platforms: list[dict], lanes: tuple[str, ...] = RULE1_LANES, prior: dict | None = None) -> dict:
    if lanes == BOUNDARY_LANES:
        return evaluate_boundary(platforms, prior)
    rule1_platforms = {}
    rule2_platforms = {}
    for platform in platforms:
        name = platform["platform"]
        time = platform["timeStatistics"]
        memory = platform["memoryStatistics"]
        overlap = (time.get("lanes") or {}).get("overlap", {}) if time.get("complete") else {}
        primary = overlap.get("wallMedianOfRatios")
        corpus_sum = overlap.get("wallCorpusSum")
        large = overlap.get("wallLargeFilesMedianOfRatios")
        if primary is None:
            rule1_platforms[name] = {"wallBoundMet": None, "reason": time.get("reason") or "incomplete time data"}
        else:
            primary_met = primary["ciHigh"] <= WALL_OVERHEAD_MAX
            companion_conflict = primary_met and (
                corpus_sum is None or large is None or
                corpus_sum["value"] > WALL_OVERHEAD_MAX or
                large["median"] > WALL_OVERHEAD_MAX)
            rule1_platforms[name] = {
                "ciHigh": primary["ciHigh"],
                "wallBoundMet": primary_met,
                "corpusSum": None if corpus_sum is None else corpus_sum["value"],
                "largeFilesMedian": None if large is None else large["median"],
                "companionConflict": companion_conflict,
                "outputsVerified": time.get("outputsVerified") is True,
                "memoryWithinBound": memory.get("verdict"),
            }

        rule2_platforms[name] = rule2_platform(platform, "overlap")

    met = [name for name, entry in rule1_platforms.items()
           if entry["wallBoundMet"] is True and entry.get("companionConflict") is False]
    conflicted = [name for name, entry in rule1_platforms.items()
                  if entry["wallBoundMet"] is True and entry.get("companionConflict") is True]
    missed = [name for name, entry in rule1_platforms.items() if entry["wallBoundMet"] is False]
    measured = [name for name, entry in rule1_platforms.items() if entry["wallBoundMet"] is not None]

    if len(missed) >= PLATFORMS_REQUIRED:
        rule1 = "REJECT"
    elif len(met) >= PLATFORMS_REQUIRED:
        conditions = [rule1_platforms[name].get("outputsVerified") and rule1_platforms[name].get("memoryWithinBound")
                      for name in measured]
        if all(value is True for value in conditions):
            rule1 = "ADOPT"
        elif any(value is False for value in conditions) or conflicted:
            rule1 = "DEFER"
        else:
            rule1 = NOT_EVALUATED
    elif conflicted:
        rule1 = "DEFER"
    else:
        rule1 = NOT_EVALUATED

    rule2 = rule2_count(rule2_platforms, "A1a required", "A1a not required")

    if rule1 == "REJECT":
        rule3 = "option 1 reopens"
    elif rule1 == "ADOPT" and rule2 == "A1a not required":
        rule3 = "option 3 stands (close #168)"
    elif rule1 == "ADOPT" and rule2 == "A1a required":
        rule3 = "pending A1a"
    else:
        rule3 = NOT_EVALUATED

    return {
        "rule1": {"verdict": rule1, "boundFraction": WALL_OVERHEAD_MAX, "platforms": rule1_platforms},
        "rule2": {"verdict": rule2, "boundFraction": CPU_OVERHEAD_MAX, "concurrency": RULE2_CONCURRENCY,
                  "lane": "overlap", "platforms": rule2_platforms},
        "rule3": {"verdict": rule3},
    }


def rule2_platform(platform: dict, lane: str) -> dict:
    """Median CPU overhead of lane at c = 8, read only where c = 8 saturates the runner."""
    if platform["processorCount"] > RULE2_CONCURRENCY:
        return {
            "exceeds": None,
            "reason": (
                f"processorCount {platform['processorCount']} exceeds the frozen "
                f"c={RULE2_CONCURRENCY} saturation point"
            ),
        }
    concurrent = platform["concurrentStatistics"]
    level = (concurrent.get("levels") or {}).get(str(RULE2_CONCURRENCY), {}) if concurrent.get("complete") else {}
    cpu = (level.get(lane) or {}).get("cpuOverhead")
    if cpu is None:
        return {"exceeds": None, "reason": concurrent.get("reason") or "incomplete concurrent data"}
    return {"median": cpu["median"], "exceeds": cpu["median"] > CPU_OVERHEAD_MAX}


def rule2_count(rule2_platforms: dict, holds: str, does_not_hold: str) -> str:
    exceeds = [name for name, entry in rule2_platforms.items() if entry["exceeds"] is True]
    within = [name for name, entry in rule2_platforms.items() if entry["exceeds"] is False]
    if len(exceeds) >= PLATFORMS_REQUIRED:
        return holds
    if len(within) >= PLATFORMS_REQUIRED:
        return does_not_hold
    return NOT_EVALUATED


def evaluate_boundary(platforms: list[dict], prior: dict | None) -> dict:
    """Rule 2 again on the built A1a (lane boundary), rule 1 from the rule-1 run."""
    rule2_platforms = {platform["platform"]: rule2_platform(platform, "boundary") for platform in platforms}
    rule2 = rule2_count(rule2_platforms, "holds after A1a", "does not hold after A1a")

    if prior is None:
        rule1 = NOT_EVALUATED
        rule1_reason = "no prior verdict of the rule-1 run"
    elif prior["rule2"] != "A1a required":
        rule1 = prior["rule1"]
        rule1_reason = f"the rule-1 run did not require A1a (rule 2: {prior['rule2']})"
    else:
        rule1 = prior["rule1"]
        rule1_reason = None

    if rule1 == "REJECT":
        rule3 = "option 1 reopens"
    elif rule1 == "ADOPT" and rule1_reason is None and rule2 == "holds after A1a":
        rule3 = "option 1 reopens"
    elif rule1 == "ADOPT" and rule1_reason is None and rule2 == "does not hold after A1a":
        rule3 = "option 3 stands (close #168)"
    else:
        rule3 = NOT_EVALUATED

    rule1_entry = {"verdict": rule1, "boundFraction": WALL_OVERHEAD_MAX, "source": "prior verdict"}
    if prior is not None:
        rule1_entry["prior"] = prior
    if rule1_reason is not None:
        rule1_entry["reason"] = rule1_reason
    return {
        "rule1": rule1_entry,
        "rule2": {"verdict": rule2, "boundFraction": CPU_OVERHEAD_MAX, "concurrency": RULE2_CONCURRENCY,
                  "lane": "boundary", "platforms": rule2_platforms},
        "rule3": {"verdict": rule3},
    }


def summarize(platform_dirs: list[Path], lock_path: Path, repetitions: int = REPETITIONS,
              resamples: int = BOOTSTRAP_RESAMPLES, require_all_platforms: bool = False,
              prior_verdict: Path | None = None) -> dict:
    pairs_sha256 = load_lock(lock_path)
    prior = load_prior_verdict(prior_verdict) if prior_verdict is not None else None
    platforms = []
    seen = set()
    commits = set()
    workflow_run_numbers = set()
    lane_sets = set()
    for directory in platform_dirs:
        platform = read_platform(directory, pairs_sha256)
        if platform["platform"] in seen:
            raise SummaryError(f"{directory}: platform {platform['platform']} appears twice")
        seen.add(platform["platform"])
        commits.add(platform["gitCommit"])
        workflow_run_numbers.add(platform["workflowRunNumber"])
        lanes = platform["lanes"]
        lane_sets.add(lanes)
        if len(lane_sets) != 1:
            raise SummaryError(f"mixed lane sets across platforms {sorted(lane_sets)}")
        rng = random.Random(BOOTSTRAP_SEED)
        platform["timeStatistics"] = time_statistics(platform.pop("time"), repetitions, rng, resamples, lanes)
        platform["concurrentStatistics"] = concurrent_statistics(
            platform.pop("concurrent"), repetitions, rng, resamples, lanes)
        platform["memoryStatistics"] = memory_statistics(platform.pop("memory"), lanes)
        platform["alignment"] = alignment_statistics(platform.pop("prepare"))
        platforms.append(platform)

    if len(commits) != 1:
        raise SummaryError(f"mixed evidence across platforms, gitCommit values {sorted(commits)}")
    if len(workflow_run_numbers) != 1:
        raise SummaryError(
            f"mixed evidence across workflow dispatches, run numbers {sorted(workflow_run_numbers)}")
    if require_all_platforms and seen != set(PLATFORMS):
        raise SummaryError(f"expected platforms {list(PLATFORMS)}, found {sorted(seen)}")
    lanes = next(iter(lane_sets)) if lane_sets else RULE1_LANES
    if prior is not None and lanes != BOUNDARY_LANES:
        raise SummaryError("--prior-verdict applies only to a boundary run (lanes off, overlap, boundary)")

    return {
        "schema": SUMMARY_SCHEMA,
        "experiment": EXPERIMENT,
        "corpusPairsSha256": pairs_sha256,
        "repetitions": repetitions,
        "bootstrap": {
            "resamples": resamples,
            "seed": BOOTSTRAP_SEED,
            "decisionResamplingUnit": "paired-repetition-block",
            "fileBootstrap": "diagnostic-only",
        },
        "platforms": sorted(platforms, key=lambda platform: PLATFORMS.index(platform["platform"])),
        "rules": evaluate(platforms, lanes, prior),
    }


def write_compact_samples(document: dict, directory: Path) -> list[Path]:
    directory.mkdir(parents=True, exist_ok=True)
    written = []
    for platform in document["platforms"]:
        compact = {
            "schema": "chunkshift.patch-lab-apply-check-compact.v1",
            "experiment": EXPERIMENT,
            "platform": platform["platform"],
            "gitCommit": platform["gitCommit"],
            "runIds": platform["runIds"],
            "processorCount": platform["processorCount"],
            "processorDescription": platform["processorDescription"],
            "workflowRunNumber": platform["workflowRunNumber"],
            "runDate": platform["runDate"],
            "corpusPairsSha256": document["corpusPairsSha256"],
            "timeFiles": platform["timeStatistics"].get("compactFiles", []),
            "concurrentRepetitions": platform["concurrentStatistics"].get("compactRepetitions", []),
            "memoryFiles": platform["memoryStatistics"].get("compactFiles", []),
        }
        path = directory / f"{EXPERIMENT}-compact-{platform['platform']}.json"
        path.write_text(json.dumps(compact, indent=1, sort_keys=True) + "\n", encoding="utf-8")
        written.append(path)
    return written


# --- markdown# --- markdown ----------------------------------------------------------------------


def percent(value: float | None) -> str:
    return "—" if value is None else f"{value * 100:.2f} %"


def interval(stat: dict | None, key: str = "median") -> str:
    if stat is None:
        return "—"
    return f"{percent(stat[key])} [{percent(stat['ciLow'])}, {percent(stat['ciHigh'])}]"


def write_markdown(document: dict) -> str:
    lines = [f"# {EXPERIMENT} summary", ""]
    rules = document["rules"]
    rule1 = rules["rule1"]
    rule1_source = ""
    if rule1.get("source") == "prior verdict":
        prior = rule1.get("prior") or {}
        rule1_source = (f" (from the rule-1 run, verdict sha256 {prior.get('sha256', '—')}"
                        f"{'; ' + rule1['reason'] if rule1.get('reason') else ''})")
    lines += [
        f"- Rule 1 (A2, wall overhead upper CI bound <= {percent(WALL_OVERHEAD_MAX)} on "
        f"{PLATFORMS_REQUIRED} platforms): **{rule1['verdict']}**{rule1_source}",
        f"- Rule 2 (CPU overhead of lane {rules['rule2'].get('lane', 'overlap')} at c = {RULE2_CONCURRENCY} > "
        f"{percent(CPU_OVERHEAD_MAX)} on {PLATFORMS_REQUIRED} platforms): **{rules['rule2']['verdict']}**",
        f"- Rule 3 (#168): **{rules['rule3']['verdict']}**",
        "",
    ]

    for platform in document["platforms"]:
        time = platform["timeStatistics"]
        lanes = tuple(platform.get("lanes") or RULE1_LANES)
        lines += [f"## {platform['platform']}", "",
                  f"Commit `{platform['gitCommit']}`; RunIds: {', '.join(platform['runIds']) or '—'}; "
                  f"CPU: {platform['processorDescription']} ({platform['processorCount']} logical); "
                  f"time repetitions {time.get('repetitions')}, files {time.get('files', 0)}.", ""]
        if time.get("lanes"):
            lines += ["| lane | wall, median of ratios [95 % CI] | wall, corpus sum | wall, files >= 1 MiB | "
                      "CPU, median of ratios | CPU, corpus sum |",
                      "| --- | ---: | ---: | ---: | ---: | ---: |"]
            for lane in check_lanes(lanes):
                stats = time["lanes"][lane]
                lines.append(
                    f"| {lane} | {interval(stats['wallMedianOfRatios'])} | {interval(stats['wallCorpusSum'], 'value')} | "
                    f"{interval(stats['wallLargeFilesMedianOfRatios'])} | {interval(stats['cpuMedianOfRatios'])} | "
                    f"{interval(stats['cpuCorpusSum'], 'value')} |")
            totals = time["totals"]
            lines += ["", "| totals (sum of per-file medians) | " + " | ".join(lanes) + " |",
                      "| --- |" + " ---: |" * len(lanes)]
            for metric in ("wall", "cpu"):
                lines.append(f"| {metric} s | " + " | ".join(f"{totals[metric][lane]:.2f}" for lane in lanes) + " |")
            components = time["decomposition"]
            lines += ["", "| check component | wall s | CPU s | share of check wall |", "| --- | ---: | ---: | ---: |"]
            for name in (*COMPONENTS, "residual"):
                component = components[name]
                lines.append(f"| {name} | {component['wall']:.3f} | {component['cpu']:.3f} | "
                             f"{percent(component.get('shareOfCheckWall'))} |")
        concurrent = platform["concurrentStatistics"]
        if concurrent.get("levels"):
            checked = check_lanes(lanes)
            lines += ["", "| concurrency | " + " | ".join(f"{lane} CPU overhead" for lane in checked) + " | " +
                      " | ".join(f"{lane} wall overhead" for lane in checked) + " |",
                      "| ---: |" + " ---: |" * (2 * len(checked))]
            for level, record in concurrent["levels"].items():
                lines.append(f"| {level} | " +
                             " | ".join(interval(record[lane]["cpuOverhead"]) for lane in checked) + " | " +
                             " | ".join(interval(record[lane]["wallOverhead"]) for lane in checked) + " |")
        memory = platform["memoryStatistics"]
        if memory.get("worst"):
            lines += ["", f"Memory (idle {memory['idleBaselineBytes'] / MIB:.1f} MiB, {memory['records']} files): " +
                      ", ".join(f"{lane} worst +{memory['worst'][lane]['excessBytes'] / MIB:.1f} MiB"
                                for lane in lanes)]
        alignment = platform.get("alignment")
        if alignment:
            corpus = alignment["corpus"]
            if corpus["targetBytes"] > 0:
                lines += ["", f"Record only: same-offset bytes {percent(corpus['sameOffsetBytes'] / corpus['targetBytes'])}, "
                          f"in whole 4 KiB blocks {percent(corpus['alignedBytes'] / corpus['targetBytes'])} of target bytes."]
        lines.append("")
    return "\n".join(lines) + "\n"


def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("platforms", nargs="+", type=Path)
    parser.add_argument("--corpus-lock", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--markdown", type=Path)
    parser.add_argument("--compact-dir", type=Path)
    parser.add_argument("--require-all-platforms", action="store_true")
    parser.add_argument("--prior-verdict", type=Path,
                        help="verdict JSON of the rule-1 run; required to evaluate rule 3 of a boundary run")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        document = summarize(args.platforms, args.corpus_lock, require_all_platforms=args.require_all_platforms,
                             prior_verdict=args.prior_verdict)
    except SummaryError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    if args.markdown:
        args.markdown.write_text(write_markdown(document), encoding="utf-8")
    if args.compact_dir:
        write_compact_samples(document, args.compact_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
