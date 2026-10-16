"""Offline verification of G1A orchestration and fail-closed stage observations."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import run_patch_meta_001_g1a as g1


class MetaG1ATests(unittest.TestCase):
    def test_corpus_is_source_locked_and_exclusive(self):
        with tempfile.TemporaryDirectory() as temp:
            a, b = Path(temp) / "a", Path(temp) / "b"
            first, parsed = g1.make_corpus(a)
            second, other = g1.make_corpus(b)
            self.assertEqual(first, second)
            self.assertEqual(parsed, other)
            self.assertEqual(len(parsed["pairs"]), 2)
            for pair in parsed["pairs"]:
                self.assertEqual(pair["changed"][0]["baseSize"], g1.SIZE)
                self.assertEqual(pair["changed"][0]["targetSize"], g1.SIZE)
                self.assertNotEqual(pair["changed"][0]["baseSha256"],
                                    pair["changed"][0]["targetSha256"])
            with self.assertRaises(FileExistsError):
                g1.make_corpus(a)

    def test_command_is_exact_and_no_patching_heuristics(self):
        cmd = g1.command("lab.dll", "create", Path("c"), Path("w"),
                         g1.PAIRS[0], Path("p.csp"),
                         ["--lane", "csp", "--execution", "h2-w2"])
        self.assertEqual(cmd[:5], ["dotnet", "lab.dll", "patch-lab", "one", "create"])
        self.assertIn("--work", cmd)
        self.assertIn("--execution", cmd)
        self.assertNotIn("--candidate-selection", cmd)

    def valid_observation(self, patch):
        digest = g1.file_sha(patch)
        return {
            "schema": g1.SCHEMA, "status": "STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT",
            "lane": "csp", "execution": "h2-w2", "family": "meta-g1a",
            "baseVersion": "v1", "targetVersion": "v2", "path": "artifact.bin",
            "baseSourceSha256": "a" * 64, "targetSourceSha256": "b" * 64,
            "patchSha256": digest, "outputPatchBytes": patch.stat().st_size,
            "patchPhysicalBytes": patch.stat().st_size,
            "totalWallSeconds": 1.0, "builderWallSeconds": 0.4,
            "baseDigestWallSeconds": .1, "targetDigestWallSeconds": .1,
            "baseManifestBuildWallSeconds": .0, "targetManifestBuildWallSeconds": .0,
            "postBuilderPatchShaWallSeconds": .1, "builderCpuSeconds": .2,
            "baseContentReadBytes": 10, "targetContentReadBytes": 20,
            "baseManifestReadBytes": 5, "targetManifestReadBytes": 5,
            "builderAllocatedBytes": 300, "baseManifestId": "base",
            "targetManifestId": "target",
            "baseManifestCacheHit": True, "targetManifestCacheHit": True
        }

    def test_fail_closed_validation(self):
        with tempfile.TemporaryDirectory() as temp:
            patch = Path(temp) / "patch.csp"
            patch.write_bytes(b"real-patch-bytes")
            sources = {"v1": "a" * 64, "v2": "b" * 64}
            baseline = self.valid_observation(patch)
            self.assertEqual(g1.validate_observation(baseline, g1.PAIRS[0], sources, patch),
                             baseline)
            variants = [
                ("schema", "unknown"),
                ("status", "ADOPT"),
                ("execution", "h0"),
                ("baseSourceSha256", "c" * 64),
                ("patchSha256", "d" * 64),
                ("outputPatchBytes", 0),
                ("builderWallSeconds", float("nan")),
                ("totalWallSeconds", -1),
                ("baseManifestReadBytes", "0"),
                ("baseManifestCacheHit", "true"),
                ("baseManifestId", ""),
            ]
            for key, invalid in variants:
                with self.subTest(key=key):
                    altered = baseline | {key: invalid}
                    with self.assertRaises(ValueError):
                        g1.validate_observation(altered, g1.PAIRS[0], sources, patch)
            with self.assertRaises(ValueError):
                g1.validate_observation(
                    baseline | {"baseDigestWallSeconds": .9},
                    g1.PAIRS[0], sources, patch)

    def test_no_summary_on_missing_dotnet(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            # 'execute' must fail before claiming success if the DLL is missing.
            from argparse import Namespace
            args = Namespace(dll=root / "missing.dll", corpus=root / "corpus",
                             work=root / "work", output=root / "out", rounds=2)
            with self.assertRaises(FileNotFoundError):
                g1.execute(args)
            self.assertFalse((root / "out" / "summary.json").exists())


if __name__ == "__main__":
    unittest.main()
