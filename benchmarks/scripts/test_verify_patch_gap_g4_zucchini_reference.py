#!/usr/bin/env python3
"""Independent Zucchini reference attribution verifier regression coverage."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
MODULE = HERE / "verify_patch_gap_g4_zucchini_reference.py"
spec = importlib.util.spec_from_file_location("zucchini_audit", MODULE)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def build(role="calibration"):
    count = 1049 if role == "calibration" else 814
    rows = []
    controls = {}
    for i in range(count):
        path = f"object-{i}"
        key = (role, "dotnet-aspnetcore-win-x64" if role == "calibration" else "node-linux-x64",
               "old", "new", path)
        controls[key] = 500
        row = {"datasetRole": role, "family": key[1], "baseVersion": "old",
               "targetVersion": "new", "path": path, "structuralCandidate": i % 2 == 0,
               "targetSha256": "a" * 64}
        if i == 0:
            row.update(status="VERIFIED", patchBytes=100, patchSha256="c" * 64,
                       reconstructionSha256="a" * 64, baseParserExit=0, targetParserExit=0)
        elif i % 2 == 0:
            row.update(status="UNSUPPORTED_TARGET", patchBytes=None,
                       baseParserExit=0, targetParserExit=1)
        else:
            row.update(status="OUTSIDE_STRUCTURAL_SUBSET", patchBytes=None)
        rows.append(row)
    summary = {}
    for r in rows:
        summary[r["status"]] = summary.get(r["status"], 0) + 1
    return {
        "schema": mod.EXPECTED_SCHEMA, "datasetRole": role, "mode": "measure",
        "corpusPairsSha256": mod.EXPECTED_LOCK,
        "tool": {"binarySha256": "b" * 64},
        "files": rows, "counts": summary,
    }, controls


class FrozenReferenceAuditTests(unittest.TestCase):
    def test_original_committed_h0_roots(self):
        source = ROOT / mod.EVIDENCE
        for role, count, expected in (
            ("calibration", 1049, 11860274),
            ("evaluation", 844, 26363364),
        ):
            with self.subTest(role=role):
                values = mod.frozen_h0(source, role)
                self.assertEqual(len(values), count)
                self.assertEqual(sum(values.values()), expected)

    def test_reference_matched_subset_not_global_gate(self):
        reference, controls = build()
        report = mod.verify(reference, controls, "calibration")
        self.assertEqual(report["matchedVerifiedFiles"], 1)
        self.assertEqual(report["matchedH0PatchBytes"], 500)
        self.assertEqual(report["matchedZucchiniPatchBytes"], 100)
        self.assertEqual(report["rfcSizeGate"], "NOT_APPLICABLE")

    def test_zero_byte_success_fails_closed(self):
        reference, controls = build("evaluation")
        reference["files"][0]["patchBytes"] = 0
        with self.assertRaisesRegex(ValueError, "without physical patch bytes"):
            mod.verify(reference, controls, "evaluation")

    def test_hidden_unsupported_patch_cost_fails_closed(self):
        reference, controls = build()
        reference["files"][2]["patchBytes"] = 0
        with self.assertRaisesRegex(ValueError, "unsupported row carries a patch cost"):
            mod.verify(reference, controls, "calibration")

    def test_missing_reference_row_fails_closed(self):
        reference, controls = build()
        reference["files"].pop()
        with self.assertRaisesRegex(ValueError, "reference population"):
            mod.verify(reference, controls, "calibration")

    def test_different_population_and_status_fail_closed(self):
        reference, controls = build()
        reference["files"][1]["status"] = "VERIFIED"
        with self.assertRaisesRegex(ValueError, "verified row without physical patch bytes"):
            mod.verify(reference, controls, "calibration")

    def test_missing_original_h0_key_fails_closed(self):
        reference, controls = build()
        reference["files"][1]["path"] = "not-in-frozen-control"
        with self.assertRaisesRegex(ValueError, "frozen H0 file identity"):
            mod.verify(reference, controls, "calibration")


if __name__ == "__main__":
    unittest.main()
