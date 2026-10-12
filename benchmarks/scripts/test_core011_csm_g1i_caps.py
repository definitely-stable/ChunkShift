"""G1-I deterministic two-host schedule, candidate source safety and evidence gates."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

P=Path(__file__).with_name("core011_csm_g1i_caps.py")
SPEC=importlib.util.spec_from_file_location("core011_g1i",P)
m=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(m)
RUN="CORE011-CSM-001/G1I-SYNTHETIC"


def make_synthetic(root,replicate):
    src=Path("src/ChunkShift/Manifest/CsmReader.cs").read_text(encoding="utf-8")
    assert m.g1g_sha_source(src) if hasattr(m,"g1g_sha_source") else m.h.g1g.sha(src.encode())==m.ORIGINAL_SOURCE_SHA
    for v in m.VARIANTS:
        out=root/v
        out.mkdir(parents=True)
        (out/"build.json").write_text(json.dumps({
            "schema":"chunkshift.core011.g1i-build.v1",
            "variant":v,"capacity":m.CAPACITY[v],
            "originalSourceSha256":m.ORIGINAL_SOURCE_SHA,
            "variantSourceSha256":m.h.g1g.sha(m.source_variant(src,v).encode()),
            "headCommit":"synthetic-sha",
            "sourceScope":"uncommitted disposable detached Git worktree",
            "smallBufferThresholdBytes":81920 if v!="eager" else None,
            "clearOnReturn":v!="eager",
        }))
        for kind,size,workers in m.CASES:
            count=5052 if size==384 else 11
            if kind=="plan":
                data={
                    "schema":"chunkshift.core011.csm-g1f-product-amdahl.v1",
                    "experimentId":"CORE011-CSM-001",
                    "runId":RUN.replace("/G1I-","/G1F-",1),
                    "checkoutSha":"synthetic-sha",
                    "verdict":"NO_VERDICT_EXPLORATORY",
                    "conditions":{"operation":"plan","sizeMiB":size,"rounds":5,
                                  "repetitions":8,"workers":1,"suite":"blake3",
                                  "includeBlockIndex":True},
                    "fixture":{"baseContentSha256":"a"*64,
                               "targetContentSha256":"b"*64,
                               "baseCsmSha256":"c"*64,
                               "targetCsmSha256":"d"*64,
                               "patchSha256":"e"*64,
                               "targetChunks":11,"reusedChunks":10,"missingChunks":1},
                    "samples":[{"roundNumber":i,"completedOperations":8,
                                "processAllocatedBytes":3600000,
                                "batchWallMilliseconds":2.0,
                                "batchCpuMilliseconds":1.9,"gen2":0}
                               for i in range(1,6)],
                    "postGcManagedBytes":600000,
                }
                name=f"plan-{size}.json"
            else:
                data={
                    "schema":"chunkshift.core011.csm-multiblock-metadata.v1",
                    "experimentId":"CORE011-CSM-001",
                    "runId":RUN.replace("/G1I-","/G1E-",1),
                    "checkoutSha":"synthetic-sha",
                    "verdict":"NO_VERDICT_EXPLORATORY",
                    "conditions":{"operation":"manifest-verify","rounds":5,
                                  "repetitions":16,"workers":workers,
                                  "suite":"blake3","includeBlockIndex":True},
                    "fixture":{"sizeMiB":size,
                               "payloadCollectedBeforeMeasurement":True,
                               "chunkCount":count,
                               "contentSha256":"f"*64,
                               "manifestSha256":(m.h.g1g.MANIFEST_384_SHA
                                                 if size==384 else "f"*64),
                               "manifestBytes":182366 if size==384 else 830,
                               "minCblkBlocksBy4096EntryCap":2 if size==384 else 1},
                    "samples":[{"round":i,"completedOperations":workers*16,
                                "validatedChunks":workers*16*count,
                                "maxActiveOperations":min(workers,4),
                                "processAllocatedBytes":workers*16*44000,
                                "wallMilliseconds":8.0,
                                "processCpuMilliseconds":7.0,
                                "gen2Collections":0}
                               for i in range(1,6)],
                    "retention":{"managedBytesAfterExplicitGc":650000},
                }
                name=f"verify-{size}-w{workers}.json"
            (out/name).write_text(json.dumps(data))
    (root/"pool-buckets.json").write_text(json.dumps({
        "schema":"chunkshift.core011.g1i-bucket-probe.v1",
        "requestedBytes":147500,
        "buckets":[{"cap":cap,"returnedLength":262144,
                    "theoreticalMaxCachedPayloadBytes":cap*262144}
                   for cap in (1,2,4)]
    }))
    (root/"order.json").write_text(json.dumps({
        "schema":"chunkshift.core011.g1i-order.v1",
        "runId":RUN,"replicate":replicate,
        "orderByCase":{
            f"{kind}-{size}-w{workers}":m.ordered_variants(i,replicate)
            for i,(kind,size,workers) in enumerate(m.CASES)}
    }))


class G1iValidationTests(unittest.TestCase):
    def test_four_source_distinct(self):
        s=Path("src/ChunkShift/Manifest/CsmReader.cs").read_text()
        h={v:m.h.g1g.sha(m.source_variant(s,v).encode()) for v in m.VARIANTS}
        self.assertEqual(len(set(h.values())),4)
        self.assertEqual(h["eager"],m.ORIGINAL_SOURCE_SHA)
        for c in (1,2,4):
            self.assertIn(f"maxArraysPerBucket: {c});",
                          m.source_variant(s,f"hybrid{c}"))

    def test_explicit_source_drift_gate(self):
        with self.assertRaisesRegex(ValueError,"frozen reader source"):
            m.source_variant("unexpected reader","hybrid2")

    def test_balanced_orders(self):
        for index in range(6):
            for replicate in (0,1):
                self.assertEqual(set(m.ordered_variants(index,replicate)),set(m.VARIANTS))
                self.assertEqual(len(m.ordered_variants(index,replicate)),4)
        self.assertNotEqual(m.ordered_variants(0,0),m.ordered_variants(0,1))

    def test_complete_matrix_each_host(self):
        for rep in (0,1):
            with self.subTest(replicate=rep),tempfile.TemporaryDirectory() as tmp:
                root=Path(tmp)
                make_synthetic(root,rep)
                value=m.evaluate(root,RUN,rep)
                self.assertEqual(value["caseCount"],24)
                self.assertEqual(value["roundCount"],120)
                self.assertEqual(len(value["matchedComparisons"]),6)
                self.assertEqual(value["verdict"],"NO_VERDICT_EXPLORATORY")

    def mutate(self,path,modify,pattern):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            make_synthetic(root,0)
            p=root/path
            value=json.loads(p.read_text())
            modify(value)
            p.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError,pattern):
                m.evaluate(root,RUN,0)

    def test_corrupt_large_csm_sha(self):
        self.mutate("hybrid1/verify-384-w16.json",
                    lambda x:x["fixture"].update(manifestSha256="9"*64),
                    "384MiB CSM SHA drift")

    def test_missing_round(self):
        self.mutate("hybrid2/plan-8.json",
                    lambda x:x["samples"].pop(),
                    "incomplete round count")

    def test_physical_patch_drift(self):
        self.mutate("hybrid2/plan-8.json",
                    lambda x:x["fixture"].update(patchSha256="1"*64),
                    "different physical manifest/CSP corpus")

    def test_incomplete_chunk_oracle(self):
        self.mutate("hybrid4/verify-1-w1.json",
                    lambda x:x["samples"][0].update(validatedChunks=0),
                    "incomplete source-free")

    def test_reader_source_drift(self):
        self.mutate("hybrid1/build.json",
                    lambda x:x.update(originalSourceSha256="0"*64),
                    "invalid source provenance")

    def test_forged_checkout(self):
        self.mutate("hybrid4/plan-8.json",
                    lambda x:x.update(checkoutSha="other"),
                    "checkout SHA differs")

    def test_forged_budget(self):
        self.mutate("hybrid1/build.json",
                    lambda x:x.update(capacity=44),
                    "invalid source provenance")

    def test_bad_buckets(self):
        self.mutate("pool-buckets.json",
                    lambda x:x["buckets"][1].update(returnedLength=0),
                    "invalid capacity|pool capacity")

    def test_mutated_order(self):
        self.mutate("order.json",
                    lambda x:x["orderByCase"].update({"plan-1-w1":["eager"]}),
                    "unfrozen experimental ordering")

    def test_nonfinite_wall(self):
        self.mutate("hybrid4/plan-1.json",
                    lambda x:x["samples"][0].update(batchWallMilliseconds=float("nan")),
                    "invalid or nonfinite")

    def test_adopt_rejected(self):
        self.mutate("hybrid1/plan-1.json",
                    lambda x:x.update(verdict="ADOPT"),
                    "premature verdict")

    def test_missing_lane(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            make_synthetic(root,0)
            (root/"eager"/"verify-384-w16.json").unlink()
            with self.assertRaises(FileNotFoundError):
                m.evaluate(root,RUN,0)


if __name__=="__main__":
    unittest.main()
