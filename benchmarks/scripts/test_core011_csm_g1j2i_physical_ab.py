#!/usr/bin/env python3
"""G1-J2I fail-closed source mutation, byte-oracle and frozen lane contracts."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core011_csm_g1j2i_physical_ab as j


class J2IPhysicalAb(unittest.TestCase):
    def test_profile_source_has_unique_frozen_anchor(self):
        original = (Path.cwd() / j.CONFIG).read_text()
        transformed = j.variant_source(original)
        self.assertEqual(transformed.count(j.LAB_ID), 1)
        self.assertNotIn(j.OLD, transformed)
        self.assertNotEqual(j.sha(original.encode()), j.sha(transformed.encode()))
        self.assertIn("new FastCdcProfile(4 * 1024, 16 * 1024, 64 * 1024)",
                      transformed)
        self.assertIn("new ChunkingProfileId(", transformed)

    def test_reject_source_drift_or_second_profile(self):
        with self.assertRaisesRegex(ValueError, "anchor"):
            j.variant_source("not the expected registration")
        original = (Path.cwd() / j.CONFIG).read_text()
        with self.assertRaisesRegex(ValueError, "anchor"):
            j.variant_source(original + "\n" + j.OLD)
        with self.assertRaises(ValueError):
            j.variant_source(original + "\n// " + j.LAB_ID)
        with self.assertRaises(ValueError):
            j.variant_source(j.variant_source(original))

    def test_freeze_contains_two_and_only_two_named_profiles_and_pairs(self):
        self.assertEqual(j.PROFILES, ("stable64k", "lab16k"))
        self.assertEqual(j.MODES, ("based", "standalone"))
        self.assertEqual(set(j.EXPECTED), {"roslyn", "newtonsoft"})
        self.assertEqual(j.EXPECTED["roslyn"]["lab16k"], (315,316,8,149568))
        self.assertEqual(j.EXPECTED["newtonsoft"]["lab16k"], (36,36,5,65601))
        self.assertEqual(j.EXPECTED["roslyn"]["baselineCspBytes"]["based"],1260098)
        self.assertEqual(j.EXPECTED["newtonsoft"]["baselineCspBytes"]["based"],78435)
        self.assertNotEqual(j.LAB_ID,j.STABLE_ID)

    def test_exact_reuse_verifies_original_bytes_not_opaque_id_only(self):
        base=b"foo" + b"hello" + b"world"
        target=b"xyz" + b"hello" + b"monde"
        left=[{"offset":3,"length":5,"chunkId":"a"*64}]
        right=[{"offset":3,"length":5,"chunkId":"a"*64}]
        self.assertEqual(j.exact_reuse(base,target,left,right),(1,5))
        with self.assertRaisesRegex(ValueError,"opaque ChunkId"):
            j.exact_reuse(base,b"xyz"+"Hella".encode()+b"monde",left,right)
        with self.assertRaisesRegex(ValueError,"opaque ChunkId"):
            j.exact_reuse(base,target,left,[{"offset":3,"length":4,"chunkId":"a"*64}])
        self.assertEqual(j.exact_reuse(base,target,left,
            [{"offset":3,"length":5,"chunkId":"b"*64}]),(0,0))

    def test_upstream_corpus_is_locked(self):
        ros,heldout,frozen_sha=j.check_zip_contracts()
        self.assertEqual(ros["versions"]["base"]["dllBytes"],6604072)
        self.assertEqual(heldout["base"]["dllBytes"],711952)
        self.assertEqual(heldout["target"]["dllBytes"],712464)
        self.assertEqual(len(frozen_sha),64)


if __name__=="__main__":
    unittest.main()
