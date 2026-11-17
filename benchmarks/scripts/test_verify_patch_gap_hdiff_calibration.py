"""Fail-closed synthetic tamper tests for HDiffPatch calibration report auditing."""
import copy
import importlib.util
import unittest
from pathlib import Path

PATH = Path(__file__).with_name("verify_patch_gap_hdiff_calibration.py")
SPEC = importlib.util.spec_from_file_location("hdiff_calibration_audit", PATH)
ref = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ref)


def sample():
    sha = "a" * 64
    source = {"family": "dotnet-aspnetcore-win-x64", "base": "1", "target": "2",
              "path": "test.dll", "baseSha256": sha, "baseBytes": 20,
              "targetSha256": "b" * 64, "targetBytes": 21}
    rows = [dict(source) for _ in range(1049)]
    def lane(mode, patch):
        return {"patchBytes": patch, "patchSha256": "c" * 64,
                "createSeconds": 0.1, "applySeconds": 0.01, "matchMode": mode,
                "applyCache": "-s-8m", "exactReconstruction": True}
    out = [dict(row, oldSha256=sha, lanes={
        "memory": lane("-m-4", 6), "stream": lane("-s-64", 7)}) for row in rows]
    report = {
        "schema": "chunkshift.patch-gap-hdiff-reference.v1",
        "status": "ORIGINAL_REFERENCE_MEASURED",
        "rfcSizeGate": "NOT_APPLICABLE", "role": "calibration",
        "runId": "PATCH-GAP-001/RUN-20261101-3-" + "d" * 7 + "-linux-x64-hdiff-calibration",
        "pairsSha256": ref.PAIRS_SHA, "protocolCommit": ref.PROTOCOL,
        "hdiffpatchSourceCommit": ref.SOURCE_COMMIT, "archiveSha256": ref.ARCHIVE_SHA,
        "binarySha256": {"hdiffz": "e" * 64, "hpatchz": "f" * 64},
        "environment": {"sourceGitCommit": "d" * 40},
        "files": out, "totals": {"memory": 6294, "stream": 7343},
    }
    return report, rows


class CalibrationAuditTests(unittest.TestCase):
    def test_complete_original_population_recomputes(self):
        data, rows = sample()
        result = ref.validate(data, rows)
        self.assertEqual(1049, result["files"])
        self.assertEqual(6294, result["referencePatchBytes"]["memory"])
        self.assertFalse(result["readyForEvaluation"])
        self.assertFalse(result["readyToCloseIssue183"])
        self.assertFalse(result["independentBinaryApplyReplayed"])

    def test_missing_file_is_fatal(self):
        data, rows = sample()
        data["files"].pop()
        with self.assertRaisesRegex(ValueError, "complete"):
            ref.validate(data, rows)

    def test_wrong_file_identity_is_fatal(self):
        data, rows = sample()
        data["files"][1]["path"] = "unapproved.dll"
        with self.assertRaisesRegex(ValueError, "frozen identity"):
            ref.validate(data, rows)

    def test_bad_lane_byte_total_is_fatal(self):
        data, rows = sample()
        data["totals"]["memory"] += 1
        with self.assertRaisesRegex(ValueError, "producer totals"):
            ref.validate(data, rows)

    def test_unverified_reconstruction_is_fatal(self):
        data, rows = sample()
        data["files"][3]["lanes"]["stream"]["exactReconstruction"] = False
        with self.assertRaisesRegex(ValueError, "exactness"):
            ref.validate(data, rows)

    def test_non_finite_or_fabricated_patch_is_fatal(self):
        for field, val in (("patchBytes", 0), ("createSeconds", float("nan"))):
            with self.subTest(field=field):
                data, rows = sample()
                data["files"][4]["lanes"]["memory"][field] = val
                with self.assertRaises(ValueError):
                    ref.validate(data, rows)

    def test_reference_is_never_a_csp_factor_gate(self):
        data, rows = sample()
        data["rfcSizeGate"] = "PASS"
        with self.assertRaisesRegex(ValueError, "RFC gate"):
            ref.validate(data, rows)

    def test_unbound_git_identity_is_fatal(self):
        data, rows = sample()
        data["environment"]["sourceGitCommit"] = "a" * 40
        with self.assertRaisesRegex(ValueError, "RunId Git HEAD binding"):
            ref.validate(data, rows)


if __name__ == "__main__":
    unittest.main()
