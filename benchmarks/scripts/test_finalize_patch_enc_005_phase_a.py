import importlib.util
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
    def test_patch_map_round_trips_dispatch_projection(self):
        document = {
            "patchShas": {
                "H4-L1-R2": [
                    {
                        "family": "f",
                        "base": "1",
                        "target": "2",
                        "path": "a",
                        "patchSha256": "a" * 64,
                    }
                ]
            }
        }
        maps = MODULE.patch_map_from_document(document)
        self.assertEqual("a" * 64, maps["H4-L1-R2"][("f", "1", "2", "a")])

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
        document = {
            "schema": "chunkshift.patch-enc-005-dispatch.v1",
            "attempt": 1,
            "experimentId": paired.EXPERIMENT_ID,
            "runId": run_id,
            "protocolCommit": paired.FROZEN_PROTOCOL_COMMIT,
            "sourceCommit": commit,
            "platform": "linux-x64",
            "datasetRole": "calibration",
            "datasetSha256": paired.FROZEN_DATASET_SHA256,
        }
        MODULE.validate_attempt(args, document, 1)
        document["datasetSha256"] = "0" * 64
        with self.assertRaises(ValueError):
            MODULE.validate_attempt(args, document, 1)


if __name__ == "__main__":
    unittest.main()
