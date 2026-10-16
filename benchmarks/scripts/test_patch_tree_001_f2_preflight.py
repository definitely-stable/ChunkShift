"""Synthetic-only F2-P0 mechanics; no claims about original CSP or F2 winner."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("f2", Path(__file__).with_name("patch_tree_001_f2_preflight.py"))
f2 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(f2)


def candidate(ordinal, source, frame=50, count=1, start=0):
    return {"ordinal": ordinal, "firstSourceFile": source, "finalFrameBytes": frame,
            "finalCostBytes": None if frame is None else frame + count * 32,
            "recordCount": count, "startOffset": start}


def entry(candidates=None, target_id="A", zstd=130, stored=130, refs=0):
    return {"targetIndex": 1, "targetChunkId": target_id, "targetLength": 200,
            "noDictionaryFrameBytes": zstd, "storedBytes": stored,
            "dictionaryRefs": refs, "candidates": candidates or []}


class F2PreflightTests(unittest.TestCase):
    def test_first_source_is_a_filter_not_dictionary_proof(self):
        x = entry([candidate(0, "other", 4), candidate(1, "a.dll", 20)])
        self.assertEqual(f2.constrained_choice(x, "a.dll")["localCostBytes"], 52)
        self.assertEqual(f2.constrained_choice(x, "a.dll")["firstSourceEligible"], 1)

    def test_no_candidate_falls_back_to_zstd_or_raw(self):
        self.assertEqual(f2.constrained_choice(entry(zstd=130), "a")["localCostBytes"], 130)
        self.assertEqual(f2.constrained_choice(entry(zstd=300), "a")["choice"], "raw")

    def test_final_unmeasured_candidates_cannot_claim_gain(self):
        x = entry([candidate(0, "a", None), candidate(1, "a", 99)])
        c = f2.constrained_choice(x, "a")
        self.assertEqual(c["firstSourceEligible"], 2)
        self.assertEqual(c["firstSourceFinalMeasured"], 1)
        self.assertEqual(c["localCostBytes"], 130)

    def test_tie_keeps_prior_choice(self):
        x = entry([candidate(0, "a", 98)]) # 98+32 == 130
        self.assertEqual(f2.constrained_choice(x, "a")["choice"], "zstd")

    def test_pair_enforces_exact_entry_identity_and_order(self):
        with self.assertRaisesRegex(ValueError, "identity/order"):
            f2.compare_pair("a", [entry()], [entry(target_id="B")])
        with self.assertRaisesRegex(ValueError, "cardinality"):
            f2.compare_pair("a", [entry()], [])

    def test_local_cost_only_never_treated_as_physical_patch(self):
        a = entry(stored=130)
        b = entry([candidate(0, "a", 20)], stored=20, refs=1)
        v = f2.compare_pair("a", [a], [b])
        self.assertEqual(v["emittedEntries"], 1)
        self.assertEqual(v["firstSourceFilteredCostBytes"], 52)
        self.assertEqual(v["t1ObservedLocalCostBytes"], 52)

    def test_duplicate_or_broken_cost_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "ordinal"):
            f2.constrained_choice(entry([candidate(0, "a"), candidate(0, "a")]), "a")
        bad = candidate(1, "a")
        bad["finalCostBytes"] += 1
        with self.assertRaisesRegex(ValueError, "refs"):
            f2.constrained_choice(entry([bad]), "a")
        with self.assertRaisesRegex(ValueError, "incomplete"):
            f2.constrained_choice(entry([candidate(0, "a", None) | {"finalCostBytes": 10}]), "a")

    def test_path_case_is_exact(self):
        self.assertEqual(f2.constrained_choice(entry([candidate(0, "A.dll", 10)]), "a.dll")["firstSourceEligible"], 0)

    def test_ref_bounds_are_strict(self):
        with self.assertRaisesRegex(ValueError, "refs"):
            f2.chosen_cost(entry(refs=5))


if __name__ == "__main__":
    unittest.main()
