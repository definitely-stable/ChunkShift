#!/usr/bin/env python3
"""Recompute PATCH-APPLY-003/EVIDENCE-20260929-001 from the committed compact
samples and check them against apply-check-verdict.json.

By docs/benchmarks/PATCH-APPLY-003-PROTOCOL.md (and PATCH-APPLY-002 section 5
and 6 that it inherits), from the three PATCH-APPLY-003-compact-<platform>.json
files this script recomputes:

  - per lane (seq, overlap) the wall median of per-file ratios, the wall
    corpus sum, the wall median over files of at least 1 MiB, the CPU median
    of ratios over files whose off median CPU is at least 20 ms, and the CPU
    corpus sum (the point estimates of rule 1 and its section 2.2 companions);
  - per concurrency level the CPU and wall overhead per repetition, their
    median and the 95 % percentile bootstrap over repetitions (10,000
    resamples, seed 20260928), drawing from the same random stream as
    benchmarks/scripts/summarize_apply_check.py;
  - the worst memory excess over idle per lane.

Every value must equal the verdict JSON exactly. The upper CI bound of rule 1
is the paired repetition-block bootstrap of section 2.1; it needs the
per-repetition values of every file, which the compact files do not hold, so
it is read from the verdict JSON (the raw artifacts reproduce it with the
summarizer). Rules 1-3 are then applied as frozen. Standard library only;
Python 3.12 or later, whose compensated float sum() the summarizer used
(CPython 3.14 in the workflow), so the corpus sums agree to the last bit.
"""

import hashlib
import json
import random
import statistics
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPERIMENT = "PATCH-APPLY-003"
COMMIT = "d62a2f1d472799d55c29f623571f540b15005385"
PAIRS_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
LANES = ("off", "seq", "overlap")
CHECK_LANES = ("seq", "overlap")
REPETITIONS = 10
RESAMPLES = 10_000
SEED = 20260928
CPU_FLOOR = 0.020
LARGE = 1024 * 1024
WALL_MAX = 0.10
CPU_MAX = 0.25
RULE2_C = 8
MEMORY_LIMIT = 64 * 1024 * 1024
REQUIRED = 2
TIME_INTERVALS = 5  # wall ratios, wall sum, wall >= 1 MiB, CPU ratios, CPU sum

if sys.version_info < (3, 12):
    sys.exit("Python 3.12 or later is required: the summarizer's float sum() is compensated from 3.12 on")

failures = []


def check(label, mine, theirs):
    if mine != theirs:
        failures.append(f"{label}: recomputed {mine!r}, verdict {theirs!r}")


def pct(value):
    return "—" if value is None else f"{value * 100:.2f} %"


# --- SHA256SUMS ---------------------------------------------------------------

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


# --- time lanes -----------------------------------------------------------------


def time_point(files, lane, metric, minimum_size=None, corpus_sum=False, cpu_floor=False):
    ratios, pairs = [], []
    for record in files:
        if minimum_size is not None and record["targetSize"] < minimum_size:
            continue
        values = record[f"{metric}MedianSeconds"]
        off, value = values["off"], values[lane]
        if off <= 0 or (cpu_floor and off < CPU_FLOOR):
            continue
        ratios.append(value / off - 1)
        pairs.append((value, off))
    if corpus_sum:
        denominator = sum(off for _, off in pairs)
        return (sum(value for value, _ in pairs) / denominator - 1 if denominator > 0 else None), len(pairs)
    return (statistics.median(ratios) if ratios else None), len(ratios)


STATS = (
    ("wallMedianOfRatios", "median", dict(metric="wall")),
    ("wallCorpusSum", "value", dict(metric="wall", corpus_sum=True)),
    ("wallLargeFilesMedianOfRatios", "median", dict(metric="wall", minimum_size=LARGE)),
    ("cpuMedianOfRatios", "median", dict(metric="cpu", cpu_floor=True)),
    ("cpuCorpusSum", "value", dict(metric="cpu", corpus_sum=True)),
)


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


rows = {}
for platform in PLATFORMS:
    compact = json.loads((HERE / f"{EXPERIMENT}-compact-{platform}.json").read_text(encoding="utf-8"))
    mine = summary[platform]
    check(f"{platform} schema", compact["schema"], "chunkshift.patch-lab-apply-check-compact.v1")
    check(f"{platform} platform", compact["platform"], platform)
    check(f"{platform} commit", compact["gitCommit"], COMMIT)
    check(f"{platform} verdict commit", mine["gitCommit"], COMMIT)
    check(f"{platform} corpus", compact["corpusPairsSha256"], PAIRS_SHA256)
    for field in ("runIds", "processorCount", "processorDescription", "workflowRunNumber", "runDate"):
        check(f"{platform} {field}", compact[field], mine[field])
    run_id = f"{EXPERIMENT}/RUN-{compact['runDate']}-{compact['workflowRunNumber']}-{COMMIT[:7]}-{platform}"
    check(f"{platform} RunId", compact["runIds"], [run_id])

    time = mine["timeStatistics"]
    check(f"{platform} time complete", time["complete"], True)
    check(f"{platform} time repetitions", time["repetitions"], list(range(REPETITIONS)))
    check(f"{platform} outputsVerified", time["outputsVerified"], True)
    files = compact["timeFiles"]
    keys = [(r["family"], r["base"], r["target"], r["path"]) for r in files]
    check(f"{platform} file order", keys, sorted(keys))
    check(f"{platform} file count", len(files), time["files"])
    check(f"{platform} compact files", files, time["compactFiles"])

    point = {}
    for lane in CHECK_LANES:
        for name, key, options in STATS:
            value, count = time_point(files, lane, **options)
            stat = time["lanes"][lane][name]
            check(f"{platform} {lane} {name}", value, stat[key])
            check(f"{platform} {lane} {name} count", count, stat["count"])
            check(f"{platform} {lane} {name} unit", stat["resamplingUnit"], "paired-repetition-block")
            point[lane, name] = (value, stat["ciLow"], stat["ciHigh"], count)
    for metric in ("wall", "cpu"):
        for lane in LANES:
            total = sum(record[f"{metric}MedianSeconds"][lane] for record in files)
            check(f"{platform} total {metric} {lane}", total, time["totals"][metric][lane])

    # Concurrent lane: the summarizer draws every interval of this platform
    # from one Random(SEED): first the ten time intervals, repetition-block
    # resamples of REPETITIONS indices each, then the concurrency levels.
    rng = random.Random(SEED)
    for lane in CHECK_LANES:
        for name, _, _ in STATS:
            if point[lane, name][0] is not None:
                for _ in range(RESAMPLES):
                    rng.choices(range(REPETITIONS), k=REPETITIONS)
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
            level_stats[level, lane] = (cpu_stat, wall_stat)

    memory = mine["memoryStatistics"]
    check(f"{platform} memory environment", memory["reason"], None)
    memory_files = compact["memoryFiles"]
    check(f"{platform} compact memory", memory_files, memory["compactFiles"])
    check(f"{platform} memory records", len(memory_files), memory["records"])
    check(f"{platform} memory >= 1 MiB", all(r["targetSize"] >= LARGE for r in memory_files), True)
    worst = {lane: max(r["excessBytes"][lane] for r in memory_files) for lane in LANES}
    for lane in LANES:
        check(f"{platform} memory worst {lane}", worst[lane], memory["worst"][lane]["excessBytes"])
    memory_ok = worst["overlap"] <= MEMORY_LIMIT
    check(f"{platform} memory verdict", memory_ok, memory["verdict"])

    rows[platform] = {
        "processors": (compact["processorCount"], compact["processorDescription"]),
        "point": point, "levels": level_stats, "memory": worst, "memoryOk": memory_ok,
        "outputs": time["outputsVerified"] is True,
    }


# --- rules -------------------------------------------------------------------------

print()
print("Rule 1 (A2): wall overhead of overlap over off; section 2.2 companions")
rule1 = {}
for platform in PLATFORMS:
    point = rows[platform]["point"]
    primary, _, high, _ = point["overlap", "wallMedianOfRatios"]
    corpus_sum = point["overlap", "wallCorpusSum"][0]
    large = point["overlap", "wallLargeFilesMedianOfRatios"][0]
    met = high <= WALL_MAX
    conflict = met and (corpus_sum > WALL_MAX or large > WALL_MAX)
    rule1[platform] = (met, conflict)
    print(f"  {platform:11} primary median {primary!r}, upper 95 % bound {high!r} "
          f"({'<=' if met else '>'} 10 %)")
    print(f"  {'':11} corpus sum {corpus_sum!r}; files >= 1 MiB median {large!r}"
          f"{'  -> conflicted' if conflict else ''}")
    print(f"  {'':11} outputs verified {rows[platform]['outputs']}; overlap worst memory "
          f"{rows[platform]['memory']['overlap'] / 2**20:.3f} MiB over idle (<= 64: {rows[platform]['memoryOk']})")
    entry = summary[platform]
    check(f"{platform} rule1 ciHigh", high, verdict["rules"]["rule1"]["platforms"][platform]["ciHigh"])
    check(f"{platform} rule1 conflict", conflict, verdict["rules"]["rule1"]["platforms"][platform]["companionConflict"])

met = [p for p, (m, c) in rule1.items() if m and not c]
conflicted = [p for p, (m, c) in rule1.items() if m and c]
missed = [p for p, (m, _) in rule1.items() if not m]
conditions = all(rows[p]["outputs"] and rows[p]["memoryOk"] for p in PLATFORMS)
if len(missed) >= REQUIRED:
    verdict1 = "REJECT"
elif len(met) >= REQUIRED:
    verdict1 = "ADOPT" if conditions else "DEFER"
elif conflicted:
    verdict1 = "DEFER"
else:
    verdict1 = "not evaluated"
print(f"  clean pass {met}, conflicted {conflicted}, missed {missed} -> {verdict1}")

print()
print(f"Rule 2 (A1a): CPU overhead of the adopted lane over off at c = {RULE2_C}, median over repetitions")
lane2 = "overlap" if verdict1 == "ADOPT" else "seq"
rule2 = {}
for platform in PLATFORMS:
    count, description = rows[platform]["processors"]
    stat = rows[platform]["levels"][RULE2_C, lane2][0]
    if count > RULE2_C:
        rule2[platform] = None
        print(f"  {platform:11} {description} ({count} logical): not evaluated (> {RULE2_C})")
    else:
        rule2[platform] = stat["median"] > CPU_MAX
        print(f"  {platform:11} {description} ({count} logical): {lane2} median {stat['median']!r} "
              f"[{stat['ciLow']!r}, {stat['ciHigh']!r}] ({'>' if rule2[platform] else '<='} 25 %)")
exceeds = [p for p, e in rule2.items() if e is True]
within = [p for p, e in rule2.items() if e is False]
verdict2 = ("A1a required" if len(exceeds) >= REQUIRED else
            "A1a not required" if len(within) >= REQUIRED else "not evaluated")
print(f"  exceeds {exceeds}, within {within} -> {verdict2}")

if verdict1 == "REJECT":
    verdict3 = "option 1 reopens"
elif verdict1 == "ADOPT" and verdict2 == "A1a not required":
    verdict3 = "option 3 stands (close #168)"
elif verdict1 == "ADOPT" and verdict2 == "A1a required":
    verdict3 = "pending A1a"
else:
    verdict3 = "not evaluated"
print()
print(f"Rule 3 (#168): {verdict3}")

check("rule 1", verdict1, verdict["rules"]["rule1"]["verdict"])
check("rule 2", verdict2, verdict["rules"]["rule2"]["verdict"])
check("rule 3", verdict3, verdict["rules"]["rule3"]["verdict"])

print()
if failures:
    print(f"{len(failures)} mismatch(es) against apply-check-verdict.json:")
    for failure in failures:
        print(f"  {failure}")
    sys.exit(1)
print("every recomputed value equals apply-check-verdict.json")
