#!/usr/bin/env python3
"""Synthetic, untrusted-input tests; never substitute for a real Chromium run."""
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("patch_gap_g4_zucchini_reference.py")
spec = importlib.util.spec_from_file_location("zucchini_ref", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

FAKE = """#!/usr/bin/env python3
import pathlib, sys
args = sys.argv[1:]
mode = args[0]
if mode == '-read':
    data = pathlib.Path(args[1]).read_bytes()
    sys.exit(6 if data.startswith(b'BAD') else 2 if data.startswith(b'IOFAIL') else 0)
if mode == '-gen':
    pathlib.Path(args[3]).write_bytes(pathlib.Path(args[2]).read_bytes())
    sys.exit(0)
if mode == '-apply':
    pathlib.Path(args[3]).write_bytes(pathlib.Path(args[2]).read_bytes())
    sys.exit(0)
sys.exit(3)
"""
CORRUPT = """#!/usr/bin/env python3
import pathlib, sys
a = sys.argv[1:]
if a[0] == '-read':
    sys.exit(0)
if a[0] == '-gen':
    pathlib.Path(a[3]).write_bytes(b'diff')
    sys.exit(0)
if a[0] == '-apply':
    pathlib.Path(a[3]).write_bytes(b'WRONG')
    sys.exit(0)
sys.exit(3)
"""


def digest(b):
    return hashlib.sha256(b).hexdigest()


class ZucchiniReferenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.base = self.root / "tree" / "node-win-x64" / "1.0" / "bin.exe"
        self.target = self.root / "tree" / "node-win-x64" / "1.1" / "bin.exe"
        self.base.parent.mkdir(parents=True)
        self.target.parent.mkdir(parents=True)
        self.base.write_bytes(b"MZ" + bytes(range(16)))
        self.target.write_bytes(b"MZ" + bytes(range(17)))
        self.row = {"family": "node-win-x64", "baseVersion": "1.0",
                    "targetVersion": "1.1", "path": "bin.exe",
                    "structuralCandidate": True,
                    "baseBytes": self.base.stat().st_size,
                    "targetBytes": self.target.stat().st_size,
                    "baseSha256": mod.sha(self.base),
                    "targetSha256": mod.sha(self.target),
                    "datasetRole": "evaluation"}
        self.tool = self.root / "zucchini"
        self.tool.write_text(FAKE, encoding="utf-8")
        self.tool.chmod(0o755)

    def test_exact_round_trip_and_physical_bytes(self):
        results = mod.measure([self.row], self.root, self.tool, 10)
        self.assertEqual(results[0]["status"], "VERIFIED")
        self.assertEqual(results[0]["patchBytes"], self.target.stat().st_size)
        self.assertEqual(results[0]["reconstructionSha256"], mod.sha(self.target))

    def test_parser_support_is_probed_before_encode(self):
        self.base.write_bytes(b"BAD" + bytes(15))
        self.row["baseSha256"] = mod.sha(self.base)
        result = mod.measure([self.row], self.root, self.tool, 10)[0]
        self.assertEqual(result["status"], "UNSUPPORTED_BASE")
        self.assertIsNone(result["patchBytes"])

    def test_probe_io_failure_is_not_parser_unsupported(self):
        self.base.write_bytes(b"IOFAIL" + bytes(12))
        self.row["baseSha256"] = mod.sha(self.base)
        with self.assertRaisesRegex(ValueError, "reason other than unsupported image"):
            mod.measure([self.row], self.root, self.tool, 10)

    def test_reconstruction_mismatch_fails_closed(self):
        self.tool.write_text(CORRUPT, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
            mod.measure([self.row], self.root, self.tool, 10)

    def test_corrupted_source_rejected(self):
        self.base.write_bytes(b"CORRUPTED")
        with self.assertRaisesRegex(ValueError, "base corpus identity"):
            mod.measure([self.row], self.root, self.tool, 10)

    def test_out_of_subset_no_reference_bytes(self):
        row = dict(self.row, structuralCandidate=False)
        result = mod.measure([row], self.root, self.tool, 10)[0]
        self.assertEqual(result["status"], "OUTSIDE_STRUCTURAL_SUBSET")
        self.assertIsNone(result["patchBytes"])

    def test_parent_traversal_rejected(self):
        with self.assertRaisesRegex(ValueError, "unsafe frozen relative path"):
            mod.safe_file(self.root, dict(self.row, path="../bin.exe"), "baseVersion")

    def test_pinned_binary_hash_required(self):
        manifest = {
            "schema": "chunkshift.patch-gap-g4-zucchini-build.v1",
            "chromiumComponentCommit": mod.CHROMIUM_COMPONENT,
            "chromiumSourceCommit": "a" * 40,
            "binarySha256": digest(b"wrong"),
            "compilerIdentity": "test", "buildCommand": "test",
            "gnArgs": "test", "buildSystemIdentity": "test",
            "sourceCheckoutProvenance": "test",
        }
        path = self.root / "manifest.json"
        path.write_text(json.dumps(manifest), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "binary mismatch"):
            mod.validate_tool(self.tool, path)
        manifest["binarySha256"] = mod.sha(self.tool)
        path.write_text(json.dumps(manifest), encoding="utf-8")
        self.assertEqual(mod.validate_tool(self.tool, path), manifest)
        manifest["chromiumComponentCommit"] = "0" * 40
        path.write_text(json.dumps(manifest), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source pin"):
            mod.validate_tool(self.tool, path)


if __name__ == "__main__":
    unittest.main()
