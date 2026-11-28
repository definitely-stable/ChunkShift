import struct
import unittest

from ext4_read_eio_oracle import BLOCK, OFFSET, SIZE, decode_extent, error_table, verify_failure
from integrated_crash_oracle import PACK_BYTES, sha


def extent(logical=0, physical=65536, length=8192, flags=1, mapped=1):
    return (struct.pack("<QQIIII", OFFSET, BLOCK, 0, mapped, 1, 0) +
            struct.pack("<QQQQQIIII", logical, physical, length, 0, 0, flags, 0, 0, 0))


class Ext4ReadEioOracleTests(unittest.TestCase):
    def test_exact_extent_and_table(self):
        target = decode_extent(extent())
        self.assertEqual(target, 69632)
        self.assertEqual(error_table("7:1", target),
                         "0 136 linear 7:1 0\n136 8 error\n144 32624 linear 7:1 144")

    def test_unusable_extents_rejected(self):
        for data in (extent(flags=2), extent(flags=0x800), extent(mapped=0),
                     extent(physical=1), extent(physical=SIZE), extent(length=BLOCK),
                     extent(logical=2*BLOCK), extent()[:-1]):
            with self.subTest(data=data.hex()), self.assertRaises(ValueError):
                decode_extent(data)

    def test_wrong_error_or_success_rejected(self):
        expected = bytes(PACK_BYTES)
        result = dict(kind="native-read-eio", hresult=5, type="IOException",
                      accepted=False, bytes=PACK_BYTES, expected_sha256=sha(expected))
        verify_failure(result, expected, 5)
        for key, value in (("hresult", 28), ("accepted", True), ("bytes", 0),
                           ("expected_sha256", "wrong"), ("type", "InvalidDataException")):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_failure(dict(result, **{key: value}), expected, 5)
        with self.assertRaises(ValueError):
            verify_failure(result, expected, 28)


if __name__ == "__main__":
    unittest.main()
