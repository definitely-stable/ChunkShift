#!/usr/bin/env python3
"""Unit tests for summarize_patch_lab.py with small hand-made evidence files.

Run with:
  python -m unittest benchmarks/scripts/test_summarize_patch_lab.py
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

import print_patch_lab_encoder as encoder_print  # noqa: E402
import print_patch_lab_memory as memory_print  # noqa: E402
import summarize_patch_lab as summary  # noqa: E402

PAIRS_SHA256 = "0" * 64
OTHER_SHA256 = "f" * 64
COMMIT = "0123456789abcdef0123456789abcdef01234567"
CALIBRATION = "cal-family"
HOLDOUT = "hold-family"
MIB = 1024 * 1024

LINUX_X64 = {
    "osDescription": "Ubuntu 24.04.5 LTS",
    "osArchitecture": "X64",
    "processorCount": 8,
    "gitCommit": COMMIT,
}
LINUX_ARM64 = {
    "osDescription": "Ubuntu 24.04.5 LTS",
    "osArchitecture": "Arm64",
    "processorCount": 8,
    "gitCommit": COMMIT,
}


class Fixture:
    """A temporary corpus lock/manifest pair plus platform directories."""

    def __init__(self, case: unittest.TestCase):
        self._temporary = tempfile.TemporaryDirectory()
        case.addCleanup(self._temporary.cleanup)
        self.root = Path(self._temporary.name)
        (self.root / "corpus-lock.json").write_text(
            json.dumps({"pairsSha256": PAIRS_SHA256}), encoding="utf-8"
        )
        manifest = {
            "schema": "chunkshift.patch-corpus.v1",
            "families": [
                {"id": CALIBRATION, "split": "calibration"},
                {"id": HOLDOUT, "split": "holdout"},
            ],
        }
        (self.root / "patch-corpus.json").write_text(json.dumps(manifest), encoding="utf-8")
        self.log = io.StringIO()

    def platform(self, name: str) -> Path:
        directory = self.root / name
        directory.mkdir(parents=True, exist_ok=True)
        return directory

    def run(self, *directories: Path):
        output = self.root / "verdict.json"
        markdown = self.root / "summary.md"
        with contextlib.redirect_stdout(self.log), contextlib.redirect_stderr(self.log):
            code = summary.main(
                [
                    *[str(directory) for directory in directories],
                    "--corpus-lock",
                    str(self.root / "corpus-lock.json"),
                    "--output",
                    str(output),
                    "--markdown",
                    str(markdown),
                ]
            )
        document = json.loads(output.read_text(encoding="utf-8")) if output.is_file() else None
        text = markdown.read_text(encoding="utf-8") if markdown.is_file() else None
        return code, document, text


def lab_record(
    family,
    path,
    *,
    target_size=2 * MIB,
    patch=0,
    tcsm=0,
    unique=0,
    stored=0,
    entries=0,
    raw=0,
    zstd_entries=0,
    dictionary=0,
    dict_refs=0,
    target_chunks=0,
    create=0.0,
    apply=0.0,
    check_off=0.0,
):
    return {
        "family": family,
        "base": "1.0",
        "target": "1.1",
        "path": path,
        "targetSize": target_size,
        "uniqueMissingBytes": unique,
        "patchBytes": patch,
        "tcsmBytes": tcsm,
        "payloadEntries": entries,
        "rawEntries": raw,
        "zstdEntries": zstd_entries,
        "dictionaryEntries": dictionary,
        "storedPayloadBytes": stored,
        "dictionaryReferences": dict_refs,
        "targetChunks": target_chunks,
        "createSeconds": create,
        "applySeconds": apply,
        "applyNoCheckSeconds": check_off,
    }


def write_lane(
    directory: Path,
    lane: str,
    records: list[dict],
    *,
    environment=LINUX_X64,
    run_id="PATCH-PREFREEZE-001/RUN-20260928-1-0123456-linux-x64",
    pairs_sha256=PAIRS_SHA256,
) -> None:
    document = {
        "schema": summary.LAB_SCHEMA,
        "runId": run_id,
        "lane": lane,
        "policy": {},
        "workers": 1,
        "applyRepeats": 3,
        "corpusPairsSha256": pairs_sha256,
        "environment": dict(environment),
        "startedUtc": "2026-09-28T00:00:00Z",
        "elapsedSeconds": 1.0,
        "files": records,
    }
    (directory / f"patch-lab-{lane}.json").write_text(json.dumps(document), encoding="utf-8")


def sweep_records(calibration_bytes: int, create_seconds: float, holdout_bytes: int) -> list[dict]:
    return [
        lab_record(CALIBRATION, "a.dll", patch=calibration_bytes, create=create_seconds),
        lab_record(HOLDOUT, "b.js", patch=holdout_bytes, create=create_seconds / 2),
    ]


def write_sweep(directory: Path, overrides: dict[str, tuple] | None = None, *, environment=LINUX_X64) -> None:
    overrides = overrides or {}
    for lane in summary.SWEEP_LANES:
        calibration_bytes, create_seconds, holdout_bytes = overrides.get(lane, (1000, 100.0, 500))
        write_lane(
            directory,
            lane,
            sweep_records(calibration_bytes, create_seconds, holdout_bytes),
            environment=environment,
            run_id="PATCH-ENC-002/RUN-20260928-1-0123456-linux-x64",
        )


def memory_record(family, path, *, create_peak, apply_peak, target_size=2 * MIB) -> dict:
    return {
        "family": family,
        "base": "1.0",
        "target": "1.1",
        "path": path,
        "targetSize": target_size,
        "createPeakBytes": create_peak,
        "applyPeakBytes": apply_peak,
    }


def write_memory(directory: Path, records: list[dict], *, baseline=10 * MIB, environment=LINUX_X64) -> None:
    document = {
        "schema": summary.MEMORY_SCHEMA,
        "corpusPairsSha256": PAIRS_SHA256,
        "environment": dict(environment),
        "idleBaselineBytes": baseline,
        "files": records,
    }
    (directory / "patch-lab-memory.json").write_text(json.dumps(document), encoding="utf-8")


def write_references(directory: Path, records: list[dict]) -> None:
    document = {
        "schema": summary.REFS_SCHEMA,
        "runId": "PATCH-PREFREEZE-001/RUN-20260928-1-0123456-linux-x64",
        "corpusPairsSha256": PAIRS_SHA256,
        "environment": {
            "osDescription": "Ubuntu 24.04.5 LTS",
            "osArchitecture": "x86_64",
            "processorCount": 8,
            "pythonVersion": "3.14.0",
            "gitCommit": COMMIT,
        },
        "tools": {"zstd": "v1.5.5", "xdelta3": "3.0.11", "bsdiff4": "1.2.4"},
        "workers": 1,
        "decodeRepeats": 3,
        "files": records,
    }
    (directory / "patch-lab-references.json").write_text(json.dumps(document), encoding="utf-8")


def prefreeze_pair(directory: Path, csp_patch: int, raw_patch: int, *, csp_apply=1.0, raw_apply=2.0, environment=LINUX_X64) -> None:
    write_lane(
        directory,
        "csp",
        [lab_record(CALIBRATION, "a.dll", patch=csp_patch, apply=csp_apply, check_off=max(csp_apply - 0.1, 0.1))],
        environment=environment,
    )
    write_lane(
        directory,
        "csp-raw",
        [lab_record(CALIBRATION, "a.dll", patch=raw_patch, apply=raw_apply, check_off=max(raw_apply - 0.1, 0.1))],
        environment=environment,
    )


class PrefreezeTests(unittest.TestCase):
    def test_r1_and_r2_pass_adopt(self):
        fixture = Fixture(self)
        prefreeze_pair(fixture.platform("platform-x64"), 2 * MIB, 4 * MIB)
        prefreeze_pair(fixture.platform("platform-arm64"), 2 * MIB, 4 * MIB, environment=LINUX_ARM64)
        code, document, markdown = fixture.run(fixture.platform("platform-x64"), fixture.platform("platform-arm64"))

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-PREFREEZE-001"]
        self.assertEqual(experiment["verdict"], "ADOPT")
        self.assertEqual(experiment["rules"]["R1"]["platforms"]["platform-x64"]["ratio"], 0.5)
        self.assertEqual(experiment["rules"]["R2"]["verdict"], "ADOPT")
        self.assertIn("**ADOPT**", markdown)
        self.assertIn("2.00", markdown)

    def test_r1_fail_rejects(self):
        fixture = Fixture(self)
        prefreeze_pair(fixture.platform("platform-x64"), 8 * MIB, 10 * MIB)
        code, document, _ = fixture.run(fixture.platform("platform-x64"))

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-PREFREEZE-001"]
        self.assertEqual(experiment["rules"]["R1"]["verdict"], "REJECT")
        self.assertEqual(experiment["rules"]["R2"]["verdict"], "ADOPT")
        self.assertEqual(experiment["verdict"], "REJECT")

    def test_r2_fail_rejects(self):
        fixture = Fixture(self)
        prefreeze_pair(fixture.platform("platform-x64"), 2 * MIB, 4 * MIB, csp_apply=10.0, raw_apply=1.0)
        code, document, _ = fixture.run(fixture.platform("platform-x64"))

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-PREFREEZE-001"]
        self.assertEqual(experiment["rules"]["R1"]["verdict"], "ADOPT")
        self.assertEqual(experiment["rules"]["R2"]["verdict"], "REJECT")
        self.assertEqual(experiment["verdict"], "REJECT")

    def test_missing_csp_raw_is_not_evaluated(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(directory, "csp", [lab_record(CALIBRATION, "a.dll", patch=2 * MIB, apply=1.0, check_off=0.9)])
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-PREFREEZE-001"]
        self.assertEqual(experiment["verdict"], "not evaluated")
        self.assertEqual(experiment["rules"]["R1"]["verdict"], "not evaluated")
        self.assertEqual(experiment["rules"]["R2"]["verdict"], "not evaluated")

    def test_reference_lanes_are_reported(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        lanes = {
            "full": (3 * MIB, 0.0, 0.0),
            "full-zstd": (1 * MIB, 2.0, 0.4),
            "zstd-patch-from": (int(0.5 * MIB), 8.0, 0.5),
            "xdelta3": (int(0.4 * MIB), 6.0, 0.3),
            "bsdiff": (int(0.3 * MIB), 9.0, 0.2),
        }
        record = {
            "family": CALIBRATION,
            "base": "1.0",
            "target": "1.1",
            "path": "a.dll",
            "targetSize": 3 * MIB,
            "lanes": {
                lane: {"bytes": values[0], "encodeSeconds": values[1], "decodeSeconds": values[2]}
                for lane, values in lanes.items()
            },
        }
        write_references(directory, [record])
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        rows = document["experiments"]["PATCH-PREFREEZE-001"]["informative"]["platforms"]["platform-x64"]["lanes"]
        self.assertEqual(rows["full"]["endToEnd1000Seconds"], 3 * MIB * 8 / 1e9)
        self.assertEqual(rows["xdelta3"]["bytes"], int(0.4 * MIB))
        self.assertEqual(rows["xdelta3"]["decodeSeconds"], 0.3)


class EncodeTests(unittest.TestCase):
    def test_winner_adopted(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_sweep(directory, {"sweep-L9-K1-C8": (900, 120.0, 480)})
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-ENC-002"]
        self.assertEqual(experiment["verdict"], "ADOPT")
        self.assertEqual(experiment["winner"], "sweep-L9-K1-C8")
        self.assertEqual(experiment["adopted"], "sweep-L9-K1-C8")
        self.assertEqual(experiment["outcome"], "ADOPT sweep-L9-K1-C8")
        self.assertAlmostEqual(experiment["savingFraction"], 0.1)

    def test_default_kept_below_two_percent(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_sweep(directory, {"sweep-L9-K1-C8": (995, 120.0, 480)})
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-ENC-002"]
        self.assertEqual(experiment["winner"], "sweep-L9-K1-C8")
        self.assertEqual(experiment["adopted"], "current default")
        self.assertEqual(experiment["outcome"], "ADOPT current default")

    def test_default_kept_when_holdout_grows(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_sweep(directory, {"sweep-L9-K1-C8": (800, 120.0, 600)})
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-ENC-002"]
        self.assertEqual(experiment["winner"], "sweep-L9-K1-C8")
        self.assertFalse(experiment["winnerHoldoutWithinDefault"])
        self.assertEqual(experiment["adopted"], "current default")

    def test_fast_setting_ineligible_by_time(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_sweep(
            directory,
            {
                "sweep-L9-K1-C8": (500, 200.0, 400),
                "sweep-L9-K1-C16": (900, 140.0, 480),
            },
        )
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-ENC-002"]
        self.assertFalse(experiment["settings"]["sweep-L9-K1-C8"]["eligible"])
        self.assertTrue(experiment["settings"]["sweep-L9-K1-C16"]["eligible"])
        self.assertEqual(experiment["winner"], "sweep-L9-K1-C16")
        self.assertEqual(experiment["adopted"], "sweep-L9-K1-C16")

    def test_no_x64_linux_platform(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-arm64")
        write_sweep(directory, environment=LINUX_ARM64)
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-ENC-002"]
        self.assertEqual(experiment["verdict"], "not evaluated")
        self.assertEqual(experiment["reason"], "no x64 Linux sweep")

    def test_missing_sweep_lanes(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(directory, "csp", [lab_record(CALIBRATION, "a.dll", patch=MIB)])
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-ENC-002"]
        self.assertEqual(experiment["verdict"], "not evaluated")
        self.assertEqual(experiment["reason"], "no x64 Linux sweep")
        self.assertEqual(len(experiment["missingLanes"]), len(summary.SWEEP_LANES))

    def test_sweep_bytes_compared_across_platforms(self):
        fixture = Fixture(self)
        write_sweep(fixture.platform("platform-x64"))
        write_sweep(fixture.platform("platform-arm64"), environment=LINUX_ARM64)
        code, document, _ = fixture.run(fixture.platform("platform-x64"), fixture.platform("platform-arm64"))

        self.assertEqual(code, 0)
        cross = document["experiments"]["PATCH-ENC-002"]["crossPlatformSweepBytes"]
        self.assertTrue(cross["sweep-L19-K2-C8"]["identical"])

        fixture = Fixture(self)
        write_sweep(fixture.platform("platform-x64"))
        write_sweep(fixture.platform("platform-arm64"), {"sweep-L9-K1-C8": (901, 100.0, 500)}, environment=LINUX_ARM64)
        code, document, _ = fixture.run(fixture.platform("platform-x64"), fixture.platform("platform-arm64"))

        self.assertEqual(code, 0)
        cross = document["experiments"]["PATCH-ENC-002"]["crossPlatformSweepBytes"]
        self.assertFalse(cross["sweep-L9-K1-C8"]["identical"])
        self.assertEqual(cross["sweep-L9-K1-C8"]["firstDifference"]["path"], "a.dll")


class ApplyTests(unittest.TestCase):
    def test_a1_and_a2_pass_adopt(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(directory, "csp", [lab_record(CALIBRATION, "a.dll", patch=MIB, apply=1.1, check_off=1.0)])
        write_memory(
            directory,
            [memory_record(CALIBRATION, "a.dll", create_peak=20 * MIB, apply_peak=30 * MIB)],
            baseline=10 * MIB,
        )
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-APPLY-001"]
        self.assertEqual(experiment["verdict"], "ADOPT")
        self.assertAlmostEqual(experiment["rules"]["A1"]["platforms"]["platform-x64"]["overheadFraction"], 0.1)
        self.assertEqual(experiment["rules"]["A2"]["platforms"]["platform-x64"]["verdict"], "ADOPT")

    def test_a1_fail_defers(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(directory, "csp", [lab_record(CALIBRATION, "a.dll", patch=MIB, apply=1.3, check_off=1.0)])
        write_memory(
            directory,
            [memory_record(CALIBRATION, "a.dll", create_peak=20 * MIB, apply_peak=30 * MIB)],
            baseline=10 * MIB,
        )
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-APPLY-001"]
        self.assertEqual(experiment["rules"]["A1"]["verdict"], "DEFER")
        self.assertEqual(experiment["verdict"], "DEFER")

    def test_a2_fail_names_worst_file(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(directory, "csp", [lab_record(CALIBRATION, "a.dll", patch=MIB, apply=1.1, check_off=1.0)])
        write_memory(
            directory,
            [
                memory_record(CALIBRATION, "a.dll", create_peak=20 * MIB, apply_peak=30 * MIB),
                memory_record(HOLDOUT, "b.js", create_peak=200 * MIB, apply_peak=300 * MIB),
            ],
            baseline=10 * MIB,
        )
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-APPLY-001"]
        a2 = experiment["rules"]["A2"]["platforms"]["platform-x64"]
        self.assertEqual(a2["verdict"], "DEFER")
        self.assertEqual(a2["worst"]["path"], "b.js")
        self.assertEqual(a2["worst"]["excessBytes"], 290 * MIB)
        self.assertEqual(experiment["verdict"], "DEFER")

    def test_missing_memory_is_not_evaluated(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(directory, "csp", [lab_record(CALIBRATION, "a.dll", patch=MIB, apply=1.1, check_off=1.0)])
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 0)
        experiment = document["experiments"]["PATCH-APPLY-001"]
        self.assertEqual(experiment["rules"]["A1"]["verdict"], "ADOPT")
        self.assertEqual(experiment["rules"]["A2"]["verdict"], "not evaluated")
        self.assertEqual(experiment["verdict"], "not evaluated")


class PartialEvidenceTests(unittest.TestCase):
    def test_platform_without_the_lane_is_not_a_pass(self):
        fixture = Fixture(self)
        x64 = fixture.platform("platform-x64")
        prefreeze_pair(x64, 2 * MIB, 4 * MIB)
        write_memory(
            x64,
            [memory_record(CALIBRATION, "a.dll", create_peak=20 * MIB, apply_peak=30 * MIB)],
            baseline=10 * MIB,
        )
        arm64 = fixture.platform("platform-arm64")
        write_lane(
            arm64,
            "sweep-L19-K2-C8",
            sweep_records(1000, 100.0, 500),
            environment=LINUX_ARM64,
        )
        code, document, _ = fixture.run(x64, arm64)

        self.assertEqual(code, 0)
        prefreeze = document["experiments"]["PATCH-PREFREEZE-001"]
        self.assertEqual(prefreeze["verdict"], "not evaluated")
        self.assertEqual(prefreeze["rules"]["R1"]["verdict"], "not evaluated")
        apply_experiment = document["experiments"]["PATCH-APPLY-001"]
        self.assertEqual(apply_experiment["verdict"], "not evaluated")
        self.assertEqual(apply_experiment["rules"]["A1"]["verdict"], "not evaluated")
        self.assertEqual(apply_experiment["rules"]["A2"]["verdict"], "not evaluated")


class ProvenanceTests(unittest.TestCase):
    def test_lock_mismatch_is_rejected(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(
            directory,
            "csp",
            [lab_record(CALIBRATION, "a.dll", patch=MIB)],
            pairs_sha256=OTHER_SHA256,
        )
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 1)
        self.assertIsNone(document)

    def test_mixed_commit_is_rejected(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(directory, "csp", [lab_record(CALIBRATION, "a.dll", patch=MIB)])
        other = dict(LINUX_X64, gitCommit="f" * 40)
        write_lane(directory, "csp-raw", [lab_record(CALIBRATION, "a.dll", patch=2 * MIB)], environment=other)
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 1)
        self.assertIsNone(document)

    def test_missing_commit_is_rejected(self):
        fixture = Fixture(self)
        directory = fixture.platform("platform-x64")
        write_lane(
            directory,
            "csp",
            [lab_record(CALIBRATION, "a.dll", patch=MIB)],
            environment={key: value for key, value in LINUX_X64.items() if key != "gitCommit"},
        )
        code, document, _ = fixture.run(directory)

        self.assertEqual(code, 1)
        self.assertIsNone(document)

    def test_empty_platform_reports_not_evaluated(self):
        fixture = Fixture(self)
        code, document, markdown = fixture.run(fixture.platform("platform-x64"))

        self.assertEqual(code, 0)
        self.assertEqual(document["experiments"]["PATCH-PREFREEZE-001"]["verdict"], "not evaluated")
        self.assertEqual(document["experiments"]["PATCH-ENC-002"]["verdict"], "not evaluated")
        self.assertEqual(document["experiments"]["PATCH-APPLY-001"]["verdict"], "not evaluated")
        self.assertIn("not evaluated", markdown)


class DiagnosisMemoryTests(unittest.TestCase):
    def test_a_diagnosis_memory_run_never_decides_a2(self) -> None:
        within = {"family": "f", "base": "1", "target": "2", "path": "a", "targetSize": 2 * MIB,
                  "createPeakBytes": 40 * MIB, "applyPeakBytes": 35 * MIB}
        for lane, environment in (("sweep-L19-K2-C8", {}), ("csp", {"MALLOC_ARENA_MAX": "2"})):
            platform = {
                "name": "x64",
                "lanes": {},
                "runIds": [],
                "memory": {
                    "idleBaselineBytes": 30 * MIB,
                    "lane": lane,
                    "memoryEnvironment": environment,
                    "files": [within],
                },
            }
            result = summary.evaluate_apply([platform])
            self.assertEqual(summary.NOT_EVALUATED, result["verdict"])
            self.assertIsNone(result["rules"]["A2"]["platforms"]["x64"]["verdict"])
            self.assertIn("diagnosis run", result["reason"])
            self.assertIn(lane, result["reason"])


class PrintMemoryTests(unittest.TestCase):
    def document(self, files: list[dict]) -> dict:
        return {
            "schema": "chunkshift.patch-lab-memory.v1",
            "runId": "PATCH-APPLY-001/RUN-1",
            "lane": "sweep-L19-K2-C8",
            "policy": {"level": 19, "dictionaryChunks": 2, "maxCandidates": 8, "searchRadius": 262144},
            "memoryEnvironment": {"MALLOC_TRIM_THRESHOLD_": "131072"},
            "idleBaselineBytes": 30 * MIB,
            "files": files,
        }

    def test_excess_is_reported_per_file_with_worst_median_and_count(self) -> None:
        files = [
            {"family": "f", "base": "1", "target": "2", "path": "a", "targetSize": 2 * MIB,
             "createPeakBytes": 130 * MIB, "applyPeakBytes": 40 * MIB},
            {"family": "f", "base": "1", "target": "2", "path": "b", "targetSize": MIB,
             "createPeakBytes": 60 * MIB, "applyPeakBytes": 50 * MIB},
        ]
        lines = memory_print.render(self.document(files))
        self.assertIn("lane=sweep-L19-K2-C8", lines[1])
        self.assertEqual("memoryEnvironment=MALLOC_TRIM_THRESHOLD_=131072", lines[2])
        self.assertEqual("idle=30.0 MiB", lines[3])
        self.assertEqual("100.0\t10.0\t2.0\tf 1->2 a", lines[5])
        self.assertEqual("files=2 create worst=100.0 median=65.0 over64=1 apply worst=20.0", lines[-1])

    def test_documents_before_the_lane_field_default_to_csp(self) -> None:
        document = self.document([])
        for key in ("lane", "policy", "memoryEnvironment"):
            del document[key]
        lines = memory_print.render(document)
        self.assertTrue(lines[1].startswith("lane=csp "))
        self.assertEqual("memoryEnvironment=-", lines[2])
        self.assertEqual("files=0", lines[-1])

    def test_other_schemas_are_rejected(self) -> None:
        with self.assertRaises(ValueError):
            memory_print.render({"schema": "chunkshift.patch-lab.v1"})


class PrintEncoderTests(unittest.TestCase):
    FAMILIES = {"cal": "calibration", "hold": "holdout"}

    def document(self, files: list[dict]) -> dict:
        return {
            "schema": "chunkshift.patch-lab.v1",
            "runId": "PATCH-ENC-003/RUN-1",
            "lane": "enc-L19-K4-C8-prefix",
            "policy": {"level": 19, "dictionaryChunks": 4, "dictionaryLoad": "prefix"},
            "workers": 4,
            "elapsedSeconds": 12.34,
            "files": files,
        }

    @staticmethod
    def file(family: str, path: str, size: int, seconds: float, sha: str) -> dict:
        return {"family": family, "base": "1", "target": "2", "path": path,
                "patchBytes": size, "createSeconds": seconds, "patchSha256": sha}

    def test_totals_are_split_and_the_digest_covers_every_patch(self) -> None:
        files = [
            self.file("cal", "a", 100, 1.5, "aa"),
            self.file("cal", "b", 50, 0.5, "bb"),
            self.file("hold", "c", 7, 3.0, "cc"),
        ]
        lines = encoder_print.render(self.document(files), self.FAMILIES)
        self.assertIn("lane=enc-L19-K4-C8-prefix workers=4", lines[0])
        self.assertEqual("calibration files=2 bytes=150 createSeconds=2.00", lines[1])
        self.assertEqual("holdout files=1 bytes=7 createSeconds=3.00", lines[2])
        self.assertEqual("total bytes=157 createSeconds=5.00 elapsedSeconds=12.3", lines[3])

        reordered = encoder_print.render(self.document(files[::-1]), self.FAMILIES)
        self.assertEqual(lines[4], reordered[4])
        changed = encoder_print.render(
            self.document([*files[:2], self.file("hold", "c", 7, 3.0, "cd")]), self.FAMILIES)
        self.assertNotEqual(lines[4], changed[4])

    def test_a_family_without_a_split_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            encoder_print.render(self.document([self.file("other", "a", 1, 1.0, "aa")]), self.FAMILIES)

    def test_other_schemas_are_rejected(self) -> None:
        with self.assertRaises(ValueError):
            encoder_print.render({"schema": "chunkshift.patch-lab-memory.v1"}, self.FAMILIES)


if __name__ == "__main__":
    unittest.main()
