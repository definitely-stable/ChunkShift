"""D0 deterministic synthetic topology tests; explicitly NOT CSP size evidence."""

import sys
import unittest
from itertools import combinations
from random import Random
from hashlib import sha256
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from patch_dict_001_d0 import (  # noqa: E402
    BaseChunk, Plan, Run, VerifiedBase, bounded_oracle,
    MAX_CANDIDATES, MAX_DICTIONARY_BYTES, CSP_V1_MAX_REFERENCES,
)


def base_of(*items: bytes) -> VerifiedBase:
    offset = 0
    chunks = []
    for ordinal, item in enumerate(items):
        chunks.append(BaseChunk(ordinal, offset, item, sha256(item).hexdigest()))
        offset += len(item)
    return VerifiedBase(chunks)


class PatchDictD0Tests(unittest.TestCase):
    def test_disjoint_plan_materializes_in_base_order(self):
        base = base_of(b"aa", b"bb", b"cc", b"dd", b"ee")
        self.assertEqual(b"aaddee", base.materialize(Plan((Run(0, 1), Run(3, 2)))))

    def test_contiguous_control_is_explicit_and_equal_budget(self):
        base = base_of(b"aa", b"bb", b"cc", b"dd")
        result = bounded_oracle(base, [Run(0, 2), Run(2, 2)],
                                lambda p, data: len(data) + 5 * len(p.runs),
                                byte_budget=4)
        self.assertEqual(1, len(result.best_contiguous.plan.runs))
        self.assertEqual(result.best_contiguous, result.best_any)
        self.assertEqual(2, result.evaluated)

    def test_noncontiguous_plan_can_win_a_synthetic_objective(self):
        base = base_of(b"AA", b"xx", b"yy", b"BB")
        # Controlled illustration: two distant anchors are valuable; NOT zstd.
        def synthetic(p, dictionary):
            return (0 if b"AA" in dictionary else 100) + (0 if b"BB" in dictionary else 100) + len(p.runs)
        result = bounded_oracle(base, [Run(0, 1), Run(3, 1)], synthetic)
        self.assertEqual((Run(0, 1), Run(3, 1)), result.best_any.plan.runs)
        self.assertEqual(3, result.evaluated)

    def test_adjacent_and_overlapping_selected_runs_are_not_canonical(self):
        base = base_of(b"a", b"b", b"c", b"d", b"e")
        for plan in (Plan((Run(0, 2), Run(2, 1))),
                     Plan((Run(0, 3), Run(2, 2))),
                     Plan((Run(4, 1), Run(0, 1)))):
            with self.assertRaises(ValueError):
                base.materialize(plan)
        result = bounded_oracle(base, [Run(0, 2), Run(2, 1), Run(3, 2)],
                                lambda p, d: len(d))
        self.assertEqual(4, result.evaluated)  # three singles, first+third

    def test_duplicate_content_distinct_occurrences_are_legal(self):
        base = base_of(b"same", b"space", b"same")
        result = bounded_oracle(base, [Run(0, 1), Run(2, 1)],
                                lambda p, d: 100 - len(d))
        self.assertEqual((Run(0, 1), Run(2, 1)), result.best_any.plan.runs)

    def test_proposal_order_does_not_change_tie(self):
        base = base_of(b"aa", b"bb", b"cc", b"dd", b"ee")
        proposal = [Run(4, 1), Run(0, 1), Run(2, 1)]
        results = [bounded_oracle(base, x, lambda p, d: 0)
                   for x in (proposal, proposal[::-1], sorted(proposal))]
        self.assertEqual(results[0], results[1])
        self.assertEqual(results[1], results[2])
        self.assertEqual((Run(0, 1),), results[0].best_any.plan.runs)

    def test_exact_budget_and_one_byte_over_are_distinguished(self):
        base = base_of(b"aaa", b"x", b"bbb")
        pool = [Run(0, 1), Run(2, 1)]
        allowed = bounded_oracle(base, pool, lambda p, d: 100 - len(d), byte_budget=6)
        blocked = bounded_oracle(base, pool, lambda p, d: 100 - len(d), byte_budget=5)
        self.assertEqual(2, len(allowed.best_any.plan.runs))
        self.assertEqual(1, len(blocked.best_any.plan.runs))

    def test_sha256_mismatch_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
            VerifiedBase([BaseChunk(0, 0, b"modified", sha256(b"original").hexdigest())])

    def test_base_offsets_ordinals_and_immutable_bytes_required(self):
        good = BaseChunk(0, 0, b"aa", sha256(b"aa").hexdigest())
        for bad in (BaseChunk(1, 0, b"x", sha256(b"x").hexdigest()),
                    BaseChunk(0, 1, b"x", sha256(b"x").hexdigest()),
                    BaseChunk(0, 0, bytearray(b"aa"), good.sha256_hex),
                    BaseChunk(0, 0, b"", sha256(b"").hexdigest())):
            with self.assertRaises(ValueError):
                VerifiedBase([bad])
        with self.assertRaises(ValueError):
            VerifiedBase([good, BaseChunk(1, 3, b"b", sha256(b"b").hexdigest())])

    def test_duplicate_and_invalid_proposals_fail_closed(self):
        base = base_of(b"a", b"b")
        for pool in ([Run(0, 1)] * 2, [Run(-1, 1)], [Run(0, 0)],
                     [Run(0, 3)], [Run(0, 1)] * (MAX_CANDIDATES + 1)):
            with self.assertRaises(ValueError):
                bounded_oracle(base, pool, lambda p, d: 0)

    def test_exact_integer_cost_and_callback_errors(self):
        base = base_of(b"aaa")
        for bad in (-1, 1.5, True, (1 << 63)):
            with self.assertRaises(ValueError):
                bounded_oracle(base, [Run(0, 1)], lambda p, d: bad)
        with self.assertRaisesRegex(RuntimeError, "broken codec"):
            bounded_oracle(base, [Run(0, 1)],
                           lambda p, d: (_ for _ in ()).throw(RuntimeError("broken codec")))

    def test_trained_dictionary_magic_cannot_be_proposed(self):
        base = base_of(bytes.fromhex("37a430ec") + b"aa", b"bb")
        with self.assertRaisesRegex(ValueError, "no eligible"):
            bounded_oracle(base, [Run(0, 1)], lambda p, d: 1)
        result = bounded_oracle(base, [Run(0, 1), Run(1, 1)], lambda p, d: 1)
        self.assertEqual((Run(1, 1),), result.best_any.plan.runs)

    def test_small_random_universe_matches_independent_bruteforce(self):
        rng = Random(222)
        for _ in range(40):
            base = base_of(*(bytes([i + 1]) * rng.randrange(1, 5) for i in range(6)))
            pool = [Run(i, 1) for i in range(6)]
            budget = rng.randrange(2, 13)
            def cost(plan, data):
                # Synthetic, deliberately nonmonotone function of selected bytes.
                return (sum(data) * 17 + len(data) * 19 + len(plan.runs) * 11) % 73
            result = bounded_oracle(base, pool[::-1], cost, byte_budget=budget)
            expected = []
            for k in range(1, 7):
                for sub in combinations(pool, k):
                    if any(a.end >= b.start for a, b in zip(sub, sub[1:])):
                        continue
                    plan = Plan(sub)
                    dictionary = b"".join(base.chunks[r.start].data for r in sub)
                    if len(dictionary) > budget:
                        continue
                    expected.append((
                        (cost(plan, dictionary), len(sub), len(dictionary), len(sub), sub),
                        plan,
                    ))
            expected.sort()
            best_single = min(x for x in expected if len(x[1].runs) == 1)
            self.assertEqual(expected[0][1], result.best_any.plan)
            self.assertEqual(best_single[1], result.best_contiguous.plan)
            self.assertEqual(len(expected), result.evaluated)

    def test_v1_compatibility_depends_on_refs_not_run_contiguity(self):
        base = base_of(*(bytes([i + 1]) for i in range(9)))
        within = bounded_oracle(base, [Run(0, 2), Run(4, 2)],
                                lambda p, d: 100 - len(d))
        self.assertEqual(4, CSP_V1_MAX_REFERENCES)
        self.assertEqual(4, within.best_any.referenced_chunks)
        self.assertTrue(within.best_any.csp_v1_compatible)
        beyond = bounded_oracle(base, [Run(0, 2), Run(4, 3)],
                                lambda p, d: 100 - len(d))
        self.assertEqual(5, beyond.best_any.referenced_chunks)
        self.assertFalse(beyond.best_any.csp_v1_compatible)

    def test_no_false_cross_file_or_byte_range_admission(self):
        base = base_of(b"abc", b"def")
        for plan in (Plan((Run(0, 0),)), Plan((Run(0, 3),)), Plan(())):
            with self.assertRaises(ValueError):
                base.materialize(plan)
        with self.assertRaises(ValueError):
            base.materialize(Plan((Run(0, 2),)), MAX_DICTIONARY_BYTES + 1)


if __name__ == "__main__":
    unittest.main()
