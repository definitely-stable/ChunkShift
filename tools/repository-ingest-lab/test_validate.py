import copy
import unittest

from validate import validate


class ValidationTests(unittest.TestCase):
    def setUp(self):
        self.hash = "a" * 64
        self.result = dict(candidate="C", suite="sha256", target_mib=1,
                           source_sha256=self.hash, restore_sha256=self.hash,
                           verdict="pass", layout_sha256="b" * 64,
                           logical_bytes=1024, logical_occurrences=4,
                           unique_frames=2, unique_payload_bytes=512,
                           pack_count=2, pack_bytes=512 + 136 + 336,
                           allocated_bytes_delta_ready=100,
                           process_peak_working_set_bytes_at_ready=1024,
                           stage_files_completed=0, modeled_stage_payload_bytes=0,
                           modeled_request_copy_bytes=0, active_duplicate_count=1,
                           closed_duplicate_count=1, modeled_duplicate_comparison_bytes=512,
                           wall_ms_ready=1.0, cpu_ms_ready=0.0, restore_ms=1.0,
                           rebuild_ms=1.0, cleanup_ms=0.0, processors=2,
                           runtime=".NET 10", os="test", arch="X64")

    def check(self, record, candidate="C", family="cross"):
        return validate(record, candidate, "sha256", family, self.hash, 1024, 1)

    def test_valid(self):
        self.assertEqual(len(self.check(self.result)), 7)

    def test_bad_metrics_and_identity(self):
        for key, value in (("wall_ms_ready", float("nan")), ("wall_ms_ready", 0),
                           ("cpu_ms_ready", -1), ("restore_ms", float("inf")),
                           ("unique_frames", True), ("pack_bytes", 0),
                           ("target_mib", 2), ("restore_sha256", "c" * 64),
                           ("runtime", ""), ("stage_files_completed", 1),
                           ("active_duplicate_count", 2)):
            with self.subTest(key=key, value=value):
                record = copy.copy(self.result)
                record[key] = value
                with self.assertRaises(ValueError):
                    self.check(record)

    def test_missing_field(self):
        del self.result["cpu_ms_ready"]
        with self.assertRaises(KeyError):
            self.check(self.result)

    def test_cross_without_closed_hit(self):
        self.result.update(active_duplicate_count=2, closed_duplicate_count=0)
        with self.assertRaises(ValueError):
            self.check(self.result)

    def test_recent_without_active_hit(self):
        self.result.update(active_duplicate_count=0, closed_duplicate_count=2)
        with self.assertRaises(ValueError):
            self.check(self.result, family="recent")

    def test_unique_rejects_duplicate_corpus(self):
        with self.assertRaises(ValueError):
            self.check(self.result, family="unique")

    def test_staging_accounting(self):
        self.result.update(candidate="A", stage_files_completed=4,
                           modeled_stage_payload_bytes=1024, modeled_request_copy_bytes=1024,
                           active_duplicate_count=0, closed_duplicate_count=0,
                           modeled_duplicate_comparison_bytes=0)
        self.check(self.result, candidate="A")
        self.result["stage_files_completed"] = 2
        with self.assertRaises(ValueError):
            self.check(self.result, candidate="A")


if __name__ == "__main__":
    unittest.main()
