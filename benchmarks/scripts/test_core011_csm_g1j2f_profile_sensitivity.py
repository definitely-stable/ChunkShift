"""Negative controls for lab-only G1-J2F frozen five-way profile comparison."""
import json
from pathlib import Path
import sys
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parent))
import core011_csm_g1j2f_profile_sensitivity as s


class G1J2FUnit(unittest.TestCase):
    def test_bounded_preregistered_lanes(self):
        self.assertEqual(s.LANES,(
          "stable-fastcdc64k","lab-fastcdc16k","lab-fastcdc256k",
          "lab-fixed16k","lab-fixed64k"))
        self.assertEqual(s.STABLE_CSM["base"],
          "da64f7ac2bed330730db249bd9b40614474da9b3d9bad648f5d93b18f58fd009")
        self.assertEqual(s.STABLE_CSM["target"],
          "83ab6375f6c50db29c89cbe48f3f5845dedd6f99e0f79fea471ec1bd152968ed")

    def test_scanner_tsv_full_coverage(self):
        raw=b"a"*10+b"b"*7
        tsv="0\t10\t"+"0"*64+"\n10\t7\t"+"1"*64+"\n"
        rows=s.validate_scanner(raw,tsv)
        self.assertEqual(len(rows),2)
        self.assertEqual(rows[-1]["offset"]+rows[-1]["length"],len(raw))

    def test_scanner_invalid_gap_or_overflow(self):
        raw=b"a"*12
        with self.assertRaisesRegex(ValueError,"tuple"):
            s.validate_scanner(raw,"1\t10\t"+"0"*64+"\n")
        with self.assertRaisesRegex(ValueError,"out-of-range"):
            s.validate_scanner(raw,"0\t20\t"+"0"*64+"\n")
        with self.assertRaisesRegex(ValueError,"cover"):
            s.validate_scanner(raw,"0\t9\t"+"0"*64+"\n")
        with self.assertRaisesRegex(ValueError,"tuple"):
            s.validate_scanner(raw,"0\t12\t"+"INVALID"+"\n")

    def test_true_shifted_byte_identity_is_valid(self):
        base=b"a"*5+b"b"*7
        target=b"x"*3+b"a"*5+b"b"*7
        bchunks=[{"offset":0,"length":5,"chunkId":"1"*64},
                 {"offset":5,"length":7,"chunkId":"2"*64}]
        tchunks=[{"offset":0,"length":3,"chunkId":"3"*64},
                 {"offset":3,"length":5,"chunkId":"1"*64},
                 {"offset":8,"length":7,"chunkId":"2"*64}]
        z=s.compare_lane(base,target,bchunks,tchunks)
        self.assertEqual(z["reusedChunks"],2)
        self.assertEqual(z["reusedBytes"],12)
        self.assertEqual(z["missingBytes"],3)
        self.assertEqual(z["cblkRecordAndEnvelopeBytesIfEncoded"]["base"],116)

    def test_false_hash_identity_rejected(self):
        base=b"A"*5
        target=b"B"*5
        bchunks=[{"offset":0,"length":5,"chunkId":"1"*64}]
        tchunks=[{"offset":0,"length":5,"chunkId":"1"*64}]
        with self.assertRaisesRegex(ValueError,"hash identity"):
            s.compare_lane(base,target,bchunks,tchunks)

    def test_len_mismatch_rejected(self):
        base=b"A"*5
        target=b"A"*6
        bchunks=[{"offset":0,"length":5,"chunkId":"1"*64}]
        tchunks=[{"offset":0,"length":6,"chunkId":"1"*64}]
        with self.assertRaisesRegex(ValueError,"collision"):
            s.compare_lane(base,target,bchunks,tchunks)

    def test_missing_chunks_correct(self):
        base=b"A"*5
        target=b"B"*5
        bchunks=[{"offset":0,"length":5,"chunkId":"1"*64}]
        tchunks=[{"offset":0,"length":5,"chunkId":"2"*64}]
        z=s.compare_lane(base,target,bchunks,tchunks)
        self.assertEqual((z["reusedChunks"],z["reusedBytes"]), (0,0))
        self.assertEqual((z["missingChunks"],z["missingBytes"]), (1,5))


if __name__=="__main__":
    unittest.main()
