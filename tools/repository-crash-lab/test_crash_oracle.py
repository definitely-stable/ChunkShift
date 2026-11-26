import copy
import hashlib
import struct
import unittest

from crash_oracle import check_condition, verify_raw


def fixture():
    payload = b"abc"
    key = hashlib.sha256(payload).hexdigest()
    packid = bytes(range(32))
    frame = dict(key=key, Ordinal=0, Offset=64, Length=3, Crc32C=123)
    data = (b"CSRAWPK0" + struct.pack("<HHHH", 1, 64, 2, 0) + packid + bytes(16) + payload +
            struct.pack("<QQQIHH", 64, 3, 3, 123, 0, 0) + bytes.fromhex(key) + struct.pack("<I", 0) +
            b"CSRAWFT0" + struct.pack("<IIQQQQQQ", 1, 0, 3, 67, 32, 99, 36, 0) +
            b"CSRAWTR0" + struct.pack("<QIHHQII", 135, 64, 1, 0, 239, 0, 0))
    physical = dict(name="fixture.pack-private", pack_id=packid.hex(),
                    pack_digest=hashlib.sha256(data).hexdigest(), bytes=len(data), frames=[frame])
    return data, physical, {key: payload}


class CrashOracleTests(unittest.TestCase):
    def test_exact_prefix_not_any_valid_pack(self):
        frame = dict(key="a", Ordinal=0, Offset=64, Length=1, Crc32C=0)
        expected = [dict(bytes=976293, frames=[frame])]
        state = dict(kind="inspection", valid=["a"], invalid=[], bytes=976293,
                     packs=1, frames=1, all_accepted=True,
                     physical=[dict(name="a", pack_id="id", **expected[0])])
        check_condition("BeforeCreate:2", state, dict(geometry=expected))
        changed = copy.deepcopy(state)
        changed["physical"][0]["frames"][0]["key"] = "b"
        with self.assertRaisesRegex(ValueError, "prefix geometry"):
            check_condition("BeforeCreate:2", changed, dict(geometry=expected))
        with self.assertRaisesRegex(ValueError, "classification"):
            check_condition("BeforeClosedDuplicate:1", state, dict(geometry=expected))

    def test_raw_payload_and_metadata_match(self):
        data, physical, payloads = fixture()
        self.assertEqual(payloads, verify_raw(data, physical, payloads, "sha256"))

    def test_rehashed_wrong_payload_or_index_rejected(self):
        data, physical, payloads = fixture()
        for offset, error in [(64, "differs from source"), (67, "directory mismatch"),
                              (99, "hash index mismatch"), (135, "footer mismatch"),
                              (199, "trailer mismatch")]:
            with self.subTest(offset=offset):
                changed = bytearray(data)
                changed[offset] ^= 1
                descriptor = dict(physical, pack_digest=hashlib.sha256(changed).hexdigest())
                with self.assertRaisesRegex(ValueError, error):
                    verify_raw(changed, descriptor, payloads, "sha256")

    def test_snapshot_digest_and_suite_binding(self):
        data, physical, payloads = fixture()
        with self.assertRaisesRegex(ValueError, "PackDigest"):
            verify_raw(data, dict(physical, pack_digest="0"*64), payloads, "sha256")
        with self.assertRaisesRegex(ValueError, "header"):
            verify_raw(data, physical, payloads, "blake3")


if __name__ == "__main__":
    unittest.main()
