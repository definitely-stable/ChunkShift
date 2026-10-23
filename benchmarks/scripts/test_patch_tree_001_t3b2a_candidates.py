#!/usr/bin/env python3
"""Unbiased B2-A preselection negative and determinism matrix. No originals."""
import copy
import json
import tempfile
import unittest
from pathlib import Path

import patch_tree_001_inventory as inv
import patch_tree_001_t3b2a_candidates as planner

N = 96 * 1024


class StructuralCandidatesTests(unittest.TestCase):
    def setUp(self):
        d = tempfile.TemporaryDirectory()
        self.addCleanup(d.cleanup)
        self.old = Path(d.name, "old")
        self.new = Path(d.name, "new")
        self.old.mkdir()
        self.new.mkdir()

    def put(self, root, path, blob=None):
        dst = root.joinpath(*path.split("/"))
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_bytes((b"X" * N) if blob is None else blob)

    def fixture(self):
        base, target = inv.layout(self.old), inv.layout(self.new)
        original, changed = {x["path"]: x for x in base["files"]}, []
        after = {x["path"]: x for x in target["files"]}
        for path in sorted(original.keys() & after.keys()):
            a, c = original[path], after[path]
            if a["sha256"] != c["sha256"]:
                changed.append(dict(path=path, baseSize=a["length"],
                                    baseSha256=a["sha256"], targetSize=c["length"],
                                    targetSha256=c["sha256"]))
        frozen = dict(
            added=[dict(path=name, size=after[name]["length"],
                        sha256=after[name]["sha256"])
                   for name in sorted(after.keys() - original.keys())],
            removed=sorted(original.keys() - after.keys()),
            changed=changed,
            identicalFiles=len([x for x in after.keys() & original.keys()
                                if x not in {c["path"] for c in changed}]),
            identicalBytes=sum(after[x]["length"]
                               for x in after.keys() & original.keys()
                               if x not in {c["path"] for c in changed}))
        return base, target, frozen

    def plan(self):
        return planner.select_pair(*self.fixture())

    def test_unique_versioned_changed_rename_produces_one_bounded_candidate(self):
        a, b = "bin/product_1.2.3.4.dll", "bin/product_1.2.4.0.dll"
        self.put(self.old, a)
        self.put(self.new, b, b"Z" * N)
        result = self.plan()
        self.assertEqual(result["selectedCount"], 1)
        self.assertEqual(result["fallBackCount"], 0)
        c = result["candidates"][0]
        self.assertEqual((c["sourcePath"], c["targetPath"]), (a, b))
        self.assertGreater(c["diagnosticLocatorFloorBytes"], len(a) + len(b))
        self.assertEqual(c["patchStatus"], "NOT_ATTEMPTED__REQUIRES_REAL_CSP_AND_COST_ADMISSION")

    def test_remains_visible_to_raw_fallback_if_filename_not_qualified(self):
        self.put(self.old, "lib/alpha.dll")
        self.put(self.new, "lib/beta.dll", b"Y" * N)
        result = self.plan()
        self.assertEqual((result["selectedCount"], result["fallBackCount"]), (0, 1))
        self.assertEqual(result["fallbackReasons"], {"nonversioned_filename": 1})

    def test_same_path_change_is_not_a_cross_path_candidate(self):
        self.put(self.old, "bin/app_1.0.0.0.dll")
        self.put(self.new, "bin/app_1.0.0.0.dll", b"Z" * N)
        result = self.plan()
        self.assertEqual(result["addedCount"], 0)
        self.assertEqual(result["selectedCount"], 0)

    def test_retain_old_file_means_copy_not_removed_base(self):
        self.put(self.old, "bin/app_1.0.0.0.dll")
        self.put(self.new, "bin/app_1.0.0.0.dll")
        self.put(self.new, "bin/app_1.0.1.0.dll", b"Z" * N)
        self.assertEqual(self.plan()["selectedCount"], 0)

    def test_ambiguous_two_sources_same_stem_never_picks_one(self):
        self.put(self.old, "bin/app_1.0.0.0.dll")
        self.put(self.old, "bin/app_1.0.1.0.dll", b"Q" * N)
        self.put(self.new, "bin/app_1.0.2.0.dll", b"Z" * N)
        result = self.plan()
        self.assertEqual(result["selectedCount"], 0)
        self.assertEqual(result["fallbackReasons"], {"ambiguous_source_candidates": 1})

    def test_same_source_claimed_by_two_targets_fails_both(self):
        self.put(self.old, "bin/app_1.0.0.0.dll")
        self.put(self.new, "bin/app_1.0.1.0.dll", b"Z" * N)
        self.put(self.new, "bin/app_1.0.2.0.dll", b"Y" * N)
        result = self.plan()
        self.assertEqual(result["selectedCount"], 0)
        self.assertEqual(result["fallbackReasons"],
                         {"source_claimed_by_multiple_targets": 2})

    def test_monotonic_version_and_parent_are_required(self):
        self.put(self.old, "bin/app_2.0.0.0.dll")
        self.put(self.new, "bin/app_1.0.0.0.dll", b"Z" * N)
        self.assertEqual(self.plan()["selectedCount"], 0)
        (self.new / "bin" / "app_1.0.0.0.dll").unlink()
        self.put(self.new, "other/app_3.0.0.0.dll", b"Z" * N)
        self.assertEqual(self.plan()["selectedCount"], 0)

    def test_identical_content_is_left_to_exact_reuse_lane(self):
        self.put(self.old, "bin/app_1.0.0.0.dll")
        self.put(self.new, "bin/app_1.0.1.0.dll")
        self.assertEqual(self.plan()["selectedCount"], 0)

    def test_size_and_ratio_caps_exclude_bad_candidates(self):
        self.put(self.old, "bin/app_1.0.0.0.dll", b"x" * N)
        self.put(self.new, "bin/app_1.0.1.0.dll", b"z" * (N * 3))
        self.assertEqual(self.plan()["selectedCount"], 0)
        self.put(self.new, "bin/app_1.0.1.0.dll", b"z" * 20)
        result = self.plan()
        self.assertEqual(result["fallbackReasons"], {"target_size_out_of_bounds": 1})

    def test_multiple_disjoint_families_emit_byte_ordered_deterministic_plan(self):
        self.put(self.old, "z/x_1.0.0.0.dll", b"X" * N)
        self.put(self.old, "a/y_1.0.0.0.dll", b"Y" * N)
        self.put(self.new, "z/x_1.0.1.0.dll", b"Z" * N)
        self.put(self.new, "a/y_1.0.2.0.dll", b"W" * N)
        result = self.plan()
        self.assertEqual(result["selectedCount"], 2)
        self.assertEqual([r["targetPath"] for r in result["candidates"]],
                         ["a/y_1.0.2.0.dll", "z/x_1.0.1.0.dll"])
        self.assertEqual(result, self.plan())

    def test_tampered_layout_or_frozen_pair_is_rejected(self):
        self.put(self.old, "bin/app_1.0.0.0.dll")
        self.put(self.new, "bin/app_1.0.1.0.dll", b"Z" * N)
        old, new, frozen = self.fixture()
        tampered = copy.deepcopy(old)
        tampered["files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "layout identity mismatch"):
            planner.select_pair(tampered, new, frozen)
        fake = copy.deepcopy(frozen)
        fake["removed"] = []
        with self.assertRaisesRegex(ValueError, "frozen pair classification"):
            planner.select_pair(old, new, fake)

    def test_noncanonical_or_invalid_version_tokens_are_not_candidates(self):
        self.assertIsNone(planner.signature("a/app_1.2.3.dll"))
        self.assertIsNone(planner.signature("a/app_1.2.3.4.exe"))
        self.assertIsNone(planner.signature("a/app_2147483648.2.3.4.dll"))
        with self.assertRaises(ValueError):
            planner.signature("../escape/app_1.2.3.4.dll")


if __name__ == "__main__":
    unittest.main()
