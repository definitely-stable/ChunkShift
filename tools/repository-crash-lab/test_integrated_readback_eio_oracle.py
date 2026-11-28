import unittest
from integrated_crash_oracle import PACK_BYTES, sha
from integrated_readback_eio_oracle import checkpoint, failure


class IntegratedReadbackEioOracleTests(unittest.TestCase):
    def setUp(self):
        self.expected = bytes(PACK_BYTES)
        self.marker = dict(kind="readback-checkpoint", pid=42, path="/private/candidate",
                           state="Running", attempted="VerifyClosedObject", completed="CloseWriter",
                           owned=True, writer_closed=True, reader_closed=False,
                           parent_owned=True, parent_close_attempted=False, parent_closed=False,
                           source_position=PACK_BYTES, pack_sha256=sha(self.expected))
        self.result = dict(kind="integrated-readback-eio", state="ReconciliationRequired",
                           attempted="VerifyClosedObject", completed="CloseWriter",
                           owned=True, writer_closed=True, reader_closed=True,
                           parent_owned=True, parent_close_attempted=True, parent_closed=True,
                           parent_barrier_started=False, source_open=True, source_position=PACK_BYTES,
                           pack_sha256=sha(self.expected), hresult=5, type="IOException")

    def test_exact_witnesses(self):
        checkpoint(self.marker, self.expected, 42, "/private/candidate")
        failure(self.result, self.expected, 5, False)

    def test_wrong_checkpoint_rejected(self):
        for key, value in (("pid", 43), ("path", "/other"), ("writer_closed", False),
                           ("completed", "FlushFile"), ("parent_closed", True),
                           ("pack_sha256", "wrong")):
            with self.subTest(key=key), self.assertRaises(ValueError):
                checkpoint(dict(self.marker, **{key: value}), self.expected, 42, "/private/candidate")

    def test_false_success_and_bad_cleanup_rejected(self):
        for key, value in (("state", "ExperimentalPackPrepared"), ("hresult", 28),
                           ("reader_closed", False), ("parent_closed", False),
                           ("parent_barrier_started", True), ("completed", "VerifyClosedObject"),
                           ("source_open", False)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                failure(dict(self.result, **{key: value}), self.expected, 5, False)
        for errno, resident in ((28, False), (5, True)):
            with self.subTest(errno=errno, resident=resident), self.assertRaises(ValueError):
                failure(self.result, self.expected, errno, resident)


if __name__ == "__main__":
    unittest.main()
