"""PATCH-META-001/G1B2A mutation controls: cost probe never proves savings."""
import unittest

import run_patch_meta_001_g1b2a as a


class ProbeTests(unittest.TestCase):
    def base(self):
        return {
            "metaProbeEnabled": True,
            "metaBaseManifestSeconds": 0.002,
            "metaTargetManifestSeconds": 0.002,
            "metaCandidateSearchWorkerSeconds": 0.0001,
            "metaCandidateSearchCalls": 81,
            "metaCandidateStarts": 1000,
            "metaBaseRecords": 74,
            "metaUniqueBaseIds": 74,
        }

    def test_on_off_are_exclusive(self):
        a.verify_probe(self.base(), "on")
        with self.assertRaises(ValueError):
            a.verify_probe(self.base(), "off")
        off = {key: None for key in a.METRICS}
        off["metaProbeEnabled"] = False
        a.verify_probe(off, "off")
        with self.assertRaises(ValueError):
            a.verify_probe(off, "on")

    def test_fail_closed_counts_and_invalid_clock(self):
        bads = [
            ("metaProbeEnabled", False),
            ("metaCandidateSearchCalls", 0),
            ("metaCandidateStarts", 0),
            ("metaCandidateStarts", 80),
            ("metaUniqueBaseIds", 75),
            ("metaBaseRecords", 0),
            ("metaBaseManifestSeconds", 0),
            ("metaTargetManifestSeconds", -1),
            ("metaCandidateSearchWorkerSeconds", float("nan")),
            ("metaCandidateSearchCalls", "81"),
        ]
        for key, bad in bads:
            with self.subTest(key=key), self.assertRaises(ValueError):
                a.verify_probe(self.base() | {key: bad}, "on")

    def test_missing_field_must_not_pass_off(self):
        off = {key: None for key in a.METRICS}
        off["metaProbeEnabled"] = False
        del off["metaCandidateStarts"]
        with self.assertRaises(ValueError):
            a.verify_probe(off, "off")

    def test_guarded_source_pins_and_status(self):
        self.assertEqual(a.PAIR, ("4.13.0", "4.14.0"))
        self.assertEqual(a.MODES, ("off", "on"))
        self.assertIn("NO_PRODUCT_VERDICT", a.STATUS)
        self.assertEqual(len(a.METRICS), 7)


if __name__ == "__main__":
    unittest.main()
