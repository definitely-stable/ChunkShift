#!/usr/bin/env python3
"""CORE011-CSM-002/G2-C2 fail-closed lazy staged ManifestId Core builds.

The only allowed destination is a clean detached worktree at a pinned source
blob. This script is a research build transform; it is NEVER run in a branch
checkout and never commits changes to production Core.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path

from core011_csm_g2c_variants import PINNED_BASE_BLOB, SOURCE, git, replace_once

THRESHOLD = 256
WIDTHS = {"baseline": 0, "gate256-stage64": 64, "gate256-stage256": 256}


def transform(source: str, variant: str) -> str:
    if variant not in WIDTHS:
        raise ValueError("Unknown G2-C2 variant")
    if variant == "baseline":
        return source
    width = WIDTHS[variant]
    source = replace_once(source,
        "    private readonly HashSuiteIncrementalHasher _hasher;\n",
        "    private readonly HashSuiteIncrementalHasher _hasher;\n"
        "    private const int G2c2Threshold = 256;\n"
        f"    private const int G2c2Width = {width};\n"
        "    private byte[]? _g2c2Buffer;\n"
        "    private int _g2c2PendingEntries;\n")
    source = replace_once(source,
        "        _hasher.Append(entry);\n        _chunkCount = checked(_chunkCount + 1);\n",
        "        if (_chunkCount < G2c2Threshold)\n"
        "        {\n"
        "            _hasher.Append(entry);\n"
        "        }\n"
        "        else\n"
        "        {\n"
        "            byte[] buffer = _g2c2Buffer ??= new byte[\n"
        "                G2c2Width * (CsmFormat.HashSize + sizeof(uint))];\n"
        "            entry.CopyTo(buffer.AsSpan(\n"
        "                _g2c2PendingEntries * (CsmFormat.HashSize + sizeof(uint)),\n"
        "                CsmFormat.HashSize + sizeof(uint)));\n"
        "            if (++_g2c2PendingEntries == G2c2Width)\n"
        "            {\n"
        "                FlushG2c2Records();\n"
        "            }\n"
        "        }\n"
        "        _chunkCount = checked(_chunkCount + 1);\n")
    source = replace_once(source,
        "        _hasher.Append(totals);\n\n        _completed = true;\n",
        "        FlushG2c2Records();\n"
        "        _hasher.Append(totals);\n\n        _completed = true;\n")
    source = replace_once(source,
        "    private void AppendIdentifier(string value)\n",
        "    private void FlushG2c2Records()\n"
        "    {\n"
        "        if (_g2c2PendingEntries == 0)\n"
        "        {\n"
        "            return;\n"
        "        }\n"
        "        _hasher.Append(_g2c2Buffer!.AsSpan(\n"
        "            0, _g2c2PendingEntries * (CsmFormat.HashSize + sizeof(uint))));\n"
        "        _g2c2PendingEntries = 0;\n"
        "    }\n\n"
        "    private void AppendIdentifier(string value)\n")
    if "new byte[" not in source:
        raise ValueError("No staged-array allocation in candidate")
    return source


def generate(root: Path, variant: str, receipt_path: Path) -> dict:
    root = root.resolve(strict=True)
    if git(root, "rev-parse", "--is-inside-work-tree") != "true":
        raise ValueError("Missing Git worktree")
    if subprocess.run(["git", "symbolic-ref", "-q", "HEAD"], cwd=root,
                      capture_output=True, check=False).returncode == 0:
        raise ValueError("Refuse branch worktree. Detached checkout required.")
    if git(root, "status", "--porcelain"):
        raise ValueError("Refuse already dirty worktree")
    path = root / SOURCE
    before = path.read_bytes()
    original_blob = git(root, "hash-object", SOURCE.as_posix())
    if original_blob != PINNED_BASE_BLOB:
        raise ValueError("Pristine ManifestId accumulator source drift")
    decoded = before.decode("utf-8")
    if decoded.encode("utf-8") != before:
        raise ValueError("Invalid source encoding")
    changed = transform(decoded, variant).encode("utf-8")
    if variant != "baseline":
        path.write_bytes(changed)
    actual_dirty = git(root, "status", "--porcelain")
    expected_dirty = "" if variant == "baseline" else f"M {SOURCE.as_posix()}"
    if actual_dirty != expected_dirty:
        raise ValueError(f"Unexpected dirty source: {actual_dirty!r}, expected {expected_dirty!r}")
    receipt = {
        "schema": "chunkshift.core011-csm-g2c2-gated-variant.v1",
        "experimentId": "CORE011-CSM-002",
        "stage": "G2-C2-NONDECISION",
        "variant": variant,
        "checkoutSha": git(root, "rev-parse", "HEAD"),
        "pristineBlob": original_blob,
        "pristineSha256": hashlib.sha256(before).hexdigest(),
        "variantSha256": hashlib.sha256(changed).hexdigest(),
        "thresholdEntries": THRESHOLD,
        "batchEntries": WIDTHS[variant],
        "additionalStageBufferBytes": WIDTHS[variant] * 36,
        "lazyStageAllocation": variant != "baseline",
        "sourceFile": SOURCE.as_posix(),
        "sourceModified": variant != "baseline",
        "releaseDecision": "NO_VERDICT_EXPLORATORY",
    }
    receipt_path.parent.mkdir(parents=True, exist_ok=True)
    receipt_path.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return receipt


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True, type=Path)
    p.add_argument("--variant", choices=sorted(WIDTHS), required=True)
    p.add_argument("--receipt", required=True, type=Path)
    args = p.parse_args()
    print(json.dumps(generate(args.root, args.variant, args.receipt), sort_keys=True))


if __name__ == "__main__":
    main()
