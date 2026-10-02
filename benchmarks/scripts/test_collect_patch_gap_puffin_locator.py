import importlib.util
import os
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("collect_patch_gap_puffin_locator.py")
SPEC = importlib.util.spec_from_file_location("patch_gap_puffin_locator", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class PatchGapPuffinLocatorTests(unittest.TestCase):
    def test_verbose_parser_accepts_glog_prefix_and_uses_last_value(self):
        log = (
            "I0101 first src_deflates_bit: 8:9\n"
            "noise\n"
            "I0101 final src_deflates_bit: 16:7,32:8\n"
        )
        self.assertEqual(
            [
                {"bitOffset": 16, "bitLength": 7},
                {"bitOffset": 32, "bitLength": 8},
            ],
            MODULE.parse_src_deflates_bit(log),
        )

    def test_extent_parser_accepts_empty_and_rejects_overlap(self):
        self.assertEqual([], MODULE.parse_extent_list(""))
        with self.assertRaises(MODULE.LocatorError):
            MODULE.parse_extent_list("8:16,20:8")
        with self.assertRaises(MODULE.LocatorError):
            MODULE.parse_extent_list("8:0")

    def test_container_type_accepts_numeric_system_text_json_enums(self):
        self.assertEqual("zip", MODULE.container_type(0))
        self.assertEqual("gzip", MODULE.container_type(1))
        self.assertEqual("zlib", MODULE.container_type(2))
        with self.assertRaises(MODULE.LocatorError):
            MODULE.container_type(4)

    def test_locate_one_requires_exact_reconstruction(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.bin"
            source.write_bytes(b"x")
            fake = root / "puffin"
            fake.write_text(
                "#!/usr/bin/env python3\n"
                "import shutil, sys\n"
                "args = {a.split('=', 1)[0]: a.split('=', 1)[1] for a in sys.argv[1:] if '=' in a}\n"
                "shutil.copyfile(args['--src_file'], args['--dst_file'])\n"
                "print('I0000 src_deflates_bit: 0:8', file=sys.stderr)\n",
                encoding="utf-8",
            )
            fake.chmod(fake.stat().st_mode | 0o111)
            work = root / "work"
            work.mkdir()

            result = MODULE.locate_one(
                fake,
                source,
                "zlib",
                MODULE.sha256_file(source),
                work,
            )

            self.assertTrue(result["succeeded"])
            self.assertEqual("OK", result["detail"])
            self.assertEqual([{"bitOffset": 0, "bitLength": 8}], result["deflateBitExtents"])
            self.assertEqual(MODULE.sha256_file(source), result["reconstructedSha256"])

    def test_build_provenance_preserves_exact_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "build-provenance.bin"
            raw = b"compiler=clang\r\nflags=-O2\x00toolchain"
            path.write_bytes(raw)

            encoded, digest = MODULE.encode_build_provenance(path)

            import base64
            self.assertEqual(raw, base64.b64decode(encoded))
            self.assertEqual(MODULE.sha256_bytes(raw), digest)

    def test_empty_build_provenance_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "empty"
            path.write_bytes(b"")
            with self.assertRaises(MODULE.LocatorError):
                MODULE.encode_build_provenance(path)

    def test_canonical_bytes_have_no_trailing_newline(self):
        payload = MODULE.canonical_bytes({"z": 1, "a": 2})
        self.assertEqual(b'{"a":2,"z":1}', payload)
        self.assertFalse(payload.endswith(b"\n"))


if __name__ == "__main__":
    unittest.main()
