#!/usr/bin/env python3
"""Independent structural verifier of PATCH-TREE-001 T0/T0S/T1 calibration.

Does not read any content file or holdout patch. It recomputes every reported
physical sum and compares file identities/population to locked original pairs.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

PAIRS_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd"
CALIBRATION = {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64"}
SCHEMA = "chunkshift.patch-tree-t0-t1-calibration.v1"
HEX64 = re.compile(r"[a-f0-9]{64}\Z")


def require(ok: bool, what: str) -> None:
    if not ok:
        raise ValueError("PATCH-TREE-001 independent verifier: " + what)


def amount(x: object, what: str) -> int:
    require(type(x) is int and x >= 0, "non-negative integer required: " + what)
    return x


def verify(doc: dict, original: dict) -> None:
    require(doc["schema"] == SCHEMA and doc["status"] == "CALIBRATION_ONLY_NOT_DECISION"
            and doc["pairsSha256"] == PAIRS_SHA256, "result envelope")
    require(original["schema"] == "chunkshift.patch-pairs.v1", "original pairs schema")
    expected = [p for p in original["pairs"] if p["family"] in CALIBRATION]
    require(len(expected) > 0 and len(doc["pairs"]) == len(expected),
            "complete original calibration pair population")
    for actual, base in zip(doc["pairs"], expected):
        identity = (base["family"], base["base"], base["target"])
        require((actual["family"], actual["baseVersion"],
                 actual["targetVersion"]) == identity,
                "frozen pair identity and order")
        require(actual["family"] in CALIBRATION, "held-out family was evaluated")
        require(HEX64.fullmatch(actual["oldTreeManifestId"]) is not None,
                "actual base CSM ManifestId must be a digest")
        for key in ("oldTreeCsmBytes", "baseLayoutBytes", "targetTreeManifestBytes",
                    "changedCount", "addedCount", "removedCount", "unchangedCount",
                    "t0PayloadBytes", "t1PayloadBytes", "t0WarmPhysicalBytes",
                    "t0SPhysicalBytes", "t1WarmPhysicalBytes", "t1ColdPhysicalBytes"):
            amount(actual[key], key)
        require(actual["oldTreeCsmBytes"] > 0 and actual["baseLayoutBytes"] > 0
                and actual["targetTreeManifestBytes"] > 0,
                "complete shared base/target metadata")
        changed = {x["path"]: x for x in base["changed"]}
        added = {x["path"]: x for x in base["added"]}
        require(actual["changedCount"] == len(changed)
                and actual["addedCount"] == len(added)
                and actual["removedCount"] == len(base["removed"])
                and actual["unchangedCount"] == base["identicalFiles"],
                "frozen pair path population")
        require(len(actual["files"]) == len(changed) + len(added),
                "missing or duplicate changed/added result")
        actual_paths = [f["path"] for f in actual["files"]]
        require(len(set(actual_paths)) == len(actual_paths), "duplicate evaluated path")
        require(actual_paths == sorted(actual_paths, key=lambda p: p.encode("utf-8")),
                "noncanonical file result order")
        s0 = s0s = s1 = 0
        for file in actual["files"]:
            require((file["family"], file["baseVersion"],
                     file["targetVersion"]) == identity,
                    "row pair identity")
            path = file["path"]
            expected_kind = "changed" if path in changed else "added" if path in added else None
            require(file["kind"] == expected_kind and expected_kind is not None,
                    "file did not belong to frozen changed/added set")
            frozen = changed[path] if expected_kind == "changed" else added[path]
            size = frozen["targetSize"] if expected_kind == "changed" else frozen["size"]
            digest = frozen["targetSha256"] if expected_kind == "changed" else frozen["sha256"]
            require(file["targetBytes"] == size and file["targetSha256"] == digest.lower(),
                    "target file identity")
            for k in ("t0Bytes", "t0SBytes", "t1Bytes", "t1BaseReads",
                      "t1BaseBytesRead", "t1BaseSeeks"):
                amount(file[k], k)
            for k in ("t0CreateSeconds", "t1CreateSeconds", "t0ApplySeconds",
                      "t1ApplySeconds"):
                value = file[k]
                require(type(value) in (int, float) and 0 <= value < 1e9,
                        "non-finite or invalid duration: " + k)
            require(HEX64.fullmatch(file["t1PatchSha256"]) is not None,
                    "T1 physical CSP checksum")
            require(file["t1Bytes"] > 0, "empty T1 CSP")
            if expected_kind == "changed":
                require(file["t0SBytes"] == file["t0Bytes"] > 0
                        and HEX64.fullmatch(file["t0PatchSha256"]) is not None,
                        "changed T0/T0S CSP must match")
            else:
                require(file["t0Bytes"] == size and file["t0SBytes"] > 0
                        and file["t0PatchSha256"] is None,
                        "added path raw T0 versus self-contained T0S")
            s0 += file["t0Bytes"]
            s0s += file["t0SBytes"]
            s1 += file["t1Bytes"]
        m = actual["targetTreeManifestBytes"]
        require(actual["t0PayloadBytes"] == s0 and actual["t1PayloadBytes"] == s1,
                "per-file physical byte totals")
        require(actual["t0WarmPhysicalBytes"] == s0 + m
                and actual["t0SPhysicalBytes"] == s0s + m
                and actual["t1WarmPhysicalBytes"] == s1 + m
                and actual["t1ColdPhysicalBytes"] ==
                    s1 + m + actual["oldTreeCsmBytes"] + actual["baseLayoutBytes"],
                "whole-update warm/cold accounting")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--result", required=True, type=Path)
    parser.add_argument("--pairs", required=True, type=Path)
    args = parser.parse_args()
    payload = args.pairs.read_bytes()
    require(hashlib.sha256(payload).hexdigest() == PAIRS_SHA256,
            "original frozen pairs SHA-256 mismatch")
    verify(json.loads(args.result.read_text(encoding="utf-8")),
           json.loads(payload))
    print("PATCH-TREE-001 calibration evidence STRUCTURAL PASS, no holdout/decision")


if __name__ == "__main__":
    main()
