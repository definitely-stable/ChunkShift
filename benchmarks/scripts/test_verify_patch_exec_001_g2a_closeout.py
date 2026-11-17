"""Adversarial contract tests for G2-A independent replay helpers."""
import csv
import io
import pathlib
import tempfile
import unittest

import verify_patch_exec_001_g2a_closeout as v


class PatchExecG2ACloseoutTests(unittest.TestCase):
    def test_round_orders_have_each_allocation_exactly_once(self):
        names = set(v.CASES)
        self.assertEqual(v.order(0), list(v.CASES))
        self.assertEqual(v.order(1), list(reversed(v.CASES)))
        for i in range(5):
            self.assertEqual(set(v.order(i)), names)
            self.assertEqual(len(v.order(i)), len(names))

    def test_physical_index_rejects_duplicate_and_missing_rows(self):
        rows = [{"family": "cal", "base": "v1", "target": "v2",
                 "path": f"{i:02d}.dll"} for i in range(16)]
        self.assertEqual(len(v.physical_rows(rows)), 16)
        with self.assertRaisesRegex(ValueError, "sixteen"):
            v.physical_rows(rows[:-1])
        rows[1] = dict(rows[0])
        with self.assertRaisesRegex(ValueError, "duplicate"):
            v.physical_rows(rows)

    def test_tsv_reader_rejects_duplicate_sample_by_contract(self):
        with tempfile.TemporaryDirectory() as root:
            file = pathlib.Path(root) / "samples.tsv"
            file.write_text("mode\tround\tallocation\n"
                            "warm-csm\t0\tf1-h0\n"
                            "warm-csm\t0\tf1-h0\n", encoding="utf-8")
            rows = v.rows_tsv(file)
            self.assertEqual(len(rows), 2)
            self.assertNotEqual(len({(r["mode"], r["round"], r["allocation"])
                                      for r in rows}), len(rows))

    def test_fails_closed_on_false_provenance(self):
        with self.assertRaises(ValueError):
            v.require(False, "reject wrong SHA")
        with self.assertRaises(FileNotFoundError):
            v.verify_archive("/nonexistent/never.zip", "warm-csm", {}, {})


if __name__ == "__main__":
    unittest.main()
