#!/usr/bin/env python3
"""CORE011-CSM-001 G1-I: source-hashed hybrid pool caps 1/2/4 vs eager.

Run each replicate on an independently allocated GitHub-hosted runner.
All Core source changes happen ONLY in disposable detached git worktrees.
"""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import statistics
import subprocess

BASE = Path(__file__).with_name("core011_csm_g1h_hybrid.py")
SPEC = importlib.util.spec_from_file_location("core011_csm_g1h", BASE)
h = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(h)

VARIANTS = ("eager", "hybrid1", "hybrid2", "hybrid4")
CAPACITY = {"eager": 0, "hybrid1": 1, "hybrid2": 2, "hybrid4": 4}
CASES = h.CASES
ROUNDS = 5
ORIGINAL_SOURCE_SHA = "a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
CAP_ANCHOR = "maxArraysPerBucket: 4);"


def ordered_variants(case_index: int, replicate: int):
    if not 0 <= case_index < len(CASES) or replicate not in (0,1):
        raise ValueError("invalid order index")
    rotation=(case_index+replicate)%4
    seq=list(VARIANTS[rotation:])+list(VARIANTS[:rotation])
    if replicate==1:
        seq.reverse()
    return seq


def source_variant(original: str, variant: str) -> str:
    if variant not in VARIANTS:
        raise ValueError("unknown G1-I strategy")
    if h.g1g.sha(original.encode()) != ORIGINAL_SOURCE_SHA:
        raise ValueError("frozen reader source SHA drift")
    if variant == "eager":
        return original
    modified = h.hybrid_source(original)
    if modified.count(CAP_ANCHOR) != 1:
        raise ValueError("hybrid pool capacity anchor drift")
    return modified.replace(CAP_ANCHOR, f"maxArraysPerBucket: {CAPACITY[variant]});", 1)


def build(repo: Path, root: Path, result: Path, variant: str) -> Path:
    worktree = root / ("core011-g1i-" + variant)
    if worktree.exists():
        raise ValueError("reused worktree")
    h.command(["git", "worktree", "add", "--detach", worktree, "HEAD"], repo, 120)
    reader = worktree / h.g1g.READER
    original = reader.read_text(encoding="utf-8")
    modified = source_variant(original, variant)
    if variant != "eager":
        reader.write_text(modified, encoding="utf-8", newline="\n")
    result.mkdir(parents=True, exist_ok=True)
    info = {
        "schema": "chunkshift.core011.g1i-build.v1",
        "variant": variant,
        "capacity": CAPACITY[variant],
        "originalSourceSha256": h.g1g.sha(original.encode()),
        "variantSourceSha256": h.g1g.sha(modified.encode()),
        "headCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=worktree, text=True).strip(),
        "sourceScope": "uncommitted disposable detached Git worktree",
        "smallBufferThresholdBytes": 80*1024 if variant != "eager" else None,
        "clearOnReturn": variant != "eager",
    }
    (result/"build.json").write_text(json.dumps(info, indent=2)+"\n")
    h.g1g.call(["dotnet","restore","benchmarks/ChunkShift.Benchmarks.slnx"], worktree)
    h.g1g.call(["dotnet","build","benchmarks/ChunkShift.Benchmarks.slnx",
                "-c","Release","--no-restore"], worktree)
    h.g1g.call(["dotnet","test","tests/ChunkShift.Tests/ChunkShift.Tests.csproj",
                "-c","Release","--filter",
                "FullyQualifiedName~Csm|FullyQualifiedName~ManifestReader|FullyQualifiedName~ManifestCancellation"],
               worktree,timeout=600)
    return worktree


def run(repo: Path, output: Path, root: Path, run_id: str, replicate: int):
    if not run_id.startswith("CORE011-CSM-001/G1I-") or replicate not in (0,1):
        raise ValueError("invalid G1-I run_id/replicate")
    output.mkdir(parents=True, exist_ok=True)
    root.mkdir(parents=True, exist_ok=True)
    builds = {}
    for v in VARIANTS:
        builds[v] = build(repo,root,output/v,v)

    # Direct probe (distinct small pool instance; no inference from GC).
    h.command(["dotnet","run","--project",
               "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
               "-c","Release","--no-build","--",
               "core011-g1i-buckets","--output",output/"pool-buckets.json"],
              builds["eager"],60)

    # Deterministic, balanced presentation order. Different hosts use
    # opposite rotations: no uncontrolled PRNG state or mutable fixture.
    order_by_case = {}
    for i,(kind,size,workers) in enumerate(CASES):
        seq=ordered_variants(i,replicate)
        label=f"{kind}-{size}-w{workers}"
        order_by_case[label]=seq
        for v in seq:
            case_file=output/v/(
                f"plan-{size}.json" if kind=="plan" else f"verify-{size}-w{workers}.json")
            # Frozen existing G1-H/G1-G/G1-E/G1-F harness and logical
            # validation; only Core CSM reader bytes differ in worktree.
            h.real_case(builds[v],case_file,v,kind,size,workers,
                        run_id.replace("/G1I-","/G1H-",1))

    (output/"order.json").write_text(json.dumps({
        "schema":"chunkshift.core011.g1i-order.v1",
        "runId":run_id,"replicate":replicate,"orderByCase":order_by_case,
    },indent=2)+"\n")
    summary=evaluate(output,run_id,replicate)
    summary["perCaseExecutionOrder"]=order_by_case
    (output/"summary.json").write_text(json.dumps(summary,indent=2)+"\n")
    print(f"G1-I PASS replicate={replicate} 24 cells / 120 rounds / "
          f"four source-verified strategies; NO_VERDICT", flush=True)


def require_number(value, key):
    if type(value) not in (int,float) or not math.isfinite(value) or value < 0:
        raise ValueError(f"invalid finite nonnegative {key}")


def evaluate(output: Path, run_id: str, replicate: int):
    if not run_id.startswith("CORE011-CSM-001/G1I-") or replicate not in (0,1):
        raise ValueError("invalid G1-I aggregate identity")
    order=json.loads((output/"order.json").read_text())
    expected_order={
        f"{kind}-{size}-w{workers}":ordered_variants(i,replicate)
        for i,(kind,size,workers) in enumerate(CASES)}
    if (order.get("schema")!="chunkshift.core011.g1i-order.v1" or
            order.get("runId")!=run_id or
            order.get("replicate")!=replicate or
            order.get("orderByCase")!=expected_order):
        raise ValueError("unfrozen experimental ordering")
    rows=[]
    refs={}
    sources=set()
    base=None
    checkout=None
    evidence_sha=None
    for v in VARIANTS:
        obj=json.loads((output/v/"build.json").read_text())
        if (obj.get("schema")!="chunkshift.core011.g1i-build.v1" or
                obj.get("variant")!=v or
                obj.get("capacity")!=CAPACITY[v] or
                obj.get("originalSourceSha256")!=ORIGINAL_SOURCE_SHA or
                obj.get("sourceScope")!="uncommitted disposable detached Git worktree" or
                obj.get("smallBufferThresholdBytes")!=(80*1024 if v!="eager" else None) or
                obj.get("clearOnReturn") is not (v!="eager")):
            raise ValueError("invalid source provenance")
        head=obj.get("headCommit")
        if not head or (checkout is not None and head!=checkout):
            raise ValueError("mixed source checkout")
        checkout=head
        sig=obj.get("variantSourceSha256")
        if not isinstance(sig,str) or len(sig)!=64:
            raise ValueError("malformed source fingerprint")
        if (v=="eager") is not (sig==ORIGINAL_SOURCE_SHA):
            raise ValueError("candidate source identical to eager")
        sources.add(sig)
        for kind,size,workers in CASES:
            filename=(f"plan-{size}.json" if kind=="plan" else f"verify-{size}-w{workers}.json")
            path=output/v/filename
            raw=path.read_bytes()
            d=json.loads(raw)
            if d.get("checkoutSha")!=checkout:
                raise ValueError("scenario checkout SHA differs from compiled source")
            label=(kind,size,workers)
            verified=h.validate_one(
                d,kind,size,workers,run_id.replace("/G1I-","/G1G-",1))
            if label in refs and verified["identity"]!=refs[label]:
                raise ValueError("different physical manifest/CSP corpus")
            refs[label]=verified["identity"]
            entry={key:value for key,value in verified.items() if key!="identity"}
            entry.update({
                "variant":v,"capacity":CAPACITY[v],
                "kind":kind,"sizeMiB":size,"workers":workers,
                "rounds":ROUNDS,"sourceSha256":sig,
                "rawSha256":h.g1g.sha(raw),
            })
            rows.append(entry)
    if len(rows)!=24 or len(sources)!=4:
        raise ValueError("missing/duplicate source variants or workload lanes")

    probe=json.loads((output/"pool-buckets.json").read_text())
    if (probe.get("schema")!="chunkshift.core011.g1i-bucket-probe.v1" or
            probe.get("requestedBytes")!=147500 or
            sorted(x["cap"] for x in probe.get("buckets",[]))!=[1,2,4]):
        raise ValueError("wrong direct .NET bucket probe")
    for x in probe["buckets"]:
        for key in ("returnedLength","theoreticalMaxCachedPayloadBytes"):
            require_number(x.get(key),key)
        if (x["returnedLength"]<147500 or
                x["theoreticalMaxCachedPayloadBytes"]!=x["cap"]*x["returnedLength"]):
            raise ValueError("pool capacity/cached-bytes mismatch")

    results={}
    for kind,size,workers in CASES:
        label=f"{kind}-{size}-w{workers}"
        matched=[r for r in rows if
                 (r["kind"],r["sizeMiB"],r["workers"])==(kind,size,workers)]
        baseline=next(r for r in matched if r["variant"]=="eager")
        results[label]={
            r["variant"]:{
                "allocatedBytesPerCall":r["allocPerCallMedian"],
                "allocationSavingPercent":
                    (1-r["allocPerCallMedian"]/baseline["allocPerCallMedian"])*100,
                "gen2Total":r["gen2Total"],
                "batchWallMedianMs":r["batchWallMedianMs"],
                "batchCpuMedianMs":r["batchCpuMedianMs"],
                "postGcManagedBytes":r["postGcManagedBytes"],
            } for r in matched
        }
    return {
        "schema":"chunkshift.core011.g1i-pool-capacity.v1",
        "experimentId":"CORE011-CSM-001",
        "runId":run_id,"replicate":replicate,
        "checkoutSha":checkout,"variants":list(VARIANTS),
        "caseCount":24,"roundCount":120,
        "verdict":"NO_VERDICT_EXPLORATORY",
        "bucketProbe":probe,"lanes":rows,
        "matchedComparisons":results,
        "limitations":[
            "Two hosted runner replicates: concurrency and CPU scheduling may differ across machines.",
            "Requested workers 16 is not 16 simultaneous CPU cores; collect maxActive from raw per-round data.",
            "Theoretical ArrayPool cap is per bucket, not an RSS bound; post-GC retained heap includes unrelated objects.",
            "Deterministic permutation by workload/replicate balances order but is not arbitrary randomized trial selection.",
            "No product adoption based only on allocations, per-process Gen2 or one/two timed runner comparisons.",
            "CPU Amdahl and representative real versioned file corpora remain unproven.",
            "Reader variants exist only in disposable uncommitted worktrees; product defaults unchanged."
        ],
    }


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output-dir",required=True,type=Path)
    parser.add_argument("--worktree-root",type=Path)
    parser.add_argument("--run-id",required=True)
    parser.add_argument("--replicate",type=int,required=True,choices=(0,1))
    parser.add_argument("--evaluate-only",action="store_true")
    a=parser.parse_args()
    if a.evaluate_only:
        summary=evaluate(a.output_dir,a.run_id,a.replicate)
        (a.output_dir/"summary.json").write_text(json.dumps(summary,indent=2)+"\n")
    else:
        if a.worktree_root is None:
            parser.error("missing --worktree-root")
        run(Path.cwd(),a.output_dir,a.worktree_root,a.run_id,a.replicate)


if __name__=="__main__":
    main()
