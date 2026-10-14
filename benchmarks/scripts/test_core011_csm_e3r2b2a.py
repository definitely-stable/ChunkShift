"""Strict E3-R2-B2A no-decision and sampled peak negative tests."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]
SCRIPT=ROOT/"benchmarks/scripts/core011_csm_e3r2b2a.py"
SPEC=importlib.util.spec_from_file_location("e3r2b2a",SCRIPT)
m=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(m)
CONTRACT=ROOT/"benchmarks/experiments/core011-csm-e3r2b2a-peak.v1.json"


class FrozenPeakTests(unittest.TestCase):
    def test_registered_eager_only_contract_and_source(self):
        o=json.loads(CONTRACT.read_text())
        m.validate_contract(o)
        self.assertEqual(m.BASELINE_SOURCE_SHA,m.sha(ROOT/"src/ChunkShift/Manifest/CsmReader.cs"))
        self.assertFalse(o["decisionRunsAllowed"])

    def test_no_adoption_mutations(self):
        for key,value in (("variant","hybrid2"),("decisionRunsAllowed",True),
                          ("budgetsLocked",True),("parentEvidenceCommit","0"*40)):
            with self.subTest(key=key):
                o=json.loads(CONTRACT.read_text())
                o[key]=value
                with self.assertRaises(ValueError):m.validate_contract(o)

    def test_drifted_sampler_or_platform_rejected(self):
        for key,value in (("independentRunnerReplicates",[0]),
                          ("samplingPeriodMilliseconds",500),
                          ("perOpenedReaderCheckpoint",False),
                          ("readerCases",[0,1]),
                          ("naturalIdleSeconds",0),
                          ("minTraceSamples",1),
                          ("metrics",[])):
            with self.subTest(key=key):
                o=json.loads(CONTRACT.read_text())
                o[key]=value
                with self.assertRaises(ValueError):m.validate_contract(o)

    def test_invalid_metric_rejected(self):
        for x in (True,"10",None,-1,float("nan"),float("inf")):
            with self.subTest(x=x):
                with self.assertRaises(ValueError):m.number(x,"privateResident",positive=True)

    def test_historical_r0_decision_still_forbidden(self):
        d=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
        self.assertEqual("preregistration",m.r0.validate_contract(d)["stage"])
        with self.assertRaises(ValueError):m.r0.validate_contract(d,for_decision=True)

    def test_run_id_is_os_and_replica_scoped(self):
        good="CORE011-CSM-004/RUN-20261014-12345-abcdef12-windows-r1-x64"
        self.assertIsNotNone(m.RUNID.fullmatch(good))
        self.assertIsNone(m.RUNID.fullmatch(good.replace("-r1-","-r9-")))

    def test_actual_peak_recomputed_from_trace(self):
        # Synthetic validation oracle intentionally uses no OS measurements.
        desc={"manifestSha256":m.CSM_SHA,"manifestBytes":182366,
              "contentBytes":384*1048576,"chunkCount":5052,"cblkCount":2,
              "builderPid":100}
        base={"schema":"chunkshift.core011.e3r2b2a-sampled-residency.v1",
              "experimentId":"CORE011-CSM-004","runId":"valid","checkoutSha":"a"*40,
              "variant":"eager","role":"fresh-source-free-consumer",
              "decisionRunsAllowed":False,"productVerdict":"NOT_QUALIFIED",
              "heldReaders":0,"naturalIdleSeconds":30,"sampleIntervalTargetMs":100,
              "fixtureSha256":m.CSM_SHA,"csmBytes":182366,
              "sourceContentBytes":384*1048576,"chunkCount":5052,"cblkCount":2,
              "producerPid":100,"consumerPid":101,"architecture":"X64",
              "cpuModel":"Test CPU Model","os":"Synthetic OS","runtime":".NET 10",
              "processorCount":4,"heldStartMs":100,"heldEndMs":30100}
        trace=[]
        for i in range(310):
            trace.append({"phase":"held-natural-idle" if i<300 else "after-dispose-natural",
                "elapsedMs":i*100,"privateResidentBytes":100000+i*64,
                "processRssBytes":200000+i*64,"privateCommitBytes":None,
                "managedHeapBytes":50000,"gen2Collections":0,"totalGcPauseMs":0.0})
        base["trace"]=trace
        base["sampleCount"]=len(trace)
        base["sampledPrivateResidentHighWaterBytes"]=trace[-1]["privateResidentBytes"]
        base["sampledProcessRssHighWaterBytes"]=trace[-1]["processRssBytes"]
        self.assertEqual(310,m.validate_trace(base,desc,"a"*40,"valid","linux",0)["samples"])
        bad=copy.deepcopy(base)
        bad["sampledPrivateResidentHighWaterBytes"]-=1
        with self.assertRaisesRegex(ValueError,"High-water"):
            m.validate_trace(bad,desc,"a"*40,"valid","linux",0)


if __name__=="__main__":
    unittest.main()
