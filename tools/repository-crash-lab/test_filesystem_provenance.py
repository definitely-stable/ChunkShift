import os
from pathlib import Path
import tempfile
import unittest

from filesystem_provenance import linux_handle, linux_mount, volume_key, windows_volume_guid

MOUNTS = r"""10 1 8:1 / / rw - ext4 /dev/root rw
20 10 0:42 /sub\040root /mnt/data rw,nosuid shared:9 - tmpfs tmpfs rw,size=1024
21 10 8:1 /other /mnt/data rw - ext4 /dev/root rw
22 10 0:43 / /mnt/data/nested rw - tmpfs tmpfs rw
"""


class ProvenanceTests(unittest.TestCase):
    def test_mount_id_not_root_or_longest_prefix(self):
        observed = linux_mount("pos:\t0\nmnt_id:\t20\n", MOUNTS, "0:42")
        self.assertEqual(20, observed["mount_id"])
        self.assertEqual("/mnt/data", observed["mount_point"])
        self.assertEqual("/sub root", observed["root"])
        self.assertEqual("tmpfs", observed["filesystem"])
        self.assertEqual("rw,nosuid", observed["mount_options"])
        self.assertEqual(["shared:9"], observed["optional_fields"])
        self.assertEqual("rw,size=1024", observed["super_options"])
        self.assertEqual("UNQUALIFIED", observed["storage_class"])

    def test_missing_ambiguous_or_wrong_device_rejected(self):
        for fdinfo, mounts, device in [
            ("pos: 0", MOUNTS, "0:42"),
            ("mnt_id: 99", MOUNTS, "0:42"),
            ("mnt_id: 20\nmnt_id: 21", MOUNTS, "0:42"),
            ("mnt_id: 20", MOUNTS + MOUNTS, "0:42"),
            ("mnt_id: 20", MOUNTS, "8:1"),
            ("mnt_id: 20", "20 1 0:42 / / rw - tmpfs", "0:42"),
        ]:
            with self.subTest(fdinfo=fdinfo, device=device):
                with self.assertRaises(ValueError):
                    linux_mount(fdinfo, mounts, device)

    def test_mount_escapes_decoded_once(self):
        mounts = r"20 1 0:42 /a\134040 /b\011c\012d rw - tmpfs tmpfs rw"
        observed = linux_mount("mnt_id: 20", mounts, "0:42")
        self.assertEqual(r"/a\040", observed["root"])
        self.assertEqual("/b\tc\nd", observed["mount_point"])

    def test_volume_guid_and_no_drive_fallback(self):
        path = r"\\?\Volume{A1234567-1234-1234-1234-123456789ABC}\folder\file"
        self.assertEqual(path[:49].lower(), windows_volume_guid(path))
        for invalid in (r"C:\folder\file", r"\\server\share\file", r"\\?\Volume{bad}\file"):
            with self.assertRaises(ValueError):
                windows_volume_guid(invalid)

    @unittest.skipUnless(os.name == "posix" and Path("/proc/self/mountinfo").is_file(), "Linux only")
    def test_live_file_and_directory_handles(self):
        with tempfile.TemporaryDirectory() as tmp:
            file = Path(tmp) / "fixture"
            file.write_bytes(b"probe")
            directory = os.open(tmp, os.O_RDONLY | os.O_DIRECTORY)
            fd = os.open(file, os.O_RDONLY)
            try:
                first, parent = linux_handle(fd), linux_handle(directory)
                self.assertEqual(volume_key(first), volume_key(parent))
                self.assertNotEqual(first["object_id"], parent["object_id"])
                self.assertEqual(str(os.fstat(fd).st_ino), first["object_id"])
            finally:
                os.close(fd)
                os.close(directory)


if __name__ == "__main__":
    unittest.main()
