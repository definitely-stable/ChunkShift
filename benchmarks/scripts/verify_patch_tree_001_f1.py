#!/usr/bin/env python3
"""PATCH-TREE-001 F1: independent structure/accounting replay; NOT a codec oracle."""
import argparse
import hashlib
import json
from pathlib import Path

PAIRS_HASH = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
SCHEMA = "chunkshift.patch-tree-t1-physical-f1.v1"
OBS_SCHEMA = "chunkshift.patch-tree-t1-physical-f1a.v1"
STATUS = "CALIBRATION_ONLY_PHYSICAL_TRACE_NOT_DECISION"
OBS_STATUS = "SYNTHETIC_CONTRACT_OR_CALIBRATION_ONLY_NOT_DECISION"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def filename(family, base, target, path, lane):
    identity = "\0".join((family, base, target, path, lane))
    return hashlib.sha256(identity.encode("utf-8")).hexdigest() + ".json"


def verify(summary_path, calibration_path, pairs_path, directory):
    summary = load(summary_path)
    cal = load(calibration_path)
    original_bytes = Path(pairs_path).read_bytes()
    require(hashlib.sha256(original_bytes).hexdigest() == PAIRS_HASH, "frozen pairs SHA")
    frozen = json.loads(original_bytes)
    require(summary.get("schema") == SCHEMA and summary.get("status") == STATUS,
            "F1 summary schema/status")
    require(summary.get("pairsSha256") == PAIRS_HASH, "summary lock")
    require(cal.get("schema") == "chunkshift.patch-tree-t0-t1-calibration.v1" and
            cal.get("status") == "CALIBRATION_ONLY_NOT_DECISION" and
            cal.get("pairsSha256") == PAIRS_HASH, "calibration lock")
    require(summary.get("runId") == cal.get("runId"), "run identity mismatch")
    pairs = cal["pairs"]
    require(len(pairs) == summary["pairCount"] == 4, "pair cardinality")
    allowed = {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64"}
    frozen_pairs = [
        (x["family"], x["base"], x["target"]) for x in frozen["pairs"]
        if x["family"] in allowed
    ]
    observed_pairs = [(x["family"], x["baseVersion"], x["targetVersion"]) for x in pairs]
    require(observed_pairs == frozen_pairs, "pair order/identity changed")
    rows = summary["files"]
    require(len(rows) == 2102, "full T0/T1 file ledger count")
    index = 0
    observed = set()
    changed = added = raw_added = t0 = t1 = accepted = 0
    t0_warm = t1_warm = 0
    for pair in pairs:
        t0_warm += pair["t0WarmPhysicalBytes"]
        t1_warm += pair["t1WarmPhysicalBytes"]
        for f in pair["files"]:
            require(f["kind"] in ("changed", "added"), "unsupported file kind")
            changed += f["kind"] == "changed"
            added += f["kind"] == "added"
            for lane in ("T0", "T1"):
                row = rows[index]
                index += 1
                size = f["t0Bytes"] if lane == "T0" else f["t1Bytes"]
                sha = f["t0PatchSha256"] if lane == "T0" else f["t1PatchSha256"]
                if lane == "T0":
                    t0 += size
                else:
                    t1 += size
                expect = (pair["family"], pair["baseVersion"],
                          pair["targetVersion"], f["path"], f["kind"], lane)
                actual = tuple(row[key] for key in (
                    "family", "baseVersion", "targetVersion", "path", "kind", "lane"))
                require(expect == actual, "file identity/order mismatch")
                require(row["physicalBytes"] == size and row["publicPatchSha256"] == sha,
                        "physical bytes or CSP SHA mismatch")
                if f["kind"] == "added" and lane == "T0":
                    require(sha is None and row["observationFile"] is None and
                            row["observationSha256"] is None and size == f["targetBytes"],
                            "added T0 is full raw file, not a CSP")
                    raw_added += size
                    continue
                name = filename(*expect[:4], lane)
                require(row["observationFile"] == name and name not in observed,
                        "missing/duplicate observation")
                observed.add(name)
                raw = (Path(directory) / name).read_bytes()
                require(hashlib.sha256(raw).hexdigest() == row["observationSha256"],
                        "observation SHA mismatch")
                o = json.loads(raw)
                label = "/".join((pair["family"],
                                  pair["baseVersion"] + "->" + pair["targetVersion"],
                                  f["path"]))
                require(o["schema"] == OBS_SCHEMA and o["status"] == OBS_STATUS and
                        o["lane"] == lane and o["sourceLabel"] == label,
                        "observation provenance mismatch")
                require(o["publicPatchSha256"] == o["tracedPatchSha256"] == sha and
                        o["physicalBytes"] == o["reconciledPhysicalBytes"] == size,
                        "public/traced parity")
                require(o["payloadEntries"] == len(o["entries"]), "entry population")
                require(o["storedRawBytes"] + o["storedZstdBytes"] +
                        o["payloadMetadataBytes"] + o["indexBytes"] +
                        o["otherPhysicalBytes"] == size, "wire sum")
                require(o["selectedDictionaryEntries"] ==
                        sum(x["dictionaryRefs"] > 0 for x in o["entries"]),
                        "selected dictionary count")
                require(o["candidateTrials"] >= 0 and o["otherPhysicalBytes"] >= 0,
                        "invalid physical metadata")
                for e in o["entries"]:
                    chosen = [c for c in e["candidates"] if c["selected"]]
                    require(e["storedBytes"] > 0 and e["targetLength"] > 0 and
                            len(chosen) == int(e["dictionaryRefs"] > 0) and
                            (e["selectedCandidate"] is not None) == bool(chosen),
                            "invalid selected candidate entry")
                    if chosen:
                        require(chosen[0]["ordinal"] == e["selectedCandidate"],
                                "candidate ordinal mismatch")
                accepted += size
    require(changed == summary["changedCount"] == 1049 and
            added == summary["addedCount"] == 2, "frozen population")
    require(summary["rawAddedBytes"] == raw_added and
            summary["reconciledObservations"] == accepted, "archive totals")
    require(summary["t0PhysicalPayloadBytes"] == t0 ==
            sum(p["t0PayloadBytes"] for p in pairs), "T0 bytes")
    require(summary["t1PhysicalPayloadBytes"] == t1 ==
            sum(p["t1PayloadBytes"] for p in pairs), "T1 bytes")
    require(summary["t0WarmBytes"] == t0_warm and
            summary["t1WarmBytes"] == t1_warm and
            t0_warm - t0 == t1_warm - t1, "shared tree metadata")
    require({p.name for p in Path(directory).glob("*.json")} == observed,
            "unexpected/missing F1 sidecars")
    return {"pairs": 4, "changed": changed, "added": added,
            "observations": len(observed), "rawAddedBytes": raw_added,
            "t0PayloadBytes": t0, "t1PayloadBytes": t1,
            "t0WarmBytes": t0_warm, "t1WarmBytes": t1_warm,
            "disposition": "STRUCTURAL_REPLAY_ONLY_NO_CAUSAL_VERDICT"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--summary", required=True)
    parser.add_argument("--calibration", required=True)
    parser.add_argument("--pairs", required=True)
    parser.add_argument("--observations", required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.summary, args.calibration,
                            args.pairs, args.observations), sort_keys=True))


if __name__ == "__main__":
    main()
