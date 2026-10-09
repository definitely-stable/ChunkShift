#!/usr/bin/env python3
"""Synthetic regression tests for frozen G4 durable compact evidence recomputation."""
import base64
import gzip
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("recompute_patch_gap_g4_evidence.py")
SHA = "c71f0c8fa483c45bf65fee4850d293c45f7878a6"
HEADER = "datasetRole\tfamily\tbaseVersion\ttargetVersion\tpath\th0Bytes\tg4Bytes\tsavedBytes\teligible\twinners\n"


def hashbytes(value):
    return hashlib.sha256(value).hexdigest()


def save(path, data):
    path.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")


class G4DurableRecomputeTests(unittest.TestCase):
    @staticmethod
    def fixture(root, evaluation_win=False):
        splits = {}
        artifact = {}
        shards = []
        allrows = []
        for role, count, h0, reduction in (
            ("calibration", 1049, 11860274, 102076),
            ("evaluation", 844, 26363364, 5272673 if evaluation_win else 0),
        ):
            rows = []
            family = "fixture-" + role
            for ordinal in range(count):
                base = 100 if ordinal else h0 - (count - 1) * 100
                saved = reduction if ordinal == 0 else 0
                outcome = base - saved
                row = "\t".join([role, family, "old", "new", str(ordinal),
                                 str(base), str(outcome), str(saved), "0", "0"]) + "\n"
                rows.append(row)
                allrows.append(((role, family, "old", "new", str(ordinal)), row))
            plain = (HEADER + "".join(rows)).encode("utf-8")
            packed = gzip.compress(plain, compresslevel=9, mtime=0)
            filename = "files/" + role + "-" + family + ".tsv.gz.b64"
            (root / "files").mkdir(exist_ok=True)
            (root / filename).write_text(base64.b64encode(packed).decode("ascii") + "\n",
                                         encoding="ascii")
            shards.append({"path": filename, "datasetRole": role, "family": family,
                           "rows": count, "tsvBytes": len(plain), "tsvSha256": hashbytes(plain),
                           "gzipBytes": len(packed), "gzipSha256": hashbytes(packed)})
            gate = 20 * reduction >= 3 * h0
            splits[role] = {
                "lane": {"h0Bytes": h0, "factorBytes": h0 - reduction, "savedBytes": reduction,
                         "meetsRfcSizeGate": gate, "bcjWinnerEntries": 0},
                "families": {family: {"files": count, "eligible": 0, "h0": h0,
                                     "g4": h0 - reduction, "saved": reduction, "winners": 0}},
            }
            document = {name: {"bytes": 100, "sha256": "a" * 64}
                        for name in ("g4-index.json", "h0-anchor.json",
                                     "xz-provenance.json", "verify.txt")}
            document["details"] = {
                "count": count, "manifestSha256": "b" * 64, "manifestBytes": 200,
            }
            artifact[role] = {"sourceCommit": SHA, "documents": document}
        canonical = (HEADER + "".join(line for _, line in sorted(allrows))).encode("utf-8")
        manifest = {
            "canonicalHeader": HEADER.rstrip("\n"),
            "combined": {"rows": len(allrows), "bytes": len(canonical),
                         "sha256": hashbytes(canonical)},
            "shards": shards,
        }
        verdict = "SIZE_GATE_PASS_NON_SIZE_PENDING" if evaluation_win else "REJECT"
        save(root / "summary.json", {"schema": "chunkshift.patch-gap-g4-evidence.v1",
                                      "sourceCommit": SHA, "splits": splits, "status": verdict})
        save(root / "artifacts.json", artifact)
        save(root / "files-manifest.json", manifest)

    @staticmethod
    def execute(root):
        return subprocess.run([sys.executable, str(SCRIPT), "--root", str(root)],
                              capture_output=True, text=True, check=False)

    def test_reject_verdict_exact_counts_and_digest(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.fixture(root)
            test = self.execute(root)
            self.assertEqual(0, test.returncode, test.stderr)
            self.assertIn("rows=1893", test.stdout)

    def test_positive_size_gate_does_not_prove_adoption(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.fixture(root, evaluation_win=True)
            test = self.execute(root)
            self.assertEqual(0, test.returncode, test.stderr)
            self.assertIn("gate=True", test.stdout)

    def test_corrupted_payload_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.fixture(root)
            file = next((root / "files").glob("*.tsv.gz.b64"))
            file.write_text("AAAA\n", encoding="ascii")
            self.assertNotEqual(0, self.execute(root).returncode)

    def test_aggregate_tampering_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.fixture(root)
            path = root / "summary.json"
            data = json.loads(path.read_text(encoding="utf-8"))
            data["splits"]["evaluation"]["lane"]["savedBytes"] += 1
            save(path, data)
            self.assertNotEqual(0, self.execute(root).returncode)


if __name__ == "__main__":
    unittest.main()
