"""E3-R2B immutable source/provenance and resident-metric fail-closed checks."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
script=ROOT/"benchmarks/scripts/core011_csm_e3r2b.py"
spec=importlib.util.spec_from_file_location("e3r2b_src",script)
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
CONTRACT=ROOT/"benchmarks/experiments/core011-csm-e3r2b-residency.v1.json"


class ResidencyGuardTests(unittest.TestCase):
    def test_frozen_source_and_no_adopt(self):
        contract=json.loads(CONTRACT.read_text())
        self.assertTrue(m.strict_contract(contract))
        self.assertEqual(m.SHA_READER,m.digest(ROOT/"src/ChunkShift/Manifest/CsmReader.cs"))
        self.assertFalse(contract["decisionRunsAllowed"])
        self.assertFalse(contract["budgetsLocked"])

    def test_eager_only_and_budgets_fail_closed(self):
        for key,bad in (("productionCandidate","hybrid2"),("decisionRunsAllowed",True),
                        ("budgetsLocked",True),("parentHead","0"*40)):
            with self.subTest(key=key):
                d=json.loads(CONTRACT.read_text())
                d[key]=bad
                with self.assertRaises(ValueError):m.strict_contract(d)

    def test_metrics_and_platform_guards_fail_closed(self):
        for field,value in (("hostedPlatforms",["windows-2025"]),
                            ("heldReaders",[0,4]),("mandatoryMetrics",[]),
                            ("windowsMetricSource","process-virtual-bytes"),
                            ("idleSecondsBySize",{"1":0,"384":30})):
            with self.subTest(field=field):
                d=json.loads(CONTRACT.read_text())
                d[field]=value
                with self.assertRaises(ValueError):m.strict_contract(d)

    def test_isolation_and_source_guards_fail_closed(self):
        d=json.loads(CONTRACT.read_text())
        d["strictSemantics"]["holdExactly16PartiallyRead"]=False
        with self.assertRaises(ValueError):m.strict_contract(d)
        d=json.loads(CONTRACT.read_text())
        d["forbidden"]["interpretPrivateMemorySizeAsResident"]=False
        with self.assertRaises(ValueError):m.strict_contract(d)

    def test_reject_invalid_residency_metrics(self):
        for value in (-1,True,float("nan"),float("inf"),None,"42"):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):m.nonnegative(value,"privateResidentBytes",positive=True)

    def test_registered_fixed_sample_phases(self):
        self.assertEqual(4,len(m.CELLS))
        self.assertEqual(2,len([x for x in m.CELLS if x[2]==300]))
        self.assertEqual([(1,0,30),(1,16,30),(384,0,300),(384,16,300)],list(m.CELLS))

    def test_decision_ready_remains_disabled(self):
        r0=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        self.assertEqual("preregistration",m.r0.validate_contract(r0)["stage"])
        with self.assertRaises(ValueError):m.r0.validate_contract(r0,for_decision=True)


if __name__=="__main__":
    unittest.main()
