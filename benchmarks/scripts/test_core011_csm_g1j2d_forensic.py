"""G1-J2D strict original payload replay, byte-level rolling window and PE guards."""
import copy
import hashlib
import json
from pathlib import Path
import random
import struct
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core011_csm_g1j2d_forensic as j


class FrozenCorpusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.contract = json.loads(j.CONTRACT.read_text())

    def test_previously_archived_hashes_are_locked(self):
        c=j.verify_contract(copy.deepcopy(self.contract))
        self.assertEqual(c["versions"]["base"]["packageSha256"],
                         "8f33bbff68fbacfaaee17b66e25861d888da8b0f9a524891fb2627f9af2683f3")
        self.assertFalse(c["decisionRunsAllowed"])
        self.assertEqual(c["expectedOriginalPlanReusedChunks"],0)

    def test_fail_closed_on_hash_or_version_drift(self):
        c=copy.deepcopy(self.contract)
        c["versions"]["base"]["dllSha256"]="not-a-hex"
        with self.assertRaisesRegex(ValueError,"bad expected"):
            j.verify_contract(c)
        c=copy.deepcopy(self.contract)
        c["versions"]["target"]["version"]="4.15.0"
        with self.assertRaisesRegex(ValueError,"source version"):
            j.verify_contract(c)
        c=copy.deepcopy(self.contract)
        c["decisionRunsAllowed"]=True
        with self.assertRaisesRegex(ValueError,"decision"):
            j.verify_contract(c)
        c=copy.deepcopy(self.contract)
        c["exactWindowSizes"]=[1024,4096,65536]
        with self.assertRaisesRegex(ValueError,"window protocol"):
            j.verify_contract(c)

    def test_real_package_drift_rejected_before_any_dll_analysis(self):
        with mock.patch.object(j.source,"get_bytes",return_value=b"forged package"):
            with self.assertRaisesRegex(ValueError,"NOT the previously recorded"):
                j.fetch_original_payloads(self.contract)


class OverlapTests(unittest.TestCase):
    def test_rolling_polynomial_matches_full_rehash_at_every_byte(self):
        data=bytes(range(99))
        for size in (1,3,16,51):
            windows=list(j.rolling(data,size))
            self.assertEqual(len(windows),len(data)-size+1)
            for offset,h in windows:
                oracle=0
                for x in data[offset:offset+size]:
                    oracle=(oracle*j.PRIME+x)&j.MASK
                self.assertEqual(h,oracle)

    def test_shifted_original_bytes_detected_at_byte_granularity(self):
        rand=random.Random(473)
        base=bytes(rand.randrange(256) for _ in range(180000))
        # Entire source appears in target at offset 137: deliberately
        # misaligned to the 256-byte source anchor and 4096 fixed blocks.
        target=b"\x3a"*137+base+b"\xe7"*211
        layout={"sections":[{"name":".text","rawOffset":0,"rawBytes":len(base)}]}
        for size in (4096,16384,65536):
            found=j.window_probe(base,target,size,256,layout)
            self.assertGreater(found["baseAnchorsWithExactTargetMatch"],0)
            self.assertEqual(found["baseAnchorsWithExactTargetMatch"],found["baseAnchors"])
            self.assertEqual(found["baseWindowCoverageLowerBoundBytes"],
                             ((len(base)-size)//256)*256+size)
            self.assertEqual(found["examplesFirstTargetOccurrenceNotCanonicalAlignment"][0]["firstTargetOffset"],137)
        fixed=j.fixed_blocks(base,target)
        self.assertEqual(fixed["targetAlignedBlocksFoundInBase"],0)

    def test_pseudo_identical_nonstride_shift_and_gap(self):
        rand=random.Random(774)
        base=bytes(rand.randrange(256) for _ in range(200000))
        target=b"shifted"*13+base[:75000]+b"new"*1000+base[75000:]
        sections={"sections":[{"name":".text","rawOffset":0,"rawBytes":len(base)}]}
        got=j.window_probe(base,target,4096,256,sections)
        self.assertGreater(got["baseAnchorsWithExactTargetMatch"],0)
        self.assertLess(got["baseAnchorsWithExactTargetMatch"],got["baseAnchors"])
        self.assertEqual(j.merged_coverage([(0,100),(20,200),(300,320)]),220)

    def test_random_distinct_data_cannot_match_four_kibibyte_window(self):
        base=b"a"*90000
        target=b"b"*100000
        pe={"sections":[{"name":".text","rawOffset":0,"rawBytes":len(base)}]}
        got=j.window_probe(base,target,4096,256,pe)
        self.assertEqual(got["baseAnchorsWithExactTargetMatch"],0)
        self.assertEqual(got["baseWindowCoverageLowerBoundBytes"],0)

    def test_duplicate_repeated_content_is_not_double_counted(self):
        base=b"\x00"*120000
        target=b"\x00"*124000
        pe={"sections":[{"name":".text","rawOffset":0,"rawBytes":len(base)}]}
        got=j.window_probe(base,target,16384,256,pe)
        self.assertEqual(got["baseAnchorsWithExactTargetMatch"],got["baseAnchors"])
        self.assertLessEqual(got["baseWindowCoverageLowerBoundBytes"],len(base))
        self.assertLessEqual(got["firstTargetWindowCoverageLowerBoundBytes"],len(target))

    def test_pe_parser_rejects_corrupt_images(self):
        with self.assertRaisesRegex(ValueError,"not PE"):
            j.pe_layout(b"\0"*8192)
        data=bytearray(8192)
        data[:2]=b"MZ"
        struct.pack_into("<I",data,0x3c,128)
        data[128:132]=b"PE\x00\x00"
        with self.assertRaisesRegex(ValueError,"unbounded"):
            j.pe_layout(bytes(data))

    def test_pe_sections_compare_relative_offsets_not_false_bases(self):
        layout_b={"sections":[{"name":".text","rawOffset":10,"rawBytes":12}]}
        layout_t={"sections":[{"name":".text","rawOffset":20,"rawBytes":12}]}
        base=bytearray(b"x"*40)
        target=bytearray(b"z"*40)
        base[10:22]=b"abcdefghijkl"
        target[20:32]=b"abcdefghijkl"
        out=j.same_offset_detail(bytes(base),bytes(target),layout_b,layout_t)
        self.assertEqual(out["sectionRelativeFileOffsetByteMatches"][".text"]
                         ["equalBytesSameRelativeSectionOffset"],12)


if __name__=="__main__":
    unittest.main()
