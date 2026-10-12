#!/usr/bin/env python3
"""G1-G controlled source-variant public CSM/Plan A/B.

All production-source edits happen ONLY in disposable Git worktrees on a
GitHub-hosted runner. Committed source, public API, and CSM/CSP bytes are intact.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import statistics
import subprocess

VARIANTS = ("eager", "lazy", "pool4")
READER = Path("src/ChunkShift/Manifest/CsmReader.cs")
OLD_BUFFER = """    private readonly byte[] _blockBuffer =
        new byte[
            CsmFormat.SectionHeaderSize
            + MaximumCblkPayloadSize];"""
NEW_LENGTH = """        int recordLength =
            CsmFormat.SectionHeaderSize + payloadLength;"""
DISPOSE_ANCHOR = """        _disposed = true;
        _manifestId.Dispose();
        _input.Dispose();"""
ROUNDS = 5
PLAN_REPS = 8
VERIFY_REPS = 16
PLAN_SIZES = (1, 8)
VERIFY_CELLS = ((1, 1), (384, 1), (384, 4), (384, 16))
MANIFEST_384_SHA = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def variant_source(original: str, variant: str) -> str:
    if variant not in VARIANTS:
        raise ValueError("unknown variant")
    if original.count(OLD_BUFFER) != 1:
        raise ValueError("source layout drift: eager array declaration")
    if original.count(NEW_LENGTH) != 1 or original.count(DISPOSE_ANCHOR) != 1:
        raise ValueError("source layout drift: validation/dispose anchors")
    if variant == "eager":
        return original
    if variant == "lazy":
        replacement = "    private byte[] _blockBuffer = [];"
        modified = original.replace(OLD_BUFFER, replacement, 1)
        modified = modified.replace(
            NEW_LENGTH,
            NEW_LENGTH + """
        if (_blockBuffer.Length < recordLength)
        {
            _blockBuffer = new byte[recordLength];
        }""", 1)
        return modified
    # Explicit bounded per-process pool of up to 4 retained buckets.
    # Rent/Return only in a disposable lab build, never in repository src/.
    modified = original.replace("using System.Buffers.Binary;",
                                "using System.Buffers;\nusing System.Buffers.Binary;", 1)
    modified = modified.replace(
        OLD_BUFFER,
        """    private static readonly ArrayPool<byte> G1gPool =
        ArrayPool<byte>.Create(
            maxArrayLength: CsmFormat.SectionHeaderSize + MaximumCblkPayloadSize,
            maxArraysPerBucket: 4);
    private readonly byte[] _blockBuffer =
        G1gPool.Rent(CsmFormat.SectionHeaderSize + MaximumCblkPayloadSize);""", 1)
    modified = modified.replace(
        DISPOSE_ANCHOR,
        DISPOSE_ANCHOR + """
        G1gPool.Return(_blockBuffer, clearArray: true);""", 1)
    return modified


def call(cmd, cwd: Path, timeout=900):
    print("$ " + " ".join(map(str, cmd)), flush=True)
    subprocess.run([str(x) for x in cmd], cwd=cwd, check=True, timeout=timeout)


def make_worktree(repo: Path, dest: Path, variant: str):
    if dest.exists():
        raise RuntimeError("variant worktree already exists: " + str(dest))
    call(["git", "worktree", "add", "--detach", str(dest), "HEAD"], repo, timeout=120)
    src = dest / READER
    original = src.read_text(encoding="utf-8")
    modified = variant_source(original, variant)
    if variant != "eager" and modified == original:
        raise RuntimeError("variant yielded unchanged source")
    if variant != "eager":
        src.write_text(modified, encoding="utf-8", newline="\n")
    return sha(original.encode()), sha(modified.encode())


def run_case(worktree: Path, out: Path, variant: str, kind: str, size: int, workers: int, run_id: str):
    out.parent.mkdir(parents=True, exist_ok=True)
    base = [
        "dotnet", "run", "--project",
        "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
        "-c", "Release", "--no-build", "--",
    ]
    # Reuse existing frozen G1-F/G1-E harnesses with their required schema
    # prefixes; the parent G1-G RunId remains the common aggregation identity.
    child_run_id = run_id.replace("/G1G-", "/G1F-" if kind == "plan" else "/G1E-", 1)
    if kind == "plan":
        cmd = base + [
            "core011-csm-product", "--operation", "plan",
            "--size-mib", str(size), "--rounds", str(ROUNDS),
            "--repetitions", str(PLAN_REPS), "--run-id", child_run_id,
            "--output", str(out)
        ]
    else:
        cmd = base + [
            "core011-csm-multiblock", "--operation", "manifest-verify",
            "--suite", "blake3", "--bidx", "true", "--size-mib", str(size),
            "--min-chunks", str(4097 if size == 384 else 1),
            "--workers", str(workers), "--rounds", str(ROUNDS),
            "--repetitions", str(VERIFY_REPS), "--run-id", child_run_id,
            "--output", str(out)
        ]
    print(f"G1-G: {variant} {kind} {size}MiB workers={workers}", flush=True)
    call(cmd, worktree, timeout=650)


def produce(repo: Path, destination: Path, worktree_root: Path, run_id: str):
    if not run_id.startswith("CORE011-CSM-001/G1G-"):
        raise ValueError("bad G1-G RunId")
    destination.mkdir(parents=True, exist_ok=True)
    worktree_root.mkdir(parents=True, exist_ok=True)
    for variant in VARIANTS:
        out = destination / variant
        out.mkdir(parents=True, exist_ok=True)
        worktree = worktree_root / ("core011-g1g-" + variant)
        source, resulting = make_worktree(repo, worktree, variant)
        (out / "build.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1g-build.v1",
            "variant": variant,
            "sourceBaseSha256": source,
            "sourceVariantSha256": resulting,
            "headCommit": subprocess.check_output(
                ["git", "rev-parse", "HEAD"], cwd=worktree, text=True).strip(),
            "editScope": "temporary detached Git worktree; NEVER committed",
            "poolRetainedBucketsMax": 4 if variant == "pool4" else 0,
            "poolClearArrayOnReturn": True if variant == "pool4" else None,
        }, indent=2)+"\n", encoding="utf-8")
        call(["dotnet", "restore", "benchmarks/ChunkShift.Benchmarks.slnx"], worktree)
        call(["dotnet", "build", "benchmarks/ChunkShift.Benchmarks.slnx",
              "-c", "Release", "--no-restore"], worktree)
        # Genuine golden vectors, malformed CSM and public reader semantics
        # are tested against the temporary variant build, not baseline.
        call(["dotnet", "test", "tests/ChunkShift.Tests/ChunkShift.Tests.csproj",
              "-c", "Release",
              "--filter", "FullyQualifiedName~Csm|FullyQualifiedName~ManifestReader|FullyQualifiedName~ManifestCancellation"], worktree, timeout=600)
        for size in PLAN_SIZES:
            run_case(worktree, out / f"plan-{size}.json", variant,
                     "plan", size, 1, run_id)
        for size, workers in VERIFY_CELLS:
            run_case(worktree, out / f"verify-{size}-w{workers}.json",
                     variant, "verify", size, workers, run_id)
    (destination / "summary.json").write_text(
        json.dumps(evaluate(destination, run_id), indent=2)+"\n", encoding="utf-8")


def strict_number(value, name):
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
        raise ValueError("invalid " + name)
    return value


def evaluate(directory: Path, run_id: str):
    rows = []
    fixtures = {}
    base_hash = None
    variant_hashes = set()
    common_commit = None
    for variant in VARIANTS:
        out = directory / variant
        build = json.loads((out / "build.json").read_text())
        if (build.get("variant") != variant or
                build.get("schema") != "chunkshift.core011.g1g-build.v1" or
                build.get("editScope") != "temporary detached Git worktree; NEVER committed" or
                build.get("poolRetainedBucketsMax") != (4 if variant == "pool4" else 0)):
            raise ValueError("variant source provenance mismatch")
        if base_hash is None:
            base_hash = build["sourceBaseSha256"]
        elif base_hash != build["sourceBaseSha256"]:
            raise ValueError("different baseline CsmReader.cs")
        commit = build.get("headCommit")
        if common_commit is None: common_commit = commit
        if not commit or commit != common_commit:
            raise ValueError("mixed source checkout commits")
        source_hash = build["sourceVariantSha256"]
        if not isinstance(source_hash, str) or len(source_hash) != 64:
            raise ValueError("invalid variant source hash")
        variant_hashes.add(source_hash)
        if (variant == "eager") != (source_hash == base_hash):
            raise ValueError("variant is not source-distinct from eager")
        for kind, size, workers in [
                *[("plan", x, 1) for x in PLAN_SIZES],
                *[("verify", x, w) for x, w in VERIFY_CELLS]]:
            path = out / (
                f"plan-{size}.json" if kind == "plan"
                else f"verify-{size}-w{workers}.json")
            raw = path.read_bytes()
            d = json.loads(raw)
            child_run_id = run_id.replace(
                "/G1G-", "/G1F-" if kind == "plan" else "/G1E-", 1)
            if d.get("runId") != child_run_id or d.get("experimentId") != "CORE011-CSM-001" or (
                    d.get("verdict") != "NO_VERDICT_EXPLORATORY"):
                raise ValueError("wrong run/corpus verdict")
            c = d.get("conditions", {})
            f = d.get("fixture", {})
            if kind == "plan":
                if (d.get("schema") != "chunkshift.core011.csm-g1f-product-amdahl.v1" or
                        c.get("operation") != "plan" or c.get("sizeMiB") != size or
                        c.get("rounds") != ROUNDS or
                        c.get("repetitions") != PLAN_REPS or
                        c.get("workers") != 1 or
                        c.get("includeBlockIndex") is not True or
                        c.get("suite") != "blake3" or
                        f.get("reusedChunks", 0) < 1 or
                        f.get("missingChunks", 0) < 1 or
                        f.get("reusedChunks", 0) + f.get("missingChunks", 0) != f.get("targetChunks") or
                        d.get("estimatedPlanReaderBufferBudgetBytes") != 295056):
                    raise ValueError("bad Plan API workload/oracle")
                ident = {key: f[key] for key in (
                    "baseContentSha256", "targetContentSha256",
                    "baseCsmSha256", "targetCsmSha256",
                    "patchSha256", "targetChunks", "reusedChunks", "missingChunks")}
                samples = d["samples"]
                allocations = [x["processAllocatedBytes"]/PLAN_REPS for x in samples]
                wall = [x["batchWallMilliseconds"] for x in samples]
                maxops = PLAN_REPS
                gen2 = [x["gen2"] for x in samples]
                retained = d["postGcManagedBytes"]
                loh = d["postGcLohLastGcBytes"]
                sample_check = [(x.get("roundNumber") == i and
                                 x.get("completedOperations") == PLAN_REPS)
                                for i,x in enumerate(samples,1)]
            else:
                if (d.get("schema") != "chunkshift.core011.csm-multiblock-metadata.v1" or
                        c.get("operation") != "manifest-verify" or
                        c.get("workers") != workers or c.get("rounds") != ROUNDS or
                        c.get("repetitions") != VERIFY_REPS or
                        c.get("suite") != "blake3" or
                        c.get("includeBlockIndex") is not True or
                        f.get("sizeMiB") != size or
                        f.get("payloadCollectedBeforeMeasurement") is not True):
                    raise ValueError("bad source-free public verify workload")
                chunks = 5052 if size == 384 else 11
                if f.get("chunkCount") != chunks:
                    raise ValueError("physical chunk fixture drift")
                if size == 384 and (f.get("manifestSha256") != MANIFEST_384_SHA or
                                    f.get("manifestBytes") != 182366):
                    raise ValueError("multi-CBLK SHA drift")
                ident = {key: f[key] for key in (
                    "contentSha256", "manifestSha256", "manifestBytes",
                    "chunkCount", "minCblkBlocksBy4096EntryCap")}
                samples = d["samples"]
                allocations = [x["processAllocatedBytes"]/(workers*VERIFY_REPS)
                               for x in samples]
                wall = [x["wallMilliseconds"] for x in samples]
                maxops = workers*VERIFY_REPS
                gen2 = [x["gen2Collections"] for x in samples]
                retained = d["retention"]["managedBytesAfterExplicitGc"]
                loh = d["retention"]["lastGcLohSizeAfterBytes"]
                sample_check = [(x.get("round") == i and
                                 x.get("completedOperations") == maxops and
                                 x.get("validatedChunks") == maxops*chunks and
                                 1 <= x.get("maxActiveOperations", 0) <= workers)
                                for i,x in enumerate(samples,1)]
            if len(samples) != ROUNDS or not all(sample_check):
                raise ValueError("partial or missing public batch")
            for t in allocations + wall + gen2:
                strict_number(t, "measurement")
            if any(x <= 0 for x in wall):
                raise ValueError("nonpositive timed batch")
            strict_number(retained, "postGC retained heap")
            strict_number(loh, "postGC last LOH")
            key = (kind,size,workers)
            if key in fixtures and fixtures[key] != ident:
                raise ValueError("variant changed public CSM/CSP physical identity")
            fixtures[key] = ident
            rows.append({
                "variant": variant, "kind": kind, "sizeMiB": size,
                "workers": workers, "rounds": ROUNDS,
                "operationsPerRound": maxops,
                "allocPerCallMedianBytes": statistics.median(allocations),
                "batchMedianMs": statistics.median(wall),
                "gen2Total": sum(gen2),
                "postGcManagedBytes": retained,
                "postGcLastLohBytes": loh,
                "sourceVariantSha256": source_hash,
                "rawSha256": sha(raw),
                "samples": samples,
            })
    if len(variant_hashes) != len(VARIANTS) or len(rows) != len(VARIANTS)*6:
        raise ValueError("missing/identical variants or incomplete matrix")
    return {
        "schema": "chunkshift.core011.g1g-source-variant-ab.v1",
        "experimentId": "CORE011-CSM-001", "runId": run_id,
        "mainCheckoutCommit": common_commit,
        "sourceBaseSha256": base_hash,
        "matrixSize": len(rows), "roundCount": len(rows)*ROUNDS,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "limitations": [
            "Variants compiled independently in uncommitted temporary worktrees.",
            "This experiment cannot establish in-path CPU Amdahl by subtracting unrelated operations.",
            "Exact-sized lazy does not mean bounded retained memory across many readers.",
            "Pool with four retained buckets can raise idle LOH/RSS; clearArray costs CPU.",
            "Hosted CPU scheduling and full-GC process fixture history confound end-to-end time.",
            "No decision to alter product default/CSM v1/public API/NuGet.",
        ],
        "lanes": rows,
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output-dir", required=True, type=Path)
    p.add_argument("--run-id", required=True)
    p.add_argument("--worktree-root", type=Path)
    p.add_argument("--evaluate-only", action="store_true")
    args = p.parse_args()
    if not args.evaluate_only:
        if args.worktree_root is None:
            p.error("--worktree-root required for variant builds")
        produce(Path.cwd(), args.output_dir, args.worktree_root, args.run_id)
    else:
        args.output_dir.joinpath("summary.json").write_text(
            json.dumps(evaluate(args.output_dir, args.run_id), indent=2)+"\n")
    print("G1-G: 18 lanes, 90 rounds, exact physical identities; NO_VERDICT_EXPLORATORY")


if __name__ == "__main__":
    main()
