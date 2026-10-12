"""J1 negative gates: pinned reader variants, five real process identities, CSM digest."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SOURCE = Path(__file__).with_name("core011_csm_g1j1_isolation.py")
SPEC = importlib.util.spec_from_file_location("g1j1", SOURCE)
m = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(m)
RUN_ID = "CORE011-CSM-001/G1J1-SYNTHETIC"
CHECKOUT = "a" * 40


def sample(round_no, count):
    return {
        "round": round_no, "completedOperations": count,
        "validatedChunks": count * 11, "maxActiveOperations": 1,
        "batchWallMilliseconds": 2.5, "allocatedBytes": 5000,
        "gcPauseMilliseconds": 0.0,
    }


def make(root):
    import hashlib
    fixture = root / "fixture"
    fixture.mkdir()
    csm = b"unit-only-payload-not-an-actual-csm"
    (fixture / "original.csm").write_bytes(csm)
    descriptor = {
        "schema": "chunkshift.core011.g1j-fixture.v1",
        "kind": "manifest-only-verify", "builderProcessRole": "external-fixture-builder",
        "consumerMayGenerateContent": False, "provenance": "synthetic",
        "corpusId": "g1j1-unit", "manifestFile": "original.csm",
        "manifestSha256": hashlib.sha256(csm).hexdigest(),
        "sourceSha256": "b" * 64, "manifestBytes": len(csm),
        "chunkCount": 11, "cblkCount": 1,
        "builderCheckoutSha": CHECKOUT, "builderPid": 90001,
        "contentBytes": 1048576, "runId": RUN_ID,
        "verdict": "NO_VERDICT_EXPLORATORY",
    }
    (fixture / "fixture.json").write_text(json.dumps(descriptor))
    source = Path(m.READER).read_text()
    for index, variant in enumerate(m.VARIANTS):
        location = root / variant
        location.mkdir()
        (location / "build.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j1-build.v1",
            "variant": variant, "checkoutSha": CHECKOUT,
            "originalReaderSha256": m.SOURCE_SHA,
            "sourceSha256": m.sha(m.candidate_source(source, variant).encode()),
            "sourceScope": "uncommitted temporary detached Git worktree",
        }))
        (location / "isolated.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j1-isolated-consumer.v1",
            "experimentId": "CORE011-CSM-001", "runId": RUN_ID,
            "verdict": "NO_VERDICT_EXPLORATORY", "checkoutSha": CHECKOUT,
            "fixtureCsmSha256": descriptor["manifestSha256"],
            "fixtureSourceSha256": descriptor["sourceSha256"],
            "consumerPid": 90002 + index, "fixtureBuilderPid": 90001,
            "consumerHasSourceFile": False, "consumerGeneratedContent": False,
            "manifestBytes": len(csm), "chunkCount": 11, "cblkCount": 1,
            "contentBytes": 1048576, "workers": 1,
            "rounds": 2, "repetitions": 2,
            "samples": [sample(1, 2), sample(2, 2)],
        }))
    return descriptor


class J1Tests(unittest.TestCase):
    def test_five_distinct_frozen_sources(self):
        src = Path(m.READER).read_text()
        self.assertEqual(m.sha(src.encode()), m.SOURCE_SHA)
        signatures = {m.sha(m.candidate_source(src, v).encode()) for v in m.VARIANTS}
        self.assertEqual(len(signatures), 5)
        self.assertEqual(m.candidate_source(src, "eager"), src)
        with self.assertRaisesRegex(ValueError, "reader SHA drift"):
            m.candidate_source("drift", "hybrid2")

    def test_synthetic_complete_isolation_matrix(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            make(root)
            got = m.evaluate(root, RUN_ID, CHECKOUT, 1, 1)
            self.assertEqual(got["caseCount"], 5)
            self.assertEqual(got["roundCount"], 10)
            self.assertEqual(got["verdict"], "NO_VERDICT_EXPLORATORY")

    def mutate(self, relative, fn, pattern):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            make(root)
            path = root / relative
            data = json.loads(path.read_text())
            fn(data)
            path.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, pattern):
                m.evaluate(root, RUN_ID, CHECKOUT, 1, 1)

    def test_same_pid_as_builder_is_rejected(self):
        self.mutate("hybrid2/isolated.json",
                    lambda d: d.update(consumerPid=90001),
                    "process/source isolation")

    def test_reused_consumer_pid_is_rejected(self):
        self.mutate("hybrid2/isolated.json",
                    lambda d: d.update(consumerPid=90002),
                    "process/source isolation")

    def test_reader_drift_is_rejected(self):
        self.mutate("hybrid4/build.json",
                    lambda d: d.update(originalReaderSha256="0"*64),
                    "source build")

    def test_incomplete_round_rejected(self):
        self.mutate("lazy/isolated.json",
                    lambda d: d["samples"].pop(),
                    "missing consumer rounds")

    def test_false_adopt_is_rejected(self):
        self.mutate("eager/isolated.json",
                    lambda d: d.update(verdict="ADOPT"),
                    "process/source isolation")

    def test_source_remnant_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            make(root)
            (root/"fixture"/"source.bin").write_bytes(b"unsafe")
            with self.assertRaisesRegex(ValueError, "original content"):
                m.evaluate(root, RUN_ID, CHECKOUT, 1, 1)

    def test_mutated_physical_csm_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            make(root)
            content = (root/"fixture"/"original.csm").read_bytes()
            (root/"fixture"/"original.csm").write_bytes(b"x"*len(content))
            with self.assertRaisesRegex(ValueError, "physical CSM"):
                m.evaluate(root, RUN_ID, CHECKOUT, 1, 1)


if __name__ == "__main__":
    unittest.main()
