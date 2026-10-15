"""E3-R2C0 source/real Plan no decision fail-closed protocol tests."""
import importlib.util
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
p=ROOT/"benchmarks/scripts/core011_csm_e3r2c0.py"
spec=importlib.util.spec_from_file_location("real_plan_guard",p)
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
C=ROOT/"benchmarks/experiments/core011-csm-e3r2c0-real-plan.v1.json"


class RealVersionedPlanTests(unittest.TestCase):
    def test_frozen_original_eager_no_decision(self):
        o=json.loads(C.read_text())
        m.protocol(o)
        self.assertEqual(m.SOURCE_SHA,m.digest(ROOT/"src/ChunkShift/Manifest/CsmReader.cs"))
        self.assertFalse(o["decisionRunsAllowed"])

    def test_reject_candidate_unfreeze_and_source_corpus_change(self):
        for key,val in (("baselineVariant","hybrid2"),("budgetsLocked",True),
                        ("candidateRunsAllowed",True),("decisionRunsAllowed",True),
                        ("parentEvidenceCommit","0"*40)):
            with self.subTest(key=key):
                v=json.loads(C.read_text());v[key]=val
                with self.assertRaises(ValueError):m.protocol(v)

    def test_corpus_released_pair_only(self):
        for key,value in (("baseVersion","4.12.0"),
                          ("targetVersion","4.15.0"),
                          ("packageHashesIndependentlyPreFrozen",True),
                          ("materializeInSeparateExitedProcess",False)):
            with self.subTest(key=key):
                v=json.loads(C.read_text());v["corpus"][key]=value
                with self.assertRaises(ValueError):m.protocol(v)

    def test_external_no_internal_sampler_required(self):
        for key,val in (("internalNativeSampler",True),
                        ("iterations",4),("warmupCalls",0),
                        ("externalSampleMilliseconds",500),
                        ("naturalIdleMillisecondsMin",0)):
            with self.subTest(key=key):
                v=json.loads(C.read_text());v["workload"][key]=val
                with self.assertRaises(ValueError):m.protocol(v)

    def test_no_fake_quantitative_measurements(self):
        for value in ("1",True,float("nan"),float("inf"),-1,0):
            with self.subTest(value=value),self.assertRaises(ValueError):
                m.finite(value,"real Plan wall",positive=True)

    def test_prior_e3_r0_release_decision_forbidden(self):
        o=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        self.assertEqual("preregistration",m.r0.validate_contract(o)["stage"])
        with self.assertRaises(ValueError):m.r0.validate_contract(o,for_decision=True)

    def test_run_id_restriction(self):
        self.assertIsNotNone(m.RUNID.fullmatch(
            "CORE011-CSM-004/RUN-20261015-1111-abcdef12-windows-x64"))
        self.assertIsNone(m.RUNID.fullmatch("CORE011-CSM-004/RUN-20261015-linux"))


if __name__=="__main__":
    unittest.main()
