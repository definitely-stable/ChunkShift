#!/usr/bin/env python3
"""CORE011-CSM-001 G1-J preregistration and isolated fixture integrity gates.

NO benchmark execution and NO product adoption in this module.
A builder/consumer OS-process boundary must also be enforced by the future runner.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re

CONTRACT_SCHEMA = "chunkshift.core011.csm-g1j-contract.v1"
FIXTURE_SCHEMA = "chunkshift.core011.g1j-fixture.v1"
CANDIDATES = ("eager", "lazy", "hybrid1", "hybrid2", "hybrid4")
CAPS = {"eager": None, "lazy": None, "hybrid1": 1, "hybrid2": 2, "hybrid4": 4}
BASELINE_SOURCE_SHA = "a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2"
REQUIRED_METRICS = {
    "allocatedBytesPerCall", "gcPauseMilliseconds", "gen2Collections",
    "batchCpuMilliseconds", "batchWallMilliseconds", "throughputCallsPerSecond",
    "peakWorkingSetBytes", "postIdleWorkingSetBytes", "privateBytes",
    "postGcManagedHeapBytes", "lastGcLohBytes", "maxActiveReaders",
    "firstEntryLatencyMilliseconds", "perCallLatencySamplesMilliseconds",
    "poolRentCount", "poolReturnCount", "poolMissCount",
}
NUMERIC_BUDGETS = {
    "cpuRatioUpperBound", "planWallRatioUpperBound", "verifyWallRatioUpperBound",
    "peakWorkingSetIncrementBytes", "idleWorkingSetIncrementBytes",
    "privateBytesIncrementBytes", "retainedManagedHeapIncrementBytes",
    "allocatedBytesPerCallMax", "gcPauseRatioUpperBound", "gen2IncrementMax",
    "confidenceLevel", "independentBlocksMinimum",
}
HEX64 = re.compile(r"^[0-9a-f]{64}$")
HEX40 = re.compile(r"^[0-9a-f]{40}$")
REQUIRED_CLASSES = (
    "short", "threshold-below", "threshold-at", "threshold-above",
    "multiblock-384m", "larger-multiblock", "real-versioned-small-edit",
    "real-versioned-insert-delete", "real-versioned-heavy-replacement",
    "real-versioned-low-reuse", "real-versioned-mostly-unchanged",
)


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(64 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_contract(obj: dict, *, for_decision: bool = False) -> dict:
    require(type(obj) is dict and obj.get("schema") == CONTRACT_SCHEMA,
            "unknown G1-J contract schema")
    require(obj.get("experimentId") == "CORE011-CSM-001" and
            obj.get("ownerIssue") == 154 and
            obj.get("protocol") ==
            "docs/benchmarks/CORE011-CSM-001-G1J-QUALIFICATION-PROTOCOL.md",
            "incorrect experiment ownership")
    require(obj.get("sourceBaselineSha256") == BASELINE_SOURCE_SHA and
            obj.get("candidates") == list(CANDIDATES) and
            obj.get("poolCaps") == CAPS and
            obj.get("smallCblkThresholdBytes") == 81920,
            "unregistered reader variants/source")
    require(obj.get("sourcePolicy") == {
        "temporaryDetachedWorktreesOnly": True,
        "productionSourceChangesAllowed": False,
    }, "unsafe production source policy")
    require(obj.get("processIsolation") == {
        "dedicatedFixtureBuilder": True,
        "freshConsumerPerCell": True,
        "consumerMayCreateSourceBytes": False,
        "recordOsPid": True,
        "verifyExactCsmDigest": True,
    }, "source-free process isolation not required")
    require(obj.get("workloadClasses") == list(REQUIRED_CLASSES),
            "missing or reordered protected workload strata")
    metrics = obj.get("metrics")
    require(type(metrics) is list and set(metrics) == REQUIRED_METRICS and
            len(metrics) == len(REQUIRED_METRICS),
            "missing/duplicate measurement metrics")
    require(obj.get("concurrency") == [1, 4, 16] and
            obj.get("experimentalCpuRunnersMinimum") >= 4 and
            obj.get("windowsRunnersMinimum") >= 2,
            "missing hosted concurrency/replication policy")
    require(obj.get("decisionGuard") == {
        "requireAllBudgets": True,
        "requireRealVersionedCorpus": True,
        "requireWindowsAndSupportedAot": True,
        "requireUnchangedCsmCspSemantics": True,
        "requireArchivedFailures": True,
    }, "weakened decision guard")
    require(obj.get("verdict") == "NO_VERDICT_EXPLORATORY",
            "premature research verdict")
    stage = obj.get("stage")
    require(stage in ("preregistration", "calibration", "decision_ready"),
            "unknown G1-J protocol stage")
    budgets = obj.get("budgetPolicy")
    require(type(budgets) is dict and
            set(budgets) == NUMERIC_BUDGETS | {
                "baselineCalibrationEvidenceId", "freezeCommitSha",
            }, "incomplete predeclared performance budget structure")
    if stage != "decision_ready":
        require(obj.get("decisionRunsAllowed") is False and
                obj.get("budgetsLocked") is False and
                all(value is None for value in budgets.values()),
                "calibration/preregistration cannot contain unlocked decision thresholds")
        require(not for_decision, "G1-J decision runs forbidden before budget lock")
    else:
        require(obj.get("decisionRunsAllowed") is True and
                obj.get("budgetsLocked") is True,
                "G1-J decision requires locked budgets")
        evidence = budgets["baselineCalibrationEvidenceId"]
        freeze_sha = budgets["freezeCommitSha"]
        require(type(evidence) is str and
                evidence.startswith("CORE011-CSM-001/EVIDENCE-") and
                HEX40.fullmatch(freeze_sha or "") is not None,
                "decision thresholds lack independent calibration/freeze reference")
        for key in NUMERIC_BUDGETS:
            number = budgets.get(key)
            require(type(number) in (int, float) and
                    math.isfinite(number) and number >= 0,
                    "missing/nonfinite decision threshold: " + key)
        require(0.5 < budgets["confidenceLevel"] < 1 and
                budgets["independentBlocksMinimum"] >= 4 and
                budgets["cpuRatioUpperBound"] > 0 and
                budgets["gcPauseRatioUpperBound"] > 0,
                "invalid decision confidence/replication/ratios")
    return {"schema": CONTRACT_SCHEMA, "stage": stage,
            "decisionRunsAllowed": obj["decisionRunsAllowed"],
            "verdict": "NO_VERDICT_EXPLORATORY"}


def validate_manifest_only_fixture(obj: dict, root: Path) -> dict:
    """Validate physical CSM identity without opening/generating source content.

    Only preflight integrity is proved here. Separate-process isolation,
    semantic CSM validity and no-content-read are future runner obligations.
    """
    require(type(obj) is dict and obj.get("schema") == FIXTURE_SCHEMA,
            "unknown fixture schema")
    require(obj.get("kind") == "manifest-only-verify" and
            obj.get("builderProcessRole") == "external-fixture-builder" and
            obj.get("consumerMayGenerateContent") is False,
            "consumer may construct source content")
    require(obj.get("provenance") in ("synthetic", "real-versioned") and
            type(obj.get("corpusId")) is str and obj["corpusId"],
            "missing corpus provenance")
    name = obj.get("manifestFile")
    require(type(name) is str and name.endswith(".csm") and
            Path(name).name == name and name not in (".", "..") and
            "/" not in name and "\\" not in name,
            "unsafe fixture path")
    require(type(obj.get("manifestBytes")) is int and
            0 < obj["manifestBytes"] <= (1 << 63) - 1 and
            type(obj.get("chunkCount")) is int and obj["chunkCount"] >= 0 and
            type(obj.get("cblkCount")) is int and obj["cblkCount"] >= 0,
            "invalid bounded manifest metadata")
    for key in ("manifestSha256", "sourceSha256"):
        require(type(obj.get(key)) is str and HEX64.fullmatch(obj[key]) is not None,
                "missing SHA256 fixture digest: " + key)
    require(type(obj.get("builderCheckoutSha")) is str and
            HEX40.fullmatch(obj["builderCheckoutSha"]) is not None,
            "missing pinned fixture builder revision")
    path = root.resolve() / name
    require(path.is_file() and not path.is_symlink(),
            "missing or symlinked CSM fixture")
    require(path.stat().st_size == obj["manifestBytes"] and
            sha256_file(path) == obj["manifestSha256"],
            "physical CSM bytes do not match frozen fixture digest")
    return {"schema": FIXTURE_SCHEMA, "corpusId": obj["corpusId"],
            "manifestSha256": obj["manifestSha256"], "manifestBytes": obj["manifestBytes"],
            "note": "CSM hash integrity only; consumer isolation/semantic validation not proved"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--contract", type=Path, required=True)
    parser.add_argument("--decision-ready", action="store_true")
    parser.add_argument("--fixture", type=Path)
    parser.add_argument("--fixture-root", type=Path)
    args = parser.parse_args()
    contract = json.loads(args.contract.read_text(encoding="utf-8"))
    result = validate_contract(contract, for_decision=args.decision_ready)
    if (args.fixture is None) != (args.fixture_root is None):
        parser.error("--fixture and --fixture-root must be passed together")
    if args.fixture:
        fixture = json.loads(args.fixture.read_text(encoding="utf-8"))
        result["fixture"] = validate_manifest_only_fixture(fixture, args.fixture_root)
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    main()
