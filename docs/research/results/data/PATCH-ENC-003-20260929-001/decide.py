#!/usr/bin/env python3
"""Recompute PATCH-ENC-003/EVIDENCE-20260929-001 from memory.json and encoder.json
by the frozen rule of docs/benchmarks/PATCH-ENC-003-PROTOCOL.md sections 4 and 5.
Standard library only."""

import json
import statistics
from pathlib import Path

HERE = Path(__file__).parent
LIMIT_MIB = 64.0
SIZE_BOUND = 1.03
TIME_BOUND = 1.25
D_SPREAD = 0.25
CANDIDATES = ("P0", "P1", "P2", "P3")
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")

memory = json.loads((HERE / "memory.json").read_text(encoding="utf-8"))
encoder = json.loads((HERE / "encoder.json").read_text(encoding="utf-8"))
files = memory["files"]

print("== memory: M (worst create) and A (worst apply) over idle, MiB")
print("cand  platform     M      median  files>64  A     worst create file")
M, A = {}, {}
for run in memory["runs"]:
    c, p = run["candidate"], run["platform"]
    create, apply = run["createExcessMiB"], run["applyExcessMiB"]
    assert len(create) == len(apply) == len(files)
    M[c, p], A[c, p] = max(create), max(apply)
    worst = files[create.index(M[c, p])]
    print(f"{c:5} {p:11} {M[c, p]:5.1f}  {statistics.median(create):6.1f}  {sum(v > LIMIT_MIB for v in create):8d}"
          f"  {A[c, p]:4.1f}  {worst}")
assert {(c, p) for c in CANDIDATES for p in PLATFORMS} == set(M)

platforms = {item["platform"]: item["lanes"] for item in encoder["platforms"]}
x64, arm64 = platforms["linux-x64"], platforms["linux-arm64"]
assert [lane["id"] for lane in x64] == ["D", "P0", "P1", "P2", "P3", "D"]
for mine, theirs in zip(x64, arm64):
    same = mine["patchesSha256"] == theirs["patchesSha256"]
    print(f"patchesSha256 {mine['id']:2} x64 == arm64: {same}")
    assert mine["lane"] == theirs["lane"]

d1, d2 = x64[0], x64[-1]
assert d1["patchesSha256"] == d2["patchesSha256"]
spread = abs(d1["totalCreateSeconds"] - d2["totalCreateSeconds"]) / min(d1["totalCreateSeconds"], d2["totalCreateSeconds"])
valid = spread <= D_SPREAD
t_d = (d1["totalCreateSeconds"] + d2["totalCreateSeconds"]) / 2
b_cal_d, b_hold_d = d1["calibration"]["bytes"], d1["holdout"]["bytes"]
print(f"\n== encoder (x64): D lanes differ by {spread:.1%} in time (valid: {valid}); T(D) = {t_d:.2f} s")
print("cand  B_cal     vs D      B_hold    vs D      T         vs T(D)  memory size time  eligible")

eligible = {}
for lane in x64[1:-1]:
    c = lane["id"]
    b_cal, b_hold, t = lane["calibration"]["bytes"], lane["holdout"]["bytes"], lane["totalCreateSeconds"]
    rule1 = all(M[c, p] <= LIMIT_MIB and A[c, p] <= LIMIT_MIB for p in PLATFORMS)
    rule2 = b_cal <= SIZE_BOUND * b_cal_d and b_hold <= SIZE_BOUND * b_hold_d
    rule3 = t <= TIME_BOUND * t_d
    if rule1 and rule2 and rule3:
        eligible[c] = (b_cal + b_hold, max(M[c, p] for p in PLATFORMS))
    print(f"{c:5} {b_cal:9d} {b_cal / b_cal_d - 1:+8.4%} {b_hold:9d} {b_hold / b_hold_d - 1:+8.4%}"
          f" {t:8.2f} {t / t_d:7.3f}x  {rule1!s:6} {rule2!s:4} {rule3!s:5} {c in eligible}")

memory_ok = [c for c in CANDIDATES if all(M[c, p] <= LIMIT_MIB and A[c, p] <= LIMIT_MIB for p in PLATFORMS)]
if not valid:
    verdict = "INVALID RUN (repeat once)"
elif eligible:
    winner = min(eligible, key=lambda c: eligible[c])
    lane = next(item["lane"] for item in x64 if item["id"] == winner)
    verdict = f"ADOPT {winner} ({lane})"
elif memory_ok:
    verdict = "DEFER"
else:
    verdict = "REJECT"
print(f"\nverdict: {verdict}")
