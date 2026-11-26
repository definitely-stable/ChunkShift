import copy
import json
from unittest.mock import patch
from pathlib import Path
import tempfile
import unittest

from holdout_evaluate import (definitions, schedule, paired_metrics, combine, strict_json,
                              inventory, audit_lane, identities, fingerprints)
from prepare_holdout import digest


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
                d = [2, 4, 6, 8, 100, 100]
                c = [1, 2, 3, 4, 100, 100]
                r["result"]["wall_ms_ready"] = (d if r["candidate"] == "D" else c)[r["round"]-1]
        result = paired_metrics(rows, self.lock)
        self.assertEqual(result["groups"][0]["D_over_C_ready_median"], 2.0)
        # Ratio of separate medians is also 2 here; change one large D
        # to a small value in the same pair, making the two estimators differ.
        for r in rows:
            if r["round"] == 5 and r["candidate"] == "D":
                r["result"]["wall_ms_ready"] = 1
        result = paired_metrics(rows, self.lock)
        self.assertEqual(result["groups"][0]["D_over_C_ready_median"], 2.0)
        self.assertNotEqual(result["groups"][0]["metrics"]["wall_ms_ready"]["D_median"] /
                            result["groups"][0]["metrics"]["wall_ms_ready"]["C_median"], 2.0)

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

    def test_complete_lane_audit_and_completion_seal(self):
        commit = "a"*40
        lane = "ubuntu-24.04"
        identity = f"REPO-INGEST-003/RUN-123-1-{commit[:12]}-{lane}-1"
        with tempfile.TemporaryDirectory() as directory:
            raw = Path(directory)
            def write(name, value):
                (raw / name).write_text(json.dumps(value), encoding="utf-8")
            def checksums():
                (raw / "sha256.txt").write_text("".join(
                    f"{digest(p)}  {p.name}\n" for p in sorted(raw.iterdir())
                    if p.name != "sha256.txt"), encoding="utf-8")
            meta = dict(experiment=self.lock["experiment"], phase="sealed-holdout",
                        commit=commit, dirty=False, lane=lane, replica=1, run_id=identity,
                        github_run_id="123", github_run_attempt="1", sdk="synthetic",
                        dotnet_info="synthetic", host="Linux", machine="x64", runner_image="test",
                        runner_image_version="test", started_utc="test", runner_name="test",
                        **identities(), fingerprints=fingerprints(self.qualified))
            write("manifest.json", meta)
            write("replay.json", {})
            (raw / "build.log").write_text("synthetic", encoding="utf-8")
            rows = []
            for n, (family, suite, rnd, pos, candidate) in enumerate(schedule(self.lock), 1):
                expected = self.geometry["families"][family]
                l = expected["logical_bytes"]
                sha = self.lock["corpora"][family]["sha256"]
                result = dict(expected, candidate=candidate, suite=suite, target_mib=32,
                              source_sha256=sha, restore_sha256=sha, verdict="pass",
                              layout_sha256=self.qualified["layouts"][family][suite],
                              runtime=".NET synthetic", os="Linux", arch="X64", processors=4,
                              stage_files_completed=expected["logical_occurrences"] if candidate=="D" else 0,
                              modeled_stage_payload_bytes=l if candidate=="D" else 0,
                              modeled_request_copy_bytes=0, range_source_read_bytes=l if candidate=="D" else 0,
                              modeled_duplicate_comparison_bytes=l-expected["unique_payload_bytes"] if candidate=="C" else 0,
                              wall_ms_ready=120 if candidate=="D" else 100,
                              cpu_ms_ready=100, allocated_bytes_delta_ready=100,
                              process_peak_working_set_bytes_at_ready=100, restore_ms=100,
                              rebuild_ms=100, cleanup_ms=0)
                if candidate=="D":
                    result.update(active_duplicate_count=0, closed_duplicate_count=0)
                write(f"sample-{n:03d}.stdout", result)
                (raw / f"sample-{n:03d}.stderr").write_text("", encoding="utf-8")
                rows.append(dict(sample_id=n, family=family, suite=suite, round=rnd, order=pos,
                                 candidate=candidate, warmup=rnd==0, commit=commit, run_id=identity,
                                 lane=lane, replica=1, exit_code=0, outcome="pass", result=result))
            (raw / "samples.jsonl").write_text("".join(json.dumps(row)+"\n" for row in rows), encoding="utf-8")
            completion = dict(commit=commit, run_id=identity, samples=112, measured=96,
                              source_sha256={f: spec["sha256"] for f, spec in self.lock["corpora"].items()},
                              private_empty=True, checkout_clean=True,
                              samples_sha256=digest(raw / "samples.jsonl"))
            write("completion.json", completion)
            checksums()
            # Only replay is mocked: official member metadata is not a unit-test fixture.
            # Actual acquisition/replay was separately qualified by #494/#495.
            with patch("holdout_evaluate.verify_replay"):
                def audit():
                    return audit_lane(raw, commit, "123", "1", self.lock, self.geometry, self.qualified)
                self.assertEqual(audit()["decision"], "PERFORMANCE_ELIGIBLE")
                (raw / "completion.json").unlink()
                checksums()
                with self.assertRaises(ValueError):
                    audit()
                write("completion.json", dict(completion, private_empty=False))
                checksums()
                with self.assertRaises(ValueError):
                    audit()
                write("completion.json", completion)
                checksums()
                (raw / "sample-001.stdout").write_text("{}", encoding="utf-8")
                with self.assertRaises(ValueError):
                    audit()
                checksums()
                with self.assertRaises(ValueError):
                    audit()


if __name__ == "__main__":
    unittest.main()
