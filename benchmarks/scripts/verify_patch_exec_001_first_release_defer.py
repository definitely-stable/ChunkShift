#!/usr/bin/env python3
"""Recheck source-locked retained G2-A measurements and release DEFER.
Original ZIP replay is a separate command; this validates retained data only.
"""
import argparse
import copy
import csv
import hashlib
import io
import json
import math
import re
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEDGER = "benchmarks/experiments/patch-exec-001-first-release-defer-20261117.json"
REPORT = "docs/research/results/PATCH-EXEC-001-FIRST-RELEASE-DEFER-20261117-001.md"
SAMPLES = "docs/research/results/data/PATCH-EXEC-001-G2A-20261015-001/samples.tsv"
IDENTITIES = "docs/research/results/data/PATCH-EXEC-001-G2A-20261015-001/patch-identities.tsv"
G2 = "docs/research/results/PATCH-EXEC-001-G2A-EVIDENCE-20261015-001.md"
LOCKS = {SAMPLES: "d51e40dd63f8d172076ec485a668ae9893adef7d",
         IDENTITIES: "88f73f2e68d8e79dd21c01fab1f86bb2273fba37",
         G2: "b2e82cf40b20450aed7e2f1b9ec337dec32fda10"}
CASES = ("f1-h0", "f1-h2w2", "f2-h0", "f2-h2w2", "f4-h0", "f4-h2w2")
MODES = ("warm-csm", "cold-csm")

def need(condition, message):
    if not condition:
        raise ValueError(message)

def blob_hash(b):
    return hashlib.sha1(b"blob " + str(len(b)).encode() + b"\0" + b).hexdigest()

def read_tsv(path):
    return list(csv.DictReader(io.StringIO(path.read_text(encoding="utf-8"),
                                          newline=""), delimiter="\t"))

def check(d, rows, identities, registry, index, report):
    need(d.get("schema") == "chunkshift.patch-exec-001.first-release-defer.v1",
         "ledger schema")
    need(d.get("experimentId") == "PATCH-EXEC-001" and
         d.get("firstReleaseDecision") == "DEFER" and
         d.get("adoption") == "NO_PRODUCTION_ADOPT", "release decision")
    need(d.get("preserveCspV1") is True and d.get("preserveH2W2Default") is True,
         "preserved product")
    for field in ("adaptivePolicyTrained", "adaptivePolicyBenchmarked",
                  "taskResultCacheBenchmarked", "freshG2BValidationOpened",
                  "sealedHoldoutOpened"):
        need(d.get(field) is False, "unsupported claim: " + field)
    src = d["source"]
    need(src["originalGithubRunId"] == 38677655592 and
         src["originalMainSha"] == "36ba15fd434a5f5abe365c269839394433b4e44b",
         "locked source run")
    need(src["warmArtifact"]["id"] == 11947765369 and
         src["warmArtifact"]["zipSha256"] ==
         "af04b7cef1438168f785ac27827816a526a80870ceced9c255688434e55c6276",
         "warm original ZIP")
    need(src["coldArtifact"]["id"] == 11947710230 and
         src["coldArtifact"]["zipSha256"] ==
         "7c5a4ab2288901aea028110fd5d8496d08ae77eb68b50754a40d52b23e00cc17",
         "cold original ZIP")
    need(src["warmArtifact"]["cpu"] != src["coldArtifact"]["cpu"],
         "separate CPU hosts")
    need((src["originalZipsRechecked"], src["zipMembersEach"],
          src["sha256sEntriesEach"], src["h0ApplyVerifiedFilesPerHost"],
          src["observedCspIdentityMatches"], src["sampleCount"]) ==
         (2, 35, 34, 16, 960, 60), "evidence population")
    need(len(rows) == 60, "exactly 60 original rows")
    keys = [(r.get("mode"), r.get("round"), r.get("allocation")) for r in rows]
    expected = {(m, str(i), c) for m in MODES for i in range(5) for c in CASES}
    need(len(set(keys)) == 60 and set(keys) == expected, "round/allocation schedule")
    for row in rows:
        for name in ("wall_seconds", "process_cpu_seconds",
                     "managed_alloc_bytes", "process_lifetime_peak_ws_bytes"):
            try:
                x = float(row[name])
            except (ValueError, TypeError, KeyError) as exc:
                raise ValueError("invalid metric " + name) from exc
            need(math.isfinite(x) and x > 0, "nonfinite/negative metric " + name)
    need(len(identities) == 16, "physical identity population")
    observed = set()
    families = {}
    for r in identities:
        key = tuple(r.get(k) for k in ("family", "base", "target", "path"))
        need(all(isinstance(k, str) and k for k in key), "missing identity")
        need(r["base"] == "10.0.10" and r["target"] == "10.0.11",
             "training source versions")
        need(key not in observed, "duplicate physical identity")
        observed.add(key)
        families[r["family"]] = families.get(r["family"], 0) + 1
        need(re.fullmatch("[0-9a-f]{64}", r.get("patch_sha256", "")) is not None,
             "physical digest")
    need(families == {"dotnet-aspnetcore-win-x64": 8,
                      "dotnet-runtime-linux-arm64": 8}, "family population")
    result = {}
    for mode in MODES:
        m = {}
        for c in ("f1-h2w2", "f2-h2w2", "f4-h2w2"):
            subset = [r for r in rows if r["mode"] == mode and
                      r["allocation"] == c]
            m[c] = (statistics.median(float(r["wall_seconds"]) for r in subset),
                    statistics.median(float(r["process_cpu_seconds"]) for r in subset),
                    max(int(r["process_lifetime_peak_ws_bytes"]) for r in subset))
        a, b, c = m["f1-h2w2"], m["f2-h2w2"], m["f4-h2w2"]
        need(c[0] < b[0] < a[0], "fixed candidate order")
        need(0 < 1 - c[0] / a[0] < .25, "not a 25% adaptive comparison")
        need(c[1] > a[1] and c[2] > a[2], "process resource cost")
        lane = d["lanes"][mode]
        for n, values in (("h2w2F1", a), ("h2w2F2", b), ("h2w2F4", c)):
            for key, val in (("WallMedianSeconds", values[0]),
                             ("CpuMedianSeconds", values[1]),
                             ("MaxLifetimeWorkingSetBytes", values[2])):
                field = n + key
                if field in lane:
                    need(math.isclose(lane[field], val, rel_tol=0, abs_tol=1e-7),
                         "source sample mismatch " + field)
        result[mode] = round(100*(1-c[0]/a[0]), 4)
    reg = [x for x in registry.get("experiments", [])
           if x.get("id") == "PATCH-EXEC-001"]
    need(len(reg) == 1 and reg[0].get("status") == "DEFER" and
         reg[0].get("firstReleaseEvidence") == REPORT, "registry")
    lines = [line for line in index.splitlines()
             if line.startswith("| PATCH-EXEC-001 |")]
    need(len(lines) == 1 and "| DEFER " in lines[0], "experiment index")
    need("DEFER / NO_PRODUCTION_ADOPT" in report and "960/960" in report,
         "report lacks decision or evidence")
    return result

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--repo-root", type=Path, default=ROOT)
    p.add_argument("--self-test", action="store_true")
    opt = p.parse_args()
    root = opt.repo_root
    for name, expected in LOCKS.items():
        need(blob_hash((root/name).read_bytes()) == expected,
             "historical G2-A source blob changed: " + name)
    data = [json.loads((root/LEDGER).read_text(encoding="utf-8")),
            read_tsv(root/SAMPLES), read_tsv(root/IDENTITIES),
            json.loads((root/"benchmarks/experiments/research-registry.v1.json").read_text(encoding="utf-8")),
            (root/"docs/research/EXPERIMENT-INDEX.md").read_text(encoding="utf-8"),
            (root/REPORT).read_text(encoding="utf-8")]
    results = check(*data)
    tests = 0
    if opt.self_test:
        mutants = [
            lambda d: d[1].pop(),
            lambda d: d[1].__setitem__(1, dict(d[1][0])),
            lambda d: d[1][0].__setitem__("wall_seconds", "NaN"),
            lambda d: d[2][0].__setitem__("patch_sha256", "bad"),
            lambda d: d[2][1].update(d[2][0]),
            lambda d: d[0].__setitem__("firstReleaseDecision", "ADOPT"),
            lambda d: d[3]["experiments"].__setitem__(
                next(i for i,x in enumerate(d[3]["experiments"]) if x["id"] == "PATCH-EXEC-001"),
                {**next(x for x in d[3]["experiments"] if x["id"] == "PATCH-EXEC-001"), "status": "ADOPT"})
        ]
        for change in mutants:
            copy_data = copy.deepcopy(data)
            change(copy_data)
            try:
                check(*copy_data)
            except ValueError:
                tests += 1
            else:
                raise AssertionError("adversarial mutant incorrectly accepted")
        need(tests == 7, "mutation test count")
    print(json.dumps({"result": "PASS", "decision": "DEFER",
                      "verifiedRows": 60, "verifiedIdentities": 16,
                      "rejectedMutations": tests, "fixedOnlyWallImprovementPercent": results},
                     indent=2))

if __name__ == "__main__":
    main()
