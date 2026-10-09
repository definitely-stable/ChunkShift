#!/usr/bin/env python3
"""Adversarial artifact-origin tests. All executable bytes are synthetic."""
import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("verify_zucchini_build_artifact.py")
spec = importlib.util.spec_from_file_location("zucchini_artifact", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
REPO = Path(__file__).resolve().parents[2]
PIN = json.loads((REPO / mod.PIN).read_text(encoding="utf-8"))


def digest(blob):
    return hashlib.sha256(blob).hexdigest()


class ArtifactOriginTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.run_id = 12345
        self.checkout = "d" * 40
        self.run = {
            "id": self.run_id, "name": mod.NAME, "event": "workflow_dispatch",
            "head_branch": "main", "head_sha": self.checkout,
            "status": "completed", "conclusion": "success", "run_attempt": 1,
            "path": mod.WORKFLOW + "@refs/heads/main",
            "repository": {"full_name": "definitely-stable/ChunkShift"},
        }
        self.jobs = {"jobs": [{
            "name": mod.BUILD_JOB, "status": "completed",
            "conclusion": "success", "runner_name": "GitHub Actions 2026 x64",
        }]}
        self.artifacts = {"artifacts": [{
            "id": 9001, "name": "patch-gap-001-g4-zucchini-pinned-binary-" + self.checkout,
            "digest": "sha256:" + "e" * 64, "expired": False,
            "workflow_run": {"id": self.run_id, "head_sha": self.checkout},
        }]}
        original = {"sourceCommit": PIN["sourceCommit"],
                    "sourceTreeSha": PIN["sourceTreeSha"],
                    "componentsTreeSha": PIN["componentsTreeSha"],
                    "componentTreeSha": PIN["componentTreeSha"],
                    "chromiumDepsBlobSha": PIN["chromiumDepsBlobSha"],
                    "depotToolsCommit": mod.DEPOT_SHA}
        binary = b"synthetic tool fixture is NOT a real Zucchini build"
        gn = b"is_debug = false\nis_component_build = false\nsymbol_level = 0\n"
        deps = b"synthetic pinned dependency-list fixture"
        (self.root / "zucchini").write_bytes(binary)
        (self.root / "args.gn").write_bytes(gn)
        (self.root / "deps-revisions.txt").write_bytes(deps)
        self.manifest = {
            "schema": mod.MANIFEST_SCHEMA,
            "chromiumComponentCommit": PIN["componentMirrorCommit"],
            "chromiumSourceCommit": PIN["sourceCommit"],
            "sourceObjects": original,
            "gnArgs": "is_debug=false is_component_build=false symbol_level=0",
            "buildCommand": "autoninja -C out/Zucchini components/zucchini:zucchini",
            "gnArgsFileSha256": digest(gn),
            "compilerBinarySha256": "a" * 64,
            "compilerIdentity": "clang fixture",
            "buildSystemIdentity": "gn/ninja fixture",
            "dependencySnapshotSha256": digest(deps),
            "binarySha256": digest(binary),
            "binaryBytes": len(binary),
        }
        (self.root / "resource-preflight.json").write_text(json.dumps({
            "schema": "chunkshift.patch-gap-g4-zucchini-hosted-preflight.v1",
            "canBuild": True, "diskPass": True, "ramPass": True,
            "requiredDiskGiB": 100, "requiredMemoryGiB": 8,
        }), encoding="utf-8")
        self.refresh()

    def refresh(self):
        (self.root / "tool-manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")

    def check(self):
        return mod.verify(self.run, self.jobs, self.artifacts,
                          self.root, PIN, self.run_id)

    def test_exact_origin_metadata_accepted(self):
        result = self.check()
        self.assertEqual(result["status"], "REAL_BUILT_BINARY_VERIFIED")
        self.assertEqual(result["referenceStatus"], "NOT_RUN")

    def test_skipped_actual_build_cannot_be_green(self):
        self.jobs["jobs"][0]["conclusion"] = "skipped"
        with self.assertRaisesRegex(ValueError, "real binary build job"):
            self.check()

    def test_normal_pr_contract_run_rejected(self):
        self.run["event"] = "pull_request"
        with self.assertRaisesRegex(ValueError, "explicit build"):
            self.check()

    def test_non_main_run_rejected(self):
        self.run["head_branch"] = "research/other"
        with self.assertRaisesRegex(ValueError, "main-branch"):
            self.check()

    def test_rerun_rejected(self):
        self.run["run_attempt"] = 2
        with self.assertRaisesRegex(ValueError, "rerun"):
            self.check()

    def test_expired_artifact_rejected(self):
        self.artifacts["artifacts"][0]["expired"] = True
        with self.assertRaisesRegex(ValueError, "expired"):
            self.check()

    def test_different_head_or_artifact_rejected(self):
        self.artifacts["artifacts"][0]["workflow_run"]["head_sha"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "source identity"):
            self.check()

    def test_non_sha256_artifact_claim_rejected(self):
        self.artifacts["artifacts"][0]["digest"] = "sha1:" + "e" * 40
        with self.assertRaisesRegex(ValueError, "SHA-256 digest"):
            self.check()

    def test_wrong_binary_bytes_rejected(self):
        (self.root / "zucchini").write_bytes(b"not the same byte string")
        with self.assertRaisesRegex(ValueError, "executable hash"):
            self.check()

    def test_changed_gn_args_rejected(self):
        (self.root / "args.gn").write_text("is_debug = true\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "wrong original GN"):
            self.check()

    def test_missing_deps_or_wrong_deps_rejected(self):
        (self.root / "deps-revisions.txt").write_bytes(b"other deps")
        with self.assertRaisesRegex(ValueError, "DEPS snapshot SHA"):
            self.check()

    def test_wrong_component_sha_rejected(self):
        self.manifest["sourceObjects"]["componentTreeSha"] = "b" * 40
        self.refresh()
        with self.assertRaisesRegex(ValueError, "source object mismatch"):
            self.check()

    def test_false_resource_provenance_rejected(self):
        (self.root / "resource-preflight.json").write_text(json.dumps({
            "schema": "chunkshift.patch-gap-g4-zucchini-hosted-preflight.v1",
            "canBuild": False,
        }), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "resources"):
            self.check()


if __name__ == "__main__":
    unittest.main()
