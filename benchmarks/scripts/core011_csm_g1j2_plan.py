#!/usr/bin/env python3
"""G1-J2 preregistration-only: real Git file revisions -> CSM -> isolated public Plan.

Does NOT run decision experiments, infer CPU performance or change production
CSM implementations.  Requires GitHub-hosted Actions for resourceful execution.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess

import core011_csm_g1j1_isolation as j1

BASE_COMMIT = "9e36b153c1863af359ea08410c59143654335148"
TARGET_COMMIT = "fffed6298fbd655a1900db2f1fe2e67027a932c5"
SOURCE_PATH = "docs/research/EXPERIMENT-INDEX.md"
RUN_PREFIX = "CORE011-CSM-001/G1J2-"
SCHEMA = "chunkshift.core011.g1j2-plan-pair.v1"
SAMPLE_SCHEMA = "chunkshift.core011.g1j2-plan-sample.v1"
VARIANTS = j1.VARIANTS


def sha(data):
    return hashlib.sha256(data).hexdigest()


def git(repo, *args):
    return subprocess.check_output(["git", *args], cwd=repo)


def source_bytes(repo, revision):
    if revision not in (BASE_COMMIT, TARGET_COMMIT):
        raise ValueError("unregistered source revision")
    content = git(repo, "show", f"{revision}:{SOURCE_PATH}")
    blob_id = git(repo, "rev-parse", f"{revision}:{SOURCE_PATH}").decode().strip()
    if len(blob_id) != 40:
        raise ValueError("unverified historical Git object")
    return content, blob_id


def prepare_sources(repo, fixture_dir, run_id):
    fixture_dir.mkdir()
    history = {}
    for name, commit in (("base", BASE_COMMIT), ("target", TARGET_COMMIT)):
        content, blob = source_bytes(repo, commit)
        (fixture_dir / (name + ".input")).write_bytes(content)
        history[name] = {
            "commit": commit, "blob": blob, "sha256": sha(content),
            "bytes": len(content),
        }
    if history["base"]["sha256"] == history["target"]["sha256"]:
        raise ValueError("Git source versions are identical")
    provenance = {
        "schema": "chunkshift.core011.g1j2-provenance.v1",
        "runId": run_id,
        "origin": "git-versioned-source",
        "repository": "https://github.com/definitely-stable/ChunkShift",
        "sourcePath": SOURCE_PATH,
        "base": history["base"], "target": history["target"],
        "corpusClassification": "real-git-text-versions; correctness-smoke-only",
        "representativeDotNetBinaryPair": False,
        "verdict": "NO_VERDICT_EXPLORATORY",
    }
    (fixture_dir / "provenance.json").write_text(
        json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    return provenance


def validate(output, run_id, checkout, repo):
    root = output / "fixture"
    provenance_file = root / "provenance.json"
    descriptor_file = root / "pair.json"
    prov = json.loads(provenance_file.read_text(encoding="utf-8"))
    desc = json.loads(descriptor_file.read_text(encoding="utf-8"))
    if (prov.get("schema") != "chunkshift.core011.g1j2-provenance.v1" or
            prov.get("origin") != "git-versioned-source" or
            prov.get("runId") != run_id or
            prov.get("sourcePath") != SOURCE_PATH or
            prov.get("representativeDotNetBinaryPair") is not False or
            desc.get("schema") != SCHEMA or
            desc.get("runId") != run_id or
            desc.get("verdict") != "NO_VERDICT_EXPLORATORY" or
            desc.get("builderCheckoutSha") != checkout or
            desc.get("origin") != prov["origin"] or
            desc.get("consumerSourceAccessAllowed") is not False or
            desc.get("provenanceSha256") != sha(provenance_file.read_bytes())):
        raise ValueError("G1-J2 pinned provenance or decision guard failure")
    builder = desc.get("builderPid")
    if type(builder) is not int or builder <= 0:
        raise ValueError("invalid external builder PID")

    fixture_shas = []
    b = desc.get("base")
    t = desc.get("target")
    if not isinstance(b, dict) or not isinstance(t, dict):
        raise ValueError("missing physical CSM pair")
    for name, data, revision in (
        ("base", b, BASE_COMMIT), ("target", t, TARGET_COMMIT)
    ):
        content, blob = source_bytes(repo, revision)
        expected = prov.get(name, {})
        physical = root / f"{name}.csm"
        if (expected.get("commit") != revision or
                expected.get("blob") != blob or
                expected.get("sha256") != sha(content) or
                expected.get("bytes") != len(content) or
                data.get("manifestFile") != f"{name}.csm" or
                data.get("sourceSha256") != sha(content) or
                data.get("sourceBytes") != len(content) or
                not physical.is_file() or physical.is_symlink() or
                data.get("csmSha256") != sha(physical.read_bytes()) or
                data.get("csmBytes") != physical.stat().st_size or
                type(data.get("chunkCount")) is not int or
                data["chunkCount"] < 1):
            raise ValueError("Git source or physical CSM digest mismatch: " + name)
        for field in ("manifestId", "fileDigest", "sourceSha256", "csmSha256"):
            digest = data.get(field)
            if (not isinstance(digest, str) or len(digest) != 64 or
                    any(c not in "0123456789abcdef" for c in digest)):
                raise ValueError("noncanonical " + field + " in " + name)
        fixture_shas.append(data["csmSha256"])
    if (root / "base.input").exists() or (root / "target.input").exists():
        raise ValueError("original source files leaked to consumer fixture")
    if fixture_shas[0] == fixture_shas[1]:
        raise ValueError("different source versions gave identical physical CSM")

    expected = desc.get("expectedPlan", {})
    if (type(expected.get("reusedChunks")) is not int or
            type(expected.get("missingChunks")) is not int or
            type(expected.get("reusedBytes")) is not int or
            type(expected.get("missingBytes")) is not int or
            expected["reusedChunks"] < 0 or expected["missingChunks"] < 0 or
            expected["reusedChunks"] + expected["missingChunks"] != t["chunkCount"] or
            expected["reusedBytes"] + expected["missingBytes"] != t["sourceBytes"]):
        raise ValueError("public Plan oracle violates target conservation")

    consumers = set()
    hashes = set()
    lanes = []
    for variant in VARIANTS:
        build = json.loads((output / variant / "build.json").read_text())
        raw = (output / variant / "plan.json").read_bytes()
        sample = json.loads(raw)
        sourcehash = build.get("sourceSha256")
        pid = sample.get("consumerPid")
        if (build.get("variant") != variant or
                build.get("schema") != "chunkshift.core011.g1j1-build.v1" or
                build.get("checkoutSha") != checkout or
                build.get("originalReaderSha256") != j1.SOURCE_SHA or
                build.get("sourceScope") != "uncommitted temporary detached Git worktree" or
                type(sourcehash) is not str or len(sourcehash) != 64 or
                sample.get("schema") != SAMPLE_SCHEMA or
                sample.get("experimentId") != "CORE011-CSM-001" or
                sample.get("runId") != run_id or
                sample.get("verdict") != "NO_VERDICT_EXPLORATORY" or
                sample.get("checkoutSha") != checkout or
                sample.get("builderPid") != builder or
                type(pid) is not int or pid <= 0 or pid == builder or pid in consumers or
                sample.get("sourceFilesAccessible") is not False or
                sample.get("baseCsmSha256") != fixture_shas[0] or
                sample.get("targetCsmSha256") != fixture_shas[1] or
                sample.get("reusedChunks") != expected["reusedChunks"] or
                sample.get("missingChunks") != expected["missingChunks"]):
            raise ValueError("source build/process/Plan sample identity mismatch")
        if sourcehash in hashes:
            raise ValueError("source strategies were not independently compiled")
        hashes.add(sourcehash)
        consumers.add(pid)
        samples = sample.get("samples")
        if not isinstance(samples, list) or len(samples) != 2:
            raise ValueError("partial or missing Plan sample rounds")
        for idx, s in enumerate(samples, 1):
            if (s.get("round") != idx or s.get("calls") != 4 or
                    any(type(s.get(key)) not in (int, float) or
                        not math.isfinite(s[key]) or s[key] < 0 for key in
                        ("allocatedBytes", "batchWallMilliseconds",
                         "batchCpuMilliseconds", "gcPauseMilliseconds", "gen2")) or
                    s["batchWallMilliseconds"] <= 0):
                raise ValueError("incomplete/nonfinite Plan sampling")
        lanes.append({"variant": variant, "pid": pid,
                      "sourceSha256": sourcehash,
                      "rawSha256": sha(raw)})
    if len(lanes) != 5 or len(consumers) != 5:
        raise ValueError("incomplete independent Plan CSM consumers")
    return {
        "schema": "chunkshift.core011.g1j2-plan-isolation-summary.v1",
        "runId": run_id, "checkoutSha": checkout,
        "fixtureSha256": sha(descriptor_file.read_bytes()),
        "provenanceSha256": sha(provenance_file.read_bytes()),
        "physicalCsmSha256": fixture_shas,
        "builderPid": builder,
        "consumers": lanes, "caseCount": 5, "roundCount": 10,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "note": "Small real Git versioned text pair. Not representative .NET binary performance calibration or ADOPT.",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--worktree-root", required=True, type=Path)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--evaluate-only", action="store_true")
    args = parser.parse_args()
    if not args.run_id.startswith(RUN_PREFIX):
        parser.error("invalid J2 RunId")
    repo = Path.cwd()
    checkout = git(repo, "rev-parse", "HEAD").decode().strip()
    if not args.evaluate_only:
        if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("GITHUB_SHA") != checkout:
            raise ValueError("J2 is GitHub-hosted only, with exact checkout SHA")
        if args.output_dir.exists() or args.worktree_root.exists():
            raise ValueError("J2 refuses reused output/worktree paths")
        args.output_dir.mkdir(parents=True)
        args.worktree_root.mkdir(parents=True)
        fixture = args.output_dir / "fixture"
        try:
            prepare_sources(repo, fixture, args.run_id)
            variants = j1.build_variants(repo, args.worktree_root,
                                          args.output_dir, checkout)
            j1.execute(j1.command(variants["eager"], "core011-g1j2-plan-build",
                                  "--fixture-dir", fixture, "--run-id", args.run_id),
                       variants["eager"], timeout=400)
            for variant, worktree in variants.items():
                j1.execute(j1.command(worktree, "core011-g1j2-plan-consume",
                                      "--fixture-dir", fixture, "--run-id", args.run_id,
                                      "--output", args.output_dir / variant / "plan.json"),
                           worktree, timeout=120)
        finally:
            for name in ("base.input", "target.input"):
                (fixture / name).unlink(missing_ok=True)
    summary = validate(args.output_dir, args.run_id, checkout, repo)
    (args.output_dir / "summary.json").write_text(
        json.dumps(summary, indent=2) + "\n")
    print("G1-J2 PASS: Git blob provenance + builder + 5 independent Plan consumers; NO_VERDICT")


if __name__ == "__main__":
    main()
