"""Determinism and fail-closed checks for PATCH-EXEC-001 synthetic pilot."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import run_patch_exec_001_pilot as pilot


class PatchExecG1PilotTests(unittest.TestCase):
    def test_generation_is_reproducible_and_source_locked(self):
        with tempfile.TemporaryDirectory() as temp:
            first = Path(temp) / "first"
            second = Path(temp) / "second"
            digest = pilot.generate_corpus(first, 4, 131072)
            self.assertEqual(digest, pilot.generate_corpus(second, 4, 131072))
            self.assertEqual((first / "pairs.json").read_bytes(),
                             (second / "pairs.json").read_bytes())
            pairs = json.loads((first / "pairs.json").read_text())
            self.assertEqual(pairs["schema"], "chunkshift.patch-pairs.v1")
            rows = pairs["pairs"][0]["changed"]
            self.assertEqual(len(rows), 4)
            for row in rows:
                for version, prefix in [("v1", "base"), ("v2", "target")]:
                    body = (first / "tree" / pilot.FAMILY / version / row["path"]).read_bytes()
                    self.assertEqual(hashlib.sha256(body).hexdigest(),
                                     row[f"{prefix}Sha256"])
                    self.assertEqual(len(body), row[f"{prefix}Size"])
                self.assertNotEqual(row["baseSha256"], row["targetSha256"])

    def test_generator_is_exclusive_and_bounded(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "corpus"
            for count, size in ((1, 131072), (2, 131071), (129, 131072),
                                (2, 1048577)):
                with self.assertRaises(ValueError):
                    pilot.generate_corpus(root, count, size)
            pilot.generate_corpus(root, 2, 131072)
            with self.assertRaises(FileExistsError):
                pilot.generate_corpus(root, 2, 131072)

    def test_round_orders_cover_same_configs(self):
        names = {c[0] for c in pilot.CASES}
        self.assertEqual(len(names), 6)
        for index in range(5):
            order = pilot.round_order(index)
            self.assertEqual({c[0] for c in order}, names)
            self.assertEqual(len(order), len(names))
        self.assertEqual([x[0] for x in pilot.round_order(0)],
                         list(reversed([x[0] for x in pilot.round_order(1)])))

    def test_command_never_changes_release_or_encoding_policy(self):
        command = pilot.runner_command(
            Path("benchmark.csproj"), Path("/tmp/corpus"), Path("/tmp/out"),
            "h2-w2", 4, True, "test-run")
        self.assertIn("--batch-create", command)
        self.assertIn("--no-apply", command)
        self.assertIn("--execution", command)
        self.assertEqual(command[command.index("--workers") + 1], "4")
        self.assertNotIn("--patch-dir", command)


if __name__ == "__main__":
    unittest.main()
