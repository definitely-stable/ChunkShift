#!/usr/bin/env python3
"""G1-J1 hosted-only independent fixture builder -> fresh consumer proof.

All Core reader variants are compiled in detached worktrees; this is a
non-decision isolation/correctness smoke, NOT a throughput experiment.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


g1i = load_module("core011_g1i", HERE / "core011_csm_g1i_caps.py")
j = load_module("core011_g1j_contract", HERE / "core011_csm_g1j_contract.py")
VARIANTS = ("eager", "lazy", "hybrid1", "hybrid2", "hybrid4")
READER = Path("src/ChunkShift/Manifest/CsmReader.cs")
SOURCE_SHA = g1i.ORIGINAL_SOURCE_SHA


def sha(data):
    return hashlib.sha256(data).hexdigest()


def candidate_source(original, variant):
    if sha(original.encode()) != SOURCE_SHA:
        raise ValueError("frozen CSM production reader SHA drift")
    if variant == "lazy":
        return g1i.h.g1g.variant_source(original, "lazy")
    if variant in ("eager", "hybrid1", "hybrid2", "hybrid4"):
        return g1i.source_variant(original, variant)
    raise ValueError("unregistered G1-J1 candidate")


def execute(command, cwd, timeout=1200, env=None):
    subprocess.run([str(x) for x in command], cwd=cwd, timeout=timeout,
                   check=True, env=env)


def build_variants(repo, worktree_root, output, checkout):
    sources = set()
    variants = {}
    for variant in VARIANTS:
        worktree = worktree_root / ("g1j1-" + variant)
        if worktree.exists():
            raise ValueError("existing worktree, fail-closed")
        execute(["git", "worktree", "add", "--detach", worktree, checkout], repo, 90)
        actual_checkout = subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=worktree, text=True).strip()
        if actual_checkout != checkout:
            raise ValueError("variant checkout identity drift")
        reader = worktree / READER
        original = reader.read_text(encoding="utf-8")
        transformed = candidate_source(original, variant)
        source_digest = sha(transformed.encode())
        if source_digest in sources:
            raise ValueError("duplicate candidate reader source")
        sources.add(source_digest)
        if variant != "eager":
            reader.write_text(transformed, encoding="utf-8", newline="\n")
        folder = output / variant
        folder.mkdir(parents=True)
        (folder / "build.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j1-build.v1",
            "variant": variant, "checkoutSha": checkout,
            "originalReaderSha256": SOURCE_SHA,
            "sourceSha256": source_digest,
            "sourceScope": "uncommitted temporary detached Git worktree",
        }, indent=2) + "\n")
        execute(["dotnet", "restore", "benchmarks/ChunkShift.Benchmarks.slnx"], worktree)
        execute(["dotnet", "build", "benchmarks/ChunkShift.Benchmarks.slnx",
                 "-c", "Release", "--no-restore"], worktree)
        # The actual rewritten Core build runs existing CSM, malformed,
        # cancellation and reader semantics tests, on both supported TFMs.
        execute(["dotnet", "test", "tests/ChunkShift.Tests/ChunkShift.Tests.csproj",
                 "-c", "Release", "--filter",
                 "FullyQualifiedName~Csm|FullyQualifiedName~ManifestReader|FullyQualifiedName~ManifestCancellation"],
                worktree)
        variants[variant] = worktree
    return variants


def command(worktree, mode, *args):
    return ["dotnet", "run", "--project",
            "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
            "-c", "Release", "--no-build", "--", mode, *args]


def evaluate(output, run_id, checkout, expected_size, workers):
    descriptor_file = output / "fixture" / "fixture.json"
    descriptor = json.loads(descriptor_file.read_text())
    physical = j.validate_manifest_only_fixture(descriptor, output / "fixture")
    if (descriptor.get("runId") != run_id or
            descriptor.get("verdict") != "NO_VERDICT_EXPLORATORY" or
            descriptor.get("builderCheckoutSha") != checkout or
            descriptor.get("contentBytes") != expected_size * 1024 * 1024 or
            descriptor.get("builderPid", 0) <= 0 or
            (expected_size == 384 and descriptor.get("cblkCount", 0) < 2)):
        raise ValueError("invalid source-free builder/CBLK oracle")
    for name in ("manifestId", "fileDigest", "sourceSha256"):
        hex_value = descriptor.get(name)
        if (not isinstance(hex_value, str) or len(hex_value) != 64 or
                any(ch not in "0123456789abcdef" for ch in hex_value)):
            raise ValueError("non-canonical logical/physical digest: " + name)
    if expected_size == 384 and (
            descriptor.get("manifestSha256") != g1i.h.g1g.MANIFEST_384_SHA or
            descriptor.get("chunkCount") != 5052 or
            descriptor.get("manifestBytes") != 182366):
        raise ValueError("known G1-I 384-MiB physical CSM oracle drift")
    if (output / "fixture" / "source.bin").exists():
        raise ValueError("builder left original content for consumers")

    results = []
    signatures = set()
    consumer_pids = set()
    for variant in VARIANTS:
        build = json.loads((output / variant / "build.json").read_text())
        if (build.get("schema") != "chunkshift.core011.g1j1-build.v1" or
                build.get("variant") != variant or
                build.get("checkoutSha") != checkout or
                build.get("originalReaderSha256") != SOURCE_SHA or
                build.get("sourceScope") != "uncommitted temporary detached Git worktree"):
            raise ValueError("wrong candidate source build")
        digest = build.get("sourceSha256")
        if not isinstance(digest, str) or len(digest) != 64:
            raise ValueError("missing candidate source digest")
        signatures.add(digest)
        file = output / variant / "isolated.json"
        obj = json.loads(file.read_text())
        pid = obj.get("consumerPid")
        if (obj.get("schema") != "chunkshift.core011.g1j1-isolated-consumer.v1" or
                obj.get("experimentId") != "CORE011-CSM-001" or
                obj.get("runId") != run_id or
                obj.get("verdict") != "NO_VERDICT_EXPLORATORY" or
                obj.get("checkoutSha") != checkout or
                obj.get("fixtureCsmSha256") != physical["manifestSha256"] or
                obj.get("fixtureSourceSha256") != descriptor["sourceSha256"] or
                obj.get("manifestBytes") != physical["manifestBytes"] or
                obj.get("chunkCount") != descriptor["chunkCount"] or
                obj.get("cblkCount") != descriptor["cblkCount"] or
                obj.get("contentBytes") != descriptor["contentBytes"] or
                obj.get("workers") != workers or
                obj.get("rounds") != 2 or
                obj.get("repetitions") != 2 or
                obj.get("consumerHasSourceFile") is not False or
                obj.get("consumerGeneratedContent") is not False or
                type(pid) is not int or pid <= 0 or
                obj.get("fixtureBuilderPid") != descriptor["builderPid"] or
                pid == descriptor["builderPid"] or
                pid in consumer_pids):
            raise ValueError("consumer process/source isolation identity mismatch")
        consumer_pids.add(pid)
        samples = obj.get("samples")
        if not isinstance(samples, list) or len(samples) != 2:
            raise ValueError("missing consumer rounds")
        for round_no, sample in enumerate(samples, 1):
            if (sample.get("round") != round_no or
                    sample.get("completedOperations") != workers * 2 or
                    sample.get("validatedChunks") != workers * 2 * descriptor["chunkCount"] or
                    not 1 <= sample.get("maxActiveOperations", 0) <= workers or
                    not sample.get("batchWallMilliseconds", 0) > 0 or
                    sample.get("allocatedBytes", -1) < 0 or
                    sample.get("gcPauseMilliseconds", -1) < 0):
                raise ValueError("invalid round or public CSM oracle")
        results.append({"variant": variant, "consumerPid": pid, "sourceSha256": digest,
                        "rawSha256": sha(file.read_bytes())})
    if len(signatures) != 5 or len(consumer_pids) != 5:
        raise ValueError("not five independently compiled/source-free consumers")
    return {
        "schema": "chunkshift.core011.g1j1-isolation-summary.v1",
        "runId": run_id, "checkoutSha": checkout,
        "fixture": physical, "builderPid": descriptor["builderPid"],
        "variants": results, "caseCount": 5, "roundCount": 10,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "limitations": [
            "Five fresh consumers and separate builder are verified in this smoke.",
            "Not a production CPU/RSS comparison, no real versioned corpus.",
            "No G1-J final threshold/calibration or adoption conclusion."
        ],
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output-dir", type=Path, required=True)
    p.add_argument("--worktree-root", type=Path, required=True)
    p.add_argument("--run-id", required=True)
    p.add_argument("--size-mib", type=int, choices=(1, 8, 384), default=384)
    p.add_argument("--workers", type=int, choices=(1, 4, 16), default=4)
    p.add_argument("--evaluate-only", action="store_true")
    args = p.parse_args()
    if not args.run_id.startswith("CORE011-CSM-001/G1J1-"):
        p.error("wrong experiment RunId")
    repo = Path.cwd()
    checkout = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    if not args.evaluate_only:
        if not os.environ.get("GITHUB_ACTIONS") == "true":
            raise ValueError("resource-consuming G1-J1 execution is GitHub-hosted only")
        if os.environ.get("GITHUB_SHA") != checkout:
            raise ValueError("GITHUB_SHA must match exact checked-out synthetic PR SHA")
        if args.output_dir.exists() or args.worktree_root.exists():
            raise ValueError("refuse reused output/worktrees")
        args.output_dir.mkdir(parents=True)
        args.worktree_root.mkdir(parents=True)
        variants = build_variants(repo, args.worktree_root, args.output_dir, checkout)
        fixture_dir = args.output_dir / "fixture"
        fixture_dir.mkdir()
        execute(command(variants["eager"], "core011-g1j1-build",
                        "--output-dir", str(fixture_dir),
                        "--size-mib", str(args.size_mib),
                        "--suite", "blake3", "--bidx", "true",
                        "--run-id", args.run_id), variants["eager"], 900)
        descriptor = json.loads((fixture_dir / "fixture.json").read_text())
        j.validate_manifest_only_fixture(descriptor, fixture_dir)
        for variant, worktree in variants.items():
            execute(command(worktree, "core011-g1j1-consume",
                            "--fixture-dir", str(fixture_dir),
                            "--workers", str(args.workers),
                            "--rounds", "2", "--repetitions", "2",
                            "--run-id", args.run_id,
                            "--output", str(args.output_dir / variant / "isolated.json")),
                    worktree, 180)
    summary = evaluate(args.output_dir, args.run_id, checkout,
                       args.size_mib, args.workers)
    (args.output_dir / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print("G1-J1 PASS: 1 builder, 5 fresh source-free consumers, 10 rounds; NO_VERDICT")


if __name__ == "__main__":
    main()
