#!/usr/bin/env python3
"""Independent archived-observation audit for PATCH-TREE-001/T2-A1.

Pure stdlib. Verifies original GitHub artifact ZIP, member hashes, prior
calibration and all retained row fields. Does NOT reconstruct raw old/new CSM
or rerun ManifestReader: raw original source/CSM intentionally not archived.
"""
import argparse
import csv
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path

ORIGINAL_ZIP_SHA = "9e5556b11277e33c84d186834ca68da6866d3bc60e77fceef92c78fb7b077fe9"
ORIGINAL_RUN_ID = "40204580308"
ORIGINAL_SHA = "555c992f30473c373f1efd0d6d8b3d18bf87a090"
PAIRS_SHA = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
DIGESTS = {
    "original-pairs.json": PAIRS_SHA,
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "prior-t0-calibration.json": "d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c",
    "t2a1-preflight.json": "823820571d3534b302e2acb8f0501ac4dbe4c6030093e6e27b3a066945d65fb6",
    "provenance.json": "3374d0689b8efe8119fbc6f32c20227516d3c0ce6844ccb6ab419d99f92ea960",
}
COLUMNS = (
    "family baseVersion targetVersion baseLayoutSha256 targetLayoutSha256 "
    "baseManifestId targetManifestId baseCsmSha256 targetCsmSha256 "
    "baseFiles targetFiles baseBytes targetBytes baseChunks targetChunks "
    "baseDistinct targetDistinct missingIds baseCsmBytes targetCsmBytes "
    "priorT0Warm metadataPlanningBytes changed added removed unchanged"
).split()
FAMILIES = (
    ("dotnet-aspnetcore-win-x64", "10.0.10", "10.0.11", 339, 5748069),
    ("dotnet-aspnetcore-win-x64", "10.0.11", "10.0.12", 339, 4409941),
    ("dotnet-runtime-linux-arm64", "10.0.10", "10.0.11", 193, 2798481),
    ("dotnet-runtime-linux-arm64", "10.0.11", "10.0.12", 193, 1802491),
)


def require(value, message):
    if not value:
        raise ValueError("T2-A1 independent replay: " + message)


def read_rows():
    with Path(__file__).with_name("census.tsv").open(encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source, delimiter="\t")
        require(reader.fieldnames == COLUMNS, "original TSV schema changed")
        rows = list(reader)
    require(len(rows) == 4, "exact four original rows")
    for idx, row in enumerate(rows):
        family, base, target, files, warm = FAMILIES[idx]
        require((row["family"], row["baseVersion"], row["targetVersion"]) ==
                (family, base, target), "source-locked original pair/version ordering")
        require(int(row["priorT0Warm"]) == warm, "prior T0 warm original admission")
        require(int(row["baseFiles"]) == int(row["targetFiles"]) == files,
                "pinned original file populations")
        require(int(row["changed"]) + int(row["added"]) +
                int(row["unchanged"]) == files and
                int(row["changed"]) + int(row["removed"]) +
                int(row["unchanged"]) == files, "complete path conservation")
        for key in ("baseLayoutSha256", "targetLayoutSha256", "baseManifestId",
                    "targetManifestId", "baseCsmSha256", "targetCsmSha256"):
            require(re.fullmatch("[0-9a-f]{64}", row[key]) is not None,
                    "malformed pinned hash " + key)
        for side in ("base", "target"):
            require(0 < int(row[side + "Bytes"]) <= 160 * 1024 * 1024 and
                    0 < int(row[side + "Chunks"]) <= 262144 and
                    0 < int(row[side + "Distinct"]) <= int(row[side + "Chunks"]) and
                    0 < int(row[side + "CsmBytes"]) <= 8 * 1024 * 1024,
                    "fixed CSM and source resource limits")
        require(0 <= int(row["missingIds"]) <= int(row["targetDistinct"]),
                "missing ID cardinality")
        previous_layout = 65973 if idx < 2 else 36213
        require(int(row["metadataPlanningBytes"]) ==
                48 * int(row["baseChunks"]) +
                160 * int(row["baseDistinct"]) + previous_layout,
                "reference metadata PLANNING formula (not measured residency)")
        if idx in (1, 3):
            previous = rows[idx - 1]
            for a, b in (
                ("baseLayoutSha256", "targetLayoutSha256"),
                ("baseManifestId", "targetManifestId"),
                ("baseCsmSha256", "targetCsmSha256"),
                ("baseBytes", "targetBytes"),
                ("baseChunks", "targetChunks"),
                ("baseDistinct", "targetDistinct"),
                ("baseCsmBytes", "targetCsmBytes"),
            ):
                require(row[a] == previous[b], "adjacent old/new generation continuity: " + a)
    require(sum(int(x["priorT0Warm"]) for x in rows) == 14758982,
            "original complete T0 warm physical denominator")
    return rows


def replay_zip(archive_path, rows):
    with archive_path.open("rb") as stream:
        require(hashlib.file_digest(stream, "sha256").hexdigest() == ORIGINAL_ZIP_SHA,
                "original first-attempt Actions artifact ZIP SHA")
    with zipfile.ZipFile(archive_path) as archive:
        require(archive.testzip() is None, "ZIP CRC")
        require(set(archive.namelist()) == set(DIGESTS) | {"SHA256SUMS"},
                "original ZIP members (no raw source/CSM/CSP)")
        listed = {}
        for line in archive.read("SHA256SUMS").decode("ascii").splitlines():
            digest, name = line.split()
            require(name not in listed, "duplicate ZIP digest")
            listed[name] = digest
        require(listed == DIGESTS, "exact original member SHA256SUMS")
        for name, sha in DIGESTS.items():
            require(hashlib.sha256(archive.read(name)).hexdigest() == sha,
                    "original artifact member " + name)
        provenance = json.loads(archive.read("provenance.json"))
        require(provenance == {
            "schema": "chunkshift.patch-tree-t2a1-original-provenance.v1",
            "status": "ORIGINAL_CSM_CAPACITY_ONLY_NO_T2_CSP_NO_ADOPT",
            "sourceSha": ORIGINAL_SHA, "runId": ORIGINAL_RUN_ID,
            "runAttempt": "1", "gitRef": "refs/heads/main",
            "runnerEnvironment": "github-hosted", "runnerOS": "Linux",
            "runnerArch": "X64", "originalPairsRunId": 38136336025,
            "originalT0RunId": 38142160679, "sealedHoldoutOpened": False,
            "t2CspCreated": False,
        }, "original authenticated run provenance and no-T2-CSP scope")
        prior = json.loads(archive.read("prior-t0-calibration.json"))
        inventory = json.loads(archive.read("layouts.json"))
        output = json.loads(archive.read("t2a1-preflight.json"))
        require(prior["schema"] == "chunkshift.patch-tree-t0-t1-calibration.v1" and
                prior["status"] == "CALIBRATION_ONLY_NOT_DECISION" and
                prior["pairsSha256"] == PAIRS_SHA and len(prior["pairs"]) == 4,
                "prior complete physical T0 calibration source")
        require(inventory["schema"] == "chunkshift.patch-tree-inventory.v1" and
                inventory["status"] == "INVENTORY_ONLY_NOT_PATCH_EVIDENCE" and
                inventory["pairsSha256"] == PAIRS_SHA and
                inventory["pairCount"] == len(inventory["pairs"]) == 4,
                "original canonical source layouts")
        require(output["schema"] == "chunkshift.patch-tree-t2a1-csm-preflight.v1" and
                output["status"] == "ORIGINAL_CSM_CAPACITY_ONLY_NO_T2_CSP_NO_ADOPT" and
                output["originalPairsSha256"] == PAIRS_SHA and
                output["priorT0CalibrationSha256"] == DIGESTS["prior-t0-calibration.json"] and
                output["originalSourcesReverified"] and
                output["originalBaseCsmIdentityMatches"] and
                output["allFourPairsWithinLabCaps"] and
                output["totalPriorT0WarmPhysicalBytes"] == 14758982 and
                len(output["pairs"]) == 4, "original T2 A1 envelope")
        for i, (row, original, baseline, layout) in enumerate(
                zip(rows, output["pairs"], prior["pairs"], inventory["pairs"])):
            require(original["family"] == baseline["family"] == layout["family"] ==
                    row["family"] and
                    original["baseVersion"] == baseline["baseVersion"] ==
                    layout["baseVersion"] == row["baseVersion"] and
                    original["targetVersion"] == baseline["targetVersion"] ==
                    layout["targetVersion"] == row["targetVersion"],
                    "exact original source identity " + str(i))
            for short, side, layout_side in (
                ("base", original["base"], layout["base"]),
                ("target", original["target"], layout["target"]),
            ):
                for col, field in (
                    ("LayoutSha256","layoutSha256"),("ManifestId","manifestId"),
                    ("CsmSha256","csmSha256"),("Files","files"),
                    ("Bytes","contentBytes"),("Chunks","chunkRecords"),
                    ("Distinct","distinctChunkIds"),("CsmBytes","csmPhysicalBytes"),
                ):
                    value = row[short + col]
                    require(value == str(side[field]), "TSV/producer parity " + short + col)
                require(side["layoutSha256"] == layout_side["layoutSha256"] and
                        side["files"] == layout_side["fileCount"] and
                        side["contentBytes"] == layout_side["totalBytes"],
                        "original source layout/CSM identity")
            require(original["base"]["manifestId"] == baseline["oldTreeManifestId"] and
                    original["base"]["csmPhysicalBytes"] == baseline["oldTreeCsmBytes"],
                    "independent original old CSM match prior T0 baseline")
            for key, col in (
                ("targetUniqueMissingIds", "missingIds"),
                ("priorT0WarmPhysicalBytes", "priorT0Warm"),
                ("referenceMetadataPhysicalEstimateUpperBytes", "metadataPlanningBytes"),
                ("changedFiles", "changed"), ("addedFiles", "added"),
                ("removedFiles", "removed"), ("unchangedFiles", "unchanged"),
            ):
                require(row[col] == str(original[key]), "TSV/producer count parity " + key)
            for key, field in (
                ("priorT0WarmPhysicalBytes", "t0WarmPhysicalBytes"),
                ("priorT0SPhysicalBytes", "t0SPhysicalBytes"),
                ("priorTargetTreeMetadataBytes", "targetTreeManifestBytes"),
                ("priorBaseLocatorBytes", "baseLayoutBytes"),
                ("changedFiles", "changedCount"),("addedFiles", "addedCount"),
                ("removedFiles", "removedCount"),("unchangedFiles", "unchangedCount"),
            ):
                require(original[key] == baseline[field], "original prior T0/metadata consistency")
        return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-zip", type=Path)
    args = parser.parse_args()
    rows = read_rows()
    if args.original_zip:
        result = replay_zip(args.original_zip, rows)
        for p in result["pairs"]:
            print("T2A1 ORIGINAL", p["family"], p["baseVersion"], "->",
                  p["targetVersion"], "old CSM", p["base"]["chunkRecords"],
                  "new CSM", p["target"]["chunkRecords"], "missing unique",
                  p["targetUniqueMissingIds"])
    print("T2A1 ARCHIVED-OBSERVATION REPLAY PASS / NO_T2_CSP / NO_PRODUCTION_ADOPT")


if __name__ == "__main__":
    main()
