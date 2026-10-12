"""G1-F full-matrix evidence validator regression tests, all synthetic."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

p = Path(__file__).with_name("core011_csm_product_amdahl.py")
spec = importlib.util.spec_from_file_location("core011_csm_product_amdahl", p)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
RUN = "CORE011-CSM-001/G1F-TEST"


def synthetic(folder):
    for size, op in mod.CASES:
        item = {
            "schema": mod.SCHEMA, "experimentId": "CORE011-CSM-001",
            "runId": RUN, "checkoutSha": "test", "verdict": "NO_VERDICT_EXPLORATORY",
            "conditions": {
                "sizeMiB": size, "operation": op, "rounds": 5, "repetitions": 4,
                "workers": 1, "suite": "blake3", "includeBlockIndex": True,
                "contentRetained": True,
                "allContentAndPatchPreparedOutsideTimedRegion": True,
                "applyUsesDiskPublication": True,
            },
            "fixture": {
                "baseContentSha256": "a"*64,
                "targetContentSha256": "b"*64,
                "baseCsmSha256": "c"*64,
                "targetCsmSha256": "d"*64,
                "patchSha256": "e"*64,
                "baseChunks": 20, "targetChunks": 20,
                "baseManifestBytes": 800, "targetManifestBytes": 850,
                "patchBytes": 1000, "reusedChunks": 18, "missingChunks": 2,
            },
            "estimatedPlanReaderBufferBudgetBytes": 295056 if op == "plan" else None,
            "postGcManagedBytes": 300000, "postGcLohLastGcBytes": 160000,
            "postGcWorkingSetBytes": 600000,
            "samples": [{
                "roundNumber": r, "completedOperations": 4,
                "batchWallMilliseconds": 3.4, "batchCpuMilliseconds": 3.2,
                "processAllocatedBytes": 1700000,
                "gen0": 1, "gen1": 1, "gen2": 1,
                "managedHeapAfterBytes": 350000,
                "workingSetAfterBytes": 800000,
                "lohSizeAfterLastGcBytes": 150000,
            } for r in range(1, 6)]
        }
        (folder / f"{size}mib-{op}.json").write_text(json.dumps(item))


class GateTests(unittest.TestCase):
    def run_mutation(self, size, op, mutate, message):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            synthetic(folder)
            path = folder / f"{size}mib-{op}.json"
            row = json.loads(path.read_text())
            mutate(row)
            path.write_text(json.dumps(row))
            with self.assertRaisesRegex(ValueError, message):
                mod.validate(folder, RUN)

    def test_pass_exact_matrix(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            synthetic(folder)
            result = mod.validate(folder, RUN)
            self.assertEqual(result["caseCount"], 8)
            self.assertEqual(result["roundCount"], 40)
            self.assertEqual(result["completedCalls"], 160)
            self.assertEqual(result["cases"][1]["hypotheticalPlanReaderBytes"], 295056)

    def test_wrong_identity_rejected(self):
        self.run_mutation(1, "create",
                          lambda x: x["fixture"].update(patchSha256="f"*64),
                          "inconsistent corpus")

    def test_missing_public_call_rejected(self):
        self.run_mutation(8, "plan",
                          lambda x: x["samples"][1].update(completedOperations=3),
                          "completed operation count")

    def test_bad_allocation_rejected(self):
        self.run_mutation(8, "verify-pair",
                          lambda x: x["samples"][0].update(processAllocatedBytes=-2),
                          "invalid processAllocatedBytes")

    def test_nonfinite_time_rejected(self):
        self.run_mutation(1, "apply",
                          lambda x: x["samples"][0].update(batchWallMilliseconds=float("nan")),
                          "invalid batchWallMilliseconds")

    def test_lost_reader_budget_rejected(self):
        self.run_mutation(1, "plan",
                          lambda x: x.update(estimatedPlanReaderBufferBudgetBytes=147528),
                          "hypothetical Plan")

    def test_invalid_patch_mix_rejected(self):
        self.run_mutation(8, "create",
                          lambda x: x["fixture"].update(missingChunks=0),
                          "mixed-reuse")

    def test_wrong_operation_rejected(self):
        self.run_mutation(8, "apply",
                          lambda x: x["conditions"].update(operation="plan"),
                          "product workload")

    def test_wrong_source_role_rejected(self):
        self.run_mutation(1, "plan",
                          lambda x: x["conditions"].update(contentRetained=False),
                          "product workload")

    def test_unregistered_verdict_rejected(self):
        self.run_mutation(1, "verify-pair",
                          lambda x: x.update(verdict="ADOPT"),
                          "schema/run/verdict")


if __name__ == "__main__":
    unittest.main()
