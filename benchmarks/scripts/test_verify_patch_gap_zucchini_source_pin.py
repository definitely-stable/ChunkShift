#!/usr/bin/env python3
"""Synthetic GitHub commit/tree adversarial tests for frozen Zucchini source."""
import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("verify_patch_gap_zucchini_source_pin.py")
spec = importlib.util.spec_from_file_location("source_lock", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class UpstreamSourceLockTests(unittest.TestCase):
    def setUp(self):
        self.pin = mod.load(Path(__file__).resolve().parents[2] / mod.PIN_PATH)
        self.root_sha = "a" * 40
        self.parent_sha = "b" * 40
        self.commit = {"sha": self.pin["sourceCommit"], "commit": {"tree": {"sha": self.root_sha}}}
        self.root = {"sha": self.root_sha, "truncated": False, "tree": [
            {"path": "components", "type": "tree", "mode": "040000", "sha": self.parent_sha},
        ]}
        self.components = {"sha": self.parent_sha, "truncated": False, "tree": [
            {"path": "zucchini", "type": "tree", "mode": "040000",
             "sha": self.pin["componentTreeSha"]},
        ]}

    def test_valid_exact_mirror_source_tree(self):
        proof = mod.verify_remote(self.pin, self.commit, self.root, self.components)
        self.assertEqual(proof["result"], "SOURCE_PIN_PASS")
        self.assertEqual(proof["componentTreeSha"], self.pin["componentTreeSha"])

    def test_changed_source_commit_rejected(self):
        self.commit["sha"] = "c" * 40
        with self.assertRaisesRegex(ValueError, "commit identity"):
            mod.verify_remote(self.pin, self.commit, self.root, self.components)

    def test_changed_subtree_rejected(self):
        self.components["tree"][0]["sha"] = "c" * 40
        with self.assertRaisesRegex(ValueError, "tree SHA mismatch"):
            mod.verify_remote(self.pin, self.commit, self.root, self.components)

    def test_missing_component_rejected(self):
        self.components["tree"].clear()
        with self.assertRaisesRegex(ValueError, "missing or duplicate"):
            mod.verify_remote(self.pin, self.commit, self.root, self.components)

    def test_wrong_root_rejected(self):
        self.root["sha"] = "d" * 40
        with self.assertRaisesRegex(ValueError, "root tree identity"):
            mod.verify_remote(self.pin, self.commit, self.root, self.components)

    def test_components_not_bound_to_root_rejected(self):
        self.components["sha"] = "e" * 40
        with self.assertRaisesRegex(ValueError, "components tree identity"):
            mod.verify_remote(self.pin, self.commit, self.root, self.components)

    def test_truncated_tree_rejected(self):
        self.components["truncated"] = True
        with self.assertRaisesRegex(ValueError, "truncated"):
            mod.verify_remote(self.pin, self.commit, self.root, self.components)

    def test_fake_component_pin_rejected(self):
        self.pin["componentMirrorCommit"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "frozen component mirror"):
            mod.verify_remote(self.pin, self.commit, self.root, self.components)


if __name__ == "__main__":
    unittest.main()
