#!/usr/bin/env python3
"""Independent PATCH-GAP-001 G4 byte-study recomputation."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
PAIRS = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
INVENTORY_SHA = "d2bf3e49da5ddf228e67abbd03fdc7d97af403a88804858dca3de4075e225ad6"
CAL_SHA = "3788afe8e3e5fa3c8d47a1947844aa147fc34f15d68c2de3516c0993b83a7ffe"
EVAL_SHA = "345d2675fd4f2a3f8cf855b237c3748a197281d7bce5163c1360bfc5b53d490b"
H0 = {"calibration": 11_860_274, "evaluation": 26_363_364}
ELIGIBLE = {"calibration": 521, "evaluation": 4}
XZ_COMMIT = "d3e650e63c110e830fd5391e7f8b45df0b91d3da"
XZ_VERSION = "5.8.4"


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def exact_gate(h0: int, factor: int) -> bool:
    saved = h0 - factor
    return 20 * saved >= 3 * h0


def close(a: float, b: float) -> bool:
    return math.isclose(a, b, rel_tol=0.0, abs_tol=1e-12)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", required=True, type=Path)
    parser.add_argument("--detail-dir", required=True, type=Path)
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--xz-provenance", required=True, type=Path)
    parser.add_argument("--dataset-role", required=True, choices=("calibration", "evaluation"))
    args = parser.parse_args()

    root = load(args.index)
    role = args.dataset_role
    if root["schema"] != "chunkshift.patch-gap-g4-byte-study.v1":
        raise SystemExit("unexpected G4 index schema")
    if root["experimentId"] != "PATCH-GAP-001" or root["protocolCommit"] != PROTOCOL:
        raise SystemExit("foreign experiment/protocol identity")
    if root["datasetRole"] != role or root["datasetSha256"] != PAIRS:
        raise SystemExit("foreign split/corpus identity")
    if root["inventoryFileSha256"] != INVENTORY_SHA or sha256(args.inventory) != INVENTORY_SHA:
        raise SystemExit("frozen G4 inventory file identity mismatch")
    expected_role_sha = CAL_SHA if role == "calibration" else EVAL_SHA
    if root["inventoryRoleSha256"] != expected_role_sha:
        raise SystemExit("frozen G4 inventory split identity mismatch")
    if root["bcj"]["version"] != XZ_VERSION or root["bcj"]["sourceCommit"] != XZ_COMMIT:
        raise SystemExit("foreign XZ/liblzma identity")

    xz = load(args.xz_provenance)
    if (
        xz.get("schema") != "chunkshift.patch-gap-g4-xz-build.v1"
        or xz.get("sourceCommit") != XZ_COMMIT
        or xz.get("version") != XZ_VERSION
        or xz.get("librarySha256") != root["bcj"]["librarySha256"]
        or not isinstance(xz.get("sourceArchiveSha256"), str)
        or len(xz["sourceArchiveSha256"]) != 64
        or not xz.get("buildCommand")
    ):
        raise SystemExit("G4 XZ build provenance does not bind the measured liblzma")

    inventory = load(args.inventory)
    if inventory["schema"] != "chunkshift.patch-gap-g4-inventory.v1":
        raise SystemExit("unexpected inventory schema")
    if inventory["protocolCommit"] != PROTOCOL or inventory["corpusPairsSha256"] != PAIRS:
        raise SystemExit("inventory protocol/corpus mismatch")
    if inventory["calibrationSha256"] != CAL_SHA or inventory["evaluationSha256"] != EVAL_SHA:
        raise SystemExit("inventory split digest mismatch")

    role_rows = [row for row in inventory["rows"] if row["datasetRole"] == role]
    inventory_by_key = {
        (row["family"], row["baseVersion"], row["targetVersion"], row["path"]): row
        for row in role_rows
    }
    if len(inventory_by_key) != len(role_rows):
        raise SystemExit("duplicate frozen G4 inventory key")
    inventory_families = {row["family"] for row in role_rows}
    consumed_inventory = set()

    eligible_rows = [row for row in role_rows if row["gateEligible"]]
    if len(eligible_rows) != ELIGIBLE[role]:
        raise SystemExit("eligible population count mismatch")
    eligible_target = sum(int(row["targetBytes"]) for row in eligible_rows)
    eligible_missing = sum(int(row["uniqueMissingBytes"]) for row in eligible_rows)

    h0_total = 0
    factor_total = 0
    h0_reads = 0
    factor_reads = 0
    apply_reads = 0
    eligible_files = 0
    eligible_entries = 0
    bcj_winners = 0

    files = root["files"]
    for row in files:
        detail_path = args.detail_dir / row["detailPath"]
        if not detail_path.is_file():
            raise SystemExit(f"missing detail {row['detailPath']}")
        raw = detail_path.read_bytes()
        if len(raw) != int(row["detailBytes"]):
            raise SystemExit(f"detail size mismatch {row['detailPath']}")
        if hashlib.sha256(raw).hexdigest() != row["detailSha256"]:
            raise SystemExit(f"detail digest mismatch {row['detailPath']}")
        detail = json.loads(raw)
        if detail["schema"] != "chunkshift.patch-gap-g4-file.v1":
            raise SystemExit("unexpected detail schema")
        if detail["datasetRole"] != role:
            raise SystemExit("detail split mismatch")
        for key in ("family", "baseVersion", "targetVersion", "path", "targetBytes",
                    "gateEligible", "h0PatchBytes", "factorPatchBytes", "savedBytes",
                    "h0BaseBytesRead", "g4TrialBaseBytesRead", "applyBaseBytesRead",
                    "eligiblePayloadEntries", "bcjWinnerEntries"):
            if detail[key] != row[key]:
                raise SystemExit(f"compact/detail mismatch for {key}: {row['path']}")

        inventory_key = (
            detail["family"],
            detail["baseVersion"],
            detail["targetVersion"],
            detail["path"],
        )
        frozen = inventory_by_key.get(inventory_key)
        if frozen is None:
            if detail["family"] in inventory_families:
                raise SystemExit(f"subset file lost frozen G4 inventory row: {detail['path']}")
        else:
            if inventory_key in consumed_inventory:
                raise SystemExit(f"frozen G4 inventory row consumed twice: {detail['path']}")
            consumed_inventory.add(inventory_key)
            if bool(frozen["gateEligible"]) != bool(detail["gateEligible"]):
                raise SystemExit(f"G4 gate eligibility differs from frozen inventory: {detail['path']}")
            if int(frozen["targetBytes"]) != int(detail["targetBytes"]):
                raise SystemExit(f"G4 target bytes differ from frozen inventory: {detail['path']}")

        h0 = int(detail["h0PatchBytes"])
        factor = int(detail["factorPatchBytes"])
        if factor > h0 or int(detail["savedBytes"]) != h0 - factor:
            raise SystemExit(f"G4 non-regression/accounting failure: {row['path']}")

        entries = detail["entries"]
        if detail["gateEligible"]:
            eligible_files += 1
            if len(entries) != int(detail["eligiblePayloadEntries"]):
                raise SystemExit("eligible entry count mismatch")
            removed = 0
            added = 0
            winners = 0
            for entry in entries:
                h0_cost = int(entry["h0CostBytes"])
                winner = int(entry["winnerCostBytes"])
                if winner <= 0 or winner > h0_cost:
                    raise SystemExit("entry violates additive non-regression")
                h0_refs = int(entry["h0DictionaryReferences"])
                if h0_cost != int(entry["h0StoredBytes"]) + 32 * h0_refs:
                    raise SystemExit("H0 entry cost equation mismatch")
                h0_encoding_name = entry["h0Encoding"]
                h0_selected_ordinal = int(entry["h0SelectedCandidateOrdinal"])
                h0_candidate_start = int(entry["h0CandidateStartIndex"])
                h0_candidate_offset = int(entry["h0CandidateStartOffset"])
                if h0_encoding_name == "raw":
                    expected_h0_encoding = 0
                    if h0_refs != 0:
                        raise SystemExit("raw H0 winner carries dictionary references")
                    if (h0_selected_ordinal, h0_candidate_start, h0_candidate_offset) != (-1, -1, -1):
                        raise SystemExit("raw H0 winner carries dictionary candidate identity")
                elif h0_encoding_name == "zstd":
                    expected_h0_encoding = 1
                    if h0_refs != 0:
                        raise SystemExit("no-dictionary zstd H0 winner carries dictionary references")
                    if (h0_selected_ordinal, h0_candidate_start, h0_candidate_offset) != (-1, -1, -1):
                        raise SystemExit("no-dictionary zstd H0 winner carries dictionary candidate identity")
                elif h0_encoding_name == "zstd-dictionary":
                    expected_h0_encoding = 1
                    if h0_refs <= 0 or h0_selected_ordinal < 0 or h0_candidate_start < 0 or h0_candidate_offset < 0:
                        raise SystemExit("dictionary zstd H0 winner lost dictionary candidate identity")
                else:
                    raise SystemExit(f"unknown H0 encoding identity: {h0_encoding_name}")
                if winner != int(entry["winnerStoredBytes"]) + 32 * int(entry["winnerDictionaryReferences"]):
                    raise SystemExit("G4 winner cost equation mismatch")
                target_norm = entry["targetNormalization"]
                if int(target_norm["inputBytes"]) != int(entry["targetLength"]):
                    raise SystemExit("target normalization length mismatch")
                if (
                    int(target_norm["untouchedPrefixBytes"])
                    + int(target_norm["processedBytes"])
                    + int(target_norm["untouchedTailBytes"])
                    != int(target_norm["inputBytes"])
                ):
                    raise SystemExit("target normalization boundary equation mismatch")

                trials = entry["trials"]
                if not trials:
                    raise SystemExit("eligible G4 entry lost the mandatory no-dictionary trial")
                first_trial = trials[0]
                if h0_encoding_name == "zstd-dictionary":
                    selected_trials = [
                        trial for trial in trials
                        if int(trial["candidateOrdinal"]) == h0_selected_ordinal
                    ]
                    if len(selected_trials) != 1:
                        raise SystemExit("H0 selected dictionary candidate is not uniquely retained in G4 trials")
                    h0_trial = selected_trials[0]
                    if (
                        int(h0_trial["candidateStartIndex"]) != h0_candidate_start
                        or int(h0_trial["candidateStartOffset"]) != h0_candidate_offset
                        or int(h0_trial["dictionaryReferences"]) != h0_refs
                    ):
                        raise SystemExit("H0 selected dictionary identity disagrees with retained G4 candidate")

                if (
                    first_trial["storedForm"] != "bcj-zstd"
                    or int(first_trial["candidateOrdinal"]) != -1
                    or int(first_trial["dictionaryReferences"]) != 0
                    or first_trial["dictionaryNormalization"] is not None
                ):
                    raise SystemExit("first G4 trial is not the frozen no-dictionary trial")

                expected_trial = None
                expected_cost = h0_cost
                for trial in trials:
                    refs = int(trial["dictionaryReferences"])
                    trial_cost = int(trial["costBytes"])
                    if trial_cost != int(trial["frameBytes"]) + 32 * refs:
                        raise SystemExit("G4 trial cost equation mismatch")
                    if refs == 0:
                        if trial["dictionaryChunkIds"] or int(trial["dictionaryBytes"]) != 0:
                            raise SystemExit("no-dictionary trial carries dictionary metadata")
                    else:
                        if len(trial["dictionaryChunkIds"]) != refs:
                            raise SystemExit("G4 dictionary reference count mismatch")
                        norm = trial["dictionaryNormalization"]
                        if norm is None:
                            raise SystemExit("G4 dictionary trial lost normalization metadata")
                        if (
                            int(norm["untouchedPrefixBytes"])
                            + int(norm["processedBytes"])
                            + int(norm["untouchedTailBytes"])
                            != int(norm["inputBytes"])
                        ):
                            raise SystemExit("dictionary normalization boundary equation mismatch")
                        if int(norm["inputBytes"]) != int(trial["dictionaryBytes"]):
                            raise SystemExit("dictionary normalization length mismatch")
                        candidate_start = int(trial["candidateStartIndex"])
                        canonical_start = int(trial["canonicalStartIndex"])
                        if canonical_start > candidate_start:
                            raise SystemExit("canonical dictionary occurrence is later than H0 candidate")
                        if bool(trial["canonicalStartDiffers"]) != (canonical_start != candidate_start):
                            raise SystemExit("canonical-start-differs flag mismatch")
                    if trial_cost < expected_cost:
                        expected_cost = trial_cost
                        expected_trial = trial

                if int(entry["winnerCostBytes"]) != expected_cost:
                    raise SystemExit("strict G4 winner cost mismatch")
                architecture = detail["architecture"]
                expected_synthetic_encoding = 3 if architecture == "ARM64" else 2
                if architecture not in ("X86", "X64", "ARM64"):
                    raise SystemExit(f"unsupported G4 architecture identity: {architecture}")

                if expected_trial is None:
                    if (
                        entry["winnerStoredForm"] != "H0"
                        or int(entry["winnerEncoding"]) != expected_h0_encoding
                    ):
                        raise SystemExit("G4 H0 winner encoding/identity was not preserved exactly")
                else:
                    if (
                        entry["winnerStoredForm"] != expected_trial["storedForm"]
                        or int(entry["winnerEncoding"]) != expected_synthetic_encoding
                        or int(entry["winnerCandidateOrdinal"]) != int(expected_trial["candidateOrdinal"])
                        or int(entry["winnerCandidateStartIndex"]) != int(expected_trial["candidateStartIndex"])
                        or int(entry["winnerCanonicalStartIndex"]) != int(expected_trial["canonicalStartIndex"])
                    ):
                        raise SystemExit("G4 winner metadata differs from first strict minimum")

                if entry["winnerStoredForm"] != "H0":
                    winners += 1
                    if int(entry["winnerEncoding"]) != expected_synthetic_encoding:
                        raise SystemExit("synthetic G4 winner encoding does not match executable architecture")
                    if not entry["winnerFrameSha256"]:
                        raise SystemExit("synthetic G4 winner lost frame identity")
                removed += h0_cost
                added += winner
            if factor != h0 - removed + added:
                raise SystemExit(f"file physical equation mismatch: {row['path']}")
            if winners != int(detail["bcjWinnerEntries"]):
                raise SystemExit("BCJ winner count mismatch")
        else:
            if entries or factor != h0 or int(detail["eligiblePayloadEntries"]) != 0:
                raise SystemExit("ineligible file changed under G4")

        if not detail["reconstructionPass"]:
            raise SystemExit("reconstruction failed")

        h0_total += h0
        factor_total += factor
        h0_reads += int(detail["h0BaseBytesRead"])
        factor_reads += int(detail["h0BaseBytesRead"]) + int(detail["g4TrialBaseBytesRead"])
        apply_reads += int(detail["applyBaseBytesRead"])
        eligible_entries += int(detail["eligiblePayloadEntries"])
        bcj_winners += int(detail["bcjWinnerEntries"])

    lane = root["lane"]
    expected_saved = h0_total - factor_total
    if len(consumed_inventory) != len(role_rows):
        missing = next(key for key in inventory_by_key if key not in consumed_inventory)
        raise SystemExit(f"frozen G4 inventory row was not consumed: {missing}")

    if h0_total != H0[role]:
        raise SystemExit(f"H0 split anchor mismatch: {h0_total} != {H0[role]}")
    if factor_total > h0_total:
        raise SystemExit("aggregate G4 bytes exceed H0")
    expected_reduction = expected_saved / h0_total
    expected_amplification = (factor_reads / h0_reads) if h0_reads else None

    exact = {
        "h0Bytes": h0_total,
        "factorBytes": factor_total,
        "savedBytes": expected_saved,
        "meetsRfcSizeGate": exact_gate(h0_total, factor_total),
        "h0BaseBytesRead": h0_reads,
        "factorBaseBytesRead": factor_reads,
        "applyBaseBytesRead": apply_reads,
        "fileCount": len(files),
        "gateEligibleFiles": eligible_files,
        "gateEligibleTargetBytes": eligible_target,
        "gateEligibleUniqueMissingBytes": eligible_missing,
        "eligiblePayloadEntries": eligible_entries,
        "bcjWinnerEntries": bcj_winners,
    }
    for key, value in exact.items():
        if lane[key] != value:
            raise SystemExit(f"aggregate mismatch for {key}: {lane[key]} != {value}")
    if not close(float(lane["reductionVsCsp"]), expected_reduction):
        raise SystemExit("reduction ratio mismatch")
    if expected_amplification is None:
        if lane["baseReadAmplification"] is not None:
            raise SystemExit("base-read amplification should be null")
    elif not close(float(lane["baseReadAmplification"]), expected_amplification):
        raise SystemExit("base-read amplification mismatch")

    print(
        "PATCH-GAP-001 G4 independent recomputation: PASS\n"
        f"role={role} h0={h0_total} factor={factor_total} saved={expected_saved} "
        f"reduction={expected_reduction:.9%} gate={exact_gate(h0_total, factor_total)} "
        f"eligible_files={eligible_files} bcj_winners={bcj_winners}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
