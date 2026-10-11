"""A1-v2 calibration: immutable source identities and sealed evaluation exclusion."""
from __future__ import annotations

import copy
import hashlib
import io
import json
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path

import patch_dotnet_001_a1v2 as v2
import patch_dotnet_001_corpus as a1
from test_patch_dotnet_001_a2_classify import pe_image

PLAN = Path(__file__).resolve().parents[2] / (
    "docs/benchmarks/patch-dotnet-001/calibration-extension-v2.v1.json"
)


class CalibrationExtensionTests(unittest.TestCase):
    def setUp(self):
        self.plan = json.loads(PLAN.read_text())

    def test_actual_source_plan_and_role_lock(self):
        groups = v2.validate(self.plan)
        self.assertEqual(3, len(groups))
        self.assertEqual({"framework-dependent", "self-contained"},
                         {g["deployment"] for g in groups})
        self.assertTrue(all(g["role"] == "calibration" for g in groups))

    def test_no_holdout_relabel_or_digest_replacement(self):
        for k, value in [
            ("role", "evaluation"),
            ("product", "dotnet-sdk"),
        ]:
            plan = copy.deepcopy(self.plan)
            plan["groups"][0][k] = value
            with self.assertRaises(a1.CorpusError):
                v2.validate(plan)
        plan = copy.deepcopy(self.plan)
        plan["parentA2"]["filesSha256"] = "0" * 64
        with self.assertRaisesRegex(a1.CorpusError, "PARENT_A2"):
            v2.validate(plan)
        plan = copy.deepcopy(self.plan)
        plan["groups"][0]["assets"][0]["digest"] = "f" * 63
        with self.assertRaisesRegex(a1.CorpusError, "SOURCE_HASH"):
            v2.validate(plan)

    def test_eval_digest_index_pin_and_scope(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "a2-files.jsonl"
            rows = [
                {"role": "calibration", "sha256": "a" * 64},
                {"role": "evaluation", "sha256": "b" * 64},
                {"role": "evaluation", "sha256": "c" * 64},
            ]
            path.write_text("".join(json.dumps(r) + "\n" for r in rows))
            locked_sha = a1.sha256_file(path)
            self.assertEqual({"b" * 64, "c" * 64}, v2.eval_digest_set(path, locked_sha))
            with self.assertRaisesRegex(a1.CorpusError, "INDEX_SHA"):
                v2.eval_digest_set(path, "0" * 64)

    @staticmethod
    def fixture(doc: dict, folder: Path) -> tuple[Path, str]:
        sources = folder / "sources"
        sources.mkdir()
        overlap = ""
        for group in doc["groups"]:
            for asset in group["assets"]:
                payload = bytearray(pe_image())
                payload[0x550] = 1 if asset["version"] == "7.5.3" else 2
                blob = bytes(payload)
                if group["id"] == "ps-win-fdd" and asset["version"] == "7.5.4":
                    overlap = hashlib.sha256(blob).hexdigest()
                path = sources / asset["name"]
                if group["format"] == "zip":
                    with zipfile.ZipFile(path, "w") as stream:
                        stream.writestr("System.Management.Automation.dll", blob)
                else:
                    with tarfile.open(path, "w:gz") as stream:
                        item = tarfile.TarInfo("System.Management.Automation.dll")
                        item.size = len(blob)
                        stream.addfile(item, io.BytesIO(blob))
                asset["digest"] = a1.sha256_file(path)
        return sources, overlap

    def test_synthetic_full_pop_and_duplicate_exclusion(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            doc = copy.deepcopy(self.plan)
            sources, overlap = self.fixture(doc, folder)
            raw = a1.encode_json(doc)
            groups = v2.validate(doc)
            first = v2.materialize(doc, raw, groups, sources, folder / "one", {overlap})
            second = v2.materialize(doc, raw, groups, sources, folder / "two", {overlap})
            self.assertEqual(first["pairsSha256"], second["pairsSha256"])
            self.assertEqual(3, len(first["groups"]))
            self.assertTrue(first["noPatchBytesMeasured"])
            self.assertTrue(first["originalA2HoldoutUnchanged"])
            self.assertEqual(0, first["totalCalibrationStructuralD3Files"])
            self.assertEqual(0, first["distinctCalibrationStructuralD3Files"])
            pairs = json.loads((folder / "one/v2-pairs.json").read_text())["pairs"]
            self.assertEqual(3, len(pairs))
            self.assertTrue(all(len(g["changed"]) == 1 for g in pairs))
            self.assertTrue(all(g["changed"][0]["evaluationContentDuplicate"] for g in pairs))
            self.assertEqual(a1.sha256_file(folder / "one/v2-files.jsonl"), first["filesSha256"])

    def test_repeated_deployment_bytes_count_only_once_for_calibration(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            doc = copy.deepcopy(self.plan)
            sources, _ = self.fixture(doc, folder)
            audit = v2.materialize(doc, a1.encode_json(doc), v2.validate(doc),
                                   sources, folder / "without-eval-overlap", set())
            self.assertEqual(3, audit["totalCalibrationStructuralD3Files"])
            self.assertEqual(1, audit["distinctCalibrationStructuralD3Files"])
            self.assertEqual(2048, audit["distinctCalibrationStructuralD3TargetBytes"])
            pairs = json.loads((folder / "without-eval-overlap/v2-pairs.json").read_text())["pairs"]
            self.assertEqual(1, sum(int(r["firstDistinctEligibleTarget"])
                                    for p in pairs for r in p["changed"]))
            self.assertTrue(all(r["structuralD3Potential"] for p in pairs
                                for r in p["changed"]))

    def test_digest_mismatch_fails_before_any_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            doc = copy.deepcopy(self.plan)
            sources, _ = self.fixture(doc, folder)
            first = doc["groups"][0]["assets"][0]["name"]
            (sources / first).write_bytes(b"corrupted")
            with self.assertRaisesRegex(a1.CorpusError, "SOURCE_DIGEST_MISMATCH"):
                v2.materialize(doc, a1.encode_json(doc), v2.validate(doc), sources,
                               folder / "fail", set())
            self.assertFalse((folder / "fail").exists())


if __name__ == "__main__":
    unittest.main()
