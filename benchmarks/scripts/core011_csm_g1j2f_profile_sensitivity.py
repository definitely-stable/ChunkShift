#!/usr/bin/env python3
"""G1-J2F strictly lab-only chunking profile sensitivity on frozen Roslyn pair."""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import subprocess

import core011_csm_g1j2d_forensic as d
import core011_csm_g1j2e_boundary as e

SCHEMA="chunkshift.core011.g1j2f-profile-sensitivity.v1"
LANES=("stable-fastcdc64k","lab-fastcdc16k","lab-fastcdc256k","lab-fixed16k","lab-fixed64k")
STABLE_CSM=e.CSM_SHA


def must(cond,msg):
    if not cond:
        raise ValueError(msg)


def sha256(raw):
    return hashlib.sha256(raw).hexdigest()


def validate_scanner(raw, tsv):
    result=[]
    pos=0
    for line in tsv.splitlines():
        pieces=line.split("\t")
        must(len(pieces)==3,"bad laboratory profile TSV field count")
        off,length=int(pieces[0]),int(pieces[1])
        cid=pieces[2]
        must(off==pos and length>0 and len(cid)==64 and
             all(x in "0123456789abcdef" for x in cid),
             "invalid profile chunk tuple")
        pos += length
        must(pos<=len(raw),"out-of-range source chunk")
        result.append({"offset":off,"length":length,"chunkId":cid})
    must(result and pos==len(raw),"laboratory profile must cover every byte")
    return result


def compare_lane(base,target,bc,tc):
    # Verify each reused ChunkId references EXACT same bytes, never rely only
    # on an opaque hash digest. The use of common BLAKE3 is enforced in C#.
    by_id={}
    for c in bc:
        by_id.setdefault(c["chunkId"],[]).append(c)
    reused=0
    reused_bytes=0
    duplicate_target=0
    unique=set()
    for c in tc:
        if c["chunkId"] in by_id:
            matches=by_id[c["chunkId"]]
            assert matches
            found=False
            for m in matches:
                must(m["length"]==c["length"],"ChunkId collision across different lengths")
                must(base[m["offset"]:m["offset"]+m["length"]]==
                     target[c["offset"]:c["offset"]+c["length"]],
                     "hash identity not verified by original bytes")
                found=True
            if found:
                reused+=1
                reused_bytes+=c["length"]
                if c["chunkId"] in unique:
                    duplicate_target+=1
                unique.add(c["chunkId"])
    missing=len(tc)-reused
    missing_bytes=len(target)-reused_bytes
    must(reused+missing==len(tc) and
         reused_bytes+missing_bytes==len(target),"reuse conservation failure")
    return {"baseChunks":len(bc),"targetChunks":len(tc),
            "reusedChunks":reused,"reusedBytes":reused_bytes,
            "missingChunks":missing,"missingBytes":missing_bytes,
            "duplicateReusedTargetChunkIds":duplicate_target,
            "uniqueReusedIds":len(unique),
            "reuseFractionTargetBytes":round(reused_bytes/len(target),9),
            "cblkCountIfEncoded":{ "base":(len(bc)+4095)//4096,
                                   "target":(len(tc)+4095)//4096 },
            "cblkRecordAndEnvelopeBytesIfEncoded":{
                "base":36*len(bc)+44*((len(bc)+4095)//4096),
                "target":36*len(tc)+44*((len(tc)+4095)//4096)},
            "metadataMeaning":"CBLK IDs+lengths+header+prefix+CRC ONLY, hypothetical for unsupported lab profiles; excludes CORE/BIDX/CEND/FOOT/trailer and patch payload/compression."}


def evaluate(output,base,target,contract,run_id,checkout):
    control=json.loads((output/"scanner-control.json").read_text())
    must(control.get("schema")=="chunkshift.core011.g1j2f-scanner-control.v1" and
         control.get("verdict")=="NO_VERDICT_EXPLORATORY" and
         control.get("hashSuite")=="chunkshift.blake3-256.v1" and
         control.get("baselineReusedChunks")==0 and
         control.get("baselineMissingChunks")==81,
         "baseline public Plan/hashing controls differ")
    samples=control.get("observations")
    must(isinstance(samples,list) and len(samples)==10,"expected ten lab controls")
    must([(x["role"],x["lane"]) for x in samples]==
         [(role,lane) for role in ("base","target") for lane in LANES],
         "profile lane order drift")
    csm={}
    for role in ("base","target"):
        csm_file=output/(role+".stable.csm")
        raw=csm_file.read_bytes()
        must(sha256(raw)==STABLE_CSM[role],
             "stable public CSM physical identity differs from archived J2C")
        csm[role]={"sha256":sha256(raw),"bytes":len(raw)}
    scan={}
    sources={"base":base,"target":target}
    for role in ("base","target"):
        scan[role]={}
        for lane in LANES:
            f=output/f"{role}.{lane}.tsv"
            rows=validate_scanner(sources[role],f.read_text())
            ctl=next(x for x in samples if x["role"]==role and x["lane"]==lane)
            must(ctl["chunkCount"]==len(rows) and
                 ctl["contentLength"]==len(sources[role]) and
                 ctl["tsvFile"]==f.name,"streaming/scalar scanner controls drift")
            scan[role][lane]=rows
    baseline=scan["base"]["stable-fastcdc64k"]
    original_target=scan["target"]["stable-fastcdc64k"]
    must(len(baseline)==74 and len(original_target)==81,
         "frozen historical stable profile chunk count drift")
    report={}
    for lane in LANES:
        left=scan["base"][lane]
        right=scan["target"][lane]
        report[lane]=compare_lane(base,target,left,right)
        report[lane]["baseTsvSha256"]=sha256((output/f"base.{lane}.tsv").read_bytes())
        report[lane]["targetTsvSha256"]=sha256((output/f"target.{lane}.tsv").read_bytes())
        report[lane]["registeredForPublicManifest"]=lane=="stable-fastcdc64k"
    must(report["stable-fastcdc64k"]["reusedChunks"]==0 and
         report["stable-fastcdc64k"]["missingChunks"]==81 and
         report["stable-fastcdc64k"]["reusedBytes"]==0,
         "exact stable public CSM baseline reused any unexpected bytes")
    return {"schema":SCHEMA,"runId":run_id,"checkoutSha":checkout,
            "corpusContractSha256":sha256(d.CONTRACT.read_bytes()),
            "sourceDigests":{"base":sha256(base),"target":sha256(target)},
            "stablePhysicalCsm":csm,
            "allLanes":list(LANES),"profiles":report,
            "measureType":"exact-reused-canonical-chunk-bytes;NOT patch-size or CPU benchmark",
            "limitations":"Four nonproduction lab chunking profiles are not CSM-registered and cannot be interpreted as public API/CSM wire profile decisions; no compression, CPU, reconstruction, delta or optimization verdict.",
            "verdict":"NO_VERDICT_EXPLORATORY"}


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output-dir",type=Path,required=True)
    parser.add_argument("--run-id",required=True)
    args=parser.parse_args()
    must(args.run_id.startswith("CORE011-CSM-001/G1J2F-"),
         "wrong G1-J2F runId")
    repo=Path.cwd()
    checkout=subprocess.check_output(["git","rev-parse","HEAD"],text=True).strip()
    must(os.environ.get("GITHUB_ACTIONS")=="true" and
         os.environ.get("GITHUB_SHA")==checkout and len(checkout)==40,
         "G1-J2F must run on exact GitHub-hosted checkout")
    must(not args.output_dir.exists(),"refuse reused research artifact")
    output=args.output_dir
    output.mkdir(parents=True)
    try:
        contract=d.verify_contract(json.loads(d.CONTRACT.read_text()))
        original=d.fetch_original_payloads(contract)
        for role in ("base","target"):
            (output/(role+".input")).write_bytes(original[role])
        subprocess.run(["dotnet","restore","benchmarks/ChunkShift.Benchmarks.slnx"],
                       cwd=repo,check=True,timeout=900)
        subprocess.run(["dotnet","build","benchmarks/ChunkShift.Benchmarks.slnx",
                        "-c","Release","--no-restore"],
                       cwd=repo,check=True,timeout=900)
        subprocess.run(["dotnet","run","--no-build","-c","Release","--project",
                        "benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj",
                        "--","core011-g1j2f-profiles",
                        "--base",output/"base.input",
                        "--target",output/"target.input",
                        "--output-dir",output],cwd=repo,check=True,timeout=600)
        report=evaluate(output,original["base"],original["target"],contract,args.run_id,checkout)
        (output/"summary.json").write_text(json.dumps(report,indent=2)+"\n")
        for lane in LANES:
            print(lane, "reused=",report["profiles"][lane]["reusedChunks"],
                  "bytes=",report["profiles"][lane]["reusedBytes"])
        print("G1-J2F all five lane and original CSM provenance PASS; product NO_VERDICT")
    except Exception as error:
        (output/"failure.json").write_text(json.dumps({
            "schema":"chunkshift.core011.g1j2f-failure.v1",
            "runId":args.run_id,
            "errorType":type(error).__name__,
            "message":str(error)[:400],
            "verdict":"NO_VERDICT_EXPLORATORY",
        },indent=2)+"\n")
        raise
    finally:
        for role in ("base","target"):
            (output/(role+".input")).unlink(missing_ok=True)


if __name__=="__main__":
    main()
