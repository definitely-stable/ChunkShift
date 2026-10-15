#!/usr/bin/env python3
"""E3-R2-C0: real Roslyn .NET DLL pair source-free public Plan, native external observer."""
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

ROOT=Path(__file__).resolve().parents[2]
SCRIPTS=ROOT/"benchmarks/scripts"
sys.path.insert(0,str(SCRIPTS))
import core011_csm_g1j2c_roslyn as roslyn
import core011_csm_g1j_contract as g1j
import core011_csm_e3_guard as r0
SOURCE_SHA="a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
PARENT="7719bfe69da722f8cb0e8f750fa99527ba1463af"
CONTRACT=ROOT/"benchmarks/experiments/core011-csm-e3r2c0-real-plan.v1.json"
RUNID=re.compile(r"^CORE011-CSM-004/RUN-[0-9]{8}-[0-9]+-[0-9a-f]{8}-(?:linux|windows)-x64$")


def require(cond,message):
    if not cond:raise ValueError(message)


def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def protocol(p):
    require(p.get("schema")=="chunkshift.core011.e3r2c0-real-versioned-plan.v1" and
        p.get("experimentId")=="CORE011-CSM-004" and p.get("ownerIssue")==154 and
        p.get("stage")=="R2-C0-real-versioned-DLL-Plan-baseline" and
        p.get("parentEvidenceCommit")==PARENT and
        p.get("originalReaderSha256")==SOURCE_SHA and p.get("baselineVariant")=="eager" and
        p.get("candidateRunsAllowed") is False and p.get("decisionRunsAllowed") is False and
        p.get("budgetsLocked") is False,"R2C0 decision/source freeze drift")
    require(p.get("corpus")=={
        "origin":"nuget-released-managed-pe","package":"microsoft.codeanalysis.csharp",
        "baseVersion":"4.13.0","targetVersion":"4.14.0",
        "assemblyPath":"lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll",
        "packageHashesIndependentlyPreFrozen":False,
        "packageDigestsSource":"self-calculated during download, not independent external authenticity",
        "materializeInSeparateExitedProcess":True,"removeSourceBeforeConsumer":True,
        "originalSourceInArchives":False
    } and p.get("workload")=={
        "publicOperation":"ChunkPatch.PlanAsync","iterations":64,"warmupCalls":4,
        "sourceOnlyCsm":True,"naturalIdleMillisecondsMin":30000,
        "externalSampleMilliseconds":100,"internalNativeSampler":False
    } and p.get("hostedPlatforms")==["ubuntu-24.04","windows-2025"] and
    p.get("runnerReplicasPerPlatform")==1,"R2C0 physical corpus/real Plan matrix drift")
    require(p.get("requiredMetrics")==[
        "externalPrivateResidentSampledPeakBytes","externalSampleCount",
        "planBatchWallMilliseconds","planBatchCpuMilliseconds","planAllocatedBytes",
        "planGen2Collections","planGcPauseMilliseconds","managedHeapAfterIdleBytes",
        "publicPlanConservation","originalCsmSha256"] and
        p.get("requirements")=={
            "externalIndependentNativeObserver":True,
            "distinctSourceBuilderAndConsumerPids":True,
            "independentPhysicalCsmHashVerification":True,
            "unchangedProductionCode":True,"existingR0DecisionGateClosed":True
        } and type(p.get("limitations")) is list and len(p["limitations"])==5,
        "R2C0 metric/evidence closure weakened")


def run(cmd,log,timeout=500):
    with log.open("w",encoding="utf-8") as output:
        try:p=subprocess.run([str(x) for x in cmd],cwd=ROOT,
                             stdout=output,stderr=subprocess.STDOUT,timeout=timeout)
        except subprocess.TimeoutExpired as exc:
            raise ValueError("process timeout "+str(log)) from exc
    if p.returncode:
        raise ValueError("Exit "+str(p.returncode)+": "+str(log)+"\n"+
                         "\n".join(log.read_text(errors="replace").splitlines()[-30:]))
    print("PASS "+str(log),flush=True)


def apphost(platform):
    p=ROOT/"benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0"/(
        "ChunkShift.Benchmarks.exe" if platform=="windows" else "ChunkShift.Benchmarks")
    require(p.is_file(),"Missing actual CLR native apphost")
    return p


def fixture_run_id(run_id):
    return run_id.replace("CORE011-CSM-004/RUN-","CORE011-CSM-001/G1J2C-",1)


def build_fixture(folder,run_id,platform,app):
    """A short-lived dedicated orchestrator is the only Python process reading DLL bytes."""
    folder.mkdir(parents=True,exist_ok=False)
    records={}
    try:
        for label,version in roslyn.VERSIONS.items():
            dll,record=roslyn.download_version(version)
            (folder/(label+".input")).write_bytes(dll)
            records[label]=record
        require(records["base"]["sha256"]!=records["target"]["sha256"],
                "Published source DLL versions identical")
        prov={
            "schema":"chunkshift.core011.g1j2-provenance.v1",
            "runId":fixture_run_id(run_id),
            "origin":"nuget-released-managed-pe",
            "sourcePath":"lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll",
            "repository":"https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/",
            "base":records["base"],"target":records["target"],
            "corpusClassification":"official-release-managed-DLL-version-pair;first-provenance-smoke",
            "representativeDotNetBinaryPair":False,
            "artificialByteEdits":False,"packageDigestsPreFrozen":False,
            "verdict":"NO_VERDICT_EXPLORATORY"
        }
        (folder/"provenance.json").write_text(json.dumps(prov,indent=2)+"\n",encoding="utf-8")
        run([app,"core011-g1j2-plan-build","--fixture-dir",folder,
             "--run-id",fixture_run_id(run_id)],folder.parent/"fixture-build.log",timeout=500)
        require((folder/"pair.json").is_file(),"Builder did not create CSM pair descriptor")
        print("E3-R2C0 fixture builder complete (real binaries removed)",flush=True)
    finally:
        for x in ("base.input","target.input"):
            (folder/x).unlink(missing_ok=True)


def prepare_check(dir,run_id,checkout):
    require(all(not (dir/(s+".input")).exists() for s in ("base","target")),
            "Original managed DLL leaked into measured process")
    prov=json.loads((dir/"provenance.json").read_text())
    pair=json.loads((dir/"pair.json").read_text())
    require(prov.get("schema")=="chunkshift.core011.g1j2-provenance.v1" and
        prov.get("origin")=="nuget-released-managed-pe" and
        prov.get("runId")==fixture_run_id(run_id) and
        prov.get("packageDigestsPreFrozen") is False and
        prov.get("representativeDotNetBinaryPair") is False and
        pair.get("schema")=="chunkshift.core011.g1j2-plan-pair.v1" and
        pair.get("builderCheckoutSha")==checkout and
        pair.get("runId")==fixture_run_id(run_id) and
        pair.get("origin")=="nuget-released-managed-pe" and
        pair.get("consumerSourceAccessAllowed") is False and
        pair.get("provenanceSha256")==digest(dir/"provenance.json") and
        type(pair.get("builderPid")) is int and pair["builderPid"]>0,
        "Invalid independent real NuGet DLL fixture identity")
    physical={}
    for label,version in roslyn.VERSIONS.items():
        meta=pair[label]; p=dir/(label+".csm")
        require(p.is_file() and not p.is_symlink() and
             p.stat().st_size==meta["csmBytes"] and
             digest(p)==meta["csmSha256"] and meta["manifestFile"]==label+".csm" and
             meta["sourceSha256"]==prov[label]["sha256"] and
             prov[label]["version"]==version and
             prov[label]["packageHashSource"]=="computed-from-downloaded-package;not-authenticated" and
             prov[label]["digestFreezeStatus"]=="observed-in-first-nondecision-run;not-independent-freeze",
             "Physical CSM hash/source DLL version/qualification changed")
        physical[label]={"originalDllSha256":meta["sourceSha256"],
             "originalDllBytes":meta["sourceBytes"],
             "csmSha256":meta["csmSha256"],"csmBytes":meta["csmBytes"],
             "chunkCount":meta["chunkCount"],"cblkCount":meta["cblkCount"]}
    require(physical["base"]["csmSha256"]!=physical["target"]["csmSha256"] and
            physical["base"]["originalDllSha256"]!=physical["target"]["originalDllSha256"],
            "Distinct published real DLL versions had identical CSM/source")
    expected=pair["expectedPlan"]
    require(expected["reusedChunks"]+expected["missingChunks"]==pair["target"]["chunkCount"] and
            expected["reusedBytes"]+expected["missingBytes"]==pair["target"]["sourceBytes"],
            "Actual public Plan target chunk/byte conservation violated")
    return pair,physical


def wait(path,p,seconds=120):
    until=time.monotonic()+seconds
    while time.monotonic()<until:
        if path.is_file():return
        if p.poll() is not None:raise ValueError("Real Plan target died before observer")
        time.sleep(.025)
    raise ValueError("Real Plan target ready timeout")


def measure(out,app,fixture,run_id):
    out.mkdir()
    ready,go,done=(out/name for name in ("ready","go","done"))
    consumer=out/"target.json"
    observer=out/"observer.json"
    tl=(out/"target.log").open("w",encoding="utf-8")
    t=subprocess.Popen([str(app),"core011-e3r2c0-real-plan-target",
          "--fixture-dir",str(fixture),"--run-id",run_id,"--result",str(consumer),
          "--ready",str(ready),"--go",str(go),"--done",str(done)],
          cwd=ROOT,stdout=tl,stderr=subprocess.STDOUT)
    o=None
    ol=None
    try:
        wait(ready,t)
        require(ready.read_text().strip()==str(t.pid),
                "Target PID mismatches native apphost PID")
        ol=(out/"observer.log").open("w",encoding="utf-8")
        o=subprocess.Popen([str(app),"core011-e3r2b2b-observer",
            "--target-pid",str(t.pid),"--run-id",run_id,
            "--result",str(observer),"--go",str(go),"--done",str(done),
            "--sample-ms","100"],cwd=ROOT,stdout=ol,stderr=subprocess.STDOUT)
        ores=o.wait(timeout=120)
        tres=t.wait(timeout=15)
        require(ores==0 and tres==0,
                "Real Plan/observer exit nonzero (target "+str(tres)+" observer "+str(ores)+")")
    finally:
        if o is not None and o.poll() is None:
            o.kill();o.wait()
        if t.poll() is None:
            t.kill();t.wait()
        if ol is not None:ol.close()
        tl.close()
    return t.pid,o.pid


def finite(x,name,positive=False):
    require(type(x) in (int,float) and math.isfinite(x) and
            (x>0 if positive else x>=0),"Invalid numeric measurement "+name)


def verify(output,run_id,checkout,platform,pair,physical,pids):
    folder=output/"measurement"
    target=json.loads((folder/"target.json").read_text())
    obs=json.loads((folder/"observer.json").read_text())
    tp,op=pids
    require(tp!=op and tp!=pair["builderPid"] and op!=pair["builderPid"] and
        target.get("schema")=="chunkshift.core011.e3r2c0-real-plan.v1" and
        target.get("runId")==run_id and target.get("checkoutSha")==checkout and
        target.get("variant")=="eager" and target.get("decisionRunsAllowed") is False and
        target.get("productVerdict")=="NOT_QUALIFIED" and
        target.get("sourceContentAccessible") is False and
        target.get("consumerPid")==tp and target.get("builderPid")==pair["builderPid"] and
        target.get("baseCsmSha256")==physical["base"]["csmSha256"] and
        target.get("targetCsmSha256")==physical["target"]["csmSha256"] and
        target.get("originalBaseDllSha256")==physical["base"]["originalDllSha256"] and
        target.get("originalTargetDllSha256")==physical["target"]["originalDllSha256"] and
        target.get("expectedReusedChunks")==pair["expectedPlan"]["reusedChunks"] and
        target.get("expectedMissingChunks")==pair["expectedPlan"]["missingChunks"] and
        target.get("planCalls")==64 and
        target.get("verdict")=="REAL_DLL_PLAN_BASELINE_CALIBRATION_ONLY" and
        type(target.get("naturalIdleMilliseconds")) is int and
        target["naturalIdleMilliseconds"]>=30000 and
        target.get("architecture")=="X64",
        "Real published DLL public Plan target semantics/process identity incorrect")
    for field in ("batchWallMilliseconds","batchCpuMilliseconds","allocatedBytes",
                  "gen2Collections","gcPauseMilliseconds","managedHeapAfterIdleBytes"):
        finite(target.get(field),field,positive=field in
              ("batchWallMilliseconds","managedHeapAfterIdleBytes"))
    require(obs.get("schema")=="chunkshift.core011.e3r2b2b-external.v1" and
            obs.get("experimentId")=="CORE011-CSM-004" and
            obs.get("runId")==run_id and
            obs.get("targetPid")==tp and obs.get("observerPid")==op and
            obs.get("samplePeriodMs")==100 and obs.get("verdict")==
            "EXTERNAL_BASELINE_CALIBRATION_ONLY","Real Plan external native observer mismatch")
    want="linux-smaps_rollup-private_clean_plus_private_dirty" if platform=="linux" else \
         "windows-psapi-ex2-private_working_set"
    trace=obs.get("samples")
    require(obs.get("metricSource")==want and type(trace) is list and
         type(obs.get("sampleCount")) is int and len(trace)==obs["sampleCount"] and
         len(trace)>=260 and obs.get("clockDurationMs",0)>=30000,
         "Incomplete real Plan external native trace")
    resident=[]
    rss=[]
    previous=-1
    for x in trace:
        require(x.get("residentMetricSource")==want,"Wrong source in native trace")
        for k in ("privateResidentBytes","processRssBytes","uptimeMilliseconds"):
            finite(x.get(k),k,positive=k in ("privateResidentBytes","processRssBytes"))
        require(x["uptimeMilliseconds"]>=previous and
            x["privateResidentBytes"]<=x["processRssBytes"],
            "Invalid real Plan private resident OS/time bound")
        previous=x["uptimeMilliseconds"]
        resident.append(x["privateResidentBytes"]);rss.append(x["processRssBytes"])
    require(obs["observedPrivateResidentMaximumBytes"]==max(resident) and
        obs["observedProcessRssMaximumBytes"]==max(rss),
        "Observed Plan native peak not recomputed from raw OS samples")
    return {"schema":"chunkshift.core011.e3r2c0-real-plan-summary.v1",
        "runId":run_id,"checkoutSha":checkout,"platform":platform,
        "originalCorpusSource":"official-NuGet-Roslyn-DLL;self-observed-hash-not-authenticated",
        "originalPairBuilderPid":pair["builderPid"],
        "consumerPid":tp,"externalObserverPid":op,
        "physicalCsm":physical,
        "expectedPlan":pair["expectedPlan"],
        "planCalls":64,"batchWallMilliseconds":target["batchWallMilliseconds"],
        "batchCpuMilliseconds":target["batchCpuMilliseconds"],
        "allocatedBytes":target["allocatedBytes"],
        "gen2Collections":target["gen2Collections"],
        "gcPauseMilliseconds":target["gcPauseMilliseconds"],
        "managedHeapAfterIdleBytes":target["managedHeapAfterIdleBytes"],
        "externalPrivateResidentSampledPeakBytes":max(resident),
        "externalRssSampledPeakBytes":max(rss),
        "externalNativeSampleCount":len(trace),
        "targetJsonSha256":digest(folder/"target.json"),
        "observerJsonSha256":digest(folder/"observer.json"),
        "decisionRunsAllowed":False,"productVerdict":"NOT_QUALIFIED"}


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--output-dir",required=True,type=Path)
    p.add_argument("--run-id",required=True)
    p.add_argument("--platform",choices=("linux","windows"),required=True)
    p.add_argument("--prepare-only",action="store_true")
    p.add_argument("--fixture-dir",type=Path)
    args=p.parse_args()
    require(RUNID.fullmatch(args.run_id) and args.run_id.endswith(args.platform+"-x64"),
            "Unknown real Plan run-id")
    if args.prepare_only:
        require(args.fixture_dir is not None,"Independent builder needs fixture dir")
        build_fixture(args.fixture_dir,args.run_id,args.platform,apphost(args.platform))
        return
    require(args.fixture_dir is None,"Consumer runner cannot pass builder-owned path")
    require(os.getenv("GITHUB_ACTIONS")=="true" and
            os.getenv("RUNNER_OS","").lower()==args.platform,
            "E3-R2C0 must run only on matching hosted OS")
    checkout=subprocess.check_output(["git","rev-parse","HEAD"],
                                     cwd=ROOT,text=True).strip()
    require(os.environ.get("GITHUB_SHA")==checkout and checkout[:8] in args.run_id,
            "Unpinned real Plan checkout")
    subprocess.run(["git","merge-base","--is-ancestor",PARENT,checkout],
                   cwd=ROOT,timeout=30,check=True)
    protocol(json.loads(CONTRACT.read_text()))
    guard=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
    r0.validate_contract(guard)
    r0.check_historical_contract(ROOT)
    original=(ROOT/"src/ChunkShift/Manifest/CsmReader.cs").read_bytes()
    r0.validate_source_pin(guard,original)
    require(hashlib.sha256(original).hexdigest()==SOURCE_SHA,
            "Eager source SHA pin violated")
    output=args.output_dir.resolve()
    require(not output.exists(),"Output cannot preexist a measurement")
    output.mkdir(parents=True)
    project="benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"
    run(["dotnet","restore",project],output/"restore.log",timeout=600)
    run(["dotnet","build",project,"-c","Release","--no-restore"],
        output/"build.log",timeout=600)
    app=apphost(args.platform)
    fixture=output/"fixture"
    run([sys.executable,Path(__file__),"--prepare-only","--platform",args.platform,
        "--run-id",args.run_id,"--output-dir",output,"--fixture-dir",fixture],
        output/"independent-builder.log",timeout=500)
    pair,physical=prepare_check(fixture,args.run_id,checkout)
    pids=measure(output/"measurement",app,fixture,args.run_id)
    summary=verify(output,args.run_id,checkout,args.platform,pair,physical,pids)
    (output/"summary.json").write_text(json.dumps(summary,indent=2)+"\n")
    print("E3-R2C0 REAL PUBLISHED DLL PUBLIC PLAN EXTERNAL RESIDENCY CALIBRATION PASS; NO ADOPT",flush=True)


if __name__=="__main__":
    main()
