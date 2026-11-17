#!/usr/bin/env python3
"""Fail-closed offline replay of PATCH-ML-001 first-release entry-gate evidence.

Uses only prior immutable, repository-tracked source JSON. No training,
model confirmation, original source materialization, or performance testing.
"""
from __future__ import annotations

import argparse
import copy
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEDGER = "benchmarks/experiments/patch-ml-001-first-release-defer-20261110.json"
REPORT = "docs/research/results/PATCH-ML-001-FIRST-RELEASE-DEFER-20261110-001.md"
LOCKED_CORPUS = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
LOCKED_G2 = "54061f4efe0969af0d772ddbc6d026fdf58bd4f2776514a9fb098753d062001d"


def need(value: bool, message: str) -> None:
    if not value:
        raise ValueError(message)


def load(root: Path, rel: str) -> dict:
    path = root / rel
    need(path.is_file(), "missing evidence: " + rel)
    result = json.loads(path.read_text(encoding="utf-8"))
    need(isinstance(result, dict), "expected JSON document: " + rel)
    return result


def verify(g2: dict, g2v: dict, h6: dict, h6v: dict, d: dict, ledger: dict) -> dict:
    source = ledger["sources"]
    need(ledger["schema"] == "chunkshift.patch-ml-001-first-release-defer.v1", "schema")
    need(ledger["experimentId"] == "PATCH-ML-001", "experiment")
    need(ledger["firstReleaseDecision"] == "DEFER_LEARNED_REFERENCE_RANKING__NO_PRODUCTION_ADOPT", "decision")
    for boolean in ("modelTrained", "modelWeightsAvailable",
                    "learnedCandidateA_BMeasured", "freshMLHoldoutOpened",
                    "defaultBuilderChanged", "publicApiChanged"):
        need(ledger[boolean] is False, "unwarranted ML/product claim " + boolean)
    need(ledger["preserveCspV1"] is True, "unexpected CSP change")
    need(source["lockedCorpusSha256"] == LOCKED_CORPUS, "corpus lock")
    need(source["lockedG2SampleSha256"] == LOCKED_G2, "sample lock")

    need(g2["schema"] == "chunkshift.patch-g2-oracle.v1", "G2 raw schema")
    need(g2v["schema"] == "chunkshift.patch-enc-005-g2-verdict.v1", "G2 verdict schema")
    need(g2["datasetRole"] == g2v["datasetRole"] == "calibration", "G2 dataset role")
    need(g2["datasetSha256"] == g2v["datasetSha256"] == LOCKED_CORPUS, "G2 dataset digest")
    need(g2["oracleSampleSha256"] == g2v["oracleSampleSha256"] == LOCKED_G2, "G2 sample digest")
    need(g2["sourceCommit"] == g2v["sourceCommit"], "G2 source commit")
    need(g2["runId"] == g2v["runId"], "G2 run")
    need(g2["policy"] == g2v["policy"] == "L19-K4-ALL-PREFIX-H20C20-REF32", "G2 policy")
    rows = g2["rows"]
    need(len(rows) == 256, "G2 number of rows")
    h0_sum = oracle_sum = starts = valid = improved = outside = 0
    pairs: dict[tuple[str, str, str], list[int]] = defaultdict(lambda: [0, 0, 0])
    for row in rows:
        old, new = int(row["h0CostBytes"]), int(row["oracleCostBytes"])
        saved = int(row["savedBytes"])
        need(old >= new >= 0 and saved == old - new, "G2 row cost/conservation")
        count = int(row["candidateStartsEnumerated"])
        valid_count = int(row["validCandidateCount"])
        need(0 <= valid_count <= count, "G2 candidate bounds")
        h0_sum += old
        oracle_sum += new
        starts += count
        valid += valid_count
        changed = int(saved > 0)
        improved += changed
        outside += int(changed and row["oracleEncoding"] == "zstd-dictionary"
                       and int(row["oracleStartDistanceBytes"]) > 256 * 1024)
        key = (row["family"], row["baseVersion"], row["targetVersion"])
        pair = pairs[key]
        pair[0] += old
        pair[1] += new
        pair[2] += 1
    gates = sum(int(v[1] * 100 <= v[0] * 97) for v in pairs.values())
    for field, value in (
        ("h0SampleCostBytes", h0_sum),
        ("g2SampleCostBytes", oracle_sum),
        ("savedBytes", h0_sum - oracle_sum),
        ("rowCount", len(rows)), ("improvedRows", improved),
        ("outsideRadiusWinners", outside),
        ("candidateStartsEnumerated", starts), ("validCandidateCount", valid),
        ("pairPassCount", gates),
    ):
        need(g2v[field] == value, "G2 verdict: " + field)
    need(g2v["status"] == "MISS" and g2v["nextAction"] == "H6_O_GUARD_ONLY",
         "G2 gate verdict")
    need(len(pairs) == 4 and all(v[2] == 64 for v in pairs.values()), "G2 four locked pairs")
    need(not g2v["overallFivePercentGate"] and oracle_sum * 100 > h0_sum * 95,
         "G2 five-percent gate")
    need(gates == 0 and h0_sum == 974223 and oracle_sum == 974049
         and improved == 11 and outside == 11 and starts == 10311,
         "G2 locked exact cost headroom")
    g = ledger["g2"]
    for name, value in (
        ("sampledEntries", len(rows)), ("pairs", len(pairs)),
        ("candidateStartsEnumerated", starts),
        ("baselineCostBytes", h0_sum),
        ("exactWholeBaseOracleCostBytes", oracle_sum),
        ("savedBytes", h0_sum - oracle_sum),
        ("improvedRows", improved), ("outsideRadiusWinners", outside),
        ("threePercentPairPasses", gates),
    ):
        need(g[name] == value, "ML ledger G2: " + name)
    need(g["fivePercentAggregateGatePassed"] is False
         and g["verdict"] == g2v["status"]
         and g["nextAction"] == g2v["nextAction"], "ML G2 disposition")

    need(h6["schema"] == "chunkshift.patch-enc-005-h6o-compact.v1", "H6 compact schema")
    need(h6v["schema"] == "chunkshift.patch-enc-005-h6o-guard-verdict.v1", "H6 verdict schema")
    need(h6["experimentId"] == h6v["experimentId"] == "PATCH-ENC-005", "H6 experiment")
    need(h6["sourceCommit"] == h6v["sourceCommit"], "H6 source commit")
    need(h6["runId"] == h6v["runId"], "H6 run")
    need(h6["datasetSha256"] == h6v["datasetSha256"] == LOCKED_CORPUS, "H6 dataset lock")
    need(h6v["datasetRole"] == "evaluation", "H6 dataset role")
    need(h6["lane"] == h6v["lane"] == "H6-O12-SF3-S128", "H6 lane")
    need(h6["timing"]["h0PatchBytes"] == h6v["h0PatchBytes"], "H6 H0 bytes")
    need(h6["timing"]["h6PatchBytes"] == h6v["h6PatchBytes"], "H6 trial bytes")
    h0 = h6v["h0PatchBytes"]
    candidate = h6v["h6PatchBytes"]
    ratios = h6["timing"]["wallRatios"]
    need(len(ratios) == 5 and all(0 < x < 1.5 for x in ratios), "H6 wall rounds")
    median = statistics.median(ratios)
    need(math.isclose(median, h6v["wallRatioMedian"], rel_tol=1e-12),
         "H6 median")
    need(math.isclose(candidate / h0, h6v["byteRatio"], rel_tol=1e-12),
         "H6 byte ratio")
    m = h6["memory"]
    need(m["h0MaxCreatePeakBytes"] - m["h0IdleBaselineBytes"] ==
         h6v["h0CreatePeakOverIdleBytes"], "H6 H0 memory")
    need(m["h6MaxCreatePeakBytes"] - m["h6IdleBaselineBytes"] ==
         h6v["h6CreatePeakOverIdleBytes"], "H6 trial memory")
    need(h6["correctness"]["filesCheckedPerLane"] == 844
         and h6["correctness"]["invalidDecoderVerdicts"] == 0
         and h6["correctness"]["decoderTargetDigestMismatches"] == 0,
         "H6 decoder correctness")
    need(h6["determinism"]["filesCheckedPerLane"] == 844
         and h6["determinism"]["acceptedVsCorrectnessPatchShaMismatches"] == 0,
         "H6 same-build determinism")
    need(h6v["status"] == "MISS" and h6v["nextAction"] == "STOP_RESEMBLANCE",
         "H6 frozen verdict")
    need(h6v["wallOk"] is True and h6v["memoryOk"] is True
         and h6v["correctnessOk"] is True and h6v["deterministicBytesOk"] is True
         and h6v["bytesOk"] is False, "H6 guard states")
    need(candidate == 30248709 and h0 == 26363364, "H6 source counts")
    h = ledger["h6"]
    for name, value in (
        ("evaluatedFiles", 844), ("baselinePhysicalCspBytes", h0),
        ("h6PhysicalCspBytes", candidate),
        ("physicalCspIncreaseBytes", candidate - h0),
    ):
        need(h[name] == value, "ML ledger H6: " + name)
    need(math.isclose(h["medianWallRatio"], median, rel_tol=1e-12),
         "ML H6 median")
    need(h["createWallGatePassed"] is True
         and h["physicalByteGatePassed"] is False
         and h["allAppliesVerified"] is True
         and h["verdict"] == "MISS"
         and h["nextAction"] == "STOP_RESEMBLANCE",
         "ML H6 decision")

    need(d["experimentId"] == "PATCH-DICT-001", "DICT experiment")
    need(d["physicalCspImprovementClaimsNotHoldout"] is True, "DICT unsealed provenance")
    need(d["source"]["frozenPairsSha256"] == LOCKED_CORPUS, "DICT source lock")
    total = d["total"]
    need(len(d["files"]) == total["originalFiles"] == 2, "DICT sample")
    need(sum(int(row["payloadEntries"]) for row in d["files"]) ==
         total["payloadEntries"] == 9, "DICT PAYL accounting")
    for arm in ("H0", "fixedK4", "variableK", "composite"):
        need(sum(int(row["fullPhysicalBytes"][arm]) for row in d["files"]) ==
             total["fullPhysicalBytes"][arm], "DICT arm conservation: " + arm)
    db = total["fullPhysicalBytes"]
    need(db == {"H0":11086, "fixedK4":11086, "variableK":10568, "composite":10568},
         "DICT exact four arms")
    need(total["variableKSavingsVsH0Percent"] > 4.67
         and total["variableKSavingsVsH0Percent"] < 4.68,
         "DICT reported percentage")
    need(total["variableKSavedReferenceBytes"] +
         total["variableKSavedFrameBytes"] == db["H0"] - db["variableK"],
         "DICT reference/frame conservation")
    ctx = ledger["dictContext"]
    for name, value in (
        ("fileCount",2), ("paylEntries",9),
        ("baselineWholeCspBytes",db["H0"]),
        ("variableKWholeCspBytes",db["variableK"]),
        ("variableKSavingsBytes",db["H0"] - db["variableK"]),
        ("compositeExtraSavingsBytes",db["variableK"] - db["composite"]),
    ):
        need(ctx[name] == value, "ML ledger DICT: " + name)
    need(ctx["newMLCandidateUniverseDemonstrated"] is False,
         "Do not claim a learned scorer or independent confirmation")
    need(all(ledger["adoptionAlternativesNotTested"][k] is True for k in (
        "physicalBytesAtLeastTenPercentWithLearnedRanking",
        "candidateTrialsAtLeastTwofoldFewerWithinOnePercentBytes")),
        "Unwarranted ML adoption verdict")

    return {
        "g2EntrySaved": h0_sum - oracle_sum, "g2Rows": len(rows),
        "g2CandidateStarts": starts, "g2PairPasses": gates,
        "h6PhysicalBytesIncrease": candidate - h0,
        "h6WallRatioMedian": median,
        "dictVariableKSaved": db["H0"] - db["variableK"],
        "dictCompositeIncrement": db["variableK"] - db["composite"],
        "decision": ledger["firstReleaseDecision"],
    }


def self_test(g2: dict, g2v: dict, h6: dict, h6v: dict, d: dict, ledger: dict) -> None:
    originals = (g2, g2v, h6, h6v, d, ledger)

    def rejection(index: int, mutate, label: str) -> None:
        copies = [copy.deepcopy(value) for value in originals]
        mutate(copies[index])
        try:
            verify(*copies)
        except (ValueError, KeyError, TypeError):
            return
        raise ValueError("adversarial mutation NOT rejected: " + label)

    rejection(0, lambda doc: doc["rows"][0].__setitem__(
        "oracleCostBytes", doc["rows"][0]["oracleCostBytes"] + 1), "corrupt G2 row")
    rejection(1, lambda doc: doc.__setitem__(
        "savedBytes", doc["savedBytes"] + 1), "corrupt G2 verdict")
    rejection(2, lambda doc: doc["timing"].__setitem__(
        "h6PatchBytes", doc["timing"]["h6PatchBytes"] - 1), "corrupt H6 compact")
    rejection(3, lambda doc: doc.__setitem__(
        "bytesOk", True), "false H6 gate")
    rejection(4, lambda doc: doc["files"][0]["fullPhysicalBytes"].__setitem__(
        "variableK", doc["files"][0]["fullPhysicalBytes"]["variableK"] + 1),
        "corrupt DICT file")
    rejection(5, lambda doc: doc["g2"].__setitem__(
        "savedBytes", 0), "false ML ledger")
    print("PASS: six independent fail-closed mutation controls")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    root = args.repo_root.resolve()
    ledger = load(root, LEDGER)
    sources = ledger["sources"]
    docs = [
        load(root, sources[k]) for k in
        ("g2Oracle", "g2Verdict", "h6Compact", "h6Verdict", "dictCloseout")
    ]
    need((root / REPORT).is_file(), "Missing durable decision report")
    summary = verify(*docs, ledger)
    print("PASS PATCH-ML-001 source-backed first-release DEFER:", json.dumps(
        summary, sort_keys=True))
    if args.self_test:
        self_test(*docs, ledger)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
