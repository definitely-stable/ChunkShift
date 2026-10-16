"""PATCH-META-001 G1B1: exact-source Roslyn preflight, not a cache efficacy study.

Two original Microsoft.CodeAnalysis.CSharp releases. Digests are pinned from
CORE011-CSM-001/G1J2C independently archived original-package observations.
No training, no tuning, no fallback binaries, no release adoption claim.
"""
import argparse
import hashlib
import io
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys
import urllib.parse
import urllib.request
import zipfile

import run_patch_meta_001_g1a as g1a

EXPERIMENT = "PATCH-META-001"
SCHEMA = "chunkshift.patch-meta-g1b1-roslyn.v1"
STATUS = "REAL_ROSLYN_CALIBRATION_NO_PRODUCT_VERDICT"
FAMILY = "roslyn-pe"
VERSIONS = ("4.13.0", "4.14.0")
PAIRS = (VERSIONS, VERSIONS[::-1])
PACKAGE_ID = "microsoft.codeanalysis.csharp"
PACKAGE_ENTRY = "lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll"
BASE_URL = "https://api.nuget.org/v3-flatcontainer"
MAX_PACKAGE = 32 * 1024 * 1024
MAX_DLL = 20 * 1024 * 1024
# Literal expected digests, NOT hashes calculated from the current download.
# Source: docs/research/results/CORE011-CSM-001-G1J2C-20261012-001.md.
EXPECTED = {
    "4.13.0": {
        "packageSha256": "8f33bbff68fbacfaaee17b66e25861d888da8b0f9a524891fb2627f9af2683f3",
        "dllSha256": "7da8fb18fa6e19a166b35d87b731ecbb040c3ad2cc7894b19953384aee0f5fe4",
        "dllBytes": 6604072,
    },
    "4.14.0": {
        "packageSha256": "e4cce3dd791860b9300d6875eebd4d11749b5f0c166103e23a6912325828d496",
        "dllSha256": "9acdb971334046d22a07fad02648e2725ef5a1b9d0fbe4fe82f1ada57fa70c9e",
        "dllBytes": 6697264,
    },
}


def package_url(version):
    if version not in VERSIONS:
        raise ValueError("unapproved package version")
    return (f"{BASE_URL}/{PACKAGE_ID}/{version}/"
            f"{PACKAGE_ID}.{version}.nupkg")


def read_exact_release(version, loader=None):
    url = package_url(version)
    if loader is None:
        request = urllib.request.Request(
            url, headers={"User-Agent": "ChunkShift-PATCH-META-001/1"})
        with urllib.request.urlopen(request, timeout=120) as response:
            final = urllib.parse.urlsplit(response.geturl())
            if (response.status != 200 or final.scheme != "https" or
                final.hostname not in ("api.nuget.org", "globalcdn.nuget.org")):
                raise ValueError("unapproved release origin/redirect")
            raw = response.read(MAX_PACKAGE + 1)
    else:
        raw = loader(url)
    if not isinstance(raw, bytes) or not 0 < len(raw) <= MAX_PACKAGE:
        raise ValueError("missing/oversized original NuGet package")
    if g1a.sha(raw) != EXPECTED[version]["packageSha256"]:
        raise ValueError("independently pinned package SHA-256 mismatch")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        if archive.testzip() is not None:
            raise ValueError("corrupt original NuGet ZIP CRC")
        candidates = [info for info in archive.infolist()
                      if info.filename == PACKAGE_ENTRY]
        if len(candidates) != 1 or candidates[0].is_dir() or not (
                65536 <= candidates[0].file_size <= MAX_DLL):
            raise ValueError("invalid Roslyn PE DLL entry")
        binary = archive.read(candidates[0])
    if (len(binary) != EXPECTED[version]["dllBytes"] or
        g1a.sha(binary) != EXPECTED[version]["dllSha256"] or
        binary[:2] != b"MZ"):
        raise ValueError("original Roslyn DLL pin/PE signature mismatch")
    pe = int.from_bytes(binary[0x3c:0x40], "little")
    if not 0 < pe < len(binary) - 4 or binary[pe:pe + 4] != b"PE\x00\x00":
        raise ValueError("invalid PE header")
    # Checksums are exact source pins; no source byte transformations.
    return binary, {
        "version": version,
        "sourceUrl": url,
        "packageSha256": EXPECTED[version]["packageSha256"],
        "sourceSha256": EXPECTED[version]["dllSha256"],
        "sourceBytes": EXPECTED[version]["dllBytes"],
        "entry": PACKAGE_ENTRY,
        "provenance": "official NuGet release; frozen against prior archived hash",
        "isSynthetic": False,
    }


def prepare_corpus(corpus: Path, loader=None):
    if corpus.exists():
        raise FileExistsError("refuse existing corpus (uncertain provenance)")
    content = {}
    provenance = {}
    # Verify both originals BEFORE creating any usable corpus document.
    for version in VERSIONS:
        content[version], provenance[version] = read_exact_release(version, loader)
    corpus.mkdir(parents=True)
    for version, binary in content.items():
        dest = corpus / "tree" / FAMILY / version / "artifact.dll"
        dest.parent.mkdir(parents=True)
        dest.write_bytes(binary)
    pairs = {"schema": "chunkshift.patch-pairs.v1", "pairs": []}
    for base, target in PAIRS:
        pairs["pairs"].append({
            "family": FAMILY, "base": base, "target": target,
            "changed": [{
                "path": "artifact.dll",
                "baseSize": len(content[base]),
                "baseSha256": g1a.sha(content[base]),
                "targetSize": len(content[target]),
                "targetSha256": g1a.sha(content[target]),
            }],
        })
    (corpus / "pairs.json").write_text(
        json.dumps(pairs, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    return {
        "source": provenance,
        "pairsSha256": g1a.file_sha(corpus / "pairs.json"),
    }


def child(dll, action, corpus, work, pair, patch, extra):
    return ["dotnet", str(dll), "patch-lab", "one", action,
            "--corpus", str(corpus), "--family", FAMILY,
            "--base", pair[0], "--target", pair[1],
            "--path", "artifact.dll", "--work", str(work),
            "--patch", str(patch), *extra]


def checked_child(command):
    result = subprocess.run(command, capture_output=True, text=True, check=True)
    peaks = [x for x in result.stdout.splitlines() if x.startswith("peak=")]
    if len(peaks) != 1 or not peaks[0][5:].isdigit():
        raise ValueError("child must report exactly one process lifetime peak")
    return int(peaks[0][5:])


def verify_sample(raw, pair, provenance, patch):
    if not isinstance(raw, dict) or raw.get("schema") != g1a.SCHEMA or raw.get(
            "status") != "STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT":
        raise ValueError("wrong stage schema/verdict")
    for name, expected in {
        "lane": "csp", "execution": "h2-w2", "family": FAMILY,
        "baseVersion": pair[0], "targetVersion": pair[1],
        "path": "artifact.dll",
        "baseSourceSha256": provenance["source"][pair[0]]["sourceSha256"],
        "targetSourceSha256": provenance["source"][pair[1]]["sourceSha256"],
        "baseSourceBytes": EXPECTED[pair[0]]["dllBytes"],
        "targetSourceBytes": EXPECTED[pair[1]]["dllBytes"],
        "outputPatchBytes": patch.stat().st_size,
        "patchPhysicalBytes": patch.stat().st_size,
        "patchSha256": g1a.file_sha(patch),
    }.items():
        if raw.get(name) != expected:
            raise ValueError("source/physical CSP/stage mismatch: " + name)
    for name in ("baseManifestId", "targetManifestId"):
        value = raw.get(name)
        if not isinstance(value, str) or len(value) != 64:
            raise ValueError("missing canonical CSM identity: " + name)
    for name in ("baseManifestCacheHit", "targetManifestCacheHit"):
        if type(raw.get(name)) is not bool:
            raise ValueError("missing CSM-hit boolean")
    for name in ("builderWallSeconds", "builderCpuSeconds",
                 "totalWallSeconds", "baseDigestWallSeconds",
                 "targetDigestWallSeconds", "postBuilderPatchShaWallSeconds",
                 "baseManifestBuildWallSeconds", "targetManifestBuildWallSeconds"):
        value = raw.get(name)
        if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
            raise ValueError("missing/non-finite stage time: " + name)
    if raw["builderWallSeconds"] <= 0 or raw["totalWallSeconds"] <= 0:
        raise ValueError("empty measured builder duration")
    accounted = sum(raw[name] for name in (
        "builderWallSeconds", "baseDigestWallSeconds",
        "targetDigestWallSeconds", "baseManifestBuildWallSeconds",
        "targetManifestBuildWallSeconds", "postBuilderPatchShaWallSeconds"))
    if accounted > raw["totalWallSeconds"] * 1.05 + 0.005:
        raise ValueError("overlapping stage clocks")
    for name in ("baseContentReadBytes", "targetContentReadBytes",
                 "baseManifestReadBytes", "targetManifestReadBytes",
                 "builderAllocatedBytes", "baseContentReadCalls",
                 "targetContentReadCalls"):
        if type(raw.get(name)) is not int or raw[name] < 0:
            raise ValueError("invalid stream/alloc count: " + name)
    if raw["targetContentReadBytes"] < EXPECTED[pair[1]]["dllBytes"]:
        raise ValueError("full exact target read was not observed")
    return raw


def run(args):
    if args.rounds != 5:
        raise ValueError("G1B1 real calibration locks exactly five rounds")
    dll = Path(args.dll).resolve(strict=True)
    corpus, work, output = Path(args.corpus), Path(args.work), Path(args.output)
    if corpus.exists() or output.exists() or work.exists():
        raise FileExistsError("all paths must be fresh to avoid stale measurements")
    if len({p.resolve() for p in (corpus, work, output)}) != 3:
        raise ValueError("corpus/work/output paths must be distinct")
    provenance = prepare_corpus(corpus)
    output.mkdir(parents=True)
    raw_rows, patch_digests, csm_ids = [], {}, {}
    for round_number in range(args.rounds):
        order = PAIRS if round_number % 2 == 0 else PAIRS[::-1]
        for base, target in order:
            pair = (base, target)
            label = f"r{round_number}-base{base}-target{target}"
            patch = output / f"{label}.csp"
            stage = output / f"{label}.stage.json"
            applied = output / f"{label}.applied"
            create_peak = checked_child(child(
                dll, "create", corpus, work, pair, patch,
                ["--lane", "csp", "--execution", "h2-w2",
                 "--meta-attribution", str(stage)]))
            sample = verify_sample(json.loads(stage.read_text()), pair, provenance, patch)
            checked_child(child(dll, "apply", corpus, work, pair, patch,
                                ["--output", str(applied), "--check", "seq"]))
            if (g1a.file_sha(applied) !=
                    provenance["source"][target]["sourceSha256"]):
                raise ValueError("independent exact apply target SHA-256 mismatch")
            if round_number == 0 and pair == PAIRS[0]:
                if sample["baseManifestCacheHit"] or sample["targetManifestCacheHit"]:
                    raise ValueError("source CSM was not initially cold")
            if round_number > 0 and (not sample["baseManifestCacheHit"] or
                                      not sample["targetManifestCacheHit"]):
                raise ValueError("warm CSM validation/regeneration mismatch")
            expected_ids = (sample["baseManifestId"], sample["targetManifestId"])
            if pair in csm_ids and csm_ids[pair] != expected_ids:
                raise ValueError("same-build canonical identities changed")
            if pair in patch_digests and patch_digests[pair] != sample["patchSha256"]:
                raise ValueError("same-build CSP physical transparency violated")
            csm_ids[pair] = expected_ids
            patch_digests[pair] = sample["patchSha256"]
            raw_rows.append({
                "round": round_number, "pair": f"{base}->{target}",
                "stage": stage.name,
                "stageSha256": g1a.file_sha(stage),
                "targetSha256": provenance["source"][target]["sourceSha256"],
                "patchSha256": sample["patchSha256"],
                "createPeakWorkingSetBytes": create_peak,
                "builderWallSeconds": sample["builderWallSeconds"],
                "builderCpuSeconds": sample["builderCpuSeconds"],
                "totalWallSeconds": sample["totalWallSeconds"],
                "baseManifestHit": sample["baseManifestCacheHit"],
                "targetManifestHit": sample["targetManifestCacheHit"],
            })
            patch.unlink()
            applied.unlink()
    # First pass includes manifest building and is intentionally excluded.
    warm = [json.loads((output / r["stage"]).read_text())
            for r in raw_rows if r["round"] > 0]
    digest_times = [x["baseDigestWallSeconds"] + x["targetDigestWallSeconds"]
                    for x in warm]
    summary = {
        "schema": SCHEMA, "experimentId": EXPERIMENT, "status": STATUS,
        "runId": os.environ.get("GITHUB_RUN_ID"),
        "attempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "gitCommit": os.environ.get("HEAD_SHA", "LOCAL_UNVERIFIED"),
        "platform": platform.platform(), "python": sys.version,
        "processorCount": os.cpu_count(),
        "lane": "csp", "execution": "h2-w2",
        "pairsSha256": provenance["pairsSha256"],
        "provenance": provenance["source"],
        "rounds": args.rounds, "measuredCreates": len(raw_rows),
        "allApplySha256Matched": True,
        "allSameBuildCspSha256Matched": True,
        "warmSampleCount": len(warm),
        "medianWarmBuilderWallSeconds": statistics.median(
            x["builderWallSeconds"] for x in warm),
        "medianWarmBuilderCpuSeconds": statistics.median(
            x["builderCpuSeconds"] for x in warm),
        "medianWarmSourceDigestWallSeconds": statistics.median(digest_times),
        "medianWarmEndToEndObservedWallSeconds": statistics.median(
            x["totalWallSeconds"] for x in warm),
        "limitation": (
            "one bidirectional original Roslyn release pair, calibration only; "
            "no feature cache on/off, no builder-internal feature timer, "
            "no provenance claim beyond pinned package+DLL bytes; "
            "warm OS page cache, small N, per-platform paired evidence only"),
        "samples": raw_rows,
    }
    # Successful summary is the LAST effect, after every original apply passes.
    (output / "summary.json").write_text(
        json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({k: summary[k] for k in (
        "status", "runId", "gitCommit", "measuredCreates",
        "medianWarmBuilderWallSeconds", "medianWarmSourceDigestWallSeconds",
        "medianWarmEndToEndObservedWallSeconds")}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True)
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--rounds", type=int, default=5)
    run(parser.parse_args())


if __name__ == "__main__":
    main()
