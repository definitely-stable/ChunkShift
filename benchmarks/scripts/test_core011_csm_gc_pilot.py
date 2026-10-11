"""Fail-closed schema/matrix tests for the G1 CSM GC pilot."""

import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

PATH = Path(__file__).with_name("core011_csm_gc_pilot.py")
SPEC = importlib.util.spec_from_file_location("core011_csm_gc_pilot", PATH)
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)


def sample(round_number, workers):
    return dict(
        round=round_number,
        wallMilliseconds=2.0,
        processCpuMilliseconds=2.0,
        processAllocatedBytes=2048,
        workingSetAfterBytes=1048576,
        peakProcessWorkingSetBytes=2097152,
        privateMemoryAfterBytes=4194304,
        gen0Collections=0,
        gen1Collections=0,
        gen2Collections=0,
        lohLastGcSizeAfterBytes=262144,
        lohLastGcIndex=1,
        heapLastGcSizeBytes=524288,
        maxActiveOperations=1,
        completedOperations=workers * 2,
        validatedChunks=workers * 2,
    )


def fixture(folder: Path):
    for i, (suite, bidx, workers, operation) in enumerate(MOD.CASES):
        record = {
            "schema": MOD.SCHEMA,
            "experimentId": "CORE011-CSM-001",
            "runId": "CI",
            "checkoutSha": "exact-head",
            "corpus": {
                "contentMiB": 8, "profileId": "fastcdc.gear.chunkshift.v1.64k",
                "contentSha256": "a" * 64,
                "manifestSha256": (("b" if bidx else "c") * 64),
                "manifestBytes": 1024,
            },
            "conditions": {
                "hashSuite": suite, "includeBlockIndex": bidx,
                "workers": workers, "operation": operation,
                "rounds": 2, "repetitions": 2,
            },
            "samples": [sample(1, workers), sample(2, workers)],
        }
        (folder / f"scenario-{i:02d}.json").write_text(json.dumps(record))


class CsmGcPilotValidationTests(unittest.TestCase):
    def test_complete_matrix_no_adoption(self):
        with tempfile.TemporaryDirectory() as path:
            fixture(Path(path))
            result = MOD.evaluate(Path(path), "CI", 2, 2, 8)
            self.assertEqual(24, result["sampleCount"])
            self.assertEqual("NO_VERDICT_EXPLORATORY", result["decision"])

    def test_missing_scenario_rejected(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            fixture(root)
            next(root.glob("scenario-*.json")).unlink()
            with self.assertRaisesRegex(ValueError, "expected 12"):
                MOD.evaluate(root, "CI", 2, 2, 8)

    def test_incomplete_worker_batch_rejected(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            fixture(root)
            target = next(root.glob("scenario-*.json"))
            data = json.loads(target.read_text())
            data["samples"][0]["completedOperations"] = 0
            target.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "incomplete worker"):
                MOD.evaluate(root, "CI", 2, 2, 8)

    def test_unfrozen_manifest_rejected(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            fixture(root)
            target = root / "scenario-01.json"
            data = json.loads(target.read_text())
            data["corpus"]["manifestSha256"] = "f" * 64
            target.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "CSM fixture"):
                MOD.evaluate(root, "CI", 2, 2, 8)


if __name__ == "__main__":
    unittest.main()
