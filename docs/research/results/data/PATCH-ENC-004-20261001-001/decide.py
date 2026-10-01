#!/usr/bin/env python3
"""Recompute the PATCH-ENC-004 decision (protocol section 7) from the compact
evidence in this directory: totals.json, patches.json, memory.json. Stdlib only.
Exits 1 if the result differs from verdict.json (h1.decision, workers.decision,
workers.adoptedExecution), 2 if the H0 spread makes the run invalid."""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
WORKERS = (1, 2, 4, 8)
H0_SPREAD_LIMIT = 0.25
READ_RATIO_MIN = 2.0
SPEEDUP_MIN = 1.5


def load(name):
    return json.loads((HERE / name).read_text(encoding="utf-8"))


totals = load("totals.json")["platforms"]
patches = load("patches.json")
memory = load("memory.json")["platforms"]
verdict = load("verdict.json")


def seconds(p, lane):
    return totals[p][lane]["createSeconds"]


def t_h0(p):
    return (seconds(p, "h0#1") + seconds(p, "h0#11")) / 2


def bytes_ok(lane):
    # h0 is checked through its second lane (h0#11) against the first
    key = "h0#11" if lane == "h0" else lane
    return all(patches["mismatchCountVsOwnFirstH0"][p][key] == 0 for p in PLATFORMS)


def mem_met(p, lane):
    m = memory[p]["lanes"][lane]
    return m["maxMiB"] <= m["boundMiB"]


print("H0 validity (two lanes within 25 %)")
valid = True
for p in PLATFORMS:
    a, b = sorted((seconds(p, "h0#1"), seconds(p, "h0#11")))
    spread = b / a - 1
    ok = spread <= H0_SPREAD_LIMIT
    valid &= ok
    print(f"  {p:12s} h0#1={seconds(p, 'h0#1'):9.2f}s h0#11={seconds(p, 'h0#11'):9.2f}s "
          f"spread={spread * 100:6.2f}%  {'ok' if ok else 'INVALID'}")

print("\nH1")
# R(H0) is the first H0 lane's base bytes read (h0#1), as the evaluator does
ratios, regress, mem1 = {}, {}, {}
for p in PLATFORMS:
    r0 = totals[p]["h0#1"]["baseBytesRead"]
    r1 = totals[p]["h1"]["baseBytesRead"]
    ratios[p] = r0 / r1 if r1 > 0 else None
    delta = (seconds(p, "h1") / t_h0(p) - 1) * 100
    regress[p] = seconds(p, "h1") <= t_h0(p)
    mem1[p] = mem_met(p, "h1")
    print(f"  {p:12s} R(H0)/R(H1)={ratios[p]:.3f}  T(H1)={seconds(p, 'h1'):9.2f}s "
          f"meanT(H0)={t_h0(p):9.2f}s delta={delta:+6.2f}%  no-regression={regress[p]}  "
          f"M={memory[p]['lanes']['h1']['maxMiB']:.1f}/64 MiB ok={mem1[p]}")
chosen = ratios["linux-x64"]  # evaluator's readRatio platform
ratio_ok = chosen is None or chosen >= READ_RATIO_MIN
h1_ok = bytes_ok("h1") and ratio_ok and all(regress.values()) and all(mem1.values())
h1 = "ADOPT" if h1_ok else "REJECT"
print(f"  bytes identical={bytes_ok('h1')}  read ratio (linux-x64)={chosen:.3f} >= 2: {ratio_ok}")
print(f"  H1 decision: {h1}")

print("\nWorkers (speedup = meanT(H0) / T(lane); qualify: >=1.5 on >=2 platforms, bytes, memory)")
rows = []
for w in WORKERS:
    for fam in ("h3", "h2"):
        lane = f"{fam}-w{w}"
        eligible = fam == "h2" or h1 == "ADOPT"
        sp = {p: t_h0(p) / seconds(p, lane) for p in PLATFORMS}
        fast = sum(1 for s in sp.values() if s >= SPEEDUP_MIN)
        b = bytes_ok(lane)
        m = all(mem_met(p, lane) for p in PLATFORMS)
        q = eligible and b and fast >= 2 and m
        rows.append((lane, q))
        print(f"  {lane:6s} " + " ".join(f"{p}={sp[p]:.2f}" for p in PLATFORMS)
              + f"  fast={fast}/3 bytes={b} mem={m} eligible={eligible} -> {'QUALIFIES' if q else 'no'}")
adopted = next((lane for lane, q in rows if q), None)
workers = "ADOPT" if adopted else "REJECT"
print(f"  Workers decision: {workers}" + (f" ({adopted})" if adopted else ""))

if not valid:
    print("\nRun INVALID (H0 spread > 25 %): no decision.")
    sys.exit(2)

expected = (verdict["h1"]["decision"], verdict["workers"]["decision"], verdict["workers"]["adoptedExecution"])
got = (h1, workers, adopted)
print(f"\nrecomputed: H1 {h1}, workers {workers}, adopted {adopted}")
print(f"verdict.json: H1 {expected[0]}, workers {expected[1]}, adopted {expected[2]}")
if got != expected:
    print("MISMATCH with verdict.json")
    sys.exit(1)
print("agrees with verdict.json")
