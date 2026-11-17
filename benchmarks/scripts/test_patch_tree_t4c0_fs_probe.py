#!/usr/bin/env python3
"""Non-invasive T4-C0 deterministic format and fail-closed negative fixtures."""
import pathlib
import tempfile
import unittest

import patch_tree_t4c0_fs_probe as probe


class T4C0FormatTests(unittest.TestCase):
    def test_pinned_canonical_le_pointer_and_checksum(self):
        raw = probe.encode_head(0, 1, "0" * 64)
        self.assertEqual(88, len(raw))
        self.assertEqual(bytes.fromhex("543443307630210a"), raw[:8])
        self.assertEqual("0a33e8319dd69e441921385b256f5c8333e699f3d8b02af1d0becb3215c4ce67",
                         raw[56:].hex())
        self.assertEqual((0, 1, "0" * 64), probe.decode_head(raw))

    def test_checksum_covers_both_cursor_components_and_generation(self):
        raw = probe.encode_head(5, 200, "1" * 64)
        self.assertEqual((5, 200, "1" * 64), probe.decode_head(raw))
        for pos in (0, 7, 8, 15, 16, 23, 24, 55, 56, 87):
            altered = bytearray(raw)
            altered[pos] ^= 1
            with self.subTest(pos=pos), self.assertRaises(ValueError):
                probe.decode_head(bytes(altered))
        for corrupted in (raw[:-1], raw + b"x", b"", b"x" * 88):
            with self.assertRaises(ValueError):
                probe.decode_head(corrupted)

    def test_reject_noncanonical_or_overflowing_cursor(self):
        for ep, seq, digest in ((-1, 1, "0" * 64), (0, -1, "0" * 64),
                                (2**64, 2, "0" * 64),
                                (0, 2**64, "0" * 64),
                                (0, 1, "F" * 64), (0, 1, "x" * 64),
                                (0, 1, "short")):
            with self.subTest(ep=ep, seq=seq, digest=digest[:5]):
                with self.assertRaises(ValueError):
                    probe.encode_head(ep, seq, digest)

    def test_in_place_generation_tamper_is_detected_before_publication(self):
        with tempfile.TemporaryDirectory(prefix="chunkshift-t4c0-unit-") as temporary:
            root = pathlib.Path(temporary) / "controlled"
            old, new, _ = probe.setup(root)
            self.assertEqual((0, 1, old), probe.visible(root))
            (root / "generations" / new / "a").write_bytes(b"bad")
            with self.assertRaises(AssertionError):
                probe.verify_generation(root, new)
            self.assertEqual((0, 1, old), probe.visible(root))


if __name__ == "__main__":
    unittest.main()
