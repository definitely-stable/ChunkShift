#!/usr/bin/env python3
"""G1-J2G: physical CSP size and exact apply controls on a frozen Roslyn pair.

Research-only. Uses unchanged PUBLIC ChunkShift CLI, with exactly two
registered-stable64k lanes. All raw binary material stays in a private
work directory that is unlinked before artifact upload.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

import core011_csm_g1j2d_forensic as frozen_source
import core011_csm_g1j2e_boundary as boundary

SCHEMA = "chunkshift.core011.g1j2g-physical-csp-cost.v1"
PREFIX = "CORE011-CSM-001/G1J2G-"
EXPECTED_SOURCE_SHA = {
    "base": "7da8fb18fa6e19a166b35d87b731ecbb040c3ad2cc7894b19953384aee0f5fe4",
    "target": "9acdb971334046d22a07fad02648e2725ef5a1b9d0fbe4fe82f1ada57fa70c9e",
}
EXPECTED_BYTES = {"base": 6604072, "target": 6697264}
LANES = ("BASE_AWARE_STABLE64K", "SELF_CONTAINED_STABLE64K")


def require(value, message):
    if not value:
        raise ValueError(message)


def sha256(raw):
    return hashlib.sha256(raw).hexdigest()


def digest_file(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def parse_kv(stdout):
    values = {}
    for raw in stdout.splitlines():
        if not raw:
            continue
        parts = raw.split("=", 1)
        require(len(parts) == 2 and parts[0] and parts[1],
                "public CLI output is not key=value")
        key, value = parts
        require(key not in values, "duplicate public CLI metric")
        values[key] = value
    return values


def require_metrics(metrics, expected):
    for key, value in expected.items():
        require(metrics.get(key) == str(value),
                "public CLI frozen oracle mismatch: " + key)


def patch_metrics(stdout, actual_path, expected_target_id, base_id=None):
    record = parse_kv(stdout)
    required = ("target-manifest-id", "patch-bytes", "payload-bytes",
                "payload-entries", "file-digest")
    require(all(key in record for key in required), "incomplete public PatchInfo")
    require(record["target-manifest-id"] == expected_target_id,
            "patch target manifest identity changed")
    require((record.get("base-manifest-id") == base_id) if base_id is not None
            else ("base-manifest-id" not in record),
            "wrong base manifest association")
    try:
        physical = int(record["patch-bytes"])
        payload = int(record["payload-bytes"])
        entries = int(record["payload-entries"])
    except ValueError as exc:
        raise ValueError("noninteger public PatchInfo metrics") from exc
    require(physical > 0 and 0 <= payload <= physical and entries >= 0,
            "invalid physical/payload accounting")
    require(actual_path.stat().st_size == physical,
            "public PatchInfo physical length differs from actual file")
    return {
        "physicalPatchBytes": physical,
        "payloadBytes": payload,
        "physicalMinusReportedPayloadBytes": physical - payload,
        "payloadEntries": entries,
        "physicalPatchSha256": digest_file(actual_path),
        "patchFileDigest": record["file-digest"],
        "targetManifestId": record["target-manifest-id"],
        "baseManifestId": record.get("base-manifest-id"),
    }


def timed_cli(repo, private, name, *arguments):
    """One Release CLI invocation. /usr/bin/time measures this child process.

    Wall includes process startup/CLI overhead; unrelated hosts must not be
    treated as statistically independent samples.
    """
    meter = Path("/usr/bin/time")
    require(meter.is_file(), "GitHub-hosted GNU /usr/bin/time missing")
    time_file = private / (name + ".time")
    require(not time_file.exists(), "duplicate CLI observation")
    cmd = [str(meter), "-f", "%U\t%S\t%M", "-o", str(time_file),
           "dotnet", "run", "--no-build", "-c", "Release",
           "--project", "src/ChunkShift.Cli/ChunkShift.Cli.csproj",
           "--", *map(str, arguments)]
    start = time.perf_counter_ns()
    call = subprocess.run(cmd, cwd=repo, capture_output=True, text=True,
                          timeout=900, check=False)
    wall = (time.perf_counter_ns() - start) / 1e9
    require(call.returncode == 0, "public CLI " + name + " failed; exit=" +
            str(call.returncode))
    pieces = time_file.read_text().strip().split("\t")
    require(len(pieces) == 3, "malformed GNU time metrics")
    cpu_user, cpu_system = float(pieces[0]), float(pieces[1])
    peak_rss = int(pieces[2])
    require(cpu_user >= 0 and cpu_system >= 0 and peak_rss > 0,
            "invalid subprocess timing/RSS")
    return call.stdout, {
        "wallSeconds": round(wall, 6),
        "childUserCpuSeconds": cpu_user,
        "childSystemCpuSeconds": cpu_system,
        "childMaximumRssKiB": peak_rss,
        "scope": "whole dotnet CLI subprocess incl startup; descriptive only",
    }


def run(repo, output, run_id):
    checkout = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("GITHUB_SHA") == checkout and len(checkout) == 40,
            "J2G original binary work must run on exact GitHub-hosted checkout")
    require(run_id.startswith(PREFIX) and len(run_id) > len(PREFIX),
            "invalid G1-J2G RunId")
    require(not output.exists(), "refuse overwrite of J2G output")
    output.mkdir(parents=True)
    private = output / "private"
    private.mkdir()
    try:
        contract_bytes = frozen_source.CONTRACT.read_bytes()
        contract = frozen_source.verify_contract(json.loads(contract_bytes))
        original = frozen_source.fetch_original_payloads(contract)
        for role in ("base", "target"):
            raw = original[role]
            require(len(raw) == EXPECTED_BYTES[role] and
                    sha256(raw) == EXPECTED_SOURCE_SHA[role],
                    "frozen source bytes mismatch: " + role)
            (private / (role + ".input")).write_bytes(raw)

        subprocess.run(
            ["dotnet", "build", "src/ChunkShift.Cli/ChunkShift.Cli.csproj",
             "-c", "Release"],
            cwd=repo, check=True, timeout=900, capture_output=True, text=True)
        dotnet_version = subprocess.check_output(
            ["dotnet", "--version"], cwd=repo, text=True).strip()

        observed = {}
        clocks = {}
        for role in ("base", "target"):
            stdout, metrics = timed_cli(repo, private, "manifest-" + role,
                "create", private / (role + ".input"),
                private / (role + ".csm"), "--blake3", "--bidx")
            fields = parse_kv(stdout)
            require_metrics(fields, {
                "chunks": 74 if role == "base" else 81,
                "content-bytes": EXPECTED_BYTES[role],
            })
            require(digest_file(private / (role + ".csm")) ==
                    boundary.CSM_SHA[role],
                    "historical CSM physical SHA changed: " + role)
            observed[role] = {
                "originalContentSha256": EXPECTED_SOURCE_SHA[role],
                "contentBytes": EXPECTED_BYTES[role],
                "physicalCsmBytes": (private / (role + ".csm")).stat().st_size,
                "physicalCsmSha256": boundary.CSM_SHA[role],
                "manifestId": fields["manifest-id"],
            }
            clocks["manifest-" + role] = metrics
            stdout, clocks["verify-" + role] = timed_cli(
                repo, private, "verify-" + role, "verify",
                private / (role + ".csm"), private / (role + ".input"))
            require_metrics(parse_kv(stdout), {"valid": "true"})

        stdout, clocks["plan"] = timed_cli(
            repo, private, "plan", "plan",
            "--base", private / "base.csm",
            "--target", private / "target.csm")
        plan = parse_kv(stdout)
        require_metrics(plan, {
            "valid": "true",
            "base-chunks": 74,
            "target-chunks": 81,
            "base-bytes": EXPECTED_BYTES["base"],
            "target-bytes": EXPECTED_BYTES["target"],
            "reused-chunks": 0,
            "reused-bytes": 0,
            "missing-chunks": 81,
            "missing-bytes": EXPECTED_BYTES["target"],
            "same-profile": "true",
        })
        require_metrics(plan, {
            "base-manifest-id": observed["base"]["manifestId"],
            "target-manifest-id": observed["target"]["manifestId"],
        })

        lanes = {}
        for lane in LANES:
            with_base = lane == "BASE_AWARE_STABLE64K"
            patch = private / (lane + ".csp")
            target_copy = private / (lane + ".reconstructed")
            args = ["patch", "create", "--target-manifest",
                    private / "target.csm", "--target",
                    private / "target.input", "-o", patch]
            if with_base:
                args.extend(["--base-manifest", private / "base.csm",
                             "--base", private / "base.input"])
            stdout, clocks[lane + "-create"] = timed_cli(
                repo, private, lane + "-create", *args)
            info = patch_metrics(stdout, patch, observed["target"]["manifestId"],
                                 observed["base"]["manifestId"] if with_base else None)
            apply_args = ["patch", "apply", patch, "-o", target_copy]
            if with_base:
                apply_args.extend(["--base-manifest", private / "base.csm",
                                   "--base", private / "base.input"])
            stdout, clocks[lane + "-apply"] = timed_cli(
                repo, private, lane + "-apply", *apply_args)
            require_metrics(parse_kv(stdout), {
                "applied": "true",
                "target-manifest-id": observed["target"]["manifestId"],
                "output-bytes": EXPECTED_BYTES["target"],
            })
            require(target_copy.stat().st_size == EXPECTED_BYTES["target"] and
                    digest_file(target_copy) == EXPECTED_SOURCE_SHA["target"],
                    "patch apply reconstruction not identical to frozen target")
            stdout, clocks[lane + "-verify"] = timed_cli(
                repo, private, lane + "-verify", "verify",
                private / "target.csm", target_copy)
            require_metrics(parse_kv(stdout), {"valid": "true"})
            info["reconstructedTargetSha256"] = EXPECTED_SOURCE_SHA["target"]
            info["exactReconstruction"] = True
            lanes[lane] = info

        based = lanes[LANES[0]]["physicalPatchBytes"]
        standalone = lanes[LANES[1]]["physicalPatchBytes"]
        require(standalone > 0, "self-contained patch has invalid size")
        outcome = {
            "schema": SCHEMA,
            "runId": run_id,
            "checkoutSha": checkout,
            "runner": os.environ.get("RUNNER_OS", "unknown"),
            "dotnetSdkVersion": dotnet_version,
            "corpusContractSha256": sha256(contract_bytes),
            "source": observed,
            "publicPlan": {
                "reusedChunks": 0,
                "missingChunks": 81,
                "missingBytes": EXPECTED_BYTES["target"],
            },
            "lanes": lanes,
            "comparisons": {
                "baseAwareMinusStandalonePhysicalBytes": based - standalone,
                "baseAwareFractionOfStandalone": round(based / standalone, 8),
                "baseAwareFractionOfTarget": round(based / EXPECTED_BYTES["target"], 8),
                "selfContainedFractionOfTarget": round(standalone / EXPECTED_BYTES["target"], 8),
            },
            "observations": clocks,
            "timingLimitations": "single hosted runner; whole CLI startup/caches; no statistical product CPU verdict",
            "decisionRunsAllowed": False,
            "budgetsLocked": False,
            "verdict": "NO_VERDICT_EXPLORATORY",
        }
        (output / "summary.json").write_text(json.dumps(outcome, indent=2) + "\n")
        print("J2G physical CSP and both exact reconstruction lanes PASS",
              "base-aware bytes", based, "self-contained bytes", standalone,
              "product NO_VERDICT_EXPLORATORY")
    except Exception as error:
        (output / "failure.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j2g-failure.v1",
            "runId": run_id,
            "errorType": type(error).__name__,
            "failure": "J2G experiment failed; inspect hosted job log; no product verdict",
            "verdict": "NO_VERDICT_EXPLORATORY",
        }, indent=2) + "\n")
        raise
    finally:
        # Do not upload or retain original DLLs, patches or reconstructions.
        shutil.rmtree(private, ignore_errors=False)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    a = parser.parse_args()
    run(Path.cwd(), a.output_dir, a.run_id)


if __name__ == "__main__":
    main()
