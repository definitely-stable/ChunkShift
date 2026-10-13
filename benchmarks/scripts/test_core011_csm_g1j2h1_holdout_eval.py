#!/usr/bin/env python3
"""Adversarial controls for the independent pre-frozen J2H1 evaluation."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core011_csm_g1j2h1_holdout_eval as h


class J2H1Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.contract = json.loads(h.CONTRACT.read_text())

    def test_h0_fixed_provenance(self):
        self.assertEqual(h.validate_contract(self.contract)["asset"],
                         "lib/net6.0/Newtonsoft.Json.dll")
        x = copy.deepcopy(self.contract)
        x["h0OriginalRunId"] = "fabricated"
        with self.assertRaisesRegex(ValueError, "provenance"):
            h.validate_contract(x)
        x = copy.deepcopy(self.contract)
        x["base"]["nugetVersion"] = "13.0.4"
        with self.assertRaisesRegex(ValueError, "version"):
            h.validate_contract(x)
        x = copy.deepcopy(self.contract)
        x["target"]["dllSha256"] = "not-a-hash"
        with self.assertRaisesRegex(ValueError, "literal"):
            h.validate_contract(x)

    def test_reject_nonfrozen_package_content(self):
        with self.assertRaises(ValueError):
            h.verify_download(b"bad-nupkg", self.contract["base"])
        self.assertEqual(h.LANES, (
            "BASE_AWARE_STABLE64K", "SELF_CONTAINED_STABLE64K"))
        self.assertEqual(h.PROFILE_LANES, (
            "stable-fastcdc64k", "lab-fastcdc16k",
            "lab-fastcdc256k", "lab-fixed16k", "lab-fixed64k"))

    def synthetic_scanner(self, folder, target_chunk_id):
        fixture = Path(folder)
        observations = []
        for role, content in (("base", b"abcd"), ("target", b"abce")):
            for lane in h.PROFILE_LANES:
                fname = f"{role}.{lane}.tsv"
                cid = "0" * 64 if role == "base" else target_chunk_id
                (fixture / fname).write_text(f"0\t4\t{cid}\n")
                observations.append({
                    "role": role, "lane": lane,
                    "chunkCount": 1, "contentLength": len(content),
                    "tsvFile": fname,
                })
        (fixture / "scanner-control.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j2h1-profile-controls.v1",
            "observations": observations,
            "verdict": "NO_VERDICT_EXPLORATORY",
            "hashSuite": "chunkshift.blake3-256.v1",
        }))

    def test_all_five_profile_conservation(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.synthetic_scanner(tmp, "1" * 64)
            d = h.evaluate_profiles(Path(tmp), b"abcd", b"abce")
            self.assertEqual(len(d), 5)
            self.assertEqual(d["stable-fastcdc64k"]["reusedBytes"], 0)
            self.assertEqual(d["lab-fastcdc16k"]["missingBytes"], 4)
            self.assertFalse(d["lab-fastcdc16k"]["registeredPublicProfile"])

    def test_reject_hash_identity_collision_and_missing_rows(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.synthetic_scanner(tmp, "0" * 64)
            with self.assertRaisesRegex(ValueError, "hash identity"):
                h.evaluate_profiles(Path(tmp), b"abcd", b"abce")
        with tempfile.TemporaryDirectory() as tmp:
            self.synthetic_scanner(tmp, "1" * 64)
            (Path(tmp) / "target.lab-fixed64k.tsv").write_text("")
            with self.assertRaises(ValueError):
                h.evaluate_profiles(Path(tmp), b"abcd", b"abce")
        with tempfile.TemporaryDirectory() as tmp:
            self.synthetic_scanner(tmp, "1" * 64)
            control = Path(tmp) / "scanner-control.json"
            record = json.loads(control.read_text())
            record["observations"].pop()
            control.write_text(json.dumps(record))
            with self.assertRaisesRegex(ValueError, "ten"):
                h.evaluate_profiles(Path(tmp), b"abcd", b"abce")


if __name__ == "__main__":
    unittest.main()
