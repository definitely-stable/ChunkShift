"""Deterministic, adversarial tests for PATCH-DOTNET-001 corpus preregistration."""
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

import patch_dotnet_001_corpus as c

ROOT = Path(__file__).resolve().parents[2]
PLAN = ROOT / "docs/benchmarks/patch-dotnet-001/corpus-plan.v1.json"


class CorpusLockTests(unittest.TestCase):
    def setUp(self):
        self.plan = json.loads(PLAN.read_text(encoding="utf-8"))

    def test_real_plan_preregistered_no_family_leakage(self):
        groups = c.validate_plan(self.plan)
        self.assertEqual(5, len(groups))
        self.assertEqual(
            ["desktop-win-x64", "sdk-linux-x64"],
            sorted(g["id"] for g in groups if g["role"] == "evaluation"),
        )

    def test_reject_role_leakage_and_duplicate_groups(self):
        plan = copy.deepcopy(self.plan)
        plan["groups"][2]["product"] = plan["groups"][0]["product"]
        with self.assertRaisesRegex(c.CorpusError, "LEAKAGE"):
            c.validate_plan(plan)
        plan = copy.deepcopy(self.plan)
        plan["groups"][1]["id"] = plan["groups"][0]["id"]
        with self.assertRaisesRegex(c.CorpusError, "DUPLICATE"):
            c.validate_plan(plan)

    def test_reject_invalid_asset_hash_or_non_https(self):
        plan = copy.deepcopy(self.plan)
        plan["groups"][0]["assets"][0]["digest"] = "0" * 63
        with self.assertRaisesRegex(c.CorpusError, "DIGEST"):
            c.validate_plan(plan)
        plan = copy.deepcopy(self.plan)
        plan["groups"][0]["assets"][0]["url"] = "http://example.com/payload.zip"
        with self.assertRaisesRegex(c.CorpusError, "PROVENANCE"):
            c.validate_plan(plan)

    def test_reject_missing_candidate_scope(self):
        plan = copy.deepcopy(self.plan)
        plan["groups"][2]["candidateAllowPrefixes"] = []
        with self.assertRaisesRegex(c.CorpusError, "MISSING_CANDIDATE_SCOPE"):
            c.validate_plan(plan)
        plan = copy.deepcopy(self.plan)
        plan["groups"][0]["candidateAllowPrefixes"] = ["../escape/"]
        with self.assertRaises(c.CorpusError):
            c.validate_plan(plan)

    def test_strict_member_paths(self):
        for name in ("../evil", "/etc/passwd", "foo/../../escape", "C:/attack",
                     "a\\..\\evil", "a/\x00xyz"):
            with self.subTest(name=name), self.assertRaises(c.CorpusError):
                c.relative_member(name, "as-is")
        self.assertEqual("{version}/data.bin",
                         c.relative_member("./10.0.11/data.bin", "replace-dotnet10-version-components"))
        self.assertEqual("{version}/data.bin",
                         c.relative_member("10.0.112/data.bin", "replace-dotnet10-version-components"))
        self.assertEqual("data.bin", c.relative_member("release/data.bin", "strip-first"))

    @staticmethod
    def build_fixture(plan: dict, folder: Path, tamper: str | None = None):
        sources = folder / "sources"
        sources.mkdir()
        for group in plan["groups"]:
            for asset in group["assets"]:
                version = asset["version"]
                rule = group["pathRule"]
                prefix = "node-package" if rule == "strip-first" else ""
                scoped = group["candidateAllowPrefixes"][0] if group["candidateAllowPrefixes"] else "nested/"
                name = f"{prefix + '/' if prefix else ''}{scoped}{version + '/' if rule != 'strip-first' else ''}data.bin"
                if tamper == "escape" and group["id"] == plan["groups"][0]["id"] \
                        and version == group["base"]:
                    name = "../escape.bin"
                if tamper == "case" and group["id"] == plan["groups"][0]["id"] \
                        and version == group["base"]:
                    members = [(name, b"alpha"), (name.upper(), b"bravo")]
                else:
                    members = [(name, f"payload-{version}".encode("ascii"))]

                path = sources / asset["name"]
                if group["format"] == "zip":
                    with zipfile.ZipFile(path, "w") as archive:
                        for item_name, content in members:
                            archive.writestr(item_name, content)
                else:
                    with tarfile.open(path, "w:gz") as archive:
                        for item_name, content in members:
                            info = tarfile.TarInfo(item_name)
                            info.size = len(content)
                            info.mtime = 0
                            archive.addfile(info, io.BytesIO(content))
                asset["digest"] = c.digest_file(path, asset["digestAlgorithm"])
        return sources

    def test_materialization_is_exact_repeatable_and_zero_patch(self):
        with tempfile.TemporaryDirectory() as directory:
            tmp = Path(directory)
            plan = copy.deepcopy(self.plan)
            sources = self.build_fixture(plan, tmp)
            raw = c.encode_json(plan)
            groups = c.validate_plan(plan)
            first = c.materialize(raw, groups, sources, tmp / "result-a")
            second = c.materialize(raw, groups, sources, tmp / "result-b")
            self.assertEqual(first["pairsSha256"], second["pairsSha256"])
            self.assertEqual(first["planSha256"], hashlib.sha256(raw).hexdigest())
            self.assertEqual(10, len(first["sourceAssets"]))
            self.assertEqual("INVENTORY_ONLY_NOT_DECISION_EVIDENCE", first["status"])
            pairs = json.loads((tmp / "result-a" / "pairs.json").read_text())
            self.assertEqual("chunkshift.patch-pairs.v1", pairs["schema"])
            self.assertEqual(5, len(pairs["pairs"]))
            for pair in pairs["pairs"]:
                self.assertEqual(1, len(pair["changed"]))
                self.assertEqual(0 if pair["role"] == "negative-control" else 1,
                                 len(pair["candidatePaths"]))
                self.assertEqual(pair["changed"][0]["targetSize"] if pair["candidatePaths"] else 0,
                                 pair["candidateTargetBytes"])
                self.assertNotEqual(pair["changed"][0]["baseSha256"],
                                    pair["changed"][0]["targetSha256"])
            with self.assertRaisesRegex(c.CorpusError, "ALREADY_EXISTS"):
                c.materialize(raw, groups, sources, tmp / "result-a")

    def test_archive_escape_fails_before_result_publication(self):
        with tempfile.TemporaryDirectory() as directory:
            tmp = Path(directory)
            plan = copy.deepcopy(self.plan)
            sources = self.build_fixture(plan, tmp, tamper="escape")
            with self.assertRaisesRegex(c.CorpusError, "TRAVERSAL"):
                c.materialize(c.encode_json(plan), c.validate_plan(plan), sources, tmp / "result")
            self.assertFalse((tmp / "result").exists())
            self.assertFalse((tmp / "escape.bin").exists())

    def test_case_collision_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            tmp = Path(directory)
            plan = copy.deepcopy(self.plan)
            sources = self.build_fixture(plan, tmp, tamper="case")
            with self.assertRaisesRegex(c.CorpusError, "DUPLICATE"):
                c.materialize(c.encode_json(plan), c.validate_plan(plan), sources, tmp / "result")
            self.assertFalse((tmp / "result").exists())

    def test_corrupted_source_rejected_before_extract(self):
        with tempfile.TemporaryDirectory() as directory:
            tmp = Path(directory)
            plan = copy.deepcopy(self.plan)
            sources = self.build_fixture(plan, tmp)
            first = plan["groups"][0]["assets"][0]["name"]
            with (sources / first).open("ab") as output:
                output.write(b"tampering")
            with self.assertRaisesRegex(c.CorpusError, "DIGEST_MISMATCH"):
                c.materialize(c.encode_json(plan), c.validate_plan(plan), sources, tmp / "result")
            self.assertFalse((tmp / "result").exists())


if __name__ == "__main__":
    unittest.main()
