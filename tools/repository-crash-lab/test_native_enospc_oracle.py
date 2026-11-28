import unittest

from integrated_crash_oracle import PACK_BYTES, WINDOW, sha
from native_enospc_oracle import verify_failure


class NativeEnospcOracleTests(unittest.TestCase):
    def setUp(self):
        self.expected = bytes((i * 13 + 3) & 255 for i in range(PACK_BYTES))
        self.result = dict(kind="native-enospc", phase="flush", state="ReconciliationRequired",
                           attempted="FlushFile", completed="WriteVerifiedObject", owned=True,
                           reader_closed=False, parent_owned=True, parent_close_attempted=True,
                           parent_closed=True, source_open=True, source_position=PACK_BYTES,
                           pack_sha256=sha(self.expected), writer_closed=False,
                           errors=[dict(type="IOException", hresult=28)] * 2)

    def test_valid_late_and_early(self):
        verify_failure("flush", self.result, self.expected, self.expected[:WINDOW])
        early = dict(self.result, phase="write", attempted="WriteVerifiedObject",
                     completed="CreateExclusive", source_position=WINDOW, writer_closed=True,
                     errors=[dict(type="IOException", hresult=28)])
        verify_failure("write", early, self.expected, b"")

    def test_false_success_wrong_errno_and_cleanup_rejected(self):
        for key, value in (("state", "ExperimentalPackPrepared"), ("attempted", "WriteVerifiedObject"),
                           ("errors", [dict(type="IOException", hresult=5)]),
                           ("parent_closed", False), ("writer_closed", True),
                           ("source_open", False), ("source_position", WINDOW),
                           ("pack_sha256", "wrong")):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_failure("flush", dict(self.result, **{key: value}), self.expected,
                               self.expected[:WINDOW])

    def test_wrong_prefix_rejected(self):
        for orphan in (None, b"", self.expected, self.expected[:WINDOW-1],
                       b"x" + self.expected[1:WINDOW]):
            with self.subTest(length=None if orphan is None else len(orphan)), self.assertRaises(ValueError):
                verify_failure("flush", self.result, self.expected, orphan)


if __name__ == "__main__":
    unittest.main()
