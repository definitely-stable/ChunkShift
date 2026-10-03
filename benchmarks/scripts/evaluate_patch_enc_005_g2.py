#!/usr/bin/env python3
"""Validate PATCH-ENC-005 G2 oracle evidence and evaluate the frozen §4.3 gate."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import defaultdict
from pathlib import Path

SAMPLE_SCHEMA = "chunkshift.patch-g2-sample.v1"
ORACLE_SCHEMA = "chunkshift.patch-g2-oracle.v1"
VERDICT_SCHEMA = "chunkshift.patch-enc-005-g2-verdict.v1"
LOCK_SCHEMA = "chunkshift.patch-enc-005-g2-sample-lock.v1"
EXPERIMENT_ID = "PATCH-ENC-005"
PROTOCOL_COMMIT = "96fd9b296d6998cac397e61041f22df51e6dd43c"
DATASET_ROLE = "calibration"
DATASET_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
POLICY = "L19-K4-ALL-PREFIX-H20C20-REF32"
CANDIDATE_ORDER = "abs-offset-then-lower-index"
PAIR_KEYS = (
    ("dotnet-aspnetcore-win-x64", "10.0.10", "10.0.11"),
    ("dotnet-aspnetcore-win-x64", "10.0.11", "10.0.12"),
    ("dotnet-runtime-linux-arm64", "10.0.10", "10.0.11"),
    ("dotnet-runtime-linux-arm64", "10.0.11", "10.0.12"),
)
IDENTITY_FIELDS = (
    "family",
    "baseVersion",
    "targetVersion",
    "path",
    "targetIndex",
    "targetChunkId",
    "targetOffset",
    "targetLength",
)
ENCODINGS = {"raw", "zstd", "zstd-dictionary"}


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError(f"{path}: expected a JSON object")
    return value


def is_lower_hex(value: object, length: int) -> bool:
    return (
        isinstance(value, str)
        and len(value) == length
        and all(ch in "0123456789abcdef" for ch in value)
    )


def require_identity(document: dict, schema: str, label: str) -> None:
    expected = {
        "schema": schema,
        "experimentId": EXPERIMENT_ID,
        "protocolCommit": PROTOCOL_COMMIT,
        "datasetRole": DATASET_ROLE,
        "datasetSha256": DATASET_SHA256,
    }
    for key, value in expected.items():
        if document.get(key) != value:
            raise ValueError(
                f"{label}: {key} mismatch: {document.get(key)!r} != {value!r}"
            )


def sample_key_sha256(row: dict) -> str:
    target_index = row.get("targetIndex")
    if not isinstance(target_index, int) or target_index < 0:
        raise ValueError("sample row targetIndex must be a non-negative integer")
    chunk_id = row.get("targetChunkId")
    if not is_lower_hex(chunk_id, 64):
        raise ValueError("sample row targetChunkId must be lowercase 64-hex")
    fields = (
        row.get("family"),
        row.get("baseVersion"),
        row.get("targetVersion"),
        row.get("path"),
        chunk_id,
        str(target_index),
    )
    if any(not isinstance(value, str) for value in fields[:-1]):
        raise ValueError("sample row identity strings are malformed")
    return hashlib.sha256(b"\x00".join(value.encode("utf-8") for value in fields)).hexdigest()


def canonical_rows_sha256(rows: list[dict]) -> str:
    payload = json.dumps(
        rows,
        ensure_ascii=False,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def validate_lock(sample_path: Path, lock_path: Path) -> tuple[dict, dict]:
    raw = sample_path.read_bytes()
    sample = json.loads(raw)
    lock = load_json(lock_path)

    require_identity(sample, SAMPLE_SCHEMA, "sample")
    require_identity(lock, LOCK_SCHEMA, "lock")

    if not is_lower_hex(sample.get("sourceCommit"), 40):
        raise ValueError("sample: sourceCommit must be lowercase full 40-hex")
    if lock.get("sourceCommit") != sample["sourceCommit"]:
        raise ValueError("lock/sample sourceCommit mismatch")

    raw_sha = hashlib.sha256(raw).hexdigest()
    if raw_sha != lock.get("sampleSha256"):
        raise ValueError(
            f"sample file SHA-256 mismatch: {raw_sha} != {lock.get('sampleSha256')}"
        )

    rows = sample.get("rows")
    if not isinstance(rows, list) or len(rows) != 256:
        raise ValueError("sample must contain exactly 256 rows")

    pair_rows: dict[tuple[str, str, str], list[dict]] = defaultdict(list)
    block_order: list[tuple[str, str, str]] = []
    previous = None

    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("sample row must be an object")
        for field in IDENTITY_FIELDS:
            if field not in row:
                raise ValueError(f"sample row is missing {field}")
        if not isinstance(row["targetOffset"], int) or row["targetOffset"] < 0:
            raise ValueError("sample row targetOffset must be a non-negative integer")
        if not isinstance(row["targetLength"], int) or row["targetLength"] <= 0:
            raise ValueError("sample row targetLength must be a positive integer")
        if row.get("sampleKeySha256") != sample_key_sha256(row):
            raise ValueError("sampleKeySha256 mismatch")
        pair = (row["family"], row["baseVersion"], row["targetVersion"])
        if pair != previous:
            block_order.append(pair)
            previous = pair
        pair_rows[pair].append(row)

    if tuple(block_order) != PAIR_KEYS:
        raise ValueError(f"sample pair-block order mismatch: {block_order!r}")
    if set(pair_rows) != set(PAIR_KEYS):
        raise ValueError("sample pair membership mismatch")

    for pair in PAIR_KEYS:
        group = pair_rows[pair]
        if len(group) != 64:
            raise ValueError(f"{pair}: expected 64 rows, got {len(group)}")
        if len({row["targetChunkId"] for row in group}) != 64:
            raise ValueError(f"{pair}: targetChunkId is not unique")
        ordered = sorted(
            group,
            key=lambda row: (
                row["sampleKeySha256"],
                row["path"],
                row["targetIndex"],
            ),
        )
        if group != ordered:
            raise ValueError(f"{pair}: digest/path/index ordering mismatch")

    rows_hash = canonical_rows_sha256(rows)
    if sample.get("oracleSampleSha256") != rows_hash:
        raise ValueError("sample oracleSampleSha256 does not recompute")
    if lock.get("oracleSampleSha256") != rows_hash:
        raise ValueError("lock oracleSampleSha256 does not match sample")
    if lock.get("rowCount") != 256:
        raise ValueError("lock rowCount mismatch")

    return sample, lock


def validate_choice(row: dict, prefix: str, target_length: int) -> None:
    encoding = row.get(prefix + "Encoding")
    stored = row.get(prefix + "StoredBytes")
    refs = row.get(prefix + "DictionaryRefs")
    cost = row.get(prefix + "CostBytes")
    start_index = row.get(prefix + "StartIndex")
    start_offset = row.get(prefix + "StartOffset")
    record_count = row.get(prefix + "RecordCount")
    first_chunk = row.get(prefix + "FirstChunkId")

    if encoding not in ENCODINGS:
        raise ValueError(f"{prefix}: unknown encoding {encoding!r}")
    if not isinstance(stored, int) or stored < 0:
        raise ValueError(f"{prefix}: stored bytes must be non-negative")
    if not isinstance(refs, int) or refs < 0 or refs > 4:
        raise ValueError(f"{prefix}: dictionary refs must be in [0,4]")
    if not isinstance(cost, int) or cost != stored + (32 * refs):
        raise ValueError(f"{prefix}: REF32 cost accounting mismatch")

    if encoding == "raw":
        if stored != target_length:
            raise ValueError(f"{prefix}: raw stored bytes must equal target length")
        if refs != 0:
            raise ValueError(f"{prefix}: raw encoding cannot name dictionary refs")
    elif encoding == "zstd":
        if refs != 0:
            raise ValueError(f"{prefix}: no-dictionary zstd cannot name refs")
        if cost >= target_length:
            raise ValueError(f"{prefix}: zstd winner must strictly beat raw")
    else:
        if refs < 1:
            raise ValueError(f"{prefix}: dictionary encoding requires refs")
        if cost >= target_length:
            raise ValueError(f"{prefix}: dictionary winner must strictly beat raw")

    if encoding == "zstd-dictionary":
        if (
            not isinstance(start_index, int)
            or start_index < 0
            or not isinstance(start_offset, int)
            or start_offset < 0
            or not isinstance(record_count, int)
            or record_count != refs
            or not is_lower_hex(first_chunk, 64)
        ):
            raise ValueError(f"{prefix}: malformed dictionary winner metadata")
    elif any(value is not None for value in (start_index, start_offset, record_count, first_chunk)):
        raise ValueError(f"{prefix}: non-dictionary encoding has dictionary metadata")


def validate_oracle(sample: dict, oracle: dict) -> None:
    require_identity(oracle, ORACLE_SCHEMA, "oracle")
    if not is_lower_hex(oracle.get("sourceCommit"), 40):
        raise ValueError("oracle: sourceCommit must be lowercase full 40-hex")
    if not isinstance(oracle.get("runId"), str) or not oracle["runId"].strip():
        raise ValueError("oracle: runId is missing")
    run_match = re.fullmatch(
        r"PATCH-ENC-005/RUN-[0-9]{8}-[0-9]{3}-([0-9a-f]{40})-linux-x64",
        oracle["runId"],
    )
    if run_match is None:
        raise ValueError("oracle: runId does not match the frozen format")
    if run_match.group(1) != oracle["sourceCommit"]:
        raise ValueError("oracle: runId source commit does not match sourceCommit")
    if oracle.get("oracleSampleSha256") != sample.get("oracleSampleSha256"):
        raise ValueError("oracle/sample oracleSampleSha256 mismatch")
    if oracle.get("policy") != POLICY:
        raise ValueError("oracle policy mismatch")
    if oracle.get("candidateOrder") != CANDIDATE_ORDER:
        raise ValueError("oracle candidate order mismatch")

    sample_rows = sample["rows"]
    rows = oracle.get("rows")
    if not isinstance(rows, list) or len(rows) != len(sample_rows):
        raise ValueError("oracle row population mismatch")

    for ordinal, (expected, row) in enumerate(zip(sample_rows, rows, strict=True)):
        if not isinstance(row, dict):
            raise ValueError(f"oracle row {ordinal}: expected object")
        for field in IDENTITY_FIELDS:
            if row.get(field) != expected.get(field):
                raise ValueError(f"oracle row {ordinal}: {field} does not match locked sample")

        target_length = expected["targetLength"]
        validate_choice(row, "h0", target_length)
        validate_choice(row, "oracle", target_length)

        starts = row.get("candidateStartsEnumerated")
        valid = row.get("validCandidateCount")
        if (
            not isinstance(starts, int)
            or starts < 0
            or not isinstance(valid, int)
            or valid < 0
            or valid > starts
        ):
            raise ValueError(f"oracle row {ordinal}: candidate counts are invalid")

        h0 = row["h0CostBytes"]
        g2 = row["oracleCostBytes"]
        saved = row.get("savedBytes")
        if g2 > h0:
            raise ValueError(f"oracle row {ordinal}: oracle cost exceeds H0")
        if saved != h0 - g2:
            raise ValueError(f"oracle row {ordinal}: savedBytes mismatch")

        distance = row.get("oracleStartDistanceBytes")
        if row["oracleEncoding"] == "zstd-dictionary":
            if not isinstance(distance, int) or distance < 0:
                raise ValueError(f"oracle row {ordinal}: dictionary distance is invalid")
            if distance != abs(row["oracleStartOffset"] - row["targetOffset"]):
                raise ValueError(f"oracle row {ordinal}: dictionary distance mismatch")
            if row["oracleStartIndex"] >= starts:
                raise ValueError(f"oracle row {ordinal}: oracle start index is outside the base")
            if valid == 0:
                raise ValueError(f"oracle row {ordinal}: dictionary winner requires a valid candidate")
        elif distance is not None:
            raise ValueError(f"oracle row {ordinal}: non-dictionary winner has distance")

        if row["h0Encoding"] == "zstd-dictionary":
            if row["h0StartIndex"] >= starts:
                raise ValueError(f"oracle row {ordinal}: H0 start index is outside the base")
            if valid == 0:
                raise ValueError(f"oracle row {ordinal}: H0 dictionary winner requires a valid candidate")


def evaluate(sample: dict, oracle: dict) -> dict:
    validate_oracle(sample, oracle)

    pair_totals = {
        pair: {
            "h0CostBytes": 0,
            "oracleCostBytes": 0,
            "savedBytes": 0,
            "improvedRows": 0,
            "rows": 0,
            "outsideRadiusWinners": 0,
        }
        for pair in PAIR_KEYS
    }

    total_h0 = 0
    total_oracle = 0
    total_candidates = 0
    valid_candidates = 0
    improved_rows = 0
    outside_radius = 0

    for row in oracle["rows"]:
        pair = (row["family"], row["baseVersion"], row["targetVersion"])
        totals = pair_totals[pair]
        h0 = row["h0CostBytes"]
        g2 = row["oracleCostBytes"]
        saved = h0 - g2
        outside = (
            row["oracleEncoding"] == "zstd-dictionary"
            and row["oracleStartDistanceBytes"] > 256 * 1024
        )

        total_h0 += h0
        total_oracle += g2
        total_candidates += row["candidateStartsEnumerated"]
        valid_candidates += row["validCandidateCount"]
        improved_rows += int(saved > 0)
        outside_radius += int(outside)

        totals["h0CostBytes"] += h0
        totals["oracleCostBytes"] += g2
        totals["savedBytes"] += saved
        totals["improvedRows"] += int(saved > 0)
        totals["outsideRadiusWinners"] += int(outside)
        totals["rows"] += 1

    overall_pass = total_oracle * 100 <= total_h0 * 95
    pair_results = []
    pair_pass_count = 0

    for pair in PAIR_KEYS:
        totals = pair_totals[pair]
        passed = totals["oracleCostBytes"] * 100 <= totals["h0CostBytes"] * 97
        pair_pass_count += int(passed)
        pair_results.append(
            {
                "family": pair[0],
                "baseVersion": pair[1],
                "targetVersion": pair[2],
                **totals,
                "ratio": totals["oracleCostBytes"] / totals["h0CostBytes"],
                "passesThreePercentGate": passed,
            }
        )

    passed = overall_pass and pair_pass_count >= 2
    return {
        "schema": VERDICT_SCHEMA,
        "experimentId": EXPERIMENT_ID,
        "runId": oracle["runId"],
        "protocolCommit": PROTOCOL_COMMIT,
        "sourceCommit": oracle["sourceCommit"],
        "datasetRole": DATASET_ROLE,
        "datasetSha256": DATASET_SHA256,
        "oracleSampleSha256": sample["oracleSampleSha256"],
        "policy": POLICY,
        "candidateOrder": CANDIDATE_ORDER,
        "status": "PASS" if passed else "MISS",
        "nextAction": "FULL_PHASE_B" if passed else "H6_O_GUARD_ONLY",
        "h0SampleCostBytes": total_h0,
        "g2SampleCostBytes": total_oracle,
        "savedBytes": total_h0 - total_oracle,
        "sampleRatio": total_oracle / total_h0,
        "overallFivePercentGate": overall_pass,
        "pairPassCount": pair_pass_count,
        "requiredPairPassCount": 2,
        "rowCount": len(oracle["rows"]),
        "improvedRows": improved_rows,
        "candidateStartsEnumerated": total_candidates,
        "validCandidateCount": valid_candidates,
        "outsideRadiusWinners": outside_radius,
        "pairs": pair_results,
    }


def write_summary(path: Path, verdict: dict) -> None:
    lines = [
        "# PATCH-ENC-005 G2 verdict",
        "",
        f"- run: `{verdict['runId']}`",
        f"- source: `{verdict['sourceCommit']}`",
        f"- sample: `{verdict['oracleSampleSha256']}`",
        f"- status: **{verdict['status']}**",
        f"- next action: **{verdict['nextAction']}**",
        f"- H0 sample cost: {verdict['h0SampleCostBytes']:,}",
        f"- G2 sample cost: {verdict['g2SampleCostBytes']:,}",
        f"- ratio: {verdict['sampleRatio']:.6f}",
        f"- saved bytes: {verdict['savedBytes']:,}",
        f"- improved rows: {verdict['improvedRows']} / {verdict['rowCount']}",
        f"- pair passes: {verdict['pairPassCount']} / 4 (need >= 2)",
        "",
        "| family | base | target | H0 | G2 | ratio | saved | pass <=0.97 |",
        "| --- | --- | --- | ---: | ---: | ---: | ---: | --- |",
    ]
    for pair in verdict["pairs"]:
        lines.append(
            "| {family} | {baseVersion} | {targetVersion} | {h0CostBytes:,} | "
            "{oracleCostBytes:,} | {ratio:.6f} | {savedBytes:,} | {passed} |".format(
                **pair,
                passed="yes" if pair["passesThreePercentGate"] else "no",
            )
        )
    lines.extend(
        [
            "",
            "The main G2 gate passes only when the overall ratio is <=0.95 and at "
            "least two of four pair ratios are <=0.97.",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--sample", type=Path, required=True)
    parser.add_argument("--lock", type=Path, required=True)
    parser.add_argument("--oracle", type=Path)
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--validate-lock-only", action="store_true")
    args = parser.parse_args()

    try:
        sample, _ = validate_lock(args.sample, args.lock)
        if args.validate_lock_only:
            print(sample["oracleSampleSha256"])
            return 0
        if args.oracle is None or args.output_dir is None:
            parser.error("--oracle and --output-dir are required unless --validate-lock-only is used")
        oracle = load_json(args.oracle)
        verdict = evaluate(sample, oracle)
        args.output_dir.mkdir(parents=True, exist_ok=True)
        (args.output_dir / "verdict.json").write_text(
            json.dumps(verdict, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        write_summary(args.output_dir / "summary.md", verdict)
        print(json.dumps(verdict, sort_keys=True, separators=(",", ":")))
        return 0
    except (OSError, ValueError, json.JSONDecodeError) as error:
        parser.error(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
