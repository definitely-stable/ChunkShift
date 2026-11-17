#!/usr/bin/env python3
"""Independent F2 original calibration physical-ledger replay, not a codec oracle.

Requires full source-locked original calibration.json plus exactly 1051 SHA-bound
real CSP analysis sidecars. No materialization or sealed holdout is performed.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

# PATCH-TREE-001 F2 first-attempt original-data replay must share the frozen,
# fully-qualified 64-digit source lock with the already accepted T0/T1 verifier.
# The original F2 run #38888000986 computed all 1051 CSPs but was rejected by
# a transcription error in the former 63-digit literal. Keep the source of
# truth in the preregistered corpus lock, never a copied partial digest.
CORPUS_LOCK = Path(__file__).resolve().parents[2] / "docs/benchmarks/patch-corpus/corpus-lock.json"
LOCK = json.loads(CORPUS_LOCK.read_text(encoding="utf-8"))["pairsSha256"]
if re.fullmatch(r"[0-9a-f]{64}", LOCK) is None:
    raise ValueError("F2 frozen original pairs digest must be 64 lowercase hex digits")
SCHEMA = "chunkshift.patch-tree-f2-physical-calibration.v1"
STATUS = "CALIBRATION_ONLY_PHYSICAL_NOT_DECISION_NO_ADOPT"
OBS_SCHEMA = "chunkshift.patch-tree-f2-physical-foundation.v1"
OBS_STATUS = "SYNTHETIC_MECHANICS_OR_UNSEALED_CALIBRATION_ONLY_NO_ADOPT"
BASE = (14_758_982, 83_593_822)


def need(ok, explanation):
    if not ok:
        raise ValueError(explanation)


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def filename(family, base, target, path):
    return hashlib.sha256("\0".join([family, base, target, path]).encode("utf-8")).hexdigest() + ".json"


def validate_lane(lane):
    wire = ("rawStoredBytes", "zstdStoredBytes", "payloadMetadataBytes", "indexBytes", "otherPhysicalBytes")
    need(lane.get("exactApplyVerified") is True, "F2 apply verification missing")
    size = lane["physicalBytes"]
    need(type(size) is int and size > 0 and size == lane["reconciledPhysicalBytes"],
         "F2 CSP length parity")
    need(all(type(lane[x]) is int and lane[x] >= 0 for x in wire), "negative wire component")
    need(sum(lane[x] for x in wire) == size, "F2 physical byte conservation")
    need(type(lane["payloadEntries"]) is int and lane["payloadEntries"] >= 0,
         "invalid F2 payload count")
    need(all(type(lane[x]) in (int, float) and lane[x] >= 0
             for x in ("createMilliseconds", "applyMilliseconds")), "negative execution duration")
    need(isinstance(lane["cspSha256"], str) and
         len(lane["cspSha256"]) == 64 and
         all(c in "0123456789abcdef" for c in lane["cspSha256"]), "invalid physical CSP digest")


def validate_observation(pair, file, row, obs):
    need(obs.get("schema") == OBS_SCHEMA and obs.get("status") == OBS_STATUS,
         "F2 sidecar schema/status")
    need(obs.get("targetSha256") == file["targetSha256"], "F2 target hash")
    order = (["t0-public"] if file["kind"] == "changed" else []) + [
        "t1-public", "t1-h0-parity", "t1-relative-local", "t1-no-dictionary"]
    lanes = obs.get("lanes")
    need(isinstance(lanes, list) and [x["name"] for x in lanes] == order,
         "F2 fixed physical lanes")
    for lane in lanes:
        validate_lane(lane)
    by_name = {x["name"]: x for x in lanes}
    t1, h0 = by_name["t1-public"], by_name["t1-h0-parity"]
    need(t1["physicalBytes"] == file["t1Bytes"] ==
         row["t1PhysicalBytes"] and
         t1["cspSha256"] == file["t1PatchSha256"] and
         t1["cspSha256"] == h0["cspSha256"] and
         t1["physicalBytes"] == h0["physicalBytes"], "F2 same-build public/H0 parity")
    need(t1["baseManifestId"] == pair["oldTreeManifestId"] and
         len({x["baseManifestId"] for x in lanes if x["name"] != "t0-public"}) == 1,
         "F2 virtual BASE manifest was changed")
    need(len({x["targetManifestId"] for x in lanes}) == 1,
         "F2 target CSM was changed")
    if file["kind"] == "changed":
        t0 = by_name["t0-public"]
        need(t0["physicalBytes"] == file["t0Bytes"] ==
             row["t0PhysicalBytes"] and
             t0["cspSha256"] == file["t0PatchSha256"], "F2 T0 physical CSP parity")
    else:
        need(file["t0PatchSha256"] is None and
             file["t0Bytes"] == file["targetBytes"],
             "F2 added path T0 must be full raw transport")
    need(row["relativeLocalPhysicalBytes"] ==
         by_name["t1-relative-local"]["physicalBytes"] and
         row["noDictionaryPhysicalBytes"] ==
         by_name["t1-no-dictionary"]["physicalBytes"], "F2 alternate physical lane sum")


def replay(summary_file, calibration_file, pairs_file, observation_dir):
    pairs_raw = Path(pairs_file).read_bytes()
    need(hashlib.sha256(pairs_raw).hexdigest() == LOCK, "original pairs SHA lock")
    original = json.loads(pairs_raw)
    cal, summary = load(calibration_file), load(summary_file)
    need(cal.get("schema") == "chunkshift.patch-tree-t0-t1-calibration.v1" and
         cal.get("status") == "CALIBRATION_ONLY_NOT_DECISION" and
         summary.get("schema") == SCHEMA and summary.get("status") == STATUS and
         summary.get("pairsSha256") == cal.get("pairsSha256") == LOCK and
         summary.get("runId") == cal.get("runId"), "F2 provenance/status envelope")
    need(len(cal.get("pairs", [])) == summary.get("pairCount") == 4,
         "missing original calibration pair")
    allowed = {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64"}
    frozen_ids = [(p["family"], p["base"], p["target"]) for p in original["pairs"]
                  if p["family"] in allowed]
    observed_ids = [(p["family"], p["baseVersion"], p["targetVersion"])
                    for p in cal["pairs"]]
    need(frozen_ids == observed_ids, "F2 ordered original transitions")
    folder = Path(observation_dir)
    files = summary["files"]
    idx, changed, added = 0, 0, 0
    warm = {"t0": 0, "t1": 0, "local": 0, "nodict": 0}
    observed = set()
    for pair in cal["pairs"]:
        metadata = pair["targetTreeManifestBytes"]
        for lane in warm:
            warm[lane] += metadata
        for f in pair["files"]:
            need(f["kind"] in ("changed", "added"), "not original changed/added population")
            changed += f["kind"] == "changed"
            added += f["kind"] == "added"
            row = files[idx]
            idx += 1
            identifier = (pair["family"], pair["baseVersion"],
                          pair["targetVersion"], f["path"])
            need(tuple(row[x] for x in ("family", "baseVersion",
                                        "targetVersion", "path")) == identifier and
                 row["kind"] == f["kind"], "F2 source path/row order")
            name = filename(*identifier)
            need(row["observationFile"] == name and name not in observed,
                 "missing or duplicate observation identity")
            observed.add(name)
            raw = (folder / name).read_bytes()
            need(len(raw) <= 1_048_576 and
                 hashlib.sha256(raw).hexdigest() == row["observationSha256"],
                 "invalid F2 sidecar SHA or oversized artifact")
            obs = json.loads(raw)
            validate_observation(pair, f, row, obs)
            warm["t0"] += row["t0PhysicalBytes"]
            warm["t1"] += row["t1PhysicalBytes"]
            warm["local"] += row["relativeLocalPhysicalBytes"]
            warm["nodict"] += row["noDictionaryPhysicalBytes"]
    need(idx == len(files) == 1051 and changed == summary["changedCount"] == 1049 and
         added == summary["addedCount"] == 2, "incomplete original F2 physical population")
    need({p.name for p in folder.iterdir()} == observed,
         "unexpected or missing F2 sidecar")
    need((warm["t0"], warm["t1"]) == BASE and
         warm["t0"] == summary["t0WarmBytes"] and
         warm["t1"] == summary["t1WarmBytes"] and
         warm["local"] == summary["relativeLocalWarmBytes"] and
         warm["nodict"] == summary["noDictionaryWarmBytes"], "F2 exact whole-update physical totals")
    return {"status": "STRUCTURAL_ORIGINAL_F2_REPLAY_PASS_NOT_ADOPT",
            "pairs": 4, "changed": changed, "added": added, "observations": len(observed),
            "t0WarmBytes": warm["t0"], "t1WarmBytes": warm["t1"],
            "relativeLocalWarmBytes": warm["local"],
            "noDictionaryWarmBytes": warm["nodict"]}


def main():
    a = argparse.ArgumentParser(description=__doc__)
    for opt in ("summary", "calibration", "pairs", "observations"):
        a.add_argument("--" + opt, required=True)
    args = a.parse_args()
    print(json.dumps(replay(args.summary, args.calibration, args.pairs,
                            args.observations), sort_keys=True))


if __name__ == "__main__":
    main()
