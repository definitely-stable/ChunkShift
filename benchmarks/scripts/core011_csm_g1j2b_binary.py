#!/usr/bin/env python3
"""G1-J2B: pinned *actual built .NET DLLs* -> real CSM pair -> isolated PlanAsync.

Non-decision correctness/corpus-suitability probe, NOT CPU qualification.
All builds and samples run on GitHub-hosted runners, never self-hosted.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

import core011_csm_g1j1_isolation as j1
import core011_csm_g1j2_plan as j2

BASE = "b712d305a52219fea5fe5ae56bb4a80fc9ca5cda"
TARGET = "d4509dcc18ae76d9bd3bfae73d27fdc61423a4f8"
PROJECT = "src/ChunkShift/ChunkShift.csproj"
TFM = "net10.0"
ORIGIN = "git-built-dotnet-binary"
SCHEMA = "chunkshift.core011.g1j2b-dotnet-pair-summary.v1"
RUN_PREFIX = "CORE011-CSM-001/G1J2B-"
VARIANTS = j1.VARIANTS


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def git(repo: Path, *args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=repo, text=True).strip()


def immutable_git_source(repo: Path, commit: str) -> dict:
    if commit not in (BASE, TARGET):
        raise ValueError("unpinned .NET revision")
    # Tree of actual managed source, and package+SDK configuration fingerprints.
    return {
        "commit": commit,
        "projectBlob": git(repo, "rev-parse", f"{commit}:{PROJECT}"),
        "sourceTree": git(repo, "rev-parse", f"{commit}:src/ChunkShift"),
        "packagesBlob": git(repo, "rev-parse", f"{commit}:Directory.Packages.props"),
        "sdkBlob": git(repo, "rev-parse", f"{commit}:global.json"),
    }


def build_dll(repo: Path, worktree_root: Path, out: Path, label: str, commit: str):
    metadata = immutable_git_source(repo, commit)
    wt = worktree_root / ("git-" + label)
    if wt.exists():
        raise ValueError("refuse reused binary source worktree")
    j1.execute(["git", "worktree", "add", "--detach", str(wt), commit], repo, 60)
    if git(wt, "rev-parse", "HEAD") != commit:
        raise ValueError("wrong build checkout for " + label)
    info = subprocess.check_output(["dotnet", "--info"], cwd=wt, text=True)
    # Fix the CI property explicitly; project defaults may add source-link info,
    # but each assembly is a genuine signed-by-Git-revision compilation.
    j1.execute(["dotnet", "build", PROJECT, "-c", "Release", "-f", TFM,
                "-p:ContinuousIntegrationBuild=true", "--no-incremental"], wt, 1200)
    assembly = wt / "src/ChunkShift/bin/Release/net10.0/ChunkShift.dll"
    if not assembly.is_file() or assembly.stat().st_size < 4096:
        raise ValueError("missing actual compiled .NET PE DLL")
    data = assembly.read_bytes()
    # Only a cheap preflight; C# builder verifies CLR header, metadata and MVID.
    if data[:2] != b"MZ" or b"PE\x00\x00" not in data[:8192]:
        raise ValueError("compiled file lacks PE header")
    out.write_bytes(data)
    return {
        **metadata,
        "assemblyPath": "ChunkShift.dll",
        "sha256": digest(data),
        "bytes": len(data),
        "targetFramework": TFM,
        "buildCommand": ["dotnet", "build", PROJECT, "-c", "Release",
                         "-f", TFM, "-p:ContinuousIntegrationBuild=true",
                         "--no-incremental"],
        "dotnetInfoSha256": digest(info.encode()),
        "binaryFormat": "managed-PE-verified-by-builder",
    }


def prepare(repo: Path, output: Path, worktree_root: Path, run_id: str) -> dict:
    if output.exists() or worktree_root.exists():
        raise ValueError("refuse existing binary-pair output and worktrees")
    output.mkdir(parents=True)
    worktree_root.mkdir(parents=True)
    fixture = output / "fixture"
    fixture.mkdir()
    records = {}
    for label, sha in (("base", BASE), ("target", TARGET)):
        records[label] = build_dll(repo, worktree_root, fixture / (label + ".input"), label, sha)
    if records["base"]["sha256"] == records["target"]["sha256"]:
        raise ValueError("same compiled assembly, version pair invalid")
    source = {
        "schema": "chunkshift.core011.g1j2-provenance.v1",
        "runId": run_id, "origin": ORIGIN, "sourcePath": PROJECT,
        "repository": "https://github.com/definitely-stable/ChunkShift",
        "base": records["base"], "target": records["target"],
        "corpusClassification": "genuine-git-built-CLR-net10-DLL; pilot-only",
        "representativeDotNetBinaryPair": False,
        "artificialByteEdits": False, "verdict": "NO_VERDICT_EXPLORATORY",
    }
    (fixture / "provenance.json").write_text(json.dumps(source, indent=2) + "\n")
    return source


def check(repo: Path, output: Path, checkout: str, run_id: str) -> dict:
    root = output / "fixture"
    provenance = json.loads((root / "provenance.json").read_text())
    pair = json.loads((root / "pair.json").read_text())
    if (provenance.get("schema") != "chunkshift.core011.g1j2-provenance.v1" or
        provenance.get("runId") != run_id or
        provenance.get("origin") != ORIGIN or
        provenance.get("sourcePath") != PROJECT or
        provenance.get("artificialByteEdits") is not False or
        provenance.get("representativeDotNetBinaryPair") is not False or
        provenance.get("verdict") != "NO_VERDICT_EXPLORATORY" or
        pair.get("schema") != j2.SCHEMA or
        pair.get("runId") != run_id or
        pair.get("origin") != ORIGIN or
        pair.get("builderCheckoutSha") != checkout or
        pair.get("verdict") != "NO_VERDICT_EXPLORATORY" or
        pair.get("consumerSourceAccessAllowed") is not False or
        pair.get("provenanceSha256") != digest((root / "provenance.json").read_bytes())):
        raise ValueError("binary source/protocol identity mismatch")
    builder = pair.get("builderPid")
    if type(builder) is not int or builder <= 0:
        raise ValueError("invalid binary builder process identity")
    csm_shas = []
    for label, commit in (("base", BASE), ("target", TARGET)):
        source = provenance[label]
        rec = pair[label]
        frozen = immutable_git_source(repo, commit)
        if any(source.get(k) != v for k,v in frozen.items()):
            raise ValueError("binary source revision/tree fingerprint drift")
        if (source.get("binaryFormat") != "managed-PE-verified-by-builder" or
            source.get("targetFramework") != TFM or
            type(source.get("bytes")) is not int or source["bytes"] < 4096 or
            rec.get("sourceSha256") != source.get("sha256") or
            rec.get("sourceBytes") != source.get("bytes")):
            raise ValueError("Git .NET assembly provenance mismatch")
        if (root / (label + ".input")).exists():
            raise ValueError("raw .NET PE DLL leaked into CSM consumer fixture")
        file = root / (label + ".csm")
        if not file.is_file() or file.is_symlink() or (
            rec.get("manifestFile") != label + ".csm" or
            rec.get("csmBytes") != file.stat().st_size or
            rec.get("csmSha256") != digest(file.read_bytes()) or
            not isinstance(rec.get("chunkCount"), int) or rec["chunkCount"] < 1):
            raise ValueError("actual built .NET physical CSM mismatch")
        for k in ("manifestId", "fileDigest", "sourceSha256", "csmSha256"):
            x = rec.get(k)
            if type(x) is not str or len(x) != 64 or any(ch not in "0123456789abcdef" for ch in x):
                raise ValueError("not canonical 256-bit .NET digest: " + k)
        csm_shas.append(rec["csmSha256"])
    if csm_shas[0] == csm_shas[1]:
        raise ValueError("same physical binary CSM across real revisions")
    expected = pair.get("expectedPlan", {})
    b, t = pair["base"], pair["target"]
    for k in ("reusedChunks", "missingChunks", "reusedBytes", "missingBytes",
              "uniqueMissingChunks", "uniqueMissingBytes"):
        if type(expected.get(k)) is not int or expected[k] < 0:
            raise ValueError("invalid built .NET pair Plan counters")
    if (expected["reusedChunks"] + expected["missingChunks"] != t["chunkCount"] or
        expected["reusedBytes"] + expected["missingBytes"] != t["sourceBytes"]):
        raise ValueError("compiled .NET version pair lost Plan conservation")
    pid_set = set()
    sources = set()
    results = []
    for variant in VARIANTS:
        build = json.loads((output / variant / "build.json").read_text())
        raw = (output / variant / "plan.json").read_bytes()
        obs = json.loads(raw)
        pid = obs.get("consumerPid")
        sha = build.get("sourceSha256")
        if (build.get("schema") != "chunkshift.core011.g1j1-build.v1" or
            build.get("variant") != variant or
            build.get("checkoutSha") != checkout or
            build.get("originalReaderSha256") != j1.SOURCE_SHA or
            build.get("sourceScope") != "uncommitted temporary detached Git worktree" or
            type(sha) is not str or len(sha) != 64 or sha in sources or
            obs.get("schema") != j2.SAMPLE_SCHEMA or
            obs.get("experimentId") != "CORE011-CSM-001" or
            obs.get("runId") != run_id or
            obs.get("verdict") != "NO_VERDICT_EXPLORATORY" or
            obs.get("checkoutSha") != checkout or
            obs.get("sourceFilesAccessible") is not False or
            obs.get("builderPid") != builder or
            type(pid) is not int or pid <= 0 or pid == builder or pid in pid_set or
            obs.get("baseCsmSha256") != csm_shas[0] or
            obs.get("targetCsmSha256") != csm_shas[1] or
            obs.get("reusedChunks") != expected["reusedChunks"] or
            obs.get("missingChunks") != expected["missingChunks"] or
            type(obs.get("samples")) is not list or len(obs["samples"]) != 2):
            raise ValueError("binary consumer/source/oracle mismatch")
        for i,s in enumerate(obs["samples"],1):
            if (s.get("round") != i or s.get("calls") != 4 or
                type(s.get("allocatedBytes")) is not int or s["allocatedBytes"] < 0 or
                type(s.get("batchWallMilliseconds")) not in (int,float) or
                not 0 < s["batchWallMilliseconds"] < 60000):
                raise ValueError("missing/invalid binary Plan pilot rounds")
        pid_set.add(pid)
        sources.add(sha)
        results.append({"variant":variant, "consumerPid":pid,
                        "readerSourceSha256":sha, "rawSha256":digest(raw)})
    if len(results) != 5:
        raise ValueError("incomplete five-variant binary suite")
    reuse=expected["reusedChunks"]; missing=expected["missingChunks"]
    return {
        "schema": SCHEMA, "runId": run_id, "checkoutSha": checkout,
        "builderPid": builder, "consumerResults": results,
        "corpus": {"baseSourceCommit": BASE, "targetSourceCommit": TARGET,
                   "baseSourceSha256": provenance["base"]["sha256"],
                   "targetSourceSha256": provenance["target"]["sha256"],
                   "baseCsmSha256": csm_shas[0], "targetCsmSha256": csm_shas[1],
                   "reusedChunks":reuse, "missingChunks":missing,
                   "targetChunks":t["chunkCount"]},
        "corpusQualification": "MIXED_REUSE_PASS" if reuse > 0 and missing > 0
                               else "NO_GO_NO_MIXED_REUSE",
        "caseCount":5, "roundCount":10,
        "verdict":"NO_VERDICT_EXPLORATORY",
        "note":"Real compiled DLL only; no CPU calibration, no validated reusable mixed binary corpus on NO_GO."
    }


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--output-dir",type=Path,required=True)
    p.add_argument("--worktree-root",type=Path,required=True)
    p.add_argument("--run-id",required=True)
    p.add_argument("--evaluate-only",action="store_true")
    a=p.parse_args()
    if not a.run_id.startswith(RUN_PREFIX):
        p.error("invalid G1-J2B run identity")
    repo=Path.cwd()
    checkout=git(repo,"rev-parse","HEAD")
    if not a.evaluate_only:
        if os.environ.get("GITHUB_ACTIONS")!="true" or os.environ.get("GITHUB_SHA")!=checkout:
            raise ValueError("resourceful J2B only allowed in exact GitHub-hosted checkout")
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
    summary=check(repo,a.output_dir,checkout,a.run_id)
    (a.output_dir/"summary.json").write_text(json.dumps(summary,indent=2)+"\n")
    print("J2B real managed DLL pair verified; "+summary["corpusQualification"]+
          "; NO_VERDICT_EXPLORATORY")


if __name__=="__main__":
    main()
