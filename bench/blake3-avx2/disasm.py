#!/usr/bin/env python3
"""Summarize Tier-1 JIT listings of the 8-way kernels for base and patched.

Usage: disasm.py <mode> <base-listing> <patched-listing>
Prints DISASM lines (per variant and method) and a DISASMDIFF line for Compress8.
"""

import difflib
import re
import sys


def listings(path):
    try:
        text = open(path, encoding="utf-8", errors="replace").read()
    except FileNotFoundError:
        return {}
    result = {}
    for block in re.split(r"(?=; Assembly listing for method )", text):
        m = re.match(r"; Assembly listing for method [^\n]*:([A-Za-z0-9_]+)\([^\n]*\(([^()]*)\)\s*\n", block)
        if m:
            result.setdefault(m.group(1), []).append((m.group(2), block))
    return {name: blocks[-1] for name, blocks in result.items()}


def instructions(block):
    lines = []
    for line in block.splitlines():
        line = re.sub(r"\s*;.*$", "", line)
        if not line.startswith("       ") or not line.strip() or line.strip().startswith("align"):
            continue
        line = re.sub(r"0x[0-9A-Fa-f]{8,}", "ADDR", line.strip())
        lines.append(re.sub(r"\s+", " ", line))
    return lines


def stats(block):
    base = "rbp" if "; rbp based frame" in block else "rsp"
    stack = r"\[(?:rsp|" + base + r")[^\]]*\]"
    ins = instructions(block)
    stores = [l for l in ins if re.search(r"ymmword ptr " + stack + r", ymm", l)]
    loads = [l for l in ins if re.search(r"ymmword ptr " + stack, l) and l not in stores]
    return len(ins), len(stores), len(loads)


def main():
    mode, base_path, patched_path = sys.argv[1:4]
    found = {"base": listings(base_path), "patched": listings(patched_path)}
    for variant, methods in found.items():
        for name in ("Compress8", "Compress8Avx2"):
            if name in methods:
                tier, block = methods[name]
                count, stores, loads = stats(block)
                print(f"DISASM {mode} {variant} {name} tier={tier.replace(' ', '_')} instr={count} "
                      f"ymm_stack_stores={stores} ymm_stack_loads={loads}")
    base_c8 = found["base"].get("Compress8")
    patched_c8 = found["patched"].get("Compress8")
    if base_c8 and patched_c8:
        diff = [l for l in difflib.unified_diff(instructions(base_c8[1]), instructions(patched_c8[1]), lineterm="", n=0)
                if not l.startswith(("---", "+++", "@@"))]
        verdict = "identical" if not diff else f"different({len(diff)})"
    else:
        verdict = "patched-not-used" if base_c8 else "absent"
    print(f"DISASMDIFF {mode} Compress8 {verdict}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
