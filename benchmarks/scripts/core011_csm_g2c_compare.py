#!/usr/bin/env python3
"""Fail-closed G2-C experimental result contract: no ADOPT verdict."""
import argparse
import hashlib
import json
from pathlib import Path

VALID_VARIANTS = {"baseline", "stage64", "stage256"}
OPERATIONS = {"create", "manifest-verify", "enumerate"}


def validate(root: Path) -> dict:
    receipts = [json.loads((root / f"{v}.receipt.json").read_text())
                for v in sorted(VALID_VARIANTS)]
    if len({r["checkoutSha"] for r in receipts}) != 1 or len({
        r["pristineSha256"] for r in receipts}) != 1 or any(
        r["pristineBlob"] != "894c61b68bfba111467f061e8a0caf81bc8eea81"
        or r["releaseDecision"] != "NO_VERDICT_EXPLORATORY" for r in receipts):
        raise ValueError("Source variants do not share one frozen Core parent")
    if {r["variant"] for r in receipts} != VALID_VARIANTS:
        raise ValueError("Missing source variant")
    if {r["batchEntries"] for r in receipts} != {0, 64, 256}:
        raise ValueError("Wrong staged widths")

    summaries = []
    sources = {}
    files = sorted(root.glob("case-*/samples/*.json"))
    cases = sorted({f.parent.parent.name for f in files})
    if len(cases) != 2 or len(files) != 24:
        raise ValueError(f"Expected exactly 2 cases × 3 ops × 4 independent processes, found {len(files)}")

    for case in cases:
        entries = sorted((root / case / "samples").glob("*.json"))
        seen = set()
        for path in entries:
            data = json.loads(path.read_text())
            if (data.get("schema") != "chunkshift.core011-csm-g2c-sample.v1"
                    or data.get("stage") != "G2-C-NONDECISION"
                    or data.get("verdict") != "NO_VERDICT_EXPLORATORY"
                    or data.get("variant") not in VALID_VARIANTS
                    or data.get("operation") not in OPERATIONS):
                raise ValueError(f"Invalid sample record: {path}")
            key = (data["operation"], data["variant"],
                   path.stem.endswith("-repeat"))
            if key in seen:
                raise ValueError(f"Duplicate record: {path}")
            seen.add(key)
            if data["variant"] != "baseline" and key[2]:
                raise ValueError("Only baseline may be repeated")
            if not data["samples"] or len(data["samples"]) != data["rounds"]:
                raise ValueError("Missing measured rounds")
            if data["rounds"] != 2 or data["repetitions"] != 3:
                raise ValueError("Changed preregistered round count")
            for sample in data["samples"]:
                if (sample["completed"] != 3 or sample["allocatedBytes"] < 0
                        or sample["wallMilliseconds"] <= 0
                        or sample["workingSetBytes"] <= 0):
                    raise ValueError("Invalid measurement")
            id_fields = {k: data[k] for k in (
                "fixtureManifestSha256", "fixtureSourceSha256", "suite",
                "hasBlockIndex", "contentMib", "count", "manifestId", "fileDigest")}
            if case not in sources:
                sources[case] = id_fields
            elif sources[case] != id_fields:
                raise ValueError("Variants did not verify the SAME immutable CSM fixture")
            summaries.append({
                "case": case, "operation": data["operation"],
                "variant": data["variant"], "repeatBaseline": key[2],
                "medianWallMsPerCall": sorted(
                    s["wallMilliseconds"] / s["completed"] for s in data["samples"]
                )[len(data["samples"]) // 2],
                "allocatedBytesPerCall": sum(
                    s["allocatedBytes"] for s in data["samples"]
                ) / (3 * len(data["samples"])),
            })

        expected = {(op, variant, False) for op in OPERATIONS for variant in VALID_VARIANTS}
        expected |= {(op, "baseline", True) for op in OPERATIONS}
        if seen != expected:
            raise ValueError(f"Missing variant / operation cases: {expected-seen}")

    return {
        "schema": "chunkshift.core011-csm-g2c-qualification-guard.v1",
        "experimentId": "CORE011-CSM-002",
        "stage": "nondecision_calibration",
        "checkoutSha": receipts[0]["checkoutSha"],
        "pristineSha256": receipts[0]["pristineSha256"],
        "datasetKinds": ["deterministic-synthetic-file"],
        "caseCount": len(cases),
        "verifiedOperationResults": len(files),
        "sourceReceipts": receipts,
        "fixtureIdentities": sources,
        "measurements": summaries,
        "adoptPermitted": False,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "limitations": "One GitHub runner, synthetic 1/8MiB, short run; no cross-host confidence, independent holdout or real multi-CBLK production decision.",
    }


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--root", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    result = validate(args.root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n")
    print(f"G2-C {result['verdict']}: exact fixture checks {result['verifiedOperationResults']}")


if __name__ == "__main__":
    main()
