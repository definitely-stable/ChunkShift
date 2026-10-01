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

    def test_median_five_is_the_third_sorted_value(self):
        self.assertEqual(3.0, MODULE.median_five([5.0, 1.0, 3.0, 4.0, 2.0]))
        with self.assertRaises(ValueError):
            MODULE.median_five([1.0, 2.0])

    def test_development_family_sets_are_exact(self):
        MODULE.validate_development_families(
            "calibration",
            "dotnet-runtime-linux-arm64,dotnet-aspnetcore-win-x64",
        )
        with self.assertRaises(ValueError):
            MODULE.validate_development_families(
                "calibration",
                "dotnet-aspnetcore-win-x64",
            )
        # Confirmation is governed by its separately frozen pair-list lock.
        MODULE.validate_development_families("confirmation", None)

    def test_aggregate_sums_create_and_apply_evidence(self):
        result = {
            "files": [
                {
                    "family": "f",
                    "base": "1",
                    "target": "2",
                    "path": "a",
                    "patchSha256": "a" * 64,
                    "createSeconds": 1.25,
                    "patchBytes": 10,
                    "createMetrics": {"cpuSeconds": 0.5},
                    "applyMetrics": {
                        "medianWallSeconds": 0.3,
                        "medianCpuSeconds": 0.2,
                        "medianBaseReads": 4,
                        "medianBaseBytesRead": 100,
                        "samples": [{}, {}, {}, {}, {}],
                    },
                },
                {
                    "family": "f",
                    "base": "1",
                    "target": "2",
                    "path": "b",
                    "patchSha256": "b" * 64,
                    "createSeconds": 2.75,
                    "patchBytes": 20,
                    "createMetrics": {"cpuSeconds": 1.5},
                    "applyMetrics": {
                        "medianWallSeconds": 0.7,
                        "medianCpuSeconds": 0.4,
                        "medianBaseReads": 6,
                        "medianBaseBytesRead": 200,
                        "samples": [{}, {}, {}, {}, {}],
                    },
                },
            ]
        }
        aggregate = MODULE.aggregate(result)
        self.assertEqual(4.0, aggregate["wallSeconds"])
        self.assertEqual(2.0, aggregate["cpuSeconds"])
        self.assertEqual(30, aggregate["patchBytes"])
        self.assertEqual(1.0, aggregate["applyWallSeconds"])
        self.assertAlmostEqual(0.6, aggregate["applyCpuSeconds"])
        self.assertEqual(10, aggregate["applyBaseReads"])
        self.assertEqual(300, aggregate["applyBaseBytesRead"])

    def test_aggregate_rejects_missing_five_repeat_apply_evidence(self):
        result = {
            "files": [
                {
                    "createSeconds": 1.0,
                    "patchBytes": 10,
                    "createMetrics": {"cpuSeconds": 0.5},
                    "applyMetrics": {
                        "medianWallSeconds": 0.1,
                        "medianCpuSeconds": 0.1,
                        "medianBaseReads": 1,
                        "medianBaseBytesRead": 1,
                        "samples": [{}],
                    },
                }
            ]
        }
        with self.assertRaises(ValueError):
            MODULE.aggregate(result)

    def test_h7_h4_byte_oracle_compares_per_file_sha(self):
        base = {
            "files": [
                {
                    "family": "f",
                    "base": "1",
                    "target": "2",
                    "path": "a",
                    "patchSha256": "a" * 64,
                }
            ]
        }
        same = {"files": [dict(base["files"][0])]}
        MODULE.require_same_patch_bytes("oracle", base, same)

        changed = {"files": [dict(base["files"][0], patchSha256="b" * 64)]}
        with self.assertRaises(ValueError):
            MODULE.require_same_patch_bytes("oracle", base, changed)


if __name__ == "__main__":
    unittest.main()
