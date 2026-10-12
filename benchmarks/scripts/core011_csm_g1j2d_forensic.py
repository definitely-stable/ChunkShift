#!/usr/bin/env python3
"""G1-J2D frozen-original-Roslyn forensic: exact windows vs unchanged PE sections.

Only original, independently hash-pinned byte payloads. NO CSM decision runs.
The rolling hash is only an accelerator: all reported matches verify bytes.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

import core011_csm_g1j2c_roslyn as source

SCHEMA = "chunkshift.core011.g1j2d-forensic.v1"
CONTRACT = Path("benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json")
MASK = (1 << 64) - 1
PRIME = 257


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def require(ok, message):
    if not ok:
        raise ValueError(message)


def verify_contract(contract):
    require(contract.get("schema") == "chunkshift.core011.g1j2d-frozen-corpus.v1", "bad contract schema")
    require(contract.get("stage") == "first-bytewise-forensic-qualification" and
            contract.get("verdict") == "NO_VERDICT_EXPLORATORY" and
            contract.get("decisionRunsAllowed") is False and
            contract.get("budgetsLocked") is False and
            contract.get("productionChangesAllowed") is False, "decision guard changed")
    require(contract.get("packageId") == "Microsoft.CodeAnalysis.CSharp" and
            contract.get("flatContainer") == source.FLAT and
            contract.get("payloadPath") == source.ASSEMBLY_PATH and
            contract.get("sourceRun") == "38286138940", "upstream source drift")
    require(contract.get("exactWindowSizes") == [4096, 16384, 65536] and
            contract.get("baseAnchorStrideBytes") == 256 and
            contract.get("targetSearchStrideBytes") == 1 and
            contract.get("rollingHash") == "polynomial257-mod2^64-with-byte-verification" and
            contract.get("alignedBlockSizeBytes") == 4096, "window protocol drift")
    require(contract.get("expectedOriginalPlanReusedChunks") == 0 and
            contract.get("expectedOriginalPlanMissingChunks") == 81 and
            contract.get("expectedOriginalCblkCounts") == [1, 1], "original J2C Plan oracle drift")
    for role, ver in source.VERSIONS.items():
        item = contract["versions"][role]
        require(item["version"] == ver and item["packageBytes"] > 1024 and
                item["dllBytes"] > 65536 and
                source.validate_sha512_text(item["packageSha512Base64"]), "source version/digest drift")
        for key in ("packageSha256", "dllSha256"):
            h = item[key]
            require(type(h) is str and len(h) == 64 and
                    all(k in "0123456789abcdef" for k in h), "bad expected SHA256")
    return contract


def fetch_original_payloads(contract):
    outputs = {}
    for role in ("base", "target"):
        item = contract["versions"][role]
        url, _ = source.flat_urls(item["version"])
        # NuGet package is kept in memory only for first-party bytewise forensic.
        payload = source.get_bytes(url, source.MAX_PACKAGE)
        require(len(payload) == item["packageBytes"] and
                sha256(payload) == item["packageSha256"] and
                source.sha512_b64(payload) == item["packageSha512Base64"],
                "released NuGet package bytes are NOT the previously recorded source")
        dll = source.extract_release(payload)
        require(len(dll) == item["dllBytes"] and sha256(dll) == item["dllSha256"],
                "original DLL digest differs from J2C frozen expected")
        outputs[role] = dll
    return outputs


def pe_layout(data):
    require(len(data) > 4096 and data[:2] == b"MZ", "not PE MZ")
    pe_offset = struct.unpack_from("<I", data, 0x3c)[0]
    require(64 <= pe_offset < min(len(data) - 24, 65536) and
            data[pe_offset:pe_offset+4] == b"PE\x00\x00", "missing PE signature")
    coff = pe_offset + 4
    num_sections, opt_size = struct.unpack_from("<H", data, coff+2)[0], struct.unpack_from("<H",data,coff+16)[0]
    require(0 < num_sections <= 96 and opt_size >= 96, "unbounded section metadata")
    opt = coff + 20
    magic = struct.unpack_from("<H",data,opt)[0]
    require(magic in (0x10b,0x20b), "unexpected PE optional header")
    dirs = opt + (96 if magic == 0x10b else 112)
    n_rva = struct.unpack_from("<I",data,opt + (92 if magic == 0x10b else 108))[0]
    require(n_rva >= 15 and dirs + 15 * 8 <= opt + opt_size, "missing CLR directory")
    clr_rva, clr_size = struct.unpack_from("<II",data,dirs+14*8)
    require(clr_rva > 0 and clr_size >= 0x48, "no CLR metadata header")
    start = opt + opt_size
    require(start + num_sections*40 <= len(data), "truncated section table")
    sections = []
    for i in range(num_sections):
        off = start+i*40
        name = data[off:off+8].split(b"\x00",1)[0].decode("ascii","replace")
        virtual_size, rva, size, raw = struct.unpack_from("<IIII",data,off+8)
        require(size <= len(data) and raw <= len(data) and raw+size <= len(data),
                "PE section outside binary")
        require(len(name)>0, "unnamed section")
        sections.append({"name":name,"rawOffset":raw,"rawBytes":size,
                         "rva":rva,"virtualBytes":virtual_size})
    require(any(s["rva"] <= clr_rva < s["rva"]+max(s["virtualBytes"],s["rawBytes"]) for s in sections),
            "CLR directory not mapped by PE section")
    return {"peHeaderFileOffset":pe_offset,
            "clrDirectoryRva":clr_rva,"clrDirectoryBytes":clr_size,
            "sections":sections}


def region_name(offset,layout):
    for section in layout["sections"]:
        if section["rawOffset"] <= offset < section["rawOffset"] + section["rawBytes"]:
            return section["name"]
    return "<headers/overlay>"


def rolling(data, size):
    """Yields (offset, polynomial hash) at ALL byte offsets; O(n) time/constant memory."""
    if len(data) < size:
        return
    power = pow(PRIME,size-1,1<<64)
    h=0
    for x in data[:size]:
        h=((h*PRIME)+x) & MASK
    yield (0,h)
    for i in range(size,len(data)):
        h=(((h-data[i-size]*power)*PRIME)+data[i]) & MASK
        yield (i-size+1,h)


def merged_coverage(intervals):
    """Union of provably identical bytes, not an estimate or summed overlapping windows."""
    if not intervals:
        return 0
    intervals=sorted(intervals)
    coverage=0
    left,right=intervals[0]
    for start,end in intervals[1:]:
        if start > right:
            coverage += right-left
            left,right=start,end
        else:
            right=max(right,end)
    return coverage+right-left


def window_probe(base,target,window,stride,base_pe):
    require(window in (4096,16384,65536) and stride == 256, "forensic protocol drift")
    # A base grid at 256-byte strides versus *every single target offset*
    # detects exact runs at least window+255 bytes, regardless of relocation.
    hashes={}
    anchors=0
    for pos,h in rolling(base,window):
        if pos % stride != 0:
            continue
        hashes.setdefault(h,[]).append(pos)
        anchors += 1
    matched={}
    for target_at,h in rolling(target,window):
        candidates=hashes.get(h)
        if not candidates:
            continue
        remaining=[]
        target_bytes=target[target_at:target_at+window]
        for base_at in candidates:
            if base[base_at:base_at+window] == target_bytes:
                matched[base_at]=target_at
            else:
                remaining.append(base_at)
        if remaining:
            hashes[h]=remaining
        else:
            del hashes[h]
            if not hashes:
                break
    regions={}
    for off in matched:
        region=region_name(off,base_pe)
        regions[region]=regions.get(region,0)+1
    examples=[{"baseOffset":off,"firstTargetOffset":matched[off],
               "baseSection":region_name(off,base_pe)}
              for off in sorted(matched)[:12]]
    return {
        "windowBytes":window,"baseStrideBytes":stride,"targetStrideBytes":1,
        "baseAnchors":anchors,
        "baseAnchorsWithExactTargetMatch":len(matched),
        "baseWindowCoverageLowerBoundBytes":merged_coverage(
            [(i,i+window) for i in matched]),
        "firstTargetWindowCoverageLowerBoundBytes":merged_coverage(
            [(j,j+window) for j in matched.values()]),
        "matchingBaseAnchorsBySection":dict(sorted(regions.items())),
        "examplesFirstTargetOccurrenceNotCanonicalAlignment":examples,
        "guarantee":"Any exact common run at least window+stride-1 bytes contains a base anchor; absence of matched anchors excludes such runs.",
        "limit":"Coverage is lower bound from anchored exact windows; first target hit is not evidence of canonical byte relocation."
    }


def fixed_blocks(base,target,size=4096):
    # Deliberately separate test for common fixed-size blocks at 0-mod-size
    # boundaries on BOTH sources; ignore offsets if only length diff.
    blocks={base[i:i+size] for i in range(0,len(base)-size+1,size)}
    candidates=range(0,len(target)-size+1,size)
    return {"blockSizeBytes":size,"baseAlignedUniqueBlocks":len(blocks),
            "targetAlignedBlocks":len(candidates),
            "targetAlignedBlocksFoundInBase":sum(target[i:i+size] in blocks for i in candidates),
            "interpretation":"Fixed-block 0-aligned comparison only; strictly different from FastCDC exact ChunkId and not a patch benchmark."}


def same_offset_detail(base,target,base_pe,target_pe):
    n=min(len(base),len(target))
    same=sum(a==b for a,b in zip(base,target))
    by_name={}
    bs={s["name"]:s for s in base_pe["sections"]}
    ts={s["name"]:s for s in target_pe["sections"]}
    for name in sorted(bs.keys()|ts.keys()):
        b=bs.get(name)
        t=ts.get(name)
        if b is None or t is None:
            by_name[name]={"presentInBoth":False}
            continue
        overlap=min(b["rawBytes"],t["rawBytes"])
        l=base[b["rawOffset"]:b["rawOffset"]+overlap]
        rr=target[t["rawOffset"]:t["rawOffset"]+overlap]
        equal=sum(a==bb for a,bb in zip(l,rr))
        by_name[name]={"presentInBoth":True,"commonRelativeSectionBytes":overlap,
                       "equalBytesSameRelativeSectionOffset":equal,
                       "baseRawOffset":b["rawOffset"],"targetRawOffset":t["rawOffset"],
                       "baseRawBytes":b["rawBytes"],"targetRawBytes":t["rawBytes"]}
    return {"commonFileOffsetBytes":n,
            "equalBytesSameFileOffset":same,
            "equalSameOffsetFraction":same/n if n else 0,
            "sectionRelativeFileOffsetByteMatches":by_name,
            "caveat":"Same-offset byte coincidence is NOT guaranteed contiguous reuse or CDC chunk reuse."}


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--contract",type=Path,default=CONTRACT)
    parser.add_argument("--output-dir",type=Path,required=True)
    parser.add_argument("--run-id",required=True)
    args=parser.parse_args()
    require(args.run_id.startswith("CORE011-CSM-001/G1J2D-"),"wrong experiment RunId")
    repo=Path.cwd()
    sha=subprocess.check_output(["git","rev-parse","HEAD"],cwd=repo,text=True).strip()
    require(os.environ.get("GITHUB_ACTIONS")=="true" and
            os.environ.get("GITHUB_SHA")==sha and
            len(sha)==40, "G1-J2D runs only on exact GitHub-hosted checkout")
    require(not args.output_dir.exists(),"refuse previous forensic result")
    args.output_dir.mkdir(parents=True)
    try:
        contract_bytes=args.contract.read_bytes()
        contract=verify_contract(json.loads(contract_bytes))
        dll=fetch_original_payloads(contract)
        base,target=dll["base"],dll["target"]
        base_pe=pe_layout(base)
        target_pe=pe_layout(target)
        result={
            "schema":SCHEMA,"experimentId":"CORE011-CSM-001",
            "runId":args.run_id,"checkoutSha":sha,
            "contractSha256":sha256(contract_bytes),
            "verdict":"NO_VERDICT_EXPLORATORY",
            "originalJ2cPlan":{"reusedChunks":0,"missingChunks":81,
                               "baseCblk":1,"targetCblk":1},
            "source":{k:{"version":contract["versions"][k]["version"],
                         "actualDllSha256":sha256(dll[k]),
                         "actualDllBytes":len(dll[k]),
                         "nugetPackageSha256":contract["versions"][k]["packageSha256"]}
                      for k in ("base","target")},
            "peLayouts":{"base":base_pe,"target":target_pe},
            "sameOffset":same_offset_detail(base,target,base_pe,target_pe),
            "alignedFixedBlocks":fixed_blocks(base,target),
            "unalignedExactWindows":[
                window_probe(base,target,size,contract["baseAnchorStrideBytes"],base_pe)
                for size in contract["exactWindowSizes"]
            ],
            "scope":"Exact unmodified bytes only; no edits, delta creation, or CSM/CDC reconfiguration.",
            "limit":"Base-window grid is sparse (256 B), target byte sliding is exhaustive. PE section comparison is descriptive; do not infer IL/RVA cause or production improvement.",
        }
        (args.output_dir/"forensic.json").write_text(
            json.dumps(result,indent=2)+"\n")
        print("G1-J2D exact original bytes: ",
              {str(x["windowBytes"]):x["baseAnchorsWithExactTargetMatch"]
               for x in result["unalignedExactWindows"]},
              "NO_VERDICT_EXPLORATORY")
    except Exception as error:
        (args.output_dir/"failure.json").write_text(json.dumps({
            "schema":"chunkshift.core011.g1j2d-failure.v1",
            "runId":args.run_id,"checkoutSha":sha,
            "errorType":type(error).__name__,"message":str(error)[:600],
            "verdict":"NO_VERDICT_EXPLORATORY","attempt":"FAIL",
        },indent=2)+"\n")
        raise


if __name__ == "__main__":
    main()
