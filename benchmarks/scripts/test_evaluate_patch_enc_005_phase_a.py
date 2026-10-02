import importlib.util
import json
import tempfile
import unittest
import sys
from pathlib import Path

ROOT = Path(__file__).parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
EVALUATOR_PATH = ROOT / "evaluate_patch_enc_005_phase_a.py"
RECOMPUTE_PATH = ROOT / "recompute_patch_enc_005_phase_a.py"


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


EVALUATOR = load("patch_enc_005_evaluator", EVALUATOR_PATH)
RECOMPUTE = load("patch_enc_005_recompute", RECOMPUTE_PATH)


def compact(lane_overrides=None):
    lane_overrides = lane_overrides or {}
    patch_bytes = {
        "csp": 1000,
        "H4-L1-R2": 1000,
        "H7-L1-R2-E75": 1000,
        "H9-L9-K4-C16-R1M": 960,
        "H9-L12-K4-C16-R1M": 950,
        "H9-L15-K4-C16-R1M": 940,
    }
    platforms = {}
    defaults = {
        "H4-L1-R2": (0.45, 0.60, 1.02),
        "H7-L1-R2-E75": (0.44, 0.59, 1.01),
        "H9-L9-K4-C16-R1M": (1.20, 0.90, 1.01),
        "H9-L12-K4-C16-R1M": (1.30, 0.95, 1.02),
        "H9-L15-K4-C16-R1M": (1.40, 1.00, 1.03),
    }
    for platform in EVALUATOR.PLATFORMS:
        timing = {}
        apply = {"csp": 10.0}
        memory = {}
        for lane in EVALUATOR.LANES:
            wall, cpu, apply_ratio = lane_overrides.get(lane, defaults[lane])
            timing[lane] = {
                "wallRatioMedian": wall,
                "cpuRatioMedian": cpu,
                "wallRatios": [wall] * 5,
                "cpuRatios": [cpu] * 5,
            }
            apply[lane] = 10.0 * apply_ratio
        for lane in EVALUATOR.ALL_LANES:
            memory[lane] = {
                "createPeakOverIdleBytes": 40 * 1024 * 1024,
                "applyPeakOverIdleBytes": 30 * 1024 * 1024,
                "files": [
                    {
                        "createPeakOverIdleBytes": 40 * 1024 * 1024,
                        "applyPeakOverIdleBytes": 30 * 1024 * 1024,
                    }
                ],
            }
        rounds = []
        for index in range(5):
            candidates = []
            for lane in EVALUATOR.LANES:
                wall, cpu, _ = lane_overrides.get(lane, defaults[lane])
                candidates.append(
                    {
                        "lane": lane,
                        "aggregate": {
                            "wallSeconds": 100.0 * wall,
                            "cpuSeconds": 100.0 * cpu,
                            "patchBytes": patch_bytes[lane],
                        },
                    }
                )
            rounds.append(
                {
                    "round": index + 1,
                    "h0Start": {"wallSeconds": 100.0, "cpuSeconds": 100.0},
                    "h0End": {"wallSeconds": 100.0, "cpuSeconds": 100.0},
                    "candidates": candidates,
                }
            )
        apply_raw = {}
        for lane in EVALUATOR.ALL_LANES:
            wall = apply[lane]
            apply_raw[lane] = {
                "aggregate": {
                    "wallSeconds": wall,
                    "cpuSeconds": 5.0,
                    "baseReads": 3,
                    "baseBytesRead": 4096,
                },
                "files": [
                    {
                        "samples": [
                            {
                                "wallSeconds": wall,
                                "cpuSeconds": 5.0,
                                "baseReads": 3,
                                "baseBytesRead": 4096,
                                "baseSeeks": 1,
                            }
                            for _ in range(5)
                        ]
                    }
                ],
            }
        platforms[platform] = {
            "rounds": rounds,
            "timing": timing,
            "applyWallSeconds": apply,
            "applyRaw": apply_raw,
            "memory": memory,
        }
    return {
        "schema": "chunkshift.patch-enc-005-phase-a-compact.v1",
        "experimentId": "PATCH-ENC-005",
        "protocolCommit": EVALUATOR.PROTOCOL_COMMIT,
        "sourceCommit": "a" * 40,
        "datasetRole": "calibration",
        "datasetSha256": EVALUATOR.DATASET_SHA256,
        "patchBytes": patch_bytes,
        "platforms": platforms,
    }


SOURCE = "a" * 40
FILE_KEY = {
    "family": "dotnet-runtime-linux-arm64",
    "base": "10.0.10",
    "target": "10.0.11",
    "path": "shared/test.dll",
}
PATCH_BYTES = {
    "csp": 1000,
    "H4-L1-R2": 1000,
    "H7-L1-R2-E75": 1000,
    "H9-L9-K4-C16-R1M": 960,
    "H9-L12-K4-C16-R1M": 950,
    "H9-L15-K4-C16-R1M": 940,
}
PATCH_SHA = {
    "csp": "1" * 64,
    "H4-L1-R2": "2" * 64,
    "H7-L1-R2-E75": "2" * 64,
    "H9-L9-K4-C16-R1M": "3" * 64,
    "H9-L12-K4-C16-R1M": "4" * 64,
    "H9-L15-K4-C16-R1M": "5" * 64,
}


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def environment(platform: str) -> dict:
    arch = "Arm64" if platform == "linux-arm64" else "X64"
    os_description = "Linux test" if platform.startswith("linux-") else "Microsoft Windows test"
    return {
        "osDescription": os_description,
        "osArchitecture": arch,
        "processArchitecture": arch,
        "frameworkDescription": ".NET 10.0.0 test",
        "processorCount": 4,
        "gitCommit": SOURCE,
        "processorDescription": "test cpu",
    }


def synthetic_artifacts(root: Path):
    paired_paths = {}
    memory_paths = {}
    accepted = {
        lane: [{**FILE_KEY, "patchSha256": PATCH_SHA[lane]}]
        for lane in EVALUATOR.ALL_LANES
    }

    for platform in EVALUATOR.PLATFORMS:
        paired_root = root / "paired" / platform
        trace = {}
        for lane in EVALUATOR.ALL_LANES:
            relative = f"trace-correctness/{lane}/run.json"
            write_json(
                paired_root / relative,
                {
                    "schema": "chunkshift.patch-lab.v1",
                    "lane": lane,
                    "files": [
                        {
                            **FILE_KEY,
                            "patchBytes": PATCH_BYTES[lane],
                            "patchSha256": PATCH_SHA[lane],
                        }
                    ],
                },
            )
            trace_directory = f"trace-correctness/{lane}/traces"
            write_json(
                paired_root / trace_directory / "trace.json",
                {
                    "schema": "chunkshift.patch-candidate-trace.v1",
                    "experimentId": "PATCH-ENC-005",
                    "runId": f"PATCH-ENC-005/RUN-20261002-001-{SOURCE}-{platform}",
                    "protocolCommit": EVALUATOR.PROTOCOL_COMMIT,
                    "sourceCommit": SOURCE,
                    "platform": platform,
                    "lane": lane,
                    "datasetRole": "calibration",
                    "datasetSha256": EVALUATOR.DATASET_SHA256,
                    "family": FILE_KEY["family"],
                    "baseVersion": FILE_KEY["base"],
                    "targetVersion": FILE_KEY["target"],
                    "path": FILE_KEY["path"],
                    "baseManifestId": "sha256:" + "a" * 64,
                    "targetManifestId": "sha256:" + "b" * 64,
                    "finalLevel": 19,
                    "entries": [
                        {
                            "targetIndex": 0,
                            "targetChunkId": "sha256:" + "c" * 64,
                            "targetOffset": 0,
                            "targetLength": 1024,
                            "candidateCount": 0,
                            "cheapTrialCount": 0,
                            "expensiveTrialCount": 0,
                            "totalCompressionTrialCount": 1,
                            "level19TrialCount": 1,
                            "noDictionaryFrameBytes": 100,
                            "l19NoDictionaryFrameBytes": 100,
                            "baselineCostBytes": 100,
                            "selectedEncoding": "zstd",
                            "selectedCandidate": None,
                            "storedBytes": 100,
                            "dictionaryRefs": 0,
                            "candidates": [],
                        }
                    ],
                },
            )
            trace[lane] = {
                "result": relative,
                "traceDirectory": trace_directory,
                "correctness": [
                    {
                        **FILE_KEY,
                        "patchSha256": PATCH_SHA[lane],
                        "targetSha256": "f" * 64,
                        "decoderOutputSha256": "f" * 64,
                        "verdict": "valid",
                    }
                ],
            }

        candidates = [
            {
                "lane": lane,
                "aggregate": {
                    "wallSeconds": 45.0 if lane in ("H4-L1-R2", "H7-L1-R2-E75") else 120.0,
                    "cpuSeconds": 70.0 if lane in ("H4-L1-R2", "H7-L1-R2-E75") else 90.0,
                    "patchBytes": PATCH_BYTES[lane],
                    "allocatedBytes": 1,
                    "baseReads": 1,
                    "baseBytesRead": 1,
                    "baseSeeks": 1,
                },
                "wallRatio": 0.45 if lane in ("H4-L1-R2", "H7-L1-R2-E75") else 1.2,
                "cpuRatio": 0.7 if lane in ("H4-L1-R2", "H7-L1-R2-E75") else 0.9,
            }
            for lane in EVALUATOR.LANES
        ]
        summaries = []
        for lane in EVALUATOR.LANES:
            wall = next(row["wallRatio"] for row in candidates if row["lane"] == lane)
            cpu = next(row["cpuRatio"] for row in candidates if row["lane"] == lane)
            summaries.append(
                {
                    "lane": lane,
                    "wallRatios": [wall] * 5,
                    "cpuRatios": [cpu] * 5,
                    "wallRatioMedian": wall,
                    "cpuRatioMedian": cpu,
                }
            )
        rounds = [
            {
                "round": index + 1,
                "h0Start": {
                    "wallSeconds": 100.0,
                    "cpuSeconds": 100.0,
                    "patchBytes": 1000,
                    "allocatedBytes": 1,
                    "baseReads": 1,
                    "baseBytesRead": 1,
                    "baseSeeks": 1,
                },
                "h0End": {
                    "wallSeconds": 100.0,
                    "cpuSeconds": 100.0,
                    "patchBytes": 1000,
                    "allocatedBytes": 1,
                    "baseReads": 1,
                    "baseBytesRead": 1,
                    "baseSeeks": 1,
                },
                "bracketNoisy": False,
                "candidates": candidates,
            }
            for index in range(5)
        ]
        apply = {}
        for lane in EVALUATOR.ALL_LANES:
            wall = 10.0 if lane == "csp" else 10.2
            samples = [
                {
                    "wallSeconds": wall,
                    "cpuSeconds": 5.0,
                    "baseReads": 3,
                    "baseBytesRead": 4096,
                    "baseSeeks": 1,
                }
                for _ in range(5)
            ]
            apply[lane] = {
                "environment": environment(platform),
                "aggregate": {
                    "wallSeconds": wall,
                    "cpuSeconds": 5.0,
                    "baseReads": 3,
                    "baseBytesRead": 4096,
                },
                "files": [
                    {
                        **FILE_KEY,
                        "patchSha256": PATCH_SHA[lane],
                        "samples": samples,
                    }
                ],
            }
        run_id = f"PATCH-ENC-005/RUN-20261002-001-{SOURCE}-{platform}"
        paired = {
            "schema": "chunkshift.patch-enc-005-paired.v2",
            "experimentId": "PATCH-ENC-005",
            "runId": run_id,
            "protocolCommit": EVALUATOR.PROTOCOL_COMMIT,
            "sourceCommit": SOURCE,
            "platform": platform,
            "datasetRole": "calibration",
            "datasetSha256": EVALUATOR.DATASET_SHA256,
            "githubRunId": "123",
            "githubRunNumber": "1",
            "githubRunAttempt": "1",
            "status": "VALID",
            "acceptedTiming": {"rounds": rounds, "summaries": summaries},
            "acceptedPatchShas": accepted,
            "timingEnvironment": environment(platform),
            "applyEvidence": apply,
            "traceCorrectness": trace,
        }
        paired_path = paired_root / "paired.json"
        write_json(paired_path, paired)
        paired_paths[platform] = paired_path

        memory_root = root / "memory" / platform
        memory_paths[platform] = memory_root
        for lane in EVALUATOR.ALL_LANES:
            write_json(
                memory_root / f"{lane}.json",
                {
                    "schema": "chunkshift.patch-lab-memory.v1",
                    "runId": run_id,
                    "lane": lane,
                    "corpusPairsSha256": EVALUATOR.DATASET_SHA256,
                    "execution": "h2-w2",
                    "population": "max-base-target",
                    "applyCheck": "boundary",
                    "environment": environment(platform),
                    "idleBaselineBytes": 20 * 1024 * 1024,
                    "files": [
                        {
                            **FILE_KEY,
                            "baseSize": 2 * 1024 * 1024,
                            "targetSize": 2 * 1024 * 1024,
                            "createPeakBytes": 60 * 1024 * 1024,
                            "applyPeakBytes": 50 * 1024 * 1024,
                        }
                    ],
                },
            )

    cross_path = root / "cross-platform.json"
    write_json(
        cross_path,
        {
            "schema": "chunkshift.patch-enc-005-cross-platform.v1",
            "experimentId": "PATCH-ENC-005",
            "date": "20261002",
            "sequence": "001",
            "sourceCommit": SOURCE,
            "protocolCommit": EVALUATOR.PROTOCOL_COMMIT,
            "dataset": f"calibration:{EVALUATOR.DATASET_SHA256}",
            "githubRunId": "123",
            "githubRunNumber": "1",
            "githubRunAttempt": "1",
            "platforms": sorted(EVALUATOR.PLATFORMS),
            "patchBytesEqual": True,
            "acceptedPatchShas": accepted,
        },
    )
    return paired_paths, memory_paths, cross_path


class PatchEnc005PhaseAEvaluatorTests(unittest.TestCase):

    def test_full_synthetic_artifact_chain_compiles(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paired, memory, cross = synthetic_artifacts(root)

            compiled, files = EVALUATOR.build_compact(
                paired, memory, cross, SOURCE
            )
            verdict = EVALUATOR.evaluate(compiled)

            self.assertEqual(6, len(files))
            self.assertEqual("READY_FOR_FIXED_EVALUATION", verdict["status"])
            self.assertEqual(1000, verdict["h0PatchBytes"])
            self.assertTrue(compiled["byteOracles"]["crossPlatformPatchSha256Equal"])


    def test_speed_and_size_finalists_follow_frozen_order(self):
        result = EVALUATOR.evaluate(compact())

        self.assertEqual("READY_FOR_FIXED_EVALUATION", result["status"])
        self.assertEqual("H7-L1-R2-E75", result["speedFinalist"])
        self.assertEqual("H9-L15-K4-C16-R1M", result["sizeFinalist"])
        self.assertEqual(
            ["H7-L1-R2-E75", "H9-L15-K4-C16-R1M"],
            result["finalists"],
        )
        self.assertIn("speed", result["lanes"]["H7-L1-R2-E75"]["qualificationBranches"])
        self.assertIn("size", result["lanes"]["H9-L15-K4-C16-R1M"]["qualificationBranches"])

    def test_memory_guardrail_removes_otherwise_eligible_lane(self):
        document = compact()
        for platform in EVALUATOR.PLATFORMS:
            document["platforms"][platform]["memory"]["H7-L1-R2-E75"][
                "createPeakOverIdleBytes"
            ] = EVALUATOR.CREATE_BOUND_BYTES + 1

        result = EVALUATOR.evaluate(document)

        self.assertFalse(result["lanes"]["H7-L1-R2-E75"]["eligible"])
        self.assertIn("memory-bound", result["lanes"]["H7-L1-R2-E75"]["reasons"])
        self.assertNotEqual("H7-L1-R2-E75", result["speedFinalist"])

    def test_apply_guardrail_is_required_on_every_platform(self):
        document = compact()
        document["platforms"]["win-x64"]["applyWallSeconds"]["H4-L1-R2"] = 11.01

        result = EVALUATOR.evaluate(document)

        self.assertFalse(result["lanes"]["H4-L1-R2"]["eligible"])
        self.assertIn("apply>1.10", result["lanes"]["H4-L1-R2"]["reasons"])

    def test_no_qualifying_lane_rejects_without_opening_evaluation(self):
        document = compact(
            {
                lane: (1.60, 1.00, 1.00)
                for lane in EVALUATOR.LANES
            }
        )
        document["patchBytes"] = {lane: 1000 for lane in EVALUATOR.ALL_LANES}

        result = EVALUATOR.evaluate(document)

        self.assertEqual("REJECT", result["status"])
        self.assertEqual([], result["finalists"])
        self.assertEqual([], result["pareto"])

    def test_wall_threshold_requires_four_of_five_rounds(self):
        document = compact()
        values = [0.49, 0.49, 0.49, 0.60, 0.60]
        for platform in EVALUATOR.PLATFORMS:
            document["platforms"][platform]["timing"]["H4-L1-R2"]["wallRatios"] = values
            document["platforms"][platform]["timing"]["H4-L1-R2"]["wallRatioMedian"] = 0.49

        result = EVALUATOR.evaluate(document)

        self.assertNotIn("speed", result["lanes"]["H4-L1-R2"]["qualificationBranches"])
        self.assertEqual(
            {"linux-x64": 3, "linux-arm64": 3, "win-x64": 3},
            result["lanes"]["H4-L1-R2"]["speedWallRoundPassesByPlatform"],
        )

    def test_exact_byte_thresholds_do_not_depend_on_float_rounding(self):
        document = compact()
        document["patchBytes"]["H4-L1-R2"] = 1020
        for platform in EVALUATOR.PLATFORMS:
            document["platforms"][platform]["timing"]["H4-L1-R2"]["wallRatioMedian"] = 0.50
            document["platforms"][platform]["timing"]["H4-L1-R2"]["wallRatios"] = [0.50] * 5

        result = EVALUATOR.evaluate(document)

        self.assertIn("speed", result["lanes"]["H4-L1-R2"]["qualificationBranches"])

    def test_h0_memory_violation_invalidates_calibration_evidence(self):
        document = compact()
        document["platforms"]["linux-x64"]["memory"]["csp"][
            "createPeakOverIdleBytes"
        ] = EVALUATOR.CREATE_BOUND_BYTES + 1

        with self.assertRaisesRegex(ValueError, "H0 violates frozen memory bounds"):
            EVALUATOR.evaluate(document)

    def test_independent_recompute_matches_selection(self):
        document = compact()
        verdict = EVALUATOR.evaluate(document)
        independent = RECOMPUTE.recompute(document)

        self.assertEqual(verdict["pareto"], independent["pareto"])
        self.assertEqual(verdict["speedFinalist"], independent["speedFinalist"])
        self.assertEqual(verdict["sizeFinalist"], independent["sizeFinalist"])
        self.assertEqual(verdict["finalists"], independent["finalists"])
        self.assertEqual(verdict["status"], independent["status"])


if __name__ == "__main__":
    unittest.main()
