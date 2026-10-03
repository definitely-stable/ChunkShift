"""Exercise the workflow's actual Python blocks without Linux build dependencies."""
import pathlib
import subprocess
import sys
import tempfile
import textwrap
import unittest
import zipfile

WORKFLOW = pathlib.Path(__file__).resolve().parents[2] / ".github/workflows/patch-gap-001-puffin-toolchain.yml"


def workflow_python(command):
    body = WORKFLOW.read_text(encoding="utf-8").split(command + " <<'PY'\n", 1)[1]
    return textwrap.dedent(body.split("          PY\n", 1)[0])


class PuffinRuntimePackagingTests(unittest.TestCase):
    def test_packages_loader_names_as_regular_files_through_zip(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            libraries = root / "system"
            libraries.mkdir()
            package = root / "lib64"
            package.mkdir()
            dependencies = {
                "libglog.so.1": "libglog.so.0.6.0",
                "libgflags.so.2.2": "libgflags.so.2.2.2",
                "libunwind.so.8": "libunwind.so.8.0.1",
                "liblzma.so.5": "liblzma.so.5.4.5",
            }
            lines = []
            for name, versioned in dependencies.items():
                source = libraries / versioned
                source.write_bytes(versioned.encode())
                lines.append(f"{name} => {source.as_posix()[len(source.drive):]} (0x1234)")
            # The producer intentionally relies on the runner for the base C++ runtime.
            base = libraries / "libstdc++.so.6.0.33"
            base.write_bytes(b"base-runtime")
            lines.append(f"libstdc++.so.6 => {base.as_posix()[len(base.drive):]} (0x1234)")
            ldd = root / "ldd.txt"
            ldd.write_text("\n".join(lines), encoding="utf-8")
            result = subprocess.run(
                [sys.executable, "-c", workflow_python('          python3 - "$PACKAGE_DIR/ldd.txt" "$PACKAGE_DIR/lib64"'), str(ldd), str(package)],
                capture_output=True, text=True, check=False,
            )
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(set(dependencies), {path.name for path in package.iterdir()})
            archive = root / "package.zip"
            with zipfile.ZipFile(archive, "w") as bundle:
                for path in package.iterdir():
                    self.assertFalse(path.is_symlink())
                    bundle.write(path, f"lib64/{path.name}")
            with zipfile.ZipFile(archive) as bundle:
                bundle.extractall(root / "extracted")
            for name, versioned in dependencies.items():
                self.assertEqual(versioned.encode(), (root / "extracted/lib64" / name).read_bytes())


    def test_runtime_replay_rejects_system_fallback_and_missing_dependency(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            package = root / "lib64"
            package.mkdir()
            library = package / "libglog.so.1"
            library.write_bytes(b"packaged-glog")
            system = root / "system"
            system.mkdir()
            other = system / library.name
            other.write_bytes(library.read_bytes())
            ldd = root / "ldd-packaged.txt"
            script = workflow_python('          python3 - "$PACKAGE_DIR/ldd-packaged.txt" "$PACKAGE_DIR/lib64"')
            cases = (
                (f"libglog.so.1 => {library.as_posix()[len(library.drive):]} (0x1234)", 0, ""),
                (f"libglog.so.1 => {other.as_posix()[len(other.drive):]} (0x1234)", 1, "outside lib64"),
                ("libglog.so.1 => not found", 1, "outside lib64"),
                ("linux-vdso.so.1 (0x1234)", 1, "absent from ldd"),
            )
            for line, exit_code, message in cases:
                with self.subTest(line=line):
                    ldd.write_text(line, encoding="utf-8")
                    result = subprocess.run(
                        [sys.executable, "-c", script, str(ldd), str(package)],
                        capture_output=True, text=True, check=False,
                    )
                    self.assertEqual(exit_code, result.returncode, result.stderr)
                    if message:
                        self.assertIn(message, result.stderr)


if __name__ == "__main__":
    unittest.main()
