#!/usr/bin/env python3
"""Negative controls for immutable PATCH-TREE-001 T3 closeout admission."""
import copy
import json
import unittest

from verify_patch_tree_001_t3_closeout import (
    LEDGER, C1, C1_COLUMNS, D0, D0_COLUMNS,
    evaluate, read_tsv,
)


class T3AdmissionCloseoutTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ledger = json.loads(LEDGER.read_text(encoding="utf-8"))
        cls.c1 = read_tsv(C1, C1_COLUMNS, 16)
        cls.d0 = read_tsv(D0, D0_COLUMNS, 24)

    def assert_invalid(self, *, ledger=None, c1=None, d0=None):
        with self.assertRaises(ValueError):
            evaluate(
                self.ledger if ledger is None else ledger,
                self.c1 if c1 is None else c1,
                self.d0 if d0 is None else d0,
            )

    def test_real_frozen_source_evidence_passes_without_original_zip(self):
        rows = evaluate(self.ledger, self.c1, self.d0)
        self.assertEqual(2, len(rows))
        self.assertTrue(all(row["t3OverT0sCpu"] > 3 for row in rows))
        self.assertTrue(all(row["t3OverT0sWall"] > 3 for row in rows))
        self.assertEqual([86727, 5944],
                         [row["t3WarmPhysicalBytes"] for row in rows])
        self.assertEqual([False, True],
                         [row["h3OverH2Cpu"] < 1 for row in rows])

    def test_cannot_relax_original_cpu_budget(self):
        x = copy.deepcopy(self.ledger)
        x["admission"]["maxCreateCpuRatio"] = 12
        self.assert_invalid(ledger=x)

    def test_cannot_claim_shipping_fallback_was_implemented(self):
        x = copy.deepcopy(self.ledger)
        x["productionFallbackImplemented"] = True
        self.assert_invalid(ledger=x)

    def test_cannot_mark_product_adopt_from_two_samples(self):
        x = copy.deepcopy(self.ledger)
        x["productDecision"] = "ADOPT"
        self.assert_invalid(ledger=x)

    def test_cannot_weaken_synthetic_to_real_scope(self):
        x = copy.deepcopy(self.ledger)
        x["controls"]["t3b1ModelNotActualFullTree"] = False
        self.assert_invalid(ledger=x)

    def test_cannot_change_original_t0s_cpu(self):
        x = copy.deepcopy(self.ledger)
        x["pairs"][1]["b2bMedianCreateCpuMs"]["t0s"] /= 2
        self.assert_invalid(ledger=x)

    def test_cannot_hide_csp_byte_drift(self):
        x = copy.deepcopy(self.ledger)
        x["pairs"][0]["t3CspPhysicalBytes"] -= 1
        self.assert_invalid(ledger=x)

    def test_h3_cache_must_have_actual_reads_and_full_base_coverage(self):
        rows = copy.deepcopy(self.c1)
        rows[3]["base_read_B"] = "1"
        self.assert_invalid(c1=rows)

    def test_c1_original_pair_identity_must_be_consistent(self):
        rows = copy.deepcopy(self.c1)
        rows[1]["patch_sha256"] = "0" * 64
        self.assert_invalid(c1=rows)

    def test_d0_stage_probe_counts_must_be_consistent(self):
        rows = copy.deepcopy(self.d0)
        rows[4]["dict_encodes"] = "0"
        self.assert_invalid(d0=rows)

    def test_d0_off_control_must_remain_uninstrumented(self):
        rows = copy.deepcopy(self.d0)
        rows[0]["dict_ticks"] = "1"
        self.assert_invalid(d0=rows)


if __name__ == "__main__":
    unittest.main()
