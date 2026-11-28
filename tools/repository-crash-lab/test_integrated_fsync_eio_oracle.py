import unittest
from integrated_crash_oracle import PACK_BYTES, sha
from integrated_fsync_eio_oracle import checkpoint, failure, trace_witness


class IntegratedFsyncOracleTests(unittest.TestCase):
    def setUp(self):
        self.path = "/private/candidate"
        self.expected = bytes(PACK_BYTES)
        self.initial = bytes([255]) * PACK_BYTES
        self.trace = [
            '10.000001 pwrite64(3</private/candidate>, "x"..., 65850, 0) = 65850',
            '10.000002 fsync(3</private/candidate>) = 0',
            '10.000003 pwrite64(3</private/candidate>, "x"..., 65536, 0) = 65536',
            '10.000004 pwrite64(3</private/candidate>, "x"..., 314, 65536) = 314',
            '10.000005 fsync(3</private/candidate>) = -1 EIO (Input/output error)',
        ]
        self.marker = dict(kind="fsync-checkpoint", pid=42, path=self.path, state="Running",
                           attempted="CreateExclusive", completed="None", owned=True,
                           writer_closed=False, reader_closed=False, parent_owned=True,
                           parent_close_attempted=False, parent_closed=False, source_position=0,
                           pack_sha256=sha(self.expected), initial_sha256=sha(self.initial))
        self.result = dict(kind="integrated-fsync-eio", state="ReconciliationRequired",
                           attempted="FlushFile", completed="WriteVerifiedObject", owned=True,
                           writer_closed=True, reader_closed=False, parent_owned=True,
                           parent_close_attempted=True, parent_closed=True, later_stage_started=False,
                           source_open=True, source_position=PACK_BYTES, pack_sha256=sha(self.expected),
                           hresult=5, type="IOException")

    def test_positive(self):
        checkpoint(self.marker, self.expected, self.initial, 42, self.path)
        failure(self.result, self.expected)
        result = trace_witness(["\n".join(self.trace[::2]), "\n".join(self.trace[1::2])], self.path)
        self.assertEqual(result["fsync_errno"], 5)

    def test_wrong_trace_rejected(self):
        for rows in (self.trace[:-1], self.trace[:3]+self.trace[4:],
                     self.trace+[self.trace[-1].replace("000005", "000006")],
                     self.trace[:-1]+[self.trace[-1].replace("EIO", "ENOSPC")],
                     self.trace[:-1]+[self.trace[-1].replace("3<", "4<")],
                     self.trace[:3]+[self.trace[3].replace("65536", "0")]+self.trace[4:],
                     self.trace[:3]+[self.trace[3].replace("= 314", "= -1 EIO")]+self.trace[4:]):
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                trace_witness(["\n".join(rows)], self.path)

    def test_wrong_lifecycle_rejected(self):
        for key, value in (("pid", 43), ("initial_sha256", "wrong"), ("writer_closed", True)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                checkpoint(dict(self.marker, **{key: value}), self.expected, self.initial, 42, self.path)
        for key, value in (("state", "ExperimentalPackPrepared"), ("attempted", "WriteVerifiedObject"),
                           ("hresult", 28), ("later_stage_started", True), ("writer_closed", False),
                           ("parent_closed", False)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                failure(dict(self.result, **{key: value}), self.expected)


if __name__ == "__main__":
    unittest.main()
