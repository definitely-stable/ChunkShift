#!/usr/bin/env python3
"""Adversarial envelope/byte-accounting checks for the independent verifier."""
import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "tree_verifier", Path(__file__).with_name("verify_patch_tree_001_calibration.py"))
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class TreeCalibrationVerifierTests(unittest.TestCase):
    def fixture(self):
        identity = {"family": "dotnet-aspnetcore-win-x64", "base": "v1", "target": "v2"}
        original = {
            "schema": "chunkshift.patch-pairs.v1",
            "pairs": [{**identity,
                       "changed": [{"path": "changed", "targetSize": 5,
                                    "targetSha256": "a" * 64}],
                       "added": [{"path": "new", "size": 4,
                                  "sha256": "b" * 64}],
                       "removed": ["gone"], "identicalFiles": 1,
                       "identicalBytes": 2}]
        }
        def row(path, kind, target_bytes, t0, t0s, t1, digest, patch_digest):
            return {
                "family": identity["family"], "baseVersion": "v1", "targetVersion": "v2",
                "path": path, "kind": kind, "targetBytes": target_bytes,
                "t0Bytes": t0, "t0SBytes": t0s, "t1Bytes": t1,
                "t0PatchSha256": patch_digest, "t1PatchSha256": "c" * 64,
                "targetSha256": digest, "t0CreateSeconds": 0.1,
                "t1CreateSeconds": 0.2, "t0ApplySeconds": 0.3,
                "t1ApplySeconds": 0.4, "t1BaseReads": 1,
                "t1BaseBytesRead": 128, "t1BaseSeeks": 2
            }
        pair = {
            "family": identity["family"], "baseVersion": "v1", "targetVersion": "v2",
            "oldTreeManifestId": "d" * 64, "oldTreeCsmBytes": 50,
            "baseLayoutBytes": 40, "targetTreeManifestBytes": 20,
            "changedCount": 1, "addedCount": 1, "removedCount": 1,
            "unchangedCount": 1, "t0PayloadBytes": 104, "t1PayloadBytes": 115,
            "t0WarmPhysicalBytes": 124, "t0SPhysicalBytes": 160,
            "t1WarmPhysicalBytes": 135, "t1ColdPhysicalBytes": 225,
            "files": [row("changed", "changed", 5, 100, 100, 80, "a" * 64, "e" * 64),
                      row("new", "added", 4, 4, 40, 35, "b" * 64, None)]
        }
        result = {"schema": mod.SCHEMA, "status": "CALIBRATION_ONLY_NOT_DECISION",
                  "pairsSha256": mod.PAIRS_SHA256, "pairs": [pair]}
        return result, original

    def test_exact_structural_accounting(self):
        doc, pairs = self.fixture()
        mod.verify(doc, pairs)

    def test_reject_modified_warm_physical_sum(self):
        doc, pairs = self.fixture()
        doc["pairs"][0]["t1WarmPhysicalBytes"] += 1
        with self.assertRaisesRegex(ValueError, "accounting"):
            mod.verify(doc, pairs)

    def test_reject_missing_added_file(self):
        doc, pairs = self.fixture()
        doc["pairs"][0]["files"].pop()
        with self.assertRaisesRegex(ValueError, "missing or duplicate"):
            mod.verify(doc, pairs)

    def test_reject_calibration_role_substitution(self):
        doc, pairs = self.fixture()
        doc["pairs"][0]["family"] = "node-win-x64"
        with self.assertRaisesRegex(ValueError, "identity"):
            mod.verify(doc, pairs)

    def test_reject_generic_compression_as_reuse(self):
        doc, pairs = self.fixture()
        doc["pairs"][0]["files"][1]["t0Bytes"] = 40
        with self.assertRaisesRegex(ValueError, "added path raw"):
            mod.verify(doc, pairs)

    def test_reject_tampered_sha(self):
        doc, pairs = self.fixture()
        doc["pairs"][0]["files"][0]["targetSha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "target file identity"):
            mod.verify(doc, pairs)

    def test_reject_extra_pair(self):
        doc, pairs = self.fixture()
        doc["pairs"].append(copy.deepcopy(doc["pairs"][0]))
        with self.assertRaisesRegex(ValueError, "population"):
            mod.verify(doc, pairs)


if __name__ == "__main__":
    unittest.main()
