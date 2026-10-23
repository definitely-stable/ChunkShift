#!/usr/bin/env python3
"""Independently recompute T3-B0 archived exact cross-path census.

Usage: python recompute.py patch-tree-t3b0-original-calibration.zip
Stdlib only. Does not download archives, reopen original trees, run CSP, or
import producer inventory/T3 source. Refuses any change to pinned evidence.
"""
import argparse
import hashlib
import json
import struct
import zipfile
from pathlib import Path

ARCHIVE_SHA256 = "a1dda5714080c2f3a17a6f0051f86725bc0804267cde1a61b3aadee051c176d9"
PAIRS_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
RESULT_SHA256 = "14a8c652daa65e63bcc6fc786ccfdf9d6ed21ba6d6a6cb44a55d6224f81478d8"
LAYOUT_SHA256 = "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a"
PROVENANCE_SHA256 = "b8030a0b00009cfc8de143d8e461ede266222c9b969ba543884ec1c2a3e9fc27"
SOURCE_SHA = "0301d21fa07fe9eec52bd645d73db2f4d55fd880"
EXPECTED_PAIRS = [
    ("dotnet-aspnetcore-win-x64", "10.0.10", "10.0.11"),
    ("dotnet-aspnetcore-win-x64", "10.0.11", "10.0.12"),
    ("dotnet-runtime-linux-arm64", "10.0.10", "10.0.11"),
    ("dotnet-runtime-linux-arm64", "10.0.11", "10.0.12"),
]
EXPECTED_SHA = {
    "original-pairs.json": PAIRS_SHA256,
    "layouts.json": LAYOUT_SHA256,
    "t3b0-census.json": RESULT_SHA256,
    "provenance.json": PROVENANCE_SHA256,
}


def checked(condition, reason):
    if not condition:
        raise ValueError("T3B0 independent replay: " + reason)


def hash_bytes(data):
    return hashlib.sha256(data).hexdigest()


def inspect_layout(layout):
    checked(layout["schema"] == "chunkshift.patch-tree-layout.v1", "layout schema")
    checked(layout["order"] == "NFC UTF-8 byte order", "layout order")
    checked(layout["fileCount"] == len(layout["files"]), "layout count")
    h = hashlib.sha256()
    offset = 0
    previous = None
    data = {}
    for e in layout["files"]:
        p = e["path"].encode("utf-8")
        checked(previous is None or previous < p, "non-increasing path order")
        checked(e["offset"] == offset and isinstance(e["length"], int)
                and e["length"] >= 0, "invalid virtual offset")
        checked(len(e["sha256"]) == 64, "invalid SHA shape")
        h.update(struct.pack("<I", len(p)))
        h.update(p)
        h.update(struct.pack("<Q", e["length"]))
        h.update(bytes.fromhex(e["sha256"]))
        offset += e["length"]
        previous = p
        data[e["path"]] = e
    checked(offset == layout["totalBytes"] and h.hexdigest() == layout["layoutSha256"],
            "layout identity mismatch")
    return data


def replay(path: Path):
    checked(hash_bytes(path.read_bytes()) == ARCHIVE_SHA256, "physical ZIP sha256")
    with zipfile.ZipFile(path) as archive:
        checked(archive.testzip() is None, "ZIP integrity")
        checked(set(archive.namelist()) == {"SHA256SUMS", *EXPECTED_SHA}, "ZIP members")
        checks = {}
        for line in archive.read("SHA256SUMS").decode("ascii").splitlines():
            sha, name = line.split()
            checked(name not in checks, "duplicate checksum")
            checks[name] = sha
        checked(checks == EXPECTED_SHA, "original internal checksum ledger")
        for name, value in EXPECTED_SHA.items():
            checked(hash_bytes(archive.read(name)) == value, "physical member: " + name)
        pairs = json.loads(archive.read("original-pairs.json"))
        inventory = json.loads(archive.read("layouts.json"))
        result = json.loads(archive.read("t3b0-census.json"))
        provenance = json.loads(archive.read("provenance.json"))
        checked(provenance["sourceSha"] == SOURCE_SHA and
                provenance["workflowRunId"] == "39915730705" and
                provenance["gitRef"] == "refs/heads/main" and
                provenance["runAttempt"] == "1" and
                provenance["runnerEnvironment"] == "github-hosted" and
                provenance["runnerOS"] == "Linux" and
                provenance["runnerArch"] == "X64" and
                provenance["sealedHoldoutOpened"] is False, "original host provenance")
        checked(inventory["pairsSha256"] == PAIRS_SHA256 and
                inventory["pairCount"] == len(EXPECTED_PAIRS) and
                result["originalPairsSha256"] == PAIRS_SHA256, "frozen pairs envelope")
        frozen = [p for p in pairs["pairs"] if p["family"] in (EXPECTED_PAIRS[0][0],
                                                               EXPECTED_PAIRS[2][0])]
        checked(len(frozen) == len(EXPECTED_PAIRS) ==
                len(inventory["pairs"]) == len(result["pairs"]), "population")
        totals = {k: 0 for k in ("addedCount", "removedCount", "changedCount",
                                 "unchangedCount", "exactCandidateCount",
                                 "addedRawBytes")}
        for required, pair, item, row in zip(EXPECTED_PAIRS, frozen,
                                              inventory["pairs"], result["pairs"]):
            identity = (pair["family"], pair["base"], pair["target"])
            checked(identity == required == (item["family"], item["baseVersion"],
                                              item["targetVersion"]) ==
                    (row["family"], row["baseVersion"], row["targetVersion"]),
                    "pair identity/order")
            a = inspect_layout(item["base"])
            b = inspect_layout(item["target"])
            newly_added = [b[p] for p in b.keys() - a.keys()]
            removed = a.keys() - b.keys()
            changed = [p for p in a.keys() & b.keys() if a[p]["sha256"] != b[p]["sha256"]]
            unchanged = [p for p in a.keys() & b.keys() if a[p]["sha256"] == b[p]["sha256"]]
            candidates = sum(any(x["length"] == entry["length"] and
                                 x["sha256"] == entry["sha256"] for x in a.values())
                             for entry in newly_added)
            n = dict(addedCount=len(newly_added), removedCount=len(removed),
                     changedCount=len(changed), unchangedCount=len(unchanged),
                     exactCandidateCount=candidates,
                     addedRawBytes=sum(x["length"] for x in newly_added))
            checked(n["addedCount"] == len(pair["added"]) and
                    n["removedCount"] == len(pair["removed"]) and
                    n["changedCount"] == len(pair["changed"]) and
                    n["unchangedCount"] == pair["identicalFiles"] and
                    set(removed) == set(pair["removed"]), "frozen original pair classification")
            checked(all(n[k] == row[k] for k in n) and
                    row["mappedCount"] == row["mapPhysicalBytes"] ==
                    row["netSavedBytes"] == row["mappedContentBytes"] == 0,
                    "reported counts or exact map")
            for k, value in n.items():
                totals[k] += value
        checked(totals == dict(addedCount=2, removedCount=2, changedCount=1049,
                               unchangedCount=13, exactCandidateCount=0,
                               addedRawBytes=2713272), "complete original four-pair ledger")
        checked(result["totals"]["netSavedBytes"] == 0 and
                result["totals"]["exactCandidateCount"] == 0 and
                result["baselineT0WarmPhysicalBytes"] ==
                result["modelledT3WarmCostBytes"] == 14758982, "net cost")
        return totals


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifact_zip", type=Path)
    args = parser.parse_args()
    print("PATCH-TREE-001 T3B0 INDEPENDENT REPLAY PASS", json.dumps(replay(args.artifact_zip),
          sort_keys=True), "EXACT_ADDED_PATH_NO_HEADROOM / NO_ADOPT")


if __name__ == "__main__":
    main()
