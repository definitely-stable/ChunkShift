#!/usr/bin/env python3
"""Print one patch-lab memory result (chunkshift.patch-lab-memory.v1) as text.

The memory job of patch-lab.yml prints it into the job log, so a run's lane,
allocator/GC environment and per-file excess over the idle baseline can be
read without downloading the artifact
(docs/benchmarks/PATCH-APPLY-001-A2-DIAGNOSIS.md).

  print_patch_lab_memory.py <patch-lab-memory.json> [...]
"""

from __future__ import annotations

import json
import statistics
import sys
from pathlib import Path

MIB = 1024 * 1024
SCHEMA = "chunkshift.patch-lab-memory.v1"


def render(document: dict) -> list[str]:
    if document.get("schema") != SCHEMA:
        raise ValueError(f"not a {SCHEMA} document: {document.get('schema')!r}")

    idle = document["idleBaselineBytes"]
    environment = document.get("memoryEnvironment") or {}
    policy = document.get("policy") or {}
    lines = [
        f"runId={document.get('runId')}",
        f"lane={document.get('lane', 'csp')} policy={json.dumps(policy, sort_keys=True)}",
        "memoryEnvironment=" + (" ".join(f"{k}={v}" for k, v in sorted(environment.items())) or "-"),
        f"idle={idle / MIB:.1f} MiB",
        "create_excess_mib\tapply_excess_mib\ttarget_mib\tfile",
    ]
    creates, applies = [], []
    for item in document["files"]:
        create = (item["createPeakBytes"] - idle) / MIB
        apply = (item["applyPeakBytes"] - idle) / MIB
        creates.append(create)
        applies.append(apply)
        lines.append(
            f"{create:.1f}\t{apply:.1f}\t{item['targetSize'] / MIB:.1f}\t"
            f"{item['family']} {item['base']}->{item['target']} {item['path']}")
    if creates:
        lines.append(
            f"files={len(creates)} create worst={max(creates):.1f} median={statistics.median(creates):.1f} "
            f"over64={sum(value > 64 for value in creates)} apply worst={max(applies):.1f}")
    else:
        lines.append("files=0")
    return lines


def main(arguments: list[str]) -> int:
    if not arguments:
        print(__doc__.strip().splitlines()[-1].strip(), file=sys.stderr)
        return 2
    for path in arguments:
        document = json.loads(Path(path).read_text(encoding="utf-8"))
        print(f"== {path}")
        print("\n".join(render(document)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
