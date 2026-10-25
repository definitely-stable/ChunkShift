#!/usr/bin/env python3
"""T3-B2D0 independent archive/source-identity and sample replay; stdlib only.

Consumes frozen original Actions ZIP when provided. It never imports the
production patch builder or producer lab. Archived ZIP lacks raw source/CSP:
this validates their recorded SHA/provenance, not a second physical decoder.
"""
import argparse
import csv
import hashlib
import json
import math
import statistics
import struct
import zipfile
from pathlib import Path

ZIP_SHA = "cc8aeb691d6c1cbdd93db008027d7b12d6f2a606633a623f7f38ef7812f2ddcb"
SOURCE_SHA = "bb38ec50eb12b2da6ac985d6ac052224d63878cc"
DIGESTS = {
    "original-pairs.json": "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "original-b1-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "original-b2a-selected.json": "1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b",
    "rehash-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "stages.json": "7ea88a26992d5ea98c1b0216c08fc26f1ff2407aebcc9575de5821ec1fba1ca0",
    "provenance.json": "fcf9183416d2bd7735d9a91fb39bf6a194acff09aa1dbee29fde42da11d8d1f6",
}
FIELDS = ("pair", "lane", "round", "warmup", "cpu_ms", "wall_ms", "plain_ticks",
          "read_ticks", "dict_ticks", "verify_ticks", "entries", "dict_reads",
          "dict_encodes", "verified_records", "patch_sha256")
PAIRS = (
    ("10.0.10", "10.0.11", 86432, 20, 136, 77,
     "24a5579e96411f64c9b790e68d4ff01cbeb42f79500094553007fd1330e5dd29"),
    ("10.0.11", "10.0.12", 5649, 9, 57, 30,
     "de7e1d02606c7663dac0aaaad86da4b4839552e58a6a00ee7cc99b6498c52103"),
)


def require(condition, message):
    if not condition:
        raise ValueError("B2D0 independent original replay: " + message)


def near(x, y):
    return math.isclose(x, y, rel_tol=1e-10, abs_tol=1e-9)


def samples():
    with Path(__file__).with_name("samples.tsv").open(newline="", encoding="utf-8") as stream:
        reader = csv.DictReader(stream, delimiter="\t")
        require(tuple(reader.fieldnames or ()) == FIELDS, "TSV exact schema")
        rows = list(reader)
    require(len(rows) == 24, "all 24 original rows")
    for index, (_, _, physical, entries, reads, verified, sha) in enumerate(
            ((b, t, physical, entries, reads, verified, sha)
             for b, t, physical, entries, reads, verified, sha in PAIRS), 1):
        batch = rows[(index-1)*12:index*12]
        assert_chronology = [("OFF_A", -1, 1), ("OFF_B", -1, 1), ("ON", -1, 1)]
        assert_chronology.extend(
            (lane, r, 0) for r, order in enumerate(
                (("OFF_A", "ON", "OFF_B"),
                 ("ON", "OFF_B", "OFF_A"),
                 ("OFF_B", "OFF_A", "ON"))) for lane in order)
        require([(r["lane"], int(r["round"]), int(r["warmup"])) for r in batch]
                == assert_chronology, "OFF_A/OFF_B/ON rotation")
        require(all(int(r["pair"]) == index and r["patch_sha256"] == sha for r in batch),
                "original patch SHA population")
        for row in batch:
            require(float(row["wall_ms"]) > 0 and float(row["cpu_ms"]) >= 0,
                    "nonnegative process counters")
            if row["lane"] == "ON":
                require(int(row["entries"]) == entries and
                        int(row["dict_reads"]) == int(row["dict_encodes"]) == reads and
                        int(row["verified_records"]) == verified and
                        all(int(row[key]) >= 0 for key in
                            ("plain_ticks","read_ticks","dict_ticks","verify_ticks")),
                        "measured stage counts/ticks")
            else:
                require(all(row[key] == "" for key in
                            ("plain_ticks","read_ticks","dict_ticks","verify_ticks",
                             "entries","dict_reads","dict_encodes","verified_records")),
                        "OFF probe must be absent")
        yield index, batch


def independent_medians(rows):
    output = []
    for index, batch in rows:
        lanes = {
            name: [s for s in batch if s["lane"] == name and s["warmup"] == "0"]
            for name in ("OFF_A", "OFF_B", "ON")
        }
        require(all(len(s) == 3 for s in lanes.values()), "measured counts")
        cpu = {name: statistics.median(float(s["cpu_ms"]) for s in row)
               for name, row in lanes.items()}
        wall = {name: statistics.median(float(s["wall_ms"]) for s in row)
                for name, row in lanes.items()}
        baseline = statistics.median(float(s["cpu_ms"]) for s in
            lanes["OFF_A"] + lanes["OFF_B"])
        require(all(t > 0 for t in cpu.values()) and baseline > 0,
                "valid independent CPU denominators")
        med = {
            "pair": index,
            "cpu": cpu,
            "wall": wall,
            "offBOverOffACpu": cpu["OFF_B"] / cpu["OFF_A"],
            "onOverPooledOffCpu": cpu["ON"] / baseline,
            "ticks": {key: statistics.median(int(s[key]) for s in lanes["ON"])
                      for key in ("plain_ticks", "read_ticks", "dict_ticks", "verify_ticks")}
        }
        require(med["ticks"]["dict_ticks"] > med["ticks"]["plain_ticks"]
                > med["ticks"]["read_ticks"] > med["ticks"]["verify_ticks"],
                "observed stage elapsed ordering on both original pairs")
        output.append(med)
    return output


def original(zip_path, rows, calculations):
    require(hashlib.sha256(zip_path.read_bytes()).hexdigest() == ZIP_SHA, "original ZIP SHA")
    with zipfile.ZipFile(zip_path) as archive:
        require(archive.testzip() is None, "ZIP CRC")
        require(set(archive.namelist()) == set(DIGESTS) | {"SHA256SUMS"}, "ZIP entries")
        listed = {}
        for line in archive.read("SHA256SUMS").decode("ascii").splitlines():
            digest, name = line.split()
            require(name not in listed, "duplicate checksum entry")
            listed[name] = digest
        require(listed == DIGESTS, "independent fixed original per-member hashes")
        for name, digest in DIGESTS.items():
            require(hashlib.sha256(archive.read(name)).hexdigest() == digest,
                    "member SHA "+name)

        provenance = json.loads(archive.read("provenance.json"))
        require(provenance == {
            "schema": "chunkshift.patch-tree-t3b2d0-original-provenance.v1",
            "runId": "40194727803", "runAttempt": "1", "sourceSha": SOURCE_SHA,
            "ref": "refs/heads/main", "runnerEnvironment": "github-hosted",
            "runnerOS": "Linux", "runnerArch": "X64",
            "originalPairsRunId": 38136336025, "originalB1RunId": 39967460136,
            "originalB2aRunId": 39977481690, "holdoutOpened": False,
            "rawCspArchived": False,
        }, "first-run provenance / original-source controls")
        selection = json.loads(archive.read("original-b1-selection.json"))
        require(selection == json.loads(archive.read("rehash-selection.json")) and
                selection["originalPairsSha256"] == DIGESTS["original-pairs.json"],
                "verified B1 source selection")
        b2a = json.loads(archive.read("original-b2a-selected.json"))
        found = [c for p in b2a["pairs"] for c in p["candidates"]]
        require(len(found) == len(selection["candidates"]) == 2 and
                b2a["totals"]["fallBackCount"] == 0, "B2A structural selector locked")
        for a, b in zip(selection["candidates"], found):
            for field in ("sourcePath","targetPath","sourceSha256","targetSha256",
                          "sourceLength","targetLength"):
                require(a[field] == b[field], "B1/B2A identity "+field)
        evidence = json.loads(archive.read("stages.json"))
        require(evidence["schema"] == "chunkshift.patch-tree-t3b2d0-encode-stage.v1" and
                evidence["status"] == "B2D0_ORIGINAL_STAGE_CALIBRATION_NO_VERDICT_NO_ADOPT" and
                evidence["originalPairsSha256"] == DIGESTS["original-pairs.json"] and
                evidence["warmupsPerLane"] == 1 and evidence["measuredRoundsPerLane"] == 3 and
                len(evidence["pairs"]) == 2, "original stage envelope")
        for index, (pair, expected, (_, tsv), med) in enumerate(
                zip(evidence["pairs"], PAIRS, rows, calculations), 1):
            base, target, size, nentries, reads, verified, sha = expected
            require(pair["baseVersion"] == base and pair["targetVersion"] == target and
                    pair["sourceLength"] == 1356632 and pair["baseCsmBytes"] == 1114 and
                    pair["mapBytes"] == 295 and pair["patchBytes"] == size and
                    pair["patchSha256"] == sha, "original CSP/CSM/physical identity")
            for field in ("sourceSha256","targetSha256","sourceLength","targetLength"):
                require(pair[field] == selection["candidates"][index-1][field],
                        "original file identity "+field)
            require(len(pair["samples"]) == len(tsv) == 12, "all original A/A/ON rows")
            for original_row, sample in zip(pair["samples"], tsv):
                stage = original_row["stage"] or {}
                actual = (
                    index, original_row["lane"], original_row["round"],
                    int(original_row["warmup"]), original_row["processCpuMilliseconds"],
                    original_row["wallMilliseconds"],
                    stage.get("plainEncodeTicks", ""), stage.get("dictionaryReadTicks", ""),
                    stage.get("dictionaryEncodeTicks", ""), stage.get("baseVerifyTicks", ""),
                    stage.get("targetEntries", ""), stage.get("dictionaryReads", ""),
                    stage.get("dictionaryEncodes", ""), stage.get("verifiedBaseRecords", ""),
                    original_row["patchSha256"],
                )
                require([str(x) for x in actual] == [sample[key] for key in FIELDS],
                        "TSV must equal ORIGINAL 24 samples")
                require(original_row["verified"] is True and
                        original_row["patchBytes"] == size and
                        original_row["patchSha256"] == sha, "per-sample SDK CSP/apply")
                if original_row["lane"] == "ON":
                    require(stage["stopwatchFrequency"] == 1000000000 and
                            stage["plainEncodes"] == stage["targetEntries"],
                            "clock and uncompressed-trial attribution")
            for field, value in (("medianOffACpuMs", med["cpu"]["OFF_A"]),
                                 ("medianOffBCpuMs", med["cpu"]["OFF_B"]),
                                 ("medianOnCpuMs", med["cpu"]["ON"]),
                                 ("offBOverOffACpu", med["offBOverOffACpu"]),
                                 ("onOverPooledOffCpu", med["onOverPooledOffCpu"])):
                require(near(pair[field], value), "independently recomputed "+field)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-zip", type=Path)
    args = parser.parse_args()
    rows = list(samples())
    medians = independent_medians(rows)
    if args.original_zip:
        original(args.original_zip, rows, medians)
    print("T3B2D0 independent original A/A/ON replay PASS:",
          json.dumps(medians, indent=2))
    print("ENCODE_STAGE_DOMINATES_OBSERVED_WORKER_TICKS / NO_PRODUCTION_ADOPT")


if __name__ == "__main__":
    main()
