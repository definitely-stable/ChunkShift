#!/usr/bin/env python3
"""Independent source-locked T3-B1 ZIP/control/cost replay. Standard library only.

This verifies producer summaries and cross-source identities, not absent raw CSP
bytes. The original GitHub-hosted producer performed actual CSP create/apply.
"""
import argparse
import hashlib
import json
import struct
import zipfile
from pathlib import Path

ZIP_SHA256 = "06ce564ef43b07e5ea386551479fe86edaf8c58f266e01f579578acd50293624"
MEMBERS = {
    "original-pairs.json": "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "t3b1-selection.json": "2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad",
    "t3b1-physical.json": "c8a9de0599101e15834a9cd3026214f5176d71b251e79d1d0a6b162dbb4120cd",
    "provenance.json": "be095ea1eafd7cd5377f5329d9e99b8235956bc583677557b8f96705546b3c86",
}
SHA = "a64fc8cedb651d9c7f14f886709cfdf65c693f10"
EXPECTED = [("10.0.10", "10.0.11"), ("10.0.11", "10.0.12")]


def require(ok, message):
    if not ok:
        raise ValueError("T3B1 original audit: " + message)


def hexhash(buf):
    return hashlib.sha256(buf).hexdigest()


def layout_files(source):
    require(source["schema"] == "chunkshift.patch-tree-layout.v1", "layout schema")
    h = hashlib.sha256()
    at = 0
    previous = None
    result = {}
    for file in source["files"]:
        path = file["path"]
        encoded = path.encode("utf-8")
        require(previous is None or previous < encoded, "layout path ordering")
        require(file["offset"] == at and file["length"] >= 0 and
                len(file["sha256"]) == 64, "layout entry")
        h.update(struct.pack("<I", len(encoded)))
        h.update(encoded)
        h.update(struct.pack("<Q", file["length"]))
        h.update(bytes.fromhex(file["sha256"]))
        at += file["length"]
        previous = encoded
        result[path] = file
    require(at == source["totalBytes"] and
            h.hexdigest() == source["layoutSha256"] and
            len(result) == source["fileCount"], "layout SHA/content count")
    return result


def reference(c, manifest):
    # An implementation-independent reproduction of the fixed billed B1 record.
    payload = b"T3B1\x00" + struct.pack("<I", 1)
    for path in (c["targetPath"], c["sourcePath"]):
        encoded = path.encode("utf-8")
        payload += struct.pack("<I", len(encoded)) + encoded
    payload += struct.pack("<q", c["sourceLength"])
    payload += struct.pack("<q", c["targetLength"])
    payload += bytes.fromhex(c["sourceSha256"])
    payload += bytes.fromhex(c["targetSha256"])
    payload += bytes.fromhex(manifest)
    return payload


def audit(zip_path):
    require(hexhash(zip_path.read_bytes()) == ZIP_SHA256, "ZIP SHA against GitHub digest")
    with zipfile.ZipFile(zip_path) as z:
        require(z.testzip() is None, "ZIP CRC/integrity")
        require(set(z.namelist()) == {"SHA256SUMS", *MEMBERS}, "archive members")
        manifest = {}
        for line in z.read("SHA256SUMS").decode("ascii").splitlines():
            digest, name = line.split()
            require(name not in manifest, "duplicate member digest")
            manifest[name] = digest
        require(manifest == MEMBERS, "checksum manifest differs")
        for name, digest in MEMBERS.items():
            require(hexhash(z.read(name)) == digest, "member SHA " + name)

        selection = json.loads(z.read("t3b1-selection.json"))
        output = json.loads(z.read("t3b1-physical.json"))
        inventory = json.loads(z.read("layouts.json"))
        pairs = json.loads(z.read("original-pairs.json"))
        provenance = json.loads(z.read("provenance.json"))
        require(provenance["runId"] == "39967460136" and
                provenance["attempt"] == "1" and
                provenance["sourceSha"] == SHA and
                provenance["gitRef"] == "refs/heads/main" and
                provenance["runnerEnvironment"] == "github-hosted" and
                provenance["runnerOS"] == "Linux" and
                provenance["runnerArch"] == "X64" and
                provenance["sealedHoldoutOpened"] is False, "original-run provenance")
        require(selection["schema"] == "chunkshift.patch-tree-t3b1-fixed-two.v1" and
                selection["originalPairsSha256"] == MEMBERS["original-pairs.json"] and
                output["schema"] == "chunkshift.patch-tree-t3b1-two-csp.v1" and
                output["status"] == "CALIBRATION_ONLY_TWO_RENAMED_FILES_NO_ADOPT" and
                len(selection["candidates"]) == len(output["rows"]) == 2, "result envelope")

        frozen = {(x["family"], x["base"], x["target"]): x
                  for x in pairs["pairs"]}
        layout = {(x["family"], x["baseVersion"], x["targetVersion"]): x
                  for x in inventory["pairs"]}
        require(len(inventory["pairs"]) == 4 and
                set(x["family"] for x in inventory["pairs"]) ==
                {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64"},
                "original calibration only")
        hashes = {}
        for key, x in layout.items():
            hashes[key] = (layout_files(x["base"]), layout_files(x["target"]))
        accum = dict(raw=0, t0s=0, warm=0, cold=0, csp=0, maps=0)
        for index, (candidate, result) in enumerate(zip(
                selection["candidates"], output["rows"])):
            a, b = EXPECTED[index]
            key = ("dotnet-aspnetcore-win-x64", a, b)
            old, new = hashes[key]
            original = frozen[key]
            require((candidate["baseVersion"], candidate["targetVersion"]) == (a, b),
                    "source pair order")
            require(candidate["sourcePath"] in original["removed"] and
                    len(original["removed"]) == len(original["added"]) == 1 and
                    original["added"][0]["path"] == candidate["targetPath"],
                    "frozen moved source/target relationship")
            require(candidate["sourcePath"] in old and
                    candidate["targetPath"] in new and
                    old[candidate["sourcePath"]]["sha256"] == candidate["sourceSha256"] and
                    old[candidate["sourcePath"]]["length"] == candidate["sourceLength"] and
                    new[candidate["targetPath"]]["sha256"] == candidate["targetSha256"] and
                    new[candidate["targetPath"]]["length"] == candidate["targetLength"],
                    "original layout identity")
            for field in ("sourcePath", "targetPath", "sourceSha256", "targetSha256",
                          "sourceLength", "targetLength"):
                require(result[field] == candidate[field], "CSP result source identity")
            require(result["baseManifestId"] != result["targetManifestId"] and
                    result["targetManifestId"] == result["t0SManifestId"] and
                    result["exactReconstruction"] is True and
                    len(result["t3CspSha256"]) == len(result["t0SCspSha256"]) == 64,
                    "BASE / target identity and reported apply")
            mapbytes = reference(candidate, result["baseManifestId"])
            require(len(mapbytes) == result["mapPhysicalBytes"] and
                    hexhash(mapbytes) == result["mapSha256"], "recomputed path locator map")
            require(result["rawT0Bytes"] == candidate["targetLength"] and
                    result["t3WarmBytes"] ==
                    result["t3CspPhysicalBytes"] + len(mapbytes) and
                    result["t3ColdBytesIncludingBaseCsm"] ==
                    result["t3WarmBytes"] + result["sourceCsmBytes"],
                    "per-file complete physical cost")
            for col, value in [
                ("raw", result["rawT0Bytes"]),
                ("t0s", result["t0SPhysicalBytes"]),
                ("warm", result["t3WarmBytes"]),
                ("cold", result["t3ColdBytesIncludingBaseCsm"]),
                ("csp", result["t3CspPhysicalBytes"]),
                ("maps", result["mapPhysicalBytes"]),
            ]:
                accum[col] += value
        require(accum == dict(raw=2713272, t0s=1097577, warm=92671, cold=94899,
                              csp=92081, maps=590), "aggregate physical totals")
        require(output["originalT0WarmBytes"] == 14758982 and
                output["originalT0SWarmBytes"] == 13143287 and
                output["originalCommonTreeMetadataBytes"] == 185436 and
                output["rawRenamedTargetBytes"] == accum["raw"] and
                output["actualT0SBytesForTwo"] == accum["t0s"] and
                output["actualT3WarmBytesForTwo"] == accum["warm"] and
                output["actualT3ColdBytesForTwo"] == accum["cold"] and
                output["modelledFullT3WarmBytes"] ==
                14758982 - accum["raw"] + accum["warm"] and
                output["modelledFullT3ColdBytes"] ==
                14758982 - accum["raw"] + accum["cold"], "modelled ledger")
        return accum


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("original_artifact_zip", type=Path)
    x = p.parse_args()
    print("T3B1 ORIGINAL INDEPENDENT ARCHIVE/COST AUDIT PASS", audit(x.original_artifact_zip))
    print("B1_PROMISING_CALIBRATION_ONLY_NO_ADOPT")
