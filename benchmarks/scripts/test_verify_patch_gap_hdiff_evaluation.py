"""Negative/contract tests: no real HDiffPatch evaluation on PRs."""
import copy
import importlib.util
import json
import unittest
from pathlib import Path

P = Path(__file__).with_name("verify_patch_gap_hdiff_evaluation.py")
SPEC = importlib.util.spec_from_file_location("hdiff_eval_audit", P)
ref = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ref)
ROOT = Path(__file__).resolve().parents[2]
CAL = json.loads((ROOT / "benchmarks/experiments/patch-gap-001-hdiff-calibration-20261101.json")
                 .read_text(encoding="utf-8"))


def fixture():
    sha = "a" * 64
    groups = [
        ("node-linux-x64", 531),
        ("node-win-x64", 283),
        ("tzdata", 28),
        ("chunkshift-source", 2),
    ]
    original, rows, h0files = [], [], []
    i = 0
    for family, count in groups:
        for _ in range(count):
            src = {"family": family, "base": "old", "target": "new",
                   "path": "f" + str(i), "baseSha256": sha,
                   "baseBytes": 20, "targetSha256": "b"*64, "targetBytes": 21}
            original.append(src)
            rows.append(dict(src, oldSha256=sha, lanes={
                "memory": {"matchMode": "-m-4", "applyCache": "-s-8m",
                           "exactReconstruction": True, "patchBytes": 6,
                           "patchSha256": "c"*64, "createSeconds": .1, "applySeconds": .02},
                "stream": {"matchMode": "-s-64", "applyCache": "-s-8m",
                           "exactReconstruction": True, "patchBytes": 7,
                           "patchSha256": "d"*64, "createSeconds": .2, "applySeconds": .03},
            }))
            h0files.append({"family": family, "baseVersion": "old",
                            "targetVersion": "new", "path": "f"+str(i),
                            "baseSha256": sha, "targetSha256": "b"*64, "targetBytes": 21,
                            "h0PatchSha256": "e"*64, "h0PatchBytes": 10})
            i += 1
    h0files[0]["h0PatchBytes"] = ref.H0_TOTAL - 10 * (ref.COUNT - 1)
    report = {
        "schema": "chunkshift.patch-gap-hdiff-reference.v1",
        "status": "ORIGINAL_REFERENCE_MEASURED",
        "rfcSizeGate": "NOT_APPLICABLE", "role": "evaluation",
        "runId": "PATCH-GAP-001/RUN-20261104-1-" + "f"*7 + "-linux-x64-hdiff-evaluation",
        "pairsSha256": ref.PAIRS_SHA, "protocolCommit": ref.PROTOCOL,
        "hdiffpatchSourceCommit": ref.SOURCE_COMMIT, "archiveSha256": ref.ARCHIVE_SHA,
        "binarySha256": {"hdiffz": "1"*64, "hpatchz": "2"*64},
        "environment": {"sourceGitCommit": "f"*40},
        "files": rows, "totals": {"memory": 6*844, "stream": 7*844},
    }
    h0 = {"schema": "chunkshift.patch-gap-g1-byte-study.v1",
          "datasetRole": "evaluation", "datasetSha256": ref.PAIRS_SHA,
          "sourceCommit": ref.H0_SOURCE_COMMIT, "protocolCommit": ref.PROTOCOL,
          "files": h0files}
    return report, original, h0


class FixedEvaluationAuditTests(unittest.TestCase):
    def test_complete_original_frozen_reference(self):
        x, expected, h0 = fixture()
        result = ref.audit(x, expected, h0, CAL)
        self.assertEqual(result["fileCount"], 844)
        self.assertEqual(result["h0CspBytes"], 26363364)
        self.assertEqual(result["referencePatchBytes"]["memory"], 5064)
        self.assertEqual(result["filesVsH0"]["memory"]["win"], 844)
        self.assertFalse(result["readyToCloseIssue183"])
        self.assertFalse(result["independentBinaryApplyReplayed"])

    def test_missing_or_duplicate_file_rejected(self):
        x, expected, h0 = fixture()
        x["files"] = x["files"][:-1]
        with self.assertRaisesRegex(ValueError, "complete"):
            ref.audit(x, expected, h0, CAL)
        x, expected, h0 = fixture()
        h0["files"][1] = copy.deepcopy(h0["files"][0])
        with self.assertRaisesRegex(ValueError, "duplicate H0"):
            ref.audit(x, expected, h0, CAL)

    def test_wrong_reference_origin_rejected(self):
        x, expected, h0 = fixture()
        x["files"][500]["targetSha256"] = "0"*64
        with self.assertRaisesRegex(ValueError, "original targetSha256"):
            ref.audit(x, expected, h0, CAL)

    def test_wrong_h0_source_or_totals_rejected(self):
        x, expected, h0 = fixture()
        h0["sourceCommit"] = "0"*40
        with self.assertRaisesRegex(ValueError, "original frozen H0"):
            ref.audit(x, expected, h0, CAL)
        x, expected, h0 = fixture()
        h0["files"][2]["h0PatchBytes"] += 1
        with self.assertRaisesRegex(ValueError, "frozen H0 total"):
            ref.audit(x, expected, h0, CAL)

    def test_unaudited_calibration_and_missing_lane_rejected(self):
        x, expected, h0 = fixture()
        bad = copy.deepcopy(CAL)
        bad["run"]["measurementStepsPassed"] = False
        with self.assertRaisesRegex(ValueError, "calibration provenance"):
            ref.audit(x, expected, h0, bad)
        x, expected, h0 = fixture()
        del x["files"][0]["lanes"]["stream"]
        with self.assertRaisesRegex(ValueError, "both frozen modes"):
            ref.audit(x, expected, h0, CAL)

    def test_false_rfc_and_source_binding_rejected(self):
        x, expected, h0 = fixture()
        x["rfcSizeGate"] = "PASS"
        with self.assertRaisesRegex(ValueError, "reference-only evaluation"):
            ref.audit(x, expected, h0, CAL)
        x, expected, h0 = fixture()
        x["environment"]["sourceGitCommit"] = "b"*40
        with self.assertRaisesRegex(ValueError, "eval RunId"):
            ref.audit(x, expected, h0, CAL)

    def test_bad_physical_values_rejected(self):
        x, expected, h0 = fixture()
        x["files"][4]["lanes"]["memory"]["patchBytes"] = 0
        with self.assertRaisesRegex(ValueError, "physical patch"):
            ref.audit(x, expected, h0, CAL)
        x, expected, h0 = fixture()
        x["totals"]["memory"] += 1
        with self.assertRaisesRegex(ValueError, "physical totals"):
            ref.audit(x, expected, h0, CAL)


if __name__ == "__main__":
    unittest.main()
