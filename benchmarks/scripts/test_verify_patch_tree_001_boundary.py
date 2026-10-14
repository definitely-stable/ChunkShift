#!/usr/bin/env python3
"""Adversarial synthetic contracts for exact T1 boundary-attribution archives."""
import copy
import hashlib
import importlib.util
import json
import unittest
from pathlib import Path

p = Path(__file__).with_name("verify_patch_tree_001_boundary.py")
spec = importlib.util.spec_from_file_location("tree_boundary_verifier", p)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def sha(s):
    return hashlib.sha256(s).hexdigest()


def layout(items):
    offset = 0
    records = []
    digest = hashlib.sha256()
    for path, data in sorted(items, key=lambda f: f[0].encode()):
        h = sha(data)
        b = path.encode()
        digest.update(len(b).to_bytes(4, "little"))
        digest.update(b)
        digest.update(len(data).to_bytes(8, "little"))
        digest.update(bytes.fromhex(h))
        records.append({"path": path, "offset": offset,
                        "length": len(data), "sha256": h})
        offset += len(data)
    return {"schema": "chunkshift.patch-tree-layout.v1",
            "order": "NFC UTF-8 byte order", "fileCount": len(records),
            "totalBytes": offset, "layoutSha256": digest.hexdigest(), "files": records}


def metric(n, b):
    return {"count": n, "bytes": b}


def fixture():
    old_a, new_a, old_only = b"00000000", b"11111111", b"ABCD"
    base = layout([("a", old_a), ("oldonly", old_only)])
    target = layout([("a", new_a), ("added", old_only)])
    pair = {
        "family": "dotnet-aspnetcore-win-x64", "base": "v1", "target": "v2",
        "changed": [{"path": "a", "baseSize": 8, "baseSha256": sha(old_a),
                     "targetSize": 8, "targetSha256": sha(new_a)}],
        "added": [{"path": "added", "size": 4, "sha256": sha(old_only)}],
        "removed": ["oldonly"], "identicalFiles": 0, "identicalBytes": 0,
    }
    def row(path, kind, old_len, target_len, available):
        m = metric
        return {"path": path, "kind": kind, "oldFileBytes": old_len,
                "targetBytes": target_len, "standaloneOld": m(int(old_len != 0), old_len),
                "oldPreservedSameOffset": m(int(old_len != 0), old_len),
                "oldAvailableAnywhereInVirtualTree": m(int(old_len != 0), old_len),
                "virtualContainedWithinFile": m(int(old_len != 0), old_len),
                "virtualCrossBoundaryOverlap": m(0, 0),
                "targetChunks": m(int(target_len != 0), target_len),
                "targetExactInSamePathOld": m(0, 0),
                "targetExactInVirtualTree": m(int(available), target_len if available else 0)}
    rows = [row("a", "changed", 8, 8, False),
            row("added", "added", 0, 4, True),
            row("oldonly", "removed", 4, 0, False)]
    doc = {"schema": module.SCHEMA, "status": module.STATUS,
           "pairsSha256": module.PAIRS_SHA, "runId": "synthetic",
           "pairs": [{"family": pair["family"], "baseVersion": "v1",
                      "targetVersion": "v2", "oldTreeManifestId": "a" * 64,
                      "oldTreeChunks": 2, "oldTreeBytes": 12, "files": rows}]}
    inv = {"schema": "chunkshift.patch-tree-inventory.v1",
           "status": "INVENTORY_ONLY_NOT_PATCH_EVIDENCE",
           "pairsSha256": module.PAIRS_SHA, "pairCount": 1,
           "pairs": [{"family": pair["family"], "baseVersion": "v1",
                      "targetVersion": "v2", "datasetRole": "calibration",
                      "base": base, "target": target}]}
    original = {"schema": "chunkshift.patch-pairs.v1", "pairs": [pair]}
    return doc, original, inv


class BoundaryEvidenceTests(unittest.TestCase):
    def test_valid_fixture(self):
        module.verify(*fixture())

    def test_reject_missing_or_extra_rows(self):
        for change in ("pop", "duplicate"):
            doc, original, inv = fixture()
            if change == "pop":
                doc["pairs"][0]["files"].pop()
            else:
                doc["pairs"][0]["files"].append(copy.deepcopy(doc["pairs"][0]["files"][-1]))
            with self.assertRaisesRegex(ValueError, "population|rows"):
                module.verify(doc, original, inv)

    def test_reject_fake_boundary_overlap(self):
        doc, original, inv = fixture()
        doc["pairs"][0]["files"][0]["virtualCrossBoundaryOverlap"] = metric(1, 1)
        with self.assertRaisesRegex(ValueError, "virtual source exact byte covering"):
            module.verify(doc, original, inv)

    def test_reject_invented_chunk_availability(self):
        doc, original, inv = fixture()
        doc["pairs"][0]["files"][0]["oldAvailableAnywhereInVirtualTree"] = metric(0, 0)
        with self.assertRaisesRegex(ValueError, "old identity containment"):
            module.verify(doc, original, inv)

    def test_reject_added_file_as_same_path_reuse(self):
        doc, original, inv = fixture()
        doc["pairs"][0]["files"][1]["targetExactInSamePathOld"] = metric(1, 4)
        with self.assertRaisesRegex(ValueError, "non-changed"):
            module.verify(doc, original, inv)

    def test_reject_changed_original_file(self):
        doc, original, inv = fixture()
        inv["pairs"][0]["base"]["files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "independent layout digest"):
            module.verify(doc, original, inv)

    def test_reject_heldout_family(self):
        doc, original, inv = fixture()
        doc["pairs"][0]["family"] = "node-win-x64"
        with self.assertRaisesRegex(ValueError, "identity"):
            module.verify(doc, original, inv)

    def test_reject_negative_and_boolean_metrics(self):
        for invalid in (-1, True):
            doc, original, inv = fixture()
            doc["pairs"][0]["files"][0]["targetChunks"]["bytes"] = invalid
            with self.assertRaisesRegex(ValueError, "unsigned integer"):
                module.verify(doc, original, inv)

    def test_reject_duplicate_json_property(self):
        with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
            module.strict_json(b'{"schema":"a","schema":"b"}')

    def test_reject_truncated_calibration_population(self):
        doc, original, inv = fixture()
        original["pairs"].append(copy.deepcopy(original["pairs"][0]))
        with self.assertRaisesRegex(ValueError, "population"):
            module.verify(doc, original, inv)


    def test_hosted_execution_is_main_only_and_artifacts_fail_closed(self):
        repository = Path(__file__).resolve().parents[2]
        workflow = (repository / ".github/workflows/patch-tree-001-t1-boundary-diagnostic.yml").read_text(encoding="utf-8")
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertNotIn("  pull_request:", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertIn('test "$RUNNER_ENVIRONMENT" = "github-hosted"', workflow)
        self.assertIn('test "$GITHUB_REF" = "refs/heads/main"', workflow)
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = "1"', workflow)
        self.assertIn('os.environ["SOURCE_SHA"] != os.environ["GITHUB_SHA"]', workflow)
        self.assertIn("--calibration-only", workflow)
        self.assertIn("patch-lab tree-boundary", workflow)
        self.assertNotIn("patch-lab tree-calibration", workflow)
        self.assertNotIn("patch-lab tree-evaluation", workflow)
        self.assertIn("verify_patch_tree_001_boundary.py", workflow)
        self.assertIn("if: success()", workflow)
        self.assertIn("if: failure()", workflow)
        self.assertIn("UNVERIFIED-NOT-DECISION.txt", workflow)
        self.assertIn("name: patch-tree-001-t1-boundary-diagnostic", workflow)
        locked = module.PAIRS_SHA
        self.assertEqual(1, workflow.count("  PAIRS_SHA256: " + locked))
        self.assertIn('echo "$PAIRS_SHA256  pairs.json" | sha256sum --check', workflow)

    def test_issue_owner_dispatch_is_exact_and_nonautomatic(self):
        repository = Path(__file__).resolve().parents[2]
        operator = (repository / ".github/workflows/patch-tree-001-t1-boundary-operator.yml").read_text(encoding="utf-8")
        self.assertIn("github.event.issue.number == 184", operator)
        self.assertIn("github.event.comment.user.id == 271974079", operator)
        self.assertIn("r\"/tree-boundary (20[0-9]{6}) ([1-9][0-9]{5,18})\"", operator)
        self.assertIn("inputs[source-sha]", operator)
        self.assertIn("source != main_head()", operator)
        self.assertIn('r.get("conclusion") == "success"', operator)
        self.assertIn("patch-tree-001-t1-boundary-diagnostic.yml", operator)


if __name__ == "__main__":
    unittest.main()
