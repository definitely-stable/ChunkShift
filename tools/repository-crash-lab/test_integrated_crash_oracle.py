import unittest
from integrated_crash_oracle import (BOUNDARIES, PACK_BYTES, WINDOW, check_candidate,
                                     check_inspection, check_marker, sha)


class IntegratedOracleTests(unittest.TestCase):
    def setUp(self):
        self.expected = bytes((i * 13 + 3) & 255 for i in range(PACK_BYTES))

    def test_matrix(self):
        self.assertEqual(len(BOUNDARIES), len(set(BOUNDARIES)))
        self.assertEqual(len(BOUNDARIES), 14)
        self.assertEqual(check_candidate("CreateExclusive:before", None, self.expected), "absent")
        self.assertEqual(check_candidate("WriteVerifiedObject:partial", self.expected[:WINDOW],
                                         self.expected), "prefix")
        self.assertEqual(check_candidate("FlushFile:after", self.expected, self.expected), "exact")
        for length in (WINDOW, PACK_BYTES):
            check_candidate("FlushFile:before", self.expected[:length], self.expected)

    def test_wrong_bytes_and_extents_fail_closed(self):
        for boundary, data in (
            ("CreateExclusive:before", b""),
            ("CreateExclusive:after", self.expected[:1]),
            ("WriteVerifiedObject:partial", self.expected[:WINDOW-1]),
            ("WriteVerifiedObject:after", self.expected[:WINDOW-1]),
            ("FlushFile:after", self.expected[:-1]),
            ("Prepared", self.expected[:-1] + bytes([self.expected[-1] ^ 1])),
            ("Prepared", self.expected + b"x"),
        ):
            with self.subTest(boundary=boundary), self.assertRaises(ValueError):
                check_candidate(boundary, data, self.expected)

    def test_after_effect_marker_does_not_advance_completed(self):
        marker = dict(kind="checkpoint", boundary="FlushParentDirectory:after", state="Running",
                      attempted="FlushParentDirectory", completed="VerifyClosedObject", owned=True,
                      writer_closed=True, reader_closed=True, parent_owned=True,
                      parent_close_attempted=True, parent_closed=True)
        check_marker(marker["boundary"], marker)
        for key, value in (("completed", "FlushParentDirectory"), ("parent_closed", False),
                           ("state", "ExperimentalPackPrepared"), ("owned", False)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                check_marker(marker["boundary"], dict(marker, **{key: value}))

    def test_inspector_cannot_accept_partial_or_wrong_digest(self):
        data = self.expected[:WINDOW]
        inspection = dict(kind="inspection", accepted=False, bytes=len(data),
                          sha256=sha(data), expected_sha256=sha(self.expected))
        check_inspection(inspection, data, self.expected)
        for key, value in (("accepted", True), ("sha256", "wrong"), ("expected_sha256", sha(data)),
                           ("bytes", len(self.expected))):
            with self.subTest(key=key), self.assertRaises(ValueError):
                check_inspection(dict(inspection, **{key: value}), data, self.expected)


if __name__ == "__main__":
    unittest.main()
