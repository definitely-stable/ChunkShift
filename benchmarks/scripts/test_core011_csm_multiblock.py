"""Deterministic fail-closed evaluator tests for the multi-CBLK CSM pilot."""

import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

PATH = Path(__file__).with_name("core011_csm_multiblock.py")
SPEC = importlib.util.spec_from_file_location("core011_csm_multiblock", PATH)
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)


def fixture(path):
    for i, (suite, bidx, workers, op) in enumerate(MOD.CASES):
        chunks = 4900
        row = {
            "schema": MOD.SCHEMA,
            "experimentId": "CORE011-CSM-001",
            "runId": "CI",
            "checkoutSha": "synthetic",
            "pullRequestHeadSha": "head",
            "fixture": {
                "sizeMiB": 384, "seed": "0xC4855A112026",
                "profileId": "fastcdc.gear.chunkshift.v1.64k",
                "payloadCollectedBeforeMeasurement": True,
                "contentSha256": "a" * 64,
                "manifestSha256": ("b" if bidx else "c") * 64,
                "manifestBytes": 180000,
                "chunkCount": chunks,
                "minCblkBlocksBy4096EntryCap": 2,
            },
            "conditions": {
                "suite": suite, "includeBlockIndex": bidx,
                "workers": workers, "operation": op,
                "rounds": 2, "repetitions": 4, "minChunks": 4097,
                "memoryOnly": True, "fixtureAndContentHashOutsideTimedRegion": True,
            },
            "managedBytesAfterWarmupGc": 300000,
            "lohSizeAfterWarmupGcBytes": 160000,
            "retention": {
                "managedBytesAfterExplicitGc": 310000,
                "lastGcLohSizeAfterBytes": 180000,
                "lastGcHeapSizeBytes": 320000,
                "lastGcIndex": 5, "workingSetAfterGcBytes": 500000,
            },
            "samples": [],
            "verdict": "NO_VERDICT_EXPLORATORY",
        }
        for round_number in (1, 2):
            row["samples"].append({
                "round": round_number, "wallMilliseconds": 10.0,
                "processCpuMilliseconds": 10.0,
                "processAllocatedBytes": 1000,
                "managedHeapBeforeBytes": 310000,
                "managedHeapAfterBytes": 320000,
                "workingSetAfterBytes": 500000,
                "processLifetimePeakWorkingSetBytes": 510000,
                "gen0Collections": 0, "gen1Collections": 0,
                "gen2Collections": 0,
                "lastGcLohSizeAfterBytes": 160000,
                "lastGcIndex": 4,
                "completedOperations": workers * 4,
                "validatedChunks": workers * 4 * chunks,
                "maxActiveOperations": workers,
            })
        (path / f"case-{i:02d}.json").write_text(json.dumps(row))


class CsmMultiCblkValidationTests(unittest.TestCase):
    def check(self, folder):
        return MOD.validate(folder, "CI", 384, 2, 4, 4097)

    def test_all_frozen_cells(self):
        with tempfile.TemporaryDirectory() as name:
            p = Path(name)
            fixture(p)
            result = self.check(p)
            self.assertEqual("NO_VERDICT_EXPLORATORY", result["verdict"])
            self.assertEqual(12, result["caseCount"])
            self.assertEqual(24, result["sampleCount"])

    def test_refuses_missing_cblk_crossing(self):
        with tempfile.TemporaryDirectory() as name:
            p = Path(name)
            fixture(p)
            x = p / "case-00.json"
            data = json.loads(x.read_text())
            data["fixture"]["chunkCount"] = 4096
            x.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "multi-CBLK"):
                self.check(p)

    def test_rejects_retained_source(self):
        with tempfile.TemporaryDirectory() as name:
            p = Path(name)
            fixture(p)
            x = p / "case-00.json"
            data = json.loads(x.read_text())
            data["fixture"]["payloadCollectedBeforeMeasurement"] = False
            x.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "source payload"):
                self.check(p)

    def test_rejects_partial_enumeration(self):
        with tempfile.TemporaryDirectory() as name:
            p = Path(name)
            fixture(p)
            x = p / "case-00.json"
            data = json.loads(x.read_text())
            data["samples"][0]["validatedChunks"] -= 1
            x.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "incomplete reader"):
                self.check(p)

    def test_rejects_mismatched_manifest_identity(self):
        with tempfile.TemporaryDirectory() as name:
            p = Path(name)
            fixture(p)
            x = p / "case-08.json"
            data = json.loads(x.read_text())
            data["fixture"]["manifestSha256"] = "f" * 64
            x.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "manifest identity"):
                self.check(p)


if __name__ == "__main__":
    unittest.main()
