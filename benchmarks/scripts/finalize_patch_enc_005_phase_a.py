#!/usr/bin/env python3
"""Finalize PATCH-ENC-005 Phase-A platform evidence after at most two timing dispatches."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

import run_patch_enc_005_paired as paired


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_child(root: Path, relative: str) -> Path:
    if not isinstance(relative, str) or not relative:
        raise ValueError("timing evidence path escapes dispatch root: invalid relative path")
    normalized = relative.replace("\\", "/")
    if (
        normalized.startswith("/")
        or normalized.startswith("//")
        or (len(normalized) >= 3 and normalized[1] == ":" and normalized[2] == "/")
    ):
        raise ValueError(f"timing evidence path escapes dispatch root: {relative}")
    root = root.resolve()
    candidate = (root / normalized).resolve()
    if candidate != root and root not in candidate.parents:
        raise ValueError(f"timing evidence path escapes dispatch root: {relative}")
    return candidate


def timing_environment(dispatch_path: Path, document: dict, source_commit: str) -> dict:
    root = dispatch_path.parent
    environments: list[dict] = []
    rounds = document.get("rounds")
    if not isinstance(rounds, list) or len(rounds) != 5:
        raise ValueError("accepted dispatch must retain exactly five timing rounds")

    for round_item in rounds:
        paths = [
            round_item.get("h0StartResult"),
            round_item.get("h0EndResult"),
        ]
        candidates = round_item.get("candidates")
        if not isinstance(candidates, list):
            raise ValueError("accepted dispatch candidate result paths are missing")
        paths.extend(candidate.get("result") for candidate in candidates)
        for relative in paths:
            if not isinstance(relative, str) or not relative:
                raise ValueError("accepted dispatch contains an invalid timing result path")
            result = json.loads(safe_child(root, relative).read_text(encoding="utf-8"))
            environment = result.get("environment")
            if not isinstance(environment, dict):
                raise ValueError(f"{relative}: timing result has no environment snapshot")
            environments.append(environment)

    if not environments:
        raise ValueError("accepted dispatch contains no timing environments")
    canonical = environments[0]
    if any(environment != canonical for environment in environments[1:]):
        raise ValueError("timing environment changed inside one accepted dispatch")
    if str(canonical.get("gitCommit") or "").lower() != source_commit:
        raise ValueError("timing environment commit differs from bound source commit")
    if int(canonical.get("processorCount") or 0) < 2:
        raise ValueError("PATCH-ENC-005 timing requires at least two processors")
    for field in (
        "osDescription",
        "osArchitecture",
        "processArchitecture",
        "frameworkDescription",
        "processorDescription",
    ):
        if not str(canonical.get(field) or "").strip():
            raise ValueError(f"timing environment field {field} is empty")
    return canonical


def patch_map_from_document(
    document: dict,
    lanes: list[str] | None = None,
) -> dict[str, dict[tuple[str, str, str, str], str]]:
    projection = document.get("patchShas")
    if not isinstance(projection, dict):
        raise ValueError("accepted dispatch has no patchShas projection")

    lanes = list(paired.FROZEN_PHASE_A) if lanes is None else lanes
    expected_lanes = {"csp", *lanes}
    if set(projection) != expected_lanes:
        raise ValueError(
            "accepted dispatch lane set mismatch: "
            f"expected {sorted(expected_lanes)}, got {sorted(projection)}"
        )

    result: dict[str, dict[tuple[str, str, str, str], str]] = {}
    expected_files: set[tuple[str, str, str, str]] | None = None
    for lane, rows in projection.items():
        if not isinstance(rows, list):
            raise ValueError(f"{lane}: patch SHA projection must be a list")
        lane_map = {
            (row["family"], row["base"], row["target"], row["path"]): row["patchSha256"]
            for row in rows
        }
        if len(lane_map) != len(rows):
            raise ValueError(f"{lane}: duplicate file identity in patch SHA projection")
        files = set(lane_map)
        if expected_files is None:
            expected_files = files
        elif files != expected_files:
            raise ValueError(f"{lane}: patch SHA file set differs from the other lanes")
        result[lane] = lane_map

    if not expected_files:
        raise ValueError("accepted dispatch patch SHA projection is empty")
    return result


def validate_attempt(args: argparse.Namespace, document: dict, attempt: int) -> None:
    if document.get("schema") != "chunkshift.patch-enc-005-dispatch.v1":
        raise ValueError(f"attempt {attempt}: unexpected schema")
    if document.get("attempt") != attempt:
        raise ValueError(f"attempt {attempt}: ordinal mismatch")
    if not isinstance(document.get("valid"), bool):
        raise ValueError(f"attempt {attempt}: valid must be boolean")
    if document["valid"] is True and document.get("invalidReason") is not None:
        raise ValueError(f"attempt {attempt}: valid dispatch cannot have invalidReason")
    if document["valid"] is False and document.get("invalidReason") != "h0-bracket-noise":
        raise ValueError(f"attempt {attempt}: invalid dispatch must record h0-bracket-noise")
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


def validate_skip_marker(args: argparse.Namespace, document: dict) -> None:
    if document.get("schema") != "chunkshift.patch-enc-005-retry.v1":
        raise ValueError("attempt 2 skip marker has unexpected schema")
    if document.get("attempt") != 2 or document.get("skipped") is not True:
        raise ValueError("attempt 2 must be an explicit skip marker")
    if document.get("reason") != "attempt-1-valid":
        raise ValueError("attempt 2 skip marker has unexpected reason")
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
            raise ValueError(f"attempt 2 skip marker: {field} mismatch")


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
    parser.add_argument(
        "--mode",
        choices=("phase-a", "h6o-guard"),
        default="phase-a",
    )
    parser.add_argument("--lanes", default=",".join(paired.FROZEN_PHASE_A))
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
    lanes = [lane.strip() for lane in args.lanes.split(",") if lane.strip()]
    args.selected_lanes = lanes

    try:
        paired.validate_mode_lanes(args.mode, args.dataset_role, args.platform, lanes)
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
        validate_skip_marker(args, second)
        accepted = first
    else:
        validate_attempt(args, second, 2)
        if second.get("valid") is True:
            accepted = second

    args.output.mkdir(parents=True, exist_ok=False)
    apply_evidence = None
    trace_correctness = None
    accepted_patch_shas = None
    accepted_timing_environment = None
    if accepted is not None:
        accepted_path = args.attempt_one if accepted.get("attempt") == 1 else args.attempt_two
        accepted_timing_environment = timing_environment(
            accepted_path,
            accepted,
            args.source_commit,
        )
        lane_maps = patch_map_from_document(accepted, lanes)
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
                "dispatchSha256": sha256_file(args.attempt_one),
            },
            {
                "attempt": 2,
                "valid": second.get("valid"),
                "invalidReason": second.get("invalidReason"),
                "skipped": second.get("skipped", False),
                "dispatch": str(args.attempt_two),
                "dispatchSha256": sha256_file(args.attempt_two),
            },
        ],
        "acceptedAttempt": accepted.get("attempt") if accepted else None,
        "status": "VALID" if accepted else "INCOMPLETE",
        "acceptedTiming": None if accepted is None else {
            "attempt": accepted["attempt"],
            "rounds": accepted["rounds"],
            "summaries": accepted["summaries"],
        },
        "acceptedPatchShas": accepted_patch_shas,
        "timingEnvironment": accepted_timing_environment,
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
