"""Fail-closed E3-R0 contract tests; never a reader implementation benchmark."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = Path(__file__).with_name("core011_csm_e3_guard.py")
SPEC = importlib.util.spec_from_file_location("e3_guard", SCRIPT)
g = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(g)
CONTRACT = ROOT / "benchmarks/experiments/core011-csm-e3-reader-contract.v1.json"


def load():
    return json.loads(CONTRACT.read_text(encoding="utf-8"))


class E3R0Tests(unittest.TestCase):
    def assert_rejected(self, edit, reason):
        obj = copy.deepcopy(load())
        edit(obj)
        with self.assertRaisesRegex(ValueError, reason):
            g.validate_contract(obj)

    def test_preregistration_passes_but_is_not_decision_ready(self):
        result = g.validate_contract(load())
        self.assertEqual(result["stage"], "preregistration")
        self.assertFalse(result["decisionRunsAllowed"])
        with self.assertRaisesRegex(ValueError, "unconditionally forbidden"):
            g.validate_contract(load(), for_decision=True)

    def test_baseline_reader_git_blob_pinned(self):
        obj = load()
        data = (ROOT / obj["baseline"]["readerPath"]).read_bytes()
        self.assertEqual(g.blob_sha1(data), g.READER_SHA1)
        g.validate_source_pin(obj, data)
        with self.assertRaisesRegex(ValueError, "reader source drift"):
            g.validate_source_pin(obj, data + b"\n# malicious edit\n")

    def test_historical_g1j_is_still_preregistration(self):
        g.check_historical_contract(ROOT)

    def test_stage_switch_and_fake_adopt_rejected(self):
        self.assert_rejected(lambda o: o.update(stage="decision_ready", decisionRunsAllowed=True,
                                                  budgetsLocked=True), "locked")
        self.assert_rejected(lambda o: o.update(status="ADOPT"), "locked")
        self.assert_rejected(lambda o: o.update(historicalReleaseDisposition="ADOPT"), "locked")

    def test_precommit_budget_and_fake_corpus_rejected(self):
        self.assert_rejected(lambda o: o["numericBudgets"].update(cpuRatioUpperBound=1.05),
                             "remain entirely unset")
        self.assert_rejected(lambda o: o.update(corpusFreezeSha256="0" * 64),
                             "remain entirely unset")
        self.assert_rejected(lambda o: o.update(baselineCalibrationEvidenceId="fake"),
                             "remain entirely unset")

    def test_missing_or_extra_budget_rejected(self):
        self.assert_rejected(lambda o: o["numericBudgets"].pop("gcPauseRatioUpperBound"),
                             "remain entirely unset")
        self.assert_rejected(lambda o: o["numericBudgets"].update(newGuard=None),
                             "remain entirely unset")

    def test_unregistered_candidate_or_pool_rejected(self):
        self.assert_rejected(lambda o: o["candidates"].remove("lazy"), "candidate selection")
        self.assert_rejected(lambda o: o["primaryCandidates"].remove("hybrid2"),
                             "candidate selection")
        self.assert_rejected(lambda o: o["poolCaps"].update(hybrid2=16), "candidate selection")

    def test_threshold_neighbors_and_large_block_controls_rejected_if_missing(self):
        self.assertEqual(44 + 36 * 2274, 81908)
        self.assertEqual(44 + 36 * 2275, 81944)
        self.assertEqual(44 + 36 * 4096, 147500)
        self.assert_rejected(lambda o: o["chunkCounts"].remove(2275), "CBLK boundary")
        self.assert_rejected(lambda o: o["chunkCounts"].remove(5052), "CBLK boundary")

    def test_source_free_invariant_rejected_if_weakened(self):
        self.assert_rejected(lambda o: o["processIsolation"].update(consumerGeneratesContent=True),
                             "process separation")
        self.assert_rejected(lambda o: o["processIsolation"].update(freshConsumerPerCell=False),
                             "process separation")

    def test_corruption_and_dispose_invariant_rejected_if_weakened(self):
        self.assert_rejected(lambda o: o["correctness"].update(wholeBlockPreExposure=False),
                             "oracle weakened")
        self.assert_rejected(lambda o: o["correctness"].update(fullCapacityClearOnReturn=False),
                             "oracle weakened")
        self.assert_rejected(lambda o: o["correctness"].update(cancellationAndDispose=False),
                             "oracle weakened")

    def test_metric_loss_or_duplicate_rejected(self):
        self.assert_rejected(lambda o: o["metrics"].remove("postIdlePrivateBytes"),
                             "memory and CPU")
        self.assert_rejected(lambda o: o["metrics"].append("allocatedBytesPerCall"),
                             "memory and CPU")
        self.assert_rejected(lambda o: o.update(idleObservationSeconds=[0]),
                             "memory and CPU")

    def test_release_gate_and_public_path_rejected_if_weakened(self):
        self.assert_rejected(lambda o: o["decisionRequirements"].update(
            realVersionedMixedReuseMultiCblk=False), "decision gate")
        self.assert_rejected(lambda o: o["decisionRequirements"].update(
            productionSourceChangesAllowed=True), "decision gate")
        self.assert_rejected(lambda o: o["publicPaths"].remove("patch-plan"),
                             "scenario matrix")

    def test_no_unknown_contract_keys(self):
        self.assert_rejected(lambda o: o.update(earlyAdopt=True), "unknown/missing")

    def test_baseline_anchor_cannot_be_changed_in_json(self):
        self.assert_rejected(lambda o: o["baseline"].update(
            readerGitBlobSha1="0" * 40), "baseline reader")


if __name__ == "__main__":
    unittest.main()
