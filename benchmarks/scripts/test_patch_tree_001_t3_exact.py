#!/usr/bin/env python3
"""T3-A deterministic cross-path proof and independent fixture oracle."""
import hashlib
import json
import struct
import tempfile
import unittest
from pathlib import Path

import patch_tree_001_inventory as inv
import patch_tree_001_t3_exact as t3


def decode_independently(buf: bytes):
    if not buf:
        return []
    assert buf[:5] == b"PT3A\0"
    count = struct.unpack_from("<I", buf, 5)[0]
    pos = 9
    result = []
    for _ in range(count):
        a = struct.unpack_from("<I", buf, pos)[0]
        pos += 4
        target = buf[pos:pos + a].decode("utf-8", "strict")
        pos += a
        n = struct.unpack_from("<I", buf, pos)[0]
        pos += 4
        source = buf[pos:pos + n].decode("utf-8", "strict")
        pos += n
        length = struct.unpack_from("<Q", buf, pos)[0]
        pos += 8
        digest = buf[pos:pos + 32].hex()
        pos += 32
        result.append(dict(targetPath=target, sourcePath=source, length=length, sha256=digest))
    assert pos == len(buf)
    return result


class T3ExactTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.parent = Path(tmp.name)
        self.old = self.parent / "old"
        self.new = self.parent / "new"
        self.old.mkdir()
        self.new.mkdir()

    def put(self, root, name, content):
        target = root.joinpath(*name.split("/"))
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)

    def plan(self):
        before = inv.layout(self.old)
        after = inv.layout(self.new)
        out, blob = t3.plan(before, after)
        self.assertEqual(out["matches"], decode_independently(blob))
        self.assertEqual(out["t3ReferenceBytes"], len(blob))
        self.assertEqual(out["referenceSha256"], hashlib.sha256(blob).hexdigest())
        self.assertEqual(out["addedLaneNetSavingsBytes"],
                         out["t0AddedRawBytes"] - out["t3AddedRawBytes"] - len(blob))
        return before, after, out, blob

    def test_exact_rename_is_fully_charged_and_reconstructed(self):
        large = b"well-known-blob" * 20000
        self.put(self.old, "old/release.bin", large)
        self.put(self.new, "new/renamed.bin", large)
        old, new, outcome, blob = self.plan()
        self.assertEqual(outcome["exactMappedCount"], 1)
        self.assertEqual(outcome["t3AddedRawBytes"], 0)
        self.assertGreater(outcome["addedLaneNetSavingsBytes"], 0)
        self.assertEqual(outcome["matches"][0]["sourcePath"], "old/release.bin")
        t3.reconstruct_fixture(self.old, self.new, self.parent / "stage", old, new, outcome)
        self.assertEqual(inv.layout(self.parent / "stage"), new)

    def test_cross_path_duplicate_is_reusable_without_exclusive_consumption(self):
        payload = b"DATA" * 1000
        self.put(self.old, "z", payload)
        self.put(self.old, "b", payload)
        self.put(self.new, "b", payload)
        self.put(self.new, "c", payload)
        self.put(self.new, "d", payload)
        old, new, outcome, _ = self.plan()
        self.assertEqual(outcome["exactMappedCount"], 2)
        self.assertTrue(all(x["sourcePath"] == "b" for x in outcome["matches"]))
        t3.reconstruct_fixture(self.old, self.new, self.parent / "stage", old, new, outcome)

    def test_shortest_encoded_source_path_then_utf8_order(self):
        payload = b"Z" * 2000
        for name in ("z/long-name", "b", "a"):
            self.put(self.old, name, payload)
        self.put(self.new, "target", payload)
        _, _, outcome, _ = self.plan()
        self.assertEqual(outcome["matches"][0]["sourcePath"], "a")

    def test_changed_same_path_does_not_become_rename_or_similarity(self):
        self.put(self.old, "app.dll", b"a" * 5000)
        self.put(self.new, "app.dll", b"b" * 5000)
        _, _, outcome, blob = self.plan()
        self.assertEqual(outcome["addedCount"], 0)
        self.assertEqual(outcome["exactMappedCount"], 0)
        self.assertEqual(blob, b"")

    def test_renamed_but_changed_is_raw_pending_future_t3b(self):
        self.put(self.old, "old", b"A" * 5000)
        self.put(self.new, "new", b"A" * 4999 + b"B")
        _, _, outcome, _ = self.plan()
        self.assertEqual(outcome["exactMappedCount"], 0)
        self.assertEqual(outcome["t3AddedRawBytes"], 5000)

    def test_tiny_and_empty_added_files_never_cost_more(self):
        self.put(self.old, "source", b"A")
        self.put(self.old, "empty", b"")
        self.put(self.new, "target", b"A")
        self.put(self.new, "second", b"")
        _, _, outcome, blob = self.plan()
        self.assertEqual(outcome["exactMappedCount"], 0)
        self.assertEqual(outcome["t0AddedRawBytes"], 1)
        self.assertEqual(blob, b"")

    def test_mapping_is_stable_independent_of_file_creation_order(self):
        payload = b"M" * 5000
        self.put(self.old, "z", payload)
        self.put(self.old, "a", payload)
        self.put(self.new, "x", payload)
        _, _, one, onebuf = self.plan()
        self.old.joinpath("a").unlink()
        self.old.joinpath("z").unlink()
        self.put(self.old, "a", payload)
        self.put(self.old, "z", payload)
        _, _, two, twobuf = self.plan()
        self.assertEqual(one, two)
        self.assertEqual(onebuf, twobuf)

    def test_tampered_layout_digest_or_offsets_rejected(self):
        payload = b"Q" * 5000
        self.put(self.old, "a", payload)
        self.put(self.new, "b", payload)
        before, after, _, _ = self.plan()
        before["files"][0]["offset"] = 1
        with self.assertRaisesRegex(ValueError, "virtual offsets"):
            t3.plan(before, after)
        before = inv.layout(self.old)
        before["files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "layout identity mismatch"):
            t3.plan(before, after)

    def test_noncanonical_path_and_hash_rejected(self):
        self.put(self.old, "a", b"X" * 3000)
        self.put(self.new, "b", b"X" * 3000)
        old, new, _, _ = self.plan()
        old["files"][0]["path"] = "../escape"
        with self.assertRaises(ValueError):
            t3.plan(old, new)
        old = inv.layout(self.old)
        old["files"][0]["sha256"] = old["files"][0]["sha256"].upper()
        with self.assertRaisesRegex(ValueError, "noncanonical SHA-256"):
            t3.plan(old, new)

    def test_stale_changed_source_rejected_during_real_byte_copy(self):
        content = b"P" * 5000
        self.put(self.old, "source", content)
        self.put(self.new, "target", content)
        old, new, outcome, _ = self.plan()
        self.put(self.old, "source", b"B" * 5000)
        with self.assertRaisesRegex(ValueError, "source bytes or metadata changed"):
            t3.reconstruct_fixture(self.old, self.new, self.parent / "stage", old, new, outcome)

    def test_source_symlink_substitution_rejected(self):
        data = b"S" * 5000
        self.put(self.old, "nested/source", data)
        self.put(self.new, "target", data)
        old, new, outcome, _ = self.plan()
        nested = self.old / "nested"
        nested.rename(self.old / "detached")
        try:
            nested.symlink_to(self.old / "detached", target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("symlink creation unavailable")
        with self.assertRaisesRegex(ValueError, "symlink/non-directory fixture parent"):
            t3.reconstruct_fixture(self.old, self.new, self.parent / "stage", old, new, outcome)

    def test_mapping_identity_tamper_rejected(self):
        data = b"C" * 5000
        self.put(self.old, "base", data)
        self.put(self.new, "target", data)
        old, new, outcome, _ = self.plan()
        outcome["matches"][0]["sourcePath"] = "other"
        with self.assertRaisesRegex(ValueError, "mapping differs"):
            t3.reconstruct_fixture(self.old, self.new, self.parent / "stage", old, new, outcome)

    def test_staging_existing_refused(self):
        data = b"E" * 5000
        self.put(self.old, "base", data)
        self.put(self.new, "target", data)
        old, new, outcome, _ = self.plan()
        (self.parent / "stage").mkdir()
        with self.assertRaisesRegex(ValueError, "staging target must be newly created"):
            t3.reconstruct_fixture(self.old, self.new, self.parent / "stage", old, new, outcome)

    def test_size_cap_fail_closed(self):
        data = b"A" * 5000
        self.put(self.old, "base", data)
        self.put(self.new, "target", data)
        old, new = inv.layout(self.old), inv.layout(self.new)
        prior = t3.MAX_SIDECAR_BYTES
        try:
            t3.MAX_SIDECAR_BYTES = 8
            with self.assertRaisesRegex(ValueError, "fixed cap"):
                t3.plan(old, new)
        finally:
            t3.MAX_SIDECAR_BYTES = prior

    def test_json_summary_replay_is_deterministic(self):
        data = b"B" * 7000
        self.put(self.old, "old", data)
        self.put(self.new, "new", data)
        old, new, first, _ = self.plan()
        second, _ = t3.plan(json.loads(json.dumps(old)), json.loads(json.dumps(new)))
        self.assertEqual(json.dumps(first, sort_keys=True), json.dumps(second, sort_keys=True))


if __name__ == "__main__":
    unittest.main()
