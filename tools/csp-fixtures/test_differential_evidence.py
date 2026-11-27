"""Exercise comparator failure paths, not just the serialization helper."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import decode
import forward_apply
from differential_evidence import FailureBundles, MAX_BUNDLES


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def listing(self, name, rows):
        path = self.root / name
        path.write_text("\n".join(json.dumps(row) for row in rows) + "\n", encoding="utf-8")
        return path

    def binary(self, name, data):
        (self.root / name).write_bytes(data)

    def verify(self, kind, expected, actual, inputs, seed=None, slot="01"):
        bundle = self.root / "failures" / ("python-" + kind) / slot
        meta = json.loads((bundle / "failure.json").read_text(encoding="utf-8"))
        self.assertEqual("csp-differential-reproducer-v1", meta["schema"])
        self.assertEqual(kind, meta["comparator"])
        self.assertEqual(expected, meta["expected"])
        self.assertEqual(actual, meta["actual"])
        self.assertEqual(seed, meta["forwardSeed"])
        self.assertEqual(set(inputs), set(meta["inputs"]))
        for name, value in inputs.items():
            if value is None:
                self.assertIsNone(meta["inputs"][name])
                self.assertFalse((bundle / name).exists())
            else:
                self.assertEqual(value, (bundle / name).read_bytes())
                self.assertEqual(dict(bytes=len(value), sha256=hashlib.sha256(value).hexdigest()),
                                 meta["inputs"][name])
        return meta

    def patch_case(self):
        self.binary("case-abc-7.csp", b"patch")
        self.binary("base.csm", b"manifest")
        self.binary("base.bin", b"")
        row = dict(file="case-abc-7.csp", baseManifest="base.csm", base="base.bin", verdict="valid:abc")
        self.listing("verdicts-abc.jsonl", [row])
        return row

    def test_decode_mismatch_retains_exact_inputs(self):
        row = self.patch_case()
        with patch.object(decode, "decode", return_value=dict(verdict="malformed", reason="injected")):
            self.assertEqual(1, decode.compare(self.root))
        meta = self.verify("decode", "valid:abc", "malformed",
                           {"case.csp": b"patch", "base.csm": b"manifest", "base.bin": b""})
        self.assertEqual(row, meta["case"])
        self.assertEqual("verdicts-abc.jsonl", meta["listing"])
        self.assertEqual(1, meta["line"])

    def test_forward_mismatch_records_actual_seed(self):
        self.patch_case()
        with patch.object(forward_apply.dec, "decode", return_value=dict(verdict="malformed")), \
                patch.object(forward_apply, "apply", return_value=dict(verdict="unsupported")) as apply:
            self.assertEqual(1, forward_apply.compare(self.root))
            self.assertEqual(0, apply.call_args.kwargs["seed"])
        self.verify("forward", "malformed", "unsupported",
                    {"case.csp": b"patch", "base.csm": b"manifest", "base.bin": b""}, seed=0)

    def test_frame_mismatch_retains_dictionary_and_length(self):
        self.binary("frame-abc-3.zst", b"frame")
        self.binary("dict.bin", b"dictionary")
        row = dict(file="frame-abc-3.zst", dictionary="dict.bin", length=123, verdict="ok:abc")
        self.listing("frames-abc.jsonl", [row])
        # Real malformed frame: exercise the actual envelope parser and
        # its Malformed(rule, reason) contract, not an exception-shaped mock.
        self.assertEqual(1, decode.compare_frames(self.root))
        meta = self.verify("frames", "ok:abc", "malformed",
                           {"frame.zst": b"frame", "dictionary.bin": b"dictionary"})
        self.assertEqual(123, meta["case"]["length"])
        self.assertIn("do not start with a zstd frame", meta["reason"])
        self.assertEqual(row, meta["case"])

    def test_real_fixture_wrong_verdict_retains_replayable_patch(self):
        fixture = (Path(__file__).resolve().parents[2] /
                   "tests/ChunkShift.Patching.Tests/Fixtures/CspV1/zstd-no-dictionary.csp")
        data = fixture.read_bytes()
        expected = "valid:967dcabb78ed6db5d8911bb78c322b35be9293c96d5d5981fa540c4adde19b0c"
        self.binary("case-abc-8.csp", data)
        row = dict(file="case-abc-8.csp", baseManifest=None, base=None, verdict="malformed")
        self.listing("verdicts-abc.jsonl", [row])
        self.assertEqual(1, decode.compare(self.root))
        self.verify("decode", "malformed", expected,
                    {"case.csp": data, "base.csm": None, "base.bin": None})
        stored = self.root / "failures/python-decode/01/case.csp"
        self.assertEqual(expected, decode.verdict_text(decode.decode(stored.read_bytes())))
        # The same real input agrees with the forward-only prototype.
        self.assertEqual(0, forward_apply.compare(self.root))
        self.assertFalse((self.root / "failures/python-forward").exists())

    def test_real_frame_correct_verdict_has_no_artifact(self):
        # A real valid frame emitted by the hosted Python libzstd encoder.
        data = b"chunkshift frame evidence" * 20
        stored = decode.zstd.compress(data)
        expected = "ok:" + hashlib.sha256(data).hexdigest()
        self.binary("frame-abc-4.zst", stored)
        self.listing("frames-abc.jsonl", [
            dict(file="frame-abc-4.zst", dictionary=None, length=len(data), verdict=expected)])
        self.assertEqual(0, decode.compare_frames(self.root))
        self.assertFalse((self.root / "failures").exists())

    def test_unexpected_exception_retains_and_reraises(self):
        self.patch_case()
        error = RuntimeError("injected exception")
        with patch.object(decode, "decode", side_effect=error):
            with self.assertRaises(RuntimeError) as observed:
                decode.compare(self.root)
        self.assertIs(error, observed.exception)
        meta = self.verify("decode", "valid:abc", "exception:RuntimeError",
                           {"case.csp": b"patch", "base.csm": b"manifest", "base.bin": b""})
        self.assertIn("injected exception", meta["reason"])

    def test_matching_verdict_writes_nothing(self):
        self.patch_case()
        with patch.object(decode, "decode", return_value=dict(verdict="valid", outputSha256="abc")):
            self.assertEqual(0, decode.compare(self.root))
        self.assertFalse((self.root / "failures").exists())

    def test_cap_across_invocations_and_absent_inputs(self):
        row = dict(file="ignored.csp", verdict="malformed")
        listing = self.root / "verdicts-1.jsonl"
        for index in range(MAX_BUNDLES + 1):
            bundles = FailureBundles(self.root, "decode")
            self.assertEqual(index < MAX_BUNDLES,
                             bundles.record(listing, 1, row,
                                            {"case.csp": bytes([index]), "base.csm": None, "base.bin": None},
                                            "malformed", "unsupported"))
        self.verify("decode", "malformed", "unsupported",
                    {"case.csp": b"\x00", "base.csm": None, "base.bin": None})
        self.assertEqual(MAX_BUNDLES, len(list((self.root / "failures/python-decode").iterdir())))

    def test_retention_limit_never_makes_mismatch_pass(self):
        self.patch_case()
        with patch.object(decode, "decode", return_value=dict(verdict="malformed")), \
                patch("differential_evidence.MAX_INPUT_BYTES", 1):
            self.assertEqual(1, decode.compare(self.root))
        self.assertEqual([], list((self.root / "failures/python-decode").iterdir()))

    def test_artifact_io_error_never_makes_mismatch_pass(self):
        self.patch_case()
        (self.root / "failures").write_bytes(b"occupied")
        with patch.object(decode, "decode", return_value=dict(verdict="malformed")):
            self.assertEqual(1, decode.compare(self.root))
        self.assertEqual(b"occupied", (self.root / "failures").read_bytes())


if __name__ == "__main__":
    unittest.main()
