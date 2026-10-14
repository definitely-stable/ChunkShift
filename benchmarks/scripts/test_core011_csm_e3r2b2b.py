"""R2B2B strict external-observer/no-decision contract and falsification tests."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
p=ROOT/"benchmarks/scripts/core011_csm_e3r2b2b.py"
spec=importlib.util.spec_from_file_location("b2b_tests",p)
b2b=importlib.util.module_from_spec(spec)
spec.loader.exec_module(b2b)
CONTRACT=ROOT/"benchmarks/experiments/core011-csm-e3r2b2b-observer.v1.json"


class ObserverChecks(unittest.TestCase):
    def test_preregistered_source_and_no_decision(self):
        spec=json.loads(CONTRACT.read_text())
        b2b.check_contract(spec)
        self.assertEqual(b2b.SOURCE_SHA,b2b.sha(ROOT/"src/ChunkShift/Manifest/CsmReader.cs"))
        self.assertFalse(spec["decisionRunsAllowed"])

    def test_no_posthoc_candidate_budgets_or_holdout(self):
        for key,bad in (("variant","hybrid2"),("decisionRunsAllowed",True),
                        ("budgetsLocked",True),("stage","decision-ready"),
                        ("parentEvidenceCommit","0"*40)):
            with self.subTest(key=key):
                s=json.loads(CONTRACT.read_text())
                s[key]=bad
                with self.assertRaises(ValueError): b2b.check_contract(s)

    def test_counterbalance_and_external_sampling_required(self):
        for key,bad in (("replicateOrder",{"0":["off-0"]}),
                        ("externalSamplerPeriodMs",500),
                        ("internalSampler",["on"]),("heldReaders",[0,4]),
                        ("replicates",[0]),("naturalIdleSeconds",1)):
            with self.subTest(key=key):
                s=json.loads(CONTRACT.read_text())
                s[key]=bad
                with self.assertRaises(ValueError):b2b.check_contract(s)

    def test_os_source_and_process_provenance_are_mandatory(self):
        for field in ("externalObserverDifferentPid","allTargetsFresh",
                      "rawTraceRequired","sourceShaLocked"):
            s=json.loads(CONTRACT.read_text())
            s["requirements"][field]=False
            with self.subTest(field=field),self.assertRaises(ValueError):
                b2b.check_contract(s)

    def test_no_inf_nan_or_zero_private_pages(self):
        for n in (float("inf"),float("nan"),-1,False,"1",None,0):
            with self.subTest(n=n),self.assertRaises(ValueError):
                b2b.num(n,"privateResident",positive=True)

    def test_frozen_order_balances_sampler_first_and_last(self):
        self.assertEqual(("off-0","on-0","on-16","off-16"),b2b.ORDER[0])
        self.assertEqual(("on-0","off-0","off-16","on-16"),b2b.ORDER[1])

    def test_original_e3_r0_decision_gate_is_closed(self):
        o=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        self.assertEqual("preregistration",b2b.r0.validate_contract(o)["stage"])
        with self.assertRaises(ValueError):b2b.r0.validate_contract(o,for_decision=True)


if __name__=="__main__":
    unittest.main()
