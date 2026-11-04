"""Fail-closed hosted capacity probe contract tests, no Chromium download."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("patch_gap_zucchini_hosted_capacity.py")
SPEC = importlib.util.spec_from_file_location("zucchini_hosted_capacity", SCRIPT)
mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mod)
GiB = 1024**3


def sample(*, free=100 * GiB, memory=8 * GiB, **kw):
    data = dict(free_disk=free, physical_ram=memory, disk_total=150*GiB,
                cpu_count=4, runner_environment="github-hosted",
                runner_label="ubuntu-24.04", repository="definitely-stable/ChunkShift",
                source_sha="a"*40, event="issue_comment", ref="refs/heads/main",
                attempt=1, run_id=41812345678, observation_date="20261104",
                source_pin_sha256="b"*64)
    data.update(kw)
    return mod.measure(**data)


class HostedCapacityTests(unittest.TestCase):
    def test_exact_minima_and_result_pass(self):
        a = sample()
        self.assertEqual(a["schema"], mod.SCHEMA)
        self.assertTrue(a["hardware"]["canBuild"])
        self.assertEqual(a["hardware"]["requiredDiskGiB"], 100)
        self.assertEqual(a["hardware"]["requiredMemoryGiB"], 8)
        self.assertFalse(a["referenceCalibrationExecuted"])
        self.assertFalse(a["organizationLargerRunnerInventoryVerified"])

    def test_disk_shortage_is_recorded_not_inferred_as_codec_failure(self):
        a = sample(free=99*GiB)
        self.assertFalse(a["hardware"]["canBuild"])
        self.assertEqual(a["status"], "STANDARD_HOST_CAPACITY_FAIL__BUILD_NOT_ATTEMPTED")
        self.assertFalse(a["referenceEvaluationExecuted"])
        self.assertFalse(a["releaseGateEligible"])

    def test_memory_shortage_is_recorded(self):
        a=sample(memory=7*GiB)
        self.assertFalse(a["hardware"]["ramPass"])
        self.assertFalse(a["hardware"]["canBuild"])

    def test_unknown_or_self_hosted_rejected(self):
        for name in ("self-hosted", "", "unknown"):
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "GitHub-hosted"):
                sample(runner_environment=name)

    def test_paid_runner_is_not_silently_probed(self):
        with self.assertRaisesRegex(ValueError, "standard Ubuntu"):
            sample(runner_label="ubuntu-24.04-8core")

    def test_unbound_source_repo_event_and_sha_rejected(self):
        cases=[("source_sha", "a"*39), ("repository", "someone/else"),
               ("event", "pull_request"), ("ref", "refs/heads/feature"),
               ("attempt", 2), ("observation_date", "20261301"),
               ("source_pin_sha256", "wrong")]
        for key,value in cases:
            with self.subTest(field=key), self.assertRaises(ValueError):
                sample(**{key:value})

    def test_invalid_capacity_and_run_identity_rejected(self):
        for key,value in (("free_disk", -1), ("free_disk", 151*GiB),
                          ("disk_total", 0), ("cpu_count", 0),
                          ("run_id", 0)):
            with self.subTest(field=key), self.assertRaises(ValueError):
                sample(**{key:value})


if __name__ == "__main__":
    unittest.main()
