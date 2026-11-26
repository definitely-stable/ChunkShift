import copy
from pathlib import Path
import tempfile
import unittest

from holdout_evaluate import definitions, schedule, paired_metrics, combine, strict_json, inventory


class DecisionTests(unittest.TestCase):
    def setUp(self):
        self.lock, self.geometry, self.qualified = definitions()

    def rows(self, d=110, c=100):
        rows = []
        for family, suite, rnd, pos, candidate in schedule(self.lock):
            value = d if candidate == "D" else c
            result = dict(wall_ms_ready=value, cpu_ms_ready=100, allocated_bytes_delta_ready=100,
                          process_peak_working_set_bytes_at_ready=100, restore_ms=100,
                          rebuild_ms=100, cleanup_ms=0)
            if rnd == 0:
                result["wall_ms_ready"] = 1 if candidate == "D" else 1000000
            rows.append(dict(family=family, suite=suite, round=rnd, order=pos, candidate=candidate,
                             warmup=rnd == 0, result=result))
        return rows

    def test_schedule_balanced(self):
        rows = list(schedule(self.lock))
        self.assertEqual(len(rows), 112)
        self.assertEqual(sum(r[2] == 0 for r in rows), 16)
        for candidate in "DC":
            for position in (0, 1):
                self.assertEqual(sum(r[:2] == ("win-versioned", "sha256") and r[2] > 0
                                     and r[3:] == (position, candidate) for r in rows), 3)

    def test_exact_primary_boundary_and_warmup(self):
        self.assertEqual(paired_metrics(self.rows(), self.lock)["decision"], "PERFORMANCE_ELIGIBLE")
        self.assertEqual(paired_metrics(self.rows(109.999999, 100), self.lock)["decision"], "DEFER")

    def test_guardrail_boundary(self):
        rows = self.rows(120, 100)
        for r in rows:
            if r["candidate"] == "C":
                r["result"]["cpu_ms_ready"] = 105
        self.assertEqual(paired_metrics(rows, self.lock)["decision"], "PERFORMANCE_ELIGIBLE")
        for r in rows:
            if r["candidate"] == "C" and r["family"] == "adverse-repeat" and r["suite"] == "sha256":
                r["result"]["cpu_ms_ready"] = 105.000001
        self.assertEqual(paired_metrics(rows, self.lock)["decision"], "DEFER")

    def test_restore_boundary(self):
        rows = self.rows(120, 100)
        for r in rows:
            if r["candidate"] == "C":
                r["result"]["restore_ms"] = 110
        self.assertEqual(paired_metrics(rows, self.lock)["decision"], "PERFORMANCE_ELIGIBLE")
        for r in rows:
            if r["candidate"] == "C":
                r["result"]["restore_ms"] = 111
        self.assertEqual(paired_metrics(rows, self.lock)["decision"], "DEFER")

    def test_pair_not_ratio_of_medians(self):
        rows = self.rows(120, 100)
        for r in rows:
            if r["round"] > 0:
                v = [1, 2, 3, 100, 101, 102][r["round"]-1]
                r["result"]["wall_ms_ready"] = v if r["candidate"] == "D" else v/2
        result = paired_metrics(rows, self.lock)
        self.assertEqual(result["groups"][0]["D_over_C_ready_median"], 2.0)

    def test_missing_pair(self):
        with self.assertRaises(ValueError):
            paired_metrics(self.rows()[:-1], self.lock)

    def test_duplicate_pair(self):
        rows = self.rows()
        rows[-1] = copy.deepcopy(rows[-2])
        with self.assertRaises(ValueError):
            paired_metrics(rows, self.lock)

    def test_zero_denominator(self):
        rows = self.rows()
        rows[-1]["result"]["wall_ms_ready"] = 0
        with self.assertRaises((ValueError, ZeroDivisionError)):
            paired_metrics(rows, self.lock)

    def test_all_six_required(self):
        lanes = [dict(lane=p, replica=r, run_id=f"{p}-{r}", decision="PERFORMANCE_ELIGIBLE")
                 for p in self.lock["design"]["platforms"] for r in (1, 2)]
        self.assertEqual(combine(lanes, self.lock)["decision"], "PERFORMANCE_ELIGIBLE")
        lanes[-1]["decision"] = "DEFER"
        self.assertEqual(combine(lanes, self.lock)["decision"], "DEFER")
        with self.assertRaises(ValueError):
            combine(lanes[:-1], self.lock)
        with self.assertRaises(ValueError):
            combine(lanes[:-1]+[lanes[0]], self.lock)

    def test_strict_json(self):
        for data in ('{"a":1,"a":2}', '{"a":NaN}', '{"a":Infinity}'):
            with self.assertRaises(ValueError):
                strict_json(data)

    def test_inventory_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / "sha256.txt").write_text("", encoding="utf-8")
            with self.assertRaises(ValueError):
                inventory(path)
            (path / "extra").write_text("x", encoding="utf-8")
            with self.assertRaises(ValueError):
                inventory(path)


if __name__ == "__main__":
    unittest.main()
