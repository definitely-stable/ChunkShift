#!/usr/bin/env python3
"""Summarize run.sh output: median of the per-run medians per (mode, kind, size, variant)."""

import statistics
import sys
from collections import defaultdict


def main() -> int:
    results_path, label, cpu = sys.argv[1], sys.argv[2], sys.argv[3]
    rates = defaultdict(list)
    isa = {}
    failures = []
    disasm = []
    tests = []
    for line in open(results_path, encoding="utf-8"):
        parts = line.split()
        if not parts:
            continue
        if parts[0] == "RESULT":
            _, variant, mode, kind, size, median, *_ = parts
            rates[(mode, kind, int(size), variant)].append(float(median))
        elif parts[0] == "ISA":
            isa[(parts[1], parts[2])] = " ".join(parts[3:])
        elif parts[0] == "MISMATCH":
            failures.append(line.strip())
        elif parts[0] in ("DISASM", "DISASMDIFF"):
            disasm.append(" ".join(parts[1:]))
        elif parts[0] == "TESTS":
            tests.append(" ".join(parts[1:]))

    out = [f"### {label}", "", f"CPU: `{cpu}`", ""]
    for (variant, mode), text in sorted(isa.items()):
        if variant == "base":
            out.append(f"- `{mode}`: `{text}`")
    if tests:
        out += ["", "Upstream tests on the patched source:", *[f"- `{t}`" for t in tests]]
    if disasm:
        out += ["", "Tier-1 disassembly:", *[f"- `{d}`" for d in disasm]]
    if failures:
        out += ["", "**Vector mismatches:**", *[f"- {f}" for f in failures]]
    out += ["", "| mode | kind | size | base GB/s | patched GB/s | patched/base | native GB/s | native/patched |",
            "|---|---|---:|---:|---:|---:|---:|---:|"]
    worst = None
    keys = sorted({(m, k, s) for (m, k, s, _) in rates}, key=lambda x: (x[0], x[1], x[2]))
    for mode, kind, size in keys:
        base = statistics.median(rates[(mode, kind, size, "base")])
        patched = statistics.median(rates[(mode, kind, size, "patched")])
        natives = rates.get((mode, kind, size, "native"))
        ratio = patched / base
        worst = ratio if worst is None else min(worst, ratio)
        flag = " ⚠" if ratio < 0.97 else ""
        native_cells = f"{statistics.median(natives):.2f} | {statistics.median(natives) / patched:.2f}x" if natives else "- | -"
        out.append(f"| {mode} | {kind} | {size // 1024} KiB | {base:.2f} | {patched:.2f} | {ratio:.2f}x{flag} | {native_cells} |")
    if worst is not None:
        out += ["", f"Lowest patched/base ratio: **{worst:.2f}x**", ""]
    print("\n".join(out))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
