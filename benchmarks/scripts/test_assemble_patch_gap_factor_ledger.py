"""Tests for retained PATCH-GAP-001 F0 factor ledger (stdlib only)."""
import copy
import importlib.util
import pathlib
import unittest
from unittest import mock

SCRIPT = pathlib.Path(__file__).with_name("assemble_patch_gap_factor_ledger.py")
spec = importlib.util.spec_from_file_location("patch_gap_ledger", SCRIPT)
ledger = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ledger)


class PatchGapFactorLedgerTests(unittest.TestCase):
    def test_frozen_retained_evidence_recomputes_without_reference_claim(self):
        report = ledger.build()
        self.assertFalse(report["readyToCloseIssue183"])
        self.assertFalse(report["readyToCloseGate251"])
        self.assertEqual(report["baseline"]["evaluationBytes"], 26_363_364)
        self.assertEqual(report["factors"]["G1"]["maxEvaluationSavingsBytes"], 599_456)
        self.assertEqual(report["factors"]["G2"]["calibrationOracleSavingsBytes"], 174)
        self.assertEqual(report["factors"]["G2"]["h6oEvaluationPatchBytes"], 30_248_709)
        self.assertEqual(report["factors"]["G3"]["lanes"][0]["savedBytes"], -49_558_971)
        self.assertEqual(report["factors"]["G4_BCJ"]["lane"]["savedBytes"], 251_840)
        self.assertEqual(report["factors"]["G5_PUFFIN"]["supportedEvaluationFiles"], 0)
        self.assertEqual(set(report["referenceLanes"]), {"Zucchini", "HDiffPatch"})
        self.assertTrue(all(not x["meets15PercentGate"]
                            for x in report["factors"]["G1"]["lanes"]))

    def with_mutation(self, key, transform):
        original = ledger._read

        def modified(root, requested):
            data = original(root, requested)
            if requested == key:
                data = copy.deepcopy(data)
                transform(data)
            return data

        with mock.patch.object(ledger, "_read", side_effect=modified):
            with self.assertRaises(ledger.EvidenceError):
                ledger.build()

    def test_rejects_g1_false_saved_bytes(self):
        self.with_mutation("g1", lambda d: d["lanes"][0].update(savedBytes=999999))

    def test_rejects_g1_nesting_regression(self):
        def mutate(d):
            d["lanes"][2]["factorBytes"] = d["lanes"][1]["factorBytes"] + 1
            d["lanes"][2]["savedBytes"] = (
                d["lanes"][2]["h0Bytes"] - d["lanes"][2]["factorBytes"])
            d["lanes"][2]["reductionVsCsp"] = (
                d["lanes"][2]["savedBytes"] / d["lanes"][2]["h0Bytes"])
        self.with_mutation("g1", mutate)

    def test_rejects_g3_false_rfc_gate(self):
        self.with_mutation("g3", lambda d: d["evaluation"]["lanes"][0]
                           .update(meetsRfcSizeGate=True))

    def test_rejects_g4_missing_family_contribution(self):
        self.with_mutation("g4", lambda d: d["splits"]["evaluation"]["families"]
                           ["node-linux-x64"].update(saved=0))

    def test_rejects_changed_calibration_and_evaluation_cohort(self):
        self.with_mutation("inventory", lambda d: d["g4"]["evaluation"]
                           .update(gateEligibleRows=5))

    def test_rejects_g5_falsely_absent(self):
        self.with_mutation("inventory", lambda d: d["g5"]["evaluation"]
                           .update(puffinSupportedRows=1))

    def test_rejects_unpinned_g4_original_source(self):
        self.with_mutation("g4", lambda d: d.update(
            sourceCommit="0" * 40))

    def test_rejects_unpinned_g2_oracle(self):
        self.with_mutation("g2oracle", lambda d: d.update(
            protocolCommit="0" * 40))

    def test_rejects_claim_that_h6o_passed(self):
        self.with_mutation("g2guard", lambda d: d.update(status="PASS"))


if __name__ == "__main__":
    unittest.main()
