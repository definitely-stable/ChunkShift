#!/usr/bin/env python3
"""Independent T3-B2A original archived source-selector replay.

Standard-library only, no import of the producer. Verifies artifact identity,
all canonical layout hashes, original pair membership, independent conservative
renamed-DLL matching and the separate B1 positive-control selection.
Does NOT rehash original binaries (performed on original hosted run).
"""
import argparse
import collections
import hashlib
import json
import re
import struct
import zipfile
from pathlib import Path

ARCHIVE_SHA = "7270b0425f923204c7f9021d6e9df1194d32fc36a94f64607ce7ecfefdc3971b"
MEMBERS = {
    "original-pairs.json": "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "original-b1-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "t3b2a-selected.json": "1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b",
    "provenance.json": "080f0a53f5be82151b59d649717bfdaa234fab1a8389df5d16bf3b11fcedc926",
}
SOURCE_SHA = "1282fd6f1d01384f2858acf1192ab0f3600ec4f8"
FAMILIES = ("dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64")
PAIRS = [(f, x, y) for f in FAMILIES
         for x, y in (("10.0.10", "10.0.11"), ("10.0.11", "10.0.12"))]
PAT = re.compile(r"([A-Za-z0-9][A-Za-z0-9_.+-]{0,127}_)([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\.dll\Z", re.ASCII)
MIN_BYTES, MAX_BYTES, MAX_POOL = 65_536, 64 * 1024 * 1024, 8


def require(ok, message):
    if not ok:
        raise ValueError("T3B2A independent replay: " + message)


def sha(blob):
    return hashlib.sha256(blob).hexdigest()


def layout(side):
    require(side["schema"] == "chunkshift.patch-tree-layout.v1", "layout schema")
    require(side["order"] == "NFC UTF-8 byte order", "layout sort")
    prev = b""
    h = hashlib.sha256()
    offset = 0
    ret = {}
    for f in side["files"]:
        name = f["path"].encode("utf-8")
        require(name > prev and f["offset"] == offset and f["length"] >= 0, "layout offset/order")
        require(len(f["sha256"]) == 64, "digest")
        h.update(struct.pack("<I", len(name)) + name +
                 struct.pack("<Q", f["length"]) + bytes.fromhex(f["sha256"]))
        ret[f["path"]] = f
        prev = name
        offset += f["length"]
    require(len(ret) == side["fileCount"] and
            offset == side["totalBytes"] and h.hexdigest() == side["layoutSha256"],
            "side layout identity")
    return ret


def signature(path):
    parent, _, name = path.rpartition("/")
    m = PAT.fullmatch(name)
    if m is None:
        return None
    version = tuple(int(p) for p in m[2].split("."))
    if any(p > 2_147_483_647 for p in version):
        return None
    return parent, m[1], version


def candidate_rows(old, target, removed, added):
    index = collections.defaultdict(list)
    for path in removed:
        sig = signature(path)
        if sig is not None and MIN_BYTES <= old[path]["length"] <= MAX_BYTES:
            index[(sig[0], sig[1])].append((path, sig[2]))
    chosen, fallback = {}, {}
    claimed = collections.defaultdict(list)
    for dst in sorted(added, key=lambda x: x.encode("utf-8")):
        sig = signature(dst)
        target_file = target[dst]
        if sig is None:
            fallback[dst] = "nonversioned_filename"
            continue
        if not MIN_BYTES <= target_file["length"] <= MAX_BYTES:
            fallback[dst] = "target_size_out_of_bounds"
            continue
        bucket = index.get((sig[0], sig[1]), ())
        if len(bucket) > MAX_POOL:
            fallback[dst] = "source_pool_limit"
            continue
        eligible = [path for path, version in bucket
                    if version < sig[2] and old[path]["sha256"] != target_file["sha256"]
                    and max(old[path]["length"], target_file["length"]) <=
                    2 * min(old[path]["length"], target_file["length"])]
        if len(eligible) != 1:
            fallback[dst] = "ambiguous_source_candidates" if eligible else "no_unique_structural_source"
            continue
        chosen[dst] = eligible[0]
        claimed[eligible[0]].append(dst)
    for source, targets in claimed.items():
        if len(targets) > 1:
            for dst in targets:
                chosen.pop(dst)
                fallback[dst] = "source_claimed_by_multiple_targets"
    return chosen, fallback


def replay(path):
    require(sha(path.read_bytes()) == ARCHIVE_SHA, "ZIP digest")
    with zipfile.ZipFile(path) as z:
        require(z.testzip() is None, "ZIP CRC")
        require(set(z.namelist()) == set(MEMBERS) | {"SHA256SUMS"}, "ZIP entries")
        checks = {}
        for line in z.read("SHA256SUMS").decode("ascii").splitlines():
            digest, name = line.split()
            require(name not in checks, "duplicate digest row")
            checks[name] = digest
        require(checks == MEMBERS, "original member manifest")
        for n, checksum in MEMBERS.items():
            require(sha(z.read(n)) == checksum, "independent SHA " + n)
        original = json.loads(z.read("original-pairs.json"))
        control = json.loads(z.read("original-b1-selection.json"))
        inv = json.loads(z.read("layouts.json"))
        output = json.loads(z.read("t3b2a-selected.json"))
        provenance = json.loads(z.read("provenance.json"))

        require(provenance["sourceSha"] == SOURCE_SHA and
                provenance["runId"] == "39977481690" and
                provenance["runAttempt"] == "1" and
                provenance["gitRef"] == "refs/heads/main" and
                provenance["runnerEnvironment"] == "github-hosted" and
                provenance["runnerOS"] == "Linux" and
                provenance["runnerArch"] == "X64" and
                provenance["sealedHoldoutOpened"] is False and
                provenance["cspCreated"] is False and
                provenance["originalB1RunId"] == 39967460136,
                "pinned host provenance")
        require(output["schema"] == "chunkshift.patch-tree-t3b2a-original-calibration.v1" and
                output["status"] == "ORIGINAL_CALIBRATION_CANDIDATE_PRESELECTION_ONLY_NO_ADOPT" and
                output["originalPairsSha256"] == MEMBERS["original-pairs.json"],
                "result envelope")
        fixed = [p for p in original["pairs"] if p["family"] in FAMILIES]
        require(len(fixed) == len(PAIRS) == len(inv["pairs"]) == len(output["pairs"]),
                "population not exactly four originals")
        all_selected = []
        stats = collections.Counter()
        for identity, p, item, row in zip(PAIRS, fixed, inv["pairs"], output["pairs"]):
            key = (p["family"], p["base"], p["target"])
            require(key == identity ==
                    (item["family"], item["baseVersion"], item["targetVersion"]) ==
                    (row["family"], row["baseVersion"], row["targetVersion"]),
                    "original pair identity/order")
            old = layout(item["base"])
            new = layout(item["target"])
            removed = sorted(old.keys() - new.keys())
            added = sorted(new.keys() - old.keys())
            require(removed == sorted(p["removed"]) and
                    [x["path"] for x in p["added"]] == added, "original changed paths")
            selected, fallback = candidate_rows(old, new, removed, added)
            require(row["removedCount"] == len(removed) and
                    row["addedCount"] == len(added) and
                    row["selectedCount"] == len(selected) and
                    row["fallBackCount"] == len(fallback), "result counts")
            actual = {x["targetPath"]: x for x in row["candidates"]}
            require(set(actual) == set(selected), "selected target identity")
            for dst, src in selected.items():
                row_candidate = actual[dst]
                src_meta, dst_meta = old[src], new[dst]
                expected_locator = (5 + 4 + 4 + len(dst.encode("utf-8")) +
                                    4 + len(src.encode("utf-8")) + 16 + 32 * 3)
                require((row_candidate["sourcePath"], row_candidate["sourceSha256"],
                         row_candidate["sourceLength"], row_candidate["targetSha256"],
                         row_candidate["targetLength"],
                         row_candidate["diagnosticLocatorFloorBytes"]) ==
                        (src, src_meta["sha256"], src_meta["length"],
                         dst_meta["sha256"], dst_meta["length"], expected_locator),
                        "independent source SHA/locator cost")
                all_selected.append(row_candidate)
                stats["selectedRawTargetBytes"] += dst_meta["length"]
                stats["diagnosticLocatorFloorBytes"] += expected_locator
            require({x["targetPath"]: x["reason"] for x in row["fallbackTargets"]} ==
                    fallback, "fallback reasons")
            stats["addedCount"] += len(added)
            stats["removedCount"] += len(removed)
            stats["selectedCount"] += len(selected)
            stats["fallBackCount"] += len(fallback)
        require(dict(stats) == output["totals"] ==
                dict(addedCount=2, removedCount=2, selectedCount=2,
                     fallBackCount=0, selectedRawTargetBytes=2713272,
                     diagnosticLocatorFloorBytes=590),
                "complete census conservation")
        require(len(control["candidates"]) == 2, "fixed B1 oracle population")
        for source, prior in zip(all_selected, control["candidates"]):
            for key in ("sourcePath", "targetPath", "sourceSha256", "targetSha256",
                        "sourceLength", "targetLength"):
                require(source[key] == prior[key], "independent B1 original source comparison")
        return dict(stats)


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("archived_original_zip", type=Path)
    print("T3B2A ORIGINAL INDEPENDENT REPLAY PASS", replay(p.parse_args().archived_original_zip),
          "CALIBRATION_ONLY_NO_PRODUCTION_ADOPT")
