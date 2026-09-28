#!/usr/bin/env python3
"""Attribution tables of PATCH-APPLY-001/EVIDENCE-20260928-003 from memory-diagnosis.json
(docs/benchmarks/PATCH-APPLY-001-A2-DIAGNOSIS.md sections 3 and 4). Standard library only."""

import json
import statistics
from pathlib import Path

LIMIT = 64.0
doc = json.loads((Path(__file__).with_name("memory-diagnosis.json")).read_text(encoding="utf-8"))
files = doc["files"]

for platform in ("linux-x64", "linux-arm64"):
    e = {r["config"]: r["createExcessMiB"] for r in doc["runs"] if r["platform"] == platform}
    a = {r["config"]: r["applyExcessMiB"] for r in doc["runs"] if r["platform"] == platform}
    print(f"== {platform}")
    print("config  worst  median  p90  files>64  apply-worst  worst file")
    for config in sorted(e):
        values = e[config]
        worst = max(range(len(values)), key=values.__getitem__)
        p90 = sorted(values)[int(0.9 * len(values))]
        print(f"{config:6} {max(values):6.1f} {statistics.median(values):7.1f} {p90:5.1f}"
              f" {sum(v > LIMIT for v in values):9d} {max(a[config]):12.1f}  {files[worst]}")
    shares = {
        "(a) retention D0-D2": ("D0", "D2"),
        "(b) dictionary K=4 D2-D5": ("D2", "D5"),
        "(b) dictionary K=2 D4-D5": ("D4", "D5"),
        "zstd without dictionary D5-D6": ("D5", "D6"),
        "(c) managed D2-D7": ("D2", "D7"),
    }
    for name, (x, y) in shares.items():
        diff = [p - q for p, q in zip(e[x], e[y])]
        print(f"  {name:32} median {statistics.median(diff):6.1f}  min {min(diff):6.1f}  max {max(diff):6.1f}")
    worst = max(range(len(files)), key=e["D2"].__getitem__)
    rule = ("1 (a) suffices" if max(e["D2"]) <= LIMIT else
            "2 (b) at K = 4" if max(e["D4"]) <= LIMIT else
            "3 pipeline without zstd" if max(e["D6"]) > LIMIT else "4 (b) at every K >= 2")
    print(f"  rule: {rule}; (c) worst-file D2-D7 = {e['D2'][worst] - e['D7'][worst]:.1f} MiB ({files[worst]})")
