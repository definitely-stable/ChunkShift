#!/usr/bin/env python3
"""PATCH-TREE-001 T3-B1: source-locked, fixed two-transition changed-rename selector.

Only original .NET calibration. No H5/candidate search, holdout or patch.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

import patch_tree_001_t3b0_census as b0

SCHEMA = "chunkshift.patch-tree-t3b1-fixed-two.v1"
NAME = re.compile(r"mscordaccore_amd64_amd64_(10\.0\.\d+26\.\d+)\.dll\Z")
FAMILY = "dotnet-aspnetcore-win-x64"


def require(ok: bool, why: str):
    if not ok:
        raise ValueError("T3B1 selector: " + why)


def select(root: Path, inventory: dict, pairs_raw: bytes) -> dict:
    # Retain all B0 original-source and real-byte rehash preconditions.
    f1 = json.loads(b0.F1.read_text(encoding="utf-8"))
    b0.input_lock(root, inventory, pairs_raw, f1)
    frozen = [p for p in json.loads(pairs_raw)["pairs"]
              if p["family"] in b0.EXPECTED_FAMILIES]
    candidates = []
    for item, p in zip(inventory["pairs"], frozen):
        if p["family"] != FAMILY:
            require(not p["added"] and not p["removed"],
                    "non-ASP.NET pair gained or lost paths")
            continue
        require(len(p["added"]) == len(p["removed"]) == 1,
                "original pair must contain precisely one renamed path")
        target = p["added"][0]
        oldpath = p["removed"][0]
        targetpath = target["path"]
        require(oldpath.rpartition("/")[0] == targetpath.rpartition("/")[0],
                "renamed file parent must match")
        old_name, new_name = oldpath.rsplit("/", 1)[1], targetpath.rsplit("/", 1)[1]
        old_match, new_match = NAME.fullmatch(old_name), NAME.fullmatch(new_name)
        require(old_match is not None and new_match is not None,
                "must be the originally frozen mscordaccore version-renamed files")
        base = {e["path"]: e for e in item["base"]["files"]}
        next_files = {e["path"]: e for e in item["target"]["files"]}
        require(oldpath in base and targetpath in next_files and
                base[oldpath]["sha256"] != next_files[targetpath]["sha256"] and
                target["sha256"] == next_files[targetpath]["sha256"] and
                target["size"] == next_files[targetpath]["length"],
                "actual changed-rename identities differ")
        candidates.append(dict(
            family=p["family"], baseVersion=p["base"], targetVersion=p["target"],
            sourcePath=oldpath, sourceSha256=base[oldpath]["sha256"],
            sourceLength=base[oldpath]["length"], targetPath=targetpath,
            targetSha256=target["sha256"], targetLength=target["size"]))
    require(len(candidates) == 2 and
            [(x["baseVersion"], x["targetVersion"]) for x in candidates] ==
            [("10.0.10", "10.0.11"), ("10.0.11", "10.0.12")],
            "fixed original transition population")
    return dict(schema=SCHEMA, status="SOURCE_LOCKED_FIXED_PAIR_SELECTION_ONLY",
                originalPairsSha256=b0.EXPECTED_PAIRS_SHA256,
                candidates=candidates)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    rows = select(args.corpus,
                  json.loads(args.inventory.read_text(encoding="utf-8")),
                  (args.corpus / "pairs.json").read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(rows, indent=2, sort_keys=True) + "\n",
                           encoding="utf-8")
    print("T3B1 original two renamed DLL candidates pinned; no CSP until .NET pilot.")


if __name__ == "__main__":
    main()
