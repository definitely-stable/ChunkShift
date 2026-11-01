"""Fixture-only contract tests for HDiffPatch reference. Real binary smoke is CI."""
import importlib.util
import json
import tempfile
import unittest
import hashlib
import zipfile
from pathlib import Path
from unittest import mock

SCRIPT = Path(__file__).with_name("patch_gap_hdiff_reference.py")
spec = importlib.util.spec_from_file_location("patch_gap_hdiff", SCRIPT)
ref = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ref)


class ReferenceHarnessTests(unittest.TestCase):
    def test_frozen_lanes_and_identity(self):
        self.assertEqual(ref.LANES, {"memory": "-m-4", "stream": "-s-64"})
        self.assertEqual(ref.PAIRS_SHA,
                         "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd")
        self.assertEqual(ref.ARCHIVE_SHA,
                         "628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558")

    def test_requires_frozen_corpus(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "pairs.json").write_text("{}", encoding="utf-8")
            with self.assertRaisesRegex(ref.EvidenceError, "SHA-256 mismatch"):
                ref.pair_jobs(root, "calibration")

    def test_failure_does_not_create_decision_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive = root / "unsupported.zip"
            archive.write_bytes(b"tampered")
            out = root / "result.json"
            with self.assertRaisesRegex(ref.EvidenceError, "archive SHA mismatch"):
                with mock.patch.object(ref, "sha",
                                       side_effect=lambda p: ("f" * 64 if p == archive
                                                               else "b" * 64)):
                    ref.main(["--mode", "smoke", "--hdiffz", str(archive),
                              "--hpatchz", str(archive), "--archive", str(archive),
                              "--output", str(out), "--work", str(root / "scratch")])
            self.assertFalse(out.exists())

    def test_rejects_executable_substitution_even_when_zip_matches(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive = root / "hdiff.zip"
            with zipfile.ZipFile(archive, "w") as zipped:
                zipped.writestr("linux64/hdiffz", b"official-diff")
                zipped.writestr("linux64/hpatchz", b"official-patch")
            hdiffz = root / "hdiffz"
            hpatchz = root / "hpatchz"
            hdiffz.write_bytes(b"official-diff")
            hpatchz.write_bytes(b"substitute")
            with mock.patch.object(ref, "ARCHIVE_SHA",
                                   hashlib.sha256(archive.read_bytes()).hexdigest()):
                with self.assertRaisesRegex(ref.EvidenceError,
                                            "does not match SHA-pinned"):
                    ref.verify_binary_archive(archive, hdiffz, hpatchz)
                hpatchz.write_bytes(b"official-patch")
                accepted = ref.verify_binary_archive(archive, hdiffz, hpatchz)
                self.assertEqual(set(accepted), {"hdiffz", "hpatchz"})

    def test_command_failure_is_fatal(self):
        with mock.patch.object(ref.subprocess, "run") as run:
            run.return_value.returncode = 42
            run.return_value.stderr = b"bad codec"
            with self.assertRaisesRegex(ref.EvidenceError, "exit=42"):
                ref._exec(["/tmp/hdiffz", "-m-4"], 3)

    def test_run_pair_rejects_missing_inputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            with self.assertRaisesRegex(ref.EvidenceError, "Missing input"):
                ref.run_pair(root / "nonexistent", root / "target",
                             root / "hdiffz", root / "hpatchz", root / "out")

    def test_run_pair_rejects_reconstruction_mismatch(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            old, new = root / "old", root / "new"
            old.write_bytes(b"old")
            new.write_bytes(b"new")
            a, b = root / "hdiffz", root / "hpatchz"
            a.write_bytes(b"not an executable")
            b.write_bytes(b"not an executable")
            def fake(args, timeout):
                if args[0] == str(a):
                    Path(args[-1]).write_bytes(b"compressed")
                else:
                    Path(args[-1]).write_bytes(b"wrong")
                return 0.1
            with mock.patch.object(ref, "_exec", side_effect=fake):
                with self.assertRaisesRegex(ref.EvidenceError, "reconstructed target mismatch"):
                    ref.run_pair(old, new, a, b, root / "out")

    def test_never_calls_second_lane_if_first_oracle_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            old, new = root / "old", root / "new"
            old.write_bytes(b"A" * 32)
            new.write_bytes(b"B" * 32)
            a, b = root / "hdiffz", root / "hpatchz"
            a.touch()
            b.touch()
            calls = []
            def fake(args, timeout):
                calls.append(args)
                if args[0] == str(a):
                    Path(args[-1]).write_bytes(b"patch")
                else:
                    Path(args[-1]).write_bytes(b"bad")
                return 0
            with mock.patch.object(ref, "_exec", side_effect=fake):
                with self.assertRaises(ref.EvidenceError):
                    ref.run_pair(old, new, a, b, root / "out")
            self.assertEqual(len(calls), 2)


if __name__ == "__main__":
    unittest.main()
