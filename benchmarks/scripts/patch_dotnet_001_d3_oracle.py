#!/usr/bin/env python3
"""PATCH-DOTNET-001 D3-O: auditable optimistic physical-byte ceiling.

This does NOT generate patches or use the sealed evaluation. It consumes an
exact A1-v2 calibration-only pair lock and externally measured same-population
D0 physical patch bytes plus independently reconstructed D3 slot evidence.
The zero-byte normalized candidate assumption is intentionally impossible;
a low ceiling is decisive, a high ceiling is NOT evidence of a real gain.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import statistics
from pathlib import Path

PAIRS_SHA = "5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9"
CALIBRATION_PLAN_SHA = "f50401eed8315e04c171ee79d07f715fa4f29625ce48f1f63ac436565daf5fee"
PAIRS_SCHEMA = "chunkshift.patch-dotnet-v2-pairs.v1"
INPUT_SCHEMA = "chunkshift.patch-dotnet-d3-upper-bound-input.v1"
OUTPUT_SCHEMA = "chunkshift.patch-dotnet-d3-upper-bound.v1"
MAX_FILE = 64 * 1024 * 1024
MAX_SLOTS = 262_144
SIDECAR_HEADER = 44
SIDECAR_SLOT = 8
HEX64 = re.compile(r"^[0-9a-f]{64}$")
HEX40 = re.compile(r"^[0-9a-f]{40}$")
ALLOWED_SCANNER = {"VERIFIED_BOTH", "FALLBACK", "INELIGIBLE"}


class OracleError(ValueError):
    pass


def demand(predicate: bool, code: str) -> None:
    if not predicate:
        raise OracleError(code)


def hash_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def exact_u64(value: object, name: str, allow_zero: bool = False) -> int:
    demand(type(value) is int and 0 <= value <= 0x7FFFFFFFFFFFFFFF and
           (allow_zero or value > 0), f"INVALID_NONNEGATIVE_INTEGER:{name}")
    return value


def load_locked_pairs(path: Path) -> dict:
    raw = path.read_bytes()
    demand(hash_bytes(raw) == PAIRS_SHA, "FROZEN_PAIRS_SHA_MISMATCH")
    document = json.loads(raw)
    demand(document.get("schema") == PAIRS_SCHEMA and isinstance(document.get("pairs"), list),
           "FROZEN_PAIRS_SCHEMA")
    groups = {p["family"] for p in document["pairs"]}
    demand(groups == {"ps-linux-fdd", "ps-win-fdd", "ps-win-sc"},
           "FROZEN_PAIRS_GROUPS")
    return document


def evaluate(doc: dict, pairs: dict) -> dict:
    demand(isinstance(doc, dict) and doc.get("schema") == INPUT_SCHEMA,
           "ORACLE_INPUT_SCHEMA")
    demand(doc.get("experimentId") == "PATCH-DOTNET-001", "ORACLE_ID")
    demand(doc.get("role") == "calibration", "ORACLE_EVALUATION_FORBIDDEN")
    demand(doc.get("sourcePairsSha256") == PAIRS_SHA and
           doc.get("sourcePlanSha256") == CALIBRATION_PLAN_SHA,
           "ORACLE_SOURCE_PROVENANCE")
    demand(doc.get("d0PolicyId") == "CSP_DEFAULT_H0", "ORACLE_BASELINE_POLICY")
    demand(isinstance(doc.get("baselineEvidence"), dict), "ORACLE_BASELINE_PROVENANCE")
    demand(isinstance(doc.get("scannerEvidence"), dict), "ORACLE_SCANNER_PROVENANCE")
    for name in ("baselineEvidence", "scannerEvidence"):
        item = doc[name]
        demand(isinstance(item.get("sha256"), str) and
               HEX64.fullmatch(item["sha256"]) is not None,
               "ORACLE_EVIDENCE_SHA")
        demand(isinstance(item.get("gitCommit"), str) and
               HEX40.fullmatch(item["gitCommit"]) is not None,
               "ORACLE_EVIDENCE_COMMIT")
        demand(exact_u64(item.get("runId"), "runId") > 0, "ORACLE_EVIDENCE_RUN")
    rows = doc.get("rows")
    demand(isinstance(rows, list), "ORACLE_INPUT_ROWS")

    expected: dict[tuple[str, str, str, str], dict] = {}
    for group in pairs["pairs"]:
        demand(group.get("role") == "calibration" and
               group.get("base") == "7.5.3" and group.get("target") == "7.5.4",
               "FROZEN_GROUP_ROLE_VERSION")
        for item in group["changed"]:
            key = (group["family"], group["base"], group["target"], item["path"])
            demand(key not in expected, "FROZEN_CHANGED_DUPLICATE")
            expected[key] = item
    demand(len(expected) == 1242, "FROZEN_CHANGED_COUNT")
    seen: set[tuple[str, str, str, str]] = set()
    family_total: dict[str, dict] = {}
    eligible_ratios: dict[tuple[str, str], float] = {}
    sum_d0 = sum_ideal = 0
    candidate_rows = 0
    floor_nonhelpful = 0
    for row in rows:
        demand(isinstance(row, dict), "ORACLE_ROW_NOT_OBJECT")
        key = tuple(row.get(k) for k in ("family", "base", "target", "path"))
        demand(key in expected and key not in seen, "ORACLE_ROW_UNKNOWN_OR_DUPLICATE")
        seen.add(key)
        source = expected[key]
        demand(all(row.get(k) == source.get(k) for k in
                   ("baseSha256", "targetSha256", "baseSize", "targetSize")),
               "ORACLE_ROW_CONTENT_DRIFT")
        demand(source.get("evaluationContentDuplicate") is False,
               "ORACLE_SEALED_HOLDOUT_OVERLAP")
        demand(isinstance(source.get("structuralD3Potential"), bool),
               "ORACLE_STRUCTURAL_CLASSIFICATION_MISSING")
        h0 = exact_u64(row.get("d0PhysicalBytes"), "d0PhysicalBytes")
        demand(row.get("d0AppliedTargetSha256") == source["targetSha256"],
               "ORACLE_D0_DECODE_NOT_VERIFIED")
        status = row.get("scannerStatus")
        demand(status in ALLOWED_SCANNER, "ORACLE_SCANNER_STATUS")
        base_slots = exact_u64(row.get("baseSlotCount"), "baseSlotCount", allow_zero=True)
        target_slots = exact_u64(row.get("targetSlotCount"), "targetSlotCount", allow_zero=True)
        demand(base_slots <= MAX_SLOTS and target_slots <= MAX_SLOTS,
               "ORACLE_SLOT_CAP")
        verified = status == "VERIFIED_BOTH"
        if verified:
            demand(source["structuralD3Potential"] is True and
                   source["baseSize"] <= MAX_FILE and source["targetSize"] <= MAX_FILE,
                   "ORACLE_VERIFIED_OUTSIDE_D3_SCOPE")
            demand(base_slots > 0 and target_slots > 0, "ORACLE_EMPTY_VERIFIED_TRANSFORM")
            demand(row.get("baseReconstructedSha256") == source["baseSha256"] and
                   row.get("targetReconstructedSha256") == source["targetSha256"],
                   "ORACLE_ROUNDTRIP_NOT_VERIFIED")
            candidate_rows += 1
        else:
            demand(base_slots == 0 and target_slots == 0,
                   "ORACLE_FALLBACK_MUST_CLEAR_SLOTS")
            demand(row.get("baseReconstructedSha256") is None and
                   row.get("targetReconstructedSha256") is None,
                   "ORACLE_FALLBACK_ROUNDTRIP_FORGERY")
        inverse_floor = SIDECAR_HEADER + SIDECAR_SLOT * target_slots if verified else 0
        ideal = min(h0, inverse_floor) if verified else h0
        if verified and h0 <= inverse_floor:
            floor_nonhelpful += 1
        upper = h0 - ideal
        item = family_total.setdefault(key[0], {
            "changedFiles": 0, "d0PhysicalBytes": 0, "idealMinimumPhysicalBytes": 0,
            "upperBoundSavingBytes": 0, "verifiedScannerFiles": 0,
            "verifiedSlotCount": 0,
        })
        item["changedFiles"] += 1
        item["d0PhysicalBytes"] += h0
        item["idealMinimumPhysicalBytes"] += ideal
        item["upperBoundSavingBytes"] += upper
        if verified:
            item["verifiedScannerFiles"] += 1
            item["verifiedSlotCount"] += target_slots
            # Use distinct (base,target) *pairs*, not target alone: different
            # bases can legitimately produce different patch costs.
            identity = (source["baseSha256"], source["targetSha256"])
            ratio = upper / h0
            eligible_ratios[identity] = min(eligible_ratios.get(identity, ratio), ratio)
        sum_d0 += h0
        sum_ideal += ideal
    demand(seen == set(expected), "ORACLE_MISSING_CHANGED_FILE")
    demand(sum_d0 > 0, "ORACLE_EMPTY_BASELINE")
    upper = sum_d0 - sum_ideal
    fraction = upper / sum_d0
    answer = {
        "schema": OUTPUT_SCHEMA,
        "status": "OPTIMISTIC_CEILING_ONLY_NO_PATCH_SIZE_VERDICT",
        "sourcePairsSha256": PAIRS_SHA,
        "sourcePlanSha256": CALIBRATION_PLAN_SHA,
        "population": "calibration-only",
        "assumption": "normalized patch payload and all framing are zero bytes; CDN1 target inverse sidecar is raw 44 + 8*N bytes; fallback is D0",
        "changedRows": len(seen),
        "scannerVerifiedRows": candidate_rows,
        "scannerVerifiedDistinctPairs": len(eligible_ratios),
        "sidecarFloorNotBetterThanD0Rows": floor_nonhelpful,
        "d0PhysicalBytes": sum_d0,
        "idealMinimumPhysicalBytes": sum_ideal,
        "upperBoundSavingBytes": upper,
        "upperBoundSavingFraction": fraction,
        "evenIdealCanReach5Percent": fraction >= 0.05,
        "medianDistinctPairIdealSavingFraction": (
            statistics.median(eligible_ratios.values()) if eligible_ratios else None
        ),
        "families": dict(sorted(family_total.items())),
        "note": ("A high upper bound does not imply real gains. This score is "
                 "not production/holdout evidence. Actual normalized patch "
                 "bytes, container framing, CPU and decoder verification "
                 "must be tested in later separately preregistered runs."),
    }
    return answer


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--pairs", type=Path, required=True)
    p.add_argument("--input", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    try:
        pairs = load_locked_pairs(args.pairs)
        doc = json.loads(args.input.read_text(encoding="utf-8"))
        answer = evaluate(doc, pairs)
        if args.output.exists():
            raise OracleError("ORACLE_OUTPUT_ALREADY_EXISTS")
        args.output.write_text(json.dumps(answer, sort_keys=True, indent=2) + "\n",
                               encoding="utf-8")
    except (OracleError, OSError, KeyError, TypeError, json.JSONDecodeError) as exc:
        p.error(str(exc))
    print("PATCH-DOTNET-001 D3 optimistic calibration-only upper bound "
          f"{answer['upperBoundSavingFraction']:.8f} (NOT measured savings)")


if __name__ == "__main__":
    main()
