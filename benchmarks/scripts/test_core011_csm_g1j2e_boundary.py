"""G1-J2E fail-closed CSM CBLK parser and boundary / content distinction."""
import json
from pathlib import Path
import struct
import sys
import unittest
from unittest import mock

sys.path.insert(0,str(Path(__file__).resolve().parent))
import core011_csm_g1j2e_boundary as e


def build_minimal_csm(chunks):
    """Only a parser unit fixture; intentionally NOT a valid authenticated CSM."""
    def section(kind,body=b""):
        return struct.pack("<4sIQ",kind,1,len(body))+body
    core=section(b"CORE")
    ids=b"".join(bytes.fromhex(cid) for length,cid in chunks)
    lengths=b"".join(struct.pack("<I",length) for length,cid in chunks)
    cblk=section(b"CBLK",struct.pack("<IIQQ",len(chunks),0,0,0)+ids+lengths+b"\0"*4)
    cend=section(b"CEND")
    foot=section(b"FOOT")
    return b"CSM1"+b"\0"*28+core+cblk+cend+foot+b"CSMT"+b"\0"*60


class CblkParserTests(unittest.TestCase):
    def test_exact_cblk_fields_lengths_contiguity(self):
        one="12"*32
        two="34"*32
        chunks=[(5,one),(7,two)]
        buf=build_minimal_csm(chunks)
        got=e.parse_csm_cblk(buf,12,2)
        self.assertEqual(got,[
            {"index":0,"offset":0,"length":5,"chunkId":one},
            {"index":1,"offset":5,"length":7,"chunkId":two}])

    def test_reject_corrupted_magic_or_count(self):
        buf=build_minimal_csm([(9,"0a"*32)])
        with self.assertRaisesRegex(ValueError,"not physical"):
            e.parse_csm_cblk(b"CSM2"+buf[4:],9,1)
        with self.assertRaisesRegex(ValueError,"chunk count"):
            e.parse_csm_cblk(buf,9,2)
        with self.assertRaisesRegex(ValueError,"original bytes"):
            e.parse_csm_cblk(buf,10,1)
        # Header size mismatch must fail before treating bytes as chunks.
        corrupt=bytearray(buf)
        # Synthetic CORE has a zero-length payload; skip both section headers.
        start=32+16+16
        struct.pack_into("<I",corrupt,start,4097)
        with self.assertRaisesRegex(ValueError,"invalid frozen CBLK"):
            e.parse_csm_cblk(bytes(corrupt),9,1)
        # A truncated/missing trailer is not an acceptable logical CSM.
        with self.assertRaises(ValueError):
            e.parse_csm_cblk(buf[:-64],9,1)
        # Mutating the CBLK reserved field must never be interpreted as an ID.
        reserved=bytearray(buf)
        struct.pack_into("<I",reserved,start+4,1)
        with self.assertRaisesRegex(ValueError,"invalid frozen CBLK"):
            e.parse_csm_cblk(bytes(reserved),9,1)

    def test_independent_scanner_oracle_rows(self):
        import tempfile
        with tempfile.TemporaryDirectory() as td:
            p=Path(td)/"scanner.tsv"
            p.write_text("0\t5\t"+"ab"*32+"\n5\t7\t"+"cd"*32+"\n")
            self.assertEqual(e.parse_scanner_tsv(p,12,2)[1]["offset"],5)
            p.write_text("0\t5\t"+"ab"*32+"\n6\t7\t"+"cd"*32+"\n")
            with self.assertRaisesRegex(ValueError,"scanner chunk gap"):
                e.parse_scanner_tsv(p,12,2)


class BoundaryClassificationTests(unittest.TestCase):
    def test_whole_chunk_present_but_opposite_boundary_different(self):
        a=b"A"*5000
        b=b"B"*5000
        base=a+b
        target=b"+"*123+a+b
        ca=[{"index":0,"offset":0,"length":5000,"chunkId":"1"*64},
            {"index":1,"offset":5000,"length":5000,"chunkId":"2"*64}]
        cb=[{"index":0,"offset":0,"length":len(target),"chunkId":"3"*64}]
        sample={0:123+5000}
        result=e.chunk_diagnostic(base,target,ca,cb,sample)
        self.assertEqual(result["countByClassification"]["EXACT_WHOLE_ONLY_UNALIGNED"],2)
        self.assertEqual(result["countByClassification"].get("EXACT_WHOLE_AT_CDC_BOUNDARY",0),0)

    def test_no_whole_chunk_but_partial_identical_window(self):
        source=b"A"*8192+b"B"*8192
        other=b"A"*4096+b"X"*4096+b"B"*8192
        ca=[{"index":0,"offset":0,"length":8192,"chunkId":"1"*64},
            {"index":1,"offset":8192,"length":8192,"chunkId":"2"*64}]
        cb=[{"index":0,"offset":0,"length":len(other),"chunkId":"3"*64}]
        result=e.chunk_diagnostic(source,other,ca,cb,{0:0})
        self.assertEqual(result["chunks"][0]["classification"],"NO_WHOLE_EXACT_BYTES")
        self.assertEqual(result["chunks"][0]["fourKiBMatchCoverageInThisChunkBytes"],4096)
        self.assertEqual(result["chunks"][1]["classification"],"EXACT_WHOLE_ONLY_UNALIGNED")

    def test_identical_aligned_chunk_with_disagreeing_ids_fails(self):
        base=b"A"*6000
        ca=[{"index":0,"offset":0,"length":6000,"chunkId":"1"*64}]
        cb=[{"index":0,"offset":0,"length":6000,"chunkId":"2"*64}]
        with self.assertRaisesRegex(ValueError,"hash/suite"):
            e.chunk_diagnostic(base,base,ca,cb,{0:0})

    def test_identical_id_but_changed_bytes_fails(self):
        base=b"A"*6000
        target=b"B"*6000
        ca=[{"index":0,"offset":0,"length":6000,"chunkId":"1"*64}]
        cb=[{"index":0,"offset":0,"length":6000,"chunkId":"1"*64}]
        with self.assertRaisesRegex(ValueError,"CSM ChunkId equality"):
            e.chunk_diagnostic(base,target,ca,cb,{})

    def test_overlap_coverage_is_bounded_by_source_byte_length(self):
        base=b"A"*15000
        target=b"A"*20000
        got=e.verified_windows_for_reverse(base,target,4096)
        self.assertGreater(len(got),0)
        self.assertLessEqual(e.d.merged_coverage([(x,x+4096) for x in got]),len(base))

    def test_previous_freeze_metadata_has_original_physical_hashes(self):
        self.assertEqual(e.CSM_SHA["base"],
          "da64f7ac2bed330730db249bd9b40614474da9b3d9bad648f5d93b18f58fd009")
        self.assertEqual(e.CSM_SHA["target"],
          "83ab6375f6c50db29c89cbe48f3f5845dedd6f99e0f79fea471ec1bd152968ed")
        self.assertEqual(e.EXPECTED_COUNTS,{"base":74,"target":81})
        self.assertEqual(e.EXPECTED_WINDOW_ANCHORS[4096],2069)
        self.assertEqual(e.EXPECTED_WINDOW_COVERAGE[4096],798464)


if __name__=="__main__":
    unittest.main()
