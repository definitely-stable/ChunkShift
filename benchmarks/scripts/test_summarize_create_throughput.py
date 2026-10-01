#!/usr/bin/env python3
"""Unit tests for summarize_create_throughput.py with small hand-made run files.

Run with:
  python3 -m unittest benchmarks/scripts/test_summarize_create_throughput.py
"""

from __future__ import annotations

import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import summarize_create_throughput as summary  # noqa: E402

PAIRS_SHA256 = "0" * 64
COMMIT = "0123456789abcdef0123456789abcdef01234567"
MIB = 1024 * 1024
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
EXECUTIONS = summary.EXECUTIONS

# Create seconds per file (two files per document) by execution.
SECONDS = {
    "h0": 10.0, "h1": 8.0,
    "h2-w1": 10.0, "h2-w2": 6.25, "h2-w4": 5.0, "h2-w8": 5.0,
    "h3-w1": 10.0, "h3-w2": 6.25, "h3-w4": 5.0, "h3-w8": 5.0,
}
FILES = (("fam", "a", "b", "x.bin"), ("fam", "a", "c", "y.bin"))


def throughput(platform: str, execution: str, seconds: float, reads: int,
               started: str, sha_salt: str = "") -> dict:
    files = []
    for index, (family, base, target, path) in enumerate(FILES):
        files.append({
            "family": family, "base": base, "target": target, "path": path,
            "targetSize": 2 * MIB, "uniqueMissingBytes": MIB, "patchBytes": 100,
            "createSeconds": seconds, "applySeconds": None,
            "patchSha256": f"{index:02x}" * 32 if not sha_salt or index else sha_salt * 64,
            "createMetrics": {
                "cpuSeconds": seconds * 2, "allocatedBytes": 1000, "baseReads": 1,
                "baseBytesRead": reads, "baseSeeks": 3, "cacheLoads": 1, "cacheHits": 1,
                "cachePeakRecords": 4 + index, "cachePeakBytes": 100,
                "windowPeakEntries": 2, "windowPeakBytes": 50,
                "reorderPeakEntries": 3 + index, "reorderPeakBytes": 60,
            },
        })
    return {
        "schema": "chunkshift.patch-lab.v1",
        "runId": f"PATCH-ENC-004/RUN-20261001-42-abc1234-{platform}",
        "lane": "csp", "execution": execution, "workers": 1,
        "corpusPairsSha256": PAIRS_SHA256,
        "environment": {"osDescription": "x", "osArchitecture": "X64", "processArchitecture": "X64",
                        "frameworkDescription": ".NET 10", "processorCount": 4,
                        "gitCommit": COMMIT, "processorDescription": "cpu"},
        "startedUtc": started, "elapsedSeconds": 1.0, "files": files,
    }


def memory(platform: str, execution: str, peak_mib: float, commit: str = COMMIT,
           run_id: str | None = None, files=FILES) -> dict:
    return {
        "schema": "chunkshift.patch-lab-memory.v1",
        "runId": run_id or f"PATCH-ENC-004/RUN-20261001-42-abc1234-{platform}",
        "lane": "csp", "execution": execution, "corpusPairsSha256": PAIRS_SHA256,
        "environment": {"gitCommit": commit},
        "idleBaselineBytes": 100 * MIB,
        "files": [{"family": family, "base": base, "target": target, "path": path, "targetSize": 2 * MIB,
                   "createPeakBytes": int((100 + (peak_mib if index == 0 else 1.0)) * MIB),
                   "applyPeakBytes": 0}
                  for index, (family, base, target, path) in enumerate(files)],
    }


class Fixture:
    def __init__(self, case: unittest.TestCase):
        self._temporary = tempfile.TemporaryDirectory()
        case.addCleanup(self._temporary.cleanup)
        self.root = Path(self._temporary.name)
        (self.root / "corpus-lock.json").write_text(json.dumps({"pairsSha256": PAIRS_SHA256}), encoding="utf-8")
        self.runs = self.root / "runs"
        self.runs.mkdir()
        self.log = io.StringIO()

    def write(self, name: str, document: dict) -> Path:
        path = self.runs / name
        path.write_text(json.dumps(document), encoding="utf-8")
        return path

    def build(self, platforms=PLATFORMS, seconds=None, reads=None, h0_last=10.0,
              mismatch=None, with_memory=True, peaks=None, memory_overrides=None):
        """seconds/reads/peaks: {(platform, execution) or execution: value} overrides."""
        def pick(table, platform, execution, default):
            table = table or {}
            return table.get((platform, execution), table.get(execution, default))

        for platform in platforms:
            for position, execution in enumerate(EXECUTIONS + ("h0",)):
                last = position == len(EXECUTIONS)
                base = pick(seconds, platform, execution, SECONDS[execution])
                if last:
                    base = h0_last
                default_reads = 400 if execution == "h1" else 1000
                document = throughput(
                    platform, execution, base, pick(reads, platform, execution, default_reads),
                    "2026-10-01T01:00:00Z" if last else "2026-10-01T00:00:00Z",
                    sha_salt="e" if (platform, execution) == mismatch else "")
                self.write(f"create-throughput-{position:02d}-{execution}-{platform}.json", document)
            if with_memory:
                for execution in EXECUTIONS:
                    peak = pick(peaks, platform, execution, 30.0)
                    extra = (memory_overrides or {}).get((platform, execution), {})
                    self.write(f"create-memory-{execution}-{platform}.json",
                               memory(platform, execution, peak, **extra))

    def decide(self, *extra: str):
        output, markdown = self.root / "verdict.json", self.root / "summary.md"
        with contextlib.redirect_stdout(self.log), contextlib.redirect_stderr(self.log):
            code = summary.main(["decide", str(self.runs), "--corpus-lock", str(self.root / "corpus-lock.json"),
                                 "--output", str(output), "--markdown", str(markdown), *extra])
        return code, json.loads(output.read_text(encoding="utf-8"))


def fast_w2(platform_speeds=("linux-x64", "linux-arm64")) -> dict:
    """h3-w2 and h2-w2 are 1.6x on two platforms, 1.0x on the third."""
    table = {}
    for platform in PLATFORMS:
        if platform not in platform_speeds:
            table[(platform, "h3-w2")] = 10.0
            table[(platform, "h2-w2")] = 10.0
    return table


class SummarizeCreateThroughputTests(unittest.TestCase):
    def test_h1_and_h3_w2_adopted(self):
        fixture = Fixture(self)
        fixture.build(seconds=fast_w2())
        code, verdict = fixture.decide("--require-all-platforms")
        self.assertEqual(0, code, fixture.log.getvalue())
        self.assertTrue(verdict["valid"])
        self.assertEqual(COMMIT, verdict["commit"])
        self.assertEqual("ADOPT", verdict["h1"]["decision"])
        self.assertAlmostEqual(2.5, verdict["h1"]["readRatio"])
        self.assertEqual("h3", verdict["workers"]["family"])
        self.assertEqual("ADOPT", verdict["workers"]["decision"])
        self.assertEqual(2, verdict["workers"]["adoptedWorkers"])
        # h2-w2 qualifies too; at equal W, H3 comes first.
        self.assertEqual("h3-w2", verdict["workers"]["adoptedExecution"])
        self.assertTrue(verdict["workers"]["families"]["h2"]["table"][1]["qualifies"])
        self.assertAlmostEqual(1.6, verdict["platforms"]["linux-x64"]["h3-w2"]["speedup"])
        self.assertAlmostEqual(1.0, verdict["platforms"]["win-x64"]["h3-w1"]["speedup"])
        self.assertTrue(verdict["h0DigestsEqualAcrossPlatforms"])
        self.assertIn("| h3-w2 |", (fixture.root / "summary.md").read_text(encoding="utf-8"))

    def test_bytes_mismatch_rejects_lane_and_next_worker_count_adopted(self):
        fixture = Fixture(self)
        seconds = {"h3-w1": 5.0, "h3-w2": 5.0}
        fixture.build(seconds=seconds, mismatch=("win-x64", "h3-w1"))
        code, verdict = fixture.decide()
        self.assertEqual(0, code)
        self.assertFalse(verdict["bytes"]["h3-w1"]["win-x64"]["identical"])
        self.assertEqual(1, len(verdict["bytes"]["h3-w1"]["win-x64"]["mismatches"]))
        self.assertTrue(verdict["bytes"]["h3-w1"]["linux-x64"]["identical"])
        first = verdict["workers"]["families"]["h3"]["table"][0]
        self.assertEqual("REJECT (bytes)", first["status"])
        self.assertFalse(first["qualifies"])
        self.assertEqual("ADOPT", verdict["workers"]["decision"])
        self.assertEqual("h3-w2", verdict["workers"]["adoptedExecution"])

    def test_h1_read_ratio_below_two_rejects_and_uses_h2(self):
        fixture = Fixture(self)
        fixture.build(reads={"h1": 1000 * 2 // 3}, seconds=fast_w2())
        code, verdict = fixture.decide()
        self.assertEqual(0, code)
        self.assertEqual("REJECT", verdict["h1"]["decision"])
        self.assertAlmostEqual(1.5, verdict["h1"]["readRatio"], places=2)
        self.assertEqual("h2-w2", verdict["workers"]["adoptedExecution"])
        self.assertFalse(verdict["workers"]["h3Eligible"])
        self.assertEqual("not eligible (H1 not adopted)",
                         verdict["workers"]["families"]["h3"]["table"][1]["status"])

    def test_h1_regression_rejects(self):
        fixture = Fixture(self)
        fixture.build(seconds={("win-x64", "h1"): 12.0})
        _, verdict = fixture.decide()
        self.assertEqual("REJECT", verdict["h1"]["decision"])
        self.assertFalse(verdict["h1"]["noRegression"]["win-x64"])

    def test_h1_slower_than_the_h0_mean_is_a_regression(self):
        fixture = Fixture(self)
        # h0 lanes 10 s and 11 s per file: mean 10.5 s, H1 at 10.8 s is below
        # the slower lane but above the mean.
        fixture.build(seconds=fast_w2() | {("linux-x64", "h1"): 10.8}, h0_last=11.0)
        _, verdict = fixture.decide()
        self.assertEqual("REJECT", verdict["h1"]["decision"])
        self.assertFalse(verdict["h1"]["noRegression"]["linux-x64"])
        self.assertTrue(verdict["h1"]["noRegression"]["win-x64"])

    def test_h1_over_its_memory_bound_rejects(self):
        fixture = Fixture(self)
        fixture.build(seconds=fast_w2(), peaks={("win-x64", "h1"): 65.0})
        _, verdict = fixture.decide()
        self.assertEqual("REJECT", verdict["h1"]["decision"])
        self.assertFalse(verdict["h1"]["memoryMet"]["win-x64"])
        self.assertEqual("h2-w2", verdict["workers"]["adoptedExecution"])

    def test_h1_without_memory_evidence_is_incomplete(self):
        fixture = Fixture(self)
        fixture.build(seconds=fast_w2(), with_memory=False)
        _, verdict = fixture.decide()
        self.assertEqual("INCOMPLETE", verdict["h1"]["decision"])
        self.assertFalse(verdict["workers"]["h3Eligible"])

    def test_h0_lanes_differing_by_thirty_percent_is_invalid(self):
        fixture = Fixture(self)
        fixture.build(h0_last=13.0)
        code, verdict = fixture.decide()
        self.assertEqual(1, code)
        self.assertFalse(verdict["valid"])
        self.assertTrue(any("25 %" in reason for reason in verdict["invalidReasons"]))
        self.assertIsNone(verdict["workers"])

    def test_memory_over_bound_disqualifies_lane(self):
        fixture = Fixture(self)
        fixture.build(seconds={"h3-w2": 5.0, "h3-w4": 5.0, "h3-w8": 5.0},
                      peaks={("linux-arm64", "h3-w2"): 100.0})
        _, verdict = fixture.decide()
        rows = {row["execution"]: row for row in verdict["workers"]["families"]["h3"]["table"]}
        self.assertFalse(rows["h3-w2"]["memoryMet"]["linux-arm64"])
        self.assertFalse(rows["h3-w2"]["qualifies"])
        self.assertTrue(rows["h3-w4"]["qualifies"])
        # H1 adopted and h3-w2 over its bound: the qualifying h2-w2 is not
        # discarded, and W = 2 beats h3-w4.
        self.assertEqual("ADOPT", verdict["h1"]["decision"])
        self.assertEqual("h2-w2", verdict["workers"]["adoptedExecution"])
        self.assertEqual(96, verdict["platforms"]["win-x64"]["h3-w2"]["memoryBoundMiB"])

    def test_memory_not_measured_is_incomplete(self):
        fixture = Fixture(self)
        fixture.build(seconds=fast_w2(), with_memory=False)
        _, verdict = fixture.decide()
        self.assertEqual("INCOMPLETE", verdict["workers"]["decision"])
        self.assertIsNone(verdict["workers"]["adoptedWorkers"])
        self.assertIsNone(verdict["platforms"]["linux-x64"]["h3-w2"]["memoryMet"])

    def test_missing_platform_is_incomplete_or_invalid_when_required(self):
        fixture = Fixture(self)
        fixture.build(platforms=("linux-x64", "linux-arm64"), seconds=fast_w2())
        code, verdict = fixture.decide()
        self.assertEqual(0, code)
        self.assertEqual("INCOMPLETE", verdict["workers"]["decision"])
        code, verdict = fixture.decide("--require-all-platforms")
        self.assertEqual(1, code)
        self.assertTrue(any("win-x64" in reason for reason in verdict["invalidReasons"]))

    def test_wrong_corpus_hash_and_missing_lane_are_invalid(self):
        fixture = Fixture(self)
        fixture.build()
        (fixture.runs / "create-throughput-03-h2-w2-win-x64.json").unlink()
        path = fixture.runs / "create-throughput-00-h0-linux-x64.json"
        document = json.loads(path.read_text(encoding="utf-8"))
        document["corpusPairsSha256"] = "f" * 64
        path.write_text(json.dumps(document), encoding="utf-8")
        code, verdict = fixture.decide()
        self.assertEqual(1, code)
        joined = "\n".join(verdict["invalidReasons"])
        self.assertIn("corpusPairsSha256", joined)
        self.assertIn("win-x64 h2-w2", joined)

    def test_memory_from_another_commit_is_invalid(self):
        fixture = Fixture(self)
        fixture.build(memory_overrides={("win-x64", "h2-w2"): {"commit": "f" * 40}})
        code, verdict = fixture.decide()
        self.assertEqual(1, code)
        self.assertTrue(any("win-x64 h2-w2 memory: gitCommit" in r for r in verdict["invalidReasons"]))

    def test_memory_from_another_dispatch_is_invalid(self):
        fixture = Fixture(self)
        fixture.build(memory_overrides={
            ("linux-arm64", "h1"): {"run_id": "PATCH-ENC-004/RUN-20261001-41-abc1234-linux-arm64"}})
        code, verdict = fixture.decide()
        self.assertEqual(1, code)
        self.assertTrue(any("linux-arm64 h1 memory: runId" in r for r in verdict["invalidReasons"]))

    def test_memory_missing_the_worst_file_is_invalid(self):
        fixture = Fixture(self)
        fixture.build(memory_overrides={("linux-x64", "h3-w4"): {"files": FILES[1:]}})
        code, verdict = fixture.decide()
        self.assertEqual(1, code)
        self.assertTrue(any("linux-x64 h3-w4 memory: missing files" in r for r in verdict["invalidReasons"]))

    def test_decision_grade_needs_every_platform_and_memory_lane(self):
        fixture = Fixture(self)
        fixture.build(seconds=fast_w2())
        code, verdict = fixture.decide()
        self.assertEqual(0, code)
        self.assertFalse(verdict["decisionGrade"])
        self.assertIn("Exploratory", (fixture.root / "summary.md").read_text(encoding="utf-8"))
        code, verdict = fixture.decide("--require-all-platforms", "--require-memory")
        self.assertEqual(0, code)
        self.assertTrue(verdict["decisionGrade"])

    def test_required_memory_missing_is_invalid(self):
        fixture = Fixture(self)
        fixture.build(seconds=fast_w2(), with_memory=False)
        code, verdict = fixture.decide("--require-all-platforms", "--require-memory")
        self.assertEqual(1, code)
        self.assertTrue(any("no memory documents" in r for r in verdict["invalidReasons"]))
        self.assertFalse(verdict["decisionGrade"])

    def test_print_command_output(self):
        fixture = Fixture(self)
        fixture.build(platforms=("linux-arm64",), with_memory=False)
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            code = summary.main(["print", str(fixture.runs / "create-throughput-01-h1-linux-arm64.json")])
        self.assertEqual(0, code)
        text = buffer.getvalue()
        self.assertIn("execution=h1 platform=linux-arm64", text)
        self.assertIn("effectiveCores=2.00", text)
        self.assertIn("createSeconds=16.00", text)
        self.assertIn("baseBytesRead=800", text)
        self.assertIn("reorderPeakEntries=4", text)
        self.assertRegex(text, r"patchesSha256=[0-9a-f]{64}")


if __name__ == "__main__":
    unittest.main()
