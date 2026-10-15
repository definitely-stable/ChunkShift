"""Fail-closed and schedule contracts for G2-A real calibration evaluator."""
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import run_patch_exec_001_g2 as g2
from run_patch_exec_001_pilot import CASES, round_order


class PatchExecG2RunTests(unittest.TestCase):
    def args(self, root, modality="warm-csm"):
        return SimpleNamespace(
            modality=modality,
            subset=str(root / "subset"),
            output=str(root / "result"),
            project=str(root / "benchmark.csproj"),
        )

    def test_only_main_first_attempt_is_eligible(self):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            for env in (
                {"GITHUB_SHA": "x" * 40, "GITHUB_REF": "refs/heads/main",
                 "GITHUB_RUN_ATTEMPT": "1"},
                {"GITHUB_SHA": "f" * 40, "GITHUB_REF": "refs/heads/feature",
                 "GITHUB_RUN_ATTEMPT": "1"},
                {"GITHUB_SHA": "f" * 40, "GITHUB_REF": "refs/heads/main",
                 "GITHUB_RUN_ATTEMPT": "2"},
            ):
                with self.subTest(env=env), patch.dict(os.environ, env, clear=True):
                    with self.assertRaises(ValueError):
                        g2.execute(self.args(root))
                    self.assertFalse((root / "result").exists())

    def test_bad_modality_is_rejected_without_output(self):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            with self.assertRaises(ValueError):
                g2.execute(self.args(root, "mixed-unregistered"))
            self.assertFalse((root / "result").exists())

    def test_validation_cannot_be_mislabeled_as_training(self):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            subset = root / "subset"
            subset.mkdir()
            (subset / "selection.json").write_text(json.dumps({
                "schema": g2.POPULATION_SCHEMA,
                "role": "validation",
                "frozenOriginalPairsSha256": g2.FROZEN_PAIRS_SHA256,
                "fileCount": 16, "baseAndTargetFilesVerified": 32,
                "selectedPairsSha256": g2.EXPECTED_SELECTED_SHA256["validation"],
            }))
            (subset / "pairs.json").write_text("{}")
            env = {"GITHUB_SHA": "f" * 40, "GITHUB_REF": "refs/heads/main",
                   "GITHUB_RUN_ATTEMPT": "1"}
            with patch.dict(os.environ, env, clear=True):
                with self.assertRaisesRegex(ValueError, "not a frozen"):
                    g2.execute(self.args(root))
            self.assertFalse((root / "result").exists())

    def test_six_cases_and_five_permutations_are_exact(self):
        expected = {
            ("f1-h0", "h0", 1), ("f1-h2w2", "h2-w2", 1),
            ("f2-h0", "h0", 2), ("f2-h2w2", "h2-w2", 2),
            ("f4-h0", "h0", 4), ("f4-h2w2", "h2-w2", 4),
        }
        self.assertEqual(g2.ROUNDS, 5)
        self.assertEqual(set(CASES), expected)
        for i in range(g2.ROUNDS):
            self.assertEqual(set(round_order(i)), expected)
            self.assertEqual(len(round_order(i)), 6)


if __name__ == "__main__":
    unittest.main()
