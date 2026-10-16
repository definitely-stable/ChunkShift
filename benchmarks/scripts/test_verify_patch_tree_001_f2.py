#!/usr/bin/env python3
"""F2 physical verifier synthetic-only rejection fixtures; no original ZIPs."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location(
    "f2_verify", Path(__file__).with_name("verify_patch_tree_001_f2.py"))
f2 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(f2)

H0 = "0" * 64
H1 = "1" * 64
H2 = "2" * 64


def lane(name):
    return {"name": name, "physicalBytes": 100, "reconciledPhysicalBytes": 100,
            "cspSha256": H0, "baseManifestId": H1, "targetManifestId": H2,
            "rawStoredBytes": 100, "zstdStoredBytes": 0, "payloadMetadataBytes": 0,
            "indexBytes": 0, "otherPhysicalBytes": 0, "payloadEntries": 1,
            "createMilliseconds": 5.0, "applyMilliseconds": 5.0,
            "exactApplyVerified": True}


def example(kind="changed"):
    pair = {"oldTreeManifestId": H1}
    file = {"kind": kind, "targetSha256": H2, "t0Bytes": 100,
            "t1Bytes": 100, "t0PatchSha256": H0 if kind == "changed" else None,
            "t1PatchSha256": H0, "targetBytes": 100}
    row = {"t0PhysicalBytes": 100, "t1PhysicalBytes": 100,
           "relativeLocalPhysicalBytes": 100, "noDictionaryPhysicalBytes": 100}
    names = (["t0-public"] if kind == "changed" else []) + [
        "t1-public", "t1-h0-parity", "t1-relative-local", "t1-no-dictionary"]
    obs = {"schema": f2.OBS_SCHEMA, "status": f2.OBS_STATUS,
           "targetSha256": H2, "lanes": [lane(n) for n in names]}
    return pair, file, row, obs


class VerifyF2PhysicalTests(unittest.TestCase):
    def test_full_lane_contract_for_changed_and_added(self):
        for kind in ("changed", "added"):
            f2.validate_observation(*example(kind))

    def test_full_path_identity_is_case_sensitive(self):
        a = f2.filename("family", "old", "new", "dir/A")
        self.assertNotEqual(a, f2.filename("family", "old", "new", "dir/a"))
        self.assertNotEqual(a, f2.filename("family", "new", "old", "dir/A"))

    def test_corrupted_physical_byte_conservation_is_rejected(self):
        x = lane("t1")
        x["otherPhysicalBytes"] = 1
        with self.assertRaisesRegex(ValueError, "byte conservation"):
            f2.validate_lane(x)

    def test_false_apply_and_negative_duration_rejected(self):
        x = lane("t1")
        x["exactApplyVerified"] = False
        with self.assertRaisesRegex(ValueError, "apply"):
            f2.validate_lane(x)
        x = lane("t1")
        x["createMilliseconds"] = -1
        with self.assertRaisesRegex(ValueError, "duration"):
            f2.validate_lane(x)

    def test_target_and_base_identity_tamper_rejected(self):
        p, f, r, o = example()
        o["lanes"][-1]["baseManifestId"] = H2
        with self.assertRaisesRegex(ValueError, "BASE"):
            f2.validate_observation(p, f, r, o)
        p, f, r, o = example()
        o["targetSha256"] = H1
        with self.assertRaisesRegex(ValueError, "target hash"):
            f2.validate_observation(p, f, r, o)

    def test_raw_added_control_and_h0_parity(self):
        p, f, r, o = example("added")
        f["t0PatchSha256"] = H0
        with self.assertRaisesRegex(ValueError, "raw"):
            f2.validate_observation(p, f, r, o)
        p, f, r, o = example()
        o["lanes"][2]["cspSha256"] = H1
        with self.assertRaisesRegex(ValueError, "parity"):
            f2.validate_observation(p, f, r, o)


if __name__ == "__main__":
    unittest.main()
