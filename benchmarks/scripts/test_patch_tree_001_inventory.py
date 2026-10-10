#!/usr/bin/env python3
"""Deterministic PATCH-TREE-001 virtual-tree and frozen-pair regression tests."""
import hashlib
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("patch_tree_001_inventory.py")
SPEC = importlib.util.spec_from_file_location("patch_tree_001_inventory", SCRIPT)
mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mod)


class TreeFoundationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "corpus"
        self.tree = self.root / "tree" / "node-win-x64"
        self.old = self.tree / "24.19.0"
        self.new = self.tree / "24.20.0"
        self.old.mkdir(parents=True)
        self.new.mkdir(parents=True)

    def put(self, root, name, content):
        p = root / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(content)
        return p

    def test_utf8_order_offsets_and_empty_files(self):
        self.put(self.old, "b", b"CD")
        self.put(self.old, "a", b"AB")
        self.put(self.old, "aa", b"")
        self.put(self.old, "\u03b1", b"E")
        record = mod.layout(self.old)
        self.assertEqual([x["path"] for x in record["files"]],
                         ["a", "aa", "b", "\u03b1"])
        self.assertEqual([x["offset"] for x in record["files"]], [0, 2, 2, 4])
        self.assertEqual(record["totalBytes"], 5)
        self.assertEqual(mod.read_at(self.old, record, 1, 4), b"BCDE")
        self.assertEqual(mod.read_at(self.old, record, 5, 0), b"")

    def test_determinism_independent_of_creation_order(self):
        self.put(self.old, "z", b"1")
        self.put(self.old, "a/x", b"2")
        self.put(self.new, "a/x", b"2")
        self.put(self.new, "z", b"1")
        first, second = mod.layout(self.old), mod.layout(self.new)
        self.assertEqual(first, second)
        self.assertNotEqual(first["layoutSha256"], mod.sha256(b""))

    def test_exact_pair_inventory_and_no_patch_claim(self):
        self.put(self.old, "same", b"X")
        self.put(self.new, "same", b"X")
        self.put(self.old, "changed", b"OLD")
        self.put(self.new, "changed", b"NEW")
        self.put(self.old, "removed", b"R")
        self.put(self.new, "added", b"A")
        old_hash, new_hash = (hashlib.sha256(x).hexdigest()
                              for x in (b"OLD", b"NEW"))
        add_hash = hashlib.sha256(b"A").hexdigest()
        pair = {"family": "node-win-x64", "base": "24.19.0",
                "target": "24.20.0",
                "changed": [{"path": "changed", "baseSize": 3,
                             "baseSha256": old_hash, "targetSize": 3,
                             "targetSha256": new_hash}],
                "added": [{"path": "added", "size": 1, "sha256": add_hash}],
                "removed": ["removed"], "identicalFiles": 1,
                "identicalBytes": 1}
        payload = (json.dumps({"schema": "chunkshift.patch-pairs.v1",
                               "pairs": [pair]}, indent=1, sort_keys=True) + "\n").encode()
        (self.root / "pairs.json").write_bytes(payload)
        result = mod.inventory(self.root, hashlib.sha256(payload).hexdigest())
        self.assertEqual(result["pairCount"], 1)
        self.assertEqual(result["pairs"][0]["datasetRole"], "holdout")
        self.assertEqual(result["status"], "INVENTORY_ONLY_NOT_PATCH_EVIDENCE")
        pair["identicalBytes"] = 5
        (self.root / "pairs.json").write_bytes(
            (json.dumps({"schema": "chunkshift.patch-pairs.v1",
                         "pairs": [pair]}, indent=1, sort_keys=True) + "\n").encode())
        with self.assertRaisesRegex(ValueError, "frozen pair classification"):
            b = (self.root / "pairs.json").read_bytes()
            mod.inventory(self.root, hashlib.sha256(b).hexdigest())

    def test_frozen_lock_is_required(self):
        (self.root / "pairs.json").write_text("{}")
        with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
            mod.inventory(self.root, "0" * 64)

    def test_invalid_reads_rejected(self):
        self.put(self.old, "a", b"123")
        data = mod.layout(self.old)
        for offset, length in [(-1, 1), (4, 0), (2, 2),
                               (0, mod.MAX_READ + 1)]:
            with self.subTest(offset=offset, length=length):
                with self.assertRaisesRegex(ValueError, "invalid virtual read"):
                    mod.read_at(self.old, data, offset, length)

    def test_deleted_source_fails_read(self):
        p = self.put(self.old, "a", b"A")
        data = mod.layout(self.old)
        p.unlink()
        with self.assertRaises((FileNotFoundError, ValueError)):
            mod.read_at(self.old, data, 0, 1)

    def test_tampered_virtual_layout_path_is_rejected(self):
        self.put(self.old, "a", b"A")
        record = mod.layout(self.old)
        record["files"][0]["path"] = "../../outside"
        with self.assertRaisesRegex(ValueError, "ambiguous path"):
            mod.read_at(self.old, record, 0, 1)

    def test_virtual_reader_refuses_symlink_ancestor_after_inventory(self):
        self.put(self.old, "nested/x", b"X")
        record = mod.layout(self.old)
        real = self.old / "nested"
        replacement = self.old / "saved"
        real.rename(replacement)
        try:
            real.symlink_to(replacement, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("symlinks unavailable on runner")
        with self.assertRaisesRegex(ValueError, "symlink/special virtual source directory"):
            mod.read_at(self.old, record, 0, 1)

    def test_symlink_file_rejected(self):
        p = self.put(self.old, "real", b"A")
        try:
            (self.old / "alias").symlink_to(p)
        except (OSError, NotImplementedError):
            self.skipTest("symlinks unavailable on runner")
        with self.assertRaisesRegex(ValueError, "non-regular file"):
            mod.layout(self.old)

    def test_symlink_directory_rejected(self):
        outside = self.root / "outside"
        outside.mkdir()
        try:
            (self.old / "alias").symlink_to(outside, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("symlinks unavailable on runner")
        with self.assertRaisesRegex(ValueError, "symlink/special directory"):
            mod.layout(self.old)

    def test_casefold_collision_rejected(self):
        self.put(self.old, "A", b"X")
        try:
            self.put(self.old, "a", b"Y")
        except OSError:
            self.skipTest("case-insensitive filesystem")
        if len(list(self.old.iterdir())) == 1:
            self.skipTest("case-insensitive filesystem")
        with self.assertRaisesRegex(ValueError, "casefold collision"):
            mod.layout(self.old)

    def test_unsafe_paths_rejected(self):
        for value in ("../x", "/a", "a//b", "a\\b", "a/./b",
                      "con.txt", "x:y", "a.", "a ", "e\u0301"):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    mod.canonical_path(value)

    def test_unsafe_version_labels_rejected(self):
        for value in ("../x", "x/y", "", ".", "..", "\\x", "x y"):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    mod.label(value)

    def test_length_and_path_affect_layout_digest(self):
        self.put(self.old, "a", b"1234")
        first = mod.layout(self.old)
        (self.old / "a").rename(self.old / "b")
        self.assertNotEqual(first["layoutSha256"],
                            mod.layout(self.old)["layoutSha256"])



    def test_calibration_only_does_not_access_any_holdout_tree(self):
        # Holdout pair metadata is fixed by pairs.json, but the source tree is
        # deliberately ABSENT. Reading even a single sealed holdout file fails.
        self.old.rmdir()
        self.new.rmdir()
        self.tree.rmdir()
        pairs = {"schema": "chunkshift.patch-pairs.v1",
                 "pairs": [{"family": "node-win-x64", "base": "24.19.0",
                            "target": "24.20.0"}]}
        body = (json.dumps(pairs, sort_keys=True) + "\n").encode()
        (self.root / "pairs.json").write_bytes(body)
        result = mod.inventory(self.root, hashlib.sha256(body).hexdigest(),
                               calibration_only=True)
        self.assertEqual(result["pairCount"], 0)
        self.assertEqual(result["pairs"], [])
        with self.assertRaisesRegex(ValueError, "missing/symlink tree root"):
            mod.inventory(self.root, hashlib.sha256(body).hexdigest())

if __name__ == "__main__":
    unittest.main()
