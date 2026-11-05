"""PATCH-META-001/G1B2B: original Roslyn consumed zstd and dictionary-read attribution.

Stack on G1B2A original off/on exact SHA oracles, not a feature-cache experiment.
Python refuses missing/invalid stage data and never treats encoder or source
validation cost as a reusable PatchFeatures opportunity.
"""
import argparse
import json
import math
from pathlib import Path
import statistics

import run_patch_meta_001_g1b2a as base

SCHEMA = "chunkshift.patch-meta-g1b2b-codec-consumed.v1"
STATUS = "ORIGINAL_ROSLYN_CODECTRIAL_COST_CALIBRATION__NO_META_CACHE__NO_PRODUCT_VERDICT"
CLOCKS = ("metaPlainZstdWorkerSeconds", "metaDictionaryZstdWorkerSeconds",
          "metaDictionaryReadWorkerSeconds")
COUNTS = ("metaPlainZstdTrials", "metaDictionaryZstdTrials",
          "metaDictionaryReadCalls", "metaPlainInputBytes",
          "metaDictionaryTargetInputBytes", "metaDictionaryInputBytes",
          "metaDictionaryReadBytes")
METRICS = CLOCKS + COUNTS


def validate_codec_stage(stage, mode):
    if stage.get("metaProbeEnabled") is not (mode == "on"):
        raise ValueError("G1B2B probe mode mismatch")
    if mode == "off":
        if any(key not in stage or stage[key] is not None for key in METRICS):
            raise ValueError("G1B2B off probe must emit all null metrics")
        return
    for field in CLOCKS:
        value = stage.get(field)
        if type(value) not in (int, float) or not math.isfinite(value) or value <= 0:
            raise ValueError("G1B2B nonpositive/invalid clock: " + field)
    for field in COUNTS:
        value = stage.get(field)
        if type(value) is not int or value <= 0:
            raise ValueError("G1B2B nonpositive/invalid count: " + field)
    if stage["metaPlainZstdTrials"] != stage["metaCandidateSearchCalls"]:
        raise ValueError("one plain codec trial per actual exhaustive candidate search")
    if stage["metaDictionaryZstdTrials"] > stage["metaDictionaryReadCalls"]:
        raise ValueError("more dictionary encodes than dictionary loads")
    if stage["metaDictionaryReadCalls"] > stage["metaCandidateStarts"]:
        raise ValueError("more dictionary loads than candidate starts")
    if stage["metaDictionaryInputBytes"] > stage["metaDictionaryReadBytes"]:
        raise ValueError("dictionary encoded bytes exceed loaded bytes")
    if stage["metaDictionaryTargetInputBytes"] < stage["metaPlainInputBytes"]:
        raise ValueError("dictionary trial target bytes below un-dictionaried target bytes")


def postprocess(out):
    base_summary = json.loads((out / "summary.json").read_text(encoding="utf-8"))
    if (base_summary.get("schema") != base.SCHEMA
            or base_summary.get("status") != base.STATUS
            or base_summary.get("creates") != 10
            or base_summary.get("exactApplies") != 10
            or len(base_summary.get("samples", [])) != 10):
        raise ValueError("original G1B2A complete exact apply chain missing")
    observations = []
    for sample in base_summary["samples"]:
        raw = json.loads((out / sample["rawStage"]).read_text(encoding="utf-8"))
        mode = sample["mode"]
        base.verify_probe(raw, mode)
        validate_codec_stage(raw, mode)
        if raw["patchSha256"] != sample["patchSha256"]:
            raise ValueError("stage sample differs from checked original CSP")
        observations.append({
            "round": sample["round"], "mode": mode,
            "rawStage": sample["rawStage"],
            "rawStageSha256": sample["rawStageSha256"],
            **{k: raw[k] for k in METRICS},
        })
    if [(s["round"], s["mode"]) for s in observations] != [
        (rnd, mode) for rnd in range(5)
        for mode in (base.MODES if rnd % 2 == 0 else base.MODES[::-1])]:
        raise ValueError("pre-registered paired round order mismatch")
    warm = [s for s in observations if s["round"] > 0 and s["mode"] == "on"]
    if len(warm) != 4:
        raise ValueError("four warm on observations required")
    medians = {name: statistics.median(s[name] for s in warm) for name in METRICS}
    result = {
        "schema": SCHEMA, "experimentId": "PATCH-META-001",
        "status": STATUS, "role": "calibration", "derivedFeatureCacheEnabled": False,
        "runId": base_summary["runId"], "headSha": base_summary["headSha"],
        "platform": base_summary["platform"], "sourceFamily": base_summary["sourceFamily"],
        "sourcePair": base_summary["sourcePair"], "sourceLocks": base_summary["sourceLocks"],
        "pairsSha256": base_summary["pairsSha256"], "sameBuildCspHash": base_summary["sameBuildCspHash"],
        "execution": "h2-w2", "lane": "csp", "rounds": 5, "creates": 10,
        "exactApplies": 10, "medianWarmBuilderOffSeconds": base_summary["medianWarmBuilderOffSeconds"],
        "medianWarmBuilderOnSeconds": base_summary["medianWarmBuilderOnSeconds"],
        "medianWarmConsumed": medians, "samples": observations,
        "hardClassification": {
            "candidateSearch": "derived-and-potentially-rebuildable",
            "dictionaryRead": "authoritative-read-and-dictionary-bytes; not metadata cache gain",
            "plainCodec": "target-dependent zstd encoding; not derived base metadata",
            "dictionaryCodec": "target-and-dictionary-dependent zstd encoding; not safe reusable base feature",
            "requiredChecks": "source/manifest/dictionary verification and exact apply remain mandatory",
        },
        "rfcSizeGate": "NOT_APPLICABLE",
        "readyToCloseIssue220": False, "readyToCloseGate251": False,
        "interpretation": (
            "Descriptive worker-sum wall times may overlap under H2-W2. "
            "Codec and dictionary load are measured as actual consumed create work, "
            "but MUST NOT be counted as reusable derived-feature/cache headroom. "
            "No cache-on/off performance comparison was performed; original single "
            "CLR release pair is not independent family confirmation. "
            "Off/on builder differences include child JIT, scheduling and probe overhead."),
    }
    (out / "codec-summary.json").write_text(
        json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("status", "creates",
        "medianWarmBuilderOffSeconds", "medianWarmBuilderOnSeconds",
        "medianWarmConsumed")}, indent=2, sort_keys=True))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--dll", required=True)
    p.add_argument("--corpus", required=True)
    p.add_argument("--work", required=True)
    p.add_argument("--output", required=True)
    p.add_argument("--rounds", type=int, default=5)
    args = p.parse_args()
    # G1B2A already ensures frozen real NuGet SHA, source hashes, exact CSP
    # output, exact Apply SHA, cold vs warm and alternating off/on.
    base.measure(args)
    postprocess(Path(args.output))


if __name__ == "__main__":
    main()
