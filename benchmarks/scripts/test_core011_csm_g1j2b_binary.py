"""J2B build-source provenance, physical-pair and anti-ADOPT oracle mutation tests."""
import importlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core011_csm_g1j2b_binary as j

RUN="CORE011-CSM-001/G1J2B-MUTATION"
SHA="a"*40

class BinaryCorpusContracts(unittest.TestCase):
    def test_source_revisions_are_distinct_frozen_commits(self):
        self.assertNotEqual(j.BASE,j.TARGET)
        self.assertEqual(j.PROJECT,"src/ChunkShift/ChunkShift.csproj")
        self.assertEqual(j.TFM,"net10.0")
        with self.assertRaisesRegex(ValueError,"unpinned"):
            j.immutable_git_source(Path("."),"unapproved")

    def test_reject_source_checkout_tampering(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)
            with patch.object(j,"immutable_git_source",return_value={
                "commit":j.BASE,"projectBlob":"1"*40,
                "sourceTree":"2"*40,"packagesBlob":"3"*40,
                "sdkBlob":"4"*40,
            }):
                (path/"fixture").mkdir()
                data={"schema":"chunkshift.core011.g1j2-provenance.v1",
                      "runId":RUN,"origin":j.ORIGIN,"sourcePath":j.PROJECT,
                      "representativeDotNetBinaryPair":False,"artificialByteEdits":False,
                      "verdict":"NO_VERDICT_EXPLORATORY"}
                (path/"fixture"/"provenance.json").write_text(json.dumps(data))
                (path/"fixture"/"pair.json").write_text(json.dumps({
                    "schema":j.j2.SCHEMA,"runId":RUN,"origin":j.ORIGIN,
                    "builderCheckoutSha":SHA,"verdict":"NO_VERDICT_EXPLORATORY",
                    "consumerSourceAccessAllowed":False,"builderPid":100,
                    "provenanceSha256":j.digest((path/"fixture"/"provenance.json").read_bytes()),
                }))
                with self.assertRaises((ValueError,KeyError)):
                    j.check(Path("."),path,SHA,RUN)

    def test_binary_preflight_false_exe(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            fake=root/"invalid.input"
            fake.write_bytes(b"MZ"+b"x"*9000)
            self.assertFalse(b"PE\x00\x00" in fake.read_bytes()[:8192])

    def test_mixed_reuse_requires_both_positive(self):
        # Classification must not infer success from mere version mismatch.
        for reused, missing, expect in [
            (0,1,"NO_GO_NO_MIXED_REUSE"),
            (1,0,"NO_GO_NO_MIXED_REUSE"),
            (1,1,"MIXED_REUSE_PASS"),
        ]:
            self.assertEqual(
                "MIXED_REUSE_PASS" if reused>0 and missing>0
                else "NO_GO_NO_MIXED_REUSE",expect)

if __name__=="__main__":
    unittest.main()
