#!/usr/bin/env python3
"""Catch G4 verifier/workflow CLI drift before long frozen decision runs."""

import re
import shlex
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VERIFY = ROOT / "benchmarks/scripts/verify_patch_gap_g4.py"
WORKFLOWS = ROOT / ".github/workflows"


class G4WorkflowContractTests(unittest.TestCase):
    def test_frozen_workflows_pass_every_required_verifier_argument(self):
        required = set(re.findall(
            r'parser\.add_argument\("(--[a-z-]+)", required=True',
            VERIFY.read_text(encoding="utf-8"),
        ))
        self.assertEqual(
            {"--index", "--detail-dir", "--inventory",
             "--xz-provenance", "--dataset-role"}, required
        )

        for role in ("calibration", "evaluation"):
            with self.subTest(role=role):
                workflow = (WORKFLOWS / f"patch-gap-001-g4-{role}.yml").read_text(
                    encoding="utf-8"
                )
                calls = [
                    line.strip()
                    for line in workflow.splitlines()
                    if "python3 benchmarks/scripts/verify_patch_gap_g4.py" in line
                ]
                self.assertEqual(1, len(calls), "must invoke verifier once")
                command = calls[0].split("| tee", 1)[0].strip()
                words = shlex.split(command)
                self.assertEqual(
                    ["python3", "benchmarks/scripts/verify_patch_gap_g4.py"],
                    words[:2],
                )
                for option in required:
                    self.assertEqual(
                        1, words.count(option), f"{role} missing or duplicates {option}"
                    )
                    idx = words.index(option)
                    self.assertLess(idx + 1, len(words))
                    self.assertFalse(words[idx + 1].startswith("--"))
                self.assertEqual(
                    role, words[words.index("--dataset-role") + 1]
                )
                self.assertEqual(
                    "$OUT/xz-provenance.json",
                    words[words.index("--xz-provenance") + 1],
                )

    def test_workflows_dont_change_frozen_h0_or_corpus_identity(self):
        for role in ("calibration", "evaluation"):
            with self.subTest(role=role):
                content = (WORKFLOWS / f"patch-gap-001-g4-{role}.yml").read_text(
                    encoding="utf-8"
                )
                self.assertIn(
                    "PROTOCOL_COMMIT: 5372678ae8451a71cc95eb24f30855cbbd7e0633",
                    content,
                )
                self.assertIn(
                    "PAIRS_SHA256: 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd",
                    content,
                )


if __name__ == "__main__":
    unittest.main()
