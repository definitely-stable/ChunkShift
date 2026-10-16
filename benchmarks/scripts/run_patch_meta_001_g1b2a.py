"""PATCH-META-001/G1B2A: original Roslyn, paired disabled/enabled internal probe.

This profiles actual consumed offset-candidate generation only. No metadata reuse.
All create and apply calls execute the exact CSP implementation, with immutable
original source bytes and per-run SHA oracle. Measurement remains non-decision.
"""
import argparse
import json
import math
import os
from pathlib import Path
import platform
import statistics
import sys

import run_patch_meta_001_g1a as g1a
import run_patch_meta_001_g1b1 as g1b1

SCHEMA = "chunkshift.patch-meta-g1b2a-probe.v1"
STATUS = "REAL_ROSLYN_CONSUMED_STAGE_CALIBRATION_NO_PRODUCT_VERDICT"
PAIR = g1b1.PAIRS[0]
MODES = ("off", "on")
METRICS = ("metaBaseManifestSeconds", "metaTargetManifestSeconds",
           "metaCandidateSearchWorkerSeconds", "metaCandidateSearchCalls",
           "metaCandidateStarts", "metaBaseRecords", "metaUniqueBaseIds")


def verify_probe(stage, mode):
    if stage.get("metaProbeEnabled") is not (mode == "on"):
        raise ValueError("probe enabled state differs from requested mode")
    if mode == "off":
        if any(stage.get(key, "absent") is not None for key in METRICS):
            raise ValueError("OFF must not produce measured candidate information")
        return
    for key in METRICS:
        value = stage.get(key)
        if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
            raise ValueError("missing/nonfinite enabled cost metric: " + key)
    if stage["metaCandidateSearchCalls"] <= 0 or stage["metaCandidateStarts"] <= 0:
        raise ValueError("instrumented production chooser did not consume candidates")
    if stage["metaBaseRecords"] <= 0 or stage["metaUniqueBaseIds"] <= 0:
        raise ValueError("base manifest did not populate canonical source index")
    if stage["metaUniqueBaseIds"] > stage["metaBaseRecords"]:
        raise ValueError("invalid base distinct-record conservation")
    if stage["metaCandidateStarts"] < stage["metaCandidateSearchCalls"]:
        raise ValueError("unexpected missing candidates in original Roslyn test")
    if stage["metaBaseManifestSeconds"] <= 0 or stage["metaTargetManifestSeconds"] <= 0:
        raise ValueError("manifest validation timer did not observe real work")


def measure(args):
    if args.rounds != 5:
        raise ValueError("G1B2A preregistered 5 counterbalanced rounds")
    dll = Path(args.dll).resolve(strict=True)
    corpus, work, out = Path(args.corpus), Path(args.work), Path(args.output)
    if any(p.exists() for p in (corpus, work, out)) or len({
            p.resolve() for p in (corpus, work, out)}) != 3:
        raise FileExistsError("source/work/result paths must be fresh and disjoint")
    provenance = g1b1.prepare_corpus(corpus)
    out.mkdir(parents=True)
    original = g1b1.EXPECTED[PAIR[1]]["dllSha256"]
    samples = []
    fixed_patch_sha = None
    fixed_manifest_ids = None
    for round_number in range(args.rounds):
        order = MODES if round_number % 2 == 0 else MODES[::-1]
        for mode in order:
            label = f"r{round_number}-{mode}"
            patch = out / f"{label}.csp"
            sample = out / f"{label}.stage.json"
            applied = out / f"{label}.applied"
            peak = g1b1.checked_child(g1b1.child(
                dll, "create", corpus, work, PAIR, patch,
                ["--lane", "csp", "--execution", "h2-w2",
                 "--meta-attribution", str(sample), "--meta-probe", mode]))
            raw = g1b1.verify_sample(
                json.loads(sample.read_text(encoding="utf-8")),
                PAIR, provenance, patch)
            verify_probe(raw, mode)
            g1b1.checked_child(g1b1.child(
                dll, "apply", corpus, work, PAIR, patch,
                ["--output", str(applied), "--check", "seq"]))
            if g1a.file_sha(applied) != original:
                raise ValueError("reconstructed Roslyn bytes mismatch pinned SHA")
            if round_number > 0 and (not raw["baseManifestCacheHit"] or
                                      not raw["targetManifestCacheHit"]):
                raise ValueError("subsequent calibration needs cached verified CSM")
            ids = (raw["baseManifestId"], raw["targetManifestId"])
            if fixed_patch_sha is None:
                fixed_patch_sha = raw["patchSha256"]
                fixed_manifest_ids = ids
            elif fixed_patch_sha != raw["patchSha256"] or ids != fixed_manifest_ids:
                raise ValueError("on/off probe must not alter physical CSP/CSM identity")
            samples.append({
                "round": round_number, "mode": mode, "rawStage": sample.name,
                "rawStageSha256": g1a.file_sha(sample),
                "patchSha256": raw["patchSha256"],
                "applyTargetSha256": original,
                "builderWallSeconds": raw["builderWallSeconds"],
                "builderCpuSeconds": raw["builderCpuSeconds"],
                "totalWallSeconds": raw["totalWallSeconds"],
                "createPeakWorkingSetBytes": peak,
                "candidateSearchCalls": raw.get("metaCandidateSearchCalls"),
                "candidateStarts": raw.get("metaCandidateStarts"),
                "candidateSearchWorkerSeconds": raw.get("metaCandidateSearchWorkerSeconds"),
            })
            applied.unlink()
            patch.unlink()

    # First round creates cold manifests and is excluded from paired comparison.
    paired = [s for s in samples if s["round"] > 0]
    if len(paired) != 8:
        raise ValueError("missing complete warm on/off pair")
    def med(mode, key):
        values = [s[key] for s in paired if s["mode"] == mode]
        if len(values) != 4 or any(
                type(v) not in (int, float) or not math.isfinite(v) or v < 0
                for v in values):
            raise ValueError("incomplete repeated paired measurements")
        return statistics.median(values)
    measured_search = med("on", "candidateSearchWorkerSeconds")
    result = {
        "schema": SCHEMA, "status": STATUS, "experimentId": "PATCH-META-001",
        "runId": os.environ.get("GITHUB_RUN_ID"),
        "headSha": os.environ.get("HEAD_SHA", "LOCAL_UNVERIFIED"),
        "sourceFamily": g1b1.FAMILY, "sourcePair": list(PAIR),
        "sourceLocks": provenance["source"], "pairsSha256": provenance["pairsSha256"],
        "execution": "h2-w2", "lane": "csp",
        "rounds": 5, "creates": 10, "exactApplies": 10,
        "platform": platform.platform(), "python": sys.version,
        "medianWarmBuilderOffSeconds": med("off", "builderWallSeconds"),
        "medianWarmBuilderOnSeconds": med("on", "builderWallSeconds"),
        "medianWarmBuilderCpuOffSeconds": med("off", "builderCpuSeconds"),
        "medianWarmBuilderCpuOnSeconds": med("on", "builderCpuSeconds"),
        "medianCandidateSearchWorkerSeconds": measured_search,
        "medianCandidateCalls": med("on", "candidateSearchCalls"),
        "medianCandidateStarts": med("on", "candidateStarts"),
        "sameBuildCspHash": fixed_patch_sha,
        "interpretation": (
            "Internal search worker stopwatch seconds may overlap; no causal speedup "
            "or cacheable budget inferred. Builder off/on delta also includes "
            "JIT, worker scheduling and new probe overhead. Source checks, "
            "manifest validation and codec remain mandatory. "
            "Real original Roslyn pair, NOT general/holdout evidence."),
        "samples": samples,
    }
    (out / "summary.json").write_text(
        json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in (
        "status", "creates", "medianWarmBuilderOffSeconds",
        "medianWarmBuilderOnSeconds", "medianCandidateSearchWorkerSeconds",
        "medianCandidateCalls", "medianCandidateStarts")}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True)
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--rounds", type=int, default=5)
    measure(parser.parse_args())


if __name__ == "__main__":
    main()
