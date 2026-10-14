#!/usr/bin/env python3
"""CORE011-CSM-004 E3-R2-B1 eager only: source-free real private-resident calibration."""
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

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "benchmarks/experiments/core011-csm-e3r2b-residency.v1.json"
SHA_READER = "a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
BLOB_READER = "ff56ccddaa0efc9970fd0b019e20523d6c2933d6"
PARENT = "d20a7fa0a6591a697c268e46c23752df750b6306"
RUN_ID = re.compile(r"^CORE011-CSM-004/RUN-[0-9]{8}-[0-9]+-[0-9a-f]{8}-(?:linux|windows)-x64$")
CELLS = ((1, 0, 30), (1, 16, 30), (384, 0, 300), (384, 16, 300))


def mod(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    x = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(x)
    return x


r0 = mod(ROOT / "benchmarks/scripts/core011_csm_e3_guard.py", "e3r2b_parent_guard")
j = mod(ROOT / "benchmarks/scripts/core011_csm_g1j_contract.py", "e3r2b_builder_oracle")


def check(ok, reason):
    if not ok:
        raise ValueError(reason)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def strict_contract(c):
    check(c.get("schema") == "chunkshift.core011.e3r2b-resident-calibration.v1" and
          c.get("experimentId") == "CORE011-CSM-004" and
          c.get("stage") == "R2-B1-cross-platform-residency" and
          c.get("ownerIssue") == 154 and
          c.get("sourceReaderBlobSha1") == BLOB_READER and
          c.get("sourceReaderSha256") == SHA_READER and c.get("parentHead") == PARENT and
          c.get("productionCandidate") == "eager" and
          c.get("decisionRunsAllowed") is False and c.get("budgetsLocked") is False,
          "E3-R2B contract source/adoption gate changed")
    check(c.get("hostedPlatforms") == ["ubuntu-24.04", "windows-2025"] and
          c.get("sizesMiB") == [1, 384] and
          c.get("fixtureSuite") == "blake3" and c.get("fixtureBidx") is True and
          c.get("heldReaders") == [0, 16] and
          c.get("idleSecondsBySize") == {"1": 30, "384": 300} and
          c.get("windowsMetricSource") == "windows-psapi-ex2-private_working_set" and
          c.get("linuxMetricSource") == "linux-smaps_rollup-private_clean_plus_private_dirty",
          "E3-R2B memory metrics or matrix changed")
    check(c.get("mandatoryMetrics") == [
        "privateResidentBytes", "processRssBytes", "managedHeapBytes",
        "processPrivateDiagnosticBytes", "uptimeMilliseconds",
        "gen2Collections", "totalGcPauseMs"], "E3-R2B metric coverage missing")
    check(c.get("strictSemantics") == {
        "distinctBuilderConsumerPid": True, "sourceFileAbsent": True,
        "independentFreshProcessEachCell": True, "holdExactly16PartiallyRead": True,
        "sharedImmutableFixtureBytes": True, "requiredOriginalReaderSha": True,
        "actualNatural300SecondIdle": True,
    } and c.get("forbidden") == {
        "adopt": True, "decisionThresholds": True, "candidates": True,
        "claimSixteenParallelCPUs": True, "interpretPrivateMemorySizeAsResident": True,
    }, "E3-R2B source-free decision closure weakened")
    check(type(c.get("limitations")) is list and len(c["limitations"]) == 5,
          "E3-R2B caveats removed")
    return True


def nonnegative(v, name, positive=False):
    check(type(v) in (int, float) and math.isfinite(v) and
          (v > 0 if positive else v >= 0), "Missing/invalid metric: " + name)


def validate_cell(o, desc, head, run_id, os_name, held, idle):
    check(o.get("schema") == "chunkshift.core011.e3r2b-residency-cell.v1" and
          o.get("experimentId") == "CORE011-CSM-004" and
          o.get("runId") == run_id and o.get("checkoutSha") == head and
          o.get("variant") == "eager" and
          o.get("stage") == "baseline-residency-only" and
          o.get("decisionRunsAllowed") is False and
          o.get("verdict") == "RESIDENT_BASELINE_CALIBRATION_ONLY",
          "Non-baseline R2B sample")
    check(o.get("fixtureSha256") == desc["manifestSha256"] and
          o.get("fixtureBytes") == desc["manifestBytes"] and
          o.get("fixtureContentBytes") == desc["contentBytes"] and
          o.get("chunkCount") == desc["chunkCount"] and
          o.get("blockCount") == desc["cblkCount"] and
          o.get("producerPid") == desc["builderPid"] and
          type(o.get("consumerPid")) is int and o["consumerPid"] > 0 and
          o["consumerPid"] != o["producerPid"] and
          o.get("sourceFileAbsent") is True and
          o.get("requestedHeldReaders") == held and
          o.get("actualHeldReaders") == held and o.get("idleSeconds") == idle and
          o.get("architecture") == "X64" and
          type(o.get("processorCount")) is int and o["processorCount"] > 0,
          "Fixture, process or held-reader identity mismatch")
    samples=o.get("samples")
    phases=["warmed-no-held-readers","idle-0-natural","idle-30-natural"]
    if idle==300:
        phases.append("idle-300-natural")
    phases+=["after-dispose-natural","forced-gc-diagnostic"]
    check(type(samples) is list and [x.get("phase") for x in samples] == phases,
          "R2B natural-idle/dispose/forced-GC phases incomplete")
    want=("linux-smaps_rollup-private_clean_plus_private_dirty" if os_name == "linux"
          else "windows-psapi-ex2-private_working_set")
    prev=-1
    for s in samples:
        check(s.get("residentMetricSource")==want, "Wrong private resident telemetry source")
        for metric in ("privateResidentBytes","processRssBytes","managedHeapBytes",
                       "processPrivateDiagnosticBytes","uptimeMilliseconds",
                       "gen2Collections","totalGcPauseMs"):
            nonnegative(s.get(metric),metric,positive=metric in
                        ("privateResidentBytes","processRssBytes"))
        check(s["privateResidentBytes"] <= s["processRssBytes"] and
              s["uptimeMilliseconds"] >= prev,
              "Invalid resident upper bound or monotonic uptime")
        prev=s["uptimeMilliseconds"]
        if os_name=="linux":
            for key in ("processPssBytes","privateCleanBytes","privateDirtyBytes"):
                nonnegative(s.get(key),key)
            check(s["privateResidentBytes"] ==
                  s["privateCleanBytes"] + s["privateDirtyBytes"],
                  "Linux smaps_rollup private sum mismatch")
            check(s.get("privateCommitBytes") is None,"Linux private commit wrongly inferred")
        else:
            nonnegative(s.get("privateCommitBytes"),"Windows private commit",positive=True)
            check(s.get("privateCleanBytes") is None and
                  s.get("privateDirtyBytes") is None and
                  s.get("processPssBytes") is None,
                  "Windows data disguised as Linux smaps")
    check(samples[2]["uptimeMilliseconds"]-samples[1]["uptimeMilliseconds"]>=29000,
          "Not 30 seconds of natural idle")
    if idle==300:
        check(samples[3]["uptimeMilliseconds"]-samples[1]["uptimeMilliseconds"]>=295000,
              "Not 300 seconds of natural idle")
    return {
        "sizeMiB":desc["contentBytes"]//1048576,
        "heldReaders":held,
        "consumerPid":o["consumerPid"],
        "privateResidentBytes":[s["privateResidentBytes"] for s in samples],
        "workingSetBytes":[s["processRssBytes"] for s in samples],
        "phases":phases,
    }


def run(cmd, cwd, logfile, timeout=900):
    with logfile.open("w",encoding="utf-8") as f:
        try:
            p=subprocess.run(list(map(str,cmd)),cwd=cwd,
                             stdout=f,stderr=subprocess.STDOUT,timeout=timeout)
        except subprocess.TimeoutExpired as e:
            raise ValueError("process timeout, log: "+str(logfile)) from e
    if p.returncode:
        raise ValueError("command exit "+str(p.returncode)+": "+" ".join(map(str,cmd))+
                         "\n"+"\n".join(logfile.read_text(errors="replace").splitlines()[-35:]))
    print("PASS "+ " ".join(map(str,cmd)),flush=True)


def preflight(run_id, os_name):
    check(os.environ.get("GITHUB_ACTIONS")=="true",
          "E3-R2B runs exclusively on GitHub-hosted Actions")
    check(os_name in ("linux","windows") and RUN_ID.fullmatch(run_id),
          "Invalid R2B RunId or OS")
    check(run_id.endswith(os_name+"-x64"),"R2B wrong OS suffix")
    head=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip()
    check(os.environ.get("GITHUB_SHA")==head and head[:8] in run_id,
          "R2B unpinned checkout")
    check(os.environ.get("RUNNER_OS","").lower()==os_name,
          "R2B runner OS does not match actual platform")
    subprocess.run(["git","merge-base","--is-ancestor",PARENT,head],
                   cwd=ROOT,timeout=30,check=True)
    strict_contract(json.loads(CONTRACT.read_text(encoding="utf-8")))
    r0c=json.loads((ROOT/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
    r0.validate_contract(r0c)
    r0.check_historical_contract(ROOT)
    source=(ROOT/"src/ChunkShift/Manifest/CsmReader.cs").read_bytes()
    r0.validate_source_pin(r0c,source)
    check(hashlib.sha256(source).hexdigest()==SHA_READER,
          "Reader source unexpectedly modified")
    return head


def evaluate(output,run_id,os_name,head):
    artifacts=[]
    consumers=set()
    producers=set()
    for size in (1,384):
        fixture=output/("fixture-"+str(size))
        descriptor=json.loads((fixture/"fixture.json").read_text())
        j.validate_manifest_only_fixture(descriptor,fixture)
        check(descriptor["builderCheckoutSha"]==head and
              descriptor["contentBytes"]==size*1048576 and
              descriptor["hashSuite"]=="blake3" and descriptor["hasBlockIndex"] is True and
              not (fixture/"source.bin").exists(),
              "Wrong CSM source-free fixture provenance")
        if size==384:
            check(descriptor["chunkCount"]==5052 and
                  descriptor["cblkCount"]==2 and
                  descriptor["manifestBytes"]==182366,
                  "384-MiB two-CBLK source digest baseline changed")
        producers.add(descriptor["builderPid"])
        for msize,held,idle in CELLS:
            if msize!=size:continue
            label=str(size)+"-held"+str(held)
            path=output/(label+".json")
            o=json.loads(path.read_text())
            val=validate_cell(o,descriptor,head,run_id,os_name,held,idle)
            check(val["consumerPid"] not in consumers and
                  val["consumerPid"] not in producers,
                  "Non-independent consumer process")
            consumers.add(val["consumerPid"])
            val["rawSha256"]=digest(path)
            val["fixtureSha256"]=descriptor["manifestSha256"]
            artifacts.append(val)
    check(len(producers)==2 and len(consumers)==4 and len(artifacts)==4,
          "Expected precisely 2 builder and 4 consumer OS processes")
    return {
        "schema":"chunkshift.core011.e3r2b-residency-summary.v1",
        "experimentId":"CORE011-CSM-004","runId":run_id,
        "checkoutSha":head,"platform":os_name,"cells":artifacts,
        "sourceReaderVariant":"eager","decisionRunsAllowed":False,
        "productVerdict":"NOT_QUALIFIED","builderCount":2,"consumerCount":4,
        "limitations":[
            "Synthetic eager-only memory calibration, not candidate A/B decision.",
            "Private resident samples only; no resident high-water peak certified.",
            "Control and held-16 are distinct processes; JIT/GC noise remains.",
            "Only blake3/BIDX, no real-versioned product Plan or NativeAOT."
        ],
    }


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--run-id",required=True)
    p.add_argument("--platform",choices=("linux","windows"),required=True)
    p.add_argument("--output-dir",required=True,type=Path)
    p.add_argument("--validate-only",action="store_true")
    a=p.parse_args()
    output=a.output_dir.resolve()
    try:
        head=preflight(a.run_id,a.platform)
        if not a.validate_only:
            output.mkdir(parents=True,exist_ok=False)
            project="benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"
            run(["dotnet","restore",project],ROOT,output/"restore.log",timeout=600)
            run(["dotnet","build",project,"-c","Release","--no-restore"],ROOT,
                output/"build.log",timeout=700)
            prefix=["dotnet","run","--no-build","-c","Release","--project",project,"--"]
            for size in (1,384):
                fixture=output/("fixture-"+str(size))
                fixture.mkdir()
                builder_id=("CORE011-CSM-001/G1J1-E3R2B-"+str(size)+"-"+a.platform+
                            "-"+str(os.getpid()))
                run(prefix+["core011-g1j1-build",
                    "--output-dir",fixture,"--size-mib",size,"--suite","blake3",
                    "--bidx","true","--run-id",builder_id],
                    ROOT,output/("builder-"+str(size)+".log"),timeout=1000)
                desc=json.loads((fixture/"fixture.json").read_text())
                j.validate_manifest_only_fixture(desc,fixture)
                check(not (fixture/"source.bin").exists(),
                      "Original content leaked into consumer")
                for msize,held,idle in CELLS:
                    if msize!=size:continue
                    label=str(size)+"-held"+str(held)
                    run(prefix+["core011-e3r2b-residency","--fixture-dir",fixture,
                        "--output",output/(label+".json"),
                        "--hold-readers",held,"--idle-seconds",idle,
                        "--run-id",a.run_id],
                        ROOT,output/(label+".log"),timeout=idle+180)
        result=evaluate(output,a.run_id,a.platform,head)
        (output/"summary.json").write_text(json.dumps(result,indent=2)+"\n")
        print("E3-R2B "+a.platform+" baseline private resident PASS: "
              "2 builders/4 isolated consumers; no product verdict",flush=True)
    except (ValueError,OSError,subprocess.CalledProcessError) as error:
        print("E3-R2B FAIL-CLOSED: "+str(error),file=sys.stderr)
        raise SystemExit(2) from error


if __name__=="__main__":
    main()
