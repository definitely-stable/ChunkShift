#!/usr/bin/env python3
"""J2J CSP wire-parser adversarial synthetic tests (no NuGet/network required)."""
from pathlib import Path
import struct
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core011_csm_g1j2j_wire_attribution as j
import core011_csm_g1j2i_physical_ab as original


def section(name, payload, flags=1, crc=False):
    size = len(payload) + (4 if crc else 0)
    head = struct.pack("<4sIQ", name, flags, size)
    return head + payload + (
        struct.pack("<I", j.crc32c(head + payload)) if crc else b"")


def fixture(with_base=True, dictionary=False,
            wrong_index=False, wrong_footer=False):
    csm = b"synthetic-csm-only-test-not-a-production-manifest"
    pre = struct.pack("<4sHHQQQ", b"CSP1", 1, 32, 0, 0, 0)
    output = bytearray(pre)
    tcsm_offset = len(output)
    output += section(b"TCSM", csm)
    base_offset = len(output) if with_base else 0
    if with_base:
        output += section(b"BASE", b"\x43" * 32, flags=0)

    payl_offset = len(output)
    codec = 1 if dictionary else 0
    dictionaries = b"\xbb" * 32 if dictionary else b""
    stored = b"\x12\x13\x14\x15\x16"
    entry = b"\xaa" * 32 + struct.pack("<IBBH", len(stored), codec,
                                       1 if dictionary else 0, 0)
    entry += dictionaries + stored
    payl_prefix = struct.pack("<IIQ", 1, 0, 0)
    output += section(b"PAYL", payl_prefix + entry, crc=True)
    entry_offset = payl_offset + 16 + 16

    pidx_offset = len(output)
    index = struct.pack("<II", 1, 1)
    index += struct.pack("<QQIBBH", 0,
                         entry_offset + (1 if wrong_index else 0),
                         len(stored), codec, 1 if dictionary else 0, 0)
    output += section(b"PIDX", index, crc=True)
    foot_offset = len(output)
    foot = struct.pack("<QQQQQQQ", tcsm_offset, base_offset,
                       pidx_offset, payl_offset + (1 if wrong_footer else 0),
                       1, 1, 0)
    output += section(b"FOOT", foot)
    physical = len(output) + 64
    output += b"CSPT" + struct.pack("<HHQQ", 1, 64, foot_offset, physical)
    output += b"\0" * 40
    assert len(output) == physical
    return bytes(output), csm


class WireAttributionTests(unittest.TestCase):
    def test_castagnoli_reference(self):
        self.assertEqual(j.crc32c(b"123456789"), 0xe3069283)

    def test_exact_conservation_with_base_raw_and_zstd_dictionary(self):
        for dict_encoding in (False, True):
            for with_base in (False, True):
                with self.subTest(dictionary=dict_encoding, base=with_base):
                    patch, csm = fixture(with_base=with_base, dictionary=dict_encoding)
                    report = j.counted_bytes(patch, csm, with_base)
                    self.assertEqual(report["physicalBytes"], len(patch))
                    self.assertEqual(sum(report["ledger"].values()), len(patch))
                    self.assertEqual(report["entryCounts"]["payloadEntries"], 1)
                    self.assertEqual(report["ledger"]["pidxEntryRecords"], 24)
                    self.assertEqual(report["ledger"]["paylFixedEntryHeaders"], 40)
                    self.assertEqual(report["ledger"]["paylDictionaryReferences"],
                                     32 if dict_encoding else 0)
                    self.assertEqual(report["storedByEncodingBytes"]["zstdDictionary" if dict_encoding else "raw"], 5)

    def test_corrupt_crc_and_physical_accounting_fail_closed(self):
        raw, csm = fixture()
        payload_marker = b"\x12\x13\x14\x15\x16"
        self.assertEqual(raw.count(payload_marker), 1)
        changed = raw.replace(payload_marker, b"\x12\x13\xff\x15\x16")
        with self.assertRaisesRegex(ValueError, "PAYL CRC"):
            j.counted_bytes(changed, csm, True)
        for corrupted in (raw + b"x", raw[:-1],
                          b"BAD!" + raw[4:]):
            with self.assertRaises(ValueError):
                j.counted_bytes(corrupted, csm, True)

    def test_index_offset_and_foot_offset_invalid_even_with_valid_crc(self):
        for kind in ("wrong_index", "wrong_footer"):
            kwargs = {kind: True}
            raw, csm = fixture(**kwargs)
            with self.assertRaisesRegex(ValueError, "PIDX disagrees|FOOT offsets"):
                j.counted_bytes(raw, csm, True)

    def test_wrong_csm_or_base_binding_rejected(self):
        raw, csm = fixture()
        with self.assertRaisesRegex(ValueError, "target CSM differs"):
            j.counted_bytes(raw, csm + b"x", True)
        with self.assertRaisesRegex(ValueError, "BASE association"):
            j.counted_bytes(raw, csm, False)
        raw2, csm2 = fixture(with_base=False)
        with self.assertRaisesRegex(ValueError, "base association"):
            j.counted_bytes(raw2, csm2, True)

    def test_component_delta_and_invalid_one(self):
        raw, csm = fixture(with_base=True)
        left = j.counted_bytes(raw, csm, True)
        new, other_csm = fixture(with_base=True, dictionary=True)
        right = j.counted_bytes(new, other_csm, True)
        delta = j.component_delta(left, right)
        self.assertEqual(sum(delta.values()), len(new) - len(raw))
        self.assertEqual(delta["paylDictionaryReferences"], 32)
        with self.assertRaisesRegex(ValueError, "wire ledger schema"):
            j.component_delta({"schema": "wrong"}, right)

    def test_upstream_frozen_matrix_not_changed(self):
        self.assertEqual(set(original.EXPECTED), {"roslyn", "newtonsoft"})
        self.assertEqual(original.PROFILES, ("stable64k", "lab16k"))
        self.assertEqual(original.MODES, ("based", "standalone"))
        self.assertEqual(original.EXPECTED["roslyn"]["baselineCspBytes"]["based"], 1260098)


if __name__ == "__main__":
    unittest.main()
