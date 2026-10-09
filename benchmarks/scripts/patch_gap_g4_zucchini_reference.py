#!/usr/bin/env python3
"""Frozen PATCH-GAP-001 §8.3 Zucchini *reference* (never CSP-costed RFC evidence).

Inventory mode does not execute Zucchini or inspect resulting patch sizes.
Measure mode requires a fully identified locally built Chromium binary. All
eligible input identities come from the committed G4 manifest, not parser wins.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import tempfile
import time
from collections import Counter
from pathlib import Path

PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
CHROMIUM_COMPONENT = "667ffb4e19970939936af2e7a169175ae4c1da5b"
PAIRS_HASH = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
INVENTORY_SCHEMA = "chunkshift.patch-gap-g4-inventory.v1"
OUTPUT_SCHEMA = "chunkshift.patch-gap-g4-zucchini-reference.v1"
FROZEN_FAMILIES = frozenset({
    "dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64",
    "node-linux-x64", "node-win-x64",
})


def require(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as src:
        for block in iter(lambda: src.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def canonical(doc: object) -> bytes:
    return (json.dumps(doc, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n").encode()


def frozen_rows(inventory_path: Path, pairs_path: Path) -> list[dict]:
    inventory = read_json(inventory_path)
    require(inventory["schema"] == INVENTORY_SCHEMA, "inventory schema mismatch")
    require(inventory["protocolCommit"] == PROTOCOL, "frozen protocol mismatch")
    require(inventory["corpusPairsSha256"] == PAIRS_HASH, "inventory corpus lock mismatch")
    require(sha(pairs_path) == PAIRS_HASH, "materialized corpus lock mismatch")
    pairs = read_json(pairs_path)
    require(pairs["schema"] == "chunkshift.patch-pairs.v1", "pairs schema mismatch")
    changed = {}
    for pair in pairs["pairs"]:
        family = pair["family"]
        if family not in FROZEN_FAMILIES:
            continue
        role = "calibration" if family.startswith("dotnet-") else "evaluation"
        for record in pair["changed"]:
            key = (role, family, pair["base"], pair["target"], record["path"])
            require(key not in changed, "duplicate frozen corpus key")
            changed[key] = record
    seen = set()
    rows = []
    for row in inventory["rows"]:
        key = (row["datasetRole"], row["family"], row["baseVersion"], row["targetVersion"], row["path"])
        require(row["family"] in FROZEN_FAMILIES, "non-frozen executable family")
        require(key not in seen and key in changed, "invalid or duplicate G4 member")
        seen.add(key)
        pair = changed[key]
        require(pair["targetSize"] == row["targetBytes"], "frozen target byte mismatch")
        # Classification comes exclusively from the frozen structural inventory.
        base, target = row["base"], row["target"]
        structural = base["kind"] != 0 and target["kind"] != 0
        rows.append({
            "datasetRole": row["datasetRole"],
            "family": row["family"], "baseVersion": row["baseVersion"],
            "targetVersion": row["targetVersion"], "path": row["path"],
            "g4BcjGateEligible": row["gateEligible"],
            "structuralCandidate": structural,
            "baseKind": base["detail"], "targetKind": target["detail"],
            "baseSha256": pair["baseSha256"], "targetSha256": pair["targetSha256"],
            "baseBytes": pair["baseSize"], "targetBytes": pair["targetSize"],
        })
    require(seen == set(changed), "frozen G4 inventory has missing corpus members")
    require(len(rows) == 1863, "frozen G4 population changed")
    return rows


def validate_tool(exe: Path, manifest_path: Path) -> dict:
    manifest = read_json(manifest_path)
    require(manifest["schema"] == "chunkshift.patch-gap-g4-zucchini-build.v1", "tool manifest schema")
    require(manifest["chromiumComponentCommit"] == CHROMIUM_COMPONENT, "Zucchini source pin mismatch")
    # Chromium's full source-tree revision may differ from the standalone
    # component commit. Require both identities; do not silently equate them.
    for field in ("chromiumSourceCommit", "binarySha256", "compilerIdentity",
                  "buildCommand", "gnArgs", "buildSystemIdentity", "sourceCheckoutProvenance"):
        require(isinstance(manifest.get(field), str) and bool(manifest[field].strip()),
                "missing tool provenance: " + field)
    require(len(manifest["chromiumSourceCommit"]) == 40, "Chromium source revision must be full")
    require(len(manifest["binarySha256"]) == 64, "tool SHA-256 malformed")
    require(exe.is_file() and sha(exe) == manifest["binarySha256"], "pinned Zucchini binary mismatch")
    return manifest


def safe_file(corpus: Path, row: dict, version_key: str) -> Path:
    # Reject even a lexically prefixed path and avoid following a symlink
    # outside the already locked materialized tree.
    parts = Path(row["path"])
    require(not parts.is_absolute() and all(p not in (".", "..") for p in parts.parts)
            and bool(parts.parts), "unsafe frozen relative path")
    root = (corpus / "tree" / row["family"] / row[version_key]).resolve()
    file = (root / parts).resolve()
    require(file.is_relative_to(root) and file.is_file(), "missing or escaping corpus path")
    return file


def invoke(exe: Path, args: list[str], timeout: int) -> tuple[subprocess.CompletedProcess, float]:
    started = time.perf_counter()
    proc = subprocess.run([str(exe.resolve()), *args], capture_output=True, timeout=timeout, check=False)
    return proc, time.perf_counter() - started


def measure(rows: list[dict], corpus: Path, exe: Path, timeout: int) -> list[dict]:
    output = []
    for row in rows:
        result = dict(row)
        result["status"] = "OUTSIDE_STRUCTURAL_SUBSET"
        result["patchBytes"] = None
        if not row["structuralCandidate"]:
            output.append(result)
            continue
        base = safe_file(corpus, row, "baseVersion")
        target = safe_file(corpus, row, "targetVersion")
        require(base.stat().st_size == row["baseBytes"] and sha(base) == row["baseSha256"],
                "base corpus identity mismatch")
        require(target.stat().st_size == row["targetBytes"] and sha(target) == row["targetSha256"],
                "target corpus identity mismatch")
        # Frozen Zucchini CLI -read provides the parser-support probe.
        # A rejection is recorded, not a successful zero-byte delta.
        base_probe, _ = invoke(exe, ["-read", str(base)], timeout)
        target_probe, _ = invoke(exe, ["-read", str(target)], timeout)
        # Signals/crashes are tool errors, not evidence of parser non-support.
        require(base_probe.returncode >= 0 and target_probe.returncode >= 0,
                "Zucchini parser probe terminated by signal")
        result["baseParserExit"] = base_probe.returncode
        result["targetParserExit"] = target_probe.returncode
        if base_probe.returncode != 0 or target_probe.returncode != 0:
            result["status"] = ("UNSUPPORTED_BASE_AND_TARGET" if base_probe.returncode != 0
                                and target_probe.returncode != 0 else
                                "UNSUPPORTED_BASE" if base_probe.returncode != 0 else "UNSUPPORTED_TARGET")
            output.append(result)
            continue
        with tempfile.TemporaryDirectory(prefix="chunkshift-zucchini-") as td:
            patch, reconstructed = Path(td) / "patch.zuc", Path(td) / "reconstructed"
            generated, gen_secs = invoke(exe, ["-gen", str(base), str(target), str(patch)], timeout)
            require(generated.returncode == 0 and patch.is_file(),
                    "Zucchini -gen failed after successful parser probes: " + repr(generated.stderr[-200:]))
            applied, apply_secs = invoke(exe, ["-apply", str(base), str(patch), str(reconstructed)], timeout)
            require(applied.returncode == 0 and reconstructed.is_file(),
                    "Zucchini -apply failed: " + repr(applied.stderr[-200:]))
            require(sha(reconstructed) == row["targetSha256"],
                    "Zucchini reconstructed target SHA-256 mismatch")
            result.update(status="VERIFIED", patchBytes=patch.stat().st_size,
                          patchSha256=sha(patch), encodeSeconds=gen_secs,
                          decodeSeconds=apply_secs, reconstructionSha256=sha(reconstructed))
        output.append(result)
    return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--role", choices=["calibration", "evaluation"], required=True)
    parser.add_argument("--mode", choices=["inventory", "measure"], required=True)
    parser.add_argument("--zucchini", type=Path)
    parser.add_argument("--tool-manifest", type=Path)
    parser.add_argument("--timeout", type=int, default=600)
    args = parser.parse_args()
    require(args.timeout > 0, "positive timeout required")
    rows = [r for r in frozen_rows(args.inventory, args.corpus / "pairs.json")
            if r["datasetRole"] == args.role]
    require(len(rows) == (1049 if args.role == "calibration" else 814),
            "role's frozen G4 inventory population changed")
    provenance = None
    if args.mode == "measure":
        require(args.zucchini is not None and args.tool_manifest is not None,
                "real pinned executable and manifest mandatory")
        provenance = validate_tool(args.zucchini, args.tool_manifest)
        rows = measure(rows, args.corpus, args.zucchini, args.timeout)
    counts = dict(sorted(Counter(r.get("status", "STRUCTURAL_CANDIDATE" if r["structuralCandidate"]
                                 else "OUTSIDE_STRUCTURAL_SUBSET") for r in rows).items()))
    result = {"schema": OUTPUT_SCHEMA, "mode": args.mode, "datasetRole": args.role,
              "protocolCommit": PROTOCOL, "corpusPairsSha256": PAIRS_HASH,
              "frozenInventorySha256": sha(args.inventory),
              "zucchiniComponentCommit": CHROMIUM_COMPONENT,
              "tool": provenance, "counts": counts, "files": rows,
              "scope": "reference-only; subset cost, never RFC 15% gate"}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(canonical(result))
    print(json.dumps({"role": args.role, "mode": args.mode, "files": len(rows),
                      "counts": counts}, sort_keys=True))


if __name__ == "__main__":
    main()
