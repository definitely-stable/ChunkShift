"""CORE011 E3-R2-C1 source-corpus and active external Plan no-decision guards."""
import importlib.util
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
path=ROOT/"benchmarks/scripts/core011_csm_e3r2c1.py"
spec=importlib.util.spec_from_file_location("e3r2c1",path)
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
PROTOCOL=ROOT/"benchmarks/experiments/core011-csm-e3r2c1-corpus.v1.json"


class E3R2C1Guards(unittest.TestCase):
    def test_protocol_and_original_reader_pin(self):
        s=json.loads(PROTOCOL.read_text())
        m.check_protocol(s)
        self.assertEqual(m.digest(ROOT/"src/ChunkShift/Manifest/CsmReader.cs"),m.READER_SHA)
        self.assertFalse(s["decisionRunsAllowed"])

    def test_six_cases_and_stress_nonrelease_label(self):
        s=json.loads(PROTOCOL.read_text())
        self.assertEqual(len(s["cases"]),6)
        self.assertIn("not-a-single-real-file",s["cases"][-1]["sourceClass"])
        self.assertIn("not-a-released-dll",s["cases"][-3]["sourceClass"])
        self.assertEqual(s["cases"][-1]["expected"],"mixed-reuse-multiblock")

    def test_no_adopt_candidate_or_holdout_escape(self):
        for key,val in (
            ("candidateRunsAllowed",True),("decisionRunsAllowed",True),
            ("budgetsLocked",True),("productionSourceChangesAllowed",True),
            ("sourceOnlyInExitedBuilder",False),("consumerOriginalSourceForbidden",False),
            ("originalReaderSha256","0"*64)):
            with self.subTest(field=key):
                s=json.loads(PROTOCOL.read_text())
                s[key]=val
                with self.assertRaises(ValueError):m.check_protocol(s)

    def test_real_release_vs_derived_recipes_pinned(self):
        for k,v in (
            ("recipe","real-unfrozen-data"),("sourceClass","official-released-managed-pe-pair"),
            ("expected","zero-reuse-single-block")):
            s=json.loads(PROTOCOL.read_text())
            s["cases"][-1][k]=v
            with self.subTest(field=k),self.assertRaises(ValueError):
                m.check_protocol(s)

    def test_active_observer_time_and_memory_rules(self):
        for key,v in (
            ("activePlanMinimumMs",0),("externalObserverIntervalMs",1000),
            ("minExternalActiveSamples",0),("naturalIdleMinimumMs",20000),
            ("replicatesPerOs",4)):
            s=json.loads(PROTOCOL.read_text());s[key]=v
            with self.subTest(field=key),self.assertRaises(ValueError):
                m.check_protocol(s)

    def test_nonfinite_negative_and_string_counters_rejected(self):
        for v in (-1,False,float("nan"),float("inf"),"0",0):
            with self.subTest(v=v),self.assertRaises(ValueError):
                m.number(v,"privateBytes",positive=True)

    def test_preserves_e3r0_decision_gate(self):
        r=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        m.guard.validate_contract(r)
        with self.assertRaises(ValueError):m.guard.validate_contract(r,for_decision=True)


if __name__=="__main__":
    unittest.main()
