#!/usr/bin/env python3
"""CORE011-CSM-002 G2-C: fail-closed detached-worktree source variants.

Never run on a tracked production branch. The caller creates an isolated,
detached Git worktree from one immutable comparison checkout.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path

SOURCE = Path("src/ChunkShift/Manifest/ManifestIdAccumulator.cs")
# Exact pristine Core manifest-accumulator Git blob at G2-B merge into main.
PINNED_BASE_BLOB = "894c61b68bfba111467f061e8a0caf81bc8eea81"
VALID_VARIANTS = {"baseline": 0, "stage64": 64, "stage256": 256}


def replace_once(source: str, before: str, after: str) -> str:
    if source.count(before) != 1:
        raise ValueError(f"source drift: expected one match, got {source.count(before)} for {before[:80]!r}")
    return source.replace(before, after)


def transform(source: str, variant: str) -> str:
    if variant not in VALID_VARIANTS:
        raise ValueError("Unknown variant")
    if variant == "baseline":
        return source
    width = VALID_VARIANTS[variant]
    source = replace_once(source,
        "    private readonly HashSuiteIncrementalHasher _hasher;\n",
        "    private readonly HashSuiteIncrementalHasher _hasher;\n"
        f"    private const int G2cBatchEntries = {width};\n"
        "    private readonly byte[] _g2cRecords = new byte[\n"
        "        G2cBatchEntries * (CsmFormat.HashSize + sizeof(uint))];\n"
        "    private int _g2cPendingEntries;\n")
    source = replace_once(source,
        "        Span<byte> entry = stackalloc byte[CsmFormat.HashSize + sizeof(uint)];\n",
        "        Span<byte> entry = _g2cRecords.AsSpan(\n"
        "            _g2cPendingEntries * (CsmFormat.HashSize + sizeof(uint)),\n"
        "            CsmFormat.HashSize + sizeof(uint));\n")
    source = replace_once(source,
        "        _hasher.Append(entry);\n        _chunkCount = checked(_chunkCount + 1);\n",
        "        if (++_g2cPendingEntries == G2cBatchEntries)\n"
        "        {\n"
        "            FlushG2cRecords();\n"
        "        }\n"
        "        _chunkCount = checked(_chunkCount + 1);\n")
    source = replace_once(source,
        "        _hasher.Append(totals);\n\n        _completed = true;\n",
        "        FlushG2cRecords();\n"
        "        _hasher.Append(totals);\n\n        _completed = true;\n")
    source = replace_once(source,
        "    private void AppendIdentifier(string value)\n",
        "    private void FlushG2cRecords()\n"
        "    {\n"
        "        if (_g2cPendingEntries == 0)\n"
        "        {\n"
        "            return;\n"
        "        }\n\n"
        "        _hasher.Append(_g2cRecords.AsSpan(\n"
        "            0, _g2cPendingEntries * (CsmFormat.HashSize + sizeof(uint))));\n"
        "        _g2cPendingEntries = 0;\n"
        "    }\n\n"
        "    private void AppendIdentifier(string value)\n")
    return source


def git(cwd: Path, *args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=cwd, text=True).strip()


def generate(root: Path, variant: str, output: Path) -> dict:
    root = root.resolve(strict=True)
    if git(root, "rev-parse", "--is-inside-work-tree") != "true":
        raise ValueError("Not a worktree")
    if subprocess.run(["git", "symbolic-ref", "-q", "HEAD"], cwd=root,
                      capture_output=True, check=False).returncode == 0:
        raise ValueError("Refuse non-detached worktree; never rewrite a branch")
    if git(root, "status", "--porcelain"):
        raise ValueError("Refuse non-clean worktree")
    path = root / SOURCE
    before = path.read_bytes()
    blob = git(root, "hash-object", str(SOURCE))
    if blob != PINNED_BASE_BLOB:
        raise ValueError(f"Source drift: got blob {blob}, expected {PINNED_BASE_BLOB}")
    decoded = before.decode("utf-8")
    if decoded.encode("utf-8") != before:
        raise ValueError("Source encoding mismatch")
    changed = transform(decoded, variant).encode("utf-8")
    if variant != "baseline":
        path.write_bytes(changed)
    current = git(root, "status", "--porcelain")
    expected = "" if variant == "baseline" else " M " + SOURCE.as_posix()
    if current != expected.strip():
        # git status strips the leading whitespace in git() for output;
        # only the exact file may have changed, never create extra artifacts.
        raise ValueError(f"Unexpected dirty worktree: {current!r}")
    receipt = {
        "schema": "chunkshift.core011-csm-g2c-variant.v1",
        "variant": variant,
        "checkoutSha": git(root, "rev-parse", "HEAD"),
        "pristineBlob": blob,
        "pristineSha256": hashlib.sha256(before).hexdigest(),
        "variantSha256": hashlib.sha256(changed).hexdigest(),
        "batchEntries": VALID_VARIANTS[variant],
        "sourceFile": SOURCE.as_posix(),
        "sourceModified": variant != "baseline",
        "releaseDecision": "NO_VERDICT_EXPLORATORY",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(receipt, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    return receipt


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--variant", choices=sorted(VALID_VARIANTS), required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.root, args.variant, args.receipt), sort_keys=True))


if __name__ == "__main__":
    main()
