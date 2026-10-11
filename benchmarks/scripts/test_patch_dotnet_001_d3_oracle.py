"""D3 idealized cost oracle: fail-closed, whole-population, slot-sidecar tests."""
from __future__ import annotations

import copy
import hashlib
import json
import unittest

import patch_dotnet_001_d3_oracle as oracle

COUNTS = [("ps-linux-fdd", 263), ("ps-win-fdd", 282), ("ps-win-sc", 697)]


def fake_inputs(verified=100):
    pairs = []
    rows = []
    count = 0
    for family, quantity in COUNTS:
        changed = []
        for j in range(quantity):
            path = f"payload/file-{j:04}.dll"
            base_sha = hashlib.sha256(f"base-{family}-{j}".encode()).hexdigest()
            target_sha = hashlib.sha256(f"target-{family}-{j}".encode()).hexdigest()
            proof = count < verified
            member = {
                "path": path,
                "baseSha256": base_sha, "targetSha256": target_sha,
                "baseSize": 1000, "targetSize": 1000,
                "evaluationContentDuplicate": False,
                "structuralD3Potential": proof,
            }
            changed.append(member)
            rows.append({
                "family": family, "base": "7.5.3", "target": "7.5.4", "path": path,
                "baseSha256": base_sha, "targetSha256": target_sha,
                "baseSize": 1000, "targetSize": 1000,
                "d0PhysicalBytes": 1000,
                "d0AppliedTargetSha256": target_sha,
                "scannerStatus": "VERIFIED_BOTH" if proof else "FALLBACK",
                "baseSlotCount": 1 if proof else 0,
                "targetSlotCount": 1 if proof else 0,
                "baseReconstructedSha256": base_sha if proof else None,
                "targetReconstructedSha256": target_sha if proof else None,
            })
            count += 1
        pairs.append({"family": family, "base": "7.5.3", "target": "7.5.4",
                      "role": "calibration", "changed": changed})
    data = {
        "schema": oracle.INPUT_SCHEMA,
        "experimentId": "PATCH-DOTNET-001",
        "role": "calibration",
        "sourcePairsSha256": oracle.PAIRS_SHA,
        "sourcePlanSha256": oracle.CALIBRATION_PLAN_SHA,
        "d0PolicyId": "CSP_DEFAULT_H0",
        "baselineEvidence": {"sha256": "a"*64, "gitCommit": "b"*40, "runId": 100},
        "scannerEvidence": {"sha256": "c"*64, "gitCommit": "d"*40, "runId": 101},
        "rows": rows,
    }
    return data, {"schema": oracle.PAIRS_SCHEMA, "pairs": pairs}


class OracleTests(unittest.TestCase):
    def test_whole_corpus_and_exact_sidecar_charge(self):
        data, pairs = fake_inputs(100)
        result = oracle.evaluate(data, pairs)
        self.assertEqual(1242, result["changedRows"])
        self.assertEqual(100, result["scannerVerifiedRows"])
        self.assertEqual(100, result["scannerVerifiedDistinctPairs"])
        self.assertEqual(1242000, result["d0PhysicalBytes"])
        self.assertEqual(94800, result["upperBoundSavingBytes"])
        self.assertEqual(1147200, result["idealMinimumPhysicalBytes"])
        self.assertAlmostEqual(0.948, result["medianDistinctPairIdealSavingFraction"])
        self.assertTrue(result["evenIdealCanReach5Percent"])
        self.assertEqual("OPTIMISTIC_CEILING_ONLY_NO_PATCH_SIZE_VERDICT", result["status"])

    def test_empty_scanner_oracle_does_not_invent_savings(self):
        data, pairs = fake_inputs(0)
        result = oracle.evaluate(data, pairs)
        self.assertEqual(0, result["upperBoundSavingBytes"])
        self.assertIsNone(result["medianDistinctPairIdealSavingFraction"])
        self.assertFalse(result["evenIdealCanReach5Percent"])

    def test_large_sidecar_cannot_make_positive_gain(self):
        data, pairs = fake_inputs(1)
        data["rows"][0]["d0PhysicalBytes"] = 51
        result = oracle.evaluate(data, pairs)
        self.assertEqual(0, result["upperBoundSavingBytes"])
        self.assertEqual(1, result["sidecarFloorNotBetterThanD0Rows"])

    def test_missing_duplicated_and_extra_row_rejected(self):
        data, pairs = fake_inputs()
        for change in ("missing", "dupe", "extra"):
            with self.subTest(change=change):
                candidate = copy.deepcopy(data)
                if change == "missing":
                    candidate["rows"].pop()
                if change == "dupe":
                    candidate["rows"].append(candidate["rows"][0])
                if change == "extra":
                    row = copy.deepcopy(candidate["rows"][0])
                    row["path"] = "payload/invented.dll"
                    candidate["rows"].append(row)
                with self.assertRaises(oracle.OracleError):
                    oracle.evaluate(candidate, pairs)

    def test_forged_digest_provenance_role_and_roundtrip_rejected(self):
        data, pairs = fake_inputs()
        cases = [
            ("evaluation", lambda d: d.update(role="evaluation")),
            ("pairSha", lambda d: d.update(sourcePairsSha256="0" * 64)),
            ("d0", lambda d: d["rows"][0].update(d0AppliedTargetSha256="0"*64)),
            ("roundtrip", lambda d: d["rows"][0].update(targetReconstructedSha256="0"*64)),
            ("wrong path", lambda d: d["rows"][0].update(targetSize=999)),
            ("unverified", lambda d: d["rows"][0].update(scannerStatus="FALLBACK")),
            ("bool_d0", lambda d: d["rows"][0].update(d0PhysicalBytes=True)),
            ("negative slot", lambda d: d["rows"][0].update(targetSlotCount=-1)),
            ("overflow slots", lambda d: d["rows"][0].update(targetSlotCount=262145)),
        ]
        for name, mutation in cases:
            with self.subTest(case=name), self.assertRaises(oracle.OracleError):
                value = copy.deepcopy(data)
                mutation(value)
                oracle.evaluate(value, pairs)

    def test_no_override_of_frozen_holdout_collision(self):
        data, pairs = fake_inputs()
        pairs["pairs"][0]["changed"][0]["evaluationContentDuplicate"] = True
        with self.assertRaisesRegex(oracle.OracleError, "SEALED_HOLDOUT_OVERLAP"):
            oracle.evaluate(data, pairs)

    def test_boundary_idealized_underestimates_real_cost(self):
        data, pairs = fake_inputs(1)
        r = oracle.evaluate(data, pairs)
        self.assertEqual(948, r["upperBoundSavingBytes"])
        self.assertEqual(1000 - 52, r["upperBoundSavingBytes"])

    def test_identical_source_pair_uses_distinct_pair_metric(self):
        data, pairs = fake_inputs(2)
        # Duplicate content across deployment groups is allowed, but must not
        # create a second "distinct pair" in diagnostic eligible counts.
        p = pairs["pairs"][0]["changed"][0]
        q = pairs["pairs"][0]["changed"][1]
        q["baseSha256"] = p["baseSha256"]
        q["targetSha256"] = p["targetSha256"]
        data["rows"][1]["baseSha256"] = p["baseSha256"]
        data["rows"][1]["targetSha256"] = p["targetSha256"]
        data["rows"][1]["d0AppliedTargetSha256"] = p["targetSha256"]
        data["rows"][1]["baseReconstructedSha256"] = p["baseSha256"]
        data["rows"][1]["targetReconstructedSha256"] = p["targetSha256"]
        summary = oracle.evaluate(data, pairs)
        self.assertEqual(2, summary["scannerVerifiedRows"])
        self.assertEqual(1, summary["scannerVerifiedDistinctPairs"])


if __name__ == "__main__":
    unittest.main()
