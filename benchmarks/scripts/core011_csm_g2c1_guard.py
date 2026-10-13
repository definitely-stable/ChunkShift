#!/usr/bin/env python3
"""CORE011-CSM-002/G2-C1 strict multi-CBLK A/B pilot guard, NO_ADOPT."""
import argparse
import hashlib
import json
import math
from pathlib import Path

VARIANTS = {"baseline": 0, "stage64": 64, "stage256": 256}
OPERATIONS = {"manifest-verify", "enumerate"}
CASES = {
    "small-blake3-bidx": ("small", "blake3", True, 1),
    "multi-blake3-bidx": ("multi", "blake3", True, 384),
    "multi-sha256-nobidx": ("multi", "sha256", False, 384),
}
BLOB = "894c61b68bfba111467f061e8a0caf81bc8eea81"


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def assert_source_receipts(root: Path) -> tuple[str, dict]:
    receipts = {v: load(root / f"{v}.receipt.json") for v in VARIANTS}
    checkouts = set()
    pristine = set()
    for v, r in receipts.items():
        if (r.get("variant") != v or r.get("batchEntries") != VARIANTS[v]
                or r.get("pristineBlob") != BLOB
                or r.get("releaseDecision") != "NO_VERDICT_EXPLORATORY"):
            raise ValueError(f"Source variant invalid: {v}")
        if len(r.get("variantSha256", "")) != 64:
            raise ValueError(f"Variant source hash missing: {v}")
        checkouts.add(r["checkoutSha"])
        pristine.add(r["pristineSha256"])
    if len(checkouts) != 1 or len(pristine) != 1 or (
            receipts["baseline"]["variantSha256"]
            != receipts["baseline"]["pristineSha256"]):
        raise ValueError("All variant sources must share pristine checkout")
    if len({receipts[v]["variantSha256"] for v in VARIANTS}) != 3:
        raise ValueError("Expected three different compiled Core sources")
    return checkouts.pop(), receipts


def validate(root: Path) -> dict:
    checkout, receipts = assert_source_receipts(root)
    verified = 0
    summaries = []
    case_identities = {}
    all_pids = set()
    for case, (kind, suite, bidx, mib) in CASES.items():
        case_dir = root / case
        fixture = load(case_dir / "fixture.json")
        if (fixture.get("schema") != "chunkshift.core011.g1j-fixture.v1"
                or fixture.get("sourceSha256") is None
                or fixture.get("manifestSha256") is None
                or fixture.get("hasBlockIndex") != bidx
                or fixture.get("hashSuite") != suite
                or fixture.get("contentBytes") != mib * 1024 * 1024
                or fixture.get("builderCheckoutSha") != checkout
                or fixture.get("builderPid", 0) <= 0
                or fixture.get("verdict") != "NO_VERDICT_EXPLORATORY"
                or fixture.get("consumerMayGenerateContent") is not False):
            raise ValueError(f"Wrong immutable source-free fixture: {case}")
        count = fixture["chunkCount"]
        blocks = fixture["cblkCount"]
        if kind == "multi" and (count <= 4096 or blocks < 2):
            raise ValueError("Multi-CBLK fixture did not cross the required CBLK boundary")
        if kind == "small" and (count < 1 or count > 4096 or blocks != 1):
            raise ValueError("Small CSM fixture is not a short-block control")
        if (case_dir / "source.bin").exists() or (case_dir / "original.csm").exists():
            raise ValueError("Raw contents must be removed before publication")
        reference = {
            "chunkCount": count, "cblkCount": blocks,
            "manifestSha256": fixture["manifestSha256"],
            "sourceSha256": fixture["sourceSha256"],
            "manifestId": fixture["manifestId"],
            "fileDigest": fixture["fileDigest"],
            "contentBytes": fixture["contentBytes"],
            "hashSuite": suite,
            "hasBlockIndex": bidx,
        }
        case_identities[case] = reference
        paths = sorted((case_dir / "samples").glob("*.json"))
        if len(paths) != 8:
            raise ValueError(f"Need 2 operations × (3 variants+baseline repeat): {case}")
        seen = set()
        for p in paths:
            data = load(p)
            v = data.get("variant")
            operation = data.get("operation")
            repeat = p.stem.endswith("-repeat")
            k = (operation, v, repeat)
            if k in seen or operation not in OPERATIONS or v not in VARIANTS:
                raise ValueError(f"Unexpected / duplicate G2-C1 process sample {p}")
            seen.add(k)
            if repeat and v != "baseline":
                raise ValueError(f"Only baseline may repeat: {p}")
            if (data.get("schema") != "chunkshift.core011-csm-g2c1-consumer.v1"
                    or data.get("stage") != "G2-C1-NONDECISION"
                    or data.get("experimentId") != "CORE011-CSM-002"
                    or data.get("verdict") != "NO_VERDICT_EXPLORATORY"
                    or data.get("checkoutSha") != checkout
                    or data.get("kind") != kind
                    or data.get("hashSuite") != suite
                    or data.get("hasBlockIndex") != bidx
                    or data.get("fixtureCsmSha256") != fixture["manifestSha256"]
                    or data.get("fixtureSourceSha256") != fixture["sourceSha256"]
                    or data.get("chunkCount") != count
                    or data.get("cblkCount") != blocks
                    or data.get("manifestId") != fixture["manifestId"]
                    or data.get("fileDigest") != fixture["fileDigest"]
                    or data.get("contentLength") != fixture["contentBytes"]
                    or data.get("builderPid") != fixture["builderPid"]
                    or data.get("corruptionTargetBlock") != (2 if kind == "multi" else 1)
                    or data.get("corruptionExposedEntries") != (4096 if kind == "multi" else 0)
                    or "BlockCrc" not in data.get("corruptionFailures", "")
                    or data.get("rounds") != 3 or data.get("repetitions") != 4
                    or len(data.get("samples", [])) != 3):
                raise ValueError(f"Failed exact identity / corruption / measurement contract: {p}")
            pid = data.get("consumerPid")
            if not isinstance(pid, int) or pid < 1 or pid == fixture["builderPid"]:
                raise ValueError(f"Builder/consumer process isolation missing: {p}")
            # PIDs can technically be recycled; process identity is strengthened
            # by fresh CLI launches in YAML. Do not treat unique PIDs as proof.
            all_pids.add(pid)
            for s in data["samples"]:
                if (s.get("completed") != 4 or s.get("round") not in (1, 2, 3)
                        or s.get("allocatedBytes", -1) < 0
                        or s.get("wallMilliseconds", -1) <= 0
                        or s.get("workingSetBytes", -1) <= 0):
                    raise ValueError(f"Invalid raw round data: {p}")
                for numeric in ("wallMilliseconds", "cpuMilliseconds",
                                "gcPauseMilliseconds"):
                    if not math.isfinite(s[numeric]) or s[numeric] < 0:
                        raise ValueError(f"Invalid finite timing: {p}")
            ordered = sorted(s["wallMilliseconds"] / 4 for s in data["samples"])
            summaries.append({
                "case": case, "operation": operation, "variant": v,
                "repeatBaseline": repeat,
                "medianWallMsPerCall": ordered[1],
                "allocatedBytesPerCall": sum(s["allocatedBytes"] for s in data["samples"]) / 12,
                "postGcManagedBytes": data["postGcManagedBytes"],
                "postGcLohBytes": data["postGcLohBytes"],
                "postGcWorkingSetBytes": data["postGcWorkingSetBytes"],
            })
            verified += 1
        expected = {(op, v, False) for op in OPERATIONS for v in VARIANTS}
        expected |= {(op, "baseline", True) for op in OPERATIONS}
        if seen != expected:
            raise ValueError(f"Missing variant result in {case}: {expected-seen}")
    if verified != 24:
        raise ValueError("Incomplete multi-block study")
    return {
        "schema": "chunkshift.core011-csm-g2c1-pilot-guard.v1",
        "experimentId": "CORE011-CSM-002",
        "stage": "G2-C1-NONDECISION",
        "checkoutSha": checkout,
        "sourceReceipts": receipts,
        "caseIdentities": case_identities,
        "independentProcessResults": verified,
        "measurementRounds": verified * 3,
        "samples": summaries,
        "sourceFilesArchived": False,
        "adoptPermitted": False,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "limitations": "One hosted runner/3 timing rounds; synthetic 384MiB, no independent real versioned holdout, no concurrency or product budgets.",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    summary = validate(args.root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(summary, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print(f"G2-C1 {summary['verdict']}: {summary['independentProcessResults']} cases "
          f"/ {summary['measurementRounds']} measured rounds; byte and corrupt-CBLK oracles PASS")


if __name__ == "__main__":
    main()
