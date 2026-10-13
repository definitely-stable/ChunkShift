#!/usr/bin/env python3
"""Negative controls for the J2G physical-patch evidence evaluator."""
import tempfile
import unittest
from pathlib import Path

import core011_csm_g1j2g_patch_cost as j


class J2GPatchCostTests(unittest.TestCase):
    def test_parse_kv_rejects_duplicates_and_broken_lines(self):
        self.assertEqual(j.parse_kv("valid=true\nchunks=81\n"), {
            "valid": "true", "chunks": "81"})
        for payload in ("valid=true\nvalid=false\n", "unframed\n", "=x\n", "valid=\n"):
            with self.subTest(payload=payload), self.assertRaises(ValueError):
                j.parse_kv(payload)

    def test_frozen_plan_gate_rejects_false_success(self):
        with self.assertRaisesRegex(ValueError, "reused-chunks"):
            j.require_metrics({"reused-chunks": "8"}, {"reused-chunks": 0})
        j.require_metrics({"reused-chunks": "0", "missing-bytes": "6697264"}, {
            "reused-chunks": 0, "missing-bytes": 6697264})

    def test_physical_patch_oracle(self):
        with tempfile.TemporaryDirectory() as folder:
            patch = Path(folder) / "test.csp"
            patch.write_bytes(b"x" * 120)
            source = ("target-manifest-id=T\nbase-manifest-id=B\n"
                      "patch-bytes=120\npayload-bytes=100\n"
                      "payload-entries=5\nfile-digest=opaque\n")
            valid = j.patch_metrics(source, patch, "T", "B")
            self.assertEqual(valid["physicalPatchBytes"], 120)
            self.assertEqual(valid["physicalMinusReportedPayloadBytes"], 20)
            self.assertEqual(valid["physicalPatchSha256"],
                             j.sha256(b"x" * 120))
            with self.assertRaises(ValueError):
                j.patch_metrics(source, patch, "wrong", "B")
            with self.assertRaises(ValueError):
                j.patch_metrics(source, patch, "T", None)
            with self.assertRaises(ValueError):
                j.patch_metrics(source.replace("120", "119"), patch, "T", "B")
            with self.assertRaises(ValueError):
                j.patch_metrics(source.replace("payload-bytes=100",
                                               "payload-bytes=121"), patch, "T", "B")
            with self.assertRaises(ValueError):
                j.patch_metrics(source.replace("payload-entries=5",
                                               "payload-entries=-1"), patch, "T", "B")
            with self.assertRaises(ValueError):
                j.patch_metrics(source.replace("payload-entries=5",
                                               "payload-entries=non-number"), patch, "T", "B")
            with self.assertRaises(ValueError):
                j.patch_metrics(source.replace("file-digest=opaque", ""),
                                patch, "T", "B")

    def test_standalone_is_not_assigned_a_base_identity(self):
        with tempfile.TemporaryDirectory() as folder:
            patch = Path(folder) / "test.csp"
            patch.write_bytes(b"0123456789")
            stdout = ("target-manifest-id=T\n"
                      "patch-bytes=10\npayload-bytes=3\n"
                      "payload-entries=1\nfile-digest=D\n")
            result = j.patch_metrics(stdout, patch, "T")
            self.assertIsNone(result["baseManifestId"])
            with self.assertRaises(ValueError):
                j.patch_metrics(stdout, patch, "T", "B")

    def test_stable_predeclared_controls(self):
        self.assertEqual(j.LANES, (
            "BASE_AWARE_STABLE64K", "SELF_CONTAINED_STABLE64K"))
        self.assertEqual(j.EXPECTED_BYTES, {
            "base": 6604072, "target": 6697264})
        self.assertEqual(j.boundary.CSM_SHA["base"],
                         "da64f7ac2bed330730db249bd9b40614474da9b3d9bad648f5d93b18f58fd009")


if __name__ == "__main__":
    unittest.main()
