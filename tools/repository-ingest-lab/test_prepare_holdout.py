import io
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile

from prepare_holdout import append_members, checked_url, choose, copy_bounded


class HoldoutPreparationTests(unittest.TestCase):
    def test_cap(self):
        with self.assertRaises(ValueError):
            copy_bounded(io.BytesIO(b"1234"), io.BytesIO(), 3)

    def test_https_allowlist(self):
        for url in ("http://builds.dotnet.microsoft.com/a", "https://example.com/a",
                    "https://user@builds.dotnet.microsoft.com/a"):
            with self.assertRaises(ValueError):
                checked_url(url)

    def test_choose_exact(self):
        file = dict(rid="win-x64", name="runtime.zip",
                    url="https://builds.dotnet.microsoft.com/a", hash="a" * 128)
        metadata = {"releases": [{"release-version": "8.0.0",
                                  "runtime": {"version": "8.0.0", "files": [file]}}]}
        self.assertEqual(choose(metadata, "8.0.0", "win-x64"), (file["url"], file["hash"]))
        with self.assertRaises(ValueError):
            choose(metadata, "8.0.1", "win-x64")

    def test_zip_and_tar_sorted_members(self):
        prefix = "shared/Microsoft.NETCore.App/8.0.0/"
        with tempfile.TemporaryDirectory() as tmp:
            z = Path(tmp) / "input.zip"
            t = Path(tmp) / "input.tar.gz"
            with zipfile.ZipFile(z, "w") as archive:
                archive.writestr(prefix + "z.dll", b"Z")
                archive.writestr(prefix + "a.dll", b"A")
                archive.writestr("ignore", b"X")
            with tarfile.open(t, "w:gz") as archive:
                for name, data in (("./" + prefix + "z.dll", b"Z"), (prefix + "a.dll", b"A")):
                    item = tarfile.TarInfo(name)
                    item.size = len(data)
                    archive.addfile(item, io.BytesIO(data))
            for path in (z, t):
                out = io.BytesIO()
                members = append_members(path, "8.0.0", out, 2)
                self.assertEqual(out.getvalue(), b"AZ")
                self.assertEqual(len(members), 2)
                with self.assertRaises(ValueError):
                    append_members(path, "8.0.0", io.BytesIO(), 1)

    def test_unsafe_member(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "input.zip"
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("shared/Microsoft.NETCore.App/8.0.0/../bad", b"X")
            with self.assertRaises(ValueError):
                append_members(path, "8.0.0", io.BytesIO(), 8)


if __name__ == "__main__":
    unittest.main()
