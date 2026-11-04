"""Durable HDiff original fixed evaluation physical-size archive: all 844 rows."""
import base64
import copy
import hashlib
import json
import struct
import unittest
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FILE = ROOT / "benchmarks/experiments/patch-gap-001-hdiff-evaluation-20261104.json"
H0 = ROOT / "docs/research/results/data/PATCH-GAP-001-G1-EVALUATION-EVIDENCE-20261006-001/evaluation.json"
CAL = ROOT / "benchmarks/experiments/patch-gap-001-hdiff-calibration-20261101.json"

def verify(doc, h0, cal):
    assert doc["schema"] == "chunkshift.patch-gap-hdiff-evaluation-retained.v1"
    assert doc["role"] == "evaluation" and doc["rfcSizeGate"] == "NOT_APPLICABLE"
    assert doc["readyToCloseIssue183"] is False and doc["readyToCloseGate251"] is False
    assert doc["originalRun"]["workflowRunId"] == 41869881770
    assert doc["originalRun"]["runConclusion"] == "success"
    assert doc["originalRun"]["sourceCommit"] == "37c43c7d551280efd925aa20de90b780e1585e78"
    assert doc["originalReferenceArtifact"]["zipSha256"] == "0d1141fdfab562dee42c12443ebfa97f3632c308c5c6696823bdd0f802a47d49"
    assert doc["originalReferenceArtifact"]["matchedPhysicalRowsSha256"] == "33991b5af38a4253ed457589fb35f8c5f2a679f8959b8bb3ce0db96a1f225e46"
    assert doc["originalH0Artifact"]["zipSha256"] == "dc2a725f1612556f711d07b819f3de17d3b887bcd2e38a598eb557f9ae3ed2b0"
    assert doc["originalCorpusSha256"] == h0["datasetSha256"] == cal["source"]["pairsSha256"]
    assert doc["protocolCommit"] == cal["source"]["protocolCommit"]
    assert doc["originalToolLinuxZipSha256"] == cal["source"]["hdiffOfficialLinuxZipSha256"]
    assert doc["originalCalibrationEvidence"].endswith("patch-gap-001-hdiff-calibration-20261101.json")
    assert doc["fileCount"] == h0["sampleFileCount"] == 844
    assert doc["h0CspBytes"] == 26363364
    assert any(x["lane"] == "G1-B1-R64" and x["h0Bytes"] == 26363364 for x in h0["lanes"])
    assert doc["audit"]["independentlyRecomputedAllFileBytesAndGroupTotals"] is True
    assert doc["audit"]["independentBinaryReapply"] is False
    assert doc["audit"]["freshStatisticalHoldout"] is False
    packed = doc["physicalRowData"]
    assert packed["encoding"] == "zlib+base64" and packed["binaryFormat"] == "<III"
    assert packed["rows"] == doc["fileCount"] and packed["bytesPerRow"] == 12
    assert packed["uncompressedBytes"] == 844 * 12
    encoded = packed["data"]
    assert hashlib.sha256(encoded.encode("ascii")).hexdigest() == packed["base64Sha256"] == "8a87706adce9f25f1178edf89161f25b1ffd795bcb00d081956d461eeb1fb475"
    raw = zlib.decompress(base64.b64decode(encoded, validate=True))
    assert len(raw) == 844*12
    assert hashlib.sha256(raw).hexdigest() == packed["uncompressedSha256"] == "274bd27e1ae275d92d0b78d30cfe1c4669dd20f6c0f6c4522a20bf148abaf0ca"
    rows = [struct.unpack_from("<III", raw, offset) for offset in range(0, len(raw), 12)]
    assert all(min(row) > 0 for row in rows)
    groups = doc["groups"]
    assert len(groups) == 8
    at = 0
    family_counts = {}
    sums = [0,0,0]
    wins = [0,0]
    losses = [0,0]
    for group in groups:
        n = group["fileCount"]
        subset = rows[at:at+n]
        assert len(subset) == n and n > 0
        group_totals = [sum(row[i] for row in subset) for i in range(3)]
        assert group_totals == [group["h0Bytes"], group["memoryBytes"], group["streamBytes"]]
        family_counts[group["family"]] = family_counts.get(group["family"],0) + n
        for i in range(3):
            sums[i] += group_totals[i]
        for row in subset:
            for i in (1,2):
                if row[i] < row[0]:
                    wins[i-1] += 1
                elif row[i] > row[0]:
                    losses[i-1] += 1
        at += n
    assert at == 844
    assert family_counts == {"node-win-x64":283,"node-linux-x64":531,"tzdata":28,"chunkshift-source":2}
    assert sums == [doc["h0CspBytes"], doc["lanes"]["memory"]["patchBytes"], doc["lanes"]["stream"]["patchBytes"]]
    assert sums == [26363364, 11005397, 33207126]
    assert wins == [839,830] and losses == [5,14]
    for i,mode in enumerate(("memory","stream"),1):
        lane = doc["lanes"][mode]
        assert lane["savedBytes"] == sums[0]-sums[i]
        assert lane["winCount"] == wins[i-1] and lane["lossCount"] == losses[i-1]
        assert lane["tieCount"] == 844-wins[i-1]-losses[i-1]
        assert abs(lane["savingsPercent"]-100*(sums[0]-sums[i])/sums[0]) < 1e-9
    assert doc["biggestFiveMemorySavingsBytes"] == sum(
        sorted((a-b for a,b,c in rows), reverse=True)[:5])


class EvidenceArchiveTests(unittest.TestCase):
    def test_all_844_original_size_triplets(self):
        verify(json.loads(FILE.read_text(encoding="utf-8")),
               json.loads(H0.read_text(encoding="utf-8")),
               json.loads(CAL.read_text(encoding="utf-8")))

    def test_tamper_rejected(self):
        d=json.loads(FILE.read_text(encoding="utf-8"))
        d["lanes"]["memory"]["patchBytes"] += 1
        with self.assertRaises(AssertionError):
            verify(d,json.loads(H0.read_text(encoding="utf-8")),
                   json.loads(CAL.read_text(encoding="utf-8")))
        d=json.loads(FILE.read_text(encoding="utf-8"))
        d["physicalRowData"]["data"]= "a"+d["physicalRowData"]["data"][1:]
        with self.assertRaises((AssertionError,ValueError,zlib.error)):
            verify(d,json.loads(H0.read_text(encoding="utf-8")),
                   json.loads(CAL.read_text(encoding="utf-8")))


if __name__=="__main__":
    unittest.main()
