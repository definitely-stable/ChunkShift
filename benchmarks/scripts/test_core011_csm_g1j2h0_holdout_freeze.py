#!/usr/bin/env python3
"""Negative tests for SHA-only independent NuGet holdout prefreeze."""
import io
from pathlib import Path
import struct
import sys
import unittest
import warnings
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core011_csm_g1j2h0_holdout_freeze as h


def fake_managed_pe():
    raw = bytearray(512)
    raw[:2] = b"MZ"
    struct.pack_into("<I", raw, 0x3c, 0x80)
    raw[0x80:0x84] = b"PE\x00\x00"
    struct.pack_into("<HHIIIHH", raw, 0x84,
                     0x14c, 1, 0, 0, 0, 224, 0)
    optional = 0x98
    struct.pack_into("<H", raw, optional, 0x10b)
    struct.pack_into("<I", raw, optional + 92, 16)
    struct.pack_into("<II", raw, optional + 96 + 14 * 8, 0x2000, 0x48)
    return bytes(raw)


def archive(assembly=None, duplicate=False, name=h.ENTRY):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr(name, fake_managed_pe() if assembly is None else assembly)
        if duplicate:
            with warnings.catch_warnings():
                warnings.simplefilter("ignore", UserWarning)
                z.writestr(name, fake_managed_pe())
    return stream.getvalue()


class J2H0Tests(unittest.TestCase):
    def test_exact_preregistered_pair(self):
        self.assertEqual(h.VERSIONS, ("13.0.2", "13.0.3"))
        self.assertEqual(h.ENTRY, "lib/net6.0/Newtonsoft.Json.dll")
        self.assertEqual(h.package_url("13.0.2"),
                         "https://api.nuget.org/v3-flatcontainer/"
                         "newtonsoft.json/13.0.2/newtonsoft.json.13.0.2.nupkg")
        with self.assertRaises(ValueError):
            h.package_url("13.0.4")

    def test_valid_synthetic_managed_pe_only(self):
        pe = h.parse_managed_pe(fake_managed_pe())
        self.assertEqual(pe["clrDirectoryRva"], 0x2000)
        d = h.inspect_archive(archive())
        self.assertEqual(d["dllSha256"], h.digest(fake_managed_pe(), "sha256"))
        self.assertEqual(d["dllBytes"], 512)
        self.assertNotEqual(d["packageSha256"], d["dllSha256"])

    def test_corrupt_or_wrong_pe_rejected(self):
        variants = [
            fake_managed_pe()[:50],
            b"\x00" * 512,
            bytearray(fake_managed_pe()),
        ]
        struct.pack_into("<II", variants[-1], 0x98 + 96 + 14 * 8, 0, 0)
        for data in variants:
            with self.subTest(payload=data[:12]), self.assertRaises(ValueError):
                h.parse_managed_pe(data)

    def test_no_alternate_entry_or_duplicate(self):
        with self.assertRaisesRegex(ValueError, "exact DLL path"):
            h.inspect_archive(archive(name="lib/netstandard2.0/Newtonsoft.Json.dll"))
        with self.assertRaisesRegex(ValueError, "exact DLL path"):
            h.inspect_archive(archive(duplicate=True))

    def test_invalid_zip_and_resource_bounds(self):
        with self.assertRaises(ValueError):
            h.inspect_archive(b"notzip")
        with self.assertRaisesRegex(ValueError, "resource bound"):
            h.inspect_archive(b"x" * (h.MAX_PACKAGE_BYTES + 1))
        with self.assertRaisesRegex(ValueError, "DLL file size"):
            h.inspect_archive(archive(assembly=b""))

    def test_identical_pair_must_fail(self):
        a = h.inspect_archive(archive())
        with self.assertRaisesRegex(ValueError, "unchanged"):
            h.validate_pair(a, a)
        b = dict(a, dllSha256="0" * 64)
        self.assertTrue(h.validate_pair(a, b))
        with self.assertRaisesRegex(ValueError, "identical package"):
            h.validate_pair(dict(a, dllSha256="a" * 64), b)


if __name__ == "__main__":
    unittest.main()
