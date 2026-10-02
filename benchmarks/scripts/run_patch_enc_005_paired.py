#!/usr/bin/env python3
"""Run PATCH-ENC-005 Phase-A development evidence without contaminating create timing.

The driver owns the frozen one-retry rule. Measured create rounds are trace-free
and apply-free. After one dispatch is accepted, apply, candidate-trace and
independent-decoder correctness evidence are collected in separate untimed
passes and are bound back to the accepted per-file patch SHA-256 map.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path

EXPERIMENT_ID = "PATCH-ENC-005"
FROZEN_PROTOCOL_COMMIT = "96fd9b296d6998cac397e61041f22df51e6dd43c"
FROZEN_DATASET_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
FROZEN_PHASE_A = (
    "H4-L1-R2",
    "H7-L1-R2-E75",
    "H9-L9-K4-C16-R1M",
    "H9-L12-K4-C16-R1M",
    "H9-L15-K4-C16-R1M",
)
FROZEN_DEVELOPMENT_FAMILIES = {
    "calibration": {
        "dotnet-aspnetcore-win-x64",
        "dotnet-runtime-linux-arm64",
    },
    "evaluation": {
        "node-win-x64",
        "node-linux-x64",
        "tzdata",
        "chunkshift-source",
    },
}
PLATFORMS = {"linux-x64", "linux-arm64", "win-x64"}
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
RUN_ID_RE = re.compile(
    r"^PATCH-ENC-005/RUN-(\d{8})-(\d{3})-([0-9a-f]{40})-"
    r"(linux-x64|linux-arm64|win-x64)$"
)


def rotated(lanes: list[str], round_index: int) -> list[str]:
    if not lanes:
        return []
    offset = round_index % len(lanes)
    return lanes[offset:] + lanes[:offset]


def bracket_mean(left: float, right: float) -> float:
    return (left + right) / 2.0


def bracket_is_noisy(left: float, right: float) -> bool:
    mean = bracket_mean(left, right)
    if mean <= 0:
        return True
    return abs(left - right) > 0.25 * mean


def median_five(values: list[float]) -> float:
    if len(values) != 5:
        raise ValueError("frozen paired timing requires exactly five measured rounds")
    return sorted(values)[2]


def validate_development_families(dataset_role: str, families: str | None) -> None:
    if dataset_role not in FROZEN_DEVELOPMENT_FAMILIES:
        raise ValueError("Phase-A development runner accepts only calibration or evaluation")
    expected = FROZEN_DEVELOPMENT_FAMILIES[dataset_role]
    actual = {item.strip() for item in (families or "").split(",") if item.strip()}
    if actual != expected:
        raise ValueError(
            f"{dataset_role}: expected frozen families {sorted(expected)}, got {sorted(actual)}"
        )


def validate_run_identity(run_id: str, source_commit: str, platform: str) -> None:
    if not COMMIT_RE.fullmatch(source_commit):
        raise ValueError("--source-commit must be a lowercase full 40-hex commit")
    if platform not in PLATFORMS:
        raise ValueError(f"unsupported platform {platform!r}")
    match = RUN_ID_RE.fullmatch(run_id)
    if match is None:
        raise ValueError(
            "--run-id must match PATCH-ENC-005/RUN-YYYYMMDD-NNN-<40hex>-<platform>"
        )
    date_text, _sequence, embedded_commit, embedded_platform = match.groups()
    dt.datetime.strptime(date_text, "%Y%m%d")
    if embedded_commit != source_commit or embedded_platform != platform:
        raise ValueError("--run-id commit/platform must match source/platform arguments")


def validate_ci_binding(source_commit: str, run_id: str) -> None:
    checked_out = os.environ.get("GITHUB_SHA")
    github_run_id = os.environ.get("GITHUB_RUN_ID")
    github_run_number = os.environ.get("GITHUB_RUN_NUMBER")
    github_run_attempt = os.environ.get("GITHUB_RUN_ATTEMPT")
    if not checked_out or not github_run_id or not github_run_number or not github_run_attempt:
        raise ValueError("decision evidence requires GitHub Actions run binding")
    if checked_out.lower() != source_commit:
        raise ValueError("--source-commit does not match GITHUB_SHA")
    if github_run_attempt != "1":
        raise ValueError("decision evidence rejects GitHub workflow re-run attempts")
    try:
        run_number = int(github_run_number)
    except ValueError as error:
        raise ValueError("GITHUB_RUN_NUMBER must be an integer") from error
    if run_number < 0 or run_number > 999:
        raise ValueError("PATCH-ENC-005 NNN binding requires GITHUB_RUN_NUMBER <= 999")
    match = RUN_ID_RE.fullmatch(run_id)
    if match is None or match.group(2) != f"{run_number:03d}":
        raise ValueError("RunId NNN must equal zero-padded GITHUB_RUN_NUMBER")


def patch_sha_map(result: dict) -> dict[tuple[str, str, str, str], str]:
    return {
        (item["family"], item["base"], item["target"], item["path"]): item["patchSha256"]
        for item in result["files"]
    }


def serialized_patch_sha_map(
    mapping: dict[tuple[str, str, str, str], str]
) -> list[dict[str, str]]:
    return [
        {
            "family": key[0],
            "base": key[1],
            "target": key[2],
            "path": key[3],
            "patchSha256": value,
        }
        for key, value in sorted(mapping.items())
    ]


def require_same_patch_bytes(label: str, left: dict, right: dict) -> None:
    if patch_sha_map(left) != patch_sha_map(right):
        raise ValueError(f"{label}: patch SHA-256 mismatch")


def aggregate_create(result: dict) -> dict[str, float | int]:
    files = result["files"]
    cpu = 0.0
    allocations = 0
    base_reads = 0
    base_bytes = 0
    for item in files:
        metrics = item.get("createMetrics")
        if metrics is None:
            raise ValueError("paired timing requires measured createMetrics")
        cpu += float(metrics["cpuSeconds"])
        allocations += int(metrics["allocatedBytes"])
        base_reads += int(metrics["baseReads"])
        base_bytes += int(metrics["baseBytesRead"])
    return {
        "wallSeconds": sum(float(item["createSeconds"]) for item in files),
        "cpuSeconds": cpu,
        "allocatedBytes": allocations,
        "baseReads": base_reads,
        "baseBytesRead": base_bytes,
        "patchBytes": sum(int(item["patchBytes"]) for item in files),
    }


def aggregate_apply(result: dict) -> dict[str, float | int]:
    wall = 0.0
    cpu = 0.0
    reads = 0
    bytes_read = 0
    for item in result["files"]:
        apply = item.get("applyMetrics")
        if apply is None:
            raise ValueError("apply evidence requires measured applyMetrics")
        samples = apply.get("samples")
        if not isinstance(samples, list) or len(samples) != 5:
            raise ValueError("apply evidence requires exactly five samples per file")
        wall += float(apply["medianWallSeconds"])
        cpu += float(apply["medianCpuSeconds"])
        reads += int(apply["medianBaseReads"])
        bytes_read += int(apply["medianBaseBytesRead"])
    return {
        "wallSeconds": wall,
        "cpuSeconds": cpu,
        "baseReads": reads,
        "baseBytesRead": bytes_read,
    }


def validate_result(
    args: argparse.Namespace,
    lane: str,
    result: dict,
    *,
    apply: bool,
) -> None:
    if result.get("schema") != "chunkshift.patch-lab.v1":
        raise ValueError(f"{lane}: unexpected PatchLab schema")
    if result.get("lane") != lane:
        raise ValueError(f"{lane}: result lane mismatch")
    if result.get("runId") != args.run_id:
        raise ValueError(f"{lane}: result RunId mismatch")
    if result.get("execution") != "h2-w2":
        raise ValueError(f"{lane}: result is not explicit H2-W2")
    if result.get("corpusPairsSha256") != FROZEN_DATASET_SHA256:
        raise ValueError(f"{lane}: development corpus lock mismatch")
    if result.get("applyCheck") != "boundary":
        raise ValueError(f"{lane}: apply evidence is not pinned to production boundary check")
    expected_repeats = 5 if apply else 0
    if int(result.get("applyRepeats", -1)) != expected_repeats:
        raise ValueError(f"{lane}: unexpected apply repeat count")
    environment = result.get("environment") or {}
    if str(environment.get("gitCommit") or "").lower() != args.source_commit:
        raise ValueError(f"{lane}: measured environment commit mismatch")
    if int(environment.get("processorCount") or 0) < 2:
        raise ValueError("PATCH-ENC-005 requires Environment.ProcessorCount >= 2")


def invoke(
    args: argparse.Namespace,
    lane: str,
    directory: Path,
    *,
    traces: bool = False,
    apply: bool = False,
    preserve_patches: bool = False,
) -> tuple[Path, dict]:
    directory.mkdir(parents=True, exist_ok=False)
    output = directory / "run.json"
    command = [
        args.dotnet,
        "run",
        "--project",
        str(args.project),
        "-c",
        "Release",
        "--no-build",
        "--",
        "patch-lab",
        "run",
        "--corpus",
        str(args.corpus),
        "--lane",
        lane,
        "--output",
        str(output),
        "--workers",
        "1",
        "--execution",
        "h2-w2",
        "--apply-check",
        "boundary",
        "--run-id",
        args.run_id,
    ]
    if apply:
        command += ["--apply-repeats", "5", "--apply-check-only"]
    else:
        command += ["--no-apply"]
    if args.families:
        command += ["--families", args.families]
    if args.work:
        command += ["--work", str(args.work)]
    if preserve_patches:
        command += ["--patch-dir", str(directory / "patches")]
    if traces:
        command += [
            "--trace-dir",
            str(directory / "traces"),
            "--experiment-id",
            EXPERIMENT_ID,
            "--protocol-commit",
            FROZEN_PROTOCOL_COMMIT,
            "--source-commit",
            args.source_commit,
            "--platform",
            args.platform,
            "--dataset-role",
            args.dataset_role,
        ]
    subprocess.run(command, check=True)
    result = json.loads(output.read_text(encoding="utf-8"))
    validate_result(args, lane, result, apply=apply)
    return output, result


def run_dispatch(
    args: argparse.Namespace,
    lanes: list[str],
    attempt: int,
) -> tuple[dict, dict[str, dict[tuple[str, str, str, str], str]]]:
    root = args.output
    root.mkdir(parents=True, exist_ok=False)

    _, warmup = invoke(args, "csp", root / "warmup-h0", apply=False)
    expected_patch_shas: dict[str, dict[tuple[str, str, str, str], str]] = {
        "csp": patch_sha_map(warmup)
    }
    rounds: list[dict] = []
    invalid = False

    for round_index in range(5):
        round_number = round_index + 1
        start_path, start = invoke(
            args, "csp", root / f"round-{round_number}-h0-start", apply=False
        )
        if patch_sha_map(start) != expected_patch_shas["csp"]:
            raise ValueError("H0 patch SHA-256 changed across measured rounds")
        candidates: list[dict] = []
        candidate_results: dict[str, dict] = {}

        for lane in rotated(lanes, round_index):
            path, result = invoke(
                args, lane, root / f"round-{round_number}-{lane}", apply=False
            )
            lane_shas = patch_sha_map(result)
            if lane in expected_patch_shas and expected_patch_shas[lane] != lane_shas:
                raise ValueError(f"{lane}: patch SHA-256 changed across measured rounds")
            expected_patch_shas.setdefault(lane, lane_shas)
            candidate_results[lane] = result
            candidates.append(
                {
                    "lane": lane,
                    "result": str(path.relative_to(args.output)),
                    "aggregate": aggregate_create(result),
                }
            )

        end_path, end = invoke(
            args, "csp", root / f"round-{round_number}-h0-end", apply=False
        )
        require_same_patch_bytes("H0 bracket", start, end)
        if patch_sha_map(end) != expected_patch_shas["csp"]:
            raise ValueError("H0 patch SHA-256 changed across measured rounds")
        if "H7-L1-R2-E75" in candidate_results:
            require_same_patch_bytes(
                "H7/H4 frozen byte oracle",
                candidate_results["H4-L1-R2"],
                candidate_results["H7-L1-R2-E75"],
            )

        start_aggregate = aggregate_create(start)
        end_aggregate = aggregate_create(end)
        noisy = bracket_is_noisy(
            float(start_aggregate["wallSeconds"]),
            float(end_aggregate["wallSeconds"]),
        )
        invalid |= noisy
        wall_denominator = bracket_mean(
            float(start_aggregate["wallSeconds"]),
            float(end_aggregate["wallSeconds"]),
        )
        cpu_denominator = bracket_mean(
            float(start_aggregate["cpuSeconds"]),
            float(end_aggregate["cpuSeconds"]),
        )
        if cpu_denominator <= 0:
            raise ValueError("paired timing requires positive H0 process CPU evidence")

        for candidate in candidates:
            aggregate_candidate = candidate["aggregate"]
            candidate["wallRatio"] = (
                float(aggregate_candidate["wallSeconds"]) / wall_denominator
            )
            candidate["cpuRatio"] = (
                float(aggregate_candidate["cpuSeconds"]) / cpu_denominator
            )

        rounds.append(
            {
                "round": round_number,
                "candidateOrder": [candidate["lane"] for candidate in candidates],
                "h0StartResult": str(start_path.relative_to(args.output)),
                "h0EndResult": str(end_path.relative_to(args.output)),
                "h0Start": start_aggregate,
                "h0End": end_aggregate,
                "bracketNoisy": noisy,
                "candidates": candidates,
            }
        )

    summaries = []
    for lane in lanes:
        lane_rows = [
            next(candidate for candidate in round_item["candidates"] if candidate["lane"] == lane)
            for round_item in rounds
        ]
        wall_ratios = [float(row["wallRatio"]) for row in lane_rows]
        cpu_ratios = [float(row["cpuRatio"]) for row in lane_rows]
        summaries.append(
            {
                "lane": lane,
                "wallRatios": wall_ratios,
                "cpuRatios": cpu_ratios,
                "wallRatioMedian": median_five(wall_ratios),
                "cpuRatioMedian": median_five(cpu_ratios),
            }
        )

    document = {
        "schema": "chunkshift.patch-enc-005-dispatch.v1",
        "experimentId": EXPERIMENT_ID,
        "runId": args.run_id,
        "attempt": attempt,
        "protocolCommit": FROZEN_PROTOCOL_COMMIT,
        "sourceCommit": args.source_commit,
        "platform": args.platform,
        "datasetRole": args.dataset_role,
        "datasetSha256": FROZEN_DATASET_SHA256,
        "githubRunId": os.environ["GITHUB_RUN_ID"],
        "githubRunNumber": os.environ["GITHUB_RUN_NUMBER"],
        "githubRunAttempt": os.environ["GITHUB_RUN_ATTEMPT"],
        "rounds": rounds,
        "summaries": summaries,
        "valid": not invalid,
        "invalidReason": "h0-bracket-noise" if invalid else None,
        "patchShas": {
            lane: serialized_patch_sha_map(mapping)
            for lane, mapping in sorted(expected_patch_shas.items())
        },
    }
    (root / "dispatch.json").write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return document, expected_patch_shas


def pair_index(corpus: Path) -> dict[tuple[str, str, str, str], dict]:
    document = json.loads((corpus / "pairs.json").read_text(encoding="utf-8"))
    result: dict[tuple[str, str, str, str], dict] = {}
    for pair in document["pairs"]:
        for item in pair["changed"]:
            result[(pair["family"], pair["base"], pair["target"], item["path"])] = item
    return result


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def collect_apply_evidence(
    args: argparse.Namespace,
    lane_maps: dict[str, dict[tuple[str, str, str, str], str]],
) -> dict[str, dict]:
    evidence: dict[str, dict] = {}
    root = args.output / "apply-evidence"
    for lane in ["csp", *[item for item in FROZEN_PHASE_A if item in lane_maps]]:
        path, result = invoke(args, lane, root / lane, apply=True)
        if patch_sha_map(result) != lane_maps[lane]:
            raise ValueError(f"{lane}: apply capture patch bytes differ from accepted timing")
        evidence[lane] = {
            "result": str(path.relative_to(args.output)),
            "aggregate": aggregate_apply(result),
        }
    return evidence


def decode_capture(
    args: argparse.Namespace,
    lane: str,
    result: dict,
    capture_dir: Path,
    pair_lookup: dict[tuple[str, str, str, str], dict],
) -> list[dict]:
    work = args.work if args.work else args.corpus / "work"
    rows: list[dict] = []
    for item in result["files"]:
        key = (item["family"], item["base"], item["target"], item["path"])
        pair_file = pair_lookup[key]
        saved = item.get("savedPatch")
        if not saved:
            raise ValueError(f"{lane}/{key}: correctness capture did not preserve patch")
        patch = capture_dir / "patches" / saved
        if sha256_file(patch) != item["patchSha256"]:
            raise ValueError(f"{lane}/{key}: preserved patch digest mismatch")
        base_manifest = work / "csm" / f'{pair_file["baseSha256"]}.csm'
        base_content = (
            args.corpus / "tree" / item["family"] / item["base"] / item["path"]
        )
        completed = subprocess.run(
            [
                sys.executable,
                str(args.decoder),
                str(patch),
                "--base-manifest",
                str(base_manifest),
                "--base",
                str(base_content),
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        verdict = json.loads(completed.stdout)
        if verdict.get("verdict") != "valid":
            raise ValueError(f"{lane}/{key}: independent decoder verdict is {verdict}")
        if verdict.get("outputSha256") != pair_file["targetSha256"]:
            raise ValueError(f"{lane}/{key}: independent decoder target SHA mismatch")
        rows.append(
            {
                "family": item["family"],
                "base": item["base"],
                "target": item["target"],
                "path": item["path"],
                "patchSha256": item["patchSha256"],
                "targetSha256": pair_file["targetSha256"],
                "decoderOutputSha256": verdict["outputSha256"],
                "verdict": "valid",
            }
        )
    return rows


def collect_trace_and_correctness(
    args: argparse.Namespace,
    lane_maps: dict[str, dict[tuple[str, str, str, str], str]],
) -> dict[str, dict]:
    root = args.output / "trace-correctness"
    lookup = pair_index(args.corpus)
    evidence: dict[str, dict] = {}
    for lane in ["csp", *[item for item in FROZEN_PHASE_A if item in lane_maps]]:
        capture_dir = root / lane
        path, result = invoke(
            args,
            lane,
            capture_dir,
            traces=True,
            apply=False,
            preserve_patches=True,
        )
        if patch_sha_map(result) != lane_maps[lane]:
            raise ValueError(f"{lane}: trace capture patch bytes differ from accepted timing")
        rows = decode_capture(args, lane, result, capture_dir, lookup)
        evidence[lane] = {
            "result": str(path.relative_to(args.output)),
            "traceDirectory": str((capture_dir / "traces").relative_to(args.output)),
            "correctness": rows,
        }
    return evidence


def validate_prior_dispatch(args: argparse.Namespace, document: dict) -> None:
    if document.get("schema") != "chunkshift.patch-enc-005-dispatch.v1":
        raise ValueError("retry predecessor has unexpected schema")
    if document.get("attempt") != 1:
        raise ValueError("retry predecessor must be attempt 1")
    for field, expected in (
        ("experimentId", EXPERIMENT_ID),
        ("runId", args.run_id),
        ("protocolCommit", FROZEN_PROTOCOL_COMMIT),
        ("sourceCommit", args.source_commit),
        ("platform", args.platform),
        ("datasetRole", args.dataset_role),
        ("datasetSha256", FROZEN_DATASET_SHA256),
        ("githubRunId", os.environ.get("GITHUB_RUN_ID")),
        ("githubRunNumber", os.environ.get("GITHUB_RUN_NUMBER")),
        ("githubRunAttempt", os.environ.get("GITHUB_RUN_ATTEMPT")),
    ):
        if document.get(field) != expected:
            raise ValueError(f"retry predecessor {field} mismatch")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--platform", choices=sorted(PLATFORMS), required=True)
    parser.add_argument(
        "--dataset-role",
        choices=("calibration", "evaluation"),
        required=True,
    )
    parser.add_argument("--families", required=True)
    parser.add_argument("--work", type=Path)
    parser.add_argument("--lanes", default=",".join(FROZEN_PHASE_A))
    parser.add_argument("--attempt", type=int, choices=(1, 2), required=True)
    parser.add_argument("--prior-dispatch", type=Path)
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
    unknown = [lane for lane in lanes if lane not in FROZEN_PHASE_A]
    if unknown or len(set(lanes)) != len(lanes) or not lanes:
        parser.error(
            "--lanes must be a non-empty unique subset of frozen Phase-A lanes; "
            f"invalid={unknown}"
        )
    if "H7-L1-R2-E75" in lanes and "H4-L1-R2" not in lanes:
        parser.error("H7-L1-R2-E75 requires H4-L1-R2 for the frozen byte oracle")

    try:
        validate_development_families(args.dataset_role, args.families)
        validate_run_identity(args.run_id, args.source_commit, args.platform)
        validate_ci_binding(args.source_commit, args.run_id)
    except ValueError as error:
        parser.error(str(error))

    if args.attempt == 1:
        if args.prior_dispatch is not None:
            parser.error("attempt 1 must not specify --prior-dispatch")
        document, _ = run_dispatch(args, lanes, 1)
        return 0

    if args.prior_dispatch is None:
        parser.error("attempt 2 requires --prior-dispatch")
    prior = json.loads(args.prior_dispatch.read_text(encoding="utf-8"))
    try:
        validate_prior_dispatch(args, prior)
    except ValueError as error:
        parser.error(str(error))

    if prior.get("valid") is True:
        args.output.mkdir(parents=True, exist_ok=False)
        marker = {
            "schema": "chunkshift.patch-enc-005-retry.v1",
            "experimentId": EXPERIMENT_ID,
            "runId": args.run_id,
            "attempt": 2,
            "protocolCommit": FROZEN_PROTOCOL_COMMIT,
            "sourceCommit": args.source_commit,
            "platform": args.platform,
            "datasetRole": args.dataset_role,
            "datasetSha256": FROZEN_DATASET_SHA256,
            "githubRunId": os.environ["GITHUB_RUN_ID"],
            "githubRunNumber": os.environ["GITHUB_RUN_NUMBER"],
            "githubRunAttempt": os.environ["GITHUB_RUN_ATTEMPT"],
            "skipped": True,
            "reason": "attempt-1-valid",
        }
        (args.output / "dispatch.json").write_text(
            json.dumps(marker, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        return 0

    run_dispatch(args, lanes, 2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
