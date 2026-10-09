#!/usr/bin/env python3
"""Synthetic adversarial chain-of-custody tests; NOT real reference evidence."""
import copy
import hashlib
import importlib.util
import json
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

SCRIPT = Path(__file__).with_name("verify_zucchini_calibration_evidence.py")
spec = importlib.util.spec_from_file_location("zucchini_calibration_seal", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


def sha(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


class SealedEvaluationOriginTests(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root = Path(t.name)
        (self.root / "origin").mkdir()
        (self.root / "source").mkdir()
        self.calibration_id = 43
        self.build_id = 29
        self.head = "c" * 40
        self.binary = b"synthetic executable bytes, not a real Chromium build"
        (self.root / "source/zucchini").write_bytes(self.binary)
        self.run = {
            "id": self.calibration_id,
            "name": mod.NAME,
            "event": "workflow_dispatch",
            "path": mod.WORKFLOW + "@refs/heads/main",
            "head_branch": "main",
            "head_sha": self.head,
            "status": "completed",
            "conclusion": "success",
            "run_attempt": 1,
            "repository": {"full_name": "definitely-stable/ChunkShift"},
        }
        self.jobs = {"jobs": [{"name": mod.CAL_JOB,
                               "status": "completed", "conclusion": "success"}]}
        self.artifacts = {"artifacts": [{
            "id": 47, "name": mod.ARTIFACT_PREFIX + str(self.build_id) + "-" + self.head,
            "expired": False, "digest": "sha256:" + "e" * 64,
            "workflow_run": {"id": self.calibration_id, "head_sha": self.head},
        }]}
        self.origin = {
            "status": "REAL_BUILT_BINARY_VERIFIED",
            "referenceStatus": "NOT_RUN",
            "buildRunId": self.build_id,
            "buildHeadSha": "a" * 40,
            "buildArtifactId": 71,
            "binarySha256": sha(self.binary),
        }
        self.write_origin()

    def write_origin(self):
        (self.root / "origin/origin-proof.json").write_text(
            json.dumps(self.origin), encoding="utf-8")

    def get_seal(self):
        return mod.verify_run(self.run, self.jobs, self.artifacts,
                              self.root, self.calibration_id, self.build_id)

    def test_accepts_successful_single_calibration_metadata(self):
        x = self.get_seal()
        self.assertEqual(x["evaluationStatus"], "NOT_RUN")
        self.assertEqual(x["calibrationRunId"], self.calibration_id)
        self.assertEqual(x["binarySha256"], sha(self.binary))

    def test_skipped_real_calibration_job_rejected(self):
        self.jobs["jobs"][0]["conclusion"] = "skipped"
        with self.assertRaisesRegex(ValueError, "actual calibration job"):
            self.get_seal()

    def test_normal_pr_contract_run_rejected(self):
        self.run["event"] = "pull_request"
        with self.assertRaisesRegex(ValueError, "explicit main calibration"):
            self.get_seal()

    def test_second_attempt_rejected(self):
        self.run["run_attempt"] = 2
        with self.assertRaisesRegex(ValueError, "explicit main calibration"):
            self.get_seal()

    def test_wrong_calibration_workflow_rejected(self):
        self.run["path"] = ".github/workflows/another.yml@refs/heads/main"
        with self.assertRaisesRegex(ValueError, "wrong calibration workflow"):
            self.get_seal()

    def test_expired_calibration_artifact_rejected(self):
        self.artifacts["artifacts"][0]["expired"] = True
        with self.assertRaisesRegex(ValueError, "artifact expired"):
            self.get_seal()

    def test_artifact_from_other_commit_rejected(self):
        self.artifacts["artifacts"][0]["workflow_run"]["head_sha"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "wrong run"):
            self.get_seal()

    def test_wrong_build_id_rejected(self):
        self.origin["buildRunId"] = 99
        self.write_origin()
        with self.assertRaisesRegex(ValueError, "real binary build"):
            self.get_seal()

    def test_tampered_calibration_binary_rejected(self):
        (self.root / "source/zucchini").write_bytes(b"not the same")
        with self.assertRaisesRegex(ValueError, "calibration binary bytes"):
            self.get_seal()

    def test_multiple_calibration_artifacts_rejected(self):
        self.artifacts["artifacts"].append(copy.deepcopy(self.artifacts["artifacts"][0]))
        with self.assertRaisesRegex(ValueError, "missing or duplicate"):
            self.get_seal()

    def test_missing_signed_digest_rejected(self):
        self.artifacts["artifacts"][0]["digest"] = "sha1:" + "e" * 40
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            self.get_seal()

    def test_independent_evidence_recomputation_is_mandatory(self):
        # Synthetic stubs test the seal plumbing, NOT the frozen oracle.
        seal = self.get_seal()
        reference = {
            "datasetRole": "calibration", "mode": "measure",
            "tool": {"binarySha256": seal["binarySha256"]},
        }
        (self.root / "calibration-reference.json").write_text(json.dumps(reference))
        actual_audit = {"referenceOnly": True, "rfcSizeGate": "NOT_APPLICABLE",
                        "matchedVerifiedFiles": 1}
        (self.root / "calibration-audit.json").write_text(json.dumps(actual_audit))
        (self.root / "origin/run.json").write_text("{}")
        (self.root / "origin/jobs.json").write_text("{}")
        (self.root / "origin/artifacts.json").write_text("{}")
        inventory = self.root / "g4.json"
        inventory.write_text('{"frozen":true}')
        build = types.SimpleNamespace(verify=lambda *_args: {
            "binarySha256": seal["binarySha256"],
            "buildHeadSha": seal["buildHeadSha"],
            "buildArtifactId": seal["buildArtifactId"],
        })
        auditor = types.SimpleNamespace(
            verify_frozen_population=lambda *_args: None,
            frozen_h0=lambda *_args: {},
            verify=lambda *_args: actual_audit,
        )
        with patch.object(mod, "import_script",
                          side_effect=lambda file, _alias:
                              build if file == "verify_zucchini_build_artifact.py" else auditor):
            proof = mod.verify_evidence(seal, self.root, {}, inventory, self.root)
            self.assertEqual(proof["status"], "SEALED_CALIBRATION_VERIFIED")
            self.assertEqual(proof["evaluationStatus"], "NOT_RUN")
            (self.root / "calibration-audit.json").write_text('{"referenceOnly":false}')
            with self.assertRaisesRegex(ValueError, "independent recomputation"):
                mod.verify_evidence(seal, self.root, {}, inventory, self.root)

    def test_calibration_and_reference_binary_must_match(self):
        seal = self.get_seal()
        (self.root / "calibration-reference.json").write_text(json.dumps({
            "datasetRole": "calibration", "mode": "measure",
            "tool": {"binarySha256": "f" * 64},
        }))
        (self.root / "origin/run.json").write_text("{}")
        (self.root / "origin/jobs.json").write_text("{}")
        (self.root / "origin/artifacts.json").write_text("{}")
        mocked = types.SimpleNamespace(verify=lambda *_args: {
            "binarySha256": seal["binarySha256"],
            "buildHeadSha": seal["buildHeadSha"],
            "buildArtifactId": seal["buildArtifactId"],
        })
        with patch.object(mod, "import_script", return_value=mocked):
            with self.assertRaisesRegex(ValueError, "calibration did not use"):
                mod.verify_evidence(seal, self.root, {}, self.root / "fake.json", self.root)


if __name__ == "__main__":
    unittest.main()
