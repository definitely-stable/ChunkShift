import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("check_patch_enc_005_cross_platform.py")
SPEC = importlib.util.spec_from_file_location("patch_enc_005_cross", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def document(platform: str, patch: str = "a" * 64) -> dict:
    commit = "b" * 40
    return {
        "schema": "chunkshift.patch-enc-005-paired.v2",
        "status": "VALID",
        "platform": platform,
        "runId": f"PATCH-ENC-005/RUN-20261001-001-{commit}-{platform}",
        "sourceCommit": commit,
        "protocolCommit": "c" * 40,
        "datasetRole": "calibration",
        "datasetSha256": "d" * 64,
        "githubRunId": "123",
        "githubRunNumber": "1",
        "githubRunAttempt": "1",
        "acceptedPatchShas": {
            "csp": [
                {
                    "family": "f",
                    "base": "1",
                    "target": "2",
                    "path": "a",
                    "patchSha256": patch,
                }
            ]
        },
    }


class CrossPlatformTests(unittest.TestCase):
    def test_accepts_exact_three_platform_sha_equality(self):
        docs = [
            (Path("x64"), document("linux-x64")),
            (Path("arm64"), document("linux-arm64")),
            (Path("win"), document("win-x64")),
        ]
        result = MODULE.validate(docs)
        self.assertTrue(result["patchBytesEqual"])
        self.assertEqual(sorted(MODULE.PLATFORMS), result["platforms"])

    def test_rejects_patch_byte_mismatch(self):
        docs = [
            (Path("x64"), document("linux-x64")),
            (Path("arm64"), document("linux-arm64", "e" * 64)),
            (Path("win"), document("win-x64")),
        ]
        with self.assertRaises(ValueError):
            MODULE.validate(docs)

    def test_rejects_incomplete_platform(self):
        bad = document("win-x64")
        bad["status"] = "INCOMPLETE"
        docs = [
            (Path("x64"), document("linux-x64")),
            (Path("arm64"), document("linux-arm64")),
            (Path("win"), bad),
        ]
        with self.assertRaises(ValueError):
            MODULE.validate(docs)


if __name__ == "__main__":
    unittest.main()
