"""J2C negative tests: frozen package IDs/versions, NuGet SHA512, physical CSM/PID."""
import base64
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core011_csm_g1j2c_roslyn as j

RUN = "CORE011-CSM-001/G1J2C-UNIT"
CHECKOUT = "a" * 40


def dump(p, value):
    p.write_text(json.dumps(value, indent=2) + "\n")


def synthetic_fixture(root):
    fixture = root / "fixture"
    fixture.mkdir()
    prov = {
        "schema": "chunkshift.core011.g1j2-provenance.v1",
        "runId": RUN, "origin": j.ORIGIN,
        "sourcePath": j.ASSEMBLY_PATH,
        "artificialByteEdits": False,
        "representativeDotNetBinaryPair": False,
        "packageDigestsPreFrozen": False,
        "verdict": "NO_VERDICT_EXPLORATORY",
    }
    manifests = {}
    for label, version in j.VERSIONS.items():
        content = (label * 5).encode()
        (fixture / (label + ".csm")).write_bytes(content)
        prov[label] = {
            "version":version,"packageId":"Microsoft.CodeAnalysis.CSharp",
            "assemblyPath":j.ASSEMBLY_PATH,
            "url":j.flat_urls(version)[0], "sidecarUrl":j.flat_urls(version)[1],
            "binaryFormat":"original-PE-DLL-from-official-nupkg",
            "packageHashSource":"computed-from-downloaded-package;not-authenticated",
            "digestFreezeStatus":"observed-in-first-nondecision-run;not-independent-freeze",
            "license":"MIT","packageBytes":200000,
            "bytes":120000,"packageSha256":"b"*64,
            "sha256": "c"*64,
            "packageSha512Base64":j.sha512_b64(b"sample fixture"),
        }
        manifests[label] = {
            "manifestFile":label+".csm", "csmBytes":len(content),
            "csmSha256":j.sha256(content), "sourceSha256":"c"*64,
            "sourceBytes":120000,"chunkCount":2,
            "cblkCount":1, "manifestId":"d"*64,"fileDigest":"e"*64,
        }
    dump(fixture / "provenance.json", prov)
    pair = {
        "schema":j.j2.SCHEMA,
        "runId":RUN, "origin":j.ORIGIN,
        "builderCheckoutSha":CHECKOUT, "verdict":"NO_VERDICT_EXPLORATORY",
        "consumerSourceAccessAllowed":False,"builderPid":53001,
        "provenanceSha256":j.sha256((fixture/"provenance.json").read_bytes()),
        "base":manifests["base"],"target":manifests["target"],
        "expectedPlan":{"reusedChunks":1,"reusedBytes":60000,
                        "missingChunks":1,"missingBytes":60000,
                        "uniqueMissingChunks":1,"uniqueMissingBytes":60000},
    }
    dump(fixture/"pair.json", pair)
    for idx, variant in enumerate(j.VARIANTS):
        lane = root / variant
        lane.mkdir()
        dump(lane/"build.json",{
            "schema":"chunkshift.core011.g1j1-build.v1",
            "variant":variant,"checkoutSha":CHECKOUT,
            "originalReaderSha256":j.j1.SOURCE_SHA,
            "sourceScope":"uncommitted temporary detached Git worktree",
            "sourceSha256":f"{idx+1:064x}",
        })
        dump(lane/"plan.json",{
            "schema":j.j2.SAMPLE_SCHEMA,
            "runId":RUN, "checkoutSha":CHECKOUT,
            "experimentId":"CORE011-CSM-001",
            "verdict":"NO_VERDICT_EXPLORATORY",
            "builderPid":53001, "consumerPid":53002+idx,
            "sourceFilesAccessible":False,
            "baseCsmSha256":manifests["base"]["csmSha256"],
            "targetCsmSha256":manifests["target"]["csmSha256"],
            "reusedChunks":1,"missingChunks":1,
            "samples":[{"round":i,"calls":4,"allocatedBytes":400,
                        "batchWallMilliseconds":1.2,
                        "batchCpuMilliseconds":0.3,
                        "gcPauseMilliseconds":0.0,"gen2":0} for i in (1,2)],
        })


class G1J2CUnitTests(unittest.TestCase):
    def test_predeclared_nuget_release_pair(self):
        self.assertEqual(j.VERSIONS,{"base":"4.13.0","target":"4.14.0"})
        self.assertEqual(j.ASSEMBLY_PATH,"lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll")
        with self.assertRaisesRegex(ValueError,"non-predeclared"):
            j.flat_urls("5.0.0")

    def test_sha512_base64_and_mismatches(self):
        actual = j.sha512_b64(b"actual nupkg")
        self.assertEqual(j.validate_sha512_text(actual),actual)
        with self.assertRaisesRegex(ValueError,"noncanonical"):
            j.validate_sha512_text(base64.b64encode(b"short").decode())
        with self.assertRaisesRegex(ValueError,"invalid"):
            j.validate_sha512_text("!@#$")

    def test_zip_requires_exact_managed_assembly_path(self):
        stream = io.BytesIO()
        with ZipFile(stream,"w") as z:
            z.writestr("lib/netstandard2.0/Microsoft.CodeAnalysis.CSharp.dll",b"MZ"+b"X"*65536)
        with self.assertRaisesRegex(ValueError,"canonical Roslyn"):
            j.extract_release(stream.getvalue())

    def test_zip_rejects_invalid_pe(self):
        stream = io.BytesIO()
        with ZipFile(stream,"w") as z:
            z.writestr(j.ASSEMBLY_PATH,b"MZ"+b"X"*65536)
        with self.assertRaisesRegex(ValueError,"PE/COFF"):
            j.extract_release(stream.getvalue())

    def test_valid_sourcefree_five_processes(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            synthetic_fixture(root)
            output=j.check(root,RUN,CHECKOUT)
            self.assertEqual(output["caseCount"],5)
            self.assertEqual(output["roundCount"],10)
            self.assertEqual(output["corpusQualification"],"MIXED_REUSE_PASS")
            self.assertEqual(output["largeCblkQualification"],"NO_GO_SINGLE_CBLK")
            self.assertEqual(output["verdict"],"NO_VERDICT_EXPLORATORY")

    def mutation(self,path,mutator,message):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            synthetic_fixture(root)
            p=root/path
            obj=json.loads(p.read_text())
            mutator(obj)
            dump(p,obj)
            with self.assertRaisesRegex(ValueError,message):
                j.check(root,RUN,CHECKOUT)

    def test_adopt_forbidden(self):
        self.mutation("lazy/plan.json",lambda d:d.update(verdict="ADOPT"),"oracle mismatch")

    def test_wrong_package_version_rejected(self):
        self.mutation("fixture/provenance.json",
                      lambda d:d["target"].update(version="5.0.0"),
                      "provenance or experiment decision guard")

    def test_wrong_observed_package_digest_rejected(self):
        self.mutation("fixture/provenance.json",
                      lambda d:d["base"].update(packageSha512Base64="x"),
                      "provenance or experiment decision guard")

    def test_raw_source_file_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            synthetic_fixture(root)
            (root/"fixture"/"base.input").write_bytes(b"source must be destroyed")
            with self.assertRaisesRegex(ValueError,"source-isolation"):
                j.check(root,RUN,CHECKOUT)

    def test_reused_consumer_pid_rejected(self):
        self.mutation("hybrid4/plan.json",
                      lambda d:d.update(consumerPid=53002),
                      "oracle mismatch")

    def test_corrupt_csm_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            synthetic_fixture(root)
            (root/"fixture"/"target.csm").write_bytes(b"mutated")
            with self.assertRaisesRegex(ValueError,"physical Roslyn CSM"):
                j.check(root,RUN,CHECKOUT)

    def test_false_multi_cblk_is_separate_from_mixed_reuse(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            synthetic_fixture(root)
            self.assertEqual(j.check(root,RUN,CHECKOUT)["largeCblkQualification"],
                             "NO_GO_SINGLE_CBLK")


if __name__ == "__main__":
    unittest.main()
