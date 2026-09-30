#!/usr/bin/env python3
"""Recompute PATCH-APPLY-003/EVIDENCE-20260930-001 from the committed files and
check them against apply-check-verdict.json.

This is the run that evaluates rule 2 of PATCH-APPLY-002 section 6 again on the
built A1a, lane boundary, by docs/benchmarks/PATCH-APPLY-003-PROTOCOL.md. Its
lanes are off, overlap and boundary. From the three
PATCH-APPLY-003-compact-<platform>.json files this script recomputes:

  - per concurrency level and lane (overlap, boundary) the CPU and wall
    overhead over off per repetition, their median and the 95 % percentile
    bootstrap over repetitions (10,000 resamples, seed 20260928), drawing from
    the same random stream as benchmarks/scripts/summarize_apply_check.py.
    Rule 2 reads the boundary CPU median at c = 8;
  - per lane the point estimates of the time lane (wall and CPU median of
    per-file ratios, corpus sums, files of at least 1 MiB);
  - the worst memory excess over idle per lane.

From PATCH-APPLY-003-repetitions.jsonl (the two measured wall and CPU times of
every lane, file and repetition, and the output-identity bit and lane order of
every repetition) it rebuilds every per-file median of the compact files. With
--time-intervals it also rebuilds the paired repetition-block bootstrap
intervals of the time lane (section 2.1); no rule of this run reads them, and
they take about 18 minutes.

Rule 1 is not measured again: it comes from the verdict of the rule-1 run,
../PATCH-APPLY-003-20260929-001/apply-check-verdict.json, whose digest the
verdict records. Rules 2 and 3 are then applied as frozen from the recomputed
values. Every value must equal the verdict JSON exactly. Standard library
only; Python 3.12 or later, whose compensated float sum() the summarizer used
(CPython 3.14 in the workflow), so the sums agree to the last bit.
"""

import hashlib
import json
import random
import statistics
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PRIOR = HERE.parent / "PATCH-APPLY-003-20260929-001" / "apply-check-verdict.json"
EXPERIMENT = "PATCH-APPLY-003"
COMMIT = "fa77357f337165a0f98ee239adca21bc37de97ab"
PAIRS_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
LANES = ("off", "overlap", "boundary")
CHECK_LANES = ("overlap", "boundary")
REPETITIONS = 10
RESAMPLES = 10_000
SEED = 20260928
CPU_FLOOR = 0.020
LARGE = 1024 * 1024
CPU_MAX = 0.25
RULE2_C = 8
RULE2_LANE = "boundary"
MEMORY_LIMIT = 64 * 1024 * 1024
REQUIRED = 2
REPETITIONS_FILE = f"{EXPERIMENT}-repetitions.jsonl"

if sys.version_info < (3, 12):
    sys.exit("Python 3.12 or later is required: the summarizer's float sum() is compensated from 3.12 on")

TIME_INTERVALS = "--time-intervals" in sys.argv[1:]
failures = []


def check(label, mine, theirs):
    if mine != theirs:
        failures.append(f"{label}: recomputed {mine!r}, verdict {theirs!r}")


def lane_order(repetition):
    start = repetition % len(LANES)
    once = [LANES[(start + index) % len(LANES)] for index in range(len(LANES))]
    return once + once


# --- SHA256SUMS and the prior verdict -------------------------------------------

for line in (HERE / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
    digest, name = line.split(maxsplit=1)
    name = name.lstrip("*")
    actual = hashlib.sha256((HERE / name).read_bytes()).hexdigest()
    check(f"SHA256SUMS {name}", actual, digest)
    print(f"sha256 {name}: {'ok' if actual == digest else 'MISMATCH'}")

verdict = json.loads((HERE / "apply-check-verdict.json").read_text(encoding="utf-8"))
check("verdict experiment", verdict["experiment"], EXPERIMENT)
check("verdict corpus", verdict["corpusPairsSha256"], PAIRS_SHA256)
check("verdict repetitions", verdict["repetitions"], REPETITIONS)
check("verdict bootstrap", (verdict["bootstrap"]["resamples"], verdict["bootstrap"]["seed"]), (RESAMPLES, SEED))
summary = {platform["platform"]: platform for platform in verdict["platforms"]}
check("verdict platforms", sorted(summary), sorted(PLATFORMS))

prior_bytes = PRIOR.read_bytes()
prior = json.loads(prior_bytes)
prior_sha256 = hashlib.sha256(prior_bytes.replace(b"\r\n", b"\n")).hexdigest()
recorded_prior = verdict["rules"]["rule1"].get("prior") or {}
check("rule 1 source", verdict["rules"]["rule1"].get("source"), "prior verdict")
check("prior sha256", prior_sha256, recorded_prior.get("sha256"))
check("prior rule 1", prior["rules"]["rule1"]["verdict"], recorded_prior.get("rule1"))
check("prior rule 2", prior["rules"]["rule2"]["verdict"], recorded_prior.get("rule2"))
prior_run_ids = sorted(run_id for platform in prior["platforms"] for run_id in platform["runIds"])
check("prior RunIds", prior_run_ids, recorded_prior.get("runIds"))
print(f"prior verdict {PRIOR.name}: sha256 {prior_sha256}, rule 1 {prior['rules']['rule1']['verdict']}, "
      f"rule 2 {prior['rules']['rule2']['verdict']}")


# --- helpers ---------------------------------------------------------------------


def percentile(values, fraction):
    position = (len(values) - 1) * fraction
    lower = int(position)
    upper = min(lower + 1, len(values) - 1)
    weight = position - lower
    return values[lower] * (1 - weight) + values[upper] * weight


def ratio_stat(values, rng):
    if not values:
        return None
    estimates = sorted(statistics.median(rng.choices(values, k=len(values))) for _ in range(RESAMPLES))
    return {"median": statistics.median(values), "ciLow": percentile(estimates, 0.025),
            "ciHigh": percentile(estimates, 0.975), "count": len(values)}


def point(per_file, lane, metric, indices, minimum_size=None, corpus_sum=False, cpu_floor=False):
    """The summarizer's _time_point over the repetitions in indices."""
    ratios, pairs = [], []
    for size, values in per_file:
        if minimum_size is not None and size < minimum_size:
            continue
        off = statistics.median([values[metric]["off"][index] for index in indices])
        value = statistics.median([values[metric][lane][index] for index in indices])
        if off <= 0 or (cpu_floor and off < CPU_FLOOR):
            continue
        ratios.append(value / off - 1)
        pairs.append((value, off))
    if corpus_sum:
        denominator = sum(off for _, off in pairs)
        return ((sum(value for value, _ in pairs) / denominator - 1) if denominator > 0 else None), len(pairs)
    return (statistics.median(ratios) if ratios else None), len(ratios)


STATS = (
    ("wallMedianOfRatios", "median", dict(metric="wall")),
    ("wallCorpusSum", "value", dict(metric="wall", corpus_sum=True)),
    ("wallLargeFilesMedianOfRatios", "median", dict(metric="wall", minimum_size=LARGE)),
    ("cpuMedianOfRatios", "median", dict(metric="cpu", cpu_floor=True)),
    ("cpuCorpusSum", "value", dict(metric="cpu", corpus_sum=True)),
)


def load_repetitions():
    lines = (HERE / REPETITIONS_FILE).read_text(encoding="utf-8").splitlines()
    header = json.loads(lines[0])
    records = {}
    for line in lines[1:]:
        record = json.loads(line)
        records.setdefault(record["platform"], []).append(record)
    return header, records


rep_header, rep_records = load_repetitions()
check("repetitions schema", rep_header["schema"], "chunkshift.patch-apply-003-repetitions.v1")
check("repetitions commit", rep_header["gitCommit"], COMMIT)
check("repetitions corpus", rep_header["corpusPairsSha256"], PAIRS_SHA256)
check("repetitions lanes", rep_header["lanes"], list(LANES))
check("repetitions platforms", sorted(rep_header["platforms"]), sorted(PLATFORMS))


# --- per platform ------------------------------------------------------------------

rows = {}
for platform in PLATFORMS:
    compact = json.loads((HERE / f"{EXPERIMENT}-compact-{platform}.json").read_text(encoding="utf-8"))
    mine = summary[platform]
    check(f"{platform} schema", compact["schema"], "chunkshift.patch-lab-apply-check-compact.v1")
    check(f"{platform} platform", compact["platform"], platform)
    check(f"{platform} commit", compact["gitCommit"], COMMIT)
    check(f"{platform} verdict commit", mine["gitCommit"], COMMIT)
    check(f"{platform} verdict lanes", mine["lanes"], list(LANES))
    check(f"{platform} corpus", compact["corpusPairsSha256"], PAIRS_SHA256)
    for field in ("runIds", "processorCount", "processorDescription", "workflowRunNumber", "runDate"):
        check(f"{platform} {field}", compact[field], mine[field])
    run_id = f"{EXPERIMENT}/RUN-{compact['runDate']}-{compact['workflowRunNumber']}-{COMMIT[:7]}-{platform}"
    check(f"{platform} RunId", compact["runIds"], [run_id])

    # Time lane: the compact medians, rebuilt from every repetition.
    time = mine["timeStatistics"]
    check(f"{platform} time complete", time["complete"], True)
    check(f"{platform} time repetitions", time["repetitions"], list(range(REPETITIONS)))
    check(f"{platform} outputsVerified", time["outputsVerified"], True)
    files = compact["timeFiles"]
    keys = [(r["family"], r["base"], r["target"], r["path"]) for r in files]
    check(f"{platform} file order", keys, sorted(keys))
    check(f"{platform} file count", len(files), time["files"])
    check(f"{platform} compact files", files, time["compactFiles"])

    header = rep_header["platforms"][platform]
    check(f"{platform} repetitions RunId", [header["runId"]], compact["runIds"])
    check(f"{platform} repetitions outputsVerified", header["outputsVerified"], [True] * REPETITIONS)
    check(f"{platform} lane orders", header["laneOrders"], [lane_order(r) for r in range(REPETITIONS)])
    records = rep_records.get(platform, [])
    check(f"{platform} repetitions files", [(r["family"], r["base"], r["target"], r["path"], r["targetSize"])
                                            for r in records],
          [(*key, r["targetSize"]) for key, r in zip(keys, files)])
    per_file = []
    for record, compact_record in zip(records, files):
        values = {metric: {lane: [sum(runs) / 2 for runs in record[f"{metric}Seconds"][lane]] for lane in LANES}
                  for metric in ("wall", "cpu")}
        for metric in ("wall", "cpu"):
            for lane in LANES:
                if len(values[metric][lane]) != REPETITIONS:
                    failures.append(f"{platform} {record['path']}: {len(values[metric][lane])} repetitions of {lane}")
                elif statistics.median(values[metric][lane]) != compact_record[f"{metric}MedianSeconds"][lane]:
                    failures.append(f"{platform} {record['path']}: {metric} {lane} median differs from the compact file")
        per_file.append((record["targetSize"], values))

    # The summarizer draws every interval of this platform from one
    # Random(SEED): first the time intervals (repetition-block resamples of
    # REPETITIONS indices each, lanes overlap then boundary, in the order of
    # STATS), then the concurrency levels.
    rng = random.Random(SEED)
    indices = list(range(REPETITIONS))
    time_points = {}
    for lane in CHECK_LANES:
        for name, key, options in STATS:
            value, count = point(per_file, lane, indices=indices, **options)
            stat = time["lanes"][lane][name]
            check(f"{platform} {lane} {name}", value, stat[key])
            check(f"{platform} {lane} {name} count", count, stat["count"])
            check(f"{platform} {lane} {name} unit", stat["resamplingUnit"], "paired-repetition-block")
            time_points[lane, name] = (value, stat["ciLow"], stat["ciHigh"])
            if value is None:
                continue
            if TIME_INTERVALS:
                estimates = []
                for _ in range(RESAMPLES):
                    estimate, _ = point(per_file, lane, indices=rng.choices(indices, k=REPETITIONS), **options)
                    if estimate is not None:
                        estimates.append(estimate)
                estimates.sort()
                check(f"{platform} {lane} {name} ciLow", percentile(estimates, 0.025), stat["ciLow"])
                check(f"{platform} {lane} {name} ciHigh", percentile(estimates, 0.975), stat["ciHigh"])
                print(f"{platform} {lane} {name}: interval rebuilt from {REPETITIONS_FILE}")
            else:
                for _ in range(RESAMPLES):
                    rng.choices(indices, k=REPETITIONS)
    for metric in ("wall", "cpu"):
        for lane in LANES:
            total = sum(record[f"{metric}MedianSeconds"][lane] for record in files)
            check(f"{platform} total {metric} {lane}", total, time["totals"][metric][lane])

    # Concurrent lane.
    concurrent = mine["concurrentStatistics"]
    check(f"{platform} concurrent complete", concurrent["complete"], True)
    repetitions = compact["concurrentRepetitions"]
    check(f"{platform} concurrent repetitions", [r["repetition"] for r in repetitions], list(range(REPETITIONS)))
    check(f"{platform} compact concurrent", repetitions, concurrent["compactRepetitions"])
    levels = sorted({int(level) for r in repetitions for level in r["levels"]})
    check(f"{platform} levels", levels, [1, 2, 4, 8])
    level_stats = {}
    for level in levels:
        values = {metric: {lane: [r["levels"][str(level)][lane][f"{metric}Seconds"] for r in repetitions]
                           for lane in LANES} for metric in ("wall", "cpu")}
        for lane in CHECK_LANES:
            cpu = [v / o - 1 for v, o in zip(values["cpu"][lane], values["cpu"]["off"]) if o > 0]
            wall = [v / o - 1 for v, o in zip(values["wall"][lane], values["wall"]["off"]) if o > 0]
            cpu_stat, wall_stat = ratio_stat(cpu, rng), ratio_stat(wall, rng)
            check(f"{platform} c={level} {lane} cpu", cpu_stat, concurrent["levels"][str(level)][lane]["cpuOverhead"])
            check(f"{platform} c={level} {lane} wall", wall_stat, concurrent["levels"][str(level)][lane]["wallOverhead"])
            level_stats[level, lane] = (cpu_stat, wall_stat, values["cpu"][lane], values["cpu"]["off"])

    # Memory lane (record only for boundary).
    memory = mine["memoryStatistics"]
    check(f"{platform} memory environment", memory["reason"], None)
    memory_files = compact["memoryFiles"]
    check(f"{platform} compact memory", memory_files, memory["compactFiles"])
    check(f"{platform} memory records", len(memory_files), memory["records"])
    check(f"{platform} memory >= 1 MiB", all(r["targetSize"] >= LARGE for r in memory_files), True)
    worst = {lane: max(r["excessBytes"][lane] for r in memory_files) for lane in LANES}
    for lane in LANES:
        check(f"{platform} memory worst {lane}", worst[lane], memory["worst"][lane]["excessBytes"])
    check(f"{platform} memory verdict", worst["overlap"] <= MEMORY_LIMIT, memory["verdict"])
    check(f"{platform} boundary memory", worst["boundary"] <= MEMORY_LIMIT, memory["boundaryWithinBound"])

    rows[platform] = {
        "processors": (compact["processorCount"], compact["processorDescription"]),
        "levels": level_stats, "memory": worst, "time": time_points,
    }


# --- rules -------------------------------------------------------------------------

print()
verdict1 = prior["rules"]["rule1"]["verdict"]
print(f"Rule 1 (A2): {verdict1}, from the rule-1 run ({', '.join(prior_run_ids)})")
check("rule 1", verdict1, verdict["rules"]["rule1"]["verdict"])

print()
print(f"Rule 2 (A1a), evaluated again: CPU overhead of {RULE2_LANE} over off at c = {RULE2_C}, "
      f"median over repetitions")
rule2 = {}
for platform in PLATFORMS:
    count, description = rows[platform]["processors"]
    stat, _, lane_cpu, off_cpu = rows[platform]["levels"][RULE2_C, RULE2_LANE]
    overlap = rows[platform]["levels"][RULE2_C, "overlap"][0]
    if count > RULE2_C:
        rule2[platform] = None
        print(f"  {platform:11} {description} ({count} logical): not evaluated (> {RULE2_C})")
    else:
        rule2[platform] = stat["median"] > CPU_MAX
        print(f"  {platform:11} {description} ({count} logical): {RULE2_LANE} median {stat['median']!r} "
              f"[{stat['ciLow']!r}, {stat['ciHigh']!r}] ({'>' if rule2[platform] else '<='} 25 %); "
              f"overlap {overlap['median']!r}")
    entry = verdict["rules"]["rule2"]["platforms"][platform]
    check(f"{platform} rule 2 exceeds", rule2[platform], entry["exceeds"])
    if rule2[platform] is not None:
        check(f"{platform} rule 2 median", stat["median"], entry["median"])
exceeds = [p for p, e in rule2.items() if e is True]
within = [p for p, e in rule2.items() if e is False]
verdict2 = ("holds after A1a" if len(exceeds) >= REQUIRED else
            "does not hold after A1a" if len(within) >= REQUIRED else "not evaluated")
print(f"  exceeds {exceeds}, within {within} -> {verdict2}")
check("rule 2 lane", verdict["rules"]["rule2"]["lane"], RULE2_LANE)
check("rule 2", verdict2, verdict["rules"]["rule2"]["verdict"])

prior_required = prior["rules"]["rule2"]["verdict"] == "A1a required"
if verdict1 == "REJECT":
    verdict3 = "option 1 reopens"
elif verdict1 == "ADOPT" and prior_required and verdict2 == "holds after A1a":
    verdict3 = "option 1 reopens"
elif verdict1 == "ADOPT" and prior_required and verdict2 == "does not hold after A1a":
    verdict3 = "option 3 stands (close #168)"
else:
    verdict3 = "not evaluated"
print()
print(f"Rule 3 (#168): {verdict3}")
check("rule 3", verdict3, verdict["rules"]["rule3"]["verdict"])

print()
if failures:
    print(f"{len(failures)} mismatch(es) against apply-check-verdict.json:")
    for failure in failures:
        print(f"  {failure}")
    sys.exit(1)
print("every recomputed value equals apply-check-verdict.json")
