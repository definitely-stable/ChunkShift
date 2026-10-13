#!/usr/bin/env python3
"""CORE011 G1-J2E: actual CSM chunks versus independently verified byte matches.

GitHub-hosted only, frozen prior J2C NuGet/DLL/CSM SHA, no production changes.
The CSM chunk records come from an independent physical-CBLK parser AND from
production ChunkScanner; the builder verifies exact public Plan semantics.
"""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

import core011_csm_g1j2d_forensic as d
import core011_csm_g1j2c_roslyn as c
import core011_csm_g1j1_isolation as j

SCHEMA = "chunkshift.core011.g1j2e-boundary-forensic.v1"
PROTOCOL = "chunkshift.core011.g1j2e-frozen.v1"
CSM_SHA = {
    "base": "da64f7ac2bed330730db249bd9b40614474da9b3d9bad648f5d93b18f58fd009",
    "target": "83ab6375f6c50db29c89cbe48f3f5845dedd6f99e0f79fea471ec1bd152968ed",
}
EXPECTED_COUNTS = {"base": 74, "target": 81}
EXPECTED_SOURCE_BYTES = {"base": 6604072, "target": 6697264}
EXPECTED_WINDOW_ANCHORS = {4096:2069,16384:634,65536:275}
EXPECTED_WINDOW_COVERAGE = {4096:798464,16384:291328,65536:135680}


def must(value, message):
    if not value:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def parse_csm_cblk(physical, expected_bytes, expected_count):
    """Independent strict v1 CBLK entry parser; .NET VerifyManifestAsync is separate.

    Only supports known frozen v1 CORE/CBLK/CEND/BIDX/FOOT sections and trailer.
    """
    must(physical[:4] == b"CSM1" and len(physical) >= 96, "not physical CSM v1")
    offset=32
    entries=[]
    cblk=0
    seen=[]
    seen_trailer=False
    while offset < len(physical):
        if physical[offset:offset+4] == b"CSMT":
            must(len(physical)-offset == 64, "noncanonical CSM v1 trailer size")
            seen_trailer=True
            break
        must(offset+16 <= len(physical)-64, "truncated CSM section header")
        kind,flags,size=struct.unpack_from("<4sIQ",physical,offset)
        must(kind in (b"CORE",b"CBLK",b"CEND",b"BIDX",b"FOOT") and flags in (0,1),
             "unexpected CSM section/flags")
        start=offset+16
        end=start+size
        must(end <= len(physical)-64, "CSM section payload overlaps trailer")
        seen.append(kind.decode("ascii"))
        if kind == b"CBLK":
            cblk += 1
            must(size >= 28, "CBLK too short")
            count,reserved,first_index,first_offset=struct.unpack_from("<IIQQ",physical,start)
            must(reserved == 0 and 1 <= count <= 4096, "invalid frozen CBLK prefix")
            must(size == 24+count*36+4, "invalid CBLK record shape")
            must(first_index == len(entries), "CBLK first chunk index discontinuity")
            previous_end=entries[-1]["offset"]+entries[-1]["length"] if entries else 0
            must(first_offset == previous_end, "CBLK first byte offset discontinuity")
            ids=start+24
            lens=ids+count*32
            cursor=first_offset
            for i in range(count):
                chunk_id=physical[ids+i*32:ids+(i+1)*32].hex()
                length=struct.unpack_from("<I",physical,lens+i*4)[0]
                must(0 < length <= 2**31-1 and len(chunk_id)==64,
                     "invalid chunk ID/length")
                entries.append({"index":len(entries),"offset":cursor,
                                "length":length,"chunkId":chunk_id})
                cursor += length
        offset=end
    must(seen_trailer and seen[:2]==["CORE","CBLK"] and
         seen[-1]=="FOOT" and "CEND" in seen, "invalid CSM semantic section order")
    must(len(entries)==expected_count and cblk==1, "original CSM chunk count/CBLK drift")
    must(entries[0]["offset"]==0 and
         entries[-1]["offset"]+entries[-1]["length"]==expected_bytes,
         "manifest chunk offsets do not cover original bytes")
    return entries


def parse_scanner_tsv(path, expected_bytes, expected_count):
    entries=[]
    end=0
    for line in path.read_text().splitlines():
        parts=line.split("\t")
        must(len(parts)==3, "scanner row must have offset, length, id")
        off,length=int(parts[0]),int(parts[1])
        chunk_id=parts[2]
        must(off==end and 0<length<=2**31-1 and len(chunk_id)==64 and
             all(x in "0123456789abcdef" for x in chunk_id),
             "scanner chunk gap/id invalid")
        entries.append({"index":len(entries),"offset":off,"length":length,"chunkId":chunk_id})
        end=off+length
    must(end==expected_bytes and len(entries)==expected_count,
         "scanner chunks mismatch frozen source length/count")
    return entries


def verified_windows(base,target,size,stride=256):
    """Complete source-grid matching windows, first byte-verified target offset."""
    must(size in EXPECTED_WINDOW_ANCHORS and stride==256,
         "unregistered J2D source-window protocol")
    mapping={}
    for pos,h in d.rolling(base,size):
        if pos % stride==0:
            mapping.setdefault(h,[]).append(pos)
    found={}
    for target_at,h in d.rolling(target,size):
        candidates=mapping.get(h)
        if not candidates:
            continue
        sample=target[target_at:target_at+size]
        keep=[]
        for pos in candidates:
            if base[pos:pos+size] == sample:
                found[pos]=target_at
            else:
                keep.append(pos)
        if keep:
            mapping[h]=keep
        else:
            del mapping[h]
            if not mapping:
                break
    intervals=[(pos,pos+size) for pos in found]
    must(len(found)==EXPECTED_WINDOW_ANCHORS[size] and
         d.merged_coverage(intervals)==EXPECTED_WINDOW_COVERAGE[size],
         "J2D byte window oracle drift; source bytes or parser differ")
    return found


def overlap_union(intervals,lo,hi):
    intersections=[(max(lo,a),min(hi,b)) for a,b in intervals
                   if a < hi and b > lo]
    return d.merged_coverage(intersections) if intersections else 0


def chunk_diagnostic(source,other,chunks,other_chunks,verified_4k):
    intervals=[(pos,pos+4096) for pos in verified_4k]
    # Whole chunk exact bytes must be searched without sampling, including the
    # >64 KiB majority, so no inference from window coverage is needed.
    by_exact={(x["offset"],x["length"]):x for x in other_chunks}
    by_length={}
    for x in other_chunks:
        by_length.setdefault(x["length"],[]).append(x)
    results=[]
    counts=Counter()
    bytes_counts=Counter()
    for chunk in chunks:
        off,length=chunk["offset"],chunk["length"]
        needle=source[off:off+length]
        found=other.find(needle)
        matches_full=found >= 0
        # A matching target/base CDC chunk exists only if the entire exact
        # source bytes AND reported chunk ID match the other manifest.
        aligned=[x for x in by_length.get(length,[])
                 if other[x["offset"]:x["offset"]+length]==needle]
        if aligned:
            must(all(x["chunkId"]==chunk["chunkId"] for x in aligned),
                 "identical whole bytes had unequal reported ChunkId: hash/suite issue")
        # Independent hash-based identity: any equal CSM ChunkId should match
        # bytes exactly (this will be checked via reverse diagnostics too).
        same_ids=[x for x in other_chunks if x["chunkId"]==chunk["chunkId"]]
        must(bool(aligned)==bool(same_ids), "CSM ChunkId equality vs bytes mismatch")
        must(not aligned or matches_full, "exact chunk alignment paradox")
        contained=sum(off<=w and w+4096<=off+length for w in verified_4k)
        covered=overlap_union(intervals,off,off+length)
        state=("EXACT_WHOLE_AT_CDC_BOUNDARY" if aligned else
               "EXACT_WHOLE_ONLY_UNALIGNED" if matches_full else
               "NO_WHOLE_EXACT_BYTES")
        counts[state]+=1
        bytes_counts[state]+=length
        results.append({
            **chunk, "classification":state,
            "foundWholeBytesAnywhere":matches_full,
            "firstOtherFileOffset":found if matches_full else None,
            "exactOtherCdcChunkCount":len(aligned),
            "fourKiBMatchingBaseAnchorsFullyInside":contained,
            "fourKiBMatchCoverageInThisChunkBytes":covered,
            "fourKiBMatchCoverageLowerBoundFraction":round(covered/length,7),
        })
    must(sum(counts.values())==len(chunks), "lost chunk states")
    return {"countByClassification":dict(sorted(counts.items())),
            "bytesByClassification":dict(sorted(bytes_counts.items())),
            "fourKiBLowerBoundCoverageBytesAcrossChunks":sum(
                x["fourKiBMatchCoverageInThisChunkBytes"] for x in results),
            "chunks":results}


def run(repo,output,run_id):
    must(os.environ.get("GITHUB_ACTIONS")=="true" and
         os.environ.get("GITHUB_SHA")==subprocess.check_output(
             ["git","rev-parse","HEAD"],cwd=repo,text=True).strip(),
         "J2E original binary work requires exact hosted checkout")
    must(not output.exists(), "refuse overwritten experimental results")
    output.mkdir(parents=True)
    frozen=d.verify_contract(json.loads(d.CONTRACT.read_text()))
    contract_data=d.CONTRACT.read_bytes()
    physical=d.fetch_original_payloads(frozen)  # re-download exact pinned nupkg/DLL
    fixture=output/"fixture"
    fixture.mkdir()
    for k in ("base","target"):
        (fixture/(k+".input")).write_bytes(physical[k])
    # Keep a distinct nested builder J2C-ID to use unchanged production
    # C# builder, while preserving external research RunId in J2E output.
    inner_id="CORE011-CSM-001/G1J2C-J2E-"+run_id.split("G1J2E-",1)[1]
    provenance={"schema":"chunkshift.core011.g1j2-provenance.v1",
                "runId":inner_id,"origin":"nuget-released-managed-pe",
                "sourcePath":c.ASSEMBLY_PATH,
                "repository":"https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/",
                "verdict":"NO_VERDICT_EXPLORATORY",
                "base":{"sha256":sha256(physical["base"]),"bytes":len(physical["base"])},
                "target":{"sha256":sha256(physical["target"]),"bytes":len(physical["target"])}}
    (fixture/"provenance.json").write_text(json.dumps(provenance,indent=2)+"\n")
    # One unchanged production benchmark Core build; no temporary reader
    # variants because G1-J2E is a forensic classification, NOT perf.
    j.execute(["dotnet","restore","benchmarks/ChunkShift.Benchmarks.slnx"],repo,1200)
    j.execute(["dotnet","build","benchmarks/ChunkShift.Benchmarks.slnx",
               "-c","Release","--no-restore"],repo,1200)
    listing=output/"list.tsv"
    listing.write_text("\n".join(str(fixture/(k+".input"))+"\t"+
                                 str(output/(k+".scanner.tsv")) for k in ("base","target"))+"\n")
    j.execute(j.command(repo,"chunks","--list",listing),repo,240)
    # Public builder verifies both CSMs and exact Plan, and removes raw files.
    j.execute(j.command(repo,"core011-g1j2-plan-build",
                        "--fixture-dir",fixture,"--run-id",inner_id),repo,240)
    must(all(not (fixture/(k+".input")).exists() for k in ("base","target")),
         "raw DLL sources must be deleted before CSM parsing/artifact")
    descriptor=json.loads((fixture/"pair.json").read_text())
    must(descriptor.get("runId")==inner_id and
         descriptor.get("verdict")=="NO_VERDICT_EXPLORATORY" and
         descriptor.get("expectedPlan",{}).get("reusedChunks")==0 and
         descriptor.get("expectedPlan",{}).get("missingChunks")==81 and
         descriptor.get("expectedPlan",{}).get("reusedBytes")==0 and
         descriptor.get("expectedPlan",{}).get("missingBytes")==len(physical["target"]),
         "original production Plan oracle differs from J2C")
    chunks={}
    manifests={}
    for k in ("base","target"):
        csm=(fixture/(k+".csm")).read_bytes()
        must(sha256(csm)==CSM_SHA[k], "recreated physical CSM SHA not equal accepted J2C")
        physical_chunks=parse_csm_cblk(csm,EXPECTED_SOURCE_BYTES[k],EXPECTED_COUNTS[k])
        scanner=parse_scanner_tsv(output/(k+".scanner.tsv"),
                                  EXPECTED_SOURCE_BYTES[k],EXPECTED_COUNTS[k])
        must(physical_chunks==scanner, "independent CBLK parser/production ChunkScanner disagreement")
        chunks[k]=physical_chunks
        item=descriptor[k]
        must(item["sourceSha256"]==sha256(physical[k]) and
             item["csmSha256"]==CSM_SHA[k] and
             item["chunkCount"]==len(scanner) and item["cblkCount"]==1,
             "J2C physical CSM metadata mismatch")
        manifests[k]={"csmSha256":CSM_SHA[k],"manifestId":item["manifestId"],
                      "fileDigest":item["fileDigest"],
                      "chunkCount":len(scanner),"contentLength":len(physical[k]),
                      "cblkCount":item["cblkCount"]}
    maps={size:verified_windows(physical["base"],physical["target"],size)
          for size in (4096,16384,65536)}
    # For target windows, independently search target anchors in the base so
    # per-target-chunk coverage is valid; do not reuse base-anchored windows
    # as if they represented target anchoring.
    reverse=verified_windows_for_reverse(physical["target"],physical["base"],4096)
    base_diag=chunk_diagnostic(physical["base"],physical["target"],chunks["base"],
                                chunks["target"],maps[4096])
    target_diag=chunk_diagnostic(physical["target"],physical["base"],chunks["target"],
                                  chunks["base"],reverse)
    must(base_diag["countByClassification"].get("EXACT_WHOLE_AT_CDC_BOUNDARY",0)==0 and
         target_diag["countByClassification"].get("EXACT_WHOLE_AT_CDC_BOUNDARY",0)==0,
         "original public Plan zero reuse contradicted by exact aligned chunks")
    result={
        "schema":SCHEMA,"runId":run_id,
        "checkoutSha":os.environ["GITHUB_SHA"],
        "frozenContractSha256":sha256(contract_data),
        "originalJ2CBuilderRunId":inner_id,
        "source":{"base":{"sha256":sha256(physical["base"]),"bytes":len(physical["base"])},
                  "target":{"sha256":sha256(physical["target"]),"bytes":len(physical["target"])}},
        "originalCsm":manifests,
        "originalPlan":{"reusedChunks":0,"missingChunks":81,
                        "reusedBytes":0,"missingBytes":len(physical["target"])},
        "scanInvariant":"Independent physical CSM CBLK parser rows equal production ChunkScanner source TSV, including every offset, length and ChunkId",
        "verifiedShiftedWindows":{
            str(size):{"matchedBaseAnchors":len(maps[size]),
                       "baseCoverageBytes":d.merged_coverage([(p,p+size) for p in maps[size]])}
            for size in (4096,16384,65536)},
        "baseChunks":base_diag,
        "targetChunks":target_diag,
        "interpretation":"EXACT_WHOLE_ONLY_UNALIGNED proves an exact entire CDC chunk can be found inside the other file but not at a canonical matching other-side CDC chunk boundary. NO_WHOLE_EXACT_BYTES means no entire chunk preserved anywhere despite possible shorter exact windows.",
        "limits":"Single real Roslyn version pair, one CBLK each, zero public Plan reuse, no production change or performance decision.",
        "verdict":"NO_VERDICT_EXPLORATORY",
    }
    (output/"boundary.json").write_text(json.dumps(result,indent=2)+"\n")
    print("G1-J2E PASS; base:",base_diag["countByClassification"],
          "target:",target_diag["countByClassification"],
          "product NO_VERDICT_EXPLORATORY")
    # Input objects exist only in this process and no source binaries are
    # written under the retained output after the C# builder deletes them.
    return result


def verified_windows_for_reverse(base,target,size):
    # Reverse scan is symmetric at the same 256-B source grid but no J2D
    # aggregate was preregistered for the reverse direction.
    mapping={}
    for pos,h in d.rolling(base,size):
        if pos%256==0:
            mapping.setdefault(h,[]).append(pos)
    matched={}
    for target_at,h in d.rolling(target,size):
        pool=mapping.get(h)
        if not pool:
            continue
        sample=target[target_at:target_at+size]
        remain=[]
        for pos in pool:
            if base[pos:pos+size]==sample:
                matched[pos]=target_at
            else:
                remain.append(pos)
        if remain:
            mapping[h]=remain
        else:
            del mapping[h]
            if not mapping:
                break
    return matched


def main():
    p=argparse.ArgumentParser()
    p.add_argument("--output-dir",type=Path,required=True)
    p.add_argument("--run-id",required=True)
    a=p.parse_args()
    must(a.run_id.startswith("CORE011-CSM-001/G1J2E-"),"invalid J2E RunId")
    output=a.output_dir
    try:
        run(Path.cwd(),output,a.run_id)
    except Exception as error:
        output.mkdir(parents=True,exist_ok=True)
        # Never retain third-party package/DLL source bytes, even on failure.
        for k in ("base","target"):
            (output/"fixture"/(k+".input")).unlink(missing_ok=True)
        (output/"failure.json").write_text(json.dumps({
            "schema":"chunkshift.core011.g1j2e-attempt-failure.v1",
            "runId":a.run_id,"errorType":type(error).__name__,
            "message":str(error)[:600],"attempt":"FAIL",
            "verdict":"NO_VERDICT_EXPLORATORY",
        },indent=2)+"\n")
        raise


if __name__=="__main__":
    main()
