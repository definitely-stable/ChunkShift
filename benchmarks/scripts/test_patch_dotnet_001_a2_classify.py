"""PATCH-DOTNET-001 A2: hand-authored PE/CLR/R2R and A1 lock-failure tests."""
from __future__ import annotations

import copy
import json
import struct
import tempfile
import unittest
from pathlib import Path

import patch_dotnet_001_a2_classify as a2
import patch_dotnet_001_corpus as a1
from test_patch_dotnet_001_corpus import CorpusLockTests

REPO = Path(__file__).resolve().parents[2]
PLAN = REPO / "docs/benchmarks/patch-dotnet-001/corpus-plan.v1.json"
LOCK = REPO / "docs/benchmarks/patch-dotnet-001/a2-classification-lock.v1.json"


def pe_image(*, ilonly=True, r2r=False, malformed=False, clr=True, machine=0x8664) -> bytes:
    out = bytearray(0x800)
    out[:2] = b"MZ"
    struct.pack_into("<I", out, 0x3C, 0x80)
    out[0x80:0x84] = b"PE\x00\x00"
    struct.pack_into("<HHIIIHH", out, 0x84, machine, 1, 0, 0, 0, 224, 0x2022)
    opt = 0x98
    struct.pack_into("<H", out, opt, 0x10B)
    struct.pack_into("<I", out, opt + 92, 16)
    if clr:
        struct.pack_into("<II", out, opt + 96 + 14 * 8, 0x2000, 72)
    sec = opt + 224
    out[sec:sec + 8] = b".text\x00\x00\x00"
    struct.pack_into("<IIII", out, sec + 8, 0x600, 0x2000, 0x600, 0x200)
    if clr:
        struct.pack_into("<I", out, 0x200, 72)
        struct.pack_into("<II", out, 0x208, 0x2100, 256)
        struct.pack_into("<I", out, 0x210, 1 if ilonly else 0)
        if r2r:
            struct.pack_into("<II", out, 0x240, 0x2200, 16)
            out[0x400:0x404] = b"RTR\x00"
        meta = 0x300
        out[meta:meta + 4] = b"BSJB"
        struct.pack_into("<I", out, meta + 12, 16)
        out[meta + 16:meta + 29] = b"v4.0.30319\x00\x00\x00"
        struct.pack_into("<HH", out, meta + 32, 0, 1)
        struct.pack_into("<II", out, meta + 36, 0x80, 0x40)
        out[meta + 44:meta + 48] = b"#~\x00\x00"
        if malformed:
            out[meta:meta + 4] = b"NOPE"
    return bytes(out)


class PatchDotnetA2Tests(unittest.TestCase):
    def test_lock_matches_pinned_source_identity(self):
        lock = json.loads(LOCK.read_text())
        self.assertEqual(a2.SCHEMA, lock["schema"])
        self.assertEqual(a2.sha256(PLAN.read_bytes()), lock["planSha256"])
        self.assertEqual(15686, lock["expectedTotalFileRows"])
        self.assertEqual(3907, lock["expectedChangedFiles"])

    def test_hand_authored_pe_classes(self):
        cases = [
            ({}, "ILONLY"),
            ({"ilonly": False}, "MIXED_MODE"),
            ({"r2r": True}, "R2R_HEADER"),
            ({"malformed": True}, "MALFORMED"),
            ({"clr": False}, "PE_NATIVE_UNKNOWN"),
            ({"machine": 0x7777}, "UNSUPPORTED"),
        ]
        with tempfile.TemporaryDirectory() as scratch:
            file = Path(scratch) / "image.dll"
            for kw, kind in cases:
                with self.subTest(kw=kw):
                    file.write_bytes(pe_image(**kw))
                    observed = a2.classify(file, 64 * 1024 * 1024)
                    self.assertEqual(kind, observed["kind"], observed)
                    if kw.get("machine") == 0x7777:
                        self.assertEqual("unknown", observed["architecture"])

    def test_native_control_and_corruption(self):
        with tempfile.TemporaryDirectory() as scratch:
            file = Path(scratch) / "test.bin"
            for raw, expected in [(b"\x7fELFmore", "ELF_NATIVE_UNKNOWN"),
                                  (b"MZ", "MALFORMED"),
                                  (b"plain text", "OTHER"),
                                  (b"\xcf\xfa\xed\xfeaaaa", "MACHO_NATIVE_UNKNOWN")]:
                file.write_bytes(raw)
                self.assertEqual(expected, a2.classify(file, 67108864)["kind"])
            file.write_bytes(pe_image())
            self.assertEqual("UNSUPPORTED", a2.classify(file, 128)["kind"])
            corrupt = bytearray(pe_image())
            struct.pack_into("<I", corrupt, 0x3C, 0x7FFFFFFF)
            file.write_bytes(corrupt)
            self.assertEqual("PE_OFFSET_INVALID", a2.classify(file, 67108864)["reason"])

    def test_structurally_invalid_metadata_rva_fails_closed(self):
        with tempfile.TemporaryDirectory() as scratch:
            file = Path(scratch) / "bad.dll"
            damaged = bytearray(pe_image())
            struct.pack_into("<I", damaged, 0x208, 0xF0000000)
            file.write_bytes(damaged)
            self.assertEqual("MALFORMED", a2.classify(file, 67108864)["kind"])

    def _synthetic(self, directory: Path):
        plan = json.loads(PLAN.read_text())
        source = CorpusLockTests.build_fixture(plan, directory)
        raw = a1.encode_json(plan)
        groups = a1.validate_plan(plan)
        (directory / "fixture-plan.json").write_bytes(raw)
        root = directory / "materialized"
        audit = a1.materialize(raw, groups, source, root)
        pairs = json.loads((root / "pairs.json").read_text())["pairs"]
        lock = json.loads(LOCK.read_text())
        lock.update(planSha256=a2.sha256(raw), pairsSha256=audit["pairsSha256"],
                    filesSha256=audit["filesSha256"],
                    expectedTotalFileRows=audit["inventoryFileRows"],
                    expectedChangedFiles=sum(len(p["changed"]) for p in pairs),
                    expectedChangedTargetBytes=sum(
                        x["targetSize"] for p in pairs for x in p["changed"]),
                    expectedPathCandidateFiles=sum(len(p["candidatePaths"]) for p in pairs),
                    expectedPathCandidateBytes=sum(p["candidateTargetBytes"] for p in pairs))
        (directory / "fixture-lock.json").write_text(json.dumps(lock))
        return root, directory / "fixture-plan.json", directory / "fixture-lock.json"

    def test_a2_reproducible_and_no_patch_fields(self):
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            root, plan, lock = self._synthetic(folder)
            left = a2.analyze(root, plan, lock, folder / "output-a")
            right = a2.analyze(root, plan, lock, folder / "output-b")
            self.assertEqual(left, right)
            self.assertEqual(left["filesSha256"], a2.file_hash(folder / "output-a/a2-files.jsonl"))
            self.assertEqual(0, left["totals"]["d3PotentialBytes"])
            self.assertEqual("STRUCTURAL_INVENTORY_ONLY_NO_PATCH_VERDICT", left["status"])
            self.assertNotIn("patchSize", json.dumps(left))
            rows = [json.loads(s) for s in
                    (folder / "output-a/a2-files.jsonl").read_text().splitlines()]
            self.assertTrue(all(r["parserClassification"] == r["kind"] for r in rows))

    def test_a2_rejects_modified_source_and_role(self):
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            root, plan, lock = self._synthetic(folder)
            file = next((root / "tree").rglob("data.bin"))
            file.write_bytes(b"tampered")
            with self.assertRaisesRegex(a2.AuditError, "DRIFT"):
                a2.analyze(root, plan, lock, folder / "output")

    def test_a2_rejects_symlinked_intermediate_path(self):
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            root, plan, lock = self._synthetic(folder)
            sample = next((root / "tree").rglob("data.bin"))
            parent = sample.parent
            moved = parent.with_name(parent.name + "-copy")
            parent.rename(moved)
            parent.symlink_to(moved, target_is_directory=True)
            with self.assertRaisesRegex(a2.AuditError, "SYMLINK_OR_PATH_ESCAPE"):
                a2.analyze(root, plan, lock, folder / "output")

    def test_a2_rejects_plan_and_frozen_pairs_drift(self):
        with tempfile.TemporaryDirectory() as scratch:
            folder = Path(scratch)
            root, plan, lock = self._synthetic(folder)
            wrong = json.loads(plan.read_text())
            wrong["groups"][0]["role"] = "evaluation"
            plan.write_text(json.dumps(wrong))
            with self.assertRaisesRegex(a2.AuditError, "PLAN_HASH"):
                a2.analyze(root, plan, lock, folder / "out")
            plan.write_bytes(a1.encode_json(json.loads(PLAN.read_text())))  # still wrong identity
            with self.assertRaisesRegex(a2.AuditError, "PLAN_HASH"):
                a2.analyze(root, plan, lock, folder / "out")


if __name__ == "__main__":
    unittest.main()
