#!/usr/bin/env python3
"""Replay PATCH-DOTNET-001 D3 collector evidence from the archived ZIP.

Never downloads unpinned binaries, never consults evaluation file/patch sizes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path

import patch_dotnet_001_d3_oracle as oracle

EXPECTED = {
    "d0-physical.json", "d3-scanner.json",
    "d3-oracle-input.json", "d3-ceiling.json",
    "SHA256SUMS",
}


def audit(blob: Path, pairs_path: Path) -> dict:
    pairs = oracle.load_locked_pairs(pairs_path)
    with zipfile.ZipFile(blob) as archive:
        paths = {k.filename for k in archive.infolist() if not k.is_dir()}
        oracle.demand(paths == EXPECTED and len(archive.infolist()) == 5,
                      "COLLECTOR_ZIP_MEMBER_SET")
        raw = {name: archive.read(name) for name in EXPECTED}
    sha = hashlib.sha256(blob.read_bytes()).hexdigest()
    ledger = raw["SHA256SUMS"].decode("ascii").splitlines()
    found = {}
    for line in ledger:
        digest, separator, name = line.partition("  ")
        # sha256sum is invoked from the repository root, but upload-artifact
        # flattens the common output directory into the ZIP root.
        prefix = "artifacts/patch-dotnet-d3/"
        if name.startswith(prefix):
            name = name[len(prefix):]
        oracle.demand(separator and name in EXPECTED - {"SHA256SUMS"} and
                      name not in found and
                      len(digest) == 64 and all(c in "0123456789abcdef" for c in digest),
                      "COLLECTOR_LEDGER_MALFORMED")
        found[name] = digest
    oracle.demand(set(found) == EXPECTED - {"SHA256SUMS"}, "COLLECTOR_LEDGER_MEMBERSHIP")
    for name, expected in found.items():
        oracle.demand(oracle.hash_bytes(raw[name]) == expected, "COLLECTOR_LEDGER_DIGEST_DRIFT")
    baseline = json.loads(raw["d0-physical.json"])
    scanner = json.loads(raw["d3-scanner.json"])
    inputs = json.loads(raw["d3-oracle-input.json"])
    saved = json.loads(raw["d3-ceiling.json"])
    oracle.demand(
        inputs["baselineEvidence"]["sha256"] == oracle.hash_bytes(raw["d0-physical.json"]) and
        inputs["scannerEvidence"]["sha256"] == oracle.hash_bytes(raw["d3-scanner.json"]),
        "COLLECTOR_INPUT_NOT_AUTHENTIC_SAME_RUN")
    expected = oracle.evaluate(inputs, pairs)
    oracle.demand(expected == saved, "COLLECTOR_ORACLE_RECOMPUTE_DRIFT")
    oracle.demand(len(baseline.get("files", [])) == 1242 and
                  len(scanner.get("rows", [])) == 1242 and
                  scanner.get("verifiedFiles") == expected["scannerVerifiedRows"],
                  "COLLECTOR_H0_SCANNER_ROW_COUNT")
    oracle.demand(sum(x["patchBytes"] for x in baseline["files"]) ==
                  expected["d0PhysicalBytes"], "COLLECTOR_H0_PHYSICAL_SUM")
    return {"artifactZipSha256": sha, "files": 1242,
            "scannerVerifiedRows": expected["scannerVerifiedRows"],
            "baselinePhysicalBytes": expected["d0PhysicalBytes"],
            "idealUpperBoundFraction": expected["upperBoundSavingFraction"],
            "status": "INDEPENDENT_CALIBRATION_CEILING_REPLAY_NOT_HOLDOUT_VERDICT"}


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--artifact-zip", type=Path, required=True)
    p.add_argument("--pairs", type=Path, required=True)
    args = p.parse_args()
    try:
        print(json.dumps(audit(args.artifact_zip, args.pairs), sort_keys=True, indent=2))
    except (oracle.OracleError, OSError, ValueError, KeyError, UnicodeError,
            zipfile.BadZipFile, json.JSONDecodeError) as e:
        p.error(str(e))


if __name__ == "__main__":
    main()
