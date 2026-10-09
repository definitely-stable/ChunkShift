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
                       baseParserExit=0, targetParserExit=6)
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
    def test_structural_inventory_mismatch_fails_closed(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "inventory.json"
            row = {
                "datasetRole": "calibration", "family": "dotnet-aspnetcore-win-x64",
                "baseVersion": "old", "targetVersion": "new", "path": "app.dll",
                "base": {"kind": 1, "detail": "PE_NATIVE"},
                "target": {"kind": 1, "detail": "PE_NATIVE"},
                "gateEligible": True, "targetBytes": 32,
            }
            inventory = {
                "schema": "chunkshift.patch-gap-g4-inventory.v1",
                "protocolCommit": "5372678ae8451a71cc95eb24f30855cbbd7e0633",
                "corpusPairsSha256": mod.EXPECTED_LOCK,
                "rows": [row],
            }
            path.write_text(json.dumps(inventory), encoding="utf-8")
            reference = {
                "datasetRole": "calibration",
                "frozenInventorySha256": mod.digest(path.read_bytes()),
                "files": [{
                    "datasetRole": "calibration", "family": row["family"],
                    "baseVersion": "old", "targetVersion": "new",
                    "path": "app.dll", "structuralCandidate": True,
                    "g4BcjGateEligible": True, "baseKind": "PE_NATIVE",
                    "targetKind": "PE_NATIVE", "targetBytes": 32,
                }],
            }
            mod.verify_frozen_population(reference, path)
            reference["files"][0]["structuralCandidate"] = False
            with self.assertRaisesRegex(ValueError, "structural candidate changed"):
                mod.verify_frozen_population(reference, path)
            reference["files"][0]["structuralCandidate"] = True
            reference["files"][0]["targetBytes"] = 31
            with self.assertRaisesRegex(ValueError, "target file length changed"):
                mod.verify_frozen_population(reference, path)
            reference["files"][0]["targetBytes"] = 32
            reference["frozenInventorySha256"] = "0" * 64
            with self.assertRaisesRegex(ValueError, "inventory SHA-256 mismatch"):
                mod.verify_frozen_population(reference, path)

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

    def test_tool_io_failure_cannot_be_relabelled_unsupported(self):
        reference, controls = build()
        reference["files"][2]["targetParserExit"] = 2
        with self.assertRaisesRegex(ValueError, "exit code 6"):
            mod.verify(reference, controls, "calibration")

    def test_missing_original_h0_key_fails_closed(self):
        reference, controls = build()
        reference["files"][1]["path"] = "not-in-frozen-control"
        with self.assertRaisesRegex(ValueError, "frozen H0 file identity"):
            mod.verify(reference, controls, "calibration")


if __name__ == "__main__":
    unittest.main()
