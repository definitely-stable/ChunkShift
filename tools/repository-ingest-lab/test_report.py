import unittest

from report import METRICS, expected_schedule, summarize


class ReportTests(unittest.TestCase):
    def test_frozen_schedule(self):
        rows = list(expected_schedule(5))
        self.assertEqual(84, len(rows))
        self.assertEqual(14, sum(row[2] == 0 for row in rows))
        self.assertEqual(70, sum(row[2] > 0 for row in rows))
        self.assertEqual(["B", "C", "A"], [r[-1] for r in rows if r[:3] == ("unique", "sha256", 1)])

    @staticmethod
    def synthetic_rows():
        rows = []
        for family, suite, round_no, order, candidate in expected_schedule(5):
            metrics = {key: 10 for key in METRICS}
            metrics["wall_ms_ready"] = 10000 if round_no == 0 else {"A": 20, "B": 5, "C": 10}[candidate]
            rows.append(dict(family=family, suite=suite, round=round_no, candidate=candidate,
                             warmup=round_no == 0, corpus_sha256="a", layout_sha256="b",
                             logical_bytes=100, logical_occurrences=10, unique_payload_bytes=100,
                             unique_frames=10, pack_bytes=948, pack_count=1,
                             active_duplicate_count=0, closed_duplicate_count=0, **metrics))
        return rows

    def test_pairing_excludes_warmup(self):
        summary = summarize(self.synthetic_rows(), 5)
        self.assertEqual(6, len(summary))
        self.assertEqual([2] * 5, summary[0]["paired_ready_speedup"]["A/C"]["round_ratios"])
        self.assertEqual(0.5, summary[0]["paired_ready_speedup"]["B/C"]["median"])
        self.assertEqual(20, summary[0]["metrics"]["A"]["wall_ms_ready"]["median"])
        self.assertEqual({"A/C"}, set(summary[-1]["paired_ready_speedup"]))

    def test_missing_pair_stops(self):
        rows = self.synthetic_rows()
        with self.assertRaises(ValueError):
            summarize(rows[:-1], 5)


if __name__ == "__main__":
    unittest.main()
