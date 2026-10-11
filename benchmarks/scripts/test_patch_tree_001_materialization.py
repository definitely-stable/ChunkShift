#!/usr/bin/env python3
"""PATCH-TREE-001: synthetic, offline physical holdout-isolation contracts."""
import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

SCRIPT = Path(__file__).with_name("materialize_patch_corpus.py")
SPEC = importlib.util.spec_from_file_location("patch_tree_materialize", SCRIPT)
mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mod)

CALIBRATION = sorted(mod.TREE_CALIBRATION_FAMILIES)


class CalibrationMaterializationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.corpus = self.root / "metadata"
        self.sources = self.root / "sources"
        self.output = self.root / "output"
        self.corpus.mkdir()
        self.sources.mkdir()

        families = []
        assets = []
        self.files = {}
        for family in CALIBRATION + ["node-win-x64"]:
            families.append({
                "id": family,
                "split": "holdout" if family == "node-win-x64" else "calibration",
                "versions": ["v1", "v2"],
                "source": family + "-{v}.zip",
                "url": "https://invalid.example/" + family + "-{v}.zip",
                "layout": "zip",
                "path": "as-is",
            })
            if family == "node-win-x64":
                continue  # Intentionally no sealed holdout assets available.
            old = b"UNCHANGED"
            new = b"NEW CONTENT " + family.encode()
            self.files[family] = (old, new)
            for version, payload in (("v1", old), ("v2", new)):
                filename = family + "-" + version + ".zip"
                archive = self.sources / filename
                with zipfile.ZipFile(archive, "w") as zf:
                    zf.writestr("x.bin", payload)
                assets.append(mod.sha256_file(archive) + "  " + filename)
        manifest = {"schema": "chunkshift.patch-corpus.v1", "families": families}
        (self.corpus / "patch-corpus.json").write_text(json.dumps(manifest))
        (self.corpus / "source-assets.sha256").write_text("\n".join(assets) + "\n")
        originals = []
        for family in CALIBRATION:
            before, after = self.files[family]
            originals.append({
                "family": family, "base": "v1", "target": "v2",
                "changed": [{
                    "path": "x.bin", "baseSize": len(before),
                    "baseSha256": hashlib.sha256(before).hexdigest(),
                    "targetSize": len(after),
                    "targetSha256": hashlib.sha256(after).hexdigest(),
                }],
                "added": [], "removed": [], "identicalFiles": 0,
                "identicalBytes": 0,
            })
        originals.append({
            "family": "node-win-x64", "base": "v1", "target": "v2",
            "changed": [], "added": [], "removed": [],
            "identicalFiles": 10, "identicalBytes": 5000,
        })
        payload = json.dumps({"schema": "chunkshift.patch-pairs.v1",
                              "pairs": originals}, indent=1, sort_keys=True) + "\n"
        self.original = payload.encode()
        self.reference = self.root / "original-pairs.json"
        self.reference.write_bytes(self.original)
        (self.corpus / "corpus-lock.json").write_text(json.dumps({
            "pairsSha256": hashlib.sha256(self.original).hexdigest()
        }))

    def invoke(self, extra=None):
        argv = ["materialize_patch_corpus.py", "--root", str(self.output),
                "--sources", str(self.sources), "--lock", "--calibration-only",
                "--pairs-reference", str(self.reference)]
        argv.extend(extra or [])
        with patch.object(mod, "CORPUS", self.corpus), patch.object(sys, "argv", argv):
            return mod.main()

    def test_exact_original_pairs_kept_without_holdout_materialization(self):
        self.assertEqual(0, self.invoke())
        self.assertEqual(self.original, (self.output / "pairs.json").read_bytes())
        self.assertFalse((self.output / "tree" / "node-win-x64").exists())
        self.assertEqual({p.name for p in (self.output / "tree").iterdir()},
                         set(CALIBRATION))
        self.assertFalse(any("node-win-x64" in p.name for p in self.sources.iterdir()))

    def test_corrupt_reference_rejected_before_reading_sources(self):
        self.reference.write_bytes(self.original + b" ")
        with self.assertRaisesRegex(SystemExit, "complete pairs.json SHA-256 mismatch"):
            self.invoke()
        self.assertFalse((self.output / "tree").exists())

    def test_drifted_calibration_pair_rejected_not_silently_relocked(self):
        family = CALIBRATION[0]
        archive = self.sources / (family + "-v2.zip")
        with zipfile.ZipFile(archive, "w") as zf:
            zf.writestr("x.bin", b"CHANGED BEYOND THE ORIGINAL LOCK")
        checksum = self.corpus / "source-assets.sha256"
        checksum.write_text("\n".join(
            (mod.sha256_file(self.sources / (f + "-" + v + ".zip")) + "  " +
             f + "-" + v + ".zip")
            for f in CALIBRATION for v in ("v1", "v2")) + "\n")
        with self.assertRaisesRegex(SystemExit, "calibration trees differ"):
            self.invoke()
        self.assertFalse((self.output / "pairs.json").exists())

    def test_reject_existing_tree_without_touching_holdout_sentinel(self):
        sentinel = self.output / "tree" / "node-win-x64" / "secret.bin"
        sentinel.parent.mkdir(parents=True)
        sentinel.write_bytes(b"SEALED")
        with self.assertRaisesRegex(SystemExit, "fresh absent tree"):
            self.invoke()
        self.assertEqual(b"SEALED", sentinel.read_bytes())

    def test_no_reference_does_not_fall_back_to_full_corpus(self):
        argv = ["materialize_patch_corpus.py", "--root", str(self.output),
                "--calibration-only", "--lock"]
        with patch.object(mod, "CORPUS", self.corpus), patch.object(sys, "argv", argv):
            with self.assertRaises(SystemExit):
                mod.main()
        self.assertFalse((self.output / "tree").exists())


if __name__ == "__main__":
    unittest.main()
