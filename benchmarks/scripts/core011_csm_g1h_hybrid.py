#!/usr/bin/env python3
"""G1-H: fail-closed hybrid CBLK buffer prototype + counterbalanced A/B/A pilot.

Candidate CsmReader.cs modifications exist exclusively in disposable worktrees.
All benchmark executions are in distinct dotnet processes on GitHub-hosted CI.
"""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import statistics
import subprocess

BASE = Path(__file__).with_name("core011_csm_g1g_ab.py")
_SPEC = importlib.util.spec_from_file_location("core011_g1g", BASE)
g1g = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(g1g)

VARIANTS = ("eager", "lazy", "pool4", "hybrid")
CASES = (("plan", 1, 1), ("plan", 8, 1),
         ("verify", 1, 1), ("verify", 384, 1),
         ("verify", 384, 4), ("verify", 384, 16))
SEQUENCES = (("eager", "hybrid", "eager"),
             ("hybrid", "eager", "hybrid"))
SMALL_LIMIT = 80 * 1024
TARGET_BUFFER_BYTES = 147500
ROUNDS = 5


def hybrid_source(original: str) -> str:
    """Only replace a precisely pinned allocation seam, never parsing semantics."""
    if (original.count(g1g.OLD_BUFFER) != 1 or
            original.count(g1g.NEW_LENGTH) != 1 or
            original.count(g1g.DISPOSE_ANCHOR) != 1 or
            original.count("using System.Buffers.Binary;") != 1):
        raise ValueError("G1-H fail-closed reader source anchor drift")
    result = original.replace(
        "using System.Buffers.Binary;",
        "using System.Buffers;\nusing System.Buffers.Binary;", 1)
    result = result.replace(g1g.OLD_BUFFER, """    // G1-H lab-only: initialize the large pool only after a large CBLK.
    private static class G1hLargePool
    {
        internal static readonly ArrayPool<byte> Instance = ArrayPool<byte>.Create(
            maxArrayLength: CsmFormat.SectionHeaderSize + MaximumCblkPayloadSize,
            maxArraysPerBucket: 4);
    }

    private const int G1hSmallCblkMaximum = 80 * 1024;
    private byte[] _blockBuffer = [];
    private bool _g1hPooled;""", 1)
    result = result.replace(g1g.NEW_LENGTH, g1g.NEW_LENGTH + """
        if (_blockBuffer.Length < recordLength)
        {
            if (recordLength <= G1hSmallCblkMaximum)
            {
                // Small-only readers never access the global LOH pool.
                _blockBuffer = new byte[recordLength];
            }
            else
            {
                // Pool is process-wide but bounded to four retained arrays
                // per bucket. Never expose incomplete/invalid block entries.
                byte[] rented = G1hLargePool.Instance.Rent(recordLength);
                if (_g1hPooled)
                {
                    G1hLargePool.Instance.Return(_blockBuffer, clearArray: true);
                }

                _blockBuffer = rented;
                _g1hPooled = true;
            }
        }""", 1)
    # Pool return before existing parser/hasher Dispose calls ensures
    # idempotent reclamation even if unrelated resources throw on Dispose.
    result = result.replace(g1g.DISPOSE_ANCHOR, """        _disposed = true;
        if (_g1hPooled)
        {
            G1hLargePool.Instance.Return(_blockBuffer, clearArray: true);
            _g1hPooled = false;
            _blockBuffer = [];
        }

        _manifestId.Dispose();
        _input.Dispose();""", 1)
    if "G1hLargePool.Instance.Rent(recordLength)" not in result:
        raise ValueError("Hybrid missing bounded rental")
    return result


def source_variant(original: str, variant: str) -> str:
    return hybrid_source(original) if variant == "hybrid" else g1g.variant_source(original, variant)


def command(cmd, cwd: Path, timeout=900):
    subprocess.run([str(x) for x in cmd], cwd=cwd, check=True, timeout=timeout)


def prepare_variant(repo: Path, worktree_root: Path, out: Path, variant: str):
    dest = worktree_root / ("core011-g1h-" + variant)
    if dest.exists():
        raise ValueError("temporary worktree already exists")
    command(["git", "worktree", "add", "--detach", dest, "HEAD"], repo, 120)
    reader = dest / g1g.READER
    original = reader.read_text(encoding="utf-8")
    modified = source_variant(original, variant)
    if variant != "eager" and original == modified:
        raise ValueError("variant identical to baseline")
    if variant != "eager":
        reader.write_text(modified, encoding="utf-8", newline="\n")
    data = {
        "schema": "chunkshift.core011.g1h-variant-build.v1",
        "variant": variant,
        "sourceBaseSha256": g1g.sha(original.encode()),
        "sourceVariantSha256": g1g.sha(modified.encode()),
        "headCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=dest, text=True).strip(),
        "sourceScope": "disposable uncommitted detached worktree",
        "smallThresholdBytes": SMALL_LIMIT if variant == "hybrid" else None,
        "largePoolRetainedPerBucket": 4 if variant in ("pool4", "hybrid") else 0,
        "largePoolClearsOnReturn": variant in ("pool4", "hybrid"),
    }
    out.mkdir(parents=True, exist_ok=True)
    (out/"build.json").write_text(json.dumps(data, indent=2)+"\n", encoding="utf-8")
    g1g.call(["dotnet", "restore", "benchmarks/ChunkShift.Benchmarks.slnx"], dest)
    g1g.call(["dotnet", "build", "benchmarks/ChunkShift.Benchmarks.slnx",
              "-c", "Release", "--no-restore"], dest)
    g1g.call(["dotnet", "test", "tests/ChunkShift.Tests/ChunkShift.Tests.csproj",
              "-c", "Release",
              "--filter", "FullyQualifiedName~Csm|FullyQualifiedName~ManifestReader|FullyQualifiedName~ManifestCancellation"], dest)
    return dest


def real_case(worktree: Path, file: Path, variant: str, kind: str,
              size: int, workers: int, parent_run_id: str):
    # Frozen G1-E/G1-F harnesses intentionally require these prefixes,
    # so G1-H is an outer-aggregation experiment with a G1G-shaped child.
    child = parent_run_id.replace("/G1H-", "/G1G-", 1)
    g1g.run_case(worktree, file, variant, kind, size, workers, child)


def full_matrix(repo: Path, output: Path, worktree_root: Path, run_id: str):
    if not run_id.startswith("CORE011-CSM-001/G1H-"):
        raise ValueError("wrong G1-H run prefix")
    output.mkdir(parents=True, exist_ok=True)
    worktree_root.mkdir(parents=True, exist_ok=True)
    builds = {}
    for v in VARIANTS:
        builds[v] = prepare_variant(repo, worktree_root, output/v, v)

    # Direct bucket length is queried from the real .NET 10 ArrayPool
    # implementation, not inferred from LOH memory after last GC.
    bucket_path = output/"pool-bucket.json"
    command(["dotnet", "run", "--project",
             "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
             "-c", "Release", "--no-build", "--",
             "core011-g1h-bucket", "--output", bucket_path], builds["eager"], 60)

    # Frozen variant order deliberately staggered by workload to reduce
    # warm-cache/order bias; still not equivalent to cross-host randomization.
    for i, (kind,size,workers) in enumerate(CASES):
        order = list(VARIANTS[i%len(VARIANTS):]) + list(VARIANTS[:i%len(VARIANTS)])
        for v in order:
            file = output/v/(
                f"plan-{size}.json" if kind == "plan"
                else f"verify-{size}-w{workers}.json")
            real_case(builds[v], file, v, kind, size, workers, run_id)

    # Two deliberate counterbalanced E-H-E / H-E-H sequences, each
    # sequence on both Plan workloads. Every position is a fresh process.
    # Neither is used for a one-run adoption verdict.
    for size in (1, 8):
        for j, seq in enumerate(SEQUENCES):
            for position,v in enumerate(seq):
                label = f"repeat-{size}-s{j}-p{position}"
                suffix_run_id = run_id + "-" + label
                real_case(builds[v], output/"repeats"/(label+"-"+v+".json"),
                          v, "plan", size, 1, suffix_run_id)

    summary = evaluate(output, run_id)
    (output/"summary.json").write_text(json.dumps(summary, indent=2)+"\n", encoding="utf-8")
    print(f"G1-H PASS: {summary['totalCases']} cases, "
          f"{summary['totalRounds']} rounds, no production changes")


def strict(value, name):
    if type(value) not in (float, int) or not math.isfinite(value) or value < 0:
        raise ValueError("invalid or nonfinite " + name)


def validate_one(d: dict, kind: str, size: int, workers: int, g1g_parent: str):
    child = g1g_parent.replace(
        "/G1G-", "/G1F-" if kind == "plan" else "/G1E-", 1)
    if d.get("runId") != child or d.get("experimentId") != "CORE011-CSM-001" or (
            d.get("verdict") != "NO_VERDICT_EXPLORATORY"):
        raise ValueError("invalid child RunId / premature verdict")
    c = d.get("conditions", {})
    f = d.get("fixture", {})
    samples = d.get("samples")
    if len(samples or []) != ROUNDS:
        raise ValueError("incomplete round count")
    if kind == "plan":
        if (d.get("schema") != "chunkshift.core011.csm-g1f-product-amdahl.v1" or
                c.get("sizeMiB") != size or c.get("operation") != "plan" or
                c.get("rounds") != 5 or c.get("repetitions") != 8 or
                c.get("workers") != 1 or c.get("suite") != "blake3" or
                c.get("includeBlockIndex") is not True or
                f.get("reusedChunks", 0) < 1 or f.get("missingChunks", 0) < 1 or
                f.get("reusedChunks", 0)+f.get("missingChunks", 0) != f.get("targetChunks")):
            raise ValueError("product Plan oracle/conditions changed")
        ident = {k: f[k] for k in (
            "baseContentSha256", "targetContentSha256",
            "baseCsmSha256", "targetCsmSha256", "patchSha256",
            "targetChunks", "reusedChunks", "missingChunks")}
        count = 8
        for i,s in enumerate(samples,1):
            if s.get("roundNumber") != i or s.get("completedOperations") != count:
                raise ValueError("incomplete Plan operation")
        allocation = [s["processAllocatedBytes"]/count for s in samples]
        walls = [s["batchWallMilliseconds"] for s in samples]
        cpus = [s["batchCpuMilliseconds"] for s in samples]
        gen2 = sum(s["gen2"] for s in samples)
        post = d["postGcManagedBytes"]
    else:
        if (d.get("schema") != "chunkshift.core011.csm-multiblock-metadata.v1" or
                c.get("operation") != "manifest-verify" or
                c.get("rounds") != 5 or c.get("repetitions") != 16 or
                c.get("workers") != workers or c.get("suite") != "blake3" or
                c.get("includeBlockIndex") is not True or
                f.get("sizeMiB") != size or
                f.get("payloadCollectedBeforeMeasurement") is not True or
                f.get("chunkCount") != (5052 if size == 384 else 11)):
            raise ValueError("source-free CSM oracle/conditions changed")
        if size == 384 and (f.get("manifestBytes") != 182366 or
                f.get("manifestSha256") != g1g.MANIFEST_384_SHA):
            raise ValueError("384MiB CSM SHA drift")
        ident = {k:f[k] for k in (
            "contentSha256", "manifestSha256", "manifestBytes",
            "chunkCount", "minCblkBlocksBy4096EntryCap")}
        count = workers*16
        for i,s in enumerate(samples,1):
            if (s.get("round") != i or s.get("completedOperations") != count or
                    s.get("validatedChunks") != count*f["chunkCount"] or
                    not 1 <= s.get("maxActiveOperations",0) <= workers):
                raise ValueError("incomplete source-free reader batch")
        allocation = [s["processAllocatedBytes"]/count for s in samples]
        walls = [s["wallMilliseconds"] for s in samples]
        cpus = [s["processCpuMilliseconds"] for s in samples]
        gen2 = sum(s["gen2Collections"] for s in samples)
        post = d["retention"]["managedBytesAfterExplicitGc"]
    for key,series in [("alloc",allocation),("wall",walls),("cpu",cpus)]:
        for v in series: strict(v,key)
    if any(w<=0 for w in walls):
        raise ValueError("invalid zero-time batch")
    strict(post,"retained")
    strict(gen2,"Gen2")
    return {
        "identity": ident,
        "allocPerCallMedian": statistics.median(allocation),
        "batchWallMedianMs": statistics.median(walls),
        "batchCpuMedianMs": statistics.median(cpus),
        "gen2Total": gen2,
        "postGcManagedBytes": post,
    }


def evaluate(out: Path, run_id: str):
    if not run_id.startswith("CORE011-CSM-001/G1H-"):
        raise ValueError("wrong aggregate RunId")
    buildinfo = {}
    head = baseline_sha = None
    distinct_sha = set()
    rows = []
    cross_ident = {}
    for v in VARIANTS:
        build = json.loads((out/v/"build.json").read_text())
        if (build.get("schema") != "chunkshift.core011.g1h-variant-build.v1" or
                build.get("variant") != v or
                build.get("sourceScope") != "disposable uncommitted detached worktree" or
                build.get("largePoolRetainedPerBucket") != (4 if v in ("pool4","hybrid") else 0) or
                build.get("largePoolClearsOnReturn") is not (v in ("pool4","hybrid")) or
                build.get("smallThresholdBytes") != (SMALL_LIMIT if v=="hybrid" else None)):
            raise ValueError("invalid source variant provenance")
        if head is None: head=build["headCommit"]
        if build.get("headCommit") != head or not head:
            raise ValueError("different variant checkout")
        if baseline_sha is None: baseline_sha=build["sourceBaseSha256"]
        if build["sourceBaseSha256"] != baseline_sha:
            raise ValueError("different baseline reader source")
        modified_sha=build["sourceVariantSha256"]
        if not isinstance(modified_sha,str) or len(modified_sha)!=64:
            raise ValueError("invalid source SHA")
        distinct_sha.add(modified_sha)
        if (v=="eager") is not (modified_sha==baseline_sha):
            raise ValueError("wrong source variant identity")
        buildinfo[v] = build
        for kind,size,workers in CASES:
            file = out/v/(f"plan-{size}.json" if kind=="plan" else f"verify-{size}-w{workers}.json")
            raw=file.read_bytes()
            d=json.loads(raw)
            result=validate_one(d,kind,size,workers,run_id.replace("/G1H-","/G1G-",1))
            key=(kind,size,workers)
            if key in cross_ident and cross_ident[key]!=result["identity"]:
                raise ValueError("CSM/CSP physical identity diverges between strategies")
            cross_ident[key]=result["identity"]
            rows.append({**{k:result[k] for k in result if k!="identity"},
                         "variant":v,"kind":kind,"sizeMiB":size,"workers":workers,
                         "rounds":ROUNDS,"rawSha256":g1g.sha(raw)})
    if len(distinct_sha)!=4 or len(rows)!=24:
        raise ValueError("missing or duplicate candidate strategies")
    bucket=json.loads((out/"pool-bucket.json").read_text())
    if (bucket.get("schema")!="chunkshift.core011.g1h-pool-probe.v1" or
            bucket.get("requestedBytes")!=TARGET_BUFFER_BYTES or
            bucket.get("retentionPerBucket")!=4 or
            bucket.get("rentedArrayLength",0)<TARGET_BUFFER_BYTES):
        raise ValueError("invalid direct runtime pool bucket probe")
    repeat=[]
    for size in (1,8):
        for sequence, seq in enumerate(SEQUENCES):
            for position,v in enumerate(seq):
                label=f"repeat-{size}-s{sequence}-p{position}"
                f=out/"repeats"/(label+"-"+v+".json")
                raw=f.read_bytes()
                d=json.loads(raw)
                synthetic=run_id+"-"+label
                result=validate_one(d,"plan",size,1,
                                    synthetic.replace("/G1H-","/G1G-",1))
                if result["identity"]!=cross_ident[("plan",size,1)]:
                    raise ValueError("A/B/A physical corpus identity drift")
                repeat.append({**{k:result[k] for k in result if k!="identity"},
                               "sizeMiB":size,"sequence":sequence,
                               "position":position,"variant":v,
                               "rawSha256":g1g.sha(raw)})
    return {
        "schema":"chunkshift.core011.g1h-hybrid-ab-v1",
        "experimentId":"CORE011-CSM-001",
        "runId":run_id,
        "checkoutSha":head,
        "baselineReaderSha256":baseline_sha,
        "builds":buildinfo,
        "poolBucketProbe":bucket,
        "matrixLanes":len(rows),
        "repeatPlanLanes":len(repeat),
        "totalCases":len(rows)+len(repeat),
        "totalRounds":(len(rows)+len(repeat))*ROUNDS,
        "verdict":"NO_VERDICT_EXPLORATORY",
        "limitations":[
            "Sequential process variants on one shared runner; two AB/A repetitions cannot support p95 or production adoption.",
            "Pool retained bucket probe is an independent allocator check, not direct allocation ownership attribution.",
            "Product Plan synthetic version pairs do not cover real-world file versions.",
            "Large source-free verify has max 16 workers and 4 visible CPUs; 64-way is deferred.",
            "All prototype source changes occur exclusively in disposable uncommitted worktrees.",
        ],
        "lanes":rows,
        "repeatPlan":repeat,
    }


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--output-dir",required=True,type=Path)
    p.add_argument("--worktree-root",type=Path)
    p.add_argument("--run-id",required=True)
    p.add_argument("--evaluate-only",action="store_true")
    a=p.parse_args()
    if a.evaluate_only:
        result=evaluate(a.output_dir,a.run_id)
        (a.output_dir/"summary.json").write_text(json.dumps(result,indent=2)+"\n")
    else:
        if a.worktree_root is None:p.error("worktree root required")
        full_matrix(Path.cwd(),a.output_dir,a.worktree_root,a.run_id)
    print("G1-H completed: 36 cases / 180 rounds; no adoption",flush=True)


if __name__=="__main__":main()
