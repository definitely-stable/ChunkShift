"""Independent fail-closed validation for CORE011-CSM-001 G1-E."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

PATH = Path(__file__).with_name("core011_csm_sustained.py")
SPEC = importlib.util.spec_from_file_location("core011_csm_sustained", PATH)
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)
RUN_ID = "CORE011-CSM-001/G1E-TEST"


def create_synthetic(folder):
    for size, operation, workers in MOD.CASES:
        chunks = 5052 if size == 384 else 11
        row = {
            "schema": "chunkshift.core011.csm-multiblock-metadata.v1",
            "experimentId": "CORE011-CSM-001",
            "runId": RUN_ID, "checkoutSha": "test-commit",
            "verdict": "NO_VERDICT_EXPLORATORY",
            "conditions": {
                "operation": operation, "suite": "blake3",
                "includeBlockIndex": True, "workers": workers,
                "rounds": MOD.ROUNDS, "repetitions": MOD.REPETITIONS,
                "traceEvents": False, "memoryOnly": True,
                "fixtureAndContentHashOutsideTimedRegion": True,
            },
            "fixture": {
                "sizeMiB": size, "chunkCount": chunks,
                "minCblkBlocksBy4096EntryCap": (chunks + 4095)//4096,
                "manifestSha256": MOD.FULL_MANIFEST_SHA if size == 384 else "a"*64,
                "contentSha256": MOD.FULL_SOURCE_SHA if size == 384 else "b"*64,
                "manifestBytes": 182366 if size == 384 else 600,
                "payloadCollectedBeforeMeasurement": True,
            },
            "managedBytesAfterWarmupGc": 300000,
            "retention": {
                "managedBytesAfterExplicitGc": 310000,
                "lastGcLohSizeAfterBytes": 180000,
                "lastGcHeapSizeBytes": 320000,
                "workingSetAfterGcBytes": 500000,
            },
            "samples": [
                {
                    "round": i, "wallMilliseconds": 14.0,
                    "processCpuMilliseconds": 11.0,
                    "processAllocatedBytes": 200000*workers*MOD.REPETITIONS,
                    "gen0Collections": 1, "gen1Collections": 1,
                    "gen2Collections": 0, "managedHeapBeforeBytes": 310000,
                    "managedHeapAfterBytes": 320000,
                    "workingSetAfterBytes": 500000,
                    "lastGcLohSizeAfterBytes": 180000, "lastGcIndex": 5,
                    "completedOperations": workers*MOD.REPETITIONS,
                    "validatedChunks": chunks*workers*MOD.REPETITIONS,
                    "maxActiveOperations": workers,
                }
                for i in range(1, MOD.ROUNDS + 1)
            ]
        }
        (folder/f"size-{size}-{operation}-w{workers}.json").write_text(
            json.dumps(row), encoding="utf-8")


class G1eEvaluatorTests(unittest.TestCase):
    def mutate_and_reject(self, size, operation, workers, mutate, pattern):
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            create_synthetic(folder)
            path = folder/f"size-{size}-{operation}-w{workers}.json"
            row = json.loads(path.read_text())
            mutate(row)
            path.write_text(json.dumps(row))
            with self.assertRaisesRegex(ValueError, pattern):
                MOD.validate(folder, RUN_ID)

    def test_complete_matrix(self):
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            create_synthetic(folder)
            result = MOD.validate(folder, RUN_ID)
            self.assertEqual(12, result["caseCount"])
            self.assertEqual(60, result["sampleCount"])
            self.assertEqual(200000, result["lanes"][0]["allocPerOperationMedianBytes"])

    def test_missing_scenario_rejected(self):
        with tempfile.TemporaryDirectory() as name:
            folder = Path(name)
            create_synthetic(folder)
            (folder/"size-384-enumerate-w16.json").unlink()
            with self.assertRaises(FileNotFoundError):
                MOD.validate(folder, RUN_ID)

    def test_source_must_be_collected(self):
        self.mutate_and_reject(384, "manifest-verify", 1,
            lambda x: x["fixture"].update(payloadCollectedBeforeMeasurement=False),
            "source not collected")

    def test_physical_identity_is_pinned(self):
        self.mutate_and_reject(384, "enumerate", 16,
            lambda x: x["fixture"].update(manifestSha256="c"*64),
            "physical CSM drift")

    def test_rejects_partial_batch(self):
        self.mutate_and_reject(1, "enumerate", 4,
            lambda x: x["samples"][0].update(validatedChunks=123),
            "incomplete public CSM")

    def test_rejects_missing_round(self):
        self.mutate_and_reject(1, "manifest-verify", 1,
            lambda x: x["samples"].pop(),
            "missing/extra rounds")

    def test_rejects_non_finite_times(self):
        self.mutate_and_reject(384, "manifest-verify", 16,
            lambda x: x["samples"][0].update(wallMilliseconds=float("nan")),
            "invalid nonnegative numeric")

    def test_no_trace_enabled_in_baseline(self):
        self.mutate_and_reject(384, "manifest-verify", 4,
            lambda x: x["conditions"].update(traceEvents=True),
            "wrong workload")

    def test_cross_case_corpus_identity(self):
        self.mutate_and_reject(1, "enumerate", 4,
            lambda x: x["fixture"].update(manifestSha256="d"*64),
            "corpus differs")


if __name__ == "__main__":
    unittest.main()
