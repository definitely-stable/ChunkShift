#!/usr/bin/env python3
"""Finalize PATCH-ENC-005 Phase-A platform evidence after at most two timing dispatches."""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

import run_patch_enc_005_paired as paired


def patch_map_from_document(document: dict) -> dict[str, dict[tuple[str, str, str, str], str]]:
    result: dict[str, dict[tuple[str, str, str, str], str]] = {}
    for lane, rows in document["patchShas"].items():
        result[lane] = {
            (row["family"], row["base"], row["target"], row["path"]): row["patchSha256"]
            for row in rows
        }
    return result


def validate_attempt(args: argparse.Namespace, document: dict, attempt: int) -> None:
    if document.get("schema") != "chunkshift.patch-enc-005-dispatch.v1":
        raise ValueError(f"attempt {attempt}: unexpected schema")
    if document.get("attempt") != attempt:
        raise ValueError(f"attempt {attempt}: ordinal mismatch")
    for field, expected in (
        ("experimentId", paired.EXPERIMENT_ID),
        ("runId", args.run_id),
        ("protocolCommit", paired.FROZEN_PROTOCOL_COMMIT),
        ("sourceCommit", args.source_commit),
        ("platform", args.platform),
        ("datasetRole", args.dataset_role),
        ("datasetSha256", paired.FROZEN_DATASET_SHA256),
        ("githubRunId", os.environ.get("GITHUB_RUN_ID")),
        ("githubRunNumber", os.environ.get("GITHUB_RUN_NUMBER")),
        ("githubRunAttempt", os.environ.get("GITHUB_RUN_ATTEMPT")),
    ):
        if document.get(field) != expected:
            raise ValueError(f"attempt {attempt}: {field} mismatch")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--attempt-one", type=Path, required=True)
    parser.add_argument("--attempt-two", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--platform", choices=sorted(paired.PLATFORMS), required=True)
    parser.add_argument(
        "--dataset-role",
        choices=("calibration", "evaluation"),
        required=True,
    )
    parser.add_argument("--families", required=True)
    parser.add_argument("--work", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument(
        "--project",
        type=Path,
        default=Path("benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"),
    )
    parser.add_argument(
        "--decoder",
        type=Path,
        default=Path("tools/csp-fixtures/decode.py"),
    )
    args = parser.parse_args()
    args.source_commit = args.source_commit.lower()

    try:
        paired.validate_development_families(args.dataset_role, args.families)
        paired.validate_run_identity(args.run_id, args.source_commit, args.platform)
        paired.validate_ci_binding(args.source_commit, args.run_id)
    except ValueError as error:
        parser.error(str(error))

    first = json.loads(args.attempt_one.read_text(encoding="utf-8"))
    second = json.loads(args.attempt_two.read_text(encoding="utf-8"))
    validate_attempt(args, first, 1)

    accepted = None
    if first.get("valid") is True:
        if second.get("schema") != "chunkshift.patch-enc-005-retry.v1" or second.get("skipped") is not True:
            raise ValueError("attempt 2 must be an explicit skip marker when attempt 1 is valid")
        accepted = first
    else:
        validate_attempt(args, second, 2)
        if second.get("valid") is True:
            accepted = second

    args.output.mkdir(parents=True, exist_ok=False)
    apply_evidence = None
    trace_correctness = None
    accepted_patch_shas = None
    if accepted is not None:
        lane_maps = patch_map_from_document(accepted)
        accepted_patch_shas = {
            lane: paired.serialized_patch_sha_map(mapping)
            for lane, mapping in sorted(lane_maps.items())
        }
        apply_evidence = paired.collect_apply_evidence(args, lane_maps)
        trace_correctness = paired.collect_trace_and_correctness(args, lane_maps)

    document = {
        "schema": "chunkshift.patch-enc-005-paired.v2",
        "experimentId": paired.EXPERIMENT_ID,
        "runId": args.run_id,
        "protocolCommit": paired.FROZEN_PROTOCOL_COMMIT,
        "sourceCommit": args.source_commit,
        "platform": args.platform,
        "datasetRole": args.dataset_role,
        "datasetSha256": paired.FROZEN_DATASET_SHA256,
        "githubRunId": os.environ.get("GITHUB_RUN_ID"),
        "githubRunNumber": os.environ.get("GITHUB_RUN_NUMBER"),
        "githubRunAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        "attempts": [
            {
                "attempt": 1,
                "valid": first.get("valid"),
                "invalidReason": first.get("invalidReason"),
                "dispatch": str(args.attempt_one),
            },
            {
                "attempt": 2,
                "valid": second.get("valid"),
                "invalidReason": second.get("invalidReason"),
                "skipped": second.get("skipped", False),
                "dispatch": str(args.attempt_two),
            },
        ],
        "acceptedAttempt": accepted.get("attempt") if accepted else None,
        "status": "VALID" if accepted else "INCOMPLETE",
        "acceptedPatchShas": accepted_patch_shas,
        "applyEvidence": apply_evidence,
        "traceCorrectness": trace_correctness,
    }
    (args.output / "paired.json").write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return 0 if accepted is not None else 1


if __name__ == "__main__":
    sys.exit(main())
