#!/usr/bin/env python3
"""G1-J2I: REAL physical CSP comparison, original stable vs disposable lab16k.

Two immutable independently frozen NuGet binary families; 2 profiles; 2 CSP
modes; full SHA256 reconstruct. Experimental Core modification only in a
temporary detached Git worktree (NEVER committed or included in artifacts).
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

import core011_csm_g1j2d_forensic as roslyn
import core011_csm_g1j2e_boundary as cblk
import core011_csm_g1j2g_patch_cost as csp
import core011_csm_g1j2h0_holdout_freeze as nupkg
import core011_csm_g1j2h1_holdout_eval as newton

SCHEMA = "chunkshift.core011.g1j2i-real-physical-ab.v1"
PREFIX = "CORE011-CSM-001/G1J2I-"
CONFIG = Path("src/ChunkShift/Chunking/ChunkScanConfiguration.cs")
LAB_ID = "lab.fastcdc.gear.chunkshift.v1.m4096.t16384.x65536"
STABLE_ID = "fastcdc.gear.chunkshift.v1.64k"
OLD = """            ProfileRegistration.Create(
                FastCdcProfile.Stable64KProfileId,
                FastCdcProfile.CreateStable64K());"""
NEW = """            ProfileRegistration.Create(
                new ChunkingProfileId("lab.fastcdc.gear.chunkshift.v1.m4096.t16384.x65536"),
                new FastCdcProfile(4 * 1024, 16 * 1024, 64 * 1024));"""
PROFILES = ("stable64k", "lab16k")
MODES = ("based", "standalone")
EXPECTED = {
    "roslyn": {
        "stable64k": (74, 81, 0, 0),
        "lab16k": (315, 316, 8, 149568),
        "baselineCsmSha": cblk.CSM_SHA,
        "baselineCspSha": {
            "based": "901e044c6e76fd25cb303c4b59a042c5b23c534a1d71503ffe22dd25ece0dfd3",
            "standalone": "00f089ccee90e59dc76f7fce6aacba2e15f06bdc265f064b28688ff2c5fc3449",
        },
        "baselineCspBytes": {"based": 1260098, "standalone": 2100946},
    },
    "newtonsoft": {
        "stable64k": (11, 10, 0, 0),
        "lab16k": (36, 36, 5, 65601),
        "baselineCsmSha": {
            "base": "c3dfe51942d58bb97198ea71c619e2c094f60685161df517281deeee790659ad",
            "target": "87fb52ab2e2717ce42a3fa7c49ba72947ede5869a2d68e595a6a9845f1d8d95e",
        },
        "baselineCspSha": {
            "based": "25176257a0bab4855b66f9adb9f98428a597a1ba8c31f3ddbc64e189ee92106c",
            "standalone": "bc5698bdef5bbc97b39d7e80e6e5cd2b3a8a2e8c4ee07f5427824bcef14cc060",
        },
        "baselineCspBytes": {"based": 78435, "standalone": 247508},
    },
}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def variant_source(source):
    require(source.count(OLD) == 1, "Core profile source anchor changed, abort")
    require(LAB_ID not in source, "experimental profile already exists in Core")
    require(source.count("FastCdcProfile.Stable64KProfileId") == 1,
            "Core stable identity source registration count drift")
    modified = source.replace(OLD, NEW, 1)
    require(modified != source and modified.count(LAB_ID) == 1,
            "lab source patch did not inject one distinct identity")
    require("FastCdcProfile.CreateStable64K());" not in modified,
            "lab Core still claims default stable profile")
    return modified


def check_zip_contracts():
    old_contract = roslyn.verify_contract(json.loads(roslyn.CONTRACT.read_text()))
    heldout_raw = newton.CONTRACT.read_bytes()
    heldout_contract = newton.validate_contract(json.loads(heldout_raw))
    return old_contract, heldout_contract, sha(heldout_raw)


def fetch_sources(old_contract, heldout_contract):
    ros = roslyn.fetch_original_payloads(old_contract)
    for role in ("base", "target"):
        require(sha(ros[role]) == csp.EXPECTED_SOURCE_SHA[role] and
                len(ros[role]) == csp.EXPECTED_BYTES[role],
                "Roslyn frozen original bytes drift")
    managed = {}
    for role, version in zip(("base", "target"), nupkg.VERSIONS):
        raw_pkg = nupkg.fetch_package(version)
        managed[role] = newton.verify_download(raw_pkg, heldout_contract[role])
        del raw_pkg
    return {"roslyn": ros, "newtonsoft": managed}


def create_worktree(repo, temp_root, checkout):
    worktree = temp_root / "experimental-lab16k"
    require(not worktree.exists(), "worktree not ephemeral")
    proc = subprocess.run(
        ["git", "worktree", "add", "--detach", str(worktree), checkout],
        cwd=repo, text=True, capture_output=True, timeout=120)
    require(proc.returncode == 0, "detached worktree checkout failed")
    try:
        original = (worktree / CONFIG).read_text()
        changed = variant_source(original)
        (worktree / CONFIG).write_text(changed, encoding="utf-8", newline="\n")
        require(subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=worktree,
                                        text=True).strip() == checkout,
                "lab worktree differs from stable checkout")
        modified_files = subprocess.check_output(
            ["git", "diff", "--name-only"], cwd=worktree, text=True).splitlines()
        require(modified_files == [CONFIG.as_posix()],
                "lab worktree modified more than one Core registration file")
        return worktree, {
            "originalRegistrySourceSha256": sha(original.encode()),
            "labRegistrySourceSha256": sha(changed.encode()),
            "modifiedFiles": modified_files,
            "onlyDisposableGitWorktree": True,
            "labProfileId": LAB_ID,
            "gitCommit": checkout,
        }
    except BaseException:
        subprocess.run(["git", "worktree", "remove", "--force", str(worktree)],
                       cwd=repo, capture_output=True, timeout=120)
        raise


def release_build(repo):
    p = subprocess.run(
        ["dotnet", "build", "src/ChunkShift.Cli/ChunkShift.Cli.csproj", "-c", "Release"],
        cwd=repo, text=True, capture_output=True, timeout=900)
    require(p.returncode == 0, "Release build failed; " + p.stderr[-500:])


def exact_reuse(a_bytes, b_bytes, a_chunks, b_chunks):
    by_id = {}
    for item in a_chunks:
        by_id.setdefault(item["chunkId"], []).append(item)
    reused, count = 0, 0
    for item in b_chunks:
        found = by_id.get(item["chunkId"])
        if not found:
            continue
        # Verify each matching hash against ACTUAL frozen original bytes.
        target_slice = b_bytes[item["offset"]:item["offset"] + item["length"]]
        for prior in found:
            require(prior["length"] == item["length"] and
                    a_bytes[prior["offset"]:prior["offset"] + prior["length"]] == target_slice,
                    "opaque ChunkId match failed exact original byte comparison")
        reused += item["length"]
        count += 1
    return count, reused


def evaluate_family(repo_by_profile, private, family, original, wire_analyzer=None):
    """Compute both same-host source-distinct physical lanes, fully reconstruct."""
    family_dir = private / family
    family_dir.mkdir()
    for role in ("base", "target"):
        (family_dir / (role + ".input")).write_bytes(original[role])
    measurements = {}
    for variant in PROFILES:
        repo = repo_by_profile[variant]
        expect = EXPECTED[family][variant]
        root = family_dir / variant
        root.mkdir()
        csms = {}
        raw_entries = {}
        samples = {}
        for role in ("base", "target"):
            manifest = root / (role + ".csm")
            out, metrics = csp.timed_cli(
                repo, private, f"{family}-{variant}-{role}-csm-create",
                "create", family_dir / (role + ".input"),
                manifest, "--blake3", "--bidx")
            fields = csp.parse_kv(out)
            csp.require_metrics(fields, {"profile": LAB_ID if variant == "lab16k"
                                          else STABLE_ID,
                                         "content-bytes": len(original[role]),
                                         "bidx": "true",
                                         "chunks": expect[0 if role=="base" else 1]})
            csms[role] = {"profileId": fields["profile"],
                          "fingerprint": fields["profile-fingerprint"],
                          "manifestId": fields["manifest-id"],
                          "physicalBytes": manifest.stat().st_size,
                          "physicalSha256": csp.digest_file(manifest),
                          "chunkCount": int(fields["chunks"])}
            if variant == "stable64k":
                require(csms[role]["physicalSha256"] ==
                        EXPECTED[family]["baselineCsmSha"][role],
                        "original frozen public stable CSM physical identity drift")
            raw_entries[role] = cblk.parse_csm_cblk(
                manifest.read_bytes(), len(original[role]), csms[role]["chunkCount"])
            samples[role + "-create"] = metrics
            out, metrics = csp.timed_cli(
                repo, private, f"{family}-{variant}-{role}-csm-verify",
                "verify", manifest, family_dir / (role + ".input"))
            csp.require_metrics(csp.parse_kv(out), {"valid": "true"})
            samples[role + "-verify"] = metrics
        require(csms["base"]["fingerprint"] == csms["target"]["fingerprint"],
                "within-lane profile semantics drift")
        reuse_count, reuse_bytes = exact_reuse(
            original["base"], original["target"], raw_entries["base"], raw_entries["target"])
        require((csms["base"]["chunkCount"],csms["target"]["chunkCount"],
                 reuse_count,reuse_bytes) == expect,
                "source-frozen J2F/H1 exact reused chunk oracle drift")
        out, metrics = csp.timed_cli(
            repo, private, f"{family}-{variant}-plan",
            "plan", "--base", root / "base.csm", "--target", root / "target.csm")
        plan = csp.parse_kv(out)
        csp.require_metrics(plan, {
            "valid": "true", "same-profile": "true",
            "base-manifest-id": csms["base"]["manifestId"],
            "target-manifest-id": csms["target"]["manifestId"],
            "reused-chunks": reuse_count,
            "reused-bytes": reuse_bytes,
            "missing-chunks": csms["target"]["chunkCount"] - reuse_count,
            "missing-bytes": len(original["target"]) - reuse_bytes,
        })
        samples["plan"] = metrics
        patches = {}
        for mode in MODES:
            patch = root / (mode + ".csp")
            restored = root / (mode + ".reconstructed")
            args = ["patch", "create", "--target-manifest", root / "target.csm",
                    "--target", family_dir / "target.input", "-o", patch]
            if mode == "based":
                args += ["--base-manifest", root / "base.csm",
                         "--base", family_dir / "base.input"]
            out, samples[mode + "-create"] = csp.timed_cli(
                repo, private, f"{family}-{variant}-{mode}-patch-create", *args)
            info = csp.patch_metrics(out, patch, csms["target"]["manifestId"],
                                     csms["base"]["manifestId"] if mode == "based" else None)
            if variant == "stable64k":
                require(info["physicalPatchBytes"] ==
                        EXPECTED[family]["baselineCspBytes"][mode] and
                        info["physicalPatchSha256"] ==
                        EXPECTED[family]["baselineCspSha"][mode],
                        "original frozen public CSP physical bytes/SHA drift")
            if wire_analyzer is not None:
                wire = wire_analyzer(patch.read_bytes(), (root / "target.csm").read_bytes(),
                                     mode == "based")
                require(wire["physicalBytes"] == info["physicalPatchBytes"] and
                        wire["ledger"]["storedEncodedPayload"] == info["payloadBytes"] and
                        wire["entryCounts"]["payloadEntries"] == info["payloadEntries"],
                        "J2J independent wire ledger vs public PatchInfo mismatch")
                info["wire"] = wire
            args = ["patch", "apply", patch, "-o", restored]
            if mode == "based":
                args += ["--base-manifest", root / "base.csm",
                         "--base", family_dir / "base.input"]
            out, samples[mode + "-apply"] = csp.timed_cli(
                repo, private, f"{family}-{variant}-{mode}-patch-apply", *args)
            csp.require_metrics(csp.parse_kv(out), {
                "applied": "true", "output-bytes": len(original["target"]),
                "target-manifest-id": csms["target"]["manifestId"],
            })
            require(restored.stat().st_size == len(original["target"])
                    and csp.digest_file(restored) == sha(original["target"]),
                    "lab/stable CSP failed exact frozen target reconstruction")
            out, samples[mode + "-verify"] = csp.timed_cli(
                repo, private, f"{family}-{variant}-{mode}-post-verify",
                "verify", root / "target.csm", restored)
            csp.require_metrics(csp.parse_kv(out), {"valid": "true"})
            info["exactOriginalTargetReconstructed"] = True
            patches[mode] = info
        measurements[variant] = {
            "csms": csms,
            "exactReuse": {"chunks": reuse_count, "bytes": reuse_bytes},
            "physicalCsp": patches,
            "oneRunnerProcessSamples": samples,
        }
    require(measurements["stable64k"]["csms"]["target"]["fingerprint"] !=
            measurements["lab16k"]["csms"]["target"]["fingerprint"],
            "experimental semantics incorrectly share the stable profile fingerprint")
    require(measurements["lab16k"]["csms"]["target"]["profileId"] == LAB_ID,
            "experimental CSM falsely asserts the production profile identity")
    delta = {}
    for mode in MODES:
        stable = measurements["stable64k"]["physicalCsp"][mode]["physicalPatchBytes"]
        lab = measurements["lab16k"]["physicalCsp"][mode]["physicalPatchBytes"]
        delta[mode] = {
            "stable64kPatchBytes": stable, "lab16kPatchBytes": lab,
            "labMinusStablePhysicalBytes": lab - stable,
            "labMinusStableFraction": round((lab - stable) / stable, 8),
            "isLabPhysicallySmaller": lab < stable,
        }
    return {"variants": measurements, "physicalComparison": delta}


def run(repo, output, work_root, run_id, wire_analyzer=None,
        schema=SCHEMA, run_prefix=PREFIX):
    checkout = subprocess.check_output(["git", "rev-parse", "HEAD"],
                                        cwd=repo, text=True).strip()
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("GITHUB_SHA") == checkout and len(checkout) == 40,
            "J2I requires original GitHub-hosted exact commit")
    require(run_id.startswith(run_prefix) and len(run_id) > len(run_prefix),
            "J2I invalid run identity")
    require(not output.exists() and not work_root.exists(), "refuse reused output/worktrees")
    old, heldout, heldout_sha = check_zip_contracts()
    output.mkdir(parents=True)
    private = output / "private"
    private.mkdir()
    work_root.mkdir()
    labtree = None
    try:
        sources = fetch_sources(old, heldout)
        labtree, mutation = create_worktree(repo, work_root, checkout)
        # Variants built from the very same pinned synthetic PR commit.
        release_build(repo)
        release_build(labtree)
        families = {}
        for family in ("roslyn", "newtonsoft"):
            families[family] = evaluate_family(
                {"stable64k": repo, "lab16k": labtree}, private, family,
                sources[family], wire_analyzer=wire_analyzer)
        report = {
            "schema": schema, "runId": run_id, "checkoutSha": checkout,
            "originalRoslynContractSha256": sha(roslyn.CONTRACT.read_bytes()),
            "originalNewtonsoftContractSha256": heldout_sha,
            "sources": {family: {
                role: {"originalBytes": len(data),
                       "originalSha256": sha(data)}
                for role,data in sources[family].items()
            } for family in ("roslyn","newtonsoft")},
            "sourceMutation": mutation,
            "families": families,
            "limits": "Lab profile only in ephemeral detached same-SHA Core build. "
                      "All original DLLs/physical CSM/CSP/reconstruction deleted; "
                      "one host / process startup not product CPU evidence.",
            "decisionRunsAllowed": False, "budgetsLocked": False,
            "verdict": "NO_VERDICT_EXPLORATORY",
        }
        (output / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
        for family in ("roslyn", "newtonsoft"):
            print(family, families[family]["physicalComparison"])
        print("G1-J2I two frozen sources x two honest profile semantics x both real CSP modes "
              "full original target reconstructed PASS; product NO_VERDICT")
    except Exception as exc:
        (output / "failure.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j2i-failure.v1",
            "runId": run_id, "errorType": type(exc).__name__,
            "status": "FAILED", "verdict": "NO_VERDICT_EXPLORATORY",
        }, indent=2) + "\n")
        raise
    finally:
        shutil.rmtree(private, ignore_errors=False)
        if labtree is not None:
            subprocess.run(["git", "worktree", "remove", "--force", str(labtree)],
                           cwd=repo, capture_output=True, timeout=120, check=True)
        shutil.rmtree(work_root, ignore_errors=False)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output-dir", type=Path, required=True)
    p.add_argument("--worktree-root", type=Path, required=True)
    p.add_argument("--run-id", required=True)
    x = p.parse_args()
    run(Path.cwd(), x.output_dir, x.worktree_root, x.run_id)


if __name__ == "__main__":
    main()
