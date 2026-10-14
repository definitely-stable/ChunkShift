#!/usr/bin/env python3
"""E3-R2-B2A fixed eager-only high-frequency native resident memory calibration."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[2]
CONTRACT=ROOT/"benchmarks/experiments/core011-csm-e3r2b2a-peak.v1.json"
BASELINE_SOURCE_SHA="a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
BASELINE_BLOB_SHA="ff56ccddaa0efc9970fd0b019e20523d6c2933d6"
PARENT="2de3f8d30498a4dca85615d98d3072abaa62c838"
CSM_SHA="342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"
RUNID=re.compile(r"^CORE011-CSM-004/RUN-\d{8}-\d+-[0-9a-f]{8}-(?:linux|windows)-r[01]-x64$")
METRICS=["privateResidentBytes","processRssBytes","privateCommitBytes",
         "managedHeapBytes","gen2Collections","totalGcPauseMs","elapsedMs","cpuModel",
         "runtime","sampledPrivateResidentHighWaterBytes"]


def import_module(file,name):
    spec=importlib.util.spec_from_file_location(name,file)
    obj=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj


r0=import_module(ROOT/"benchmarks/scripts/core011_csm_e3_guard.py","e3r2b2a_r0")
g1j=import_module(ROOT/"benchmarks/scripts/core011_csm_g1j_contract.py","e3r2b2a_g1j")


def need(test,reason):
    if not test: raise ValueError(reason)


def sha(file):
    return hashlib.sha256(file.read_bytes()).hexdigest()


def validate_contract(c):
    need(c.get("schema")=="chunkshift.core011.e3r2b2a-peak-protocol.v1" and
         c.get("experimentId")=="CORE011-CSM-004" and c.get("ownerIssue")==154 and
         c.get("stage")=="R2-B2A-sampled-private-resident-calibration" and
         c.get("parentEvidenceCommit")==PARENT and
         c.get("sourceBaselineSha256")==BASELINE_SOURCE_SHA and
         c.get("sourceReaderGitBlobSha1")==BASELINE_BLOB_SHA and
         c.get("variant")=="eager" and c.get("decisionRunsAllowed") is False and
         c.get("budgetsLocked") is False,"E3-B2A source/decision freeze violated")
    need(c.get("platforms")==["ubuntu-24.04","windows-2025"] and
         c.get("independentRunnerReplicates")==[0,1] and
         c.get("sourceMiB")==384 and c.get("physicalManifestSha256")==CSM_SHA and
         c.get("chunkCount")==5052 and c.get("cblkCount")==2 and
         c.get("sourceContentArchived") is False and
         c.get("sourceBuilder")=="G1-J1 external source-free CSM fixture builder" and
         c.get("readerCases")==[0,16] and c.get("naturalIdleSeconds")==30 and
         c.get("samplingPeriodMilliseconds")==100 and
         c.get("perOpenedReaderCheckpoint") is True and
         c.get("minTraceSamples")==240 and c.get("minNaturalIdleMilliseconds")==30000,
         "E3-B2A host/sample matrix drift")
    need(c.get("metrics")==METRICS and c.get("provenance")=={
        "sameCheckout":True,"differentBuilderConsumerPids":True,
        "twoIndependentConsumersPerHost":True,"matchedPhysicalCsm":True,
        "failClosedMissingNativeCounters":True,"perNativeHostCpuModel":True,
    } and c.get("guard")=={
        "productionCodeChangesAllowed":False,"adoptAllowed":False,
        "finalDecisionRunsAllowed":False,"realVersionedHoldoutUsed":False,
        "numericDecisionBudgetsAllowed":False,
    } and type(c.get("limitations")) is list and len(c["limitations"])==5,
         "E3-B2A missing provenance/no-product-verdict gates")


def number(x,name,positive=False):
    need(type(x) in (int,float) and math.isfinite(x) and
         (x>0 if positive else x>=0),"Invalid "+name)


def validate_trace(o,desc,checkout,run_id,os_name,hold):
    need(o.get("schema")=="chunkshift.core011.e3r2b2a-sampled-residency.v1" and
         o.get("experimentId")=="CORE011-CSM-004" and
         o.get("runId")==run_id and o.get("checkoutSha")==checkout and
         o.get("variant")=="eager" and o.get("role")=="fresh-source-free-consumer" and
         o.get("decisionRunsAllowed") is False and o.get("productVerdict")=="NOT_QUALIFIED" and
         o.get("heldReaders")==hold and o.get("naturalIdleSeconds")==30 and
         o.get("sampleIntervalTargetMs")==100,
         "Wrong sampled baseline role/owner/cell")
    need(o.get("fixtureSha256")==desc["manifestSha256"] and
         o.get("csmBytes")==desc["manifestBytes"] and
         o.get("sourceContentBytes")==desc["contentBytes"] and
         o.get("chunkCount")==desc["chunkCount"]==5052 and
         o.get("cblkCount")==desc["cblkCount"]==2 and
         o.get("producerPid")==desc["builderPid"] and
         type(o.get("consumerPid")) is int and o["consumerPid"]>0 and
         o["consumerPid"]!=o["producerPid"] and
         o.get("architecture")=="X64" and
         isinstance(o.get("cpuModel"),str) and len(o["cpuModel"])>=8 and
         isinstance(o.get("os"),str) and o["os"] and
         isinstance(o.get("runtime"),str) and o["runtime"] and
         type(o.get("processorCount")) is int and o["processorCount"]>=1,
         "Wrong physical CSM/process/hardware identity")
    n=o.get("sampleCount")
    trace=o.get("trace")
    need(type(n) is int and n>=240 and type(trace) is list and len(trace)==n,
         "Missing sampled trace points")
    need(type(o.get("heldStartMs")) is int and type(o.get("heldEndMs")) is int and
         o["heldEndMs"]-o["heldStartMs"]>=30000,
         "Required 30-second true idle was not recorded")
    prev=-1
    max_private=0
    max_rss=0
    phases=set()
    checkpoints=set()
    for t in trace:
        name=t.get("phase")
        need(type(name) is str and name and
             name in ("pre-open","opening-readers","held-natural-idle",
                      "disposing-readers","after-dispose-natural") or
             (isinstance(name,str) and name.startswith("opening-checkpoint-")),
             "Invalid trace phase")
        phases.add(name)
        if name.startswith("opening-checkpoint-"): checkpoints.add(name)
        for metric in ("elapsedMs","privateResidentBytes","processRssBytes",
                       "managedHeapBytes","gen2Collections","totalGcPauseMs"):
            number(t.get(metric),metric,positive=metric in
                   ("privateResidentBytes","processRssBytes"))
        need(t["privateResidentBytes"]<=t["processRssBytes"] and
             t["elapsedMs"]>=prev,"Invalid private RSS or monotonic trace clock")
        prev=t["elapsedMs"]
        max_private=max(max_private,t["privateResidentBytes"])
        max_rss=max(max_rss,t["processRssBytes"])
        if os_name=="windows":
            number(t.get("privateCommitBytes"),"Windows committed private pages",positive=True)
        else:
            need(t.get("privateCommitBytes") is None,
                 "Linux trace cannot masquerade as Windows private commit")
    need("held-natural-idle" in phases and "after-dispose-natural" in phases,
         "No held residency or post-dispose trace")
    if hold==16:
        need(checkpoints=={"opening-checkpoint-"+str(i) for i in range(1,17)},
             "One of the 16 actual reader-open memory checkpoints is missing")
    else:
        need(not checkpoints,"Zero-reader control captured reader-open checkpoints")
    need(o.get("sampledPrivateResidentHighWaterBytes")==max_private and
         o.get("sampledProcessRssHighWaterBytes")==max_rss,
         "High-water does not equal actual sampled trace maximum")
    need(any(t["phase"]=="held-natural-idle" and
             t["elapsedMs"]>=o["heldStartMs"]+29000 for t in trace),
         "Missing samples near the end of 30s natural idle")
    return {"heldReaders":hold,"consumerPid":o["consumerPid"],
            "samples":n,"sampledPrivateResidentPeakBytes":max_private,
            "sampledRssPeakBytes":max_rss,
            "cpuModel":o["cpuModel"],
            "traceSha256":None}


def execute(argv,log,timeout=800):
    with log.open("w",encoding="utf-8") as output:
        try:
            p=subprocess.run([str(s) for s in argv],cwd=ROOT,
                stdout=output,stderr=subprocess.STDOUT,timeout=timeout)
        except subprocess.TimeoutExpired as error:
            raise ValueError("R2-B2A process timed out: "+str(log)) from error
    if p.returncode:
        raise ValueError("R2-B2A child exit "+str(p.returncode)+"\n"+
                         "\n".join(log.read_text(errors="replace").splitlines()[-32:]))
    print("PASS "+" ".join(map(str,argv)),flush=True)


def preflight(run_id,os_name,replicate):
    need(os.environ.get("GITHUB_ACTIONS")=="true",
         "E3-R2-B2A runs on GitHub-hosted runner only")
    need(replicate in (0,1) and os_name in ("linux","windows") and
         RUNID.fullmatch(run_id) and run_id.endswith(os_name+"-r"+str(replicate)+"-x64"),
         "Wrong preregistered RunId/platform/replica")
    need(os.environ.get("RUNNER_OS","").lower()==os_name,
         "Actual hosted OS differs from protocol OS")
    checkout=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip()
    need(os.environ.get("GITHUB_SHA")==checkout and checkout[:8] in run_id,
         "Git checkout not pinned")
    subprocess.run(["git","merge-base","--is-ancestor",PARENT,checkout],
                   cwd=ROOT,check=True,timeout=30)
    validate_contract(json.loads(CONTRACT.read_text()))
    r0c=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
    r0.validate_contract(r0c)
    r0.check_historical_contract(ROOT)
    src=(ROOT/"src/ChunkShift/Manifest/CsmReader.cs").read_bytes()
    r0.validate_source_pin(r0c,src)
    need(hashlib.sha256(src).hexdigest()==BASELINE_SOURCE_SHA,"Baseline reader drift")
    return checkout


def summarize(folder,checkout,run_id,os_name,replicate):
    fixture=folder/"fixture"
    descriptor=json.loads((fixture/"fixture.json").read_text())
    g1j.validate_manifest_only_fixture(descriptor,fixture)
    need(descriptor["builderCheckoutSha"]==checkout and
         descriptor["sourceSha256"] and descriptor["manifestSha256"]==CSM_SHA and
         descriptor["chunkCount"]==5052 and descriptor["cblkCount"]==2 and
         descriptor["contentBytes"]==384*1048576 and
         descriptor["hashSuite"]=="blake3" and descriptor["hasBlockIndex"] is True and
         not (fixture/"source.bin").exists(),
         "Wrong source-free physical CSM baseline")
    rows=[]
    pids=set()
    for hold in (0,16):
        f=folder/("held-"+str(hold)+".json")
        data=json.loads(f.read_text())
        cell=validate_trace(data,descriptor,checkout,run_id,os_name,hold)
        need(cell["consumerPid"] not in pids and
             cell["consumerPid"]!=descriptor["builderPid"],
             "Consumer not isolated from builder/control")
        pids.add(cell["consumerPid"])
        cell["traceSha256"]=sha(f)
        rows.append(cell)
    need(len(pids)==2 and len(rows)==2,"Missing independent cells")
    return {
        "schema":"chunkshift.core011.e3r2b2a-sampled-summary.v1",
        "experimentId":"CORE011-CSM-004","runId":run_id,
        "checkoutSha":checkout,"platform":os_name,"replicate":replicate,
        "builderPid":descriptor["builderPid"],"consumerCount":2,
        "physicalCsmSha256":descriptor["manifestSha256"],
        "decisionRunsAllowed":False,"budgetsLocked":False,
        "productVerdict":"NOT_QUALIFIED","results":rows,
        "limitations":[
            "Two independent hosts per OS, synthetic eager baseline only.",
            "Sampled (not certified instantaneous) peak; OS process baselines are unpaired.",
            "30-second idle only; 300-second Windows working-set trimming in prior B1 evidence.",
            "No decision budgets or candidate CPU/GC measurements."
        ]
    }


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--run-id",required=True)
    p.add_argument("--platform",choices=("linux","windows"),required=True)
    p.add_argument("--replicate",type=int,choices=(0,1),required=True)
    p.add_argument("--output-dir",type=Path,required=True)
    a=p.parse_args()
    try:
        head=preflight(a.run_id,a.platform,a.replicate)
        output=a.output_dir.resolve()
        output.mkdir(parents=True,exist_ok=False)
        project="benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"
        execute(["dotnet","restore",project],output/"restore.log",timeout=600)
        execute(["dotnet","build",project,"-c","Release","--no-restore"],
                output/"build.log",timeout=600)
        fixture=output/"fixture"
        fixture.mkdir()
        builder_id=("CORE011-CSM-001/G1J1-E3R2B2A-"+a.platform+"-"+
                    str(a.replicate)+"-"+str(os.getpid()))
        prefix=["dotnet","run","--no-build","-c","Release","--project",project,"--"]
        execute(prefix+["core011-g1j1-build","--output-dir",fixture,
                       "--size-mib",384,"--suite","blake3","--bidx","true",
                       "--run-id",builder_id],output/"builder.log",timeout=900)
        descriptor=json.loads((fixture/"fixture.json").read_text())
        g1j.validate_manifest_only_fixture(descriptor,fixture)
        for held in (0,16):
            execute(prefix+[
                "core011-e3r2b2a-sampled","--fixture-dir",fixture,
                "--output",output/("held-"+str(held)+".json"),
                "--run-id",a.run_id,"--hold-readers",held,
                "--idle-seconds",30,"--sample-ms",100],
                output/("held-"+str(held)+".log"),timeout=200)
        result=summarize(output,head,a.run_id,a.platform,a.replicate)
        (output/"summary.json").write_text(json.dumps(result,indent=2)+"\n")
        print("E3-R2B2A SAMPLED PEAK CALIBRATION PASS: "+a.platform+
              " replicate "+str(a.replicate)+"; NO PRODUCT VERDICT")
    except (OSError,ValueError,subprocess.CalledProcessError) as error:
        print("E3-R2B2A FAIL-CLOSED: "+str(error),file=sys.stderr)
        raise SystemExit(2) from error


if __name__=="__main__":
    main()
