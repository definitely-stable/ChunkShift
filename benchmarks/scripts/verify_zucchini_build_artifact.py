#!/usr/bin/env python3
"""Independently validate the origin and bytes of a frozen Zucchini build artifact.

No GitHub API credentials or network in this verifier: workflow downloads
immutable REST snapshots, this verifier rejects untrusted/incomplete claims.
A PR contract test uses fixtures only and MUST NOT be reported as a built tool.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

PIN = Path("docs/research/results/data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json")
WORKFLOW = ".github/workflows/patch-gap-001-g4-zucchini-hosted-build.yml"
NAME = "PATCH-GAP-001 Zucchini pinned hosted build"
BUILD_JOB = "source-pinned Zucchini binary (explicit larger hosted runner only)"
MANIFEST_SCHEMA = "chunkshift.patch-gap-g4-zucchini-build.v1"
EXACT_GN = {"is_debug": "false", "is_component_build": "false", "symbol_level": "0"}
DEPOT_SHA = "ce92a2e156beaaf1f2ed3a651a5c96f55bb78b80"


def require(value: bool, reason: str) -> None:
    if not value:
        raise ValueError("Zucchini build artifact rejected: " + reason)


def read(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def exact_gn_args(path: Path) -> None:
    require(path.is_file(), "missing original GN args.gn")
    parsed = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        match = re.fullmatch(r"([a-z_]+)\s*=\s*(false|true|[0-9]+)", line)
        require(match is not None, "unexpected GN flags")
        require(match[1] not in parsed, "duplicate GN flags")
        parsed[match[1]] = match[2]
    require(parsed == EXACT_GN, "wrong original GN flags")


def verify(run: dict, jobs: dict, artifacts: dict, root: Path, pin: dict,
           expected_run_id: int) -> dict:
    require(isinstance(expected_run_id, int) and expected_run_id > 0, "invalid requested run ID")
    require(run.get("id") == expected_run_id, "run ID changed")
    require(run.get("repository", {}).get("full_name") == "definitely-stable/ChunkShift",
            "wrong build repository")
    require(run.get("event") == "workflow_dispatch" and run.get("head_branch") == "main",
            "not a main-branch explicit build dispatch")
    require(run.get("status") == "completed" and run.get("conclusion") == "success",
            "build workflow did not succeed")
    require(run.get("run_attempt") == 1, "rerun is not admissible")
    require(run.get("name") == NAME, "unexpected build workflow name")
    require(run.get("path", "").split("@")[0] == WORKFLOW, "wrong workflow file")
    checkout = run.get("head_sha")
    require(isinstance(checkout, str) and bool(re.fullmatch(r"[0-9a-f]{40}", checkout)),
            "invalid build checkout SHA")

    matching = [j for j in jobs.get("jobs", []) if j.get("name") == BUILD_JOB]
    require(len(matching) == 1 and matching[0].get("conclusion") == "success"
            and matching[0].get("status") == "completed", "real binary build job did not succeed")
    require(matching[0].get("runner_name") and matching[0].get("runner_name") != "None",
            "missing runner attribution")
    artifact_name = "patch-gap-001-g4-zucchini-pinned-binary-" + checkout
    matches = [a for a in artifacts.get("artifacts", []) if a.get("name") == artifact_name]
    require(len(matches) == 1, "expected exactly one correctly named build artifact")
    artifact = matches[0]
    require(artifact.get("expired") is False, "build artifact expired")
    require(artifact.get("workflow_run", {}).get("id") == expected_run_id and
            artifact.get("workflow_run", {}).get("head_sha") == checkout,
            "artifact/run source identity differs")
    require(isinstance(artifact.get("digest"), str) and
            re.fullmatch(r"sha256:[0-9a-f]{64}", artifact["digest"]) is not None,
            "artifact archive SHA-256 digest missing")
    require(isinstance(artifact.get("id"), int) and artifact["id"] > 0,
            "invalid build artifact ID")

    manifest = read(root / "tool-manifest.json")
    require(manifest.get("schema") == MANIFEST_SCHEMA, "wrong binary manifest schema")
    require(pin["componentMirrorCommit"] == manifest.get("chromiumComponentCommit"),
            "wrong frozen upstream Zucchini revision")
    require(pin["sourceCommit"] == manifest.get("chromiumSourceCommit"),
            "wrong Chromium checkout SHA")
    sources = manifest.get("sourceObjects")
    require(isinstance(sources, dict), "missing source provenance objects")
    for key in ("sourceCommit", "sourceTreeSha", "componentsTreeSha",
                "componentTreeSha", "chromiumDepsBlobSha"):
        require(sources.get(key) == pin[key], "source object mismatch: " + key)
    require(sources.get("depotToolsCommit") == DEPOT_SHA, "unpinned depot_tools revision")
    require(manifest.get("gnArgs") == "is_debug=false is_component_build=false symbol_level=0",
            "GN flags claim differs from frozen plan")
    require(manifest.get("buildCommand") ==
            "autoninja -C out/Zucchini components/zucchini:zucchini",
            "unexpected upstream target or build command")
    exact_gn_args(root / "args.gn")
    require(manifest.get("gnArgsFileSha256") == sha(root / "args.gn"),
            "compiled GN args are not archived unchanged")

    require(isinstance(manifest.get("compilerBinarySha256"), str) and
            bool(re.fullmatch(r"[0-9a-f]{64}", manifest["compilerBinarySha256"])),
            "missing actual compiler SHA-256")
    require(isinstance(manifest.get("compilerIdentity"), str) and
            bool(manifest["compilerIdentity"]), "missing compiler version")
    require(isinstance(manifest.get("buildSystemIdentity"), str) and
            bool(manifest["buildSystemIdentity"]), "missing GN/Ninja versions")

    deps = root / "deps-revisions.txt"
    require(deps.is_file() and deps.stat().st_size > 0, "missing resolved DEPS snapshot")
    require(manifest.get("dependencySnapshotSha256") == sha(deps),
            "DEPS snapshot SHA-256 mismatch")
    binary = root / "zucchini"
    require(binary.is_file() and binary.stat().st_size > 0, "missing actual executable")
    require(manifest.get("binarySha256") == sha(binary) and
            manifest.get("binaryBytes") == binary.stat().st_size,
            "actual executable hash/length mismatch")
    resource = read(root / "resource-preflight.json")
    require(resource.get("schema") == "chunkshift.patch-gap-g4-zucchini-hosted-preflight.v1"
            and resource.get("canBuild") is True and resource.get("diskPass") is True
            and resource.get("ramPass") is True
            and resource.get("requiredDiskGiB", 0) >= 100
            and resource.get("requiredMemoryGiB", 0) >= 8, "build resources were not verified")
    return {
        "schema": "chunkshift.patch-gap-g4-zucchini-artifact-origin.v1",
        "status": "REAL_BUILT_BINARY_VERIFIED",
        "referenceStatus": "NOT_RUN",
        "buildRunId": expected_run_id,
        "buildHeadSha": checkout,
        "buildArtifactId": artifact["id"],
        "buildArtifactDigest": artifact["digest"],
        "binarySha256": manifest["binarySha256"],
        "chromiumSourceCommit": pin["sourceCommit"],
        "zucchiniComponentCommit": pin["componentMirrorCommit"],
        "boundary": "verified binary only; not a calibration/evaluation decision",
    }


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--run-json", type=Path, required=True)
    p.add_argument("--jobs-json", type=Path, required=True)
    p.add_argument("--artifacts-json", type=Path, required=True)
    p.add_argument("--artifact-dir", type=Path, required=True)
    p.add_argument("--run-id", type=int, required=True)
    p.add_argument("--pin", type=Path, default=PIN)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    out = verify(read(args.run_json), read(args.jobs_json),
                 read(args.artifacts_json), args.artifact_dir,
                 read(args.pin), args.run_id)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(out, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(out, sort_keys=True))


if __name__ == "__main__":
    main()
