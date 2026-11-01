#!/usr/bin/env python3
"""PATCH-GAP-001 F0: independent, fail-closed retained-factor evidence ledger.

This is NOT a production encoder, reference-run substitute or final release vote.
Only recomputes source-locked committed evidence for G1/G2/G3/G4/G5.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
ENC_PROTOCOL = "96fd9b296d6998cac397e61041f22df51e6dd43c"
DATASET = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
H0_EVALUATION = 26_363_364
H0_CALIBRATION = 11_860_274
SIZE_GATE = 0.15
# Frozen, already-reviewed source identities; do not accept an arbitrary
# 40-character SHA string as provenance for a historical factor verdict.
SOURCE_COMMITS = {
    "g1": "8983eb11faa3e7c2df17b74d201249a19dd3dc13",
    "g3": "4975b0d4121b0bee426b9a7e3fce140918cce151",
    "g4": "c71f0c8fa483c45bf65fee4850d293c45f7878a6",
    "g2oracle": "489d3f2fe2582cc6251ee2fa87cd23bad1793b01",
    "g2guard": "148ee8adc6df47cce0d866ea2e6eb25d684713d7",
}

FILES = {
    "g1": "docs/research/results/data/PATCH-GAP-001-G1-EVALUATION-EVIDENCE-20261006-001/evaluation.json",
    "g3": "docs/research/results/data/PATCH-GAP-001-G3-EVALUATION-EVIDENCE-20261007-001/evaluation.json",
    "g4": "docs/research/results/data/PATCH-GAP-001-G4-EVIDENCE-20261008-001/summary.json",
    "inventory": "docs/research/results/data/PATCH-GAP-001-20261003-001/aggregates.json",
    "g2oracle": "docs/research/results/data/PATCH-ENC-005-G2-EVIDENCE-20261003-001/verdict.json",
    "g2guard": "docs/research/results/data/PATCH-ENC-005-H6O-EVIDENCE-20261004-001/verdict.json",
}


class EvidenceError(ValueError):
    pass


def require(ok, message):
    if not ok:
        raise EvidenceError(message)


def _read(root, key):
    path = root / FILES[key]
    return json.loads(path.read_text(encoding="utf-8"))


def _lane(source, expected_h0):
    name = source["lane"]
    base = source["h0Bytes"]
    actual = source["factorBytes"]
    savings = base - actual
    require(base == expected_h0, f"{name}: H0 anchor mismatch")
    require(savings == source["savedBytes"], f"{name}: savings mismatch")
    ratio = savings / base
    require(abs(ratio - source["reductionVsCsp"]) < 1e-12,
            f"{name}: reduction mismatch")
    pass_gate = ratio >= SIZE_GATE
    require(pass_gate == source["meetsRfcSizeGate"],
            f"{name}: size gate mismatch")
    return {
        "lane": name, "h0Bytes": base, "factorBytes": actual,
        "savedBytes": savings, "reductionVsH0": ratio,
        "meets15PercentGate": pass_gate
    }


def build(root=ROOT):
    root = Path(root)
    g1, g3, g4, inventory, oracle, guard = (
        _read(root, k) for k in FILES
    )
    for name, value in (("g1", g1), ("g3", g3), ("g4", g4)):
        require(value["protocolCommit"] == PROTOCOL,
                f"{name}: frozen protocol mismatch")
        require(value["sourceCommit"] == SOURCE_COMMITS[name],
                f"{name}: frozen source SHA mismatch")
    for name, value in (("g1", g1), ("g3", g3), ("g2oracle", oracle),
                        ("g2guard", guard)):
        require(value["datasetSha256"] == DATASET,
                f"{name}: dataset mismatch")
    require(g1["schema"] == "chunkshift.patch-gap-001-g1-evaluation-result.v1"
            and g1["status"] == "REJECT" and g1["h0AnchorPass"],
            "G1 is not accepted frozen evidence")
    require(g3["schema"] == "chunkshift.patch-gap-001-g3-evaluation-result.v1"
            and g3["status"] == "REJECT" and g3["h0AnchorPass"],
            "G3 is not accepted frozen evidence")
    require(g4["schema"] == "chunkshift.patch-gap-g4-evidence.v1"
            and g4["status"] == "REJECT",
            "G4 is not accepted frozen evidence")

    require(g1["sampleFileCount"] == 844 and
            g3["evaluation"]["sampleFileCount"] == 844,
            "Evaluation row count mismatch")
    require(g3["calibration"]["sampleFileCount"] == 1049,
            "Calibration row count mismatch")
    g1_lanes = [_lane(x, H0_EVALUATION) for x in g1["lanes"]]
    require([x["lane"] for x in g1_lanes] ==
            ["G1-B1-R64", "G1-B4-R256", "G1-B8-R512", "G1-B32-R2048"],
            "G1 frozen lane set mismatch")
    require(all(a["factorBytes"] >= b["factorBytes"]
                for a, b in zip(g1_lanes, g1_lanes[1:])),
            "G1 nesting non-regression violated")
    require(not any(x["meets15PercentGate"] for x in g1_lanes),
            "G1 passed an RFC gate; review required")
    require(g1["nextAction"] == "RUN_G3_CALIBRATION_AND_EVALUATION",
            "Unexpected G1 decision lineage")

    g3_lanes = [_lane(x, H0_EVALUATION)
                for x in g3["evaluation"]["lanes"]]
    require([x["lane"] for x in g3_lanes] == ["G3-RUN", "G3-FILE"],
            "G3 frozen lane set mismatch")
    for x in g3["calibration"]["lanes"]:
        _lane(x, H0_CALIBRATION)
    require(not any(x["meets15PercentGate"] for x in g3_lanes),
            "G3 passed an RFC gate; review required")

    g4_e = g4["splits"]["evaluation"]
    g4_c = g4["splits"]["calibration"]
    g4_lane = _lane(g4_e["lane"], H0_EVALUATION)
    _lane(g4_c["lane"], H0_CALIBRATION)
    for name, split, count in (("G4 evaluation", g4_e, 844),
                               ("G4 calibration", g4_c, 1049)):
        require(split["lane"]["fileCount"] == count,
                name + ": file count mismatch")
        for k, lane_field in (("h0", "h0Bytes"), ("g4", "factorBytes"),
                               ("saved", "savedBytes"), ("files", "fileCount"),
                               ("eligible", "gateEligibleFiles")):
            require(sum(f[k] for f in split["families"].values()) ==
                    split["lane"][lane_field],
                    name + ": family reconciliation failed for " + k)
    require(not g4_lane["meets15PercentGate"], "G4 unexpectedly passed size gate")
    require(g4_e["lane"]["gateEligibleFiles"] ==
            inventory["g4"]["evaluation"]["gateEligibleRows"],
            "G4 evaluation subset membership mismatch")
    require(g4_c["lane"]["gateEligibleFiles"] ==
            inventory["g4"]["calibration"]["gateEligibleRows"],
            "G4 calibration subset membership mismatch")

    require(inventory["g5"]["status"] == "NOT_PRESENT", "G5 not absent")
    require(inventory["g5"]["evaluation"]["rows"] == 844
            and inventory["g5"]["calibration"]["rows"] == 1049
            and inventory["g5"]["evaluation"]["puffinSupportedRows"] == 0
            and inventory["g5"]["calibration"]["puffinSupportedRows"] == 0,
            "G5 frozen corpus coverage mismatch")
    require(oracle["protocolCommit"] == ENC_PROTOCOL and
            guard["protocolCommit"] == ENC_PROTOCOL,
            "G2 producer protocol mismatch")
    require(oracle["sourceCommit"] == SOURCE_COMMITS["g2oracle"] and
            guard["sourceCommit"] == SOURCE_COMMITS["g2guard"],
            "G2 original run source SHA mismatch")
    require(oracle["schema"] == "chunkshift.patch-enc-005-g2-verdict.v1"
            and oracle["status"] == "MISS"
            and oracle["datasetRole"] == "calibration"
            and oracle["rowCount"] == 256
            and oracle["h0SampleCostBytes"] - oracle["g2SampleCostBytes"]
            == oracle["savedBytes"]
            and oracle["pairPassCount"] < oracle["requiredPairPassCount"],
            "G2 calibration oracle lineage mismatch")
    require(guard["schema"] == "chunkshift.patch-enc-005-h6o-guard-verdict.v1"
            and guard["status"] == "MISS"
            and guard["datasetRole"] == "evaluation"
            and guard["h0PatchBytes"] == H0_EVALUATION
            and guard["h6PatchBytes"] > H0_EVALUATION
            and guard["nextAction"] == "STOP_RESEMBLANCE",
            "G2 H6-O evaluation verdict mismatch")
    require(not any(x["meets15PercentGate"]
                    for x in [*g1_lanes, *g3_lanes, g4_lane]),
            "At least one frozen CSP-costed factor requires an RFC decision")

    return {
        "schema": "chunkshift.patch-gap-001-factor-ledger.v1",
        "experimentId": "PATCH-GAP-001",
        "frozenProtocolCommit": PROTOCOL,
        "frozenDatasetSha256": DATASET,
        "baseline": {"calibrationBytes": H0_CALIBRATION,
                     "evaluationBytes": H0_EVALUATION,
                     "evaluationFileCount": 844},
        "factors": {
            "G1": {"status": "REJECT", "lanes": g1_lanes,
                   "maxEvaluationSavingsBytes": max(x["savedBytes"] for x in g1_lanes),
                   "baseReadAmplification": g1["baseReadAmplification"]},
            "G2": {"status": "REJECT_STOP_RESEMBLANCE",
                   "calibrationOracleRows": oracle["rowCount"],
                   "calibrationOracleSavingsBytes": oracle["savedBytes"],
                   "h6oEvaluationH0Bytes": guard["h0PatchBytes"],
                   "h6oEvaluationPatchBytes": guard["h6PatchBytes"],
                   "noUnrunSelectorClaim": True},
            "G3": {"status": "REJECT", "lanes": g3_lanes},
            "G4_BCJ": {"status": "REJECT", "lane": g4_lane,
                       "eligibleEvaluationFiles": g4_e["lane"]["gateEligibleFiles"]},
            "G5_PUFFIN": {"status": "NOT_PRESENT_IN_FROZEN_CORPUS",
                          "supportedCalibrationFiles": 0,
                          "supportedEvaluationFiles": 0}
        },
        "referenceLanes": {
            "Zucchini": "NO_COMMITTED_REAL_BINARY_CALIBRATION_AND_EVALUATION_EVIDENCE",
            "HDiffPatch": "NO_COMMITTED_FINAL_COMPARATIVE_REFERENCE_EVIDENCE"
        },
        "decision": "NO_RFC_FROM_TESTED_SINGLE_FACTORS__RESEARCH_FREEZE_STILL_OPEN",
        "readyToCloseIssue183": False,
        "readyToCloseGate251": False,
        "limitations": [
            "G2 oracle is calibration-only and must not be used as full evaluation savings.",
            "Zucchini reference is whole-file subset-only, not the 15% CSP factor gate.",
            "G5 NOT_PRESENT applies only to the fixed materialized corpus.",
            "No cross-factor interaction result is inferred from individually rejected factors.",
            "No reference patch bytes or unavailable GitHub-hosted runner evidence is invented."
        ],
        "sourceDocuments": FILES
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo-root", type=Path, default=ROOT)
    ap.add_argument("--output", type=Path)
    ap.add_argument("--require-complete", action="store_true",
                    help="Fail closed if the research freeze can be exited.")
    args = ap.parse_args()
    result = build(args.repo_root)
    output = json.dumps(result, sort_keys=True, indent=2) + "\n"
    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output, encoding="utf-8")
    else:
        print(output)
    if args.require_complete and not result["readyToCloseIssue183"]:
        raise SystemExit("PATCH-GAP-001 has unresolved external reference lanes")


if __name__ == "__main__":
    main()
