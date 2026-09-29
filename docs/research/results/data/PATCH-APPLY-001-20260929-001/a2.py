#!/usr/bin/env python3
"""Recompute rule A2 of PATCH-APPLY-001/EVIDENCE-20260929-001 from memory.json
(docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md section 3). Standard library only."""

import json
import statistics
from pathlib import Path

LIMIT_MIB = 64.0
doc = json.loads(Path(__file__).with_name("memory.json").read_text(encoding="utf-8"))
files = doc["files"]

print("platform     idle  create worst  median  files>64  apply worst  worst create file")
holds = True
for run in doc["runs"]:
    create, apply = run["createExcessMiB"], run["applyExcessMiB"]
    assert len(create) == len(apply) == len(files)
    worst = files[create.index(max(create))]
    over = sum(v > LIMIT_MIB for v in create) + sum(v > LIMIT_MIB for v in apply)
    holds &= over == 0
    print(f"{run['platform']:11} {run['idleMiB']:5.1f} {max(create):13.1f} {statistics.median(create):7.1f}"
          f" {sum(v > LIMIT_MIB for v in create):9d} {max(apply):12.1f}  {worst}")
print(f"\nA2: {'ADOPT' if holds else 'DEFER'}")
