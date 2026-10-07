#!/usr/bin/env python3
"""Fail-closed independent accounting verifier for PATCH-GAP-001 G4 evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Any

SCHEMA = "chunkshift.patch-gap-g4-byte-study.v1"
FILE_SCHEMA = "chunkshift.patch-gap-g4-file.v1"
EXPERIMENT = "PATCH-GAP-001"
POLICY = "G4-BCJ-H0-CANDIDATES-L19-PREFIX-H20C20-W20"
INVENTORY_SHA = "d2bf3e49da5ddf228e67abbd03fdc7d97af403a88804858dca3de4075e225ad6"
ROLE_SHA = {
    "calibration": "3788afe8e3e5fa3c8d47a1947844aa147fc34f15d68c2de3516c0993b83a7ffe",
    "evaluation": "345d2675fd4f2a3f8cf855b237c3748a197281d7bce5163c1360bfc5b53d490b",
}
ELIGIBLE = {"calibration": 521, "evaluation": 4}
XZ_COMMIT = "d3e650e63c110e830fd5391e7f8b45df0b91d3da"


def load(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise SystemExit(f"{path}: expected JSON object")
    return value


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def valid_sha(value: Any) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(ch in "0123456789abcdef" for ch in value)
    )


def verify_transform(
    transform: dict[str, Any],
    architecture: str,
    original_offset: int,
    byte_length: int,
    label: str,
) -> None:
    prefix = int(transform["prefixBytes"])
    processed = int(transform["processedBytes"])
    tail = int(transform["tailBytes"])
    start = int(transform["startOffset"])
    require(prefix >= 0 and processed >= 0 and tail >= 0, f"{label}: negative BCJ sizes")
    require(prefix + processed + tail == byte_length, f"{label}: BCJ byte partition mismatch")

    if architecture in ("X86", "X64"):
        require(prefix == 0, f"{label}: x86/x64 prefix must be zero")
        require(tail <= 4, f"{label}: x86/x64 tail exceeds four bytes")
        require(start == (original_offset & 0xFFFFFFFF), f"{label}: x86/x64 start offset mismatch")
    elif architecture == "Arm64":
        needed = (4 - (original_offset & 3)) & 3
        if needed >= byte_length:
            require(
                prefix == byte_length and processed == 0 and tail == 0 and start == 0,
                f"{label}: short ARM64 span mismatch",
            )
        else:
            require(prefix == needed, f"{label}: ARM64 prefix mismatch")
            require(tail <= 3, f"{label}: ARM64 tail exceeds three bytes")
            require(processed % 4 == 0, f"{label}: ARM64 processed bytes not aligned")
            require(start == ((original_offset + prefix) & 0xFFFFFFFF), f"{label}: ARM64 start mismatch")
            require(start % 4 == 0, f"{label}: ARM64 start not aligned")
    else:
        raise SystemExit(f"{label}: unsupported architecture {architecture!r}")


def verify(args: argparse.Namespace) -> dict[str, Any]:
    root = load(args.index.resolve())
    failed = [
        name
        for name, ok in {
            "schema": root.get("schema") == SCHEMA,
            "experiment": root.get("experimentId") == EXPERIMENT,
            "protocol": root.get("protocolCommit") == args.protocol_commit,
            "source": root.get("sourceCommit") == args.source_commit,
            "role": root.get("datasetRole") == args.dataset_role,
            "dataset": root.get("datasetSha256") == args.pairs_sha,
            "inventory": root.get("inventoryManifestSha256") == INVENTORY_SHA,
            "role-inventory": root.get("inventoryRoleSha256") == ROLE_SHA[args.dataset_role],
            "policy": root.get("researchPolicy") == POLICY,
            "xz": root.get("xzCommit") == XZ_COMMIT,
            "liblzma": valid_sha(root.get("libLzmaSha256")),
        }.items()
        if not ok
    ]
    require(not failed, "G4 index identity mismatch: " + ", ".join(failed))

    files = root.get("files")
    require(isinstance(files, list) and files, "G4 study has no compact file rows")
    keys = [
        (row["family"], row["baseVersion"], row["targetVersion"], row["path"])
        for row in files
    ]
    require(keys == sorted(keys), "G4 compact rows are not in canonical order")
    require(len(keys) == len(set(keys)), "G4 compact rows contain duplicate identities")

    h0 = sum(int(row["h0PatchBytes"]) for row in files)
    g4 = sum(int(row["g4PatchBytes"]) for row in files)
    h0_reads = sum(int(row["h0BaseBytesRead"]) for row in files)
    extra_reads = sum(int(row["g4ExtraBaseBytesRead"]) for row in files)
    eligible_rows = [row for row in files if row.get("gateEligible") is True]
    require(h0 == args.expected_h0, f"G4 H0 bytes {h0} != frozen {args.expected_h0}")
    require(len(eligible_rows) == ELIGIBLE[args.dataset_role], "G4 eligible file count mismatch")
    require(g4 <= h0, "G4 aggregate additive oracle failed")

    referenced: set[str] = set()
    improved_files = 0
    improved_entries = 0

    for row in files:
        row_h0 = int(row["h0PatchBytes"])
        row_g4 = int(row["g4PatchBytes"])
        require(int(row["savedBytes"]) == row_h0 - row_g4, "G4 compact savedBytes mismatch")

        if not row.get("gateEligible"):
            require(
                row_g4 == row_h0
                and int(row["g4ExtraBaseBytesRead"]) == 0
                and int(row["improvedEntries"]) == 0
                and row.get("detailPath") is None
                and row.get("detailSha256") is None
                and row.get("detailBytes") is None,
                "G4 ineligible compact row changed H0",
            )
            continue

        detail_path = args.detail_dir.resolve() / str(row["detailPath"])
        require(detail_path.is_file(), f"missing G4 detail: {detail_path.name}")
        raw = detail_path.read_bytes()
        require(hashlib.sha256(raw).hexdigest() == row["detailSha256"], "G4 detail SHA mismatch")
        require(len(raw) == int(row["detailBytes"]), "G4 detail length mismatch")
        require(detail_path.name not in referenced, "duplicate G4 detail path")
        referenced.add(detail_path.name)

        detail = json.loads(raw)
        require(detail.get("schema") == FILE_SCHEMA, "G4 detail schema mismatch")
        require(detail.get("reconstructionPass") is True, "G4 reconstruction did not pass")
        for field in ("family", "baseVersion", "targetVersion", "path", "targetBytes"):
            require(detail[field] == row[field], f"G4 compact/detail mismatch: {field}")
        require(int(detail["h0PatchBytes"]) == row_h0, "G4 detail H0 mismatch")
        require(int(detail["g4PatchBytes"]) == row_g4, "G4 detail G4 mismatch")
        require(
            int(detail["g4ExtraBaseBytesRead"]) == int(row["g4ExtraBaseBytesRead"]),
            "G4 detail base-read mismatch",
        )

        architecture = str(detail["architecture"])
        entries = detail.get("entries")
        require(isinstance(entries, list), "G4 detail entries missing")
        removed = 0
        added = 0
        file_improved = 0
        seen_indexes: set[int] = set()

        for entry in entries:
            idx = int(entry["targetIndex"])
            require(idx not in seen_indexes, "G4 duplicate target entry")
            seen_indexes.add(idx)
            target_len = int(entry["targetLength"])
            target_offset = int(entry["targetOffset"])
            h0_cost = int(entry["h0CostBytes"])
            g4_cost = int(entry["g4CostBytes"])
            require(
                h0_cost == int(entry["h0StoredBytes"]) + 32 * int(entry["h0DictionaryReferences"]),
                "G4 H0 entry equation mismatch",
            )
            require(
                g4_cost == int(entry["g4StoredBytes"]) + 32 * int(entry["g4DictionaryReferences"]),
                "G4 selected entry equation mismatch",
            )
            require(g4_cost <= h0_cost, "G4 per-entry additive oracle failed")
            verify_transform(entry["targetTransform"], architecture, target_offset, target_len, "G4 target")

            trials = entry.get("trials")
            require(isinstance(trials, list) and trials, "G4 entry has no BCJ trials")
            trial_costs: list[int] = []
            for trial in trials:
                refs = int(trial["dictionaryReferences"])
                frame = int(trial["frameBytes"])
                cost = int(trial["costBytes"])
                require(frame > 0 and refs >= 0 and cost == frame + 32 * refs, "G4 trial cost mismatch")
                trial_costs.append(cost)
                if trial["kind"] == "bcj-zstd":
                    require(
                        refs == 0
                        and trial.get("candidateOrdinal") is None
                        and trial.get("dictionaryBytes") is None
                        and trial.get("dictionaryTransform") is None,
                        "G4 no-dictionary trial metadata mismatch",
                    )
                else:
                    require(trial["kind"] == "bcj-zstd-dictionary", "G4 trial kind mismatch")
                    dbytes = int(trial["dictionaryBytes"])
                    canonical_offset = int(trial["canonicalStartOffset"])
                    require(refs > 0 and dbytes > 0, "G4 dictionary trial metadata missing")
                    verify_transform(
                        trial["dictionaryTransform"],
                        architecture,
                        canonical_offset,
                        dbytes,
                        "G4 dictionary",
                    )

            best_trial = min(trial_costs)
            if entry["winner"] == "H0":
                require(g4_cost == h0_cost and best_trial >= h0_cost, "G4 H0 tie/strict rule mismatch")
                require(entry.get("g4FrameSha256") is None, "G4 H0 winner unexpectedly stores BCJ frame digest")
            else:
                require(entry["winner"] == "BCJ", "G4 winner kind mismatch")
                require(g4_cost < h0_cost and g4_cost == best_trial, "G4 strict winner mismatch")
                require(valid_sha(entry.get("g4FrameSha256")), "G4 BCJ winner frame digest malformed")
                file_improved += 1

            removed += h0_cost
            added += g4_cost

        require(row_g4 == row_h0 - removed + added, "G4 per-file physical equation mismatch")
        require(int(row["improvedEntries"]) == file_improved, "G4 improved-entry count mismatch")
        if row_g4 < row_h0:
            improved_files += 1
        improved_entries += file_improved

    actual = {path.name for path in args.detail_dir.resolve().glob("*.json")}
    require(actual == referenced, "G4 detail directory contains stale/unreferenced evidence")

    saved = h0 - g4
    reduction = saved / h0
    factor_reads = h0_reads + extra_reads
    require(int(root["h0Bytes"]) == h0 and int(root["g4Bytes"]) == g4, "G4 aggregate bytes mismatch")
    require(int(root["savedBytes"]) == saved, "G4 aggregate savedBytes mismatch")
    require(
        math.isclose(float(root["reductionVsCsp"]), reduction, rel_tol=0.0, abs_tol=1e-15),
        "G4 aggregate reduction mismatch",
    )
    require(bool(root["meetsRfcSizeGate"]) == (20 * saved >= 3 * h0), "G4 size gate mismatch")
    require(int(root["h0BaseBytesRead"]) == h0_reads, "G4 H0 base-read mismatch")
    require(int(root["g4BaseBytesRead"]) == factor_reads, "G4 factor base-read mismatch")
    expected_amp = None if h0_reads == 0 else factor_reads / h0_reads
    if expected_amp is None:
        require(root.get("baseReadAmplification") is None, "G4 unexpected amplification")
    else:
        require(
            math.isclose(float(root["baseReadAmplification"]), expected_amp, rel_tol=0.0, abs_tol=1e-12),
            "G4 base-read amplification mismatch",
        )
    require(int(root["eligibleFiles"]) == len(eligible_rows), "G4 eligible count mismatch")
    require(int(root["improvedFiles"]) == improved_files, "G4 improved-file count mismatch")
    require(int(root["improvedEntries"]) == improved_entries, "G4 improved-entry aggregate mismatch")

    return {
        "role": args.dataset_role,
        "files": len(files),
        "eligible": len(eligible_rows),
        "h0": h0,
        "g4": g4,
        "saved": saved,
        "reduction": reduction,
        "gate": bool(root["meetsRfcSizeGate"]),
        "amplification": root.get("baseReadAmplification"),
        "improved_files": improved_files,
        "improved_entries": improved_entries,
        "run_id": root["provenance"]["runId"],
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", type=Path, required=True)
    parser.add_argument("--detail-dir", type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--protocol-commit", required=True)
    parser.add_argument("--pairs-sha", required=True)
    parser.add_argument("--dataset-role", choices=("calibration", "evaluation"), required=True)
    parser.add_argument("--expected-h0", type=int, required=True)
    parser.add_argument("--summary", type=Path, required=True)
    args = parser.parse_args()

    result = verify(args)
    args.summary.write_text(
        "\n".join(
            [
                f"### PATCH-GAP-001 G4 {result['role']}",
                "",
                f"- RunId: `{result['run_id']}`",
                f"- sourceCommit: `{args.source_commit}`",
                f"- files: {result['files']:,}",
                f"- frozen G4 eligible files: {result['eligible']:,}",
                f"- H0 bytes: {result['h0']:,}",
                f"- G4 bytes: {result['g4']:,}",
                f"- saved: {result['saved']:,} B",
                f"- reduction: {result['reduction']:.6%}",
                f"- RFC size gate: {result['gate']}",
                f"- base-read amplification: {result['amplification']}",
                f"- improved files: {result['improved_files']:,}",
                f"- improved entries: {result['improved_entries']:,}",
            ]
        )
        + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"PATCH-GAP-001 G4 {args.dataset_role} independent recomputation: PASS")


if __name__ == "__main__":
    main()
