"""G1-H hybrid buffer seam, exact-case matrix and A/B/A corruption tests."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

P=Path(__file__).with_name("core011_csm_g1h_hybrid.py")
S=importlib.util.spec_from_file_location("core011_g1h",P)
h=importlib.util.module_from_spec(S)
S.loader.exec_module(h)
RUN="CORE011-CSM-001/G1H-SYNTHETIC"


def dummy():
    return ("using System.Buffers.Binary;\n"
            "internal class CsmStreamReaderCore {\n"
            +h.g1g.OLD_BUFFER+"\n"
            +h.g1g.NEW_LENGTH+"\n"
            +h.g1g.DISPOSE_ANCHOR+"\n}")


def payload(kind,size,workers,parent_run):
    child=parent_run.replace("/G1H-","/G1F-" if kind=="plan" else "/G1E-",1)
    if kind=="plan":
        return {
            "schema":"chunkshift.core011.csm-g1f-product-amdahl.v1",
            "experimentId":"CORE011-CSM-001","runId":child,
            "verdict":"NO_VERDICT_EXPLORATORY",
            "conditions":{"sizeMiB":size,"operation":"plan","rounds":5,
                          "repetitions":8,"workers":1,"suite":"blake3",
                          "includeBlockIndex":True},
            "fixture":{"baseContentSha256":"a"*64,"targetContentSha256":"b"*64,
                       "baseCsmSha256":"c"*64,"targetCsmSha256":"d"*64,
                       "patchSha256":"e"*64,"targetChunks":11,
                       "reusedChunks":10,"missingChunks":1},
            "samples":[{"roundNumber":i,"completedOperations":8,
                        "processAllocatedBytes":800000,
                        "batchWallMilliseconds":2,"batchCpuMilliseconds":2,
                        "gen2":0} for i in range(1,6)],
            "postGcManagedBytes":600000,
        }
    chunks=5052 if size==384 else 11
    return {
        "schema":"chunkshift.core011.csm-multiblock-metadata.v1",
        "experimentId":"CORE011-CSM-001","runId":child,
        "verdict":"NO_VERDICT_EXPLORATORY",
        "conditions":{"operation":"manifest-verify","rounds":5,
                      "repetitions":16,"workers":workers,"suite":"blake3",
                      "includeBlockIndex":True},
        "fixture":{"sizeMiB":size,"chunkCount":chunks,
                   "payloadCollectedBeforeMeasurement":True,
                   "manifestBytes":182366 if size==384 else 830,
                   "manifestSha256":h.g1g.MANIFEST_384_SHA if size==384 else "f"*64,
                   "contentSha256":"0"*64,"minCblkBlocksBy4096EntryCap":2 if size==384 else 1},
        "samples":[{"round":i,"completedOperations":workers*16,
                    "validatedChunks":workers*16*chunks,
                    "maxActiveOperations":min(workers,4),
                    "processAllocatedBytes":workers*16*40000,
                    "wallMilliseconds":7,"processCpuMilliseconds":6,
                    "gen2Collections":0} for i in range(1,6)],
        "retention":{"managedBytesAfterExplicitGc":700000},
    }


def create(folder):
    for idx,v in enumerate(h.VARIANTS):
        directory=folder/v
        directory.mkdir(parents=True)
        (directory/"build.json").write_text(json.dumps({
            "schema":"chunkshift.core011.g1h-variant-build.v1",
            "variant":v,
            "sourceScope":"disposable uncommitted detached worktree",
            "sourceBaseSha256":h.g1g.sha(dummy().encode()),
            "sourceVariantSha256":h.g1g.sha(h.source_variant(dummy(),v).encode()),
            "headCommit":"synthetic",
            "largePoolRetainedPerBucket":4 if v in ("pool4","hybrid") else 0,
            "largePoolClearsOnReturn":v in ("pool4","hybrid"),
            "smallThresholdBytes":h.SMALL_LIMIT if v=="hybrid" else None,
        }))
        for kind,size,workers in h.CASES:
            file=directory/(f"plan-{size}.json" if kind=="plan" else f"verify-{size}-w{workers}.json")
            file.write_text(json.dumps(payload(kind,size,workers,RUN)))
    (folder/"pool-bucket.json").write_text(json.dumps({
        "schema":"chunkshift.core011.g1h-pool-probe.v1",
        "requestedBytes":147500,"retentionPerBucket":4,
        "rentedArrayLength":262144
    }))
    (folder/"repeats").mkdir()
    for size in (1,8):
        for sequence,order in enumerate(h.SEQUENCES):
            for position,v in enumerate(order):
                label=f"repeat-{size}-s{sequence}-p{position}"
                file=folder/"repeats"/(label+"-"+v+".json")
                file.write_text(json.dumps(payload("plan",size,1,RUN+"-"+label)))


class G1hTests(unittest.TestCase):
    def test_hybrid_source_safety_and_distinctness(self):
        variants=[h.source_variant(dummy(),v) for v in h.VARIANTS]
        self.assertEqual(len(set(variants)),4)
        self.assertIn("G1hSmallCblkMaximum = 80 * 1024",variants[3])
        self.assertIn("G1hLargePool.Instance.Rent(recordLength)",variants[3])
        self.assertIn("if (_g1hPooled)",variants[3])
        self.assertIn("clearArray: true",variants[3])
        self.assertNotIn("G1hLargePool",variants[1])
        self.assertNotIn("G1hLargePool",variants[0])

    def test_fail_closed_source_drift(self):
        with self.assertRaisesRegex(ValueError,"source anchor drift"):
            h.hybrid_source("unexpected reader")

    def test_all_36_processes(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            create(root)
            x=h.evaluate(root,RUN)
            self.assertEqual(x["matrixLanes"],24)
            self.assertEqual(x["repeatPlanLanes"],12)
            self.assertEqual(x["totalCases"],36)
            self.assertEqual(x["totalRounds"],180)
            self.assertEqual(x["verdict"],"NO_VERDICT_EXPLORATORY")

    def mutate(self,relative,mutation,diagnostic):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            create(root)
            file=root/relative
            data=json.loads(file.read_text())
            mutation(data)
            file.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,diagnostic):
                h.evaluate(root,RUN)

    def test_bad_pool_capacity(self):
        self.mutate("pool-bucket.json",
                    lambda x:x.update(rentedArrayLength=65536),
                    "bucket probe")

    def test_missing_hybrid_worker(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            create(root)
            (root/"hybrid"/"verify-384-w16.json").unlink()
            with self.assertRaises(FileNotFoundError):
                h.evaluate(root,RUN)

    def test_bad_large_sha(self):
        self.mutate("hybrid/verify-384-w16.json",
                    lambda x:x["fixture"].update(manifestSha256="a"*64),
                    "CSM SHA drift")

    def test_unverified_chunks(self):
        self.mutate("hybrid/verify-1-w1.json",
                    lambda x:x["samples"][0].update(validatedChunks=1),
                    "incomplete source-free")

    def test_cross_variant_patch_sha(self):
        self.mutate("hybrid/plan-8.json",
                    lambda x:x["fixture"].update(patchSha256="1"*64),
                    "physical identity diverges")

    def test_missing_repeat_call(self):
        self.mutate("repeats/repeat-8-s1-p1-eager.json",
                    lambda x:x["samples"][0].update(completedOperations=7),
                    "incomplete Plan")

    def test_changed_repeat_physical(self):
        self.mutate("repeats/repeat-1-s0-p1-hybrid.json",
                    lambda x:x["fixture"].update(targetCsmSha256="4"*64),
                    "A/B/A physical corpus")

    def test_mixed_checkout(self):
        self.mutate("lazy/build.json",
                    lambda x:x.update(headCommit="different"),
                    "different variant checkout")

    def test_wrong_retention_cap(self):
        self.mutate("hybrid/build.json",
                    lambda x:x.update(largePoolRetainedPerBucket=12),
                    "invalid source variant provenance")

    def test_nan_time(self):
        self.mutate("hybrid/plan-1.json",
                    lambda x:x["samples"][0].update(batchWallMilliseconds=float("nan")),
                    "nonfinite")

    def test_adopt_rejected(self):
        self.mutate("hybrid/plan-1.json",
                    lambda x:x.update(verdict="ADOPT"),
                    "premature verdict")


if __name__=="__main__":
    unittest.main()
