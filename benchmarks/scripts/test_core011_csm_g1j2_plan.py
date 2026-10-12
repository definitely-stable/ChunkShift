"""G1-J2 metadata/source/versioned Plan evidence negative tests (not CSM decoder tests)."""
import hashlib
import json
from pathlib import Path
from unittest import mock
import unittest
import tempfile

import core011_csm_g1j2_plan as g


RUN_ID = "CORE011-CSM-001/G1J2-CONTRACT-UNIT"
CHECKOUT = "a" * 40


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def build_fixture(root):
    fixture = root / "fixture"
    fixture.mkdir()
    source_inputs = {g.BASE_COMMIT: (b"A" * 1024, "1" * 40),
                     g.TARGET_COMMIT: (b"B" * 1400, "2" * 40)}
    versions = {}
    manifests = {}
    for name, rev in (("base", g.BASE_COMMIT), ("target", g.TARGET_COMMIT)):
        data, blob = source_inputs[rev]
        versions[name] = {"commit": rev, "blob": blob, "sha256": g.sha(data),
                          "bytes": len(data)}
        csm = (name + " pretend metadata").encode()
        (fixture / (name + ".csm")).write_bytes(csm)
        manifests[name] = {
            "manifestFile": name + ".csm",
            "csmSha256": g.sha(csm),
            "csmBytes": len(csm),
            "sourceSha256": g.sha(data),
            "sourceBytes": len(data),
            "chunkCount": 1,
            "cblkCount": 1,
            "manifestId": "c" * 64,
            "fileDigest": "d" * 64,
        }
    provenance = {
        "schema": "chunkshift.core011.g1j2-provenance.v1",
        "runId": RUN_ID, "origin": "git-versioned-source",
        "repository": "https://github.com/definitely-stable/ChunkShift",
        "sourcePath": g.SOURCE_PATH,
        "base": versions["base"], "target": versions["target"],
        "representativeDotNetBinaryPair": False,
    }
    write_json(fixture / "provenance.json", provenance)
    expected_plan = {"reusedChunks": 0, "missingChunks": 1,
                     "reusedBytes": 0, "missingBytes": 1400}
    descriptor = {
        "schema": g.SCHEMA, "runId": RUN_ID,
        "origin": "git-versioned-source",
        "verdict": "NO_VERDICT_EXPLORATORY",
        "provenanceSha256": g.sha((fixture / "provenance.json").read_bytes()),
        "builderCheckoutSha": CHECKOUT,
        "builderPid": 85001, "consumerSourceAccessAllowed": False,
        "base": manifests["base"], "target": manifests["target"],
        "expectedPlan": expected_plan,
    }
    write_json(fixture / "pair.json", descriptor)
    for i, v in enumerate(g.VARIANTS):
        folder = root / v
        folder.mkdir()
        write_json(folder / "build.json", {
            "schema": "chunkshift.core011.g1j1-build.v1",
            "variant": v, "checkoutSha": CHECKOUT,
            "originalReaderSha256": g.j1.SOURCE_SHA,
            "sourceSha256": ("%064x" % (i+1)),
            "sourceScope": "uncommitted temporary detached Git worktree",
        })
        write_json(folder / "plan.json", {
            "schema": g.SAMPLE_SCHEMA, "runId": RUN_ID,
            "experimentId": "CORE011-CSM-001",
            "verdict": "NO_VERDICT_EXPLORATORY",
            "checkoutSha": CHECKOUT, "builderPid": 85001,
            "consumerPid": 85002 + i, "sourceFilesAccessible": False,
            "baseCsmSha256": manifests["base"]["csmSha256"],
            "targetCsmSha256": manifests["target"]["csmSha256"],
            "reusedChunks": 0, "missingChunks": 1,
            "samples": [
                {"round": n, "calls": 4, "allocatedBytes": 100,
                 "batchWallMilliseconds": 0.5, "batchCpuMilliseconds": 0.3,
                 "gcPauseMilliseconds": 0, "gen2": 0} for n in (1, 2)
            ],
        })
    return source_inputs


def evaluator(root, inputs):
    def pinned(repo, rev):
        return inputs[rev]
    with mock.patch.object(g, "source_bytes", side_effect=pinned):
        return g.validate(root, RUN_ID, CHECKOUT, Path("."))


class J2ContractTests(unittest.TestCase):
    def test_five_separate_process_pair_complete(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            sources = build_fixture(root)
            result = evaluator(root, sources)
            self.assertEqual(result["caseCount"], 5)
            self.assertEqual(result["roundCount"], 10)
            self.assertEqual(result["verdict"], "NO_VERDICT_EXPLORATORY")

    def mutation(self, relative, transform, pattern):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            sources = build_fixture(root)
            path = root / relative
            data = json.loads(path.read_text())
            transform(data)
            write_json(path, data)
            with self.assertRaisesRegex(ValueError, pattern):
                evaluator(root, sources)

    def test_reject_false_adopt(self):
        self.mutation("hybrid2/plan.json",
                      lambda x: x.update(verdict="ADOPT"),
                      "identity mismatch")

    def test_reject_duplicate_pid(self):
        self.mutation("hybrid2/plan.json",
                      lambda x: x.update(consumerPid=85002),
                      "identity mismatch")

    def test_reject_builder_pid_used_as_consumer(self):
        self.mutation("lazy/plan.json",
                      lambda x: x.update(consumerPid=85001),
                      "identity mismatch")

    def test_reject_garbled_source_digest(self):
        self.mutation("fixture/provenance.json",
                      lambda x: x["base"].update(sha256="0"*64),
                      "provenance or decision guard")

    def test_reject_wrong_git_blob(self):
        self.mutation("fixture/provenance.json",
                      lambda x: x["base"].update(blob="a"*40),
                      "provenance or decision guard")

    def test_reject_nonhex_file_digest(self):
        self.mutation("fixture/pair.json",
                      lambda x: x["base"].update(fileDigest="ChunkShift.Primitives.Hash256"),
                      "noncanonical")

    def test_reject_plan_conservation_mismatch(self):
        self.mutation("fixture/pair.json",
                      lambda x: x["expectedPlan"].update(missingChunks=7),
                      "target conservation")

    def test_reject_missing_round(self):
        self.mutation("hybrid4/plan.json",
                      lambda x: x["samples"].pop(),
                      "partial")

    def test_reject_source_still_on_disk(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            sources = build_fixture(root)
            (root/"fixture"/"base.input").write_bytes(b"leaked")
            with self.assertRaisesRegex(ValueError, "leaked"):
                evaluator(root, sources)

    def test_reject_tampered_physical_csm(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            sources = build_fixture(root)
            (root/"fixture"/"base.csm").write_bytes(b"mutated")
            with self.assertRaisesRegex(ValueError, "digest mismatch"):
                evaluator(root, sources)

    def test_git_revisions_are_fixed(self):
        with self.assertRaisesRegex(ValueError, "unregistered"):
            g.source_bytes(Path("."), "unknown")


if __name__ == "__main__":
    unittest.main()
