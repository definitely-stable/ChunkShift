#!/usr/bin/env python3
"""Source-pinned independent replay of original PATCH-TREE-001 T2-A3A paired CREATE.

This checks archived *measurements* against historical physical oracles, not
raw CSP decoding, receiver memory, or authenticated multi-file publication.
"""
import argparse
import copy
import csv
import hashlib
import importlib.util
import io
import json
import math
from pathlib import Path
import re
import statistics
import sys
import zipfile

RUN, ARTIFACT = 40299065287, 12641767485
SOURCE = "5b2f1608030b6660c7e0df7454a892b3835808a7"
PREV_RUN, PREV_SHA = 40281148160, "fe26c8556c34c0c6f105038733023b1d00cafd49"
ZIP_SHA = "7570fee0a9f67b49741a7fb333d6e04b6e3fc20082cda1dfd7596d2b671fb511"
ZIP_SIZE = 505901
MEMBERS = {
    "original-pairs.json":"8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
    "layouts.json":"6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a",
    "prior-t0-calibration.json":"d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c",
    "prior-t2a1-preflight.json":"823820571d3534b302e2acb8f0501ac4dbe4c6030093e6e27b3a066945d65fb6",
    "prior-t2a2-pilot.json":"9d7de74a2bd44f10a2c299909a2b16e40a8f394ff3afcf8fbf319e6d19b97939",
    "t2a3a-paired-create.json":"3638ace82e8db1cf751426322bef5216a1b4b22b11b81f9c04e631f10ddfd4d3",
    "provenance.json":"550aa8e6b1a5c2acc55db5fba82458e30cb9a04578ec057cc2c0fa046da54a9e",
}

def need(ok, message):
    if not ok:
        raise ValueError("T2A3A original REJECT: " + message)

def sha(data):
    return hashlib.sha256(data).hexdigest()

def decode(raw):
    def unique(pairs):
        result = {}
        for k, v in pairs:
            need(k not in result, "duplicate JSON key " + k)
            result[k] = v
        return result
    return json.loads(raw.decode("utf-8"), object_pairs_hook=unique)

def source_oracle():
    path = Path(__file__).parent.parent / "PATCH-TREE-001-T2A2-EVIDENCE-20261026-001" / "recompute.py"
    need(path.is_file(), "independent prior T2A2 original tree/oracle missing")
    spec = importlib.util.spec_from_file_location("t2a2_source_oracle", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def zip_records(path):
    content = Path(path).read_bytes()
    need(len(content) == ZIP_SIZE and sha(content) == ZIP_SHA, "original exact ZIP bytes/SHA")
    with zipfile.ZipFile(io.BytesIO(content)) as zipfile_reader:
        entries = zipfile_reader.infolist()
        need(len(entries) == 8 and
             {e.filename for e in entries} == set(MEMBERS) | {"SHA256SUMS"},
             "ZIP exact population; duplicate or unexpected entry")
        need(all(not e.is_dir() and not (e.flag_bits & 1) and
                 e.file_size <= 2 * 1024 * 1024 for e in entries),
             "plain bounded source members")
        need(zipfile_reader.testzip() is None, "ZIP CRC check")
        entries_data = {e.filename: zipfile_reader.read(e.filename) for e in entries}
    hashes = {}
    for line in entries_data["SHA256SUMS"].decode("ascii").splitlines():
        need(re.fullmatch("[a-f0-9]{64}  [a-zA-Z0-9.-]+", line) is not None,
             "checksum grammar")
        digest, name = line.split("  ")
        need(name not in hashes, "duplicate checksum item")
        hashes[name] = digest
    need(hashes == MEMBERS, "source-locked member checksum inventory")
    need(sha(entries_data["SHA256SUMS"]) ==
         "e711cd311e938eb37cc75788bf3e77722a482dfb2f7de6e095f603c16c9bd983",
         "pinned physical SHA256SUMS")
    for name, digest in MEMBERS.items():
        need(sha(entries_data[name]) == digest, "pinned member SHA: " + name)
    return {key: decode(entries_data[key]) for key in MEMBERS}

def finite(number):
    need(type(number) in (float, int) and math.isfinite(number) and number >= 0,
         "negative/nonfinite original time")
    return number

def audit(docs):
    old, layouts = docs["prior-t0-calibration.json"], docs["layouts.json"]
    a1, a2 = docs["prior-t2a1-preflight.json"], docs["prior-t2a2-pilot.json"]
    original, result = docs["original-pairs.json"], docs["t2a3a-paired-create.json"]
    need(original["schema"] == "chunkshift.patch-pairs.v1" and
         len(original["pairs"]) == 12, "frozen original 12 descriptor inventory")
    need(layouts["schema"] == "chunkshift.patch-tree-inventory.v1" and
         layouts["status"] == "INVENTORY_ONLY_NOT_PATCH_EVIDENCE" and
         layouts["pairsSha256"] == MEMBERS["original-pairs.json"], "source tree inventory")
    need(old["schema"] == "chunkshift.patch-tree-t0-t1-calibration.v1" and
         old["status"] == "CALIBRATION_ONLY_NOT_DECISION" and
         old["pairsSha256"] == MEMBERS["original-pairs.json"], "historical original T0")
    need(a1["schema"] == "chunkshift.patch-tree-t2a1-csm-preflight.v1" and
         a1["originalSourcesReverified"] is True and
         a1["originalBaseCsmIdentityMatches"] is True and
         a1["allFourPairsWithinLabCaps"] is True, "original A1 CSM preflight")
    need(a2["schema"] == "chunkshift.patch-tree-t2a2-original-csp-pilot.v1" and
         a2["status"] == "ORIGINAL_T2_CSP_PHYSICAL_PILOT_NO_RESOURCE_OR_PRODUCTION_VERDICT" and
         a2["completeT0WarmBytes"] == 14758982 and
         a2["completeT2WarmBytes"] == 10828279 and
         a2["sealedHoldoutOpened"] is False, "original A2 full physical source")
    need(result["schema"] == "chunkshift.patch-tree-t2a3a-paired-create.v1" and
         result["status"] == "PAIRED_ORIGINAL_CREATE_ONLY_NO_RSS_OR_PRODUCTION_ADOPT" and
         result["sourcePairsSha256"] == MEMBERS["original-pairs.json"] and
         result["frozenPriorT0Sha256"] == MEMBERS["prior-t0-calibration.json"] and
         result["frozenPriorA1Sha256"] == MEMBERS["prior-t2a1-preflight.json"] and
         result["frozenPriorA2Sha256"] == MEMBERS["prior-t2a2-pilot.json"] and
         result["warmupsPerLane"] == 1 and result["measuredRoundsPerLane"] == 4 and
         result["cpuRatioGate"] == result["wallRatioGate"] == 3 and
         result["t2AbsoluteWallMillisecondsGate"] == 60000,
         "exact frozen A3A research protocol and budgets")
    need(docs["provenance.json"] == {
        "schema":"chunkshift.patch-tree-t2a3a-original-provenance.v1",
        "sourceSha":SOURCE, "runId":str(RUN), "runAttempt":"1",
        "ref":"refs/heads/main", "runnerEnvironment":"github-hosted",
        "runnerOS":"Linux", "runnerArch":"X64",
        "originalA1Run":40204580308, "originalA2Run":PREV_RUN,
        "privateRssObserved":False, "sealedHoldoutOpened":False,
        "matchedCompletePipeline":False, "productionAdopt":False,
    }, "original GitHub-hosted first-main provenance")
    need(all(len(d["pairs"]) == 4 for d in (old,layouts,a1,a2,result)),
         "exact original four-pair population")
    ref = source_oracle()
    rows, raw_count, passing = [], 0, 0
    for i, r in enumerate(result["pairs"]):
        base, a2row = old["pairs"][i], a2["pairs"][i]
        apair = a1["pairs"][i]
        prior = ref.independent_pair(i, a2row, layouts["pairs"][i], base,
                                     apair, original["pairs"][i])
        family, bv, tv = prior[:3]
        need(tuple(r[k] for k in ("family","baseVersion","targetVersion")) ==
             (family,bv,tv), "frozen original release order")
        need(r["baseLayoutSha256"] == layouts["pairs"][i]["base"]["layoutSha256"] and
             r["targetLayoutSha256"] == layouts["pairs"][i]["target"]["layoutSha256"] and
             r["baseManifestId"] == apair["base"]["manifestId"] and
             r["targetManifestId"] == apair["target"]["manifestId"] and
             r["t2CspSha256"] == a2row["t2CspSha256"], "frozen SHA/manifest identity")
        need(tuple(r[k] for k in ("changedFiles","addedFiles","removedFiles","unchangedFiles")) ==
             tuple(prior[6:10]) and r["t0WarmBytes"] == prior[10] and
             r["t2WarmBytes"] == prior[13], "original complete population and physical cost")
        records = base["files"]
        actual_t0 = sum(f["t0Bytes"] for f in records)
        need(actual_t0 == base["t0PayloadBytes"] and
             r["t0WarmBytes"] == actual_t0 + base["targetTreeManifestBytes"],
             "complete T0 physical CSP/raw plus common target tree map")
        signature = "".join(f["path"] + "\t" + f["kind"] + "\t" +
                            str(f["t0Bytes"]) + "\t" +
                            (f["t0PatchSha256"] if f["kind"] == "changed" else f["targetSha256"]) +
                            "\n" for f in records)
        t0_digest = sha(signature.encode("utf-8"))
        expected = [(-1,"T0",0),(-1,"T2",1),
                    (0,"T0",0),(0,"T2",1),(1,"T2",0),(1,"T0",1),
                    (2,"T2",0),(2,"T0",1),(3,"T0",0),(3,"T2",1)]
        samples = r["samples"]
        need(len(samples) == 10 and
             [(s["round"],s["lane"],s["order"]) for s in samples] == expected,
             "preregistered ABBA paired round order (plus 2 warmups)")
        median = {}
        for lane in ("T0","T2"):
            lane_samples = [s for s in samples if s["lane"] == lane]
            need(len(lane_samples) == 5 and lane_samples[0]["warmup"] is True and
                 all(s["warmup"] is False for s in lane_samples[1:]),
                 "exact warmup/measurement samples per lane")
            for s in lane_samples:
                need(s["physicalIdentityVerified"] is True and
                     s["payloadBytes"] == (actual_t0 if lane == "T0" else a2row["t2CspBytes"]) and
                     s["digest"] == (t0_digest if lane == "T0" else a2row["t2CspSha256"]),
                     "all original round CSP/raw SHA+physical cost")
                need(s["logicalBaseReadBytes"] ==
                     (0 if lane == "T0" else a2row["samples"][0]["baseCreateLogicalReadBytes"]),
                     "exact T2 virtual stream logical-read scope")
                finite(s["cpuMilliseconds"])
                finite(s["wallMilliseconds"])
            median[lane+"cpu"] = statistics.median(s["cpuMilliseconds"] for s in lane_samples[1:])
            median[lane+"wall"] = statistics.median(s["wallMilliseconds"] for s in lane_samples[1:])
        for value, name in ((median["T0cpu"],"medianT0CpuMs"),
                            (median["T2cpu"],"medianT2CpuMs"),
                            (median["T0wall"],"medianT0WallMs"),
                            (median["T2wall"],"medianT2WallMs")):
            need(math.isclose(value,r[name],rel_tol=1e-12,abs_tol=1e-7),
                 "independent original sample median "+name)
        need(median["T0cpu"] > 0 and median["T0wall"] > 0,
             "nonzero T0 process CPU/wall reference")
        cpu, wall = median["T2cpu"]/median["T0cpu"], median["T2wall"]/median["T0wall"]
        need(math.isclose(cpu,r["t2OverT0Cpu"],rel_tol=1e-12) and
             math.isclose(wall,r["t2OverT0Wall"],rel_tol=1e-12),
             "recomputed T2/T0 ratios")
        gate = "PASS_A3A_CREATE_GUARD" if cpu <= 3 and wall <= 3 and median["T2wall"] <= 60000 else "FAIL_A3A_CREATE_GUARD"
        need(r["createCostGate"] == gate, "original preregistered CPU/wall verdict")
        passing += gate.startswith("PASS")
        raw_count += len(samples)
        rows.append((family,bv,tv,r["t0WarmBytes"],r["t2WarmBytes"],
                     round(median["T0cpu"],4),round(median["T2cpu"],4),
                     round(median["T0wall"],4),round(median["T2wall"],4),
                     round(cpu,9),round(wall,9),gate))
    need(raw_count == 40 and passing == 2 and
         sum(row[3] for row in rows) == result["t0FullWarmBytes"] == 14758982 and
         sum(row[4] for row in rows) == result["t2FullWarmBytes"] == 10828279 and
         result["aggregateCreateCostGate"] == "A3A_CREATE_COST_GATE_FAIL__T2_DEFER",
         "40 samples, 2 PASS / 2 FAIL and frozen aggregate DEFER")
    return rows

def verify_live(run,artifact,a2):
    need(run["id"] == RUN and run["head_sha"] == SOURCE and
         run["event"] == "push" and run["run_attempt"] == 1 and
         run["status"] == "completed" and run["conclusion"] == "success" and
         run["head_branch"] == "main" and
         run["path"].split("@")[0] == ".github/workflows/patch-tree-001-t2a3a-original.yml" and
         run["repository"]["full_name"] == "definitely-stable/ChunkShift",
         "live GitHub first-main original experiment")
    need(artifact["id"] == ARTIFACT and
         artifact["name"] == "patch-tree-t2a3a-original-paired-create-NO-ADOPT" and
         artifact["size_in_bytes"] == ZIP_SIZE and artifact["expired"] is False and
         artifact["workflow_run"]["id"] == RUN and
         artifact["workflow_run"]["head_sha"] == SOURCE,
         "live GitHub original artifact identity")
    need(a2["id"] == PREV_RUN and a2["head_sha"] == PREV_SHA and
         a2["event"] == "push" and a2["run_attempt"] == 1 and
         a2["status"] == "completed" and a2["conclusion"] == "success",
         "original A2 parent GitHub provenance")

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--zip",type=Path)
    p.add_argument("--run-json",type=Path)
    p.add_argument("--artifact-json",type=Path)
    p.add_argument("--a2-json",type=Path)
    args = p.parse_args()
    if args.zip:
        rows = audit(zip_records(args.zip))
        with Path(__file__).with_name("census.tsv").open(newline="",encoding="utf-8") as f:
            committed = list(csv.reader(f,delimiter="\t"))
        expected = [list(("family","base_version","target_version","t0_warm_b","t2_warm_b",
                          "t0_cpu_ms","t2_cpu_ms","t0_wall_ms","t2_wall_ms",
                          "cpu_ratio","wall_ratio","gate"))] + [[str(x) for x in r] for r in rows]
        need(committed == expected, "committed four-pair machine census")
        print("PASS: 4 source-locked original pairs, 40 samples, 2 PASS / 2 FAIL")
    if any((args.run_json,args.artifact_json,args.a2_json)):
        need(args.zip is not None and all((args.run_json,args.artifact_json,args.a2_json)),
             "live GitHub evidence requires original ZIP and all 3 metadata files")
        verify_live(*[decode(x.read_bytes()) for x in
                      (args.run_json,args.artifact_json,args.a2_json)])
    print("T2_DEFER: first-release A3A create gate failed; no signed source locator,")
    print("private RSS, complete pipeline or independent raw CSP re-decode.")

if __name__ == "__main__":
    try: main()
    except (ValueError,KeyError,TypeError,OSError,UnicodeError,AssertionError,zipfile.BadZipFile) as exc:
        print("FAIL independent T2A3A audit:",exc,file=sys.stderr)
        sys.exit(1)
