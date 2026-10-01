import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("run_patch_enc_005_paired.py")
SPEC = importlib.util.spec_from_file_location("patch_enc_005_paired", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class PatchEnc005PairedTests(unittest.TestCase):
    def test_rotation_is_cyclic_by_one(self):
        lanes = ["a", "b", "c"]
        self.assertEqual(["a", "b", "c"], MODULE.rotated(lanes, 0))
        self.assertEqual(["b", "c", "a"], MODULE.rotated(lanes, 1))
        self.assertEqual(["c", "a", "b"], MODULE.rotated(lanes, 2))
        self.assertEqual(["a", "b", "c"], MODULE.rotated(lanes, 3))

    def test_noise_rule_is_strictly_more_than_25_percent_of_mean(self):
        self.assertFalse(MODULE.bracket_is_noisy(0.875, 1.125))
        self.assertTrue(MODULE.bracket_is_noisy(0.87, 1.13))
        self.assertTrue(MODULE.bracket_is_noisy(0.0, 0.0))

    def test_aggregate_sums_only_create_evidence(self):
        result = {
            "files": [
                {
                    "createSeconds": 1.25,
                    "patchBytes": 10,
                    "createMetrics": {"cpuSeconds": 0.5},
                },
                {
                    "createSeconds": 2.75,
                    "patchBytes": 20,
                    "createMetrics": {"cpuSeconds": 1.5},
                },
            ]
        }
        self.assertEqual(
            {"wallSeconds": 4.0, "cpuSeconds": 2.0, "patchBytes": 30},
            MODULE.aggregate(result),
        )


if __name__ == "__main__":
    unittest.main()
