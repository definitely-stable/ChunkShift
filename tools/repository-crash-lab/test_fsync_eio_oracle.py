import unittest
from fsync_eio_oracle import LENGTH, BEFORE, AFTER, witness, check_worker
from integrated_crash_oracle import sha


class FsyncEioOracleTests(unittest.TestCase):
    def setUp(self):
        self.path = "/private/fsync.bin"
        self.write = 'pwrite64(3</private/fsync.bin>, "payload"..., 65536, 0) = 65536'
        self.sync = "fsync(3</private/fsync.bin>) = -1 EIO (Input/output error)"

    def test_exact_witness(self):
        self.assertEqual(witness(self.write + "\n" + self.sync, self.path, True),
                         dict(write=LENGTH, fsync_errno=5, same_fd=True))
        witness(self.write + "\n" + "fsync(3</private/fsync.bin>) = 0", self.path, False)

    def test_wrong_syscall_or_fd_rejected(self):
        for trace in (self.sync, self.sync + "\n" + self.write,
                      self.write.replace("= 65536", "= -1 EIO") + "\n" + self.sync,
                      self.write + "\n" + self.sync.replace("3<", "4<"),
                      self.write + "\n" + self.sync.replace("EIO", "ENOSPC"),
                      self.write + "\n" + self.sync.replace("fsync", "fdatasync", 1),
                      self.write + "\n" + self.sync + "\n" + self.sync,
                      self.write + "\n" + self.sync.replace("= -1 EIO (Input/output error)", "= 0")):
            with self.subTest(trace=trace), self.assertRaises(ValueError):
                witness(trace, self.path, True)

    def test_wrong_worker_rejected(self):
        result = dict(kind="fsync-preflight", written=LENGTH, fsync_errno=5,
                      close_confirmed=True, payload_sha256=sha(AFTER))
        check_worker(result, True)
        for key, value in (("written", 0), ("fsync_errno", 0), ("close_confirmed", False),
                           ("payload_sha256", sha(BEFORE))):
            with self.subTest(key=key), self.assertRaises(ValueError):
                check_worker(dict(result, **{key: value}), True)


if __name__ == "__main__":
    unittest.main()
