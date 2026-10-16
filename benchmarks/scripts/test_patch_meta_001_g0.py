"""PATCH-META-001 G0 unit tests, not end-to-end CSP verification."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import patch_meta_001_g0 as g0


def h(value):
    return hashlib.sha256(value).hexdigest()


class MetaG0Tests(unittest.TestCase):
    def setUp(self):
        self.ctx = g0.Context(h(b"manifest"), h(b"profile"), "sha256")
        self.records = [(h(b"a"), 100), (h(b"b"), 200), (h(b"a"), 100)]

    def test_cold_warm_and_deterministic_bytes(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root) / "cache"
            fresh, status = g0.load_or_build(p, self.ctx, self.records)
            self.assertEqual(status, "REBUILT")
            before = p.read_bytes()
            cached, status = g0.load_or_build(p, self.ctx, self.records)
            self.assertEqual(status, "HIT")
            self.assertEqual(cached, fresh)
            self.assertEqual(before, p.read_bytes())
            self.assertEqual(sorted(r["firstOffset"] for r in cached["entries"]), [0, 100])
            self.assertEqual(sum(r["count"] for r in cached["entries"]), 3)

    def test_key_invalidation(self):
        variants = [
            (g0.Context(h(b"new"), self.ctx.profile_fingerprint, "sha256"), self.records),
            (g0.Context(self.ctx.manifest_id, h(b"new"), "sha256"), self.records),
            (g0.Context(self.ctx.manifest_id, self.ctx.profile_fingerprint, "blake3-256"), self.records),
            (self.ctx, [self.records[1], self.records[0], self.records[2]]),
            (self.ctx, self.records[:-1]),
        ]
        for ctx, records in variants:
            with self.subTest(ctx=ctx, records=records):
                with tempfile.TemporaryDirectory() as root:
                    p = Path(root) / "cache"
                    g0.load_or_build(p, self.ctx, self.records)
                    features, status = g0.load_or_build(p, ctx, records)
                    self.assertEqual(status, "REBUILT")
                    self.assertEqual(features, g0._derive(records))

    def test_corrupt_unknown_truncated_and_oversized(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root) / "cache"
            correct, _ = g0.load_or_build(p, self.ctx, self.records)
            data = p.read_bytes()
            obj = json.loads(data)
            obj["body"]["schema"] = "unknown-v42"
            variants = [data[:-2], data.replace(b"occurrences-v1", b"occurrences-v2"),
                        b"x" * (g0.MAX_RECORD_BYTES + 1), b"invalid-json",
                        json.dumps(obj).encode()]
            for invalid in variants:
                with self.subTest(invalid=invalid[:20]):
                    p.write_bytes(invalid)
                    features, status = g0.load_or_build(p, self.ctx, self.records)
                    self.assertEqual((features, status), (correct, "REBUILT"))

    def test_correct_digest_cannot_hide_bad_count(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root) / "cache"
            expected, _ = g0.load_or_build(p, self.ctx, self.records)
            obj = json.loads(p.read_text())
            obj["body"]["features"]["entries"][0]["count"] += 1
            obj["sha256"] = hashlib.sha256(g0._canonical(obj["body"])).hexdigest()
            p.write_bytes(g0._canonical(obj))
            self.assertEqual(g0.load_or_build(p, self.ctx, self.records),
                             (expected, "REBUILT"))

    def test_correct_digest_cannot_hide_malformed_features(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root) / "cache"
            expected, _ = g0.load_or_build(p, self.ctx, self.records)
            for invalid in [
                {"entries": "not-a-list"},
                {"entries": [{"chunkId": h(b"a"), "length": "wrong",
                              "firstOrdinal": 0, "firstOffset": 0, "count": 3},
                             {"chunkId": h(b"b"), "length": 200,
                              "firstOrdinal": 1, "firstOffset": 100, "count": 1}]},
            ]:
                obj = json.loads(p.read_text())
                obj["body"]["features"] = invalid
                obj["sha256"] = hashlib.sha256(g0._canonical(obj["body"])).hexdigest()
                p.write_bytes(g0._canonical(obj))
                self.assertEqual(g0.load_or_build(p, self.ctx, self.records),
                                 (expected, "REBUILT"))

    def test_invalid_input(self):
        for record in [("BAD", 20), (h(b"x"), 0),
                       (h(b"x"), True), (h(b"x"), 2**63)]:
            with self.subTest(record=record), self.assertRaises(ValueError):
                g0._key(self.ctx, [record])

    def test_unique_record_bound(self):
        saved = g0.MAX_UNIQUE
        g0.MAX_UNIQUE = 1
        try:
            with tempfile.TemporaryDirectory() as root:
                self.assertEqual(g0.load_or_build(Path(root) / "cache", self.ctx, self.records),
                                 (None, "BYPASS"))
        finally:
            g0.MAX_UNIQUE = saved


if __name__ == "__main__":
    unittest.main()
