#!/usr/bin/env python3
"""T3-B0 independent fixture census checks; never opens sealed holdout."""
import copy
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import patch_tree_001_inventory as inv
import patch_tree_001_t3b0_census as b0


class ExactCensusTests(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root = Path(t.name)
        self.old = self.root / "old"
        self.new = self.root / "new"
        self.old.mkdir()
        self.new.mkdir()

    def add(self, side, relative, data):
        p = side / relative
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)

    def fixture(self):
        old = inv.layout(self.old)
        new = inv.layout(self.new)
        base = {x["path"]: x for x in old["files"]}
        target = {x["path"]: x for x in new["files"]}
        added = [dict(path=k, size=x["length"], sha256=x["sha256"])
                 for k, x in target.items() if k not in base]
        frozen = dict(
            added=added, removed=[k for k in base if k not in target],
            changed=[k for k in base if k in target and base[k]["sha256"] != target[k]["sha256"]],
            identicalFiles=sum(1 for k in base if k in target and
                               base[k]["sha256"] == target[k]["sha256"]))
        item = dict(family="fixture", baseVersion="before",
                    targetVersion="after", base=old, target=new)
        return item, frozen

    def test_real_exact_move_census_and_charged_reference(self):
        data = b"bin" * 8000
        self.add(self.old, "old/path", data)
        self.add(self.new, "new/path", data)
        item, frozen = self.fixture()
        row = b0.scan_pair(item, frozen)
        self.assertEqual((row["exactCandidateCount"], row["strictMoveCount"], row["mappedCount"]), (1, 1, 1))
        self.assertEqual(row["netSavedBytes"], len(data) - row["mapPhysicalBytes"])
        self.assertGreater(row["netSavedBytes"], 0)
        self.assertEqual(row["matches"][0]["sourcePath"], "old/path")

    def test_copy_not_misclassified_as_move(self):
        data = b"Z" * 1200
        self.add(self.old, "retained", data)
        self.add(self.new, "retained", data)
        self.add(self.new, "copy", data)
        item, frozen = self.fixture()
        row = b0.scan_pair(item, frozen)
        self.assertEqual(row["strictMoveCount"], 0)
        self.assertEqual(row["copyOrAmbiguousCount"], 1)
        self.assertEqual(row["mappedCount"], 1)

    def test_large_same_path_changes_remain_out_of_scope(self):
        self.add(self.old, "module", b"A" * 10000)
        self.add(self.new, "module", b"B" * 10000)
        item, frozen = self.fixture()
        row = b0.scan_pair(item, frozen)
        self.assertEqual(row["changedCount"], 1)
        self.assertEqual(row["mappedCount"], 0)
        self.assertEqual(row["netSavedBytes"], 0)

    def test_changed_rename_not_claimed_as_exact(self):
        self.add(self.old, "old", b"A" * 10000)
        self.add(self.new, "new", b"A" * 9999 + b"B")
        item, frozen = self.fixture()
        row = b0.scan_pair(item, frozen)
        self.assertEqual(row["strictMoveCount"], 0)
        self.assertEqual(row["exactCandidateCount"], 0)
        self.assertEqual(row["addedRawBytes"], 10000)

    def test_tiny_exact_match_is_observed_but_not_sent_as_reference(self):
        self.add(self.old, "x", b"K")
        self.add(self.new, "y", b"K")
        item, frozen = self.fixture()
        row = b0.scan_pair(item, frozen)
        self.assertEqual(row["exactCandidateCount"], 1)
        self.assertEqual(row["mappedCount"], 0)
        self.assertEqual(row["netSavedBytes"], 0)

    def test_source_either_removed_or_retained_is_classified(self):
        data = b"T" * 4096
        self.add(self.old, "old", data)
        self.add(self.old, "keep", data)
        self.add(self.new, "keep", data)
        self.add(self.new, "added", data)
        item, frozen = self.fixture()
        row = b0.scan_pair(item, frozen)
        self.assertEqual(row["strictMoveCount"], 1)
        self.assertEqual(row["copyOrAmbiguousCount"], 0)
        self.assertEqual(row["mappedCount"], 1)

    def test_forged_pair_metadata_fails_before_census(self):
        self.add(self.old, "src", b"C" * 500)
        self.add(self.new, "dst", b"C" * 500)
        item, frozen = self.fixture()
        bad = copy.deepcopy(frozen)
        bad["added"][0]["size"] = 1
        with self.assertRaisesRegex(ValueError, "original added raw size mismatch"):
            b0.scan_pair(item, bad)
        bad = copy.deepcopy(frozen)
        bad["added"].clear()
        with self.assertRaisesRegex(ValueError, "added path count mismatch"):
            b0.scan_pair(item, bad)

    def test_bad_hash_or_casefold_collision_rejected_by_delegate(self):
        self.add(self.old, "src", b"C" * 4000)
        self.add(self.new, "dst", b"C" * 4000)
        item, frozen = self.fixture()
        item["base"]["files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "layout identity mismatch"):
            b0.scan_pair(item, frozen)

    def test_no_materialized_sources_are_read_in_scan_pair(self):
        # The census itself scans verified inventories; input_lock separately
        # walks and hashes the exact original source trees on the hosted run.
        data = b"E" * 4096
        self.add(self.old, "old", data)
        self.add(self.new, "new", data)
        item, frozen = self.fixture()
        self.old.joinpath("old").unlink()
        self.new.joinpath("new").unlink()
        row = b0.scan_pair(item, frozen)
        self.assertEqual(row["mappedCount"], 1)

    def test_frozen_f1_control_ledger_is_immutable(self):
        f1 = json.loads(b0.F1.read_text(encoding="utf-8"))
        self.assertEqual(f1["f1"]["runId"], b0.EXPECTED_F1_RUN)
        self.assertEqual(f1["priorOriginalPhysical"]["runId"], b0.EXPECTED_T0_RUN)
        self.assertEqual(f1["totals"]["t0WarmBytes"], 14_758_982)
        self.assertEqual(f1["totals"]["t0RawAddedControlBytes"], 2_713_272)


if __name__ == "__main__":
    unittest.main()
