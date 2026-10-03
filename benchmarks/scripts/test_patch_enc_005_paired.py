import importlib.util
import os
import unittest
from pathlib import Path
from types import SimpleNamespace

SCRIPT = Path(__file__).with_name("run_patch_enc_005_paired.py")
SPEC = importlib.util.spec_from_file_location("patch_enc_005_paired", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class PatchEnc005PairedTests(unittest.TestCase):
    def test_rotation_is_cyclic_by_one(self):
        lanes = ["a", "b", "c"]
        self.assertEqual(["a", "b", "c"], MODULE.rotated(lanes, 0))
        self.assertEqual(["b", "c", "a"], MODULE.rotated(lanes, 1))
        self.assertEqual(["c", "a", "b"], MODULE.rotated(lanes, 2))
        self.assertEqual(["a", "b", "c"], MODULE.rotated(lanes, 3))

    def test_noise_rule_is_strictly_more_than_25_percent_of_mean(self):
        self.assertFalse(MODULE.bracket_is_noisy(0.875, 1.125))
        self.assertTrue(MODULE.bracket_is_noisy(0.87, 1.13))
        self.assertTrue(MODULE.bracket_is_noisy(0.0, 0.0))

    def test_median_five_is_the_third_sorted_value(self):
        self.assertEqual(3.0, MODULE.median_five([5.0, 1.0, 3.0, 4.0, 2.0]))
        with self.assertRaises(ValueError):
            MODULE.median_five([1.0, 2.0])

    def test_development_family_sets_are_exact_and_confirmation_is_rejected(self):
        MODULE.validate_development_families(
            "calibration",
            "dotnet-runtime-linux-arm64,dotnet-aspnetcore-win-x64",
        )
        with self.assertRaises(ValueError):
            MODULE.validate_development_families(
                "calibration",
                "dotnet-aspnetcore-win-x64",
            )
        with self.assertRaises(ValueError):
            MODULE.validate_development_families("confirmation", None)

    def test_run_identity_binds_full_commit_and_platform(self):
        commit = "a" * 40
        MODULE.validate_run_identity(
            f"PATCH-ENC-005/RUN-20261001-007-{commit}-linux-x64",
            commit,
            "linux-x64",
        )
        with self.assertRaises(ValueError):
            MODULE.validate_run_identity(
                f"PATCH-ENC-005/RUN-20261001-007-{'b' * 40}-linux-x64",
                commit,
                "linux-x64",
            )

    def test_ci_binding_requires_commit_first_attempt_and_run_number(self):
        names = ("GITHUB_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_NUMBER", "GITHUB_RUN_ATTEMPT")
        previous = {name: os.environ.get(name) for name in names}
        commit = "a" * 40
        run_id = f"PATCH-ENC-005/RUN-20261001-007-{commit}-linux-x64"
        try:
            os.environ["GITHUB_SHA"] = commit
            os.environ["GITHUB_RUN_ID"] = "123456"
            os.environ["GITHUB_RUN_NUMBER"] = "7"
            os.environ["GITHUB_RUN_ATTEMPT"] = "1"
            MODULE.validate_ci_binding(commit, run_id)
            with self.assertRaises(ValueError):
                MODULE.validate_ci_binding("b" * 40, run_id)
            os.environ["GITHUB_RUN_ATTEMPT"] = "2"
            with self.assertRaises(ValueError):
                MODULE.validate_ci_binding(commit, run_id)
            os.environ["GITHUB_RUN_ATTEMPT"] = "1"
            os.environ["GITHUB_RUN_NUMBER"] = "8"
            with self.assertRaises(ValueError):
                MODULE.validate_ci_binding(commit, run_id)
        finally:
            for name, value in previous.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value

    def test_retry_predecessor_must_match_identity(self):
        commit = "a" * 40
        args = SimpleNamespace(
            run_id=f"PATCH-ENC-005/RUN-20261001-001-{commit}-linux-x64",
            source_commit=commit,
            platform="linux-x64",
            dataset_role="calibration",
        )
        previous = {
            name: os.environ.get(name)
            for name in ("GITHUB_RUN_ID", "GITHUB_RUN_NUMBER", "GITHUB_RUN_ATTEMPT")
        }
        try:
            os.environ["GITHUB_RUN_ID"] = "123"
            os.environ["GITHUB_RUN_NUMBER"] = "1"
            os.environ["GITHUB_RUN_ATTEMPT"] = "1"
            predecessor = {
                "schema": "chunkshift.patch-enc-005-dispatch.v1",
                "attempt": 1,
                "experimentId": MODULE.EXPERIMENT_ID,
                "runId": args.run_id,
                "protocolCommit": MODULE.FROZEN_PROTOCOL_COMMIT,
                "sourceCommit": commit,
                "platform": "linux-x64",
                "datasetRole": "calibration",
                "datasetSha256": MODULE.FROZEN_DATASET_SHA256,
                "githubRunId": "123",
                "githubRunNumber": "1",
                "githubRunAttempt": "1",
            }
            MODULE.validate_prior_dispatch(args, predecessor)
            predecessor["sourceCommit"] = "b" * 40
            with self.assertRaises(ValueError):
                MODULE.validate_prior_dispatch(args, predecessor)
        finally:
            for name, value in previous.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value

    def test_create_aggregate_excludes_apply(self):
        result = {
            "files": [
                {
                    "createSeconds": 1.25,
                    "patchBytes": 10,
                    "createMetrics": {
                        "cpuSeconds": 0.5,
                        "allocatedBytes": 100,
                        "baseReads": 2,
                        "baseBytesRead": 20,
                        "baseSeeks": 1,
                    },
                },
                {
                    "createSeconds": 2.75,
                    "patchBytes": 20,
                    "createMetrics": {
                        "cpuSeconds": 1.5,
                        "allocatedBytes": 200,
                        "baseReads": 3,
                        "baseBytesRead": 30,
                        "baseSeeks": 2,
                    },
                },
            ]
        }
        self.assertEqual(
            {
                "wallSeconds": 4.0,
                "cpuSeconds": 2.0,
                "allocatedBytes": 300,
                "baseReads": 5,
                "baseBytesRead": 50,
                "baseSeeks": 3,
                "patchBytes": 30,
            },
            MODULE.aggregate_create(result),
        )

    def test_apply_aggregate_requires_exactly_five_samples(self):
        result = {
            "files": [
                {
                    "applyMetrics": {
                        "medianWallSeconds": 0.3,
                        "medianCpuSeconds": 0.2,
                        "medianBaseReads": 4,
                        "medianBaseBytesRead": 100,
                        "samples": [{}, {}, {}, {}, {}],
                    }
                }
            ]
        }
        self.assertEqual(
            {
                "wallSeconds": 0.3,
                "cpuSeconds": 0.2,
                "baseReads": 4,
                "baseBytesRead": 100,
            },
            MODULE.aggregate_apply(result),
        )
        result["files"][0]["applyMetrics"]["samples"] = [{}]
        with self.assertRaises(ValueError):
            MODULE.aggregate_apply(result)

    def test_h7_h4_byte_oracle_compares_per_file_sha(self):
        base = {
            "files": [
                {
                    "family": "f",
                    "base": "1",
                    "target": "2",
                    "path": "a",
                    "patchSha256": "a" * 64,
                }
            ]
        }
        same = {"files": [dict(base["files"][0])]}
        MODULE.require_same_patch_bytes("oracle", base, same)
        changed = {"files": [dict(base["files"][0], patchSha256="b" * 64)]}
        with self.assertRaises(ValueError):
            MODULE.require_same_patch_bytes("oracle", base, changed)

    def test_h7_mismatch_is_recordable_without_turning_it_into_dispatch_failure(self):
        left = {
            ("family", "base", "target", "a"): "a" * 64,
            ("family", "base", "target", "b"): "b" * 64,
        }
        right = {
            ("family", "base", "target", "a"): "a" * 64,
            ("family", "base", "target", "b"): "c" * 64,
        }

        mismatches = MODULE.patch_mismatch_keys(left, right)

        self.assertEqual([("family", "base", "target", "b")], mismatches)
        self.assertEqual(
            MODULE.mismatch_file_set_sha256(mismatches),
            MODULE.mismatch_file_set_sha256(list(mismatches)),
        )

    def test_byte_oracle_rejects_different_file_sets(self):
        with self.assertRaises(ValueError):
            MODULE.patch_mismatch_keys(
                {("f", "b", "t", "a"): "a" * 64},
                {("f", "b", "t", "other"): "a" * 64},
            )

    def test_dispatch_continues_all_five_rounds_after_h7_oracle_failure(self):
        import tempfile

        digests = {
            "csp": "0" * 64,
            "H4-L1-R2": "4" * 64,
            "H7-L1-R2-E75": "7" * 64,
            "H9-L9-K4-C16-R1M": "9" * 64,
            "H9-L12-K4-C16-R1M": "a" * 64,
            "H9-L15-K4-C16-R1M": "b" * 64,
        }

        def fake_invoke(args, lane, directory, **_kwargs):
            return directory / "run.json", {
                "files": [
                    {
                        "family": "f",
                        "base": "1",
                        "target": "2",
                        "path": "x",
                        "patchSha256": digests[lane],
                        "createSeconds": 1.0,
                        "patchBytes": 100,
                        "createMetrics": {
                            "cpuSeconds": 1.0,
                            "allocatedBytes": 1,
                            "baseReads": 1,
                            "baseBytesRead": 1,
                            "baseSeeks": 1,
                        },
                    }
                ]
            }

        previous_invoke = MODULE.invoke
        names = ("GITHUB_RUN_ID", "GITHUB_RUN_NUMBER", "GITHUB_RUN_ATTEMPT")
        previous_env = {name: os.environ.get(name) for name in names}
        try:
            MODULE.invoke = fake_invoke
            os.environ["GITHUB_RUN_ID"] = "123"
            os.environ["GITHUB_RUN_NUMBER"] = "1"
            os.environ["GITHUB_RUN_ATTEMPT"] = "1"
            with tempfile.TemporaryDirectory() as directory:
                args = SimpleNamespace(
                    output=Path(directory) / "evidence",
                    run_id="run",
                    source_commit="a" * 40,
                    platform="linux-x64",
                    dataset_role="calibration",
                )
                document, _ = MODULE.run_dispatch(
                    args,
                    list(MODULE.FROZEN_PHASE_A),
                    1,
                )

            self.assertEqual(5, len(document["rounds"]))
            self.assertTrue(document["valid"])
            oracle = document["byteOracles"]["H7-L1-R2-E75==H4-L1-R2"]
            self.assertFalse(oracle["equal"])
            self.assertEqual(1, oracle["mismatchCount"])
        finally:
            MODULE.invoke = previous_invoke
            for name, value in previous_env.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value

    def test_result_validation_binds_dataset_source_execution_and_apply_mode(self):
        commit = "a" * 40
        args = SimpleNamespace(
            run_id=f"PATCH-ENC-005/RUN-20261001-001-{commit}-linux-x64",
            source_commit=commit,
        )
        result = {
            "schema": "chunkshift.patch-lab.v1",
            "lane": "H4-L1-R2",
            "runId": args.run_id,
            "execution": "h2-w2",
            "corpusPairsSha256": MODULE.FROZEN_DATASET_SHA256,
            "applyCheck": "boundary",
            "applyRepeats": 0,
            "environment": {"gitCommit": commit, "processorCount": 2},
        }
        MODULE.validate_result(args, "H4-L1-R2", result, apply=False)
        result["corpusPairsSha256"] = "0" * 64
        with self.assertRaises(ValueError):
            MODULE.validate_result(args, "H4-L1-R2", result, apply=False)


if __name__ == "__main__":
    unittest.main()
