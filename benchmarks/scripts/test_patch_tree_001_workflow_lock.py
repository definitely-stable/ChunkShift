#!/usr/bin/env python3
"""Frozen PATCH-TREE calibration workflow lock contract (offline/stdlib-only).

Prevents a malformed or stale inline workflow PAIRS_SHA256 from reaching the
one-shot GitHub-hosted calibration gate. Does not download or evaluate corpus.
"""
import json
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
WORKFLOW = REPO / ".github/workflows/patch-tree-001-t0-t1-calibration.yml"
LOCK = REPO / "docs/benchmarks/patch-corpus/corpus-lock.json"


def verify_workflow_digest(document: str, frozen_digest: str) -> None:
    if re.fullmatch(r"[0-9a-f]{64}", frozen_digest) is None:
        raise ValueError("frozen corpus lock must be 64 lowercase hex digits")

    matches = re.findall(r"^  PAIRS_SHA256:\s*([0-9a-z]+)\s*$", document, re.MULTILINE)
    if len(matches) != 1:
        raise ValueError("expected one workflow-level PAIRS_SHA256")
    if re.fullmatch(r"[0-9a-f]{64}", matches[0]) is None:
        raise ValueError("workflow PAIRS_SHA256 must be 64 lowercase hex digits")
    if matches[0] != frozen_digest:
        raise ValueError("workflow PAIRS_SHA256 differs from frozen corpus-lock.json")
    # A second truncated digest in the post-run JSON assertion previously
    # caused a real nine-minute calibration to fail after SDK work completed.
    candidates = re.findall(r"(?<![0-9a-f])[0-9a-f]{60,70}(?![0-9a-f])", document)
    if candidates != [frozen_digest]:
        raise ValueError("duplicated or malformed SHA-256 literal in calibration workflow")
    if 'doc["pairsSha256"] != os.environ["PAIRS_SHA256"]' not in document:
        raise ValueError("output validation must reuse the authoritative SHA-256")
    if 'echo "$PAIRS_SHA256  pairs.json" | sha256sum --check' not in document:
        raise ValueError("workflow must verify downloaded pairs.json against original lock")
    if 'if not re.fullmatch(r"[0-9a-f]{64}", os.environ["PAIRS_SHA256"]):' not in document:
        raise ValueError("workflow must fail closed before using malformed digest")


class WorkflowLockContractTests(unittest.TestCase):
    def test_no_second_literal_is_allowed_in_result_validation(self):
        locked = json.loads(LOCK.read_text(encoding="utf-8"))["pairsSha256"]
        original = WORKFLOW.read_text(encoding="utf-8")
        duplicate = original + "\n# legacy result SHA " + locked[:-1] + "\n"
        with self.assertRaisesRegex(ValueError, "duplicated or malformed"):
            verify_workflow_digest(duplicate, locked)

    def test_unverified_diagnostics_are_separate_from_accepted_artifacts(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("name: patch-tree-001-calibration-UNVERIFIED-diagnostics", workflow)
        self.assertIn("name: patch-tree-001-t0-t1-calibration", workflow)
        self.assertIn("UNVERIFIED-NOT-DECISION.txt", workflow)
        self.assertIn("if: failure()", workflow)
        self.assertIn("if: success()", workflow)

    def test_real_workflow_matches_frozen_corpus_lock(self):
        locked = json.loads(LOCK.read_text(encoding="utf-8"))["pairsSha256"]
        verify_workflow_digest(WORKFLOW.read_text(encoding="utf-8"), locked)

    def test_missing_hex_digit_fails_before_dispatch(self):
        locked = json.loads(LOCK.read_text(encoding="utf-8"))["pairsSha256"]
        original = WORKFLOW.read_text(encoding="utf-8")
        corrupt = original.replace("  PAIRS_SHA256: " + locked,
                                   "  PAIRS_SHA256: " + locked[:-1])
        with self.assertRaisesRegex(ValueError, "64 lowercase"):
            verify_workflow_digest(corrupt, locked)

    def test_other_valid_length_digest_is_not_accepted(self):
        locked = json.loads(LOCK.read_text(encoding="utf-8"))["pairsSha256"]
        original = WORKFLOW.read_text(encoding="utf-8")
        changed = ("0" if locked[0] != "0" else "1") + locked[1:]
        corrupt = original.replace("  PAIRS_SHA256: " + locked,
                                   "  PAIRS_SHA256: " + changed)
        with self.assertRaisesRegex(ValueError, "differs from frozen"):
            verify_workflow_digest(corrupt, locked)


if __name__ == "__main__":
    unittest.main()
