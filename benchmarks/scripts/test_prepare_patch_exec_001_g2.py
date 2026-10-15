"""Contract tests for PATCH-EXEC-001 G2 population freeze (no holdout)."""
import hashlib
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

import prepare_patch_exec_001_g2 as g2


def make_row(path, n=131072):
    return {"path": path, "baseSize": n, "targetSize": n,
            "baseSha256": "a" * 64, "targetSha256": "b" * 64}


def fake_document():
    pairs = []
    for family in g2.FAMILIES:
        for base, target in g2.PAIRS.values():
            rows = []
            for lower, upper, quota in g2.BINS:
                n = (lower + upper) // 2
                for k in range(quota + 2):
                    rows.append(make_row(f"bin{lower}/item{k:02}.dll", n))
            pairs.append({"family": family, "base": base, "target": target,
                          "changed": rows, "added": [], "removed": [],
                          "identicalFiles": 0, "identicalBytes": 0})
    pairs.append({"family": "node-linux-x64", "base": "holdout",
                  "target": "holdout2", "changed": [make_row("MUST_NOT_READ.dll")]})
    return {"schema": "chunkshift.patch-pairs.v1", "pairs": pairs}


class PatchExecG2PopulationTests(unittest.TestCase):
    def test_pinned_selection_is_exact_and_disjoint_across_roles(self):
        doc = fake_document()
        train = g2.choose_pairs(doc, "train")
        validation = g2.choose_pairs(doc, "validation")
        self.assertEqual(len(train), 2)
        self.assertEqual(sum(len(p["changed"]) for p in train), 16)
        self.assertEqual([p["family"] for p in train], list(g2.FAMILIES))
        self.assertEqual({p["base"] for p in train}, {"10.0.10"})
        self.assertEqual({p["base"] for p in validation}, {"10.0.11"})
        self.assertEqual(train, g2.choose_pairs(doc, "train"))
        self.assertEqual(len(validation), 2)
        for pair in train:
            self.assertEqual(len({r["path"] for r in pair["changed"]}), 8)
            self.assertNotIn("node", pair["family"])

    def test_inadequate_population_and_duplicate_are_rejected(self):
        doc = fake_document()
        del doc["pairs"][0]["changed"][:4]
        with self.assertRaises(ValueError):
            g2.choose_pairs(doc, "train")
        doc = fake_document()
        doc["pairs"].append(doc["pairs"][0])
        with self.assertRaises(ValueError):
            g2.choose_pairs(doc, "train")

    def test_paths_cannot_escape_or_use_host_specific_forms(self):
        for bad in ("", "../escape", "a/../../b", "/abs", "a\\b",
                    "C:/windows", "a//b", "a/./b", "trailing/", "a\x00b"):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                g2.safe_path(bad)
        self.assertEqual(str(g2.safe_path("shared/{version}/bin.dll")),
                         "shared/{version}/bin.dll")

    def test_materialize_checks_source_bytes_and_is_exclusive(self):
        with TemporaryDirectory() as root:
            base = Path(root)
            orig, subset = base / "original", base / "selected"
            original_rows = []
            for fam in g2.FAMILIES:
                base_bytes = (fam + "/base").encode("utf-8")
                target_bytes = (fam + "/target").encode("utf-8")
                row = {"path": "assembly.dll",
                       "baseSize": len(base_bytes), "targetSize": len(target_bytes),
                       "baseSha256": hashlib.sha256(base_bytes).hexdigest(),
                       "targetSha256": hashlib.sha256(target_bytes).hexdigest()}
                for version, payload in (("10.0.10", base_bytes),
                                         ("10.0.11", target_bytes)):
                    path = orig / "tree" / fam / version / row["path"]
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(payload)
                original_rows.append({"family": fam, "base": "10.0.10",
                                      "target": "10.0.11", "changed": [row]})
            pairs = {"schema": "chunkshift.patch-pairs.v1", "pairs": original_rows}
            reference = (json.dumps(pairs, sort_keys=True) + "\n").encode()
            original_pairs = orig / "pairs.json"
            original_pairs.write_bytes(reference)
            pin = base / "reference.json"
            pin.write_bytes(reference)
            chosen = {"schema": "chunkshift.patch-pairs.v1",
                      "pairs": [dict(p, added=[], removed=[], identicalFiles=0,
                                     identicalBytes=0) for p in original_rows]}
            selected = (json.dumps(chosen, sort_keys=True, indent=2,
                                   ensure_ascii=False) + "\n").encode()
            expected_selection = hashlib.sha256(selected).hexdigest()
            with patch.object(g2, "FROZEN_PAIRS_SHA256", hashlib.sha256(reference).hexdigest()), \
                 patch.object(g2, "EXPECTED_SELECTED_SHA256", {"train": expected_selection}), \
                 patch.object(g2, "BINS", ((1, 10000, 1),)):
                result = g2.prepare(orig, subset, "train", pin)
                self.assertEqual(result["fileCount"], 2)
                self.assertEqual(result["baseAndTargetFilesVerified"], 4)
                self.assertEqual(hashlib.sha256((subset / "pairs.json").read_bytes()).hexdigest(),
                                 result["selectedPairsSha256"])
                file = subset / "tree" / g2.FAMILIES[0] / "10.0.10" / "assembly.dll"
                self.assertEqual(os.stat(file).st_ino, os.stat(
                    orig / "tree" / g2.FAMILIES[0] / "10.0.10" / "assembly.dll").st_ino)
                with self.assertRaises(ValueError):
                    g2.prepare(orig, subset, "train", pin)
                (subset / "pairs.json").unlink()
                with self.assertRaises(ValueError):
                    g2.prepare(orig, orig / "inside", "train", pin)
                # A corrupted source must fail before publishing any subset.
                corrupt = orig / "tree" / g2.FAMILIES[0] / "10.0.11" / "assembly.dll"
                corrupt.write_bytes(b"tampered")
                with self.assertRaises(ValueError):
                    g2.prepare(orig, base / "corrupt", "train", pin)
                self.assertFalse((base / "corrupt").exists())

    def test_wrong_reference_digest_is_rejected(self):
        with TemporaryDirectory() as tmp:
            p = Path(tmp)
            (p / "pairs.json").write_text('{"schema":"chunkshift.patch-pairs.v1"}')
            with self.assertRaises(ValueError):
                g2.prepare(p, p / "selected", "train", p / "pairs.json")


if __name__ == "__main__":
    unittest.main()
