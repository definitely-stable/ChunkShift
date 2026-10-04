import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("evaluate_patch_enc_005_h6o_guard.py")
SPEC = importlib.util.spec_from_file_location("h6o_guard_eval", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class H6OGuardEvaluatorTests(unittest.TestCase):
    SOURCE = "a" * 40
    RUN_ID = f"PATCH-ENC-005/RUN-20261004-007-{SOURCE}-linux-x64"

    @staticmethod
    def file_key():
        return ("node-linux-x64", "1", "2", "a.bin")

    def patch_row(self, sha):
        f,b,t,p = self.file_key()
        return {"family":f,"base":b,"target":t,"path":p,"patchSha256":sha}

    def correctness_row(self, sha):
        f,b,t,p = self.file_key()
        return {
            "family":f,"base":b,"target":t,"path":p,
            "patchSha256":sha,"targetSha256":"c"*64,
            "decoderOutputSha256":"c"*64,"verdict":"valid",
        }

    def paired(self, h0=1000, h6=970, ratios=None):
        ratios = ratios or [1.4,1.5,1.49,1.48,1.6]
        rounds=[]
        for i,ratio in enumerate(ratios,1):
            rounds.append({
                "round":i,"candidateOrder":[MODULE.LANE],"bracketNoisy":False,
                "h0Start":{"wallSeconds":10.0,"patchBytes":h0},
                "h0End":{"wallSeconds":10.0,"patchBytes":h0},
                "candidates":[{
                    "lane":MODULE.LANE,"wallRatio":ratio,"cpuRatio":1.0,
                    "aggregate":{
                        "wallSeconds":10.0*ratio,"patchBytes":h6,
                        "selectorFiles":1,
                        "selectorBuildWallSeconds":0.1,
                        "selectorBuildCpuSeconds":0.1,
                        "selectorBytesScanned":2048,
                        "selectorPostings":1,
                        "selectorMaxPostings":1,
                        "selectorIndexPeakBytes":1024,
                    },
                }],
            })
        h0sha="1"*64; h6sha="2"*64
        return {
            "schema":"chunkshift.patch-enc-005-paired.v2","status":"VALID",
            "experimentId":MODULE.EXP,"runId":self.RUN_ID,
            "protocolCommit":MODULE.PROTO,"sourceCommit":self.SOURCE,
            "platform":MODULE.PLATFORM,"datasetRole":MODULE.ROLE,
            "datasetSha256":MODULE.DATA,
            "acceptedTiming":{"attempt":1,"rounds":rounds,"summaries":[{
                "lane":MODULE.LANE,"wallRatios":ratios,"cpuRatios":[1]*5,
                "wallRatioMedian":sorted(ratios)[2],"cpuRatioMedian":1.0,
            }]},
            "acceptedPatchShas":{
                "csp":[self.patch_row(h0sha)],
                MODULE.LANE:[self.patch_row(h6sha)],
            },
            "traceCorrectness":{
                "csp":{"correctness":[self.correctness_row(h0sha)]},
                MODULE.LANE:{"correctness":[self.correctness_row(h6sha)]},
            },
            "applyEvidence":{
                "csp":{"files":[{**self.patch_row(h0sha),"samples":[{} for _ in range(5)]}]},
                MODULE.LANE:{"files":[{**self.patch_row(h6sha),"samples":[{} for _ in range(5)]}]},
            },
        }

    def memory(self, lane, peak, idle=1000):
        f,b,t,p=self.file_key()
        return {
            "schema":"chunkshift.patch-lab-memory.v1","runId":self.RUN_ID,
            "lane":lane,"corpusPairsSha256":MODULE.DATA,
            "environment":{"gitCommit":self.SOURCE},
            "execution":"h2-w2","population":"max-base-target","applyCheck":"boundary",
            "idleBaselineBytes":idle,
            "files":[{
                "family":f,"base":b,"target":t,"path":p,
                "baseSize":2_000_000,"targetSize":2_000_000,
                "createPeakBytes":peak,"applyPeakBytes":2000,
            }],
        }

    def test_exact_frozen_boundaries_pass(self):
        p=self.paired()
        h0=self.memory("csp",2000)
        h6=self.memory(MODULE.LANE,1000+MODULE.CREATE_LIMIT)
        v=MODULE.evaluate(p,h0,h6)
        self.assertEqual("PASS",v["status"])
        self.assertEqual("OPEN_FULL_PHASE_B",v["nextAction"])
        self.assertTrue(v["bytesOk"])
        self.assertTrue(v["wallOk"])
        self.assertEqual(4,v["wallPassCount"])
        self.assertTrue(v["memoryOk"])

    def test_one_byte_over_size_gate_misses(self):
        v=MODULE.evaluate(
            self.paired(h0=10000,h6=9701),
            self.memory("csp",2000),
            self.memory(MODULE.LANE,2000),
        )
        self.assertEqual("MISS",v["status"])
        self.assertFalse(v["bytesOk"])
        self.assertEqual("STOP_RESEMBLANCE",v["nextAction"])

    def test_only_three_wall_rounds_misses_even_when_median_passes(self):
        v=MODULE.evaluate(
            self.paired(ratios=[1.4,1.4,1.4,1.6,1.6]),
            self.memory("csp",2000),
            self.memory(MODULE.LANE,2000),
        )
        self.assertEqual(1.4,v["wallRatioMedian"])
        self.assertEqual(3,v["wallPassCount"])
        self.assertFalse(v["wallOk"])
        self.assertEqual("MISS",v["status"])

    def test_one_byte_over_memory_gate_misses(self):
        v=MODULE.evaluate(
            self.paired(),
            self.memory("csp",2000),
            self.memory(MODULE.LANE,1000+MODULE.CREATE_LIMIT+1),
        )
        self.assertFalse(v["memoryOk"])
        self.assertEqual("MISS",v["status"])

    def test_corrupt_decoder_verdict_is_rejected(self):
        p=self.paired()
        p["traceCorrectness"][MODULE.LANE]["correctness"][0]["verdict"]="invalid"
        with self.assertRaisesRegex(ValueError,"decoder correctness failure"):
            MODULE.evaluate(
                p,self.memory("csp",2000),self.memory(MODULE.LANE,2000)
            )

    def test_per_file_posting_bound_is_rejected(self):
        p=self.paired()
        p["acceptedTiming"]["rounds"][0]["candidates"][0]["aggregate"]["selectorMaxPostings"] = MODULE.POSTING_LIMIT + 1
        with self.assertRaisesRegex(ValueError,"posting bound"):
            MODULE.evaluate(
                p,self.memory("csp",2000),self.memory(MODULE.LANE,2000)
            )

    def test_apply_patch_sha_mismatch_is_rejected(self):
        p=self.paired()
        p["applyEvidence"][MODULE.LANE]["files"][0]["patchSha256"]="3"*64
        with self.assertRaisesRegex(ValueError,"apply evidence patch SHA mismatch"):
            MODULE.evaluate(
                p,self.memory("csp",2000),self.memory(MODULE.LANE,2000)
            )

    def test_index_logical_bound_is_rejected(self):
        p=self.paired()
        p["acceptedTiming"]["rounds"][0]["candidates"][0]["aggregate"]["selectorIndexPeakBytes"] = MODULE.INDEX_LIMIT + 1
        with self.assertRaisesRegex(ValueError,"index logical memory bound"):
            MODULE.evaluate(
                p,self.memory("csp",2000),self.memory(MODULE.LANE,2000)
            )


if __name__ == "__main__":
    unittest.main()
