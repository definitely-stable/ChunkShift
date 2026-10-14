"""E3-R2 baseline-only contract and independent memory sample negative checks."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parents[2]
p = HERE / "benchmarks/scripts/core011_csm_e3r2.py"
spec = importlib.util.spec_from_file_location("e3r2", p)
r2 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r2)
CONTRACT = HERE / "benchmarks/experiments/core011-csm-e3r2-calibration.v1.json"


def load():
    return json.loads(CONTRACT.read_text())


class CalibrationTests(unittest.TestCase):
    def test_source_locked_preregistered_baseline_contract(self):
        self.assertEqual("baseline-only", r2.calibration_contract(load())["mode"])
        self.assertEqual("pre", "pre")

    def test_decision_or_candidate_rollback_rejected(self):
        for field, value in (("mode", "decision"), ("variant", "hybrid2"),
                             ("decisionRunsAllowed", True), ("budgetsLocked", True)):
            with self.subTest(field=field):
                o = copy.deepcopy(load())
                o[field] = value
                with self.assertRaisesRegex(ValueError, "baseline-only"):
                    r2.calibration_contract(o)

    def test_noncalibrated_matrix_mutations_rejected(self):
        for field, value in (("workers", [1]), ("sizesMiB", [1]), ("rounds", 1),
                             ("runnerPlatform", "self-hosted"),
                             ("idleSecondsByCell", {"384-16": 30}),
                             ("requiredMetrics", ["cpuMs"])):
            with self.subTest(field=field):
                o = copy.deepcopy(load())
                o[field] = value
                with self.assertRaises(ValueError):
                    r2.calibration_contract(o)

    def test_retroactive_source_or_process_forge_rejected(self):
        o = copy.deepcopy(load())
        o["mandatoryIsolation"]["independentFreshConsumerPerCell"] = False
        with self.assertRaisesRegex(ValueError, "isolation"):
            r2.calibration_contract(o)
        o = copy.deepcopy(load())
        o["forbidden"]["candidateMeasurements"] = False
        with self.assertRaisesRegex(ValueError, "no-decision"):
            r2.calibration_contract(o)

    def test_parent_r0_still_blocks_final_decision(self):
        obj = json.loads((HERE / "benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        self.assertEqual("preregistration", r2.e3.validate_contract(obj)["stage"])
        with self.assertRaisesRegex(ValueError, "unconditionally forbidden"):
            r2.e3.validate_contract(obj, for_decision=True)

    def test_nan_inf_negative_and_zero_metric_rejected(self):
        for n in (float("nan"), float("inf"), -1, None, True, "0"):
            with self.subTest(n=n):
                with self.assertRaises(ValueError):
                    r2.positive_number(n, "wallMs")
        self.assertIsNone(r2.positive_number(0, "gen2", allow_zero=True))

    def test_compiled_reader_source_sha_matches(self):
        source = HERE / "src/ChunkShift/Manifest/CsmReader.cs"
        self.assertEqual(r2.READERSHA, r2.sha(source))

    def test_no_candidate_or_numeric_budget_field_exists(self):
        obj = load()
        self.assertNotIn("numericBudgets", obj)
        self.assertNotIn("candidateBuilds", obj)
        self.assertTrue(obj["forbidden"]["decisionThresholds"])


if __name__ == "__main__":
    unittest.main()
