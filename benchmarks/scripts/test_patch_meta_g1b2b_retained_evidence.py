"""Recompute retained original PATCH-META-001 G1B2B medians and refuse fake cache claims."""
import json
import math
import re
import statistics
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEDGER = ROOT / "benchmarks/experiments/patch-meta-001-g1b2b-original-evidence-20261105.json"
REPORT = ROOT / "docs/research/results/PATCH-META-001-G1B2B-FIRST-RELEASE-DEFER-20261105-001.md"
INDEX = ROOT / "docs/research/EXPERIMENT-INDEX.md"
REGISTRY = ROOT / "benchmarks/experiments/research-registry.v1.json"
SHA64 = re.compile(r"[0-9a-f]{64}\\Z")

def verify(record):
    assert record["schema"] == "chunkshift.patch-meta-001-original-codec-evidence.v1"
    assert record["experimentId"] == "PATCH-META-001" and record["status"] == "DEFER"
    assert record["scope"] == "FIRST_PUBLIC_PATCHING_RELEASE_ONLY"
    assert record["run"]["id"] == 41953519062
    assert record["run"]["attempt"] == 1 and record["run"]["conclusion"] == "success"
    assert record["run"]["sha"] == "7dea7c722bfeefd3871ff051903dc62d3eec3881"
    assert record["run"]["stackedPR"] == 445
    assert record["run"]["parentDraftPRs"] == [372,370,368,367]
    assert record["source"]["versions"] == ["4.13.0", "4.14.0"]
    assert record["source"]["exactOriginal"] is True
    assert record["outputSameBuildCspSha256"] == "842b8f8904556e0793e1a788058f33755bdc891eb17fe51689a91a878997f0b6"
    assert record["cacheABPerformed"] is False and record["readyToAdopt"] is False
    assert record["eligibleCacheSavingsNotMeasured"] is True
    assert record["independentProductFamiliesMeasured"] == 1
    assert record["readyToFreezePatchingAPI"] is False
    expect = {
        "linux-x64": ("77a7aa4d8dde8544c3914f6279c61d0fd61e368603205b72421b2f85334e291e",
                      "4e70b053571d9720eee244871745fd1f347da63595b8f4cbfdc0467d1ee9cd6a",
                      13382387028,15531,11.5312565,11.52993885,2.7670631805,19.7816436115,0.0418015255),
        "windows-x64": ("cefd98578688bfd4961a80324c17e9a2cbba01d987f3359a3a26a5666483b5c7",
                        "cead68845ce4fd8f9cda3b0f3ac27fba2f29c137aa24b870dfa87a4ac054e84e",
                        13382636333,15503,9.75396815,9.7897074,2.19473725,16.9204577,0.0599812),
    }
    assert set(record["platforms"]) == set(expect)
    for platform,data in record["platforms"].items():
        zipsha,summarysha,artifact,size,offwall,onwall,plain,dc,read=expect[platform]
        assert data["archiveZipSha256"] == zipsha
        assert data["codecSummarySha256"] == summarysha
        assert data["artifactId"] == artifact and data["archiveZipBytes"] == size
        assert len(data["samples"]) == 10
        rows=data["samples"]
        assert [(row[0],row[1]) for row in rows] == [
            (0,"off"),(0,"on"),(1,"on"),(1,"off"),(2,"off"),(2,"on"),
            (3,"on"),(3,"off"),(4,"off"),(4,"on")
        ]
        seen=set()
        for row in rows:
            assert len(row) == 11
            rnd,mode,bwall,bcpu,sha,*vals=row
            assert isinstance(sha,str) and re.fullmatch(r"[0-9a-f]{64}",sha)
            assert sha not in seen
            seen.add(sha)
            assert type(bwall) in (int,float) and math.isfinite(bwall) and bwall>0
            assert type(bcpu) in (int,float) and math.isfinite(bcpu) and bcpu>0
            if mode=="off":
                assert vals == [None]*6
            else:
                assert all(type(v) in (int,float) and math.isfinite(v) and v>0 for v in vals)
                assert vals[3:] == [81,464,464]
        warm_off=[row for row in rows if row[0]>0 and row[1]=="off"]
        warm_on=[row for row in rows if row[0]>0 and row[1]=="on"]
        assert len(warm_off) == len(warm_on) == 4
        m=data["medians"]
        for label,column,expected in (("offBuilder",2,offwall),("onBuilder",2,onwall),
                                      ("plain",5,plain),("dict",6,dc),("read",7,read)):
            subset=warm_off if label=="offBuilder" else warm_on
            value=statistics.median(row[column] for row in subset)
            assert abs(value-expected)<1e-8 and abs(m[label]-value)<1e-8
        assert data["medianWarmDictionaryReadBytes"]==164725443
    return True

class OriginalEvidence(unittest.TestCase):
    def test_all_20_stage_fingerprints_and_warm_medians(self):
        doc=json.loads(LEDGER.read_text(encoding="utf-8"))
        self.assertTrue(verify(doc))
    def test_manipulated_scientific_state_rejected(self):
        doc=json.loads(LEDGER.read_text(encoding="utf-8"))
        doc["cacheABPerformed"]=True
        with self.assertRaises(AssertionError):
            verify(doc)
    def test_mutated_timing_or_stage_sha_rejected(self):
        doc=json.loads(LEDGER.read_text(encoding="utf-8"))
        doc["platforms"]["linux-x64"]["samples"][2][6]+=0.2
        with self.assertRaises(AssertionError):
            verify(doc)
        doc=json.loads(LEDGER.read_text(encoding="utf-8"))
        doc["platforms"]["windows-x64"]["samples"][1][4]="INVALID"
        with self.assertRaises(AssertionError):
            verify(doc)
    def test_status_and_index(self):
        reg=json.loads(REGISTRY.read_text(encoding="utf-8"))
        entry=next(x for x in reg["experiments"] if x["id"]=="PATCH-META-001")
        assert entry["status"]=="DEFER"
        assert entry["firstReleaseEvidence"]=="docs/research/results/PATCH-META-001-G1B2B-FIRST-RELEASE-DEFER-20261105-001.md"
        row=next(s for s in INDEX.read_text(encoding="utf-8").splitlines()
                 if s.startswith("| PATCH-META-001 |"))
        assert "| DEFER (" in row and "PATCH-META-001-G1B2B-FIRST-RELEASE-DEFER-20261105-001.md" in row
        assert "No actual cache A/B" in REPORT.read_text(encoding="utf-8")

if __name__=="__main__":
    unittest.main()
