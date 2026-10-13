#!/usr/bin/env python3
"""CORE011-J2H1: independently frozen Newtonsoft managed DLL holdout.

Public stable64k CSP create/apply and a separate five-profile *lab* scanner.
No production change, no unregistered public CSM, no tuning or ADOPT.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

import core011_csm_g1j2h0_holdout_freeze as h0
import core011_csm_g1j2g_patch_cost as j2g
import core011_csm_g1j2f_profile_sensitivity as j2f

SCHEMA = "chunkshift.core011.g1j2h1-frozen-holdout.v1"
PREFIX = "CORE011-CSM-001/G1J2H1-"
CONTRACT = Path("benchmarks/experiments/core011-csm-g1j2h1-frozen-corpus.v1.json")
FROZEN_H0_RUN = "CORE011-CSM-001/G1J2H0-38386615762-1-ddc77583-linux-x64"
EXPECTED_ASSET = "lib/net6.0/Newtonsoft.Json.dll"
LANES = ("BASE_AWARE_STABLE64K", "SELF_CONTAINED_STABLE64K")
PROFILE_LANES = ("stable-fastcdc64k", "lab-fastcdc16k",
                 "lab-fastcdc256k", "lab-fixed16k", "lab-fixed64k")


def require(value, message):
    if not value:
        raise ValueError(message)


def validate_contract(contract):
    require(contract.get("schema") ==
            "chunkshift.core011.g1j2h1-frozen-corpus.v1" and
            contract.get("stage") == "FROZEN_BEFORE_H1_OUTCOME" and
            contract.get("h0OriginalRunId") == FROZEN_H0_RUN and
            contract.get("asset") == EXPECTED_ASSET and
            contract.get("independentProductFamily") == h0.PACKAGE_ID and
            contract.get("h0GitHubRun") == 38386615762 and
            contract.get("h0ActionsArtifact") == 11806581918 and
            contract.get("h0OriginalZipSha256") ==
            "902be58596334bbb30f912d7ba775c563d30c181ba289b4b326454de126d3dd4" and
            contract.get("h0PrefreezeJsonSha256") ==
            "0719711873aa9397d180974e811695681ff0ca0450fcd4966e6d6e08c05b6da8",
            "H0 frozen contract provenance drift")
    for role, version in zip(("base", "target"), h0.VERSIONS):
        item = contract.get(role)
        require(isinstance(item, dict) and item.get("nugetVersion") == version,
                "H1 frozen package version drift")
        for key, nchars in (("packageSha256", 64), ("packageSha512", 128),
                            ("dllSha256", 64), ("dllSha512", 128)):
            digest = item.get(key)
            require(isinstance(digest, str) and len(digest) == nchars and
                    all(x in "0123456789abcdef" for x in digest),
                    "invalid H0 frozen literal: " + role + "/" + key)
        require(0 < item.get("packageBytes", 0) <= h0.MAX_PACKAGE_BYTES and
                0 < item.get("dllBytes", 0) <= h0.MAX_ASSEMBLY_BYTES,
                "invalid H0 frozen size")
    return contract


def verify_download(raw, expected):
    observed = h0.inspect_archive(raw)
    for key in ("packageBytes", "packageSha256", "packageSha512",
                "dllBytes", "dllSha256", "dllSha512"):
        require(observed[key] == expected[key],
                "immutable H0 NuGet/DLL oracle mismatch: " + key)
    with zipfile.ZipFile(io.BytesIO(raw)) as package:
        dll = package.read(EXPECTED_ASSET)
    require(len(dll) == expected["dllBytes"] and
            hashlib.sha256(dll).hexdigest() == expected["dllSha256"],
            "second independent H1 extraction check failed")
    return dll


def evaluate_profiles(private, base_raw, target_raw):
    meta = json.loads((private / "scanner-control.json").read_text())
    require(meta.get("schema") == "chunkshift.core011.g1j2h1-profile-controls.v1" and
            meta.get("verdict") == "NO_VERDICT_EXPLORATORY" and
            meta.get("hashSuite") == "chunkshift.blake3-256.v1",
            "unexpected H1 scanner controls")
    observations = meta.get("observations")
    require(isinstance(observations, list) and len(observations) == 10,
            "H1 expected ten kernel/scalar oracles")
    expect_order = [(role, lane) for role in ("base", "target")
                    for lane in PROFILE_LANES]
    require([(x["role"], x["lane"]) for x in observations] == expect_order,
            "H1 scanner lane order drift")
    scanners = {"base": {}, "target": {}}
    sources = {"base": base_raw, "target": target_raw}
    for role, lane in expect_order:
        path = private / f"{role}.{lane}.tsv"
        rows = j2f.validate_scanner(sources[role], path.read_text())
        oracle = next(x for x in observations
                      if x["role"] == role and x["lane"] == lane)
        require(oracle["chunkCount"] == len(rows) and
                oracle["contentLength"] == len(sources[role]) and
                oracle["tsvFile"] == path.name,
                "H1 scanner table does not match Core-runner control")
        scanners[role][lane] = rows

    output = {}
    for lane in PROFILE_LANES:
        output[lane] = j2f.compare_lane(
            base_raw, target_raw, scanners["base"][lane], scanners["target"][lane])
        output[lane]["registeredPublicProfile"] = (lane == "stable-fastcdc64k")
        output[lane]["baseTsvSha256"] = j2g.digest_file(private / f"base.{lane}.tsv")
        output[lane]["targetTsvSha256"] = j2g.digest_file(private / f"target.{lane}.tsv")
    return output


def build(repo, project):
    completed = subprocess.run(
        ["dotnet", "build", project, "-c", "Release"],
        cwd=repo, capture_output=True, text=True, timeout=900, check=False)
    require(completed.returncode == 0,
            "Release build failed " + project + " " + completed.stdout[-700:] +
            completed.stderr[-700:])


def run(repo, output, run_id):
    checkout = subprocess.check_output(["git", "rev-parse", "HEAD"],
                                        cwd=repo, text=True).strip()
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("GITHUB_SHA") == checkout and len(checkout) == 40,
            "H1 requires pinned GitHub-hosted checkout")
    require(run_id.startswith(PREFIX) and len(run_id) > len(PREFIX),
            "invalid H1 RunId")
    require(not output.exists(), "refuse to overwrite H1 evidence")
    contract_bytes = CONTRACT.read_bytes()
    freeze = validate_contract(json.loads(contract_bytes))
    output.mkdir(parents=True)
    private = output / "private"
    private.mkdir()
    try:
        originals = {}
        for role, version in zip(("base", "target"), h0.VERSIONS):
            original_package = h0.fetch_package(version)
            originals[role] = verify_download(original_package, freeze[role])
            (private / f"{role}.input").write_bytes(originals[role])
            del original_package
        require(originals["base"] != originals["target"],
                "the independently frozen changed files are identical")

        # Public unchanged product CSM/CSP and separate internal lab-kernel runner.
        build(repo, "src/ChunkShift.Cli/ChunkShift.Cli.csproj")
        build(repo, "benchmarks/ChunkShift.Benchmarks.slnx")
        sdk = subprocess.check_output(["dotnet", "--version"], cwd=repo,
                                      text=True).strip()
        public = {}
        timings = {}
        for role in ("base", "target"):
            path = private / f"{role}.csm"
            out, timings[f"{role}-csm-create"] = j2g.timed_cli(
                repo, private, f"{role}-csm-create",
                "create", private / f"{role}.input", path, "--blake3", "--bidx")
            fields = j2g.parse_kv(out)
            j2g.require_metrics(fields, {
                "content-bytes": freeze[role]["dllBytes"], "bidx": "true",
            })
            public[role] = {
                "contentBytes": freeze[role]["dllBytes"],
                "originalDllSha256": freeze[role]["dllSha256"],
                "physicalCsmSha256": j2g.digest_file(path),
                "physicalCsmBytes": path.stat().st_size,
                "manifestId": fields["manifest-id"],
                "chunkCount": int(fields["chunks"]),
                "chunkBlockCount": int(fields["cblk-count"]),
            }
            out, timings[f"{role}-csm-verify"] = j2g.timed_cli(
                repo, private, f"{role}-csm-verify",
                "verify", path, private / f"{role}.input")
            j2g.require_metrics(j2g.parse_kv(out), {"valid": "true"})

        out, timings["stable64k-plan"] = j2g.timed_cli(
            repo, private, "stable64k-plan", "plan",
            "--base", private / "base.csm",
            "--target", private / "target.csm")
        plan = j2g.parse_kv(out)
        j2g.require_metrics(plan, {
            "valid": "true", "same-profile": "true",
            "base-manifest-id": public["base"]["manifestId"],
            "target-manifest-id": public["target"]["manifestId"],
        })
        require(int(plan["base-chunks"]) == public["base"]["chunkCount"] and
                int(plan["target-chunks"]) == public["target"]["chunkCount"] and
                int(plan["base-bytes"]) == len(originals["base"]) and
                int(plan["target-bytes"]) == len(originals["target"]),
                "public CSM Plan conservation mismatch")

        proc = subprocess.run(
            ["dotnet", "run", "--no-build", "-c", "Release", "--project",
             "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
             "--", "core011-g1j2h1-profiles", "--base", str(private / "base.input"),
             "--target", str(private / "target.input"), "--output-dir", str(private)],
            cwd=repo, text=True, capture_output=True, timeout=900, check=False)
        require(proc.returncode == 0, "J2H1 actual Core kernel reference failed " +
                proc.stderr[-700:])
        profiles = evaluate_profiles(private, originals["base"], originals["target"])
        s = profiles["stable-fastcdc64k"]
        require(s["baseChunks"] == int(plan["base-chunks"]) and
                s["targetChunks"] == int(plan["target-chunks"]) and
                s["reusedChunks"] == int(plan["reused-chunks"]) and
                s["reusedBytes"] == int(plan["reused-bytes"]) and
                s["missingChunks"] == int(plan["missing-chunks"]) and
                s["missingBytes"] == int(plan["missing-bytes"]),
                "public Plan vs independent exact-byte stable scanner mismatch")

        results = {}
        for lane in LANES:
            with_base = lane == LANES[0]
            patch = private / (lane + ".csp")
            recon = private / (lane + ".reconstructed")
            args = ["patch", "create", "--target-manifest", private / "target.csm",
                    "--target", private / "target.input", "-o", patch]
            if with_base:
                args += ["--base-manifest", private / "base.csm",
                         "--base", private / "base.input"]
            out, timings[lane + "-create"] = j2g.timed_cli(
                repo, private, lane + "-create", *args)
            result = j2g.patch_metrics(out, patch, public["target"]["manifestId"],
                                       public["base"]["manifestId"] if with_base else None)
            args = ["patch", "apply", patch, "-o", recon]
            if with_base:
                args += ["--base-manifest", private / "base.csm",
                         "--base", private / "base.input"]
            out, timings[lane + "-apply"] = j2g.timed_cli(
                repo, private, lane + "-apply", *args)
            j2g.require_metrics(j2g.parse_kv(out), {
                "applied": "true",
                "output-bytes": len(originals["target"]),
                "target-manifest-id": public["target"]["manifestId"],
            })
            require(j2g.digest_file(recon) == freeze["target"]["dllSha256"] and
                    recon.stat().st_size == freeze["target"]["dllBytes"],
                    "H1 production CSP applied target does not match original bytes")
            out, timings[lane + "-verify"] = j2g.timed_cli(
                repo, private, lane + "-verify", "verify", private / "target.csm", recon)
            j2g.require_metrics(j2g.parse_kv(out), {"valid": "true"})
            result["reconstructedTargetSha256"] = freeze["target"]["dllSha256"]
            result["exactReconstruction"] = True
            results[lane] = result

        with_base = results[LANES[0]]["physicalPatchBytes"]
        standalone = results[LANES[1]]["physicalPatchBytes"]
        report = {
            "schema": SCHEMA, "runId": run_id, "checkoutSha": checkout,
            "dotnetSdkVersion": sdk,
            "frozenCorpusContractSha256": hashlib.sha256(contract_bytes).hexdigest(),
            "h0RunId": FROZEN_H0_RUN,
            "h0ZipSha256": freeze["h0OriginalZipSha256"],
            "originalBytes": {"base": len(originals["base"]),
                              "target": len(originals["target"])},
            "publicCsm": public,
            "publicPlan": {key: int(plan[key]) for key in
                           ("base-chunks", "target-chunks", "base-bytes", "target-bytes",
                            "reused-chunks", "reused-bytes", "missing-chunks", "missing-bytes")},
            "labProfiles": profiles,
            "physicalCsp": results,
            "comparisons": {
                "baseAwareMinusSelfContainedBytes": with_base - standalone,
                "baseAwareFractionOfTarget": round(with_base / len(originals["target"]), 8),
                "selfContainedFractionOfTarget": round(standalone / len(originals["target"]), 8)},
            "processMeasurements": timings,
            "limitations": "single nonrandom product version pair; one hosted runner; CLI startup/caches; unregistered lab profiles have no physical CSP",
            "noPublicProfileOrProductionChanges": True,
            "decisionRunsAllowed": False, "budgetsLocked": False,
            "verdict": "NO_VERDICT_EXPLORATORY",
        }
        (output / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
        print("H1 SHA-frozen Newtonsoft original, five actual Core profiles, "
              "two production CSP reconstructions PASS; "
              "base-aware", with_base, "standalone", standalone, "NO_VERDICT")
    except Exception as exc:
        (output / "failure.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j2h1-failure.v1",
            "runId": run_id, "errorType": type(exc).__name__,
            "stage": "FAIL", "verdict": "NO_VERDICT_EXPLORATORY",
        }, indent=2) + "\n")
        raise
    finally:
        shutil.rmtree(private, ignore_errors=False)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    run(Path.cwd(), args.output_dir, args.run_id)


if __name__ == "__main__":
    main()
