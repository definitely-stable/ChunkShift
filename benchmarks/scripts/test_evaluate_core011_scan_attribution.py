"""Validate strict factorial and no-verdict rules for CORE011-SCAN-001."""

import copy
import importlib.util
from pathlib import Path
import unittest

PATH = Path(__file__).with_name("evaluate_core011_scan_attribution.py")
SPEC = importlib.util.spec_from_file_location("evaluate_core011_scan_attribution", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def fixture():
    samples = []
    for mode in MODULE.MODES:
        for suite in MODULE.SUITES:
            for callback in MODULE.CALLBACKS:
                samples.append({
                    "mode": mode,
                    "hashSuite": suite,
                    "callback": callback,
                    "round": 1,
                    "wallMilliseconds": 5.0,
                    "processCpuMilliseconds": 4.0,
                    "processAllocatedBytes": 0,
                    "workingSetBytes": 8192,
                    "readCalls": 50 if mode == "fragmented-memory" else 5,
                    "readBytes": 1024,
                    "eofReads": 1,
                    "bytesCopied": 200 if mode == "fragmented-memory" else 100,
                    "asyncCompletions": 2 if callback == "yield-once" else 0,
                    "chunkCount": 2,
                    "contentBytes": 1024,
                })
    return {
        "schema": "chunkshift.core011.scan-attribution.v1",
        "experimentId": "CORE011-SCAN-001",
        "runId": "CI",
        "checkoutSha": "synthetic",
        "corpus": {"contentBytes": 1024, "sha256": "a" * 64},
        "conditions": {"rounds": 1},
        "samples": samples,
    }


class AttributionEvaluatorTest(unittest.TestCase):
    def test_complete_matrix_passes_without_verdict(self):
        result = MODULE.evaluate(fixture())
        self.assertEqual(12, result["sampleCount"])
        self.assertEqual("NO_VERDICT_EXPLORATORY", result["decision"])
        self.assertEqual(12, len(result["lanes"]))

    def test_duplicates_fail(self):
        source = fixture()
        source["samples"][1] = copy.deepcopy(source["samples"][0])
        with self.assertRaisesRegex(ValueError, "duplicate"):
            MODULE.evaluate(source)

    def test_invalid_eof_fails(self):
        source = fixture()
        source["samples"][0]["eofReads"] = 0
        with self.assertRaisesRegex(ValueError, "incomplete scan"):
            MODULE.evaluate(source)

    def test_missing_async_suspend_fails(self):
        source = fixture()
        sample = next(x for x in source["samples"] if x["callback"] == "yield-once")
        sample["asyncCompletions"] = 0
        with self.assertRaisesRegex(ValueError, "async handler"):
            MODULE.evaluate(source)

    def test_inconsistent_compaction_fails(self):
        source = fixture()
        source["samples"][0]["bytesCopied"] += 1
        with self.assertRaisesRegex(ValueError, "inconsistent bytesCopied"):
            MODULE.evaluate(source)

    def test_negative_wall_and_nonfinite_fail(self):
        for value in (-1, float("nan"), float("inf")):
            with self.subTest(value=value):
                source = fixture()
                source["samples"][0]["wallMilliseconds"] = value
                with self.assertRaisesRegex(ValueError, "invalid wallMilliseconds"):
                    MODULE.evaluate(source)


if __name__ == "__main__":
    unittest.main()
