"""Offline integrity/contract tests for PATCH-META-001/G1B1."""
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import run_patch_meta_001_g1b1 as g
import run_patch_meta_001_g1a as a


def fake_release():
    image = bytearray(65536)
    image[:2] = b"MZ"
    image[0x3c:0x40] = (128).to_bytes(4, "little")
    image[128:132] = b"PE\x00\x00"
    image[512:] = b"x" * (len(image) - 512)
    archive_bytes = io.BytesIO()
    with zipfile.ZipFile(archive_bytes, "w") as archive:
        archive.writestr(g.PACKAGE_ENTRY, bytes(image))
    return archive_bytes.getvalue(), bytes(image)


class MetaG1B1Tests(unittest.TestCase):
    def test_source_pins_are_literal_and_complete(self):
        self.assertEqual(g.VERSIONS, ("4.13.0", "4.14.0"))
        self.assertEqual(g.PAIRS, (g.VERSIONS, g.VERSIONS[::-1]))
        self.assertEqual(g.EXPECTED["4.13.0"]["dllBytes"], 6604072)
        self.assertEqual(g.EXPECTED["4.14.0"]["dllBytes"], 6697264)
        for version in g.VERSIONS:
            for name in ("dllSha256", "packageSha256"):
                self.assertEqual(len(bytes.fromhex(g.EXPECTED[version][name])), 32)
            self.assertEqual(g.package_url(version).split("/")[-1],
                             "microsoft.codeanalysis.csharp." + version + ".nupkg")
        with self.assertRaises(ValueError):
            g.package_url("latest")

    def test_package_pin_must_match_before_extraction(self):
        archive_bytes, image = fake_release()
        with self.assertRaisesRegex(ValueError, "pinned package"):
            g.read_exact_release("4.13.0", lambda _: archive_bytes)
        baseline = dict(g.EXPECTED["4.13.0"])
        with patch.dict(g.EXPECTED, {"4.13.0": {
                "packageSha256": a.sha(archive_bytes),
                "dllSha256": a.sha(image), "dllBytes": len(image)}}):
            loaded, details = g.read_exact_release("4.13.0", lambda _: archive_bytes)
            self.assertEqual(loaded, image)
            self.assertFalse(details["isSynthetic"])
            self.assertEqual(details["sourceSha256"], a.sha(image))
        self.assertEqual(g.EXPECTED["4.13.0"], baseline)

    def test_checksum_correct_archive_with_wrong_image_must_fail(self):
        archive_bytes, image = fake_release()
        with patch.dict(g.EXPECTED, {"4.13.0": {
                "packageSha256": a.sha(archive_bytes),
                "dllSha256": "a" * 64, "dllBytes": len(image)}}):
            with self.assertRaisesRegex(ValueError, "DLL pin"):
                g.read_exact_release("4.13.0", lambda _: archive_bytes)

    def test_stage_validator_fails_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            patch_file = Path(temp) / "patch.csp"
            patch_file.write_bytes(b"validated-independent-csp-file")
            pair = g.PAIRS[0]
            provenance = {"source": {
                version: {"sourceSha256": g.EXPECTED[version]["dllSha256"]}
                for version in g.VERSIONS}}
            baseline = {
                "schema": a.SCHEMA,
                "status": "STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT",
                "lane": "csp", "execution": "h2-w2",
                "family": g.FAMILY, "baseVersion": pair[0],
                "targetVersion": pair[1], "path": "artifact.dll",
                "baseSourceSha256": g.EXPECTED[pair[0]]["dllSha256"],
                "targetSourceSha256": g.EXPECTED[pair[1]]["dllSha256"],
                "baseSourceBytes": g.EXPECTED[pair[0]]["dllBytes"],
                "targetSourceBytes": g.EXPECTED[pair[1]]["dllBytes"],
                "patchSha256": a.file_sha(patch_file),
                "outputPatchBytes": patch_file.stat().st_size,
                "patchPhysicalBytes": patch_file.stat().st_size,
                "baseManifestId": "a" * 64, "targetManifestId": "b" * 64,
                "baseManifestCacheHit": True, "targetManifestCacheHit": True,
                "builderWallSeconds": 3.0, "builderCpuSeconds": 2.0,
                "totalWallSeconds": 4.0, "baseDigestWallSeconds": .1,
                "targetDigestWallSeconds": .1,
                "baseManifestBuildWallSeconds": 0,
                "targetManifestBuildWallSeconds": 0,
                "postBuilderPatchShaWallSeconds": .1,
                "baseContentReadBytes": 0,
                "targetContentReadBytes": g.EXPECTED[pair[1]]["dllBytes"],
                "baseManifestReadBytes": 1,
                "targetManifestReadBytes": 1,
                "builderAllocatedBytes": 10,
                "baseContentReadCalls": 0, "targetContentReadCalls": 1,
            }
            self.assertIs(g.verify_sample(baseline, pair, provenance, patch_file),
                          baseline)
            for field, bad in [
                ("status", "ADOPT"), ("execution", "h0"),
                ("baseSourceSha256", "0" * 64),
                ("patchSha256", "0" * 64), ("targetManifestId", ""),
                ("baseManifestCacheHit", "true"),
                ("builderWallSeconds", float("nan")),
                ("targetContentReadBytes", 0),
                ("builderCpuSeconds", -1),
                ("totalWallSeconds", .0001),
            ]:
                with self.subTest(field=field), self.assertRaises(ValueError):
                    g.verify_sample(baseline | {field: bad}, pair,
                                    provenance, patch_file)

    def test_invalid_reused_paths_or_rounds_never_publish_summary(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            dll = base / "existing.dll"
            dll.write_bytes(b"dummy")
            from argparse import Namespace
            args = Namespace(dll=dll, corpus=base / "corpus",
                             work=base / "work", output=base / "output",
                             rounds=4)
            with self.assertRaisesRegex(ValueError, "five"):
                g.run(args)
            self.assertFalse(args.output.exists())
            args.rounds = 5
            args.output.mkdir()
            with self.assertRaises(FileExistsError):
                g.run(args)
            self.assertFalse((args.output / "summary.json").exists())


if __name__ == "__main__":
    unittest.main()
