import importlib.util
import os
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

SCRIPTS = Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

SCRIPT = SCRIPTS / "finalize_patch_enc_005_phase_a.py"
SPEC = importlib.util.spec_from_file_location("patch_enc_005_finalize", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class FinalizeTests(unittest.TestCase):
    def test_sha256_file_binds_raw_dispatch(self):
        import tempfile

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "dispatch.json"
            path.write_bytes(b'{"valid":true}\n')
            self.assertEqual(
                "701dad29c5f2e91a84aeb91db799c4d2dd64b630f5262541a4855e706e04f80a",
                MODULE.sha256_file(path),
            )


    def test_timing_environment_is_loaded_from_all_measured_runs(self):
        import json
        import tempfile

        commit = "a" * 40
        environment = {
            "osDescription": "Linux test",
            "osArchitecture": "X64",
            "processArchitecture": "X64",
            "frameworkDescription": ".NET 10.0.0 test",
            "processorCount": 4,
            "gitCommit": commit,
            "processorDescription": "test cpu",
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = root / "measured.json"
            result.write_text(
                json.dumps({"environment": environment}),
                encoding="utf-8",
            )
            rounds = []
            for index in range(5):
                rounds.append(
                    {
                        "round": index + 1,
                        "h0StartResult": "measured.json",
                        "h0EndResult": "measured.json",
                        "candidates": [
                            {"lane": lane, "result": "measured.json"}
                            for lane in MODULE.paired.FROZEN_PHASE_A
                        ],
                    }
                )
            dispatch = root / "dispatch.json"
            dispatch.write_text("{}", encoding="utf-8")

            actual = MODULE.timing_environment(
                dispatch,
                {"rounds": rounds},
                commit,
            )

            self.assertEqual(environment, actual)

    def test_timing_environment_rejects_mixed_runner_snapshot(self):
        import json
        import tempfile

        commit = "a" * 40
        base = {
            "osDescription": "Linux test",
            "osArchitecture": "X64",
            "processArchitecture": "X64",
            "frameworkDescription": ".NET 10.0.0 test",
            "processorCount": 4,
            "gitCommit": commit,
            "processorDescription": "test cpu",
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = root / "first.json"
            second = root / "second.json"
            first.write_text(json.dumps({"environment": base}), encoding="utf-8")
            second.write_text(
                json.dumps({"environment": {**base, "processorCount": 8}}),
                encoding="utf-8",
            )
            rounds = [
                {
                    "round": index + 1,
                    "h0StartResult": "first.json",
                    "h0EndResult": "second.json" if index == 4 else "first.json",
                    "candidates": [],
                }
                for index in range(5)
            ]
            dispatch = root / "dispatch.json"
            dispatch.write_text("{}", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "timing environment changed"):
                MODULE.timing_environment(dispatch, {"rounds": rounds}, commit)


    def test_patch_map_requires_exact_phase_a_lane_and_file_sets(self):
        row = {
            "family": "f",
            "base": "1",
            "target": "2",
            "path": "a",
            "patchSha256": "a" * 64,
        }
        lanes = ["csp", *MODULE.paired.FROZEN_PHASE_A]
        document = {"patchShas": {lane: [dict(row)] for lane in lanes}}
        maps = MODULE.patch_map_from_document(document)
        self.assertEqual("a" * 64, maps["H4-L1-R2"][("f", "1", "2", "a")])

        del document["patchShas"]["H9-L15-K4-C16-R1M"]
        with self.assertRaises(ValueError):
            MODULE.patch_map_from_document(document)

        document = {"patchShas": {lane: [dict(row)] for lane in lanes}}
        document["patchShas"]["H9-L12-K4-C16-R1M"][0]["path"] = "different"
        with self.assertRaises(ValueError):
            MODULE.patch_map_from_document(document)

    def test_skip_marker_validation_binds_identity(self):
        paired = MODULE.paired
        commit = "a" * 40
        run_id = f"PATCH-ENC-005/RUN-20261001-001-{commit}-linux-x64"
        args = SimpleNamespace(
            run_id=run_id,
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
            marker = {
                "schema": "chunkshift.patch-enc-005-retry.v1",
                "experimentId": paired.EXPERIMENT_ID,
                "runId": run_id,
                "attempt": 2,
                "protocolCommit": paired.FROZEN_PROTOCOL_COMMIT,
                "sourceCommit": commit,
                "platform": "linux-x64",
                "datasetRole": "calibration",
                "datasetSha256": paired.FROZEN_DATASET_SHA256,
                "githubRunId": "123",
                "githubRunNumber": "1",
                "githubRunAttempt": "1",
                "skipped": True,
                "reason": "attempt-1-valid",
            }
            MODULE.validate_skip_marker(args, marker)
            marker["sourceCommit"] = "b" * 40
            with self.assertRaises(ValueError):
                MODULE.validate_skip_marker(args, marker)
        finally:
            for name, value in previous.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value

    def test_attempt_validation_binds_identity(self):
        paired = MODULE.paired
        commit = "a" * 40
        run_id = f"PATCH-ENC-005/RUN-20261001-001-{commit}-linux-x64"
        args = SimpleNamespace(
            run_id=run_id,
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
            document = {
                "schema": "chunkshift.patch-enc-005-dispatch.v1",
                "attempt": 1,
                "valid": True,
                "invalidReason": None,
                "experimentId": paired.EXPERIMENT_ID,
                "runId": run_id,
                "protocolCommit": paired.FROZEN_PROTOCOL_COMMIT,
                "sourceCommit": commit,
                "platform": "linux-x64",
                "datasetRole": "calibration",
                "datasetSha256": paired.FROZEN_DATASET_SHA256,
                "githubRunId": "123",
                "githubRunNumber": "1",
                "githubRunAttempt": "1",
            }
            MODULE.validate_attempt(args, document, 1)
            document["datasetSha256"] = "0" * 64
            with self.assertRaises(ValueError):
                MODULE.validate_attempt(args, document, 1)
        finally:
            for name, value in previous.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value


if __name__ == "__main__":
    unittest.main()
