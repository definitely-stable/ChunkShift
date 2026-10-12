#!/usr/bin/env python3
"""G1-J2C: two predeclared official Roslyn NuGet releases, never a decision run.

Downloads only immutable NuGet FlatContainer versions; snapshot SHA-512 is verified
against package bytes and recorded as observational evidence. Package hashes are
NOT independently preregistered/frozen yet: J2C only qualifies corpus feasibility.
"""
import argparse
import base64
import hashlib
import io
import json
import math
import os
from pathlib import Path
import subprocess
import urllib.request
import zipfile

import core011_csm_g1j1_isolation as j1
import core011_csm_g1j2_plan as j2

PACKAGE = "microsoft.codeanalysis.csharp"
VERSIONS = {"base": "4.13.0", "target": "4.14.0"}
ASSEMBLY_PATH = "lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll"
ORIGIN = "nuget-released-managed-pe"
RUN_PREFIX = "CORE011-CSM-001/G1J2C-"
SCHEMA = "chunkshift.core011.g1j2c-nuget-corpus.v1"
FLAT = "https://api.nuget.org/v3-flatcontainer"
MAX_PACKAGE = 32 * 1024 * 1024
MAX_DLL = 20 * 1024 * 1024
VARIANTS = j1.VARIANTS


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def sha512_b64(data):
    return base64.b64encode(hashlib.sha512(data).digest()).decode("ascii")


def validate_sha512_text(value):
    if not isinstance(value, str):
        raise ValueError("missing NuGet SHA-512")
    value = value.strip()
    try:
        digest = base64.b64decode(value, validate=True)
    except Exception as e:
        raise ValueError("invalid NuGet SHA-512 base64") from e
    if len(digest) != 64 or base64.b64encode(digest).decode("ascii") != value:
        raise ValueError("noncanonical NuGet SHA-512")
    return value


def flat_urls(version):
    if version not in VERSIONS.values():
        raise ValueError("non-predeclared NuGet package version")
    prefix = f"{FLAT}/{PACKAGE}/{version}/{PACKAGE}.{version}.nupkg"
    return prefix, prefix + ".sha512"


def get_bytes(url, maximum):
    with urllib.request.urlopen(url, timeout=120) as response:
        if response.status != 200 or response.geturl().split("?")[0] != url:
            raise ValueError("NuGet canonical URL or HTTP status mismatch")
        data = response.read(maximum + 1)
    if not 0 < len(data) <= maximum:
        raise ValueError("NuGet source exceeded frozen size bound")
    return data


def extract_release(package_bytes):
    with zipfile.ZipFile(io.BytesIO(package_bytes), "r") as z:
        if z.testzip() is not None:
            raise ValueError("NuGet package CRC failed")
        files = z.infolist()
        matches = [m for m in files if m.filename == ASSEMBLY_PATH]
        if len(matches) != 1 or matches[0].is_dir() or matches[0].file_size < 65536:
            raise ValueError("Missing or implausible canonical Roslyn lib/net8.0 DLL")
        info = matches[0]
        if info.file_size > MAX_DLL or info.compress_size > MAX_PACKAGE:
            raise ValueError("Oversized Roslyn DLL in NuGet")
        raw = z.read(info)
    if len(raw) != info.file_size or raw[:2] != b"MZ":
        raise ValueError("Not actual PE assembly")
    off = int.from_bytes(raw[0x3c:0x40], "little")
    if off <= 0 or off > min(len(raw) - 4, 65536) or raw[off:off + 4] != b"PE\x00\x00":
        raise ValueError("Not actual PE/COFF assembly")
    return raw


def download_version(version):
    url, digest_url = flat_urls(version)
    # NuGet itself publishes this digest. We record it and verify against the
    # package; do not mislabel it an independently pre-frozen hash or signature.
    expected = validate_sha512_text(get_bytes(digest_url, 4096).decode("ascii"))
    raw = get_bytes(url, MAX_PACKAGE)
    if sha512_b64(raw) != expected:
        raise ValueError("NuGet package SHA-512 mismatches official FlatContainer sidecar")
    assembly = extract_release(raw)
    return assembly, {
        "version": version, "packageId": "Microsoft.CodeAnalysis.CSharp",
        "url": url, "sidecarUrl": digest_url,
        "packageBytes": len(raw),
        "packageSha256": sha256(raw),
        "packageSha512Base64": expected,
        "officialSidecarSha256": sha256((expected + "\n").encode()),
        "sha256": sha256(assembly), "bytes": len(assembly),
        "assemblyPath": ASSEMBLY_PATH,
        "binaryFormat": "original-PE-DLL-from-official-nupkg",
        "license": "MIT",
        "digestFreezeStatus": "observed-in-first-nondecision-run;not-independent-freeze",
    }


def prepare(repo, output, worktrees, run_id):
    if output.exists() or worktrees.exists():
        raise ValueError("J2C refuses reused output/worktrees")
    output.mkdir(parents=True)
    worktrees.mkdir(parents=True)
    fixture = output / "fixture"
    fixture.mkdir()
    records = {}
    for label, version in VERSIONS.items():
        assembly, record = download_version(version)
        (fixture / (label + ".input")).write_bytes(assembly)
        records[label] = record
    if records["base"]["sha256"] == records["target"]["sha256"]:
        raise ValueError("released binaries are identical")
    provenance = {
        "schema": "chunkshift.core011.g1j2-provenance.v1",
        "runId": run_id, "origin": ORIGIN,
        "sourcePath": ASSEMBLY_PATH,
        "repository": "https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/",
        "base": records["base"], "target": records["target"],
        "corpusClassification": "official-release-managed-DLL-version-pair;first-provenance-smoke",
        "representativeDotNetBinaryPair": False,
        "artificialByteEdits": False,
        "packageDigestsPreFrozen": False,
        "verdict": "NO_VERDICT_EXPLORATORY",
    }
    (fixture / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    return provenance


def check(output, run_id, checkout):
    fixture = output / "fixture"
    provenance_path = fixture / "provenance.json"
    pair_path = fixture / "pair.json"
    provenance = json.loads(provenance_path.read_text())
    pair = json.loads(pair_path.read_text())
    if (provenance.get("schema") != "chunkshift.core011.g1j2-provenance.v1"
        or provenance.get("origin") != ORIGIN
        or provenance.get("sourcePath") != ASSEMBLY_PATH
        or provenance.get("runId") != run_id
        or provenance.get("representativeDotNetBinaryPair") is not False
        or provenance.get("artificialByteEdits") is not False
        or provenance.get("packageDigestsPreFrozen") is not False
        or provenance.get("verdict") != "NO_VERDICT_EXPLORATORY"
        or pair.get("schema") != j2.SCHEMA
        or pair.get("runId") != run_id
        or pair.get("origin") != ORIGIN
        or pair.get("builderCheckoutSha") != checkout
        or pair.get("verdict") != "NO_VERDICT_EXPLORATORY"
        or pair.get("consumerSourceAccessAllowed") is not False
        or pair.get("provenanceSha256") != sha256(provenance_path.read_bytes())):
        raise ValueError("NuGet provenance or experiment decision guard mismatch")
    builder = pair.get("builderPid")
    if type(builder) is not int or builder <= 0:
        raise ValueError("invalid Roslyn fixture builder PID")
    csms = []
    for label, version in VERSIONS.items():
        record = provenance[label]
        identity = pair[label]
        original_url, sidecar = flat_urls(version)
        if (record.get("version") != version
            or record.get("packageId") != "Microsoft.CodeAnalysis.CSharp"
            or record.get("assemblyPath") != ASSEMBLY_PATH
            or record.get("url") != original_url
            or record.get("sidecarUrl") != sidecar
            or record.get("binaryFormat") != "original-PE-DLL-from-official-nupkg"
            or record.get("digestFreezeStatus") != "observed-in-first-nondecision-run;not-independent-freeze"
            or record.get("license") != "MIT"
            or type(record.get("packageBytes")) is not int
            or not 1024 <= record["packageBytes"] <= MAX_PACKAGE
            or type(record.get("bytes")) is not int
            or not 65536 <= record["bytes"] <= MAX_DLL
            or identity.get("sourceSha256") != record.get("sha256")
            or identity.get("sourceBytes") != record.get("bytes")
            or (fixture / (label + ".input")).exists()):
            raise ValueError("Roslyn NuGet package/source identity or source-isolation mismatch")
        validate_sha512_text(record.get("packageSha512Base64"))
        for key in ("sha256", "packageSha256"):
            value = record.get(key)
            if not isinstance(value, str) or len(value) != 64 or any(c not in "0123456789abcdef" for c in value):
                raise ValueError("noncanonical NuGet source hash: " + key)
        csm_path = fixture / (label + ".csm")
        if (not csm_path.is_file() or csm_path.is_symlink()
            or identity.get("manifestFile") != label + ".csm"
            or identity.get("csmBytes") != csm_path.stat().st_size
            or identity.get("csmSha256") != sha256(csm_path.read_bytes())
            or type(identity.get("chunkCount")) is not int
            or identity["chunkCount"] < 1):
            raise ValueError("physical Roslyn CSM digest mismatch")
        for field in ("manifestId", "fileDigest", "csmSha256"):
            v = identity.get(field)
            if not isinstance(v, str) or len(v) != 64 or any(c not in "0123456789abcdef" for c in v):
                raise ValueError("noncanonical physical CSM identity " + field)
        csms.append(identity["csmSha256"])
    if csms[0] == csms[1]:
        raise ValueError("versions have identical physical CSM")
    plan = pair.get("expectedPlan", {})
    target = pair["target"]
    for k in ("reusedChunks", "reusedBytes", "missingChunks", "missingBytes",
              "uniqueMissingChunks", "uniqueMissingBytes"):
        if type(plan.get(k)) is not int or plan[k] < 0:
            raise ValueError("invalid Roslyn Plan counter")
    if (plan["reusedChunks"] + plan["missingChunks"] != target["chunkCount"]
        or plan["reusedBytes"] + plan["missingBytes"] != target["sourceBytes"]):
        raise ValueError("Roslyn version pair target conservation mismatch")
    pids = set()
    sources = set()
    lanes = []
    for variant in VARIANTS:
        build = json.loads((output / variant / "build.json").read_text())
        consumer_path = output / variant / "plan.json"
        consumer_bytes = consumer_path.read_bytes()
        result = json.loads(consumer_bytes)
        pid = result.get("consumerPid")
        source = build.get("sourceSha256")
        if (build.get("schema") != "chunkshift.core011.g1j1-build.v1"
            or build.get("variant") != variant
            or build.get("checkoutSha") != checkout
            or build.get("originalReaderSha256") != j1.SOURCE_SHA
            or build.get("sourceScope") != "uncommitted temporary detached Git worktree"
            or type(source) is not str or len(source) != 64 or source in sources
            or result.get("schema") != j2.SAMPLE_SCHEMA
            or result.get("runId") != run_id
            or result.get("checkoutSha") != checkout
            or result.get("experimentId") != "CORE011-CSM-001"
            or result.get("verdict") != "NO_VERDICT_EXPLORATORY"
            or result.get("builderPid") != builder
            or type(pid) is not int or pid <= 0 or pid in pids or pid == builder
            or result.get("sourceFilesAccessible") is not False
            or result.get("baseCsmSha256") != csms[0]
            or result.get("targetCsmSha256") != csms[1]
            or result.get("reusedChunks") != plan["reusedChunks"]
            or result.get("missingChunks") != plan["missingChunks"]):
            raise ValueError("Roslyn consumer process/source/Plan oracle mismatch")
        samples = result.get("samples")
        if not isinstance(samples, list) or len(samples) != 2:
            raise ValueError("missing Roslyn diagnostic rounds")
        for number, s in enumerate(samples, 1):
            if (s.get("round") != number or s.get("calls") != 4
                or any(type(s.get(k)) not in (int, float) or not math.isfinite(s[k])
                       or s[k] < 0 for k in ("batchWallMilliseconds", "batchCpuMilliseconds",
                                             "allocatedBytes", "gcPauseMilliseconds", "gen2"))
                or s["batchWallMilliseconds"] <= 0):
                raise ValueError("invalid Roslyn diagnostic round")
        pids.add(pid)
        sources.add(source)
        lanes.append({"variant":variant,"consumerPid":pid,
                      "readerSourceSha256":source,"rawSha256":sha256(consumer_bytes)})
    return {
        "schema":SCHEMA, "runId":run_id, "checkoutSha":checkout,
        "builderPid":builder, "consumers":lanes,
        "corpus":{
            "baseVersion":VERSIONS["base"],"targetVersion":VERSIONS["target"],
            "basePackageSha512":provenance["base"]["packageSha512Base64"],
            "targetPackageSha512":provenance["target"]["packageSha512Base64"],
            "baseCsmSha256":csms[0],"targetCsmSha256":csms[1],
            "baseChunks":pair["base"]["chunkCount"],
            "targetChunks":pair["target"]["chunkCount"],
            "baseCblk":pair["base"]["cblkCount"],
            "targetCblk":pair["target"]["cblkCount"],
            "reusedChunks":plan["reusedChunks"],
            "missingChunks":plan["missingChunks"],
        },
        "corpusQualification":(
            "MIXED_REUSE_PASS" if plan["reusedChunks"] > 0 and plan["missingChunks"] > 0
            else "NO_GO_NO_MIXED_REUSE"),
        "largeCblkQualification":(
            "MULTICBLK_PASS" if pair["base"]["cblkCount"] >= 2 and pair["target"]["cblkCount"] >= 2
            else "NO_GO_SINGLE_CBLK"),
        "caseCount":5, "roundCount":10,
        "verdict":"NO_VERDICT_EXPLORATORY",
        "limitations":"Package SHA512 first observed on runner, not pre-frozen; no independent signer verification, multi-host CPU calibration or production ADOPT.",
    }


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--output-dir",type=Path,required=True)
    p.add_argument("--worktree-root",type=Path,required=True)
    p.add_argument("--run-id",required=True)
    p.add_argument("--evaluate-only",action="store_true")
    a=p.parse_args()
    if not a.run_id.startswith(RUN_PREFIX):
        p.error("wrong J2C RunId")
    repo=Path.cwd()
    checkout=subprocess.check_output(["git","rev-parse","HEAD"],cwd=repo,text=True).strip()
    if not a.evaluate_only:
        if os.environ.get("GITHUB_ACTIONS")!="true" or os.environ.get("GITHUB_SHA")!=checkout:
            raise ValueError("J2C requires GitHub-hosted, pinned checkout")
        try:
            prepare(repo,a.output_dir,a.worktree_root,a.run_id)
            variants=j1.build_variants(repo,a.worktree_root,a.output_dir,checkout)
            fixture=a.output_dir/"fixture"
            j1.execute(j1.command(variants["eager"],"core011-g1j2-plan-build",
                                  "--fixture-dir",fixture,"--run-id",a.run_id),
                       variants["eager"],timeout=600)
            for variant,wt in variants.items():
                j1.execute(j1.command(wt,"core011-g1j2-plan-consume",
                                      "--fixture-dir",fixture,"--run-id",a.run_id,
                                      "--output",a.output_dir/variant/"plan.json"),
                           wt,timeout=180)
        finally:
            if (a.output_dir/"fixture").is_dir():
                for name in ("base.input","target.input"):
                    (a.output_dir/"fixture"/name).unlink(missing_ok=True)
    summary=check(a.output_dir,a.run_id,checkout)
    (a.output_dir/"summary.json").write_text(json.dumps(summary,indent=2)+"\n")
    print("G1-J2C pinned NuGet version pair correctness PASS; corpus="+
          summary["corpusQualification"]+
          " largeCBLK="+summary["largeCblkQualification"]+
          " / product NO_VERDICT")


if __name__=="__main__":
    main()
