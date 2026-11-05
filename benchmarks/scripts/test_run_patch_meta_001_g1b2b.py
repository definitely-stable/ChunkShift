"""G1B2B fail-closed codec classification and source-locked probe tests."""
import copy
import json
import math
import tempfile
import unittest
from pathlib import Path
import run_patch_meta_001_g1b2b as b
import run_patch_meta_001_g1b2a as a


class CodecConsumedProbeTests(unittest.TestCase):
    def on(self):
        data = {
            "metaProbeEnabled": True,
            "metaBaseManifestSeconds": .002, "metaTargetManifestSeconds": .003,
            "metaCandidateSearchWorkerSeconds": .0005,
            "metaCandidateSearchCalls": 81, "metaCandidateStarts": 464,
            "metaBaseRecords": 100, "metaUniqueBaseIds": 90,
            "metaPlainZstdWorkerSeconds": .25,
            "metaDictionaryZstdWorkerSeconds": 2.9,
            "metaDictionaryReadWorkerSeconds": .06,
            "metaPlainZstdTrials": 81, "metaDictionaryZstdTrials": 462,
            "metaDictionaryReadCalls": 464,
            "metaPlainInputBytes": 2_000_000,
            "metaDictionaryTargetInputBytes": 5_000_000,
            "metaDictionaryInputBytes": 20_000_000,
            "metaDictionaryReadBytes": 20_000_000,
        }
        return data

    def test_on_exact_stage_contract(self):
        a.verify_probe(self.on(), "on")
        b.validate_codec_stage(self.on(), "on")
        self.assertIn("NO_PRODUCT_VERDICT", b.STATUS)
        self.assertEqual(len(b.METRICS), 10)

    def test_off_requires_all_explicit_nulls(self):
        off = {"metaProbeEnabled": False}
        off.update({k: None for k in a.METRICS + b.METRICS})
        a.verify_probe(off, "off")
        b.validate_codec_stage(off, "off")
        for broken in ("metaPlainZstdTrials", "metaDictionaryInputBytes"):
            with self.subTest(broken=broken), self.assertRaises(ValueError):
                v = dict(off)
                del v[broken]
                b.validate_codec_stage(v, "off")

    def test_invalid_clocks_and_counts_rejected(self):
        bads = [
            ("metaPlainZstdWorkerSeconds", 0),
            ("metaDictionaryZstdWorkerSeconds", float("nan")),
            ("metaDictionaryReadWorkerSeconds", float("inf")),
            ("metaDictionaryZstdTrials", "462"),
            ("metaPlainZstdTrials", 80),
            ("metaDictionaryZstdTrials", 465),
            ("metaDictionaryReadCalls", 465),
            ("metaDictionaryInputBytes", 20_000_001),
            ("metaDictionaryTargetInputBytes", 1_999_999),
        ]
        for k, value in bads:
            with self.subTest(metric=k), self.assertRaises(ValueError):
                b.validate_codec_stage({**self.on(), k: value}, "on")

    def test_no_false_csp_or_meta_adopt(self):
        self.assertEqual(b.SCHEMA, "chunkshift.patch-meta-g1b2b-codec-consumed.v1")
        self.assertIn("NO_META_CACHE", b.STATUS)
        self.assertEqual(b.base.PAIR, ("4.13.0", "4.14.0"))


if __name__ == "__main__":
    unittest.main()
