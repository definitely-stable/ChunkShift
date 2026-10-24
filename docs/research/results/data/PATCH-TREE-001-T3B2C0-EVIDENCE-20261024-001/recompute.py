#!/usr/bin/env python3
"""Independent replay of SHA-locked original T3-B2C0 stream topology ledger.

No producer/SDK imports, source binaries, patch data or network dependencies.
Audits archived original provenance/CSM/CSP identity, full tree layout SHA256,
paired AB/BA chronology, interval-union aggregate conservation and medians.
NOTE: raw read offset event lists are NOT archived; exact union itself was
calculated and asserted on the source-locked hosted producer, not independently
reconstructed from individual offsets by this archive replay.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import statistics
import struct
import zipfile
from pathlib import Path

ZIP_SHA256 = "e590c8c571c6982e99e2fa456c304bfe0287b76d8e03215fc300559eb60b632d"
SOURCE_SHA = "e0a8d5164f8c3e4d9d300c34b6158fa9ff6c3a4b"
FILES = {
    "original-pairs.json": "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "original-b1-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "original-b2a-selected.json": "1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b",
    "original-b1-physical.json": "c8a9de0599101e15834a9cd3026214f5176d71b251e79d1d0a6b162dbb4120cd",
    "rehash-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "topology.json": "edfb0b903863558aab63e1cb57a6797cef0ee5a74d08d3a85aae112b2265136f",
    "provenance.json": "a35edbc96d10a6cb731a319ed75b2d808e4f61c6e0544ab3409db2a511103925",
}
PAIR_KEYS = [
    ("dotnet-aspnetcore-win-x64", "10.0.10", "10.0.11"),
    ("dotnet-aspnetcore-win-x64", "10.0.11", "10.0.12"),
    ("dotnet-runtime-linux-arm64", "10.0.10", "10.0.11"),
    ("dotnet-runtime-linux-arm64", "10.0.11", "10.0.12"),
]
EXPECTED_READS = [
    (36133305, 1356632, 34776673, 516),
    (14522936, 1356632, 13166304, 207),
]


def require(condition, message):
    if not condition:
        raise ValueError("T3B2C0 original independent replay: " + message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def layout(side):
    require(side["schema"] == "chunkshift.patch-tree-layout.v1", "layout schema")
    require(side["order"] == "NFC UTF-8 byte order", "layout byte order")
    previous = None
    h = hashlib.sha256()
    offset = 0
    files = {}
    for item in side["files"]:
        path = item["path"].encode("utf-8")
        require(previous is None or previous < path, "path sorted identity")
        require(item["offset"] == offset and item["length"] >= 0, "file offset")
        require(len(item["sha256"]) == 64, "file sha shape")
        h.update(struct.pack("<I", len(path)) + path
                 + struct.pack("<Q", item["length"])
                 + bytes.fromhex(item["sha256"]))
        files[item["path"]] = item
        offset += item["length"]
        previous = path
    require(h.hexdigest() == side["layoutSha256"] and
            offset == side["totalBytes"] and
            len(files) == side["fileCount"], "complete layout hash/count")
    return files


def replay(path):
    require(sha(path.read_bytes()) == ZIP_SHA256, "original GitHub ZIP SHA")
    with zipfile.ZipFile(path) as z:
        require(z.testzip() is None, "ZIP CRC")
        require(set(z.namelist()) == set(FILES) | {"SHA256SUMS"}, "ZIP membership")
        checks = {}
        for line in z.read("SHA256SUMS").decode("ascii").splitlines():
            digest, name = line.split()
            require(name not in checks, "duplicate checksum")
            checks[name] = digest
        require(checks == FILES, "member SHA manifest")
        for name, digest in FILES.items():
            require(sha(z.read(name)) == digest, "member SHA " + name)
        original = json.loads(z.read("original-pairs.json"))
        inventory = json.loads(z.read("layouts.json"))
        b1select = json.loads(z.read("original-b1-selection.json"))
        b2select = json.loads(z.read("original-b2a-selected.json"))
        rehash = json.loads(z.read("rehash-selection.json"))
        b1physical = json.loads(z.read("original-b1-physical.json"))
        report = json.loads(z.read("topology.json"))
        provenance = json.loads(z.read("provenance.json"))

        require(provenance["schema"] == "chunkshift.patch-tree-t3b2c0-original-provenance.v1"
                and provenance["runId"] == "40096807766"
                and provenance["runAttempt"] == "1"
                and provenance["sourceSha"] == SOURCE_SHA
                and provenance["ref"] == "refs/heads/main"
                and provenance["runnerEnvironment"] == "github-hosted"
                and provenance["runnerOS"] == "Linux"
                and provenance["runnerArch"] == "X64"
                and provenance["originalPairsRunId"] == 38136336025
                and provenance["originalB1RunId"] == 39967460136
                and provenance["originalB2aRunId"] == 39977481690
                and provenance["sealedHoldoutOpened"] is False
                and provenance["sourceContentArchived"] is False
                and provenance["rawCspArchived"] is False, "original source lock")
        require(b1select == rehash and
                b1select["originalPairsSha256"] == FILES["original-pairs.json"]
                and report["originalPairsSha256"] == FILES["original-pairs.json"],
                "frozen pairs and independently rehashed selector")
        b2cands = [x for pair in b2select["pairs"] for x in pair["candidates"]]
        require(len(b1select["candidates"]) == len(b2cands) == len(report["pairs"]) == 2,
                "fixed source pair count")
        require(report["schema"] == "chunkshift.patch-tree-t3b2c0-read-topology.v1"
                and report["status"] == "PASSIVE_BASE_STREAM_ATTRIBUTION_ONLY_NO_ADOPT"
                and report["warmupsPerMode"] == 1
                and report["measuredRoundsPerMode"] == 3,
                "result envelope")
        frozen = [x for x in original["pairs"] if
                  x["family"] in ("dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64")]
        require(len(frozen) == len(inventory["pairs"]) == 4, "four original pairs")
        layouts = {}
        for key, f, inv in zip(PAIR_KEYS, frozen, inventory["pairs"]):
            require(key == (f["family"], f["base"], f["target"]) ==
                    (inv["family"], inv["baseVersion"], inv["targetVersion"]),
                    "exact original pair identity")
            old, new = layout(inv["base"]), layout(inv["target"])
            require(old.keys() - new.keys() == set(f["removed"]) and
                    new.keys() - old.keys() == {a["path"] for a in f["added"]},
                    "original path population")
            layouts[key] = (old, new)

        result = []
        for i, (pair, source, selected, b1) in enumerate(zip(
                report["pairs"], b1select["candidates"], b2cands, b1physical["rows"])):
            key = PAIR_KEYS[i]
            old, new = layouts[key]
            require((pair["family"], pair["baseVersion"], pair["targetVersion"]) == key,
                    "pair output identity")
            for field in ("sourcePath", "targetPath", "sourceSha256", "targetSha256"):
                require(pair[field] == source[field] == selected[field] == b1[field],
                        "old/new fixed source SHA/path " + field)
            require(old[pair["sourcePath"]]["sha256"] == source["sourceSha256"]
                    and new[pair["targetPath"]]["sha256"] == source["targetSha256"]
                    and pair["oldSourceLength"] == old[pair["sourcePath"]]["length"]
                    and pair["newTargetLength"] == new[pair["targetPath"]]["length"],
                    "rehash-locked original source/target layout")
            require(pair["physicalCspSha256"] == b1["t3CspSha256"]
                    and pair["physicalCspBytes"] == b1["t3CspPhysicalBytes"]
                    and pair["baseCsmBytes"] == b1["sourceCsmBytes"]
                    and pair["diagnosticLocatorBytes"] == b1["mapPhysicalBytes"],
                    "original B1 output unchanged")

            sequence = [("counter-only", 0, True), ("trace", 0, True)]
            for n in range(3):
                order = ("counter-only", "trace") if n % 2 == 0 else ("trace", "counter-only")
                sequence += [(mode, n, False) for mode in order]
            samples = pair["samples"]
            require([(s["mode"], s["round"], s["warmup"]) for s in samples] == sequence,
                    "complete counterbalanced chronology")
            expected_bytes, unique, repeated, calls = EXPECTED_READS[i]
            control, traced = [], []
            for sample in samples:
                t = sample["readTopology"]
                require(sample["exactApply"] is True
                        and sample["cspSha256"] == b1["t3CspSha256"]
                        and sample["cspBytes"] == b1["t3CspPhysicalBytes"]
                        and sample["baseManifestId"] == b1["baseManifestId"]
                        and sample["targetManifestId"] == b1["targetManifestId"],
                        "each public SDK CSP and apply identity")
                require(t["sourceFileLength"] == unique and
                        t["logicalReadBytes"] == expected_bytes and
                        t["readCalls"] == calls and
                        t["positionAssignments"] == calls and
                        t["seekCalls"] == 0 and
                        sample["createWallMilliseconds"] > 0 and
                        sample["createCpuMilliseconds"] >= 0 and
                        sample["allocatedBytes"] >= 0,
                        "record, read/seek and cost ledger")
                if sample["mode"] == "trace":
                    require(t["complete"] is True
                            and t["uniqueReadBytes"] == unique
                            and t["repeatedReadBytes"] == repeated
                            and t["uniqueReadBytes"] + t["repeatedReadBytes"] ==
                                t["logicalReadBytes"]
                            and t["observedReadEventCount"] == calls
                            and 0 < t["backwardReadTransitions"] <=
                                t["noncontiguousReadTransitions"] <= calls,
                            "trace bounds and coverage conservation")
                    traced.append(sample)
                else:
                    require(t["complete"] is False
                            and t["observedReadEventCount"] == 0,
                            "counter-only must not claim distinct coverage")
                    control.append(sample)
            c = [x for x in control if not x["warmup"]]
            t = [x for x in traced if not x["warmup"]]
            require(len(c) == len(t) == 3, "measured paired samples")
            median_c = statistics.median(x["createWallMilliseconds"] for x in c)
            median_t = statistics.median(x["createWallMilliseconds"] for x in t)
            median_ccpu = statistics.median(x["createCpuMilliseconds"] for x in c)
            median_tcpu = statistics.median(x["createCpuMilliseconds"] for x in t)
            near = lambda a, b: abs(a - b) <= max(1e-8, 1e-9 * max(abs(a), abs(b)))
            for name, value in [
                ("medianCounterOnlyWallMs", median_c),
                ("medianTraceWallMs", median_t),
                ("medianCounterOnlyCpuMs", median_ccpu),
                ("medianTraceCpuMs", median_tcpu),
                ("traceToCounterWallRatio", median_t / median_c),
            ]:
                require(near(pair[name], value), "independent median " + name)
            result.append(dict(version=f"{key[1]} -> {key[2]}",
                               bytesRead=expected_bytes,
                               uniqueBytes=unique, repeatedBytes=repeated,
                               repeatedFraction=round(repeated / expected_bytes, 6),
                               readCalls=calls,
                               traceVsCounterWallRatio=round(median_t / median_c, 4)))
        return result


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("original_zip", type=Path)
    print("T3B2C0 ORIGINAL INDEPENDENT ARCHIVE AND MEDIAN AUDIT PASS",
          json.dumps(replay(p.parse_args().original_zip), sort_keys=True),
          "NO_PRODUCTION_ADOPT")
