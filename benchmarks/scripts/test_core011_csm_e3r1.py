"""E3-R1 strict source-transform and XML oracle validation tests."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "benchmarks/scripts/core011_csm_e3r1.py"
SPEC = importlib.util.spec_from_file_location("e3r1_tests_target", SCRIPT)
e3 = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(e3)
SOURCE = (ROOT / e3.SOURCE).read_text(encoding="utf-8")


class E3R1GuardTests(unittest.TestCase):
    def test_five_source_pinned_compiled_variants_are_different(self):
        candidates = {v: e3.variant_source(SOURCE, v) for v in e3.VARIANTS}
        self.assertEqual(5, len({e3.sha(x.encode()) for x in candidates.values()}))
        self.assertEqual(SOURCE, candidates["eager"])
        self.assertIn("new byte[recordLength]", candidates["lazy"])
        for cap in (1, 2, 4):
            text = candidates["hybrid" + str(cap)]
            self.assertEqual(1, text.count("maxArraysPerBucket: " + str(cap) + ");"))
            self.assertEqual(2, text.count("clearArray: true"))

    def test_reader_source_drift_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "frozen CsmReader"):
            e3.variant_source(SOURCE + "\n", "lazy")

    def test_unknown_variant_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "unregistered"):
            e3.variant_source(SOURCE, "unbounded")

    def test_r0_decision_guard_remains_closed(self):
        contract = json.loads(
            (ROOT / "benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        self.assertEqual("preregistration", e3.R0.validate_contract(contract)["stage"])
        with self.assertRaisesRegex(ValueError, "unconditionally forbidden"):
            e3.R0.validate_contract(contract, for_decision=True)

    def test_invalid_run_id_rejected_before_any_build(self):
        with self.assertRaisesRegex(ValueError, "RunId"):
            e3.preflight(ROOT, "CORE011-CSM-004/ADOPT-unsafe")

    def test_source_git_blob_equals_frozen_contract(self):
        contract = json.loads(
            (ROOT / "benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        self.assertEqual(e3.BASE_BLOB, e3.R0.blob_sha1(SOURCE.encode()))
        self.assertEqual(e3.BASE_BLOB, contract["baseline"]["readerGitBlobSha1"])
        self.assertEqual(e3.BASE_SHA, e3.sha(SOURCE.encode()))

    def test_valid_trx_but_mismatched_ownership_tests_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "net10.0.trx"
            root = ET.Element("TestRun")
            cases = ET.SubElement(root, "Results")
            for i in range(60):
                ET.SubElement(cases, "UnitTestResult", {
                    "testName": "CsmE3R1ReaderOwnershipTests.Oracle" if i < 19
                    else "CsmOtherTests.Test"})
            summary = ET.SubElement(root, "ResultSummary")
            counters = ET.SubElement(summary, "Counters", {
                "total": "60", "passed": "60", "failed": "0"})
            ET.ElementTree(root).write(path)
            self.assertEqual(19, e3.count_trx(path)["ownershipOracles"])
            counters.set("failed", "1")
            ET.ElementTree(root).write(path)
            with self.assertRaisesRegex(ValueError, "incomplete CSM"):
                e3.count_trx(path)
            counters.set("failed", "0")
            for x in list(cases)[:1]:
                cases.remove(x)
            ET.ElementTree(root).write(path)
            with self.assertRaisesRegex(ValueError, "ownership oracle"):
                e3.count_trx(path)

    def test_oracle_is_not_a_committed_core_test_file(self):
        self.assertFalse((ROOT / e3.TEMP_TEST).exists())
        self.assertTrue((ROOT / e3.ORACLE).is_file())


if __name__ == "__main__":
    unittest.main()
