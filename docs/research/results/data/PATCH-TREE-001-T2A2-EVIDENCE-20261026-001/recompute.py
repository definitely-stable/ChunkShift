#!/usr/bin/env python3
"""Stdlib-only independent audit of the first source-locked T2-A2 original ZIP.

This authenticates GitHub provenance and rederives archived *observations*. The
actual CSP bytes and original source files were NOT archived; this script does
not rerun independent CSP decoding or prove private RSS or publisher safety.
"""
import argparse
import csv
import hashlib
import io
import json
import math
from pathlib import Path
import re
import statistics
import struct
import sys
import unicodedata
import zipfile

RUN = 40281148160
ARTIFACT = 12632258695
PARENT = 40204580308
SOURCE_SHA = "fe26c8556c34c0c6f105038733023b1d00cafd49"
PARENT_SHA = "555c992f30473c373f1efd0d6d8b3d18bf87a090"
ZIP_SHA = "be60d2982ae8c8eb904fbd4233dd91d5248b14cf8e36a1c4c2365629573dda8e"
MEMBERS = {
    "original-pairs.json": "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "layouts.json": "6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "prior-t0-calibration.json": "d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c",
    "prior-t2a1-preflight.json": "823820571d3534b302e2acb8f0501ac4dbe4c6030093e6e27b3a066945d65fb6",
    "t2a2-original-pilot.json": "9d7de74a2bd44f10a2c299909a2b16e40a8f394ff3afcf8fbf319e6d19b97939",
    "provenance.json": "06e23880b72079a45f3f9dfec1fb41806c5fd91a8cbad9001ffe4ebc3f6567a1",
}
EXPECTED = [
    ("dotnet-aspnetcore-win-x64", "10.0.10", "10.0.11", 339, 1266, 1262, 702, 5748069),
    ("dotnet-aspnetcore-win-x64", "10.0.11", "10.0.12", 339, 1262, 1267, 530, 4409941),
    ("dotnet-runtime-linux-arm64", "10.0.10", "10.0.11", 193, 1058, 1045, 543, 2798481),
    ("dotnet-runtime-linux-arm64", "10.0.11", "10.0.12", 193, 1045, 1057, 385, 1802491),
]

def check(value, message):
    if not value:
        raise ValueError("T2A2 original audit REJECT: " + message)

def digest(data):
    return hashlib.sha256(data).hexdigest()

def decoder(raw, label):
    def no_duplicate(pairs):
        d = {}
        for k, v in pairs:
            check(k not in d, "duplicate JSON key: " + label)
            d[k] = v
        return d
    return json.loads(raw.decode("utf-8"), object_pairs_hook=no_duplicate)

def verify_zip(path):
    raw = path.read_bytes()
    check(len(raw) == 502387 and digest(raw) == ZIP_SHA, "original ZIP size/SHA256")
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        info = z.infolist()
        check(len(info) == 7 and {i.filename for i in info} == set(MEMBERS) | {"SHA256SUMS"},
              "ZIP exact membership; duplicate or unexpected file")
        check(sum(i.file_size for i in info) < 5 * 1024 * 1024 and
              all(not i.is_dir() and not i.flag_bits & 1 for i in info),
              "ZIP size/entry type/encryption")
        check(z.testzip() is None, "ZIP CRC")
        data = {i.filename: z.read(i.filename) for i in info}
    manifest = {}
    for line in data["SHA256SUMS"].decode("ascii").splitlines():
        check(bool(re.fullmatch(r"[0-9a-f]{64}  [A-Za-z0-9.-]+", line)),
              "SHA256SUMS syntax")
        sha, name = line.split("  ")
        check(name not in manifest, "SHA256SUMS duplicate")
        manifest[name] = sha
    check(manifest == MEMBERS, "original source-locked member hashes")
    for name, sha in MEMBERS.items():
        check(digest(data[name]) == sha, "archived member SHA: " + name)
    return {name: decoder(data[name], name) for name in MEMBERS}

def layout_index(layout):
    check(layout["schema"] == "chunkshift.patch-tree-layout.v1" and
          layout["order"] == "NFC UTF-8 byte order", "tree layout domain")
    files = layout["files"]
    check(len(files) == layout["fileCount"] and 0 < len(files) <= 1024,
          "complete full tree file population")
    offset, prev, lowered = 0, None, set()
    h = hashlib.sha256()
    for f in files:
        path = f["path"]
        check(isinstance(path, str) and path and
              unicodedata.normalize("NFC", path) == path and
              not path.startswith("/") and "\\" not in path and ":" not in path and
              all(part not in ("", ".", "..") for part in path.split("/")),
              "unsafe/noncanonical path")
        encoded = path.encode("utf-8")
        check(prev is None or prev < encoded, "UTF8 byte path order")
        prev = encoded
        check(path.casefold() not in lowered, "case-insensitive file collision")
        lowered.add(path.casefold())
        size = f["length"]
        check(type(size) is int and size >= 0 and f["offset"] == offset,
              "contiguous file offset/length")
        check(bool(re.fullmatch(r"[0-9a-fA-F]{64}", f["sha256"])), "file hash syntax")
        h.update(struct.pack("<I", len(encoded)) + encoded +
                 struct.pack("<Q", size) + bytes.fromhex(f["sha256"]))
        offset += size
    check(offset == layout["totalBytes"] and offset <= 160 * 1024 * 1024 and
          h.hexdigest() == layout["layoutSha256"], "full tree hash/size")
    return {f["path"]: f for f in files}

def diagnostic_map(layout, base):
    fields = [
        ({"path": f["path"], "offset": f["offset"], "length": f["length"],
          "sha256": f["sha256"].lower()} if base else
         {"path": f["path"], "length": f["length"],
          "sha256": f["sha256"].lower()}) for f in layout["files"]
    ]
    schema = ("chunkshift.patch-tree-base-locator-reference.v1" if base else
              "chunkshift.patch-tree-regular-files-reference.v1")
    # All frozen original paths are ASCII; matches actual .NET compact JSON bytes.
    return len(json.dumps({"schema": schema, "files": fields},
                          separators=(",", ":"), ensure_ascii=False).encode())

def finite(x):
    check(type(x) in (float, int) and math.isfinite(x) and x >= 0,
          "negative/nonfinite time measurement")
    return x

def independent_pair(k, row, layout, baseline, a1, original):
    family, bv, tv, count, oldrec, newrec, missing, t0 = EXPECTED[k]
    for entry in (row, layout, baseline, a1):
        check((entry["family"], entry["baseVersion"], entry["targetVersion"]) ==
              (family, bv, tv), "frozen four-pair order and identity")
    check((original["family"], original["base"], original["target"]) ==
          (family, bv, tv), "original locked release pair")
    check(layout["datasetRole"] == "calibration", "sealed holdout is ineligible")
    old, new = layout_index(layout["base"]), layout_index(layout["target"])
    check(len(old) == len(new) == count, "pinned full file counts")
    for which, files in (("base", old), ("target", new)):
        side = a1[which]
        lay = layout[which]
        check(side["layoutSha256"] == lay["layoutSha256"] and
              side["contentBytes"] == lay["totalBytes"] and
              side["files"] == len(files) and
              0 < side["chunkRecords"] <= 262144 and
              0 < side["distinctChunkIds"] <= side["chunkRecords"] and
              0 < side["csmPhysicalBytes"] <= 8 * 1024 * 1024,
              which + " original CSM/source capacities")
    check((a1["base"]["chunkRecords"], a1["target"]["chunkRecords"],
           a1["targetUniqueMissingIds"]) == (oldrec, newrec, missing),
          "frozen original CSM chunk and missing IDs")
    check(baseline["oldTreeManifestId"] == a1["base"]["manifestId"] and
          baseline["oldTreeCsmBytes"] == a1["base"]["csmPhysicalBytes"] and
          baseline["t0WarmPhysicalBytes"] == a1["priorT0WarmPhysicalBytes"] == t0,
          "original physical T0/base CSM lock")
    changed = {p for p in old.keys() & new.keys()
               if old[p]["sha256"].lower() != new[p]["sha256"].lower() or
               old[p]["length"] != new[p]["length"]}
    added, removed = new.keys() - old.keys(), old.keys() - new.keys()
    unmodified = len(new) - len(changed) - len(added)
    check((len(changed), len(added), len(removed), unmodified) ==
          tuple(baseline[q] for q in ("changedCount", "addedCount", "removedCount",
                                     "unchangedCount")) ==
          tuple(a1[q] for q in ("changedFiles", "addedFiles", "removedFiles",
                                "unchangedFiles")) ==
          tuple(row[q] for q in ("changedFiles", "addedFiles", "removedFiles",
                                 "unchangedFiles")), "complete changed/added/removed/unchanged")
    check(set(v["path"] for v in original["changed"]) == changed and
          set(v["path"] for v in original["added"]) == added and
          set(original["removed"]) == removed and
          original["identicalFiles"] == unmodified,
          "frozen original path classification")
    for v in original["changed"]:
        o, n = old[v["path"]], new[v["path"]]
        check(v["baseSize"] == o["length"] and
              v["targetSize"] == n["length"] and
              v["baseSha256"].lower() == o["sha256"].lower() and
              v["targetSha256"].lower() == n["sha256"].lower(),
              "frozen changed file original identities")
    for v in original["added"]:
        n = new[v["path"]]
        check(v["size"] == n["length"] and
              v["sha256"].lower() == n["sha256"].lower(), "frozen added file")
    check(sum(new[p]["length"] for p in old.keys() & new.keys() - changed) ==
          original["identicalBytes"], "all unchanged file bytes")

    targets = diagnostic_map(layout["target"], False)
    locator = diagnostic_map(layout["base"], True)
    check(targets == baseline["targetTreeManifestBytes"] ==
          a1["priorTargetTreeMetadataBytes"] ==
          row["targetTreeMapBytes"] and
          locator == baseline["baseLayoutBytes"] ==
          a1["priorBaseLocatorBytes"] == row["sourceLocatorMapBytes"],
          "independently rebuilt full target map and base locator physical bytes")
    t0rows = baseline["files"]
    check(len(t0rows) == len(changed) + len(added) and
          len({v["path"] for v in t0rows}) == len(t0rows) and
          {v["path"] for v in t0rows} == changed | added,
          "complete actual T0 changed CSP + added-file row population")
    for v in t0rows:
        target = new[v["path"]]
        check(v["targetSha256"].lower() == target["sha256"].lower() and
              v["targetBytes"] == target["length"], "T0 target file identity")
        if v["path"] in added:
            check(v["kind"] == "added" and v["t0Bytes"] == target["length"]
                  and v["t0PatchSha256"] is None, "added T0 raw file charge")
        else:
            check(v["kind"] == "changed" and v["t0Bytes"] > 0 and
                  bool(re.fullmatch("[0-9a-f]{64}", v["t0PatchSha256"])),
                  "changed T0 actual CSP charge")
    t0payload = sum(v["t0Bytes"] for v in t0rows)
    check(t0payload == baseline["t0PayloadBytes"] ==
          row["priorT0PayloadBytes"] and
          t0payload + targets == t0 == row["priorT0WarmPhysicalBytes"],
          "independently reconstructed full original T0 denominator")

    check(row["baseLayoutSha256"] == layout["base"]["layoutSha256"] and
          row["targetLayoutSha256"] == layout["target"]["layoutSha256"] and
          row["baseManifestId"] == a1["base"]["manifestId"] and
          row["targetManifestId"] == a1["target"]["manifestId"] and
          row["baseCsmBytes"] == a1["base"]["csmPhysicalBytes"] and
          row["targetCsmBytes"] == a1["target"]["csmPhysicalBytes"],
          "T2 original BASE/TARGET identity")
    csp = row["t2CspBytes"]
    check(0 < csp <= 256 * 1024 * 1024 and
          bool(re.fullmatch(r"[0-9a-f]{64}", row["t2CspSha256"])),
          "T2 actual CSP bytes/hash")
    warm, cold = csp + targets, csp + targets + locator + row["baseCsmBytes"]
    check(row["t2WarmPhysicalBytes"] == warm and
          row["t2ColdPhysicalBytes"] == cold and
          row["completeFlatReconstruction"] is True and
          row["baseFiles"] == row["targetFiles"] == count,
          "T2 warm/cold complete physical cost and reported Apply")
    check(len(row["samples"]) == 4, "one warmup plus three measurements")
    times = {}
    reads = None
    for i, s in enumerate(row["samples"]):
        check(s["round"] == i - 1 and s["warmup"] is (i == 0) and
              s["exactReconstruction"] is True and s["cspBytes"] == csp and
              s["cspSha256"] == row["t2CspSha256"],
              "four repeated CSP SHA/size and Apply equivalence")
        value = s["baseCreateLogicalReadBytes"]
        check(type(value) is int and value >= 0 and
              (reads is None or value == reads),
              "measured original base logical reads")
        reads = value
        for name in ("createCpuMilliseconds", "createWallMilliseconds",
                     "applyCpuMilliseconds", "applyWallMilliseconds"):
            finite(s[name])
            if i: times.setdefault(name, []).append(s[name])
    for name, output in (
        ("createCpuMilliseconds", "medianT2CreateCpuMilliseconds"),
        ("createWallMilliseconds", "medianT2CreateWallMilliseconds"),
        ("applyCpuMilliseconds", "medianT2ApplyCpuMilliseconds"),
        ("applyWallMilliseconds", "medianT2ApplyWallMilliseconds"),
    ):
        check(math.isclose(statistics.median(times[name]), row[output],
                           abs_tol=1e-7, rel_tol=1e-12),
              "independently recomputed T2 CPU/wall sample median")
    return (family, bv, tv, oldrec, newrec, missing, len(changed), len(added),
            len(removed), unmodified, t0, csp, row["t2CspSha256"], warm, cold,
            row["medianT2CreateCpuMilliseconds"],
            row["medianT2CreateWallMilliseconds"],
            row["medianT2ApplyCpuMilliseconds"],
            row["medianT2ApplyWallMilliseconds"], reads)

def run_audit(records, census):
    original = records["original-pairs.json"]
    inventory = records["layouts.json"]
    old = records["prior-t0-calibration.json"]
    a1 = records["prior-t2a1-preflight.json"]
    result = records["t2a2-original-pilot.json"]
    provenance = records["provenance.json"]
    check(original["schema"] == "chunkshift.patch-pairs.v1" and
          len(original["pairs"]) == 12, "12 frozen pair descriptors; 4 original only")
    check(inventory["schema"] == "chunkshift.patch-tree-inventory.v1" and
          inventory["status"] == "INVENTORY_ONLY_NOT_PATCH_EVIDENCE" and
          inventory["pairsSha256"] == MEMBERS["original-pairs.json"] and
          inventory["pairCount"] == 4, "original inventory source lock")
    check(old["schema"] == "chunkshift.patch-tree-t0-t1-calibration.v1" and
          old["status"] == "CALIBRATION_ONLY_NOT_DECISION", "historic original T0 source")
    check(a1["schema"] == "chunkshift.patch-tree-t2a1-csm-preflight.v1" and
          a1["status"] == "ORIGINAL_CSM_CAPACITY_ONLY_NO_T2_CSP_NO_ADOPT" and
          a1["originalSourcesReverified"] is True and
          a1["originalBaseCsmIdentityMatches"] is True and
          a1["allFourPairsWithinLabCaps"] is True, "original A1 successful CSM preflight")
    check(result["schema"] == "chunkshift.patch-tree-t2a2-original-csp-pilot.v1" and
          result["status"] == "ORIGINAL_T2_CSP_PHYSICAL_PILOT_NO_RESOURCE_OR_PRODUCTION_VERDICT" and
          result["warmupsPerPair"] == 1 and result["measuredRoundsPerPair"] == 3 and
          result["sealedHoldoutOpened"] is False, "original T2A2 calibration-only status")
    check(provenance == {
        "a1OriginalRunId": PARENT, "cpuWallPairedT0": False,
        "physicalT2CspArchived": False, "privateRssAdmission": False,
        "productionAdopt": False, "ref": "refs/heads/main",
        "runAttempt": "1", "runId": str(RUN), "runnerArch": "X64",
        "runnerEnvironment": "github-hosted", "runnerOS": "Linux",
        "schema": "chunkshift.patch-tree-t2a2-original-provenance.v1",
        "sealedHoldoutOpened": False, "sourceSha": SOURCE_SHA,
    }, "GitHub-hosted original first-main provenance")
    for v in (inventory, old, a1, result):
        check(v.get("pairsSha256", v.get("originalPairsSha256")) ==
              MEMBERS["original-pairs.json"], "original pairs identity")
    check(len(inventory["pairs"]) == len(old["pairs"]) ==
          len(a1["pairs"]) == len(result["pairs"]) == len(census) == 4,
          "exact four original transitions")
    rows = [independent_pair(i, result["pairs"][i], inventory["pairs"][i],
                             old["pairs"][i], a1["pairs"][i],
                             original["pairs"][i]) for i in range(4)]
    for i, j in ((0, 1), (2, 3)):
        for k in ("manifestId", "csmSha256", "csmPhysicalBytes", "chunkRecords",
                  "distinctChunkIds", "layoutSha256", "contentBytes"):
            check(a1["pairs"][i]["target"][k] == a1["pairs"][j]["base"][k],
                  "original adjacent-version CSM identity")
    for row, ledger in zip(rows, census):
        keys = ("family", "base_version", "target_version", "base_records",
                "target_records", "missing_target_ids", "changed", "added",
                "removed", "unchanged", "t0_warm_b", "t2_csp_b", "t2_csp_sha256",
                "t2_warm_b", "t2_cold_b", "median_create_cpu_ms",
                "median_create_wall_ms", "median_apply_cpu_ms",
                "median_apply_wall_ms", "base_stream_create_logical_reads_b")
        for key, actual in zip(keys, row):
            expected = ledger[key]
            if type(actual) is float:
                check(math.isclose(actual, float(expected), abs_tol=1e-7,
                                   rel_tol=1e-12), "committed census median " + key)
            else:
                check(str(actual) == expected, "committed census entry " + key)
    check(sum(r[10] for r in rows) == old["pairs"][0]["t0WarmPhysicalBytes"] +
          sum(p["t0WarmPhysicalBytes"] for p in old["pairs"][1:]) ==
          result["completeT0WarmBytes"] == 14758982, "four-pair complete T0")
    check(sum(r[13] for r in rows) == result["completeT2WarmBytes"] == 10828279,
          "four-pair original T2 warm")
    check(sum(r[14] for r in rows) == result["completeT2ColdBytes"] == 11200943,
          "four-pair original T2 cold")
    return rows

def verify_live_metadata(run, artifact, parent):
    check(run["id"] == RUN and run["head_sha"] == SOURCE_SHA and
          run["event"] == "push" and run["run_attempt"] == 1 and
          run["head_branch"] == "main" and run["status"] == "completed" and
          run["conclusion"] == "success" and
          run["path"].split("@")[0] ==
          ".github/workflows/patch-tree-001-t2a2-real-pilot.yml" and
          run["repository"]["full_name"] == "definitely-stable/ChunkShift",
          "live original Actions run")
    check(parent["id"] == PARENT and parent["head_sha"] == PARENT_SHA and
          parent["run_attempt"] == 1 and parent["event"] == "push" and
          parent["status"] == "completed" and parent["conclusion"] == "success" and
          parent["path"].split("@")[0] ==
          ".github/workflows/patch-tree-001-t2a1-real-preflight.yml",
          "live original A1 Actions parent run")
    check(artifact["id"] == ARTIFACT and
          artifact["name"] == "patch-tree-t2a2-original-CSP-pilot-NO-ADOPT" and
          artifact["size_in_bytes"] == 502387 and artifact["expired"] is False and
          artifact["workflow_run"]["id"] == RUN and
          artifact["workflow_run"]["head_sha"] == SOURCE_SHA,
          "live original artifact identity")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--original-zip", type=Path)
    parser.add_argument("--run-json", type=Path)
    parser.add_argument("--artifact-json", type=Path)
    parser.add_argument("--parent-json", type=Path)
    args = parser.parse_args()
    tsv = Path(__file__).with_name("census.tsv")
    census = list(csv.DictReader(tsv.open(encoding="utf-8", newline=""),
                                 delimiter="\t"))
    check(len(census) == 4, "committed original census rows")
    check([r["t0_warm_b"] for r in census] ==
          [str(x[-1]) for x in EXPECTED], "committed T0 denominators")
    check(sum(int(r["t2_warm_b"]) for r in census) == 10828279 and
          sum(int(r["t2_cold_b"]) for r in census) == 11200943,
          "static physical warm/cold sums")
    if args.original_zip:
        rows = run_audit(verify_zip(args.original_zip), census)
        print("PASS original ZIP audit: four transitions, 16 samples,",
              "T0", sum(r[10] for r in rows), "T2 warm", sum(r[13] for r in rows),
              "T2 cold", sum(r[14] for r in rows))
    if any((args.run_json, args.artifact_json, args.parent_json)):
        check(all((args.run_json, args.artifact_json, args.parent_json)),
              "incomplete independent live GitHub metadata")
        verify_live_metadata(*[decoder(p.read_bytes(), str(p)) for p in
                               (args.run_json, args.artifact_json,
                                args.parent_json)])
    check(args.original_zip or not any((args.run_json, args.artifact_json,
                                        args.parent_json)),
          "live provenance needs the exact original artifact")
    print("NO_PRODUCTION_ADOPT: no archived CSP bytes, paired T0 CPU, private RSS or publisher")

if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, TypeError, AssertionError, UnicodeError,
            zipfile.BadZipFile, OSError) as error:
        print("FAIL T2A2 audit:", error, file=sys.stderr)
        sys.exit(1)
