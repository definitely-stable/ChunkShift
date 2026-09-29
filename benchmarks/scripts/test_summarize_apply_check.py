#!/usr/bin/env python3
"""Unit tests for summarize_apply_check.py with small hand-made evidence files.

Run with:
  python -m unittest benchmarks/scripts/test_summarize_apply_check.py
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

import summarize_apply_check as summary  # noqa: E402

PAIRS_SHA256 = "0" * 64
COMMIT = "0123456789abcdef0123456789abcdef01234567"
MIB = 1024 * 1024
RESAMPLES = 200

ENVIRONMENTS = {
    "linux-x64": {"osDescription": "Ubuntu 24.04.5 LTS", "osArchitecture": "X64", "gitCommit": COMMIT},
    "linux-arm64": {"osDescription": "Ubuntu 24.04.5 LTS", "osArchitecture": "Arm64", "gitCommit": COMMIT},
    "win-x64": {"osDescription": "Microsoft Windows 10.0.26100", "osArchitecture": "X64", "gitCommit": COMMIT},
}

FILES = [(f"file-{index}", (index + 1) * 512 * 1024) for index in range(8)]


def cost(wall: float, cpu: float) -> dict:
    return {"wallSeconds": wall, "cpuSeconds": cpu}


class Fixture:
    """A corpus lock and platform directories written into a temporary folder."""

    def __init__(self, case: unittest.TestCase):
        self._temporary = tempfile.TemporaryDirectory()
        case.addCleanup(self._temporary.cleanup)
        self.root = Path(self._temporary.name)
        self.lock = self.root / "corpus-lock.json"
        self.lock.write_text(json.dumps({"pairsSha256": PAIRS_SHA256}), encoding="utf-8")

    def platform(
        self,
        name: str,
        overlap_factor: float = 1.05,
        seq_factor: float = 1.30,
        concurrent_cpu_factor: float = 1.10,
        repetitions: int = 10,
        memory_excess: int = 40 * MIB,
        environment: dict | None = None,
    ) -> Path:
        directory = self.root / name
        directory.mkdir()
        env = environment or ENVIRONMENTS[name]
        for repetition in range(repetitions):
            files = []
            for index, (path, size) in enumerate(FILES):
                off = 0.01 + index * 0.01 + repetition * 0.0001
                cpu = off * 0.9
                files.append({
                    "family": "fam", "base": "1", "target": "2", "path": path, "targetSize": size,
                    "targetChunks": 4,
                    "runs": {
                        "off": [cost(off, cpu), cost(off, cpu)],
                        "seq": [cost(off * seq_factor, cpu * seq_factor)] * 2,
                        "overlap": [cost(off * overlap_factor, cpu * 1.2)] * 2,
                    },
                    "decomposition": {
                        "csmParse": cost(0.001, 0.001), "reread": cost(0.002, 0.001),
                        "boundary": cost(0.003, 0.003), "hash": cost(0.004, 0.004),
                        "manifestId": cost(0.0005, 0.0005), "check": cost(0.011, 0.010),
                    },
                })
            self._write(directory / f"time-{repetition}.json", {
                "kind": "time", "repetition": repetition, "outputsVerified": True, "environment": env,
                "runId": f"PATCH-APPLY-002/RUN-20260928-001-0123456-{name}", "files": files,
            })
            passes = []
            for level in (1, 2, 4, 8):
                for lane, factor in (("off", 1.0), ("seq", 1.3), ("overlap", concurrent_cpu_factor)):
                    for number in range(2):
                        passes.append({"concurrency": level, "lane": lane, "pass": number,
                                       "wallSeconds": 10.0 / level * factor, "cpuSeconds": 10.0 * factor})
            self._write(directory / f"concurrent-{repetition}.json", {
                "kind": "concurrent", "repetition": repetition, "environment": env, "passes": passes,
            })
        self._write(directory / "memory.json", {
            "kind": "memory", "environment": env, "idleBaselineBytes": 30 * MIB, "memoryEnvironment": {},
            "files": [{"family": "fam", "path": "big", "targetSize": 4 * MIB,
                       "applyPeakBytes": {"off": 60 * MIB, "seq": 62 * MIB, "overlap": 30 * MIB + memory_excess}}],
        })
        self._write(directory / "prepare.json", {
            "kind": "prepare", "environment": env,
            "files": [{"family": "fam", "path": "a", "targetSize": 10000, "sameOffsetBytes": 5000,
                       "alignedBytes": 4096}],
        })
        return directory

    @staticmethod
    def _write(path: Path, document: dict) -> None:
        document = {"schema": summary.SCHEMA, "corpusPairsSha256": PAIRS_SHA256, **document}
        path.write_text(json.dumps(document), encoding="utf-8")

    def summarize(self, *directories: Path) -> dict:
        return summary.summarize(list(directories), self.lock, resamples=RESAMPLES)


class RuleTests(unittest.TestCase):
    def test_small_overlap_overhead_on_both_linux_lanes_adopts_a2(self):
        fixture = Fixture(self)
        document = fixture.summarize(fixture.platform("linux-x64"), fixture.platform("linux-arm64"))

        self.assertEqual(document["rules"]["rule1"]["verdict"], "ADOPT")
        self.assertEqual(document["rules"]["rule2"]["verdict"], "A1a not required")
        self.assertEqual(document["rules"]["rule3"]["verdict"], "option 3 stands (close #168)")
        stat = document["platforms"][0]["timeStatistics"]["lanes"]["overlap"]["wallMedianOfRatios"]
        self.assertAlmostEqual(stat["median"], 0.05, places=6)
        self.assertLessEqual(stat["ciLow"], stat["median"])
        self.assertGreaterEqual(stat["ciHigh"], stat["median"])

    def test_overhead_above_the_bound_on_two_platforms_rejects_a2(self):
        fixture = Fixture(self)
        document = fixture.summarize(
            fixture.platform("linux-x64", overlap_factor=1.2),
            fixture.platform("linux-arm64", overlap_factor=1.2),
            fixture.platform("win-x64"),
        )

        self.assertEqual(document["rules"]["rule1"]["verdict"], "REJECT")
        self.assertEqual(document["rules"]["rule3"]["verdict"], "option 1 reopens")

    def test_split_platforms_need_the_third(self):
        fixture = Fixture(self)
        split = fixture.summarize(
            fixture.platform("linux-x64"),
            fixture.platform("linux-arm64", overlap_factor=1.2),
        )
        self.assertEqual(split["rules"]["rule1"]["verdict"], summary.NOT_EVALUATED)

    def test_memory_over_the_bound_defers(self):
        fixture = Fixture(self)
        document = fixture.summarize(
            fixture.platform("linux-x64", memory_excess=70 * MIB),
            fixture.platform("linux-arm64"),
        )
        self.assertEqual(document["rules"]["rule1"]["verdict"], "DEFER")

    def test_cpu_overhead_under_concurrency_requires_a1a(self):
        fixture = Fixture(self)
        document = fixture.summarize(
            fixture.platform("linux-x64", concurrent_cpu_factor=1.3),
            fixture.platform("linux-arm64", concurrent_cpu_factor=1.3),
        )
        self.assertEqual(document["rules"]["rule1"]["verdict"], "ADOPT")
        self.assertEqual(document["rules"]["rule2"]["verdict"], "A1a required")
        self.assertEqual(document["rules"]["rule3"]["verdict"], "pending A1a")

    def test_incomplete_repetitions_are_not_evaluated(self):
        fixture = Fixture(self)
        document = fixture.summarize(
            fixture.platform("linux-x64", repetitions=9),
            fixture.platform("linux-arm64"),
        )
        self.assertEqual(document["rules"]["rule1"]["verdict"], summary.NOT_EVALUATED)
        self.assertIsNone(document["rules"]["rule1"]["platforms"]["linux-x64"]["wallBoundMet"])


class StatisticTests(unittest.TestCase):
    def test_decomposition_and_alignment_are_reported(self):
        fixture = Fixture(self)
        document = fixture.summarize(fixture.platform("linux-x64"))
        platform = document["platforms"][0]
        components = platform["timeStatistics"]["decomposition"]

        self.assertAlmostEqual(components["check"]["wall"], 0.011 * len(FILES))
        self.assertAlmostEqual(components["residual"]["wall"], 0.0005 * len(FILES))
        self.assertAlmostEqual(components["hash"]["shareOfCheckWall"], 4 / 11)
        self.assertEqual(platform["alignment"]["corpus"],
                         {"targetBytes": 10000, "sameOffsetBytes": 5000, "alignedBytes": 4096})

    def test_cpu_ratios_skip_files_below_the_clock_floor(self):
        fixture = Fixture(self)
        document = fixture.summarize(fixture.platform("linux-x64"))
        cpu = document["platforms"][0]["timeStatistics"]["lanes"]["overlap"]["cpuMedianOfRatios"]
        # off CPU is 0.9 x (0.01 + index x 0.01): files 0 and 1 are below 20 ms.
        self.assertEqual(cpu["count"], len(FILES) - 2)

    def test_bootstrap_is_reproducible(self):
        fixture = Fixture(self)
        directory = fixture.platform("linux-x64")
        first = fixture.summarize(directory)
        second = fixture.summarize(directory)
        self.assertEqual(first["platforms"][0]["timeStatistics"], second["platforms"][0]["timeStatistics"])


class InputTests(unittest.TestCase):
    def test_corpus_lock_mismatch_is_an_error(self):
        fixture = Fixture(self)
        directory = fixture.platform("linux-x64")
        fixture.lock.write_text(json.dumps({"pairsSha256": "f" * 64}), encoding="utf-8")
        with self.assertRaises(summary.SummaryError):
            fixture.summarize(directory)

    def test_mixed_commits_are_an_error(self):
        fixture = Fixture(self)
        directory = fixture.platform("linux-x64")
        other = dict(ENVIRONMENTS["linux-x64"], gitCommit="f" * 40)
        Fixture._write(directory / "extra.json", {"kind": "memory", "environment": other, "files": []})
        with self.assertRaises(summary.SummaryError):
            fixture.summarize(directory)

    def test_main_writes_json_and_markdown(self):
        fixture = Fixture(self)
        directory = fixture.platform("linux-x64")
        output = fixture.root / "verdict.json"
        markdown = fixture.root / "summary.md"
        with contextlib.redirect_stderr(io.StringIO()):
            code = summary.main([str(directory), "--corpus-lock", str(fixture.lock),
                                 "--output", str(output), "--markdown", str(markdown)])

        self.assertEqual(code, 0)
        self.assertEqual(json.loads(output.read_text(encoding="utf-8"))["experiment"], "PATCH-APPLY-002")
        text = markdown.read_text(encoding="utf-8")
        self.assertIn("## linux-x64", text)
        self.assertIn("| overlap |", text)


if __name__ == "__main__":
    unittest.main()
