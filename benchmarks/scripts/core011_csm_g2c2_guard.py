#!/usr/bin/env python3
"""CORE011-CSM-002/G2-C2 preregistered gated-stage public CSM A/B guard.

Rejects incomplete source-hashed, sample-identity, CBLK or corruption oracles.
ALWAYS returns an exploratory verdict, never production ADOPT.
"""
import argparse
import json
import math
from pathlib import Path

VARIANTS = {"baseline": 0, "gate256-stage64": 64, "gate256-stage256": 256}
OPERATIONS = {"manifest-verify", "enumerate"}
# Stable physical CSMs recorded in source-free G2-C0/G2-C1 evidence, before G2-C2.
CASES = {
    "small-blake3-bidx": ("small", "blake3", True, 1, 11, 1,
        "472b7fcdb8c8c5dd28db9d0e54938824deb862f1153a9bc1de410a30a02c89bb"),
    "small-sha256-nobidx": ("small", "sha256", False, 8, 102, 1,
        "f50145ca497cb1a05a91a72e6a696337a0644a46668bbec53042e12ac50f7147"),
    "multi-blake3-bidx": ("multi", "blake3", True, 384, 5052, 2,
        "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"),
    "multi-sha256-nobidx": ("multi", "sha256", False, 384, 5052, 2,
        "cb93b8bac6e1bd34fc5e72f7082d6296ea815f1c8a148f9cf0929af2f4a689b6"),
}
BLOB = "894c61b68bfba111467f061e8a0caf81bc8eea81"
SCHEMA = "chunkshift.core011-csm-g2c2-consumer.v1"


def read(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def validate(root: Path) -> dict:
    receipts = {v: read(root / f"{v}.receipt.json") for v in VARIANTS}
    checkout = receipts["baseline"]["checkoutSha"]
    pristine = receipts["baseline"]["pristineSha256"]
    modified_sha = set()
    for variant, cap in VARIANTS.items():
        receipt = receipts[variant]
        if (receipt.get("schema") != "chunkshift.core011-csm-g2c2-gated-variant.v1"
                or receipt.get("variant") != variant
                or receipt.get("experimentId") != "CORE011-CSM-002"
                or receipt.get("stage") != "G2-C2-NONDECISION"
                or receipt.get("thresholdEntries") != 256
                or receipt.get("batchEntries") != cap
                or receipt.get("additionalStageBufferBytes") != cap * 36
                or receipt.get("lazyStageAllocation") != (cap != 0)
                or receipt.get("sourceModified") != (cap != 0)
                or receipt.get("pristineBlob") != BLOB
                or receipt.get("checkoutSha") != checkout
                or receipt.get("pristineSha256") != pristine
                or receipt.get("releaseDecision") != "NO_VERDICT_EXPLORATORY"):
            raise ValueError(f"Source variant identity / preregistration changed: {variant}")
        source_sha = receipt.get("variantSha256")
        if not isinstance(source_sha, str) or len(source_sha) != 64:
            raise ValueError(f"Missing variant source SHA: {variant}")
        modified_sha.add(source_sha)
    if len(modified_sha) != 3 or receipts["baseline"]["variantSha256"] != pristine:
        raise ValueError("Expected three distinct compiled source hashes and untouched baseline")
    cases = {}
    outputs = []
    valid_count = 0
    for case, (kind, suite, bidx, size, expected_chunks, expected_blocks, expected_sha) in CASES.items():
        path = root / case
        fixture = read(path / "fixture.json")
        if ((path / "source.bin").exists() or (path / "original.csm").exists()
                or fixture.get("schema") != "chunkshift.core011.g1j-fixture.v1"
                or fixture.get("kind") != "manifest-only-verify"
                or fixture.get("builderCheckoutSha") != checkout
                or fixture.get("sourceSha256") is None
                or fixture.get("manifestSha256") != expected_sha
                or fixture.get("manifestBytes", 0) <= 0
                or fixture.get("hashSuite") != suite
                or fixture.get("hasBlockIndex") != bidx
                or fixture.get("contentBytes") != size * 1024 * 1024
                or fixture.get("chunkCount") != expected_chunks
                or fixture.get("cblkCount") != expected_blocks
                or fixture.get("builderPid", 0) <= 0
                or fixture.get("consumerMayGenerateContent") is not False
                or fixture.get("verdict") != "NO_VERDICT_EXPLORATORY"):
            raise ValueError(f"Fixture differs from independently frozen G2-C0/G2-C1 bytes: {case}")
        cases[case] = {
            "sha256": expected_sha, "chunkCount": expected_chunks, "cblkCount": expected_blocks,
            "fileDigest": fixture["fileDigest"], "manifestId": fixture["manifestId"],
            "sourceSha256": fixture["sourceSha256"],
        }
        files = sorted((path / "samples").glob("*.json"))
        if len(files) != 8:
            raise ValueError(f"Missing separate-process A/B/A results in {case}: {len(files)}")
        seen = set()
        for file in files:
            x = read(file)
            variant = x.get("variant")
            operation = x.get("operation")
            repeat = file.stem.endswith("-repeat")
            key = (operation, variant, repeat)
            if (variant not in VARIANTS or operation not in OPERATIONS or
                    key in seen or (repeat and variant != "baseline")):
                raise ValueError(f"Unexpected or repeated sample: {file}")
            seen.add(key)
            if (x.get("schema") != SCHEMA
                    or x.get("experimentId") != "CORE011-CSM-002"
                    or x.get("stage") != "G2-C2-NONDECISION"
                    or x.get("verdict") != "NO_VERDICT_EXPLORATORY"
                    or x.get("kind") != kind
                    or x.get("checkoutSha") != checkout
                    or x.get("hashSuite") != suite
                    or x.get("hasBlockIndex") != bidx
                    or x.get("chunkCount") != expected_chunks
                    or x.get("cblkCount") != expected_blocks
                    or x.get("contentLength") != size * 1024 * 1024
                    or x.get("fixtureCsmSha256") != expected_sha
                    or x.get("fixtureSourceSha256") != fixture["sourceSha256"]
                    or x.get("fileDigest") != fixture["fileDigest"]
                    or x.get("manifestId") != fixture["manifestId"]
                    or x.get("builderPid") != fixture["builderPid"]
                    or x.get("consumerPid", 0) <= 0
                    or x.get("consumerPid") == fixture["builderPid"]
                    or x.get("corruptionTargetBlock") != (2 if kind == "multi" else 1)
                    or x.get("corruptionExposedEntries") != (4096 if kind == "multi" else 0)
                    or "BlockCrc" not in x.get("corruptionFailures", "")
                    or x.get("rounds") != 3
                    or x.get("repetitions") != 4
                    or len(x.get("samples", [])) != 3):
                raise ValueError(f"Invalid CSM identity, process or CRC oracle: {file}")
            rounds = x["samples"]
            if {s["round"] for s in rounds} != {1, 2, 3}:
                raise ValueError(f"Missing/duplicate rounds: {file}")
            for s in rounds:
                if (s.get("completed") != 4
                        or s.get("allocatedBytes", -1) < 0
                        or s.get("workingSetBytes", 0) <= 0):
                    raise ValueError(f"Missing actual measurement: {file}")
                for number in ("wallMilliseconds", "cpuMilliseconds", "gcPauseMilliseconds"):
                    n = s.get(number)
                    if not isinstance(n, (float, int)) or not math.isfinite(n) or n < 0 or (
                        number == "wallMilliseconds" and n == 0):
                        raise ValueError(f"Invalid measurement: {file}")
            wall = sorted(s["wallMilliseconds"] / 4 for s in rounds)
            outputs.append({
                "case": case, "operation": operation,
                "variant": variant, "repeatBaseline": repeat,
                "medianWallMsPerCall": wall[1],
                "allocatedBytesPerCall": sum(s["allocatedBytes"] for s in rounds) / 12,
                "postGcManagedBytes": x["postGcManagedBytes"],
                "postGcLohBytes": x["postGcLohBytes"],
                "postGcWorkingSetBytes": x["postGcWorkingSetBytes"],
            })
            valid_count += 1
        expected = {(op, v, False) for op in OPERATIONS for v in VARIANTS}
        expected |= {(op, "baseline", True) for op in OPERATIONS}
        if seen != expected:
            raise ValueError(f"Incomplete comparison in {case}: {expected - seen}")
    if valid_count != 32:
        raise ValueError(f"Expected 32 independently verified processes, got {valid_count}")
    return {
        "schema": "chunkshift.core011-csm-g2c2-pilot-guard.v1",
        "experimentId": "CORE011-CSM-002",
        "stage": "G2-C2-NONDECISION",
        "thresholdEntries": 256,
        "checkoutSha": checkout,
        "cases": cases,
        "sourceReceipts": receipts,
        "independentProcessResults": 32,
        "measuredRounds": 96,
        "rawMeasurements": outputs,
        "artifactHasOriginalContent": False,
        "adoptPermitted": False,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "limitations": "One runner, synthetic controls, no product decision budgets, no real holdout, no concurrent/idle memory qualification.",
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True, type=Path)
    p.add_argument("--output", required=True, type=Path)
    args = p.parse_args()
    evidence = validate(args.root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2, sort_keys=True) + "\n")
    print(f"G2-C2 correctness PASS: {evidence['independentProcessResults']} processes,"
          f" {evidence['measuredRounds']} rounds; {evidence['verdict']}")


if __name__ == "__main__":
    main()
