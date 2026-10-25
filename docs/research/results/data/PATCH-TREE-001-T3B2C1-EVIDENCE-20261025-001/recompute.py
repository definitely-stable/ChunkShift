#!/usr/bin/env python3
"""Independent stdlib-only T3-B2C1 evidence replay; no producer/SDK imports."""
import argparse
import csv
import hashlib
import io
import json
import math
import statistics
import zipfile
from pathlib import Path

EXPECTED_ZIP = "554944ebbec4e69d6b26f88bca6fcf6bf1faa0dc6d999bf0be2f03b45a280efc"
EXPECTED_SOURCE = "df61c8534735e1e8576d3702ab8774248dd41250"
DIGESTS = {
    "original-pairs.json": "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "original-b1-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "original-b2a-selected.json": "1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b",
    "rehash-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "ranges.json": "2d5fc41b882b83df940e88a6f28a4c45d5019f4d494eef7228af7026e7554a5e",
    "provenance.json": "886d2e7aa00292b02ee7b0c006e5b7e1cd2f472834bee5cd64a689e82c873d81",
}
PAIRS = (
    ("10.0.10", "10.0.11", 36133305, 86727, 161,
     "24a5579e96411f64c9b790e68d4ff01cbeb42f79500094553007fd1330e5dd29"),
    ("10.0.11", "10.0.12", 14522936, 5944, 53,
     "de7e1d02606c7663dac0aaaad86da4b4839552e58a6a00ee7cc99b6498c52103"),
)
FIELDS = (
    "pair", "lane", "round", "warmup", "cpu_ms", "wall_ms",
    "base_read_B", "base_distinct_B", "base_repeat_B", "base_seeks",
    "backward_seeks", "cache_loads", "cache_hits", "cache_peak_B",
    "window_peak_B", "alloc_B", "patch_sha256",
)


def require(value, message):
    if not value:
        raise ValueError("T3B2C1 independently replayed evidence: " + message)


def near(a, b):
    return math.isclose(a, b, rel_tol=1e-10, abs_tol=1e-9)


def read_rows(path):
    with path.open(newline="", encoding="utf-8") as source:
        reader = csv.DictReader(source, delimiter="\t")
        require(tuple(reader.fieldnames or ()) == FIELDS, "sample table schema")
        return list(reader)


def replay_samples(rows):
    require(len(rows) == 16, "all 16 original samples")
    output = []
    for idx, (base, target, h2_reads, warm_bytes, h3_hits, expected_sha) in enumerate(PAIRS, 1):
        block = rows[(idx - 1) * 8:idx * 8]
        chronology = [("H2", -1, 1), ("H3", -1, 1)]
        for round_id in range(3):
            order = ("H2", "H3") if round_id % 2 == 0 else ("H3", "H2")
            chronology.extend((lane, round_id, 0) for lane in order)
        require([(s["lane"], int(s["round"]), int(s["warmup"])) for s in block] == chronology,
                "paired AB/BA chronology")
        medians = {}
        for lane in ("H2", "H3"):
            measured = []
            for sample in (s for s in block if s["lane"] == lane):
                read = int(sample["base_read_B"])
                distinct = int(sample["base_distinct_B"])
                repeated = int(sample["base_repeat_B"])
                require(int(sample["pair"]) == idx and
                        sample["patch_sha256"] == expected_sha and
                        distinct == 1356632 and repeated == read - distinct and
                        read == (h2_reads if lane == "H2" else distinct), "base coverage + CSP")
                require(float(sample["cpu_ms"]) >= 0 and float(sample["wall_ms"]) > 0 and
                        int(sample["alloc_B"]) >= 0 and int(sample["window_peak_B"]) > 0,
                        "observed resource measures")
                if lane == "H2":
                    require(int(sample["cache_loads"]) == int(sample["cache_hits"]) ==
                            int(sample["cache_peak_B"]) == 0, "H2 no cache")
                else:
                    require(int(sample["cache_loads"]) == 20 and
                            int(sample["cache_hits"]) == h3_hits and
                            int(sample["cache_peak_B"]) > 0 and
                            int(sample["backward_seeks"]) == 0, "H3 cache actually used")
                if sample["warmup"] == "0":
                    measured.append(sample)
            require(len(measured) == 3, "three measured samples per mode")
            medians[lane] = {
                "cpu": statistics.median(float(s["cpu_ms"]) for s in measured),
                "wall": statistics.median(float(s["wall_ms"]) for s in measured),
            }
        ratio = medians["H3"]["cpu"] / medians["H2"]["cpu"]
        output.append((base + "->" + target, h2_reads, 1356632, ratio))
        require((warm_bytes - 295) in (86432, 5649), "original CSP physical sizes")
    require(not all(item[3] < 1 for item in output), "H3 does not improve both CPU medians")
    return output


def replay_original(path, rows):
    require(hashlib.sha256(path.read_bytes()).hexdigest() == EXPECTED_ZIP, "original artifact ZIP digest")
    with zipfile.ZipFile(path) as archive:
        require(archive.testzip() is None, "ZIP CRC")
        require(set(archive.namelist()) == set(DIGESTS) | {"SHA256SUMS"}, "ZIP exact membership")
        checksums = {name: digest for digest, name in
                     (line.split() for line in archive.read("SHA256SUMS").decode("ascii").splitlines())}
        require(checksums == DIGESTS, "original SHA256SUMS")
        for name, digest in DIGESTS.items():
            require(hashlib.sha256(archive.read(name)).hexdigest() == digest, "member "+name)
        provenance = json.loads(archive.read("provenance.json"))
        require(provenance["runId"] == "40191549066" and provenance["sourceSha"] == EXPECTED_SOURCE and
                provenance["runAttempt"] == "1" and provenance["ref"] == "refs/heads/main" and
                provenance["runnerEnvironment"] == "github-hosted" and
                provenance["runnerOS"] == "Linux" and provenance["runnerArch"] == "X64" and
                provenance["holdoutOpened"] is False and provenance["rawCspArchived"] is False,
                "original run provenance")
        require(provenance["originalPairsRunId"] == 38136336025 and
                provenance["originalB1RunId"] == 39967460136 and
                provenance["originalB2aRunId"] == 39977481690, "pinned ancestor runs")
        b1 = json.loads(archive.read("original-b1-selection.json"))
        b2 = json.loads(archive.read("original-b2a-selected.json"))
        require(b1 == json.loads(archive.read("rehash-selection.json")) and
                len(b1["candidates"]) == 2 and
                len([c for p in b2["pairs"] for c in p["candidates"]]) == 2 and
                b2["totals"]["fallBackCount"] == 0, "source-locked selected pairs")
        results = json.loads(archive.read("ranges.json"))
        require(results["schema"] == "chunkshift.patch-tree-t3b2c1-source-ranges.v1" and
                results["status"] == "ORIGINAL_CALIBRATION_H2_H3_ATTRIBUTION_NO_ADOPT" and
                results["originalPairsSha256"] == DIGESTS["original-pairs.json"] and
                results["warmupsPerLane"] == 1 and results["measuredRoundsPerLane"] == 3 and
                len(results["pairs"]) == 2, "original result schema")
        archived = []
        for idx, (pair, (base, target, h2_read, physical, hits, sha)) in enumerate(zip(results["pairs"], PAIRS), 1):
            require((pair["baseVersion"], pair["targetVersion"]) == (base, target) and
                    pair["sourceLength"] == 1356632 and pair["baseCsmBytes"] == 1114 and
                    pair["diagnosticLocatorBytes"] == 295 and
                    pair["h2WarmPatchBytes"] == pair["h3WarmPatchBytes"] == physical,
                    "original pair/CSM/physical CSP")
            for key in ("sourcePath", "targetPath", "sourceSha256", "targetSha256",
                        "sourceLength", "targetLength"):
                require(pair[key] == b1["candidates"][idx - 1][key], "pinned DLL "+key)
            for sample in pair["samples"]:
                p = sample["sourceReads"]
                require(sample["reconstructedAndVerified"] is True and sample["patchSha256"] == sha and
                        sample["patchBytes"] + 295 == physical and sample["targetReadBytes"] > 0 and
                        p["repeatedReadBytes"] == p["logicalReadBytes"] - p["uniqueCoveredBytes"] and
                        p["uniqueCoveredBytes"] == 1356632 and p["readRanges"] <= 100000,
                        "actual physical CSP/apply and range bounds")
                archived.append([
                    idx, sample["lane"], sample["round"], int(sample["warmup"]),
                    sample["processCpuMilliseconds"], sample["wallMilliseconds"],
                    p["logicalReadBytes"], p["uniqueCoveredBytes"], p["repeatedReadBytes"],
                    p["positionSets"], p["backwardPositionSets"], sample["cacheLoads"],
                    sample["cacheHits"], sample["cachePeakBytes"], sample["windowPeakBytes"],
                    sample["processGcAllocatedBytes"], sample["patchSha256"],
                ])
            lanes = {
                lane: [s for s in pair["samples"] if s["lane"] == lane and not s["warmup"]]
                for lane in ("H2", "H3")
            }
            for key, lane, metric in (
                ("medianH2CpuMilliseconds", "H2", "processCpuMilliseconds"),
                ("medianH3CpuMilliseconds", "H3", "processCpuMilliseconds"),
                ("medianH2WallMilliseconds", "H2", "wallMilliseconds"),
                ("medianH3WallMilliseconds", "H3", "wallMilliseconds"),
            ):
                require(near(pair[key], statistics.median(s[metric] for s in lanes[lane])),
                        "independent median "+key)
            require(near(pair["h3OverH2CpuRatio"],
                         pair["medianH3CpuMilliseconds"] / pair["medianH2CpuMilliseconds"]) and
                    near(pair["h3OverH2WallRatio"],
                         pair["medianH3WallMilliseconds"] / pair["medianH2WallMilliseconds"]),
                    "original CPU/wall ratios")
        require([[str(x) for x in row] for row in archived] ==
                [[row[k] for k in FIELDS] for row in rows], "committed TSV equals every original ZIP sample")


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--original-zip", type=Path, help="Original GitHub artifact ZIP for full SHA/CRC/provenance replay")
    args = cli.parse_args()
    rows = read_rows(Path(__file__).with_name("samples.tsv"))
    output = replay_samples(rows)
    if args.original_zip:
        replay_original(args.original_zip, rows)
    print("T3B2C1 independent sample replay PASS:", output)
    print("H3_BOUNDED_CACHE_READS_LESS / NO_CPU_WIN / NO_PRODUCTION_ADOPT")


if __name__ == "__main__":
    main()
