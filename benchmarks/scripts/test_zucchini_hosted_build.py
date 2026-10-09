#!/usr/bin/env python3
"""Pure hosted resource and binary provenance tests; no real Chromium build."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

SCRIPT = Path(__file__).with_name("zucchini_hosted_build.py")
spec = importlib.util.spec_from_file_location("zucchini_hosted_build", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
ROOT = Path(__file__).resolve().parents[2]
PIN = json.loads((ROOT / mod.PIN).read_text(encoding="utf-8"))
GiB = 1024 ** 3


class HostedBuildGates(unittest.TestCase):
    def test_capacity_positive(self):
        report = mod.resources(120 * GiB, 64 * GiB)
        self.assertTrue(report["canBuild"])
        self.assertTrue(report["diskPass"])
        self.assertTrue(report["ramPass"])
        self.assertEqual(report["scope"], "resource preflight only; NOT built/benchmarked")

    def test_insufficient_disk_fails(self):
        report = mod.resources(99 * GiB, 64 * GiB)
        self.assertFalse(report["canBuild"])
        self.assertFalse(report["diskPass"])

    def test_insufficient_memory_fails(self):
        report = mod.resources(200 * GiB, 7 * GiB)
        self.assertFalse(report["canBuild"])
        self.assertFalse(report["ramPass"])

    def test_minimums_not_weakened(self):
        with self.assertRaisesRegex(ValueError, "minima cannot be weakened"):
            mod.resources(200 * GiB, 64 * GiB, min_disk_gib=30)
        with self.assertRaisesRegex(ValueError, "minima cannot be weakened"):
            mod.resources(200 * GiB, 64 * GiB, min_memory_gib=4)

    def test_negative_capacity_rejected(self):
        with self.assertRaisesRegex(ValueError, "negative"):
            mod.resources(-1, 64 * GiB)

    def test_pinned_commit_tree_deps_depot_and_binary(self):
        def pinned_git(path, *args):
            reference = {
                ("rev-parse", "HEAD"): PIN["sourceCommit"],
                ("rev-parse", "HEAD^{tree}"): PIN["sourceTreeSha"],
                ("rev-parse", "HEAD:components"): PIN["componentsTreeSha"],
                ("rev-parse", "HEAD:components/zucchini"): PIN["componentTreeSha"],
                ("rev-parse", "HEAD:DEPS"): PIN["chromiumDepsBlobSha"],
            }
            return mod.TOOL_COMMIT if str(path).endswith("depot") else reference[args]
        with tempfile.TemporaryDirectory() as td:
            tool = Path(td) / "zucchini"
            tool.write_bytes(b"fake executable bytes: provenance fixture only")
            with patch.object(mod, "git", side_effect=pinned_git):
                result = mod.manifest(PIN, Path(td) / "source", Path(td) / "depot",
                                      tool, "gn-test", "ninja-test", "clang-test")
            self.assertEqual(result["chromiumSourceCommit"], PIN["sourceCommit"])
            self.assertEqual(result["binaryBytes"], tool.stat().st_size)
            self.assertEqual(result["gnArgs"], mod.GN_ARGS)
            self.assertIn(PIN["chromiumDepsBlobSha"], result["sourceCheckoutProvenance"])
            self.assertIn(mod.TOOL_COMMIT, result["sourceCheckoutProvenance"])

    def test_unpinned_dependencies_are_blocked(self):
        def altered_git(path, *args):
            return "a" * 40 if args == ("rev-parse", "HEAD:DEPS") else (
                mod.TOOL_COMMIT if str(path).endswith("depot")
                else {
                    ("rev-parse", "HEAD"): PIN["sourceCommit"],
                    ("rev-parse", "HEAD^{tree}"): PIN["sourceTreeSha"],
                    ("rev-parse", "HEAD:components"): PIN["componentsTreeSha"],
                    ("rev-parse", "HEAD:components/zucchini"): PIN["componentTreeSha"],
                }[args])
        with patch.object(mod, "git", side_effect=altered_git):
            with self.assertRaisesRegex(ValueError, "incorrect Chromium DEPS"):
                mod.verify_source(PIN, Path("source"), Path("depot"))


if __name__ == "__main__":
    unittest.main()
