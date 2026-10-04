#!/usr/bin/env python3
"""Evaluate the frozen PATCH-ENC-005 H6-O false-negative guard."""

from __future__ import annotations
import argparse, json, math, re
from pathlib import Path

EXP="PATCH-ENC-005"
PROTO="96fd9b296d6998cac397e61041f22df51e6dd43c"
DATA="8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
LANE="H6-O12-SF3-S128"
PLATFORM="linux-x64"
ROLE="evaluation"
RUN_RE=re.compile(r"^PATCH-ENC-005/RUN-(\d{8})-(\d{3})-([0-9a-f]{40})-linux-x64$")
CREATE_LIMIT=160*1024*1024
INDEX_LIMIT=64*1024*1024

def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))

def median5(values):
    if len(values)!=5:
        raise ValueError("guard requires exactly five wall ratios")
    return sorted(float(x) for x in values)[2]

def validate_memory(doc, lane, run_id, source):
    if doc.get("schema")!="chunkshift.patch-lab-memory.v1": raise ValueError(f"{lane}: memory schema")
    if doc.get("lane")!=lane or doc.get("runId")!=run_id: raise ValueError(f"{lane}: memory identity")
    if doc.get("corpusPairsSha256")!=DATA: raise ValueError(f"{lane}: memory dataset")
    if doc.get("execution")!="h2-w2": raise ValueError(f"{lane}: memory execution")
    if doc.get("population")!="max-base-target": raise ValueError(f"{lane}: memory population")
    if doc.get("applyCheck")!="boundary": raise ValueError(f"{lane}: memory apply check")
    env=doc.get("environment") or {}
    if str(env.get("gitCommit") or "").lower()!=source: raise ValueError(f"{lane}: memory source")
    files=doc.get("files")
    if not isinstance(files,list) or not files: raise ValueError(f"{lane}: empty memory population")
    return {
        (x["family"],x["base"],x["target"],x["path"]): x for x in files
    }, int(doc["idleBaselineBytes"])

def validate_correctness(paired, patch_maps):
    traces=paired.get("traceCorrectness")
    applies=paired.get("applyEvidence")
    if set(traces or {})!={"csp",LANE}: raise ValueError("guard trace lane set mismatch")
    if set(applies or {})!={"csp",LANE}: raise ValueError("guard apply lane set mismatch")
    for lane in ("csp",LANE):
        rows=traces[lane].get("correctness")
        if not isinstance(rows,list) or not rows: raise ValueError(f"{lane}: no decoder correctness")
        seen={}
        for row in rows:
            key=(row["family"],row["base"],row["target"],row["path"])
            if row.get("verdict")!="valid" or row.get("decoderOutputSha256")!=row.get("targetSha256"):
                raise ValueError(f"{lane}: decoder correctness failure")
            if row.get("patchSha256")!=patch_maps[lane].get(key):
                raise ValueError(f"{lane}: correctness patch SHA mismatch")
            seen[key]=row["patchSha256"]
        if seen!=patch_maps[lane]: raise ValueError(f"{lane}: correctness file set mismatch")
        app=applies[lane]
        if not isinstance(app.get("files"),list) or len(app["files"])!=len(patch_maps[lane]):
            raise ValueError(f"{lane}: apply evidence file set mismatch")

def patch_map(rows):
    out={}
    for r in rows:
        key=(r["family"],r["base"],r["target"],r["path"])
        if key in out: raise ValueError("duplicate patch SHA row")
        out[key]=r["patchSha256"]
    return out

def evaluate(paired, h0mem, h6mem):
    if paired.get("schema")!="chunkshift.patch-enc-005-paired.v2" or paired.get("status")!="VALID":
        raise ValueError("guard paired evidence is not VALID")
    for k,v in (("experimentId",EXP),("protocolCommit",PROTO),("platform",PLATFORM),
                ("datasetRole",ROLE),("datasetSha256",DATA)):
        if paired.get(k)!=v: raise ValueError(f"paired {k} mismatch")
    source=str(paired.get("sourceCommit") or "")
    m=RUN_RE.fullmatch(str(paired.get("runId") or ""))
    if m is None or m.group(3)!=source: raise ValueError("guard RunId/source mismatch")
    run_id=paired["runId"]

    maps_doc=paired.get("acceptedPatchShas")
    if set(maps_doc or {})!={"csp",LANE}: raise ValueError("accepted patch lane set mismatch")
    maps={lane:patch_map(rows) for lane,rows in maps_doc.items()}
    if set(maps["csp"])!=set(maps[LANE]): raise ValueError("patch file sets differ")
    validate_correctness(paired,maps)

    timing=paired.get("acceptedTiming") or {}
    rounds=timing.get("rounds")
    summaries=timing.get("summaries")
    if not isinstance(rounds,list) or len(rounds)!=5: raise ValueError("guard timing rounds mismatch")
    if not isinstance(summaries,list) or len(summaries)!=1 or summaries[0].get("lane")!=LANE:
        raise ValueError("guard timing summary mismatch")

    wall=[]
    h0bytes=set()
    h6bytes=set()
    for index,r in enumerate(rounds,1):
        if r.get("round")!=index or r.get("bracketNoisy") is not False:
            raise ValueError("guard accepted round is noisy/misordered")
        cands=r.get("candidates")
        if not isinstance(cands,list) or len(cands)!=1 or cands[0].get("lane")!=LANE:
            raise ValueError("guard candidate rotation mismatch")
        left=float(r["h0Start"]["wallSeconds"]); right=float(r["h0End"]["wallSeconds"])
        denom=(left+right)/2.0
        ratio=float(cands[0]["aggregate"]["wallSeconds"])/denom
        if not math.isclose(ratio,float(cands[0]["wallRatio"]),rel_tol=1e-12,abs_tol=1e-12):
            raise ValueError("guard wall ratio does not recompute")
        wall.append(ratio)
        h0bytes.add(int(r["h0Start"]["patchBytes"])); h0bytes.add(int(r["h0End"]["patchBytes"]))
        h6bytes.add(int(cands[0]["aggregate"]["patchBytes"]))
        agg=cands[0]["aggregate"]
        if int(agg.get("selectorFiles") or 0)<=0: raise ValueError("guard selector metrics absent")
        if int(agg.get("selectorIndexPeakBytes") or -1)>INDEX_LIMIT:
            raise ValueError("guard index logical memory bound exceeded")

    if len(h0bytes)!=1 or len(h6bytes)!=1: raise ValueError("guard patch bytes changed across rounds")
    h0=next(iter(h0bytes)); h6=next(iter(h6bytes))
    summary=summaries[0]
    med=median5(wall)
    if not math.isclose(med,float(summary["wallRatioMedian"]),rel_tol=1e-12,abs_tol=1e-12):
        raise ValueError("guard median does not recompute")
    wall_pass=sum(v<=1.50 for v in wall)
    bytes_ok=h6*100<=h0*97
    wall_ok=med<=1.50 and wall_pass>=4

    h0files,h0idle=validate_memory(h0mem,"csp",run_id,source)
    h6files,h6idle=validate_memory(h6mem,LANE,run_id,source)
    if set(h0files)!=set(h6files): raise ValueError("memory file sets differ")
    h6_over=max(max(0,int(x["createPeakBytes"])-h6idle) for x in h6files.values())
    h0_over=max(max(0,int(x["createPeakBytes"])-h0idle) for x in h0files.values())
    memory_ok=h6_over<=CREATE_LIMIT

    passed=bytes_ok and wall_ok and memory_ok
    return {
        "schema":"chunkshift.patch-enc-005-h6o-guard-verdict.v1",
        "experimentId":EXP,"runId":run_id,"protocolCommit":PROTO,"sourceCommit":source,
        "platform":PLATFORM,"datasetRole":ROLE,"datasetSha256":DATA,"lane":LANE,
        "status":"PASS" if passed else "MISS",
        "nextAction":"OPEN_FULL_PHASE_B" if passed else "STOP_RESEMBLANCE",
        "h0PatchBytes":h0,"h6PatchBytes":h6,"byteRatio":h6/h0,"bytesOk":bytes_ok,
        "wallRatios":wall,"wallRatioMedian":med,"wallPassCount":wall_pass,"wallOk":wall_ok,
        "h0CreatePeakOverIdleBytes":h0_over,"h6CreatePeakOverIdleBytes":h6_over,
        "createMemoryLimitBytes":CREATE_LIMIT,"memoryOk":memory_ok,
        "correctnessOk":True,"deterministicBytesOk":True,
    }

def summary(v):
    return (
        "# PATCH-ENC-005 H6-O guard verdict\n\n"
        f"- status: **{v['status']}**\n- next action: **{v['nextAction']}**\n"
        f"- bytes: {v['h6PatchBytes']:,} / {v['h0PatchBytes']:,} = {v['byteRatio']:.6f}\n"
        f"- wall median: {v['wallRatioMedian']:.6f}; pass rounds {v['wallPassCount']}/5\n"
        f"- H6 create peak over idle: {v['h6CreatePeakOverIdleBytes']:,} B / {v['createMemoryLimitBytes']:,} B\n"
        f"- gates: bytes={v['bytesOk']}, wall={v['wallOk']}, memory={v['memoryOk']}, correctness=true, deterministic=true\n"
    )

def main():
    ap=argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--paired",type=Path,required=True)
    ap.add_argument("--memory-h0",type=Path,required=True)
    ap.add_argument("--memory-h6",type=Path,required=True)
    ap.add_argument("--output",type=Path,required=True)
    a=ap.parse_args()
    try:
        v=evaluate(load(a.paired),load(a.memory_h0),load(a.memory_h6))
        a.output.mkdir(parents=True,exist_ok=True)
        (a.output/"verdict.json").write_text(json.dumps(v,indent=2,sort_keys=True)+"\n",encoding="utf-8")
        (a.output/"summary.md").write_text(summary(v),encoding="utf-8")
        print(json.dumps(v,sort_keys=True,separators=(",",":")))
        return 0
    except (OSError,ValueError,KeyError,TypeError,json.JSONDecodeError) as e:
        ap.error(str(e))

if __name__=="__main__":
    raise SystemExit(main())
