#!/usr/bin/env python3
"""E3-R2-C1 frozen-source real DLL and openly real-derived reader calibration.

Materialization occurs in a *different Python process* from this measurement
orchestrator; each original source exists only before a separate .NET CSM
builder consumes and deletes it. Three process roles per case are independent.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"benchmarks/scripts"))
import core011_csm_g1j2d_forensic as roslyn
import core011_csm_g1j2h0_holdout_freeze as newton_source
import core011_csm_g1j2h1_holdout_eval as newton_frozen
import core011_csm_e3_guard as guard

CONTRACT=ROOT/"benchmarks/experiments/core011-csm-e3r2c1-corpus.v1.json"
PARENT="9153914ac73f46d3413334f1ff5cba9c77929bd6"
READER_SHA="a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
CASES=[
    ("roslyn-releases","official-released-managed-pe-pair",
     "roslyn-4.13.0-to-4.14.0","zero-reuse-single-block"),
    ("newtonsoft-releases","official-released-managed-pe-pair",
     "newtonsoft-13.0.2-to-13.0.3","zero-reuse-single-block"),
    ("roslyn-identical","original-released-dll-self-control",
     "roslyn-4.13.0-to-same-4.13.0","full-reuse-single-block"),
    ("roslyn-tail-insert","source-derived-binary-edit-not-a-released-dll",
     "roslyn-4.13.0-insert-4096bytes-from-4.14.0-at-base-len-minus-131072","mixed-reuse-single-block"),
    ("roslyn-tail-delete","source-derived-binary-edit-not-a-released-dll",
     "roslyn-4.13.0-delete-last-8192bytes","mixed-reuse-single-block"),
    ("roslyn-composite64","real-dll-concatenation-stress-not-a-single-real-file",
     "base-64x-roslyn-4.13.0-target-63x-roslyn-4.13.0-plus-1x-roslyn-4.14.0",
     "mixed-reuse-multiblock"),
]
RUNID=re.compile(r"^CORE011-CSM-004/RUN-[0-9]{8}-[0-9]+-[0-9a-f]{8}-(?:linux|windows)-x64$")


def require(ok,message):
    if not ok:raise ValueError(message)


def digest(path):
    h=hashlib.sha256()
    with open(path,"rb") as file:
        for chunk in iter(lambda:file.read(1048576),b""):h.update(chunk)
    return h.hexdigest()


def check_protocol(c):
    require(c.get("schema")=="chunkshift.core011.e3r2c1-corpus.v1" and
        c.get("stage")=="R2-C1-mixed-reuse-multiblock-unsealed-calibration" and
        c.get("experimentId")=="CORE011-CSM-004" and c.get("ownerIssue")==154 and
        c.get("parentEvidenceCommit")==PARENT and
        c.get("originalReaderSha256")==READER_SHA and
        c.get("sourceContracts")==[
            "benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json",
            "benchmarks/experiments/core011-csm-g1j2h1-frozen-corpus.v1.json"
        ] and
        c.get("sourceAuthenticity")==
        "previously SHA256-frozen official NuGet bytes, not cryptographic publisher signatures" and
        c.get("profile")=="fastcdc.gear.chunkshift.v1.64k" and
        c.get("suite")=="blake3-256" and c.get("includeBlockIndex") is True,
        "E3-R2-C1 original source/profile identity drift")
    require(c.get("cases")==[dict(key=k,sourceClass=cls,recipe=recipe,expected=exp)
        for k,cls,recipe,exp in CASES],"E3-R2-C1 corpus recipes/classification changed")
    require(c.get("activePlanMinimumMs")==4000 and
        c.get("externalObserverIntervalMs")==100 and
        c.get("minExternalActiveSamples")==25 and
        c.get("minExternalAllSamples")==320 and
        c.get("naturalIdleMinimumMs")==30000 and
        c.get("platforms")==["ubuntu-24.04","windows-2025"] and
        c.get("replicatesPerOs")==1 and
        c.get("variant")=="eager" and
        c.get("sourceOnlyInExitedBuilder") is True and
        c.get("consumerOriginalSourceForbidden") is True and
        c.get("candidateRunsAllowed") is False and
        c.get("decisionRunsAllowed") is False and
        c.get("budgetsLocked") is False and
        c.get("productionSourceChangesAllowed") is False and
        type(c.get("limitations")) is list and len(c["limitations"])==6,
        "E3-R2-C1 no-decision phase control changed")


def execute(cmd,log,timeout=600):
    with open(log,"w",encoding="utf-8") as out:
        try:
            z=subprocess.run(list(map(str,cmd)),cwd=ROOT,stdout=out,
                             stderr=subprocess.STDOUT,timeout=timeout)
        except subprocess.TimeoutExpired as err:
            raise ValueError("Command timed out: "+str(log)) from err
    if z.returncode:
        raise ValueError("Command failed "+str(z.returncode)+" "+str(log)+"\n"+
              "\n".join(Path(log).read_text(errors="replace").splitlines()[-25:]))


def apphost(platform):
    path=ROOT/"benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0"/(
        "ChunkShift.Benchmarks.exe" if platform=="windows" else "ChunkShift.Benchmarks")
    require(path.is_file(),"Actual native apphost executable missing")
    return path


def pinned_downloads():
    roslyn_contract=roslyn.verify_contract(json.loads(
        (ROOT/"benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json").read_text()))
    newton_contract=newton_frozen.validate_contract(json.loads(
        (ROOT/"benchmarks/experiments/core011-csm-g1j2h1-frozen-corpus.v1.json").read_text()))
    r=roslyn.fetch_original_payloads(roslyn_contract)
    n={}
    for role,version in zip(("base","target"),newton_source.VERSIONS):
        package=newton_source.fetch_package(version)
        n[role]=newton_frozen.verify_download(package,newton_contract[role])
    require(len(r["base"])==6604072 and len(r["target"])==6697264 and
            len(n["base"])==711952 and len(n["target"])==712464,
            "Previously locked true DLL content lengths drifted")
    return r,n


def prepare_all(output,run_id,platform):
    """Never invoked by measurement parent; short-lived source-carrying subprocess."""
    require(not output.exists(),"Refuse reused builder output")
    output.mkdir(parents=True)
    ros,newton=pinned_downloads()
    sources={
        "roslyn-releases":(ros["base"],ros["target"]),
        "newtonsoft-releases":(newton["base"],newton["target"]),
        "roslyn-identical":(ros["base"],ros["base"]),
        "roslyn-tail-insert":(
            ros["base"],
            ros["base"][:-131072]+ros["target"][:4096]+ros["base"][-131072:]
        ),
        "roslyn-tail-delete":(ros["base"],ros["base"][:-8192]),
    }
    app=apphost(platform)
    for case,source_class,recipe,expected in CASES:
        dir=output/case
        dir.mkdir()
        left=dir/"base.input"
        right=dir/"target.input"
        try:
            if case=="roslyn-composite64":
                with left.open("wb") as f:
                    for _ in range(64):f.write(ros["base"])
                with right.open("wb") as f:
                    for _ in range(63):f.write(ros["base"])
                    f.write(ros["target"])
            else:
                b,t=sources[case]
                left.write_bytes(b)
                right.write_bytes(t)
            cfg={
                "schema":"chunkshift.core011.e3r2c1-case.v1",
                "runId":run_id,"case":case,
                "sourceClass":source_class,"recipe":recipe,
                "baseSha256":digest(left),"targetSha256":digest(right),
                "baseBytes":left.stat().st_size,"targetBytes":right.stat().st_size,
                "decisionRunsAllowed":False,"candidateRunsAllowed":False,
                "sourcesAuthenticOnlyWhenClassIsReleased":source_class==
                    "official-released-managed-pe-pair",
            }
            (dir/"case.json").write_text(json.dumps(cfg,indent=2)+"\n")
            execute([app,"core011-e3r2c1-build","--fixture-dir",dir,
                "--run-id",run_id,"--case",case],
                dir/"build.log",timeout=600)
            require((dir/"pair.json").is_file(),"Missing externally built physical CSM")
        finally:
            left.unlink(missing_ok=True)
            right.unlink(missing_ok=True)
    print("E3-R2C1 source-bearing preparation finished and source files deleted",
          flush=True)


def preflight(platform,run_id):
    require(os.getenv("GITHUB_ACTIONS")=="true" and
            os.getenv("RUNNER_OS","").lower()==platform,
            "E3-R2-C1 must run on selected GitHub-hosted OS")
    require(RUNID.fullmatch(run_id) and run_id.endswith(platform+"-x64"),
            "Unknown run identity")
    commit=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,
                                   text=True).strip()
    require(os.getenv("GITHUB_SHA")==commit and commit[:8] in run_id,
            "Experiment GitHub checkout identity mismatch")
    subprocess.run(["git","merge-base","--is-ancestor",PARENT,commit],
                   cwd=ROOT,check=True,timeout=30)
    check_protocol(json.loads(CONTRACT.read_text()))
    old=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
    guard.validate_contract(old)
    guard.check_historical_contract(ROOT)
    guard.validate_source_pin(old,(ROOT/"src/ChunkShift/Manifest/CsmReader.cs").read_bytes())
    require(digest(ROOT/"src/ChunkShift/Manifest/CsmReader.cs")==READER_SHA,
            "E3 reader source SHA256 is modified")
    return commit


def check_fixture(output,run_id,checkout):
    result={}
    allbuilders=set()
    for case,source_class,recipe,expected in CASES:
        dir=output/case
        cfg=json.loads((dir/"case.json").read_text())
        desc=json.loads((dir/"pair.json").read_text())
        require(all(not (dir/(v+".input")).exists() for v in ("base","target")) and
            cfg.get("schema")=="chunkshift.core011.e3r2c1-case.v1" and
            cfg.get("case")==case and cfg.get("recipe")==recipe and
            cfg.get("sourceClass")==source_class and cfg.get("runId")==run_id and
            cfg.get("decisionRunsAllowed") is False and
            cfg.get("candidateRunsAllowed") is False and
            desc.get("schema")=="chunkshift.core011.e3r2c1-pair.v1" and
            desc.get("checkoutSha")==checkout and
            desc.get("case")==case and desc.get("runId")==run_id and
            desc.get("caseJsonSha256")==digest(dir/"case.json") and
            desc.get("sourceClass")==source_class and
            desc.get("productVerdict")=="NOT_QUALIFIED",
            "Corrupt source-free materialized case descriptor")
        builder=desc.get("builderPid")
        require(type(builder) is int and builder>0 and builder not in allbuilders,
                "Builder process identity not distinct")
        allbuilders.add(builder)
        for part in ("base","target"):
            item=desc[part]
            file=dir/(part+".csm")
            require(file.is_file() and not file.is_symlink() and
                digest(file)==item["csmSha256"] and
                file.stat().st_size==item["csmBytes"] and
                item["manifestFile"]==part+".csm" and
                item["sourceSha256"]==cfg[part+"Sha256"] and
                item["sourceBytes"]==cfg[part+"Bytes"] and
                item["chunkCount"]>=1 and item["cblkCount"]>=1,
                "Unexpected original physical CSM or source content identity")
        plan=desc["expectedPlan"]
        require(plan["reusedChunks"]+plan["missingChunks"]==
                desc["target"]["chunkCount"] and
            plan["reusedBytes"]+plan["missingBytes"]==
                desc["target"]["sourceBytes"],
            "Plan chunk/byte conservation violated")
        n=plan["reusedChunks"]
        total=desc["target"]["chunkCount"]
        multi=desc["base"]["cblkCount"]>=2 and desc["target"]["cblkCount"]>=2
        if expected=="zero-reuse-single-block":
            require(n==0 and not multi,"Previously frozen original release reuse drift")
        elif expected=="full-reuse-single-block":
            require(n==total and plan["missingChunks"]==0 and not multi,
                    "Original released DLL full-reuse control failed")
        elif expected=="mixed-reuse-single-block":
            require(0<n<total and not multi,
                    "Real-derived tail modification failed mixed reuse qualification")
        elif expected=="mixed-reuse-multiblock":
            require(0<n<total and multi and
                    desc["target"]["chunkCount"]>4096,
                    "Real-derived composite did not cross physical CBLK boundary")
        result[case]={"builderPid":builder,"sourceClass":source_class,
            "recipe":recipe,"expected":expected,
            "base":desc["base"],"target":desc["target"],"plan":plan,
            "configSha256":digest(dir/"case.json"),
            "pairSha256":digest(dir/"pair.json")}
    return result,allbuilders


def wait(path,process,timeout=90):
    until=time.monotonic()+timeout
    while time.monotonic()<until:
        if path.exists():return
        if process.poll() is not None:raise ValueError("Target died before ready marker")
        time.sleep(.02)
    raise ValueError("Target/observer coordination ready timeout")


def measure(app,fixtures,output,run_id,case):
    dir=output/case
    dir.mkdir()
    marks={k:dir/k for k in ("ready","go","active-start","active-end","done")}
    tp=(dir/"target.log").open("w",encoding="utf-8")
    command=[app,"core011-e3r2c1-target",
        "--fixture-dir",fixtures/case,"--run-id",run_id,"--case",case,
        "--result",dir/"target.json","--ready",marks["ready"],
        "--go",marks["go"],"--active-start",marks["active-start"],
        "--active-end",marks["active-end"],"--done",marks["done"]]
    target=subprocess.Popen(list(map(str,command)),cwd=ROOT,
                            stdout=tp,stderr=subprocess.STDOUT)
    observer=None
    opl=None
    try:
        wait(marks["ready"],target)
        require(marks["ready"].read_text().strip()==str(target.pid),
                "Native apphost/CLR target PID mismatch")
        opl=(dir/"observer.log").open("w",encoding="utf-8")
        observation=[app,"core011-e3r2c1-observer",
            "--target-pid",target.pid,"--run-id",run_id,"--case",case,
            "--result",dir/"observer.json","--go",marks["go"],
            "--active-start",marks["active-start"],"--active-end",marks["active-end"],
            "--done",marks["done"],"--sample-ms",100]
        observer=subprocess.Popen(list(map(str,observation)),cwd=ROOT,
                                  stdout=opl,stderr=subprocess.STDOUT)
        rc_observer=observer.wait(timeout=95)
        rc_target=target.wait(timeout=10)
        require(rc_observer==0 and rc_target==0,
                "Native observer or target failed: target="+str(rc_target)+
                ", external="+str(rc_observer))
    finally:
        if observer and observer.poll() is None:
            observer.kill();observer.wait()
        if target.poll() is None:
            target.kill();target.wait()
        tp.close()
        if opl:opl.close()
    return target.pid,observer.pid


def number(x,key,positive=False):
    require(type(x) in (int,float) and math.isfinite(x) and
            (x>0 if positive else x>=0),"Invalid "+key+" metric")


def validate_observation(out,fixture,run,checkout,platform,identity,pids):
    t=json.loads((out/"target.json").read_text())
    o=json.loads((out/"observer.json").read_text())
    tp,op=pids
    require(tp!=op and tp!=identity["builderPid"] and
        op!=identity["builderPid"] and
        t.get("schema")=="chunkshift.core011.e3r2c1-target.v1" and
        t.get("experimentId")=="CORE011-CSM-004" and
        t.get("runId")==run and t.get("case")==out.name and
        t.get("checkoutSha")==checkout and
        t.get("sourceClass")==identity["sourceClass"] and
        t.get("targetPid")==tp and t.get("builderPid")==identity["builderPid"] and
        t.get("variant")=="eager" and
        t.get("candidateRunsAllowed") is False and
        t.get("decisionRunsAllowed") is False and
        t.get("sourceAbsent") is True and
        t.get("internalNativeSampling") is False and
        t.get("productVerdict")=="NOT_QUALIFIED" and
        t.get("baseCsmSha256")==identity["base"]["csmSha256"] and
        t.get("targetCsmSha256")==identity["target"]["csmSha256"] and
        t.get("reusedChunks")==identity["plan"]["reusedChunks"] and
        t.get("missingChunks")==identity["plan"]["missingChunks"] and
        t.get("baseCblkCount")==identity["base"]["cblkCount"] and
        t.get("targetCblkCount")==identity["target"]["cblkCount"] and
        t.get("verdict")=="EXTERNAL_REAL_DERIVED_CORPUS_CALIBRATION_ONLY",
        "Reader target case/provenance/oracle or decision gate invalid")
    for k in ("activePlanCalls","activeWallMilliseconds","activeCpuMilliseconds",
              "activeAllocatedBytes","activeGen2","activeGcPauseMilliseconds",
              "managedHeapAfterIdleBytes","naturalIdleMilliseconds"):
        number(t.get(k),k,positive=k in
               ("activePlanCalls","activeWallMilliseconds","managedHeapAfterIdleBytes"))
    require(t["activeWallMilliseconds"]>=4000 and
        t["naturalIdleMilliseconds"]>=30000 and
        type(t["activePlanCalls"]) is int and t["activePlanCalls"]>=5,
        "Active public Plan phase or natural idle aborted")
    require(o.get("schema")=="chunkshift.core011.e3r2c1-observer.v1" and
        o.get("runId")==run and o.get("case")==out.name and
        o.get("targetPid")==tp and o.get("observerPid")==op and
        o.get("samplePeriodMs")==100 and
        o.get("verdict")=="EXTERNAL_NONDECISION_NATIVE_TRACE",
        "C1 external OS observer provenance mismatch")
    desired=("linux-smaps_rollup-private_clean_plus_private_dirty"
             if platform=="linux" else "windows-psapi-ex2-private_working_set")
    trace=o.get("samples")
    require(o.get("nativeMetricSource")==desired and
            isinstance(trace,list) and len(trace)==o.get("sampleCount") and
            len(trace)>=320 and
            o.get("clockDurationMilliseconds",0)>=34000,
            "Missing native C1 external full 4s-active +30s-idle trace")
    mx=0;rss=0;active=0;at=-1
    for sample in trace:
        require(sample.get("residentMetricSource")==desired,
                "Native counter not genuine OS source for target PID")
        for key in ("privateResidentBytes","processRssBytes","uptimeMilliseconds"):
            number(sample.get(key),key,positive=key!="uptimeMilliseconds")
        require(sample["privateResidentBytes"]<=sample["processRssBytes"] and
                sample["uptimeMilliseconds"]>=at,"Private/RSS/timestamp contradiction")
        at=sample["uptimeMilliseconds"]
        mx=max(mx,sample["privateResidentBytes"])
        rss=max(rss,sample["processRssBytes"])
        if sample.get("phase")=="active-plan":active+=1
    require(active==o["activePlanSampleCount"] and active>=25 and
            mx==o["observedPrivateResidentMaximumBytes"] and
            rss==o["observedRssMaximumBytes"],
            "Active phase native sample count / raw trace maximum mismatch")
    return {"case":out.name,"sourceClass":identity["sourceClass"],
        "baseCblkCount":identity["base"]["cblkCount"],
        "targetCblkCount":identity["target"]["cblkCount"],
        "targetChunks":identity["target"]["chunkCount"],
        "reusedChunks":identity["plan"]["reusedChunks"],
        "missingChunks":identity["plan"]["missingChunks"],
        "activePlanCalls":t["activePlanCalls"],
        "activeWallMilliseconds":t["activeWallMilliseconds"],
        "activeCpuMilliseconds":t["activeCpuMilliseconds"],
        "activeAllocatedBytes":t["activeAllocatedBytes"],
        "activeGen2":t["activeGen2"],
        "activeGcPauseMilliseconds":t["activeGcPauseMilliseconds"],
        "externalPrivateResidentSampledMaximumBytes":mx,
        "externalRssSampledMaximumBytes":rss,
        "externalActivePlanSamples":active,
        "externalTotalSamples":len(trace),
        "targetPid":tp,"observerPid":op,
        "targetSha256":digest(out/"target.json"),
        "observerSha256":digest(out/"observer.json")}


def main():
    a=argparse.ArgumentParser()
    a.add_argument("--output-dir",required=True,type=Path)
    a.add_argument("--platform",required=True,choices=("linux","windows"))
    a.add_argument("--run-id",required=True)
    a.add_argument("--prepare-only",action="store_true")
    a.add_argument("--fixtures",type=Path)
    v=a.parse_args()
    require(RUNID.fullmatch(v.run_id) and v.run_id.endswith(v.platform+"-x64"),
            "Invalid frozen C1 run id")
    if v.prepare_only:
        require(v.fixtures is not None and os.environ.get("GITHUB_ACTIONS")=="true",
                "Builder subprocess role must be explicit and GitHub-hosted")
        prepare_all(v.fixtures,v.run_id,v.platform)
        return
    require(v.fixtures is None,"Consumer orchestrator cannot accept builder paths")
    checkout=preflight(v.platform,v.run_id)
    dest=v.output_dir.resolve()
    require(not dest.exists(),"C1 refuses reused benchmark output")
    dest.mkdir(parents=True)
    project="benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"
    execute(["dotnet","restore",project],dest/"restore.log")
    execute(["dotnet","build",project,"-c","Release","--no-restore"],
            dest/"build.log")
    app=apphost(v.platform)
    fixtures=dest/"fixtures"
    execute([sys.executable,Path(__file__),"--prepare-only","--platform",v.platform,
             "--run-id",v.run_id,"--output-dir",dest,"--fixtures",fixtures],
            dest/"source-builder.log",timeout=900)
    identities,builder_pids=check_fixture(fixtures,v.run_id,checkout)
    measurements=dest/"measurements"
    measurements.mkdir()
    outputs=[]
    all_pids=set(builder_pids)
    for case,*_ in CASES:
        processes=measure(app,fixtures,measurements,v.run_id,case)
        require(processes[0]!=processes[1] and
                processes[0] not in all_pids and processes[1] not in all_pids,
                "Nonunique builder/target/external-observer process identity")
        all_pids.update(processes)
        row=validate_observation(measurements/case,fixtures/case,v.run_id,
                                 checkout,v.platform,identities[case],processes)
        outputs.append(row)
        print("E3-R2C1 "+case+": chunks reused="+str(row["reusedChunks"])+
              " target CBLK="+str(row["targetCblkCount"])+
              " private sampled peak MiB="+str(round(
                row["externalPrivateResidentSampledMaximumBytes"]/1048576,3)),
              flush=True)
    require(len(outputs)==6 and len(all_pids)==18,
            "Incomplete six independent builder-target-observer triples")
    outcome={"schema":"chunkshift.core011.e3r2c1-corpus-summary.v1",
        "experimentId":"CORE011-CSM-004","runId":v.run_id,
        "checkoutSha":checkout,"platform":v.platform,
        "fixtureIdentity":identities,"cases":outputs,
        "uniqueProcessIdentities":len(all_pids),
        "allSourceFilesDeleted":True,"productVerdict":"NOT_QUALIFIED",
        "decisionRunsAllowed":False,"budgetsLocked":False,
        "qualification":"UNSEALED_REAL_DERIVED_CORPUS_TECHNICAL_CALIBRATION_PASS",
        "scientificCaveat":"Two original release pairs, one identical control, two derived edits, one 64x concatenation stress: not a representative real-world versioned binary distribution or an instantaneous RSS upper bound."}
    (dest/"summary.json").write_text(json.dumps(outcome,indent=2)+"\n")
    print("E3-R2C1 six-case native externally observed Plan corpus PASS; NO ADOPT",flush=True)


if __name__=="__main__":
    main()
