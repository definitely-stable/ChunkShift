"""G1-J preregistration, decision lock and manifest-only fixture guard tests."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("core011_csm_g1j_contract.py")
SPEC = importlib.util.spec_from_file_location("core011_csm_g1j_contract", SCRIPT)
j = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(j)
CONTRACT = Path("benchmarks/experiments/core011-csm-g1j-contract.v1.json")
SHA40 = "a" * 40


def load():
    return json.loads(CONTRACT.read_text(encoding="utf-8"))


def fixture(root: Path):
    csm = root / "original.csm"
    csm.write_bytes(b"CSM-v1-test-sentinel-not-a-real-CSM")
    return {
        "schema": j.FIXTURE_SCHEMA, "kind": "manifest-only-verify",
        "builderProcessRole": "external-fixture-builder",
        "consumerMayGenerateContent": False,
        "provenance": "synthetic", "corpusId": "g1j-contract-unit",
        "manifestFile": csm.name,
        "manifestSha256": hashlib.sha256(csm.read_bytes()).hexdigest(),
        "sourceSha256": hashlib.sha256(b"source-created-elsewhere").hexdigest(),
        "manifestBytes": csm.stat().st_size, "chunkCount": 1, "cblkCount": 1,
        "builderCheckoutSha": SHA40,
    }


class ContractTests(unittest.TestCase):
    def test_preregistration_is_valid_but_not_decision_ready(self):
        result = j.validate_contract(load())
        self.assertEqual(result["stage"], "preregistration")
        self.assertFalse(result["decisionRunsAllowed"])
        self.assertEqual(result["verdict"], "NO_VERDICT_EXPLORATORY")
        with self.assertRaisesRegex(ValueError, "decision runs forbidden"):
            j.validate_contract(load(), for_decision=True)

    def mutate(self, edit, pattern):
        d = copy.deepcopy(load())
        edit(d)
        with self.assertRaisesRegex(ValueError, pattern):
            j.validate_contract(d)

    def test_candidate_omission_fails(self):
        self.mutate(lambda d: d["candidates"].remove("lazy"), "variants")

    def test_unguarded_production_mutation_fails(self):
        self.mutate(lambda d: d["sourcePolicy"].update(
            productionSourceChangesAllowed=True), "production source")

    def test_source_history_in_consumer_fails(self):
        self.mutate(lambda d: d["processIsolation"].update(
            consumerMayCreateSourceBytes=True), "isolation")

    def test_false_adopt_fails(self):
        self.mutate(lambda d: d.update(verdict="ADOPT"), "premature")

    def test_prelock_numeric_budget_fails(self):
        self.mutate(lambda d: d["budgetPolicy"].update(
            cpuRatioUpperBound=1.05), "unlocked decision")

    def test_premature_decision_ready_fails(self):
        self.mutate(lambda d: d.update(
            stage="decision_ready", budgetsLocked=True,
            decisionRunsAllowed=True), "calibration/freeze")

    def test_unlocked_true_decision_flag_fails(self):
        self.mutate(lambda d: d.update(decisionRunsAllowed=True), "unlocked")

    def test_tampered_replication_fails(self):
        self.mutate(lambda d: d.update(experimentalCpuRunnersMinimum=1),
                    "replication")

    def test_metric_removed_fails(self):
        self.mutate(lambda d: d["metrics"].remove("gcPauseMilliseconds"),
                    "measurement")

    def test_budget_stage_can_only_unlock_with_complete_freeze(self):
        d = load()
        d.update(stage="decision_ready", decisionRunsAllowed=True, budgetsLocked=True)
        for key in j.NUMERIC_BUDGETS:
            d["budgetPolicy"][key] = 1.0
        d["budgetPolicy"]["confidenceLevel"] = 0.95
        d["budgetPolicy"]["independentBlocksMinimum"] = 4
        d["budgetPolicy"]["baselineCalibrationEvidenceId"] = (
            "CORE011-CSM-001/EVIDENCE-20990101-001")
        d["budgetPolicy"]["freezeCommitSha"] = SHA40
        self.assertTrue(j.validate_contract(d, for_decision=True)["decisionRunsAllowed"])
        d["budgetPolicy"]["gcPauseRatioUpperBound"] = float("nan")
        with self.assertRaisesRegex(ValueError, "missing/nonfinite"):
            j.validate_contract(d, for_decision=True)


class FixtureTests(unittest.TestCase):
    def test_external_fixture_metadata_and_digest(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data = fixture(root)
            result = j.validate_manifest_only_fixture(data, root)
            self.assertEqual(result["manifestSha256"], data["manifestSha256"])
            self.assertIn("not proved", result["note"])

    def test_mutated_physical_csm_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data = fixture(root)
            (root / data["manifestFile"]).write_bytes(b"x" * data["manifestBytes"])
            with self.assertRaisesRegex(ValueError, "physical CSM"):
                j.validate_manifest_only_fixture(data, root)

    def test_same_process_content_construction_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data = fixture(root)
            data["consumerMayGenerateContent"] = True
            with self.assertRaisesRegex(ValueError, "source content"):
                j.validate_manifest_only_fixture(data, root)

    def test_unpinned_provenance_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data = fixture(root)
            data["builderCheckoutSha"] = "not-commit"
            with self.assertRaisesRegex(ValueError, "builder revision"):
                j.validate_manifest_only_fixture(data, root)

    def test_parent_path_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data = fixture(root)
            data["manifestFile"] = "../original.csm"
            with self.assertRaisesRegex(ValueError, "unsafe fixture"):
                j.validate_manifest_only_fixture(data, root)

    def test_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data = fixture(root)
            (root / "link.csm").symlink_to(root / "original.csm")
            data["manifestFile"] = "link.csm"
            with self.assertRaisesRegex(ValueError, "symlinked"):
                j.validate_manifest_only_fixture(data, root)

    def test_manifest_is_only_digest_gate_not_parser(self):
        """This deliberately validates SHA, not real CSM semantics."""
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data = fixture(root)
            result = j.validate_manifest_only_fixture(data, root)
            self.assertNotIn("validCsm", result)


if __name__ == "__main__":
    unittest.main()
