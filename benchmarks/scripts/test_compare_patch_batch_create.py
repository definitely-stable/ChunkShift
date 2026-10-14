"""Adversarial validator checks: failures are never silently speedups."""

import json
import tempfile
import unittest
from pathlib import Path

import compare_patch_batch_create as verifier


class BatchComparisonTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.row = {
            "family": "f", "base": "a", "target": "b", "path": "x",
            "targetSize": 25, "targetChunks": 1, "patchBytes": 20,
            "patchSha256": "a" * 64, "tcsmBytes": 4,
            "uniqueMissingBytes": 5, "payloadEntries": 1, "rawEntries": 1,
            "zstdEntries": 0, "dictionaryEntries": 0,
        }
        self.env = {k: 1 for k in verifier.HOST_FIELDS}
        self.env["gitCommit"] = "a" * 40
        self.oracle = self.write("oracle", {
            "schema": verifier.LEGACY, "files": [dict(self.row, applyMetrics={})],
            "applyRepeats": 1, "workers": 1, "execution": "h0",
            "elapsedSeconds": 5, "corpusPairsSha256": "e" * 64,
            "lane": "csp", "policy": {"level": 19}, "environment": self.env})
        batch = {
            "schema": verifier.BATCH, "files": [dict(self.row, createMetrics=None)],
            "applyRepeats": 0, "workers": 1, "execution": "h0",
            "elapsedSeconds": 10, "corpusPairsSha256": "e" * 64,
            "lane": "csp", "policy": {"level": 19}, "environment": self.env,
            "batchCreate": {
                "workload": "manifest-digest-plan-create-readback-sha256",
                "processCpuSeconds": 6, "processAllocatedBytes": 1024,
                "processPeakWorkingSetBytes": 100000,
            }}
        self.baseline = self.write("baseline", batch)
        faster = json.loads(json.dumps(batch))
        faster["workers"] = 2
        faster["execution"] = "h2-w2"
        faster["elapsedSeconds"] = 5
        self.candidate = self.write("candidate", faster)

    def write(self, name, data):
        p = self.root / f"{name}.json"
        p.write_text(json.dumps(data), encoding="utf-8")
        return p

    def test_equal(self):
        report = verifier.compare(self.oracle, self.baseline, [self.candidate])
        self.assertEqual(report["fileCount"], 1)
        self.assertEqual(report["candidates"][0]["relativeSpeedup"], 2)

    def test_reject_byte_drift(self):
        data = json.loads(self.candidate.read_text())
        data["files"][0]["patchSha256"] = "b" * 64
        self.write("candidate", data)
        with self.assertRaisesRegex(ValueError, "patchSha256"):
            verifier.compare(self.oracle, self.baseline, [self.candidate])

    def test_reject_missing_file(self):
        data = json.loads(self.candidate.read_text())
        data["files"].append(dict(data["files"][0], path="extra"))
        self.write("candidate", data)
        with self.assertRaisesRegex(ValueError, "missing or extra"):
            verifier.compare(self.oracle, self.baseline, [self.candidate])

    def test_reject_fake_per_file_metrics(self):
        data = json.loads(self.candidate.read_text())
        data["files"][0]["createMetrics"] = {"cpuSeconds": 1}
        self.write("candidate", data)
        with self.assertRaisesRegex(ValueError, "falsely attributed"):
            verifier.compare(self.oracle, self.baseline, [self.candidate])

    def test_reject_different_corpus_and_host(self):
        for key, value in (("corpusPairsSha256", "f" * 64), ("environment", {"osDescription": "different"})):
            data = json.loads(self.candidate.read_text())
            data[key] = value
            self.write("candidate", data)
            with self.assertRaises(ValueError):
                verifier.compare(self.oracle, self.baseline, [self.candidate])
            # Restore original for the next mutation.
            data[key] = "e" * 64 if key == "corpusPairsSha256" else self.env
            self.write("candidate", data)

    def test_reject_wrong_baseline_and_unbound_build(self):
        data = json.loads(self.baseline.read_text())
        data["execution"] = "h2-w2"
        self.write("baseline", data)
        with self.assertRaisesRegex(ValueError, "baseline must be H0"):
            verifier.compare(self.oracle, self.baseline, [self.candidate])
        data["execution"] = "h0"
        data["environment"]["gitCommit"] = None
        self.write("baseline", data)
        with self.assertRaisesRegex(ValueError, "40-hex"):
            verifier.compare(self.oracle, self.baseline, [self.candidate])

    def test_reject_oracle_build_drift(self):
        data = json.loads(self.oracle.read_text())
        data["environment"]["gitCommit"] = "b" * 40
        self.write("oracle", data)
        with self.assertRaisesRegex(ValueError, "gitCommit"):
            verifier.compare(self.oracle, self.baseline, [self.candidate])

    def test_reject_unverified_oracle(self):
        data = json.loads(self.oracle.read_text())
        data["files"][0]["applyMetrics"] = None
        self.write("oracle", data)
        with self.assertRaisesRegex(ValueError, "apply-verified"):
            verifier.compare(self.oracle, self.baseline, [self.candidate])


if __name__ == "__main__":
    unittest.main()
