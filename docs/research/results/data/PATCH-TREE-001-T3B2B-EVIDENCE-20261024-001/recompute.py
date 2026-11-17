#!/usr/bin/env python3
"""Independently replay original T3-B2B source locks, physical CSP / CPU admission.

Standard library only, no import of producer or SDK. Verifies ZIP SHA and
all archived members, locked source identities, independent median/cost gate,
paired lane order and preserved original B1 CSP SHA. Does not decode missing
raw source/CSP bytes or independently replay the original CPU time.
"""
import argparse
import hashlib
import json
import statistics
import struct
import zipfile
from pathlib import Path

ZIP_SHA = "9b0562b888952958b1e90ab1d2808f2c12e40d722628095b0127a54870375568"
SOURCE_SHA = "6c32a74f29ef179e4efbe8831c0618c9dd4bfd8a"
MEMBERS = {
    "original-pairs.json": "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "original-b1-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "original-b2a-selected.json": "1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b",
    "rehash-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "samples.json": "306944646d0b3a7fb60ed0b44296adb2d93869ee151ed2159f043589d9a4ccb5",
    "provenance.json": "0a5883bbc423eb0cdfee82d966160a7799b6380d2beec3b0e27f793fa194ae52",
}
VERSIONS = [("10.0.10", "10.0.11"), ("10.0.11", "10.0.12")]
EXPECTED = [
    dict(raw=1356632, t0s=548714, csp=86432, warm=86727, cold=87841, oldCsm=1114,
         t0sCspSha="3f8dfb52679c4d1d39f63cc0c7b5be5d43c3c2909cd3eb6d902e2540648a1208",
         t3CspSha="24a5579e96411f64c9b790e68d4ff01cbeb42f79500094553007fd1330e5dd29",
         baseSourceReadPerCreate=36133305),
    dict(raw=1356640, t0s=548863, csp=5649, warm=5944, cold=7058, oldCsm=1114,
         t0sCspSha="52ca62a90b0e88f5478866c48caee2659c7e9ce9a0f7b6d1e4550c9f46205674",
         t3CspSha="de7e1d02606c7663dac0aaaad86da4b4839552e58a6a00ee7cc99b6498c52103",
         baseSourceReadPerCreate=14522936),
]


def require(ok, message):
    if not ok:
        raise ValueError("T3B2B independent original audit: " + message)


def sha(buf):
    return hashlib.sha256(buf).hexdigest()


def layout(item):
    require(item["schema"] == "chunkshift.patch-tree-layout.v1" and
            item["order"] == "NFC UTF-8 byte order", "layout schema/order")
    h, offset, previous, files = hashlib.sha256(), 0, None, {}
    for row in item["files"]:
        name = row["path"].encode("utf-8")
        require(previous is None or previous < name, "canonical filename order")
        require(row["offset"] == offset and row["length"] >= 0, "contiguous offsets")
        require(len(row["sha256"]) == 64, "digest shape")
        h.update(struct.pack("<I", len(name)) + name +
                 struct.pack("<Q", row["length"]) + bytes.fromhex(row["sha256"]))
        files[row["path"]] = row
        previous = name
        offset += row["length"]
    require(h.hexdigest() == item["layoutSha256"] and
            offset == item["totalBytes"] and len(files) == item["fileCount"],
            "complete layout digest/size/count")
    return files


def map_record(src, target, base_manifest):
    blob = b"T3B1\x00" + struct.pack("<I", 1)
    for name in (target["targetPath"], src["sourcePath"]):
        b = name.encode("utf-8")
        blob += struct.pack("<I", len(b)) + b
    blob += struct.pack("<q", src["sourceLength"])
    blob += struct.pack("<q", target["targetLength"])
    blob += bytes.fromhex(src["sourceSha256"])
    blob += bytes.fromhex(target["targetSha256"])
    blob += bytes.fromhex(base_manifest)
    return blob


def replay(original):
    require(sha(original.read_bytes()) == ZIP_SHA, "GitHub original ZIP SHA")
    with zipfile.ZipFile(original) as z:
        require(z.testzip() is None, "ZIP CRC")
        require(set(z.namelist()) == set(MEMBERS) | {"SHA256SUMS"}, "archive membership")
        checksum = {}
        for line in z.read("SHA256SUMS").decode("ascii").splitlines():
            digest, name = line.split()
            require(name not in checksum, "duplicate checksum")
            checksum[name] = digest
        require(checksum == MEMBERS, "signed-by-artifact checksum manifest")
        for file, digest in MEMBERS.items():
            require(sha(z.read(file)) == digest, "archived content SHA " + file)
        pairs = json.loads(z.read("original-pairs.json"))
        inventory = json.loads(z.read("layouts.json"))
        pinned = json.loads(z.read("original-b1-selection.json"))
        b2a = json.loads(z.read("original-b2a-selected.json"))
        fresh = json.loads(z.read("rehash-selection.json"))
        results = json.loads(z.read("samples.json"))
        provenance = json.loads(z.read("provenance.json"))

        require(provenance["sourceSha"] == SOURCE_SHA and
                provenance["runId"] == "40078373627" and
                provenance["runAttempt"] == "1" and provenance["ref"] == "refs/heads/main" and
                provenance["runnerEnvironment"] == "github-hosted" and
                provenance["runnerOS"] == "Linux" and provenance["runnerArch"] == "X64" and
                provenance["originalPairsRunId"] == 38136336025 and
                provenance["originalB1RunId"] == 39967460136 and
                provenance["originalB2aRunId"] == 39977481690 and
                provenance["holdoutOpened"] is False and
                provenance["rawCspArchived"] is False, "original source and host provenance")
        require(pinned == fresh and
                pinned["schema"] == "chunkshift.patch-tree-t3b1-fixed-two.v1" and
                pinned["originalPairsSha256"] == MEMBERS["original-pairs.json"],
                "original B1 independent SHA-locked reselection")
        chosen = [x for p in b2a["pairs"] for x in p["candidates"]]
        require(len(chosen) == len(pinned["candidates"]) == 2 and
                b2a["totals"]["fallBackCount"] == 0, "locked original B2A source matching")
        original_pair_order = [
            ("dotnet-aspnetcore-win-x64", "10.0.10", "10.0.11"),
            ("dotnet-aspnetcore-win-x64", "10.0.11", "10.0.12"),
            ("dotnet-runtime-linux-arm64", "10.0.10", "10.0.11"),
            ("dotnet-runtime-linux-arm64", "10.0.11", "10.0.12"),
        ]
        fixed = [x for x in pairs["pairs"] if x["family"] in (
                 "dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64")]
        require(len(fixed) == len(inventory["pairs"]) == 4, "four original pairs")
        metadata = {}
        for key, source, item in zip(original_pair_order, fixed, inventory["pairs"]):
            require(key == (source["family"], source["base"], source["target"]) ==
                    (item["family"], item["baseVersion"], item["targetVersion"]),
                    "original pair ordering")
            old, new = layout(item["base"]), layout(item["target"])
            require(set(source["removed"]) == old.keys() - new.keys() and
                    {a["path"] for a in source["added"]} == new.keys() - old.keys(),
                    "original removed/added pair identity")
            metadata[key] = old, new

        require(results["schema"] == "chunkshift.patch-tree-t3b2b-repeat-create.v1" and
                results["status"] == "T3B2B_ORIGINAL_CALIBRATION_PERF_ONLY_NO_ADOPT" and
                results["originalPairsSha256"] == MEMBERS["original-pairs.json"] and
                results["warmupsPerLane"] == 1 and results["measuredRoundsPerLane"] == 5
                and len(results["pairs"]) == 2, "repeated source-locked result envelope")
        results_out = []
        for idx, (row, old_selection, b2_selection, expected) in enumerate(
                zip(results["pairs"], pinned["candidates"], chosen, EXPECTED)):
            a, b = VERSIONS[idx]
            key = ("dotnet-aspnetcore-win-x64", a, b)
            old, new = metadata[key]
            require((row["baseVersion"], row["targetVersion"]) == (a, b),
                    "per-pair version order")
            for part in ("sourcePath", "targetPath", "sourceSha256", "targetSha256"):
                require(row[part] == old_selection[part] == b2_selection[part],
                        "source identity " + part)
            require(old[row["sourcePath"]]["sha256"] == row["sourceSha256"] and
                    new[row["targetPath"]]["sha256"] == row["targetSha256"] and
                    new[row["targetPath"]]["length"] == expected["raw"], "layout original file identity")
            base_info = row["oldCsmBuild"]
            target_info = row["targetCsmBuild"]
            require(base_info["label"] == "BASE_CSM" and
                    target_info["label"] == "TARGET_CSM" and
                    base_info["sourceReadBytes"] == old_selection["sourceLength"] and
                    target_info["sourceReadBytes"] == expected["raw"] and
                    base_info["physicalCsmBytes"] == expected["oldCsm"],
                    "old/new CSM generation work/bytes")
            mapbytes = map_record(old_selection, old_selection, base_info["manifestId"])
            require(row["mapBytes"] == len(mapbytes) == 295 and
                    row["rawTargetBytes"] == expected["raw"] and
                    row["selfContainedT0SBytes"] == expected["t0s"] and
                    row["warmT3Bytes"] == expected["warm"] and
                    row["coldT3Bytes"] == expected["cold"] and
                    row["warmT3Bytes"] - row["mapBytes"] == expected["csp"] and
                    row["coldT3Bytes"] - row["warmT3Bytes"] == expected["oldCsm"],
                    "physical CSP/map/CSM ledger")
            samples = row["samples"]
            sequence = [("T0S", 0, True), ("T3", 0, True)]
            for iteration in range(5):
                order = ("T0S", "T3") if iteration % 2 == 0 else ("T3", "T0S")
                sequence += [(lane, iteration, False) for lane in order]
            require(len(samples) == len(sequence) == 12 and
                    [(s["lane"], s["round"], s["isWarmup"]) for s in samples] ==
                    sequence, "paired AB/BA chronology and no missing samples")
            lane = {}
            for name in ("T0S", "T3"):
                allrows = [s for s in samples if s["lane"] == name]
                measured = [s for s in allrows if not s["isWarmup"]]
                require(len(measured) == 5 and len(allrows) == 6, "lane round counts")
                expected_sha = expected["t3CspSha"] if name == "T3" else expected["t0sCspSha"]
                expected_size = expected["csp"] if name == "T3" else expected["t0s"]
                for s in allrows:
                    require(s["exactReconstruction"] is True and
                            s["cspSha256"] == expected_sha and
                            s["physicalCspBytes"] == expected_size and
                            s["targetReadBytes"] > 0 and
                            s["cpuMilliseconds"] >= 0 and
                            s["wallMilliseconds"] > 0 and
                            s["processAllocatedBytes"] >= 0 and
                            s["peakObservedWorkingSetBytes"] > 0 and
                            s["peakObservedPrivateCommittedBytes"] > 0 and
                            s["sourceReadBytes"] ==
                            (expected["baseSourceReadPerCreate"] if name == "T3" else 0),
                            "SDK produced CSP/hash/read/memory invariant")
                lane[name] = dict(
                    wall=statistics.median(s["wallMilliseconds"] for s in measured),
                    cpu=statistics.median(s["cpuMilliseconds"] for s in measured),
                    sourceReads=sum(s["sourceReadBytes"] for s in measured),
                    allocations=statistics.median(s["processAllocatedBytes"] for s in measured))
            require(lane["T0S"]["cpu"] > 0 and lane["T0S"]["wall"] > 0,
                    "positive T0S baseline denominators")
            t3_cpu, t3_wall = lane["T3"]["cpu"], lane["T3"]["wall"]
            t0s_cpu, t0s_wall = lane["T0S"]["cpu"], lane["T0S"]["wall"]
            cpu_ratio = t3_cpu / t0s_cpu
            wall_ratio = t3_wall / t0s_wall
            near = lambda x, y: abs(x - y) <= max(1e-9, 1e-10 * max(abs(x), abs(y)))
            for jsonfield, value in (
                ("t0SMedianCreateCpuMs", t0s_cpu), ("t3MedianCreateCpuMs", t3_cpu),
                ("t0SMedianCreateWallMs", t0s_wall), ("t3MedianCreateWallMs", t3_wall),
                ("cpuRatio", cpu_ratio), ("wallRatio", wall_ratio)
            ):
                require(near(row[jsonfield], value), "independent original median " + jsonfield)
            best = min(expected["raw"], expected["t0s"])
            size_eligible = expected["warm"] < 0.9 * best and expected["cold"] <= best
            decision = ("FALLBACK_T0_OR_T0S__SIZE_OR_CSM" if not size_eligible else
                        "FALLBACK_T0_OR_T0S__CREATE_BUDGET"
                        if cpu_ratio > 3 or wall_ratio > 3 else
                        "CALIBRATION_ONLY_PROMISING__NOT_PRODUCTION_AUTHORIZATION")
            require(row["admission"] == decision ==
                    "FALLBACK_T0_OR_T0S__CREATE_BUDGET", "independent fallback")
            results_out.append({
                "transition": a + " -> " + b,
                "t0sPhysicalBytes": expected["t0s"],
                "t3WarmPhysicalBytes": expected["warm"],
                "t3ColdPhysicalBytes": expected["cold"],
                "medianCpuRatio": cpu_ratio,
                "medianWallRatio": wall_ratio,
                "t3SourceStreamReadsFiveRounds": lane["T3"]["sourceReads"],
                "medianT0SGcAllocatedBytes": lane["T0S"]["allocations"],
                "medianT3GcAllocatedBytes": lane["T3"]["allocations"],
                "admission": decision,
            })
        return results_out


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("original_artifact_zip", type=Path)
    print("T3B2B ORIGINAL INDEPENDENT MEDIAN/BUDGET AUDIT PASS",
          json.dumps(replay(p.parse_args().original_artifact_zip), indent=2),
          "NO_PRODUCTION_ADOPT")


if __name__ == "__main__":
    main()
