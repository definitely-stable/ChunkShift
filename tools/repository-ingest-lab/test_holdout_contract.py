import json
from pathlib import Path
import unittest

from holdout_contract import check_definition, validate_external, qualification_record
from prepare_holdout import digest

LAB = Path(__file__).resolve().parent


class HoldoutContractTests(unittest.TestCase):
    def setUp(self):
        self.lock = json.loads((LAB / "holdout-lock.json").read_text(encoding="utf-8"))
        self.geometry = json.loads((LAB / "holdout-geometry-lock.json").read_text(encoding="utf-8"))
        self.family = "adverse-repeat"
        expected = self.geometry["families"][self.family]
        source = self.lock["corpora"][self.family]["sha256"]
        self.record = dict(expected, candidate="C", suite="sha256", target_mib=32,
                           source_sha256=source, restore_sha256=source, layout_sha256="a"*64,
                           verdict="pass", runtime=".NET 10", os="Linux", arch="X64", processors=4,
                           stage_files_completed=0, modeled_stage_payload_bytes=0,
                           modeled_request_copy_bytes=0, range_source_read_bytes=0,
                           modeled_duplicate_comparison_bytes=expected["logical_bytes"]-expected["unique_payload_bytes"],
                           wall_ms_ready=10.0, cpu_ms_ready=9.0, restore_ms=5.0,
                           rebuild_ms=2.0, cleanup_ms=1.0, allocated_bytes_delta_ready=100,
                           process_peak_working_set_bytes_at_ready=200)

    def check(self, result, candidate="C"):
        return validate_external(result, self.lock, self.geometry, self.family,
                                 "sha256", candidate, "ubuntu-24.04")

    def test_definition(self):
        check_definition(self.lock, self.geometry, digest(LAB / "holdout-lock.json"))
        with self.assertRaises(ValueError):
            check_definition(self.lock, self.geometry, "0"*64)

    def test_direct_and_range_same_signature(self):
        d = dict(self.record, candidate="D", active_duplicate_count=0, closed_duplicate_count=0,
                 modeled_duplicate_comparison_bytes=0, stage_files_completed=self.record["logical_occurrences"],
                 modeled_stage_payload_bytes=self.record["logical_bytes"],
                 range_source_read_bytes=self.record["logical_bytes"])
        self.assertEqual(self.check(self.record), self.check(d, "D"))
        d["range_source_read_bytes"] -= 1
        with self.assertRaises(ValueError):
            self.check(d, "D")

    def test_locality_shift_rejected(self):
        changed = dict(self.record, active_duplicate_count=17, closed_duplicate_count=509)
        with self.assertRaises(ValueError):
            self.check(changed)

    def test_geometry_shift_rejected(self):
        changed = dict(self.record, pack_count=3, pack_bytes=self.record["pack_bytes"]+168)
        with self.assertRaises(ValueError):
            self.check(changed)

    def test_arch_mismatch_rejected(self):
        with self.assertRaises(ValueError):
            self.check(dict(self.record, arch="Arm64"))

    def test_qualification_redacts_performance(self):
        result = qualification_record(self.record)
        for key in ("wall_ms_ready", "cpu_ms_ready", "allocated_bytes_delta_ready",
                    "process_peak_working_set_bytes_at_ready", "restore_ms", "rebuild_ms", "cleanup_ms"):
            self.assertNotIn(key, result)
        self.assertEqual(result["restore_sha256"], self.record["source_sha256"])
        self.assertEqual(result["closed_duplicate_count"], 510)


if __name__ == "__main__":
    unittest.main()
