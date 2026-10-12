"""G1-G fail-closed source-variant and source-oracle tests (synthetic)."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

PATH = Path(__file__).with_name("core011_csm_g1g_ab.py")
SPEC = importlib.util.spec_from_file_location("core011_csm_g1g_ab", PATH)
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)
RUN = "CORE011-CSM-001/G1G-TEST"


def source():
    return ("using System.Buffers.Binary;\n"
            "class CsmStreamReaderCore {\n"
            + MOD.OLD_BUFFER + "\n"
            + MOD.NEW_LENGTH + "\n"
            + MOD.DISPOSE_ANCHOR + "\n}")


def make_matrix(folder):
    baseline = MOD.sha(source().encode())
    for vi, variant in enumerate(MOD.VARIANTS):
        out = folder / variant
        out.mkdir(parents=True)
        (out / "build.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1g-build.v1",
            "variant": variant, "sourceBaseSha256": baseline,
            "sourceVariantSha256":
                MOD.sha(MOD.variant_source(source(), variant).encode()),
            "headCommit": "locked-sha",
            "editScope": "temporary detached Git worktree; NEVER committed",
            "poolRetainedBucketsMax": 4 if variant == "pool4" else 0,
        }))
        for size in MOD.PLAN_SIZES:
            row = {
                "schema": "chunkshift.core011.csm-g1f-product-amdahl.v1",
                "experimentId": "CORE011-CSM-001", "runId": RUN,
                "verdict": "NO_VERDICT_EXPLORATORY",
                "conditions": {
                    "operation": "plan", "sizeMiB": size,
                    "rounds": MOD.ROUNDS, "repetitions": MOD.PLAN_REPS,
                    "workers": 1, "includeBlockIndex": True, "suite": "blake3",
                },
                "fixture": {
                    "baseContentSha256": "a"*64, "targetContentSha256": "b"*64,
                    "baseCsmSha256": "c"*64, "targetCsmSha256": "d"*64,
                    "patchSha256": "e"*64, "targetChunks": 11,
                    "reusedChunks": 10, "missingChunks": 1,
                },
                "estimatedPlanReaderBufferBudgetBytes": 295056,
                "postGcManagedBytes": 500000,
                "postGcLohLastGcBytes": 180000,
                "samples": [
                    {"roundNumber": i, "completedOperations": MOD.PLAN_REPS,
                     "processAllocatedBytes": 3200000 + vi*16000,
                     "batchWallMilliseconds": 22.0, "gen2": 0}
                    for i in range(1, MOD.ROUNDS+1)]
            }
            (out / f"plan-{size}.json").write_text(json.dumps(row))
        for size, workers in MOD.VERIFY_CELLS:
            count = 5052 if size == 384 else 11
            row = {
                "schema": "chunkshift.core011.csm-multiblock-metadata.v1",
                "experimentId": "CORE011-CSM-001", "runId": RUN,
                "verdict": "NO_VERDICT_EXPLORATORY",
                "conditions": {
                    "operation": "manifest-verify", "suite": "blake3",
                    "includeBlockIndex": True, "workers": workers,
                    "rounds": MOD.ROUNDS, "repetitions": MOD.VERIFY_REPS,
                },
                "fixture": {
                    "sizeMiB": size, "chunkCount": count,
                    "contentSha256": "f"*64,
                    "manifestSha256": MOD.MANIFEST_384_SHA if size == 384 else "e"*64,
                    "manifestBytes": 182366 if size == 384 else 830,
                    "minCblkBlocksBy4096EntryCap": 2 if size == 384 else 1,
                    "payloadCollectedBeforeMeasurement": True,
                },
                "retention": {
                    "managedBytesAfterExplicitGc": 700000,
                    "lastGcLohSizeAfterBytes": 900000,
                },
                "samples": [
                    {"round": i,
                     "completedOperations": workers*MOD.VERIFY_REPS,
                     "validatedChunks": workers*MOD.VERIFY_REPS*count,
                     "maxActiveOperations": min(workers, 4),
                     "processAllocatedBytes": (workers*MOD.VERIFY_REPS)*166000,
                     "wallMilliseconds": 25.0, "gen2Collections": 0}
                    for i in range(1, MOD.ROUNDS+1)]
            }
            (out / f"verify-{size}-w{workers}.json").write_text(json.dumps(row))


class G1gTests(unittest.TestCase):
    def test_variants_are_distinct(self):
        actual = [MOD.variant_source(source(), v) for v in MOD.VARIANTS]
        self.assertEqual(len(set(actual)), 3)
        self.assertIn("new byte[recordLength]", actual[1])
        self.assertNotIn("ArrayPool", actual[1])
        self.assertIn("maxArraysPerBucket: 4", actual[2])
        self.assertIn("clearArray: true", actual[2])
        self.assertEqual(source(), actual[0])

    def test_unknown_variant_fails(self):
        with self.assertRaisesRegex(ValueError, "unknown variant"):
            MOD.variant_source(source(), "unknown")

    def test_source_anchor_drift_fails(self):
        with self.assertRaisesRegex(ValueError, "layout drift"):
            MOD.variant_source("unexpected layout", "pool4")

    def test_complete_matrix(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            make_matrix(root)
            v = MOD.evaluate(root, RUN)
            self.assertEqual(v["matrixSize"], 18)
            self.assertEqual(v["roundCount"], 90)
            self.assertEqual(v["verdict"], "NO_VERDICT_EXPLORATORY")

    def mutate_and_reject(self, variant, filename, fn, message):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            make_matrix(root)
            path = root / variant / filename
            payload = json.loads(path.read_text())
            fn(payload)
            path.write_text(json.dumps(payload))
            with self.assertRaisesRegex(ValueError, message):
                MOD.evaluate(root, RUN)

    def test_missing_lanes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            make_matrix(root)
            (root / "lazy" / "verify-384-w16.json").unlink()
            with self.assertRaises(FileNotFoundError):
                MOD.evaluate(root, RUN)

    def test_corrupt_manifest_identity(self):
        self.mutate_and_reject(
            "pool4", "verify-384-w4.json",
            lambda a: a["fixture"].update(manifestSha256="a"*64),
            "multi-CBLK SHA drift")

    def test_incomplete_oracle(self):
        self.mutate_and_reject(
            "lazy", "verify-1-w1.json",
            lambda a: a["samples"][0].update(validatedChunks=7),
            "partial or missing public batch")

    def test_changed_patch_identity(self):
        self.mutate_and_reject(
            "lazy", "plan-8.json",
            lambda a: a["fixture"].update(patchSha256="0"*64),
            "physical identity")

    def test_rejects_broken_plan(self):
        self.mutate_and_reject(
            "pool4", "plan-1.json",
            lambda a: a["fixture"].update(reusedChunks=0),
            "bad Plan")

    def test_rejects_nan(self):
        self.mutate_and_reject(
            "eager", "plan-8.json",
            lambda a: a["samples"][3].update(batchWallMilliseconds=float("nan")),
            "invalid measurement")

    def test_rejects_false_adoption(self):
        self.mutate_and_reject(
            "lazy", "plan-1.json",
            lambda a: a.update(verdict="ADOPT"),
            "wrong run/corpus verdict")

    def test_rejects_different_source(self):
        self.mutate_and_reject(
            "lazy", "build.json",
            lambda a: a.update(sourceBaseSha256="0"*64),
            "different baseline")


if __name__ == "__main__":
    unittest.main()
