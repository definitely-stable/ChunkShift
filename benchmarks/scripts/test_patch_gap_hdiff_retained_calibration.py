"""Guard the retained HDiffPatch original-calibration ledger against silent rewrite."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "benchmarks/experiments/patch-gap-001-hdiff-calibration-20261101.json"
G1 = ROOT / "docs/research/results/data/PATCH-GAP-001-G1-CALIBRATION-EVIDENCE-20261004-001/compact.json"
G1RUN = ROOT / "docs/research/results/data/PATCH-GAP-001-G1-CALIBRATION-EVIDENCE-20261004-001/calibration.json"


def verify(d, h0, original):
    assert d["schema"] == "chunkshift.patch-gap-001-hdiff-original-calibration-retained.v1"
    assert d["status"].startswith("MEASURED_CALIBRATION_ONLY__")
    assert d["role"] == "calibration" and d["rfcSizeGate"] == "NOT_APPLICABLE"
    assert d["readyToCloseIssue183"] is False and d["readyForEvaluation"] is False
    assert d["run"]["workflowConclusion"] == "failure"
    assert d["run"]["reportAuditStepPassed"] is True and d["run"]["measurementStepsPassed"] is True
    assert d["run"]["artifactUploadPassed"] is True and d["run"]["runAttempt"] == 1
    assert d["run"]["sourceCommit"] == "f0224c252503c09174093031650ea2ca1c86ee04"
    assert d["run"]["workflowRunId"] == 41333408342
    assert d["source"]["pairsSha256"] == h0["datasetSha256"]
    assert d["source"]["protocolCommit"] == h0["protocolCommit"]
    assert d["h0Control"]["zipSha256"] == original["artifact"]["artifactZipSha256"]
    assert d["h0Control"]["calibrationBytes"] == h0["h0Anchor"]["calibrationBytes"]
    assert d["h0Control"]["datasetSha256"] == h0["datasetSha256"]
    a=d["referenceArtifact"]
    assert a["id"] == 13094063839 and a["zipBytes"] == 244427
    assert a["reportedDigest"] == "sha256:"+a["zipSha256"]
    assert a["zipSha256"] == "e42db34baf9e5bb1c5374ac151d6d2ebf2a9862377d2964e6d1d0a058411548b"
    assert a["physicalRowsSha256"] == "cd6ed78e209625f9988311b6c47cde695f29dfbb47f50a537975e8b86a9876f7"
    assert d["audit"]["independentBinaryReapply"] is False
    assert d["audit"]["perFileSourceAndTargetShaMatchIndependentG1"] is True
    families=d["families"]
    assert set(families)==set(h0["families"])
    for name,base in h0["families"].items():
        assert families[name]["files"] == base["fileCount"]
        assert families[name]["h0Bytes"] == base["h0Bytes"]
    assert sum(f["files"] for f in families.values()) == d["fileCount"] == 1049
    assert sum(f["targetBytes"] for f in families.values()) == d["targetBytes"] == 393123835
    assert len(d["pairs"]) == 4
    for metric in ("files","h0Bytes","memoryBytes","streamBytes"):
        assert sum(p[metric] for p in d["pairs"].values()) == sum(f[metric] for f in families.values())
    assert sum(f["h0Bytes"] for f in families.values()) == d["h0Control"]["calibrationBytes"] == 11860274
    for lane in ("memory","stream"):
        total=sum(f[lane+"Bytes"] for f in families.values())
        assert d["totals"][lane]["patchBytes"] == total
        assert d["totals"][lane]["savedVsH0Bytes"] == 11860274-total
        assert abs(d["totals"][lane]["reductionPercent"] - 100*(11860274-total)/11860274)<1e-5
        assert sum(d["totals"][lane][k] for k in ("beatH0Files","tieH0Files","worseH0Files")) == 1049
    assert d["totals"]["memory"]["beatH0Files"] == 1049
    assert d["totals"]["stream"]["worseH0Files"] == 218


class LockedCalibrationLedger(unittest.TestCase):
    def test_retained_original_calibration_ledger(self):
        verify(json.loads(PATH.read_text(encoding="utf-8")),
               json.loads(G1.read_text(encoding="utf-8")),
               json.loads(G1RUN.read_text(encoding="utf-8")))

    def test_tampered_totals_fail_closed(self):
        doc=json.loads(PATH.read_text(encoding="utf-8"))
        doc["totals"]["memory"]["patchBytes"]+=1
        with self.assertRaises(AssertionError):
            verify(doc,json.loads(G1.read_text(encoding="utf-8")),
                   json.loads(G1RUN.read_text(encoding="utf-8")))


if __name__ == "__main__":
    unittest.main()
