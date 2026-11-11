#!/usr/bin/env python3
"""E3-R0 preregistration ONLY. Decision-ready execution always fails closed."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

EXPERIMENT = "CORE011-CSM-004"
SCHEMA = "chunkshift.core011.csm-e3-reader-contract.v1"
READER_SHA1 = "ff56ccddaa0efc9970fd0b019e20523d6c2933d6"
BASE_SHA = "b085e12edb288137c505d6ed43ad7251ec19b928"
CANDIDATES = ["eager", "lazy", "hybrid1", "hybrid2", "hybrid4"]
CAPS = {"eager": None, "lazy": None, "hybrid1": 1, "hybrid2": 2, "hybrid4": 4}
COUNTS = [0, 1, 11, 102, 2274, 2275, 4095, 4096, 4097, 5052, 8193]
METRICS = [
    "allocatedBytesPerCall", "gcLohAllocationBytes", "gen2Collections",
    "gcPauseMilliseconds", "perCallLatencyP50Ms", "perCallLatencyP95Ms",
    "batchCpuMs", "batchWallMs", "throughputCallsPerSec", "peakWorkingSetBytes",
    "postIdleWorkingSetBytes", "peakPrivateBytes", "postIdlePrivateBytes",
    "postGcManagedHeapBytes", "activeReadersMax", "firstEntryLatencyMs",
    "poolRentCount", "poolReturnCount", "poolMissCount",
]
BUDGETS = {
    "cpuRatioUpperBound", "verifyWallRatioUpperBound", "planWallRatioUpperBound",
    "readerWallRatioUpperBound", "throughputRatioLowerBound",
    "allocatedBytesPerCallUpperBound", "gen2IncrementUpperBound",
    "gcPauseRatioUpperBound", "peakWorkingSetIncrementBytes",
    "postIdleWorkingSetIncrementBytes", "peakPrivateBytesIncrementBytes",
    "postIdlePrivateBytesIncrementBytes", "postGcManagedHeapIncrementBytes",
    "totalWorkingSetMaxBytes", "confidenceLevel", "minimumIndependentHostBlocks",
    "minimumMeasuredCallsPerBlock",
}
TOP = {
    "schema", "experimentId", "ownerIssue", "parentIssue", "protocol",
    "stage", "status", "decisionRunsAllowed", "budgetsLocked",
    "historicalReleaseDisposition", "baseline", "candidates",
    "primaryCandidates", "diagnosticCandidates", "poolCaps", "chunkCounts",
    "hashSuites", "blockIndexModes", "manifestStreamModes", "publicPaths",
    "concurrency", "stressConcurrency", "processIsolation", "correctness",
    "metrics", "idleObservationSeconds", "baselineCalibrationEvidenceId",
    "corpusFreezeSha256", "decisionProtocolFreezeCommitSha", "numericBudgets",
    "decisionRequirements",
}
HERE = Path(__file__).resolve().parents[2]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def blob_sha1(data: bytes) -> str:
    """Compute Git's blob SHA-1, not plain SHA-1 of file bytes."""
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def validate_source_pin(obj: dict, reader_bytes: bytes) -> None:
    baseline = obj["baseline"]
    require(blob_sha1(reader_bytes) == baseline["readerGitBlobSha1"],
            "reader source drift: update baseline through a separate review")


def validate_contract(obj: dict, *, for_decision: bool = False) -> dict:
    require(type(obj) is dict and set(obj) == TOP, "unknown/missing E3-R0 contract fields")
    require(obj["schema"] == SCHEMA and obj["experimentId"] == EXPERIMENT and
            obj["ownerIssue"] == 154 and obj["parentIssue"] == 152 and
            obj["protocol"] == "docs/benchmarks/CORE011-CSM-004-E3-READER-PROTOCOL.md",
            "invalid E3-R0 ownership/schema")
    require(obj["historicalReleaseDisposition"] == "DEFER" and
            obj["stage"] == "preregistration" and
            obj["status"] == "NO_VERDICT_EXPLORATORY" and
            obj["decisionRunsAllowed"] is False and obj["budgetsLocked"] is False,
            "R0 preregistration locked: no decision runs")
    require(not for_decision, "R0 decision-ready execution is unconditionally forbidden")
    require(obj["baseline"] == {
        "mainCommitSha": BASE_SHA,
        "readerPath": "src/ChunkShift/Manifest/CsmReader.cs",
        "readerGitBlobSha1": READER_SHA1,
        "maxCblkRecordBytes": 147500,
        "smallCblkThresholdBytes": 81920,
        "readAheadBytes": 4096,
    }, "baseline reader source/physical bounds changed")
    require(obj["candidates"] == CANDIDATES and
            obj["primaryCandidates"] == ["lazy", "hybrid2"] and
            obj["diagnosticCandidates"] == ["hybrid1", "hybrid4"] and
            obj["poolCaps"] == CAPS, "candidate selection or pool caps changed")
    require(obj["chunkCounts"] == COUNTS and
            44 + 36 * 2274 == 81908 and 44 + 36 * 2275 == 81944 and
            44 + 36 * 4096 == 147500,
            "CBLK boundary or count control changed")
    require(obj["hashSuites"] == ["chunkshift.blake3-256.v1", "chunkshift.sha256.v1"] and
            obj["blockIndexModes"] == [False, True] and
            obj["manifestStreamModes"] ==
            ["memory", "file", "fragmented-short-read", "nonseekable"] and
            obj["publicPaths"] ==
            ["verify-manifest", "manifest-reader-full", "patch-plan"] and
            obj["concurrency"] == [1, 4, 16] and obj["stressConcurrency"] == 64,
            "protected scenario matrix weakened")
    require(obj["processIsolation"] == {
        "externalFixtureBuilder": True, "freshConsumerPerCell": True,
        "consumerGeneratesContent": False, "pinPhysicalCsmSha256": True,
        "recordConsumerPid": True, "contentPathReadForbiddenForManifestOnly": True,
    }, "source-free process separation weakened")
    require(obj["correctness"] == {
        "exactCsmBytes": True, "manifestId": True, "fileDigest": True,
        "chunkSequences": True, "crcFailureFlags": True,
        "wholeBlockPreExposure": True, "secondBlockCrcStopsAt4096": True,
        "exceptionClassAndOrdering": True, "bidx": True,
        "cancellationAndDispose": True, "fullCapacityClearOnReturn": True,
        "sourceStreamCallerOwned": True,
    }, "semantic/failure/ownership oracle weakened")
    require(obj["metrics"] == METRICS and obj["idleObservationSeconds"] == [0, 30, 300],
            "missing, duplicated, or reordered memory and CPU measurements")
    require(obj["baselineCalibrationEvidenceId"] is None and
            obj["corpusFreezeSha256"] is None and
            obj["decisionProtocolFreezeCommitSha"] is None and
            type(obj["numericBudgets"]) is dict and
            set(obj["numericBudgets"]) == BUDGETS and
            all(value is None for value in obj["numericBudgets"].values()),
            "R0 numeric decision budgets must remain entirely unset")
    require(obj["decisionRequirements"] == {
        "separateCalibration": True, "realVersionedMixedReuseMultiCblk": True,
        "linuxAndWindowsHosted": True, "supportedNativeAotAndArm64": True,
        "independentHostBlocks": True, "immutableCorpusAndSourcePins": True,
        "exactSemanticOracle": True, "rawSamplesAndFailureArchive": True,
        "immutablePriorG1J": True, "productionSourceChangesAllowed": False,
    }, "independent decision gate weakened")
    return {"experimentId": EXPERIMENT, "stage": "preregistration",
            "decisionRunsAllowed": False, "productVerdict": "NOT_QUALIFIED"}


def check_historical_contract(root: Path) -> None:
    frozen = root / "benchmarks/experiments/core011-csm-g1j-contract.v1.json"
    g1j = json.loads(frozen.read_text(encoding="utf-8"))
    require(g1j.get("experimentId") == "CORE011-CSM-001" and
            g1j.get("stage") == "preregistration" and
            g1j.get("decisionRunsAllowed") is False and
            g1j.get("budgetsLocked") is False, "historical G1-J changed")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--contract", type=Path, default=HERE / "benchmarks/experiments/core011-csm-e3-reader-contract.v1.json")
    ap.add_argument("--decision-ready", action="store_true")
    args = ap.parse_args()
    try:
        obj = json.loads(args.contract.read_text(encoding="utf-8"))
        result = validate_contract(obj, for_decision=args.decision_ready)
        reader_path = HERE / obj["baseline"]["readerPath"]
        validate_source_pin(obj, reader_path.read_bytes())
        check_historical_contract(HERE)
        print(json.dumps(result, sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(f"E3-R0 FAIL-CLOSED: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
