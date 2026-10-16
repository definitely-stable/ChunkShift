#!/usr/bin/env python3
"""Read-only original-three-ZIP F1 evidence replay. NOT a zstd/CDC oracle."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import zipfile

HASHES = {
    "f1": "a3b994d94121efaaa7b4915877c007c63744b065d629d7d02e6991b7628e4a9c",
    "boundary": "2918c70f03bf87e2e3351c52399debfa61c54887a3b9c48b8473c362606a4002",
    "physical": "e5707a7334ebc909486945edaf3eed8832a121b715ba5e9cd1354e2447865812",
}
LOCK = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"


def need(ok, why):
    if not ok:
        raise ValueError(why)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def read(z, member):
    return json.loads(z.read(member))


def verify_hashes(z, member):
    members = set()
    for line in z.read(member).decode("utf-8").splitlines():
        digest, separator, name = line.partition("  ")
        need(separator and len(digest) == 64 and name not in members, "checksum line")
        need(sha(z.read(name)) == digest, "member checksum " + name)
        members.add(name)
    return members


def pk(pair):
    return (pair["family"], pair["baseVersion"], pair["targetVersion"])


def fk(pair, file):
    return (*pk(pair), file["path"])


def replay(inputs, frozen):
    archives = {name: zipfile.ZipFile(path) for name, path in inputs.items()}
    for name, z in archives.items():
        need(sha(Path(inputs[name]).read_bytes()) == HASHES[name], "original ZIP SHA")
        need(z.testzip() is None, "ZIP CRC")
        all_names = z.namelist()
        need(len(all_names) == len(set(all_names)), "duplicate ZIP names")
        root = verify_hashes(z, "SHA256SUMS")
        if name == "f1":
            sidecars = verify_hashes(z, "F1-SHA256SUMS")
            need(len(sidecars) == 2100 and root | sidecars | {"SHA256SUMS"} == set(all_names),
                 "missing/extra original sidecars")
        else:
            need(root | {"SHA256SUMS"} == set(all_names), "missing/extra root files")
        need(sha(z.read("original-pairs.json")) == LOCK, "original frozen pairs")
    a, bb, oldz = (archives[k] for k in ("f1", "boundary", "physical"))
    need(a.read("original-pairs.json") == bb.read("original-pairs.json") ==
         oldz.read("original-pairs.json"), "source pairs mismatch")
    need(a.read("layouts.json") == bb.read("layouts.json") ==
         oldz.read("layouts.json"), "full layouts mismatch")
    s, cal = read(a, "f1-summary.json"), read(a, "calibration.json")
    bounds, old = read(bb, "boundary.json"), read(oldz, "calibration.json")
    prov = read(a, "provenance.json")
    need(prov["commit"] == frozen["f1"]["commit"] and
         prov["runId"] == str(frozen["f1"]["runId"]) and prov["runAttempt"] == "1",
         "run provenance")
    need(prov["pairsReferenceRunId"] == str(frozen["originalPairsRunId"]) and
         prov["runnerEnvironment"] == "github-hosted" and
         prov["gitRef"] == "refs/heads/main", "runner provenance")
    need(s["status"] == "CALIBRATION_ONLY_PHYSICAL_TRACE_NOT_DECISION" and
         s["runId"] == cal["runId"] == prov["resultRunId"] and
         s["pairsSha256"] == LOCK, "result provenance")
    need([pk(p) for p in cal["pairs"]] ==
         [pk(p) for p in bounds["pairs"]] ==
         [pk(p) for p in old["pairs"]], "four original pair identities")
    need(len(s["files"]) == 2102 and s["changedCount"] == 1049 and
         s["addedCount"] == 2, "summary population")
    borders = {fk(p, f): f for p in bounds["pairs"] for f in p["files"]
               if f["kind"] in ("changed", "added")}
    historical = {fk(p, f): f for p in old["pairs"] for f in p["files"]}
    need(len(borders) == len(historical) == 1051, "original file population")
    groups = {k: Counter() for k in ("same", "loss", "gain", "added")}
    components = {"T0": Counter(), "T1": Counter()}
    changes = Counter()
    consumed = set()
    row_id = 0
    for pair in cal["pairs"]:
        for file in pair["files"]:
            k = fk(pair, file)
            border, prev = borders[k], historical[k]
            for prop in ("targetSha256", "targetBytes", "t0PatchSha256",
                         "t1PatchSha256", "t0Bytes", "t1Bytes"):
                need(file[prop] == prev[prop], "historical source/physical parity")
            exact0, exact1 = (border[x]["bytes"] for x in
                              ("targetExactInSamePathOld", "targetExactInVirtualTree"))
            group = ("added" if file["kind"] == "added" else
                     "same" if exact0 == exact1 else
                     "loss" if exact1 < exact0 else "gain")
            g = groups[group]
            g["files"] += 1
            for lane in ("t0", "t1"):
                g[lane + "PhysicalBytes"] += file[lane + "Bytes"]
            g["deltaBytes"] += file["t1Bytes"] - file["t0Bytes"]
            paired = {}
            for lane in ("T0", "T1"):
                row = s["files"][row_id]
                row_id += 1
                size = file["t0Bytes" if lane == "T0" else "t1Bytes"]
                patch_sha = file["t0PatchSha256" if lane == "T0" else "t1PatchSha256"]
                need(tuple(row[x] for x in ("family", "baseVersion", "targetVersion", "path"))
                     == k and row["lane"] == lane and
                     row["physicalBytes"] == size and
                     row["publicPatchSha256"] == patch_sha, "summary row identity")
                if group == "added" and lane == "T0":
                    need(row["observationFile"] is None and
                         size == file["targetBytes"], "T0 added raw control")
                    continue
                identity = "\0".join([*k, lane])
                name = hashlib.sha256(identity.encode("utf-8")).hexdigest() + ".json"
                member = "f1-observations/" + name
                need(row["observationFile"] == name and member not in consumed,
                     "sidecar identity/duplicate")
                consumed.add(member)
                need(sha(a.read(member)) == row["observationSha256"], "sidecar SHA")
                o = read(a, member)
                need(o["publicPatchSha256"] == o["tracedPatchSha256"] == patch_sha,
                     "producer public/traced SHA")
                need(o["physicalBytes"] == o["reconciledPhysicalBytes"] == size,
                     "entry physical total")
                entries = o["entries"]
                need(len(entries) == o["payloadEntries"] and
                     o["selectedDictionaryEntries"] ==
                     sum(e["dictionaryRefs"] > 0 for e in entries), "entry count")
                fields = ("storedRawBytes", "storedZstdBytes",
                          "payloadMetadataBytes", "indexBytes", "otherPhysicalBytes")
                need(sum(o[x] for x in fields) == size, "exact CSP byte sum")
                for field in fields:
                    components[lane][field] += o[field]
                    g[lane + field] += o[field]
                for field in ("selectedDictionaryEntries", "payloadEntries", "candidateTrials"):
                    g[lane + field[0].upper() + field[1:]] += o[field]
                paired[lane] = entries
                for e in entries:
                    selected = [c for c in e["candidates"] if c["selected"]]
                    need(len(selected) == int(e["dictionaryRefs"] > 0) and
                         (e["selectedCandidate"] is not None) == bool(selected),
                         "selected candidate cardinality")
                    if selected:
                        need(selected[0]["ordinal"] == e["selectedCandidate"] and
                             selected[0]["finalFrameBytes"] == e["storedBytes"],
                             "actual selected frame")
            if group == "same":
                t0, t1 = paired["T0"], paired["T1"]
                need([(e["targetIndex"], e["targetChunkId"], e["targetLength"])
                      for e in t0] ==
                     [(e["targetIndex"], e["targetChunkId"], e["targetLength"])
                      for e in t1], "equal-exact target payload set")
                for x, y in zip(t0, t1):
                    transition = x["selectedEncoding"] + " -> " + y["selectedEncoding"]
                    changes[transition] += 1
                    changes["storedDelta:" + transition] += y["storedBytes"] - x["storedBytes"]
                    if x["selectedEncoding"] == y["selectedEncoding"] == "zstd-dictionary":
                        first0 = next(c for c in x["candidates"] if c["selected"])["firstChunkId"]
                        first1 = next(c for c in y["candidates"] if c["selected"])["firstChunkId"]
                        need(first0 != first1, "recorded different selected base first ID")
    need(row_id == 2102 and len(consumed) == 2100, "complete F1 replay")
    need([groups[k]["files"] for k in ("same", "loss", "gain", "added")] ==
         [981, 68, 0, 2], "exact-availability cohorts")
    need(sum(x["deltaBytes"] for x in groups.values()) ==
         frozen["totals"]["t1MinusT0WarmBytes"], "full warm delta")
    for cohort in frozen["cohorts"]:
        g = groups[cohort["cohort"]]
        for name in ("files", "t0PhysicalBytes", "t1PhysicalBytes", "deltaBytes"):
            need(g[name] == cohort[name], "cohort total " + name)
        for lane in ("t0", "t1"):
            if lane not in cohort:
                continue
            for name in ("storedRawBytes", "storedZstdBytes", "payloadMetadataBytes",
                         "indexBytes", "otherPhysicalBytes", "payloadEntries",
                         "selectedDictionaryEntries", "candidateTrials"):
                field = name if name.endswith("Bytes") else name[0].upper() + name[1:]
                need(g[lane.upper() + field] == cohort[lane][name],
                     "cohort field " + name)
    for name, value in frozen["physicalComponentDeltaCspOnly"].items():
        need(components["T1"][name] - components["T0"][name] == value,
             "CSP physical component " + name)
    for transition, observation in frozen["cohortSamePairedTransitions"].items():
        need(changes[transition] == observation["entries"] and
             changes["storedDelta:" + transition] == observation["storedDeltaBytes"],
             "equal-exact encoding transition")
    need(sum(components["T1"].values()) - sum(components["T0"].values()) -
         frozen["totals"]["t0RawAddedControlBytes"] ==
         frozen["totals"]["t1MinusT0WarmBytes"], "raw T0 reconciliation")
    return {"status": "ORIGINAL_ARCHIVES_REPLAY_PASS_NOT_CAUSAL",
            "fileRows": row_id, "observations": len(consumed)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for flag in ("f1", "boundary", "physical", "ledger"):
        parser.add_argument("--" + flag, required=True)
    args = parser.parse_args()
    archives = {k: getattr(args, k) for k in ("f1", "boundary", "physical")}
    expected = json.loads(Path(args.ledger).read_text(encoding="utf-8"))
    print(json.dumps(replay(archives, expected), sort_keys=True))
