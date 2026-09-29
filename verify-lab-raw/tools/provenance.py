"""Fail-closed provenance checks over the raw CORE-VERIFY-002 run evidence.

Usage: provenance.py <raw-dir> <commit> <local-decision.json>
Exits 1 on the first failed check; prints every check it passed.
"""
import json, pathlib, sys

raw, commit, local_decision = pathlib.Path(sys.argv[1]), sys.argv[2], pathlib.Path(sys.argv[3])
platforms = {"linux-x64", "linux-arm64", "win-x64"}
passed = []


def check(ok, what):
    if not ok:
        print("FAILED:", what)
        sys.exit(1)
    passed.append(what)


docs = {p.name: json.loads(p.read_text()) for p in sorted(raw.glob("run-*.json"))}
check(len(docs) == 6, f"six run documents ({len(docs)})")
for kind in ("single", "tree"):
    check({f"run-{kind}-{p}.json" for p in platforms} <= set(docs), f"one {kind} document per platform")
for name, d in docs.items():
    platform = name.split("-", 2)[2].removesuffix(".json")
    check(d["experimentId"] == "CORE-VERIFY-002", f"{name}: experimentId")
    check(d["schema"] == "chunkshift.verify-lab-run.v2", f"{name}: schema")
    check(d["smoke"] is False, f"{name}: not smoke")
    check(d["commit"] == commit, f"{name}: commit {commit[:7]}")
    check(d["platform"] == platform, f"{name}: platform field")
    check(d["runId"].startswith("CORE-VERIFY-002/RUN-") and d["runId"].endswith(f"-{commit[:7]}-{platform}"), f"{name}: RunId {d['runId']}")
    check(all(s["measurement"] is not None for s in d["samples"]), f"{name}: every sample measured")
    check(len({x["runId"] for x in docs.values() if x["platform"] == platform}) == 1, f"{platform}: one RunId string")

for kind in ("single", "tree"):
    lx, la, w = (docs[f"run-{kind}-{p}.json"] for p in ("linux-x64", "linux-arm64", "win-x64"))
    check(lx["planFingerprint"] == la["planFingerprint"], f"{kind}: Linux plan fingerprints equal")
    check(lx["plan"] == la["plan"], f"{kind}: Linux plans equal")
    check(w["plan"] == [g for g in lx["plan"] if g["mode"] != "cold"], f"{kind}: Windows plan = Linux plan without cold groups")
    check(bool(w["skipped"]) and all("cold" in s for s in w["skipped"]) and not lx["skipped"] and not la["skipped"], f"{kind}: only Windows skipped cold")


def digests(d):
    return {x["id"]: (x["bytes"], x["contentDigest"]) for x in d["workloads"]}


for wid in ("S1", "T", "SL"):
    vals = {p: digests(docs[f"run-{'tree' if wid == 'T' else 'single'}-{p}.json"])[wid] for p in platforms}
    if wid == "SL":
        by_size = {}
        for b, dg in vals.values():
            by_size.setdefault(b, set()).add(dg)
        check(all(len(s) == 1 for s in by_size.values()), f"SL digest equal where size equal (sizes {sorted(by_size)})")
    else:
        check(len(set(vals.values())) == 1, f"{wid}: bytes and digest equal on all platforms")

oracles = {p.name: json.loads(p.read_text()) for p in sorted(raw.glob("*-oracle-*.json"))}
check(len(oracles) == 6, f"six oracle reports ({len(oracles)})")
for name, o in oracles.items():
    check(o["passed"] is True and o["quick"] is False and not o["failures"], f"{name}: full oracle passed ({o['cases']} cases, {o['vectors']} vectors)")

ci = json.loads((raw / "decision.json").read_text())
local = json.loads(local_decision.read_text())
check(ci == local, "local verify-lab decide reproduces the CI decision.json")
check(ci["experimentId"] == "CORE-VERIFY-002", "decision experimentId")

for line in passed:
    print("ok:", line)
print(len(passed), "checks passed")
