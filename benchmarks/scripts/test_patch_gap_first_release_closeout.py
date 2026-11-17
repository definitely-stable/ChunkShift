"""PATCH-GAP-001 first-release research freeze: evidence-linked disposition guards."""
import copy
import hashlib
import json
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
D=ROOT/"benchmarks/experiments/patch-gap-001-first-release-closeout-20261104.json"
PIN=ROOT/"docs/research/results/data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json"
G1=ROOT/"docs/research/results/data/PATCH-GAP-001-G1-EVALUATION-EVIDENCE-20261006-001/evaluation.json"
G3=ROOT/"docs/research/results/data/PATCH-GAP-001-G3-EVALUATION-EVIDENCE-20261007-001/evaluation.json"
G4=ROOT/"docs/research/results/data/PATCH-GAP-001-G4-EVIDENCE-20261008-001/summary.json"
G5=ROOT/"docs/research/results/data/PATCH-GAP-001-20261003-001/aggregates.json"
HDIFF=ROOT/"benchmarks/experiments/patch-gap-001-hdiff-evaluation-20261104.json"
INDEX=ROOT/"docs/research/EXPERIMENT-INDEX.md"
REGISTRY=ROOT/"benchmarks/experiments/research-registry.v1.json"
REPORT=ROOT/"docs/research/results/PATCH-GAP-001-FIRST-RELEASE-CLOSEOUT-20261104-001.md"
ZIP_SHA="9e3e5b88ec6203d198466f5f2fd5df5e382f561c3a2175b64b4af205c3909c40"


def data(path):
    return json.loads(path.read_text(encoding="utf-8"))


def verify(d, g1, g3, g4, g5, hd, pin_sha):
    assert d["schema"]=="chunkshift.patch-gap-001-first-release-disposition.v1"
    assert d["experimentId"]=="PATCH-GAP-001"
    assert d["status"]=="DEFER" and d["scope"]=="FIRST_PUBLIC_PATCHING_API_RELEASE_ONLY"
    assert d["firstReleaseCsp"]=="CSP_V1_UNCHANGED_NO_VNEXT_RFC_FROM_TESTED_FACTORS"
    assert d["requiredRfcGateFraction"]==0.15
    assert d["projectGate251RemainsOpen"] is True
    factors=d["singleFactor"]
    assert g1["status"]==g3["status"]==g4["status"]=="REJECT"
    assert g1["datasetSha256"]==g3["datasetSha256"]==hd["originalCorpusSha256"]
    assert g1["protocolCommit"]==g3["protocolCommit"]==g4["protocolCommit"]==hd["protocolCommit"]
    best=next(x for x in g1["lanes"] if x["lane"]==factors["G1"]["bestLane"])
    assert best["factorBytes"]==factors["G1"]["factorBytes"]==25763908
    assert best["h0Bytes"]==factors["G1"]["h0Bytes"]==26363364
    assert best["savedBytes"]==factors["G1"]["improvementBytes"]==599456
    assert abs(best["reductionVsCsp"]-factors["G1"]["improvementFraction"])<1e-12
    for lane, field in (("G3-RUN", "runBytes"),("G3-FILE", "fileBytes")):
        x=next(y for y in g3["evaluation"]["lanes"] if y["lane"]==lane)
        assert x["factorBytes"]==factors["G3"][field]
        assert x["meetsRfcSizeGate"] is False
    b=g4["splits"]["evaluation"]["lane"]
    assert b["h0Bytes"]==factors["G4_BCJ"]["h0Bytes"]==26363364
    assert b["factorBytes"]==factors["G4_BCJ"]["factorBytes"]==26111524
    assert b["savedBytes"]==factors["G4_BCJ"]["improvementBytes"]==251840
    assert abs(b["reductionVsCsp"]-factors["G4_BCJ"]["improvementFraction"])<1e-12
    assert g5["g5"]["calibration"]["puffinSupportedRows"]==factors["G5_PUFFIN"]["calibrationSupportedRows"]==0
    assert g5["g5"]["evaluation"]["puffinSupportedRows"]==factors["G5_PUFFIN"]["evaluationSupportedRows"]==0
    assert factors["G2"]["status"]=="STOP"
    h=d["externalReferences"]["HDiffPatch"]
    assert h["rfcSizeGate"]=="NOT_APPLICABLE"
    assert hd["fileCount"]==h["evaluationRows"]==844
    assert hd["h0CspBytes"]==h["evaluationH0CspBytes"]==26363364
    assert hd["lanes"]["memory"]["patchBytes"]==h["evaluationMemoryBytes"]==11005397
    assert hd["lanes"]["stream"]["patchBytes"]==h["evaluationStreamBytes"]==33207126
    assert h["evaluationMemorySavingBytes"]==h["evaluationH0CspBytes"]-h["evaluationMemoryBytes"]
    assert h["evaluationStreamDeltaBytes"]==h["evaluationStreamBytes"]-h["evaluationH0CspBytes"]
    z=d["externalReferences"]["ZUCCHINI"]
    assert z["status"]=="DEFER_RESOURCE_UNVERIFIED__REFERENCE_NOT_MEASURED"
    assert z["sourcePinSha256"]==pin_sha
    assert z["binaryBuilt"] is False and z["calibrationRun"] is False and z["evaluationRun"] is False
    assert z["patchBytes"] is None and z["matchedSupportedSubsetPatchBytes"] is None
    assert z["organizationLargerRunnerInventory"]=="NOT_VERIFIED"
    e=z["resourceEvidence"]
    assert e["workflowRunId"]==41876675201 and e["workflowRunAttempt"]==1
    assert e["workflowConclusion"]=="success"
    assert e["sourceCommit"]=="7e110d0d9d02e517f0b3766cd5b725e2247f1fea"
    assert e["artifactId"]==13347391067 and e["artifactZipSha256"]==ZIP_SHA
    assert e["artifactZipBytes"]==1205
    assert e["standardRunnerLabel"]=="ubuntu-24.04" and e["runnerProvider"]=="github-hosted"
    assert e["freeDiskBytes"]==91750961152 and e["physicalMemoryBytes"]==16760344576
    assert e["requiredFreeDiskGiB"]==100 and e["requiredRamGiB"]==8
    assert e["freeDiskDeficitBytes"]==e["requiredFreeDiskGiB"]*2**30-e["freeDiskBytes"]>0
    assert e["canBuild"] is False and e["diskPass"] is False and e["ramPass"] is True
    assert d["interactionDecision"]["status"]=="NOT_TRIGGERED_FOR_FIRST_RELEASE"
    assert d["interactionDecision"]["proofOfAbsence"] is False


class FirstReleaseDispositionTests(unittest.TestCase):
    def test_evidence_reconciliation(self):
        verify(data(D), data(G1), data(G3), data(G4), data(G5), data(HDIFF),
               hashlib.sha256(PIN.read_bytes()).hexdigest())

    def test_no_fake_zucchini_measurements(self):
        d=data(D)
        d["externalReferences"]["ZUCCHINI"]["patchBytes"]=0
        with self.assertRaises(AssertionError):
            verify(d, data(G1),data(G3),data(G4),data(G5),data(HDIFF),
                   hashlib.sha256(PIN.read_bytes()).hexdigest())

    def test_false_disk_and_csp_outcomes_rejected(self):
        for key,value in (("diskPass",True),("freeDiskBytes",200*2**30)):
            d=data(D)
            d["externalReferences"]["ZUCCHINI"]["resourceEvidence"][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):
                verify(d,data(G1),data(G3),data(G4),data(G5),data(HDIFF),
                       hashlib.sha256(PIN.read_bytes()).hexdigest())

    def test_registry_and_index_point_to_final_decision(self):
        registry=data(REGISTRY)
        record=next(x for x in registry["experiments"] if x["id"]=="PATCH-GAP-001")
        assert record["status"]=="DEFER"
        assert record["firstReleaseEvidence"]=="docs/research/results/PATCH-GAP-001-FIRST-RELEASE-CLOSEOUT-20261104-001.md"
        assert record["firstReleaseMachine"]=="benchmarks/experiments/patch-gap-001-first-release-closeout-20261104.json"
        idx=INDEX.read_text(encoding="utf-8")
        row=next(x for x in idx.splitlines() if x.startswith("| PATCH-GAP-001 |"))
        assert "| DEFER (" in row and "PATCH-GAP-001-FIRST-RELEASE-CLOSEOUT-20261104-001.md" in row
        report=REPORT.read_text(encoding="utf-8")
        assert "DEFER_RESOURCE_UNVERIFIED" in report and "CSP v1" in report


if __name__=="__main__":
    unittest.main()
