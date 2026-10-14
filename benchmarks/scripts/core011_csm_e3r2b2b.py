#!/usr/bin/env python3
"""CORE011-CSM-004 E3-R2B2B external OS observer vs internal 100-ms sampler control."""
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
import time

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "benchmarks/experiments/core011-csm-e3r2b2b-observer.v1.json"
SOURCE_SHA = "a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
CSM_SHA = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"
PARENT = "1f70fc748e2705531d52bcc40ba6be71d309e135"
RUNID = re.compile(r"^CORE011-CSM-004/RUN-\d{8}-\d+-[0-9a-f]{8}-(?:linux|windows)-r[01]-x64$")
ORDER = {0:("off-0","on-0","on-16","off-16"),
         1:("on-0","off-0","off-16","on-16")}


def load_module(path,name):
    spec=importlib.util.spec_from_file_location(name,path)
    m=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


r0 = load_module(ROOT/"benchmarks/scripts/core011_csm_e3_guard.py","e3r2b2b_r0")
g1j = load_module(ROOT/"benchmarks/scripts/core011_csm_g1j_contract.py","e3r2b2b_builder")


def require(ok,message):
    if not ok: raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def check_contract(c):
    require(c.get("schema")=="chunkshift.core011.e3r2b2b-observer-control.v1" and
            c.get("experimentId")=="CORE011-CSM-004" and
            c.get("ownerIssue")==154 and
            c.get("stage")=="R2-B2B-external-native-observer-calibration" and
            c.get("parentEvidenceCommit")==PARENT and
            c.get("baselineSourceSha256")==SOURCE_SHA and
            c.get("physicalCsmSha256")==CSM_SHA and
            c.get("originalContentMiB")==384 and
            c.get("chunkCount")==5052 and c.get("cblkCount")==2 and
            c.get("variant")=="eager" and c.get("decisionRunsAllowed") is False and
            c.get("budgetsLocked") is False,"R2B2B source/decision contract drift")
    require(c.get("platforms")==["ubuntu-24.04","windows-2025"] and
            c.get("replicates")==[0,1] and c.get("heldReaders")==[0,16] and
            c.get("internalSampler")==["off","on"] and
            c.get("externalSamplerPeriodMs")==100 and
            c.get("naturalIdleSeconds")==30 and
            c.get("replicateOrder")=={
                "0":["off-0","on-0","on-16","off-16"],
                "1":["on-0","off-0","off-16","on-16"]},
            "R2B2B calibration matrix mutated")
    require(c.get("requiredMetrics")==[
        "externalSampledPrivateResidentPeakBytes","externalRssPeakBytes",
        "externalSampleCount","internalSampleCount","internalOpenCheckpoints",
        "managedHeapAfterIdleBytes","gen2Collections","gcPauseTotalMs"
    ] and c.get("requirements")=={
        "externalObserverDifferentPid":True,"allTargetsFresh":True,
        "sourceOnlyInExitedBuilder":True,"sameNativeMetricSource":True,
        "rawTraceRequired":True,"sourceShaLocked":True,"originalR0NoDecision":True
    } and c.get("forbidden")=={
        "adopt":True,"holdout":True,"sourceChanges":True,
        "productionBudgetFreeze":True,"claimCausalCost":True,"peakUpperBound":True
    } and type(c.get("limitations")) is list and len(c["limitations"])==5,
        "R2B2B required audit/forbidden outcome mutated")


def num(v,name,positive=False):
    require(type(v) in (int,float) and math.isfinite(v) and
            (v>0 if positive else v>=0),"R2B2B invalid metric "+name)


def preflight(run_id,platform,replica):
    require(os.getenv("GITHUB_ACTIONS")=="true" and
            os.getenv("RUNNER_OS","").lower()==platform,
            "E3-R2B2B requires same-platform GitHub-hosted runner")
    require(replica in (0,1) and RUNID.fullmatch(run_id) and
            run_id.endswith(platform+"-r"+str(replica)+"-x64"),
            "R2B2B run identity differs from registered cell")
    checkout=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip()
    require(os.getenv("GITHUB_SHA")==checkout and checkout[:8] in run_id,
            "Invalid pinned experiment checkout")
    subprocess.run(["git","merge-base","--is-ancestor",PARENT,checkout],
                   cwd=ROOT,timeout=30,check=True)
    check_contract(json.loads(CONTRACT.read_text()))
    obj=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
    r0.validate_contract(obj)
    r0.check_historical_contract(ROOT)
    source=(ROOT/"src/ChunkShift/Manifest/CsmReader.cs").read_bytes()
    r0.validate_source_pin(obj,source)
    require(hashlib.sha256(source).hexdigest()==SOURCE_SHA,
            "E3R0 reader source changed")
    return checkout


def child(argv,log,timeout=900):
    with log.open("w",encoding="utf-8") as f:
        try:
            p=subprocess.run(list(map(str,argv)),cwd=ROOT,
                             stdout=f,stderr=subprocess.STDOUT,timeout=timeout)
        except subprocess.TimeoutExpired as err:
            raise ValueError("R2B2B child timeout "+str(log)) from err
    if p.returncode:
        raise ValueError("R2B2B child exit "+str(p.returncode)+
                         "\n"+"\n".join(log.read_text(errors="replace").splitlines()[-45:]))
    print("PASS "+" ".join(map(str,argv)),flush=True)


def wait_ready(path,proc,seconds=75):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if path.exists():return
        if proc.poll() is not None:
            raise ValueError("Target terminated before observer could start")
        time.sleep(0.025)
    raise ValueError("Target never emitted ready marker")


def complete_pair(prefix,fixture,run_id,cell,label_dir):
    label_dir.mkdir()
    mode,hcount=cell.split("-")
    hold=int(hcount)
    ready=label_dir/"target-ready.txt"
    go=label_dir/"observer-go.txt"
    done=label_dir/"target-done.txt"
    targetfile=label_dir/"target.json"
    observerfile=label_dir/"observer.json"
    args=prefix+[
        "core011-e3r2b2b-target","--fixture-dir",fixture,
        "--run-id",run_id,"--result",targetfile,
        "--ready",ready,"--go",go,"--done",done,
        "--hold-readers",hold,"--internal-sampler",mode
    ]
    target_log=(label_dir/"target.log").open("w",encoding="utf-8")
    target=subprocess.Popen(list(map(str,args)),cwd=ROOT,stdout=target_log,
                            stderr=subprocess.STDOUT)
    observer=None
    observer_log=None
    try:
        wait_ready(ready,target)
        require(ready.read_text().strip()==str(target.pid),
                "Untrusted interprocess marker PID")
        observer_args=prefix+[
            "core011-e3r2b2b-observer","--target-pid",target.pid,
            "--run-id",run_id,"--result",observerfile,
            "--go",go,"--done",done,"--sample-ms",100
        ]
        observer_log=(label_dir/"observer.log").open("w",encoding="utf-8")
        observer=subprocess.Popen(list(map(str,observer_args)),cwd=ROOT,
                                 stdout=observer_log,stderr=subprocess.STDOUT)
        try:
            observer_ret=observer.wait(timeout=90)
            target_ret=target.wait(timeout=10)
        except subprocess.TimeoutExpired as err:
            raise ValueError("Observer/target coordination timeout") from err
        if target_ret or observer_ret:
            raise ValueError("R2B2B target/observer nonzero exit: target "+
                             str(target_ret)+" observer "+str(observer_ret))
    finally:
        if observer is not None and observer.poll() is None:
            observer.kill()
            observer.wait()
        if target.poll() is None:
            target.kill()
            target.wait()
        if observer_log is not None:observer_log.close()
        target_log.close()
    return target.pid,observer.pid


def validate_pair(folder,run_id,checkout,descriptor,mode,held,expected_pids,os_name):
    target=json.loads((folder/"target.json").read_text())
    observer=json.loads((folder/"observer.json").read_text())
    target_pid,observer_pid=expected_pids
    require(target.get("schema")=="chunkshift.core011.e3r2b2b-target.v1" and
            target.get("experimentId")=="CORE011-CSM-004" and
            target.get("runId")==run_id and target.get("checkoutSha")==checkout and
            target.get("variant")=="eager" and
            target.get("internalSampler")==mode and
            target.get("heldReaders")==held and
            target.get("targetPid")==target_pid and
            target.get("builderPid")==descriptor["builderPid"] and
            target.get("manifestSha256")==CSM_SHA and
            target.get("sourceContentAbsent") is True and
            target.get("verdict")=="OBSERVER_COST_CALIBRATION_ONLY_NO_PRODUCT_DECISION",
            "Target invalid provenance/semantics/decision")
    count=target.get("internalSampleCount")
    checkpoints=target.get("internalReaderOpenCheckpoints")
    if mode=="off":
        require(count==0 and checkpoints==0 and
                target.get("privateResidentHighWaterInternalBytes") is None,
                "Internal sampler-off called native private-resident observer")
    else:
        require(type(count) is int and count>=240 and checkpoints==held,
                "Internal sampler-on missing traces/checkpoints")
        num(target.get("privateResidentHighWaterInternalBytes"),
            "internal private resident maximum",positive=True)
    require(observer.get("schema")=="chunkshift.core011.e3r2b2b-external.v1" and
            observer.get("experimentId")=="CORE011-CSM-004" and
            observer.get("runId")==run_id and
            observer.get("targetPid")==target_pid and
            observer.get("observerPid")==observer_pid and
            observer_pid not in (target_pid,descriptor["builderPid"]) and
            observer.get("samplePeriodMs")==100 and
            observer.get("verdict")=="EXTERNAL_BASELINE_CALIBRATION_ONLY",
            "External observer identity or sample source mismatch")
    samples=observer.get("samples")
    n=observer.get("sampleCount")
    require(type(n) is int and n>=260 and type(samples) is list and len(samples)==n,
            "Incomplete external 100ms samples")
    kind=("linux-smaps_rollup-private_clean_plus_private_dirty"
          if os_name=="linux" else "windows-psapi-ex2-private_working_set")
    require(observer.get("metricSource")==kind,"External OS metric source wrong")
    maxresident=0
    maxrss=0
    elapsed=-1
    for s in samples:
        require(s.get("residentMetricSource")==kind,
                "Native telemetry source changed mid-trace")
        for key in ("privateResidentBytes","processRssBytes","managedHeapBytes",
                    "uptimeMilliseconds","gen2Collections","totalGcPauseMs"):
            num(s.get(key),key,positive=key in ("privateResidentBytes","processRssBytes"))
        require(s["privateResidentBytes"]<=s["processRssBytes"] and
                s["uptimeMilliseconds"]>=elapsed,
                "External native counter bound/time mismatch")
        elapsed=s["uptimeMilliseconds"]
        maxresident=max(maxresident,s["privateResidentBytes"])
        maxrss=max(maxrss,s["processRssBytes"])
    require(observer.get("observedPrivateResidentMaximumBytes")==maxresident and
            observer.get("observedProcessRssMaximumBytes")==maxrss,
            "Claimed native sampled peak differs from raw trace")
    require(type(observer.get("clockDurationMs")) is int and
            observer["clockDurationMs"]>=30000,
            "Incomplete natural-idle observation clock")
    for key in ("managedHeapAfterNaturalIdleBytes","gen2Collections","gcPauseTotalMs"):
        num(target.get(key),key)
    return {
        "case":mode+"-"+str(held),"targetPid":target_pid,
        "observerPid":observer_pid,"externalNativeSampleCount":n,
        "externalSampledPrivateResidentPeakBytes":maxresident,
        "externalSampledRssPeakBytes":maxrss,
        "internalSampleCount":count,"internalOpenCheckpoints":checkpoints,
        "managedHeapAfterIdleBytes":target["managedHeapAfterNaturalIdleBytes"],
        "gen2Collections":target["gen2Collections"],
        "gcPauseTotalMs":target["gcPauseTotalMs"],
        "targetJsonSha256":sha(folder/"target.json"),
        "observerJsonSha256":sha(folder/"observer.json")
    }


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--output-dir",required=True,type=Path)
    p.add_argument("--run-id",required=True)
    p.add_argument("--platform",choices=("linux","windows"),required=True)
    p.add_argument("--replica",type=int,choices=(0,1),required=True)
    a=p.parse_args()
    output=a.output_dir.resolve()
    try:
        checkout=preflight(a.run_id,a.platform,a.replica)
        output.mkdir(parents=True,exist_ok=False)
        project="benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"
        child(["dotnet","restore",project],output/"restore.log",timeout=600)
        child(["dotnet","build",project,"-c","Release","--no-restore"],
              output/"build.log",timeout=600)
        prefix=["dotnet","run","--no-build","-c","Release","--project",project,"--"]
        # Both dotnet run and dotnet <assembly.dll> may launch an additional CLR
        # child on hosted images. Only the built apphost executable has the same
        # OS PID as Environment.ProcessId used by our memory observer.
        apphost=ROOT/"benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0"/(
            "ChunkShift.Benchmarks.exe" if a.platform=="windows" else "ChunkShift.Benchmarks")
        require(apphost.is_file(),"Missing compiled native CLR apphost executable")
        consumer_prefix=[apphost]
        fixture=output/"fixture"
        fixture.mkdir()
        builder_id=("CORE011-CSM-001/G1J1-E3R2B2B-"+a.platform+"-r"+
                    str(a.replica)+"-"+str(os.getpid()))
        child(prefix+["core011-g1j1-build","--output-dir",fixture,
                      "--size-mib",384,"--suite","blake3","--bidx","true",
                      "--run-id",builder_id],output/"builder.log",timeout=1000)
        descriptor=json.loads((fixture/"fixture.json").read_text())
        g1j.validate_manifest_only_fixture(descriptor,fixture)
        require(descriptor["manifestSha256"]==CSM_SHA and
                descriptor["builderCheckoutSha"]==checkout and
                descriptor["chunkCount"]==5052 and descriptor["cblkCount"]==2 and
                not (fixture/"source.bin").exists(),"Physical source-free fixture drift")
        rows=[]
        pids={descriptor["builderPid"]}
        for cell in ORDER[a.replica]:
            folder=output/cell
            pair=complete_pair(consumer_prefix,fixture,a.run_id,cell,folder)
            require(pair[0] not in pids and pair[1] not in pids and pair[0]!=pair[1],
                    "Reused producer/target/observer process PID")
            pids.update(pair)
            mode,n=cell.split("-")
            rows.append(validate_pair(folder,a.run_id,checkout,descriptor,
                                      mode,int(n),pair,a.platform))
            print("E3-R2B2B completed cell "+cell+" native peak MiB "+
                  str(round(rows[-1]["externalSampledPrivateResidentPeakBytes"]/1048576,3)),
                  flush=True)
        require(len(rows)==4 and len(pids)==9,"Missing builder+four target/observer pairs")
        result={
            "schema":"chunkshift.core011.e3r2b2b-observer-summary.v1",
            "experimentId":"CORE011-CSM-004","runId":a.run_id,
            "checkoutSha":checkout,"platform":a.platform,
            "replicate":a.replica,"sourceCsmSha256":CSM_SHA,
            "builderPid":descriptor["builderPid"],"consumerCount":4,
            "observerCount":4,"cells":rows,"decisionRunsAllowed":False,
            "productVerdict":"NOT_QUALIFIED",
            "limitations":[
                "All native resident high-waters are sampled, not instantaneous upper bounds.",
                "External OS polling is not overhead-free; pair order counterbalanced, no causal proof.",
                "Only eager synthetic CSM; not full Plan, sealed real-versioned or release budget."
            ]
        }
        (output/"summary.json").write_text(json.dumps(result,indent=2)+"\n")
        print("E3-R2B2B EXTERNAL OBSERVER CALIBRATION PASS, no decision",flush=True)
    except (OSError,ValueError,subprocess.CalledProcessError) as err:
        print("E3-R2B2B FAIL-CLOSED: "+str(err),file=sys.stderr)
        raise SystemExit(2) from err


if __name__=="__main__":
    main()
