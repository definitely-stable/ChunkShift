import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).parent
EVALUATOR_PATH = ROOT / "evaluate_patch_enc_005_phase_a.py"
RECOMPUTE_PATH = ROOT / "recompute_patch_enc_005_phase_a.py"


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


EVALUATOR = load("patch_enc_005_evaluator", EVALUATOR_PATH)
RECOMPUTE = load("patch_enc_005_recompute", RECOMPUTE_PATH)


def compact(lane_overrides=None):
    lane_overrides = lane_overrides or {}
    patch_bytes = {
        "csp": 1000,
        "H4-L1-R2": 1000,
        "H7-L1-R2-E75": 1000,
        "H9-L9-K4-C16-R1M": 960,
        "H9-L12-K4-C16-R1M": 950,
        "H9-L15-K4-C16-R1M": 940,
    }
    platforms = {}
    defaults = {
        "H4-L1-R2": (0.45, 0.60, 1.02),
        "H7-L1-R2-E75": (0.44, 0.59, 1.01),
        "H9-L9-K4-C16-R1M": (1.20, 0.90, 1.01),
        "H9-L12-K4-C16-R1M": (1.30, 0.95, 1.02),
        "H9-L15-K4-C16-R1M": (1.40, 1.00, 1.03),
    }
    for platform in EVALUATOR.PLATFORMS:
        timing = {}
        apply = {"csp": 10.0}
        memory = {}
        for lane in EVALUATOR.LANES:
            wall, cpu, apply_ratio = lane_overrides.get(lane, defaults[lane])
            timing[lane] = {
                "wallRatioMedian": wall,
                "cpuRatioMedian": cpu,
                "wallRatios": [wall] * 5,
                "cpuRatios": [cpu] * 5,
            }
            apply[lane] = 10.0 * apply_ratio
        for lane in EVALUATOR.ALL_LANES:
            memory[lane] = {
                "createPeakOverIdleBytes": 40 * 1024 * 1024,
                "applyPeakOverIdleBytes": 30 * 1024 * 1024,
            }
        platforms[platform] = {
            "timing": timing,
            "applyWallSeconds": apply,
            "memory": memory,
        }
    return {
        "schema": "chunkshift.patch-enc-005-phase-a-compact.v1",
        "sourceCommit": "a" * 40,
        "patchBytes": patch_bytes,
        "platforms": platforms,
    }


class PatchEnc005PhaseAEvaluatorTests(unittest.TestCase):
    def test_speed_and_size_finalists_follow_frozen_order(self):
        result = EVALUATOR.evaluate(compact())

        self.assertEqual("READY_FOR_FIXED_EVALUATION", result["status"])
        self.assertEqual("H7-L1-R2-E75", result["speedFinalist"])
        self.assertEqual("H9-L15-K4-C16-R1M", result["sizeFinalist"])
        self.assertEqual(
            ["H7-L1-R2-E75", "H9-L15-K4-C16-R1M"],
            result["finalists"],
        )
        self.assertIn("speed", result["lanes"]["H7-L1-R2-E75"]["qualificationBranches"])
        self.assertIn("size", result["lanes"]["H9-L15-K4-C16-R1M"]["qualificationBranches"])

    def test_memory_guardrail_removes_otherwise_eligible_lane(self):
        document = compact()
        for platform in EVALUATOR.PLATFORMS:
            document["platforms"][platform]["memory"]["H7-L1-R2-E75"][
                "createPeakOverIdleBytes"
            ] = EVALUATOR.CREATE_BOUND_BYTES + 1

        result = EVALUATOR.evaluate(document)

        self.assertFalse(result["lanes"]["H7-L1-R2-E75"]["eligible"])
        self.assertIn("memory-bound", result["lanes"]["H7-L1-R2-E75"]["reasons"])
        self.assertNotEqual("H7-L1-R2-E75", result["speedFinalist"])

    def test_apply_guardrail_is_required_on_every_platform(self):
        document = compact()
        document["platforms"]["win-x64"]["applyWallSeconds"]["H4-L1-R2"] = 11.01

        result = EVALUATOR.evaluate(document)

        self.assertFalse(result["lanes"]["H4-L1-R2"]["eligible"])
        self.assertIn("apply>1.10", result["lanes"]["H4-L1-R2"]["reasons"])

    def test_no_qualifying_lane_rejects_without_opening_evaluation(self):
        document = compact(
            {
                lane: (1.60, 1.00, 1.00)
                for lane in EVALUATOR.LANES
            }
        )
        document["patchBytes"] = {lane: 1000 for lane in EVALUATOR.ALL_LANES}

        result = EVALUATOR.evaluate(document)

        self.assertEqual("REJECT", result["status"])
        self.assertEqual([], result["finalists"])
        self.assertEqual([], result["pareto"])

    def test_exact_byte_thresholds_do_not_depend_on_float_rounding(self):
        document = compact()
        document["patchBytes"]["H4-L1-R2"] = 1020
        for platform in EVALUATOR.PLATFORMS:
            document["platforms"][platform]["timing"]["H4-L1-R2"]["wallRatioMedian"] = 0.50
            document["platforms"][platform]["timing"]["H4-L1-R2"]["wallRatios"] = [0.50] * 5

        result = EVALUATOR.evaluate(document)

        self.assertIn("speed", result["lanes"]["H4-L1-R2"]["qualificationBranches"])

    def test_h0_memory_violation_invalidates_calibration_evidence(self):
        document = compact()
        document["platforms"]["linux-x64"]["memory"]["csp"][
            "createPeakOverIdleBytes"
        ] = EVALUATOR.CREATE_BOUND_BYTES + 1

        with self.assertRaisesRegex(ValueError, "H0 violates frozen memory bounds"):
            EVALUATOR.evaluate(document)

    def test_independent_recompute_matches_selection(self):
        document = compact()
        verdict = EVALUATOR.evaluate(document)
        independent = RECOMPUTE.recompute(document)

        self.assertEqual(verdict["pareto"], independent["pareto"])
        self.assertEqual(verdict["speedFinalist"], independent["speedFinalist"])
        self.assertEqual(verdict["sizeFinalist"], independent["sizeFinalist"])
        self.assertEqual(verdict["finalists"], independent["finalists"])
        self.assertEqual(verdict["status"], independent["status"])


if __name__ == "__main__":
    unittest.main()
