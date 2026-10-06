#!/usr/bin/env python3
"""Fail-closed independent verifier for PATCH-GAP-001 G3 byte evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Any

EXPECTED_SCHEMA = "chunkshift.patch-gap-g3-byte-study.v1"
FILE_SCHEMA = "chunkshift.patch-gap-g3-file.v1"
EXPERIMENT = "PATCH-GAP-001"
POLICY = "G3-RUN+FILE-L19-H20C20-W20-ANCHOR-H0-FIRST"
LANES = ("G3-RUN", "G3-FILE")
UINT32_MAX = (1 << 32) - 1


def load(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise SystemExit(f"{path}: expected JSON object")
    return value


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def verify(args: argparse.Namespace) -> dict[str, Any]:
    index_path = args.index.resolve()
    detail_dir = args.detail_dir.resolve()
    root = load(index_path)

    checks = {
        "schema": root.get("schema") == EXPECTED_SCHEMA,
        "experiment": root.get("experimentId") == EXPERIMENT,
        "protocol": root.get("protocolCommit") == args.protocol_commit,
        "source": root.get("sourceCommit") == args.source_commit,
        "role": root.get("datasetRole") == args.dataset_role,
        "dataset": root.get("datasetSha256") == args.pairs_sha,
        "policy": root.get("researchPolicy") == POLICY,
    }
    failed = [name for name, ok in checks.items() if not ok]
    require(not failed, "G3 index identity mismatch: " + ", ".join(failed))

    files = root.get("files")
    lanes = root.get("lanes")
    require(isinstance(files, list) and files, "G3 study has no file rows")
    require(
        isinstance(lanes, list)
        and [item.get("lane") for item in lanes] == list(LANES),
        "G3 lane order/coverage differs from frozen RUN/FILE set",
    )

    h0 = sum(int(row["h0PatchBytes"]) for row in files)
    require(
        h0 == args.expected_h0,
        f"G3 H0 {args.dataset_role} bytes {h0} != frozen {args.expected_h0}",
    )

    factor_totals = {lane: 0 for lane in LANES}
    extra_reads = {lane: 0 for lane in LANES}
    extra_read_calls = {lane: 0 for lane in LANES}
    extra_seeks = {lane: 0 for lane in LANES}
    group_counts = {lane: 0 for lane in LANES}
    member_counts = {lane: 0 for lane in LANES}
    group_target_bytes = {lane: 0 for lane in LANES}
    h0_reads = 0
    referenced: set[str] = set()

    for row in files:
        detail_path = detail_dir / str(row["detailPath"])
        require(detail_path.is_file(), f"missing G3 detail document: {detail_path.name}")
        raw = detail_path.read_bytes()
        require(
            hashlib.sha256(raw).hexdigest() == row["detailSha256"],
            f"G3 detail digest mismatch: {detail_path.name}",
        )
        require(
            len(raw) == int(row["detailBytes"]),
            f"G3 detail size mismatch: {detail_path.name}",
        )
        require(
            detail_path.name not in referenced,
            f"duplicate G3 detail path: {detail_path.name}",
        )
        referenced.add(detail_path.name)

        detail = json.loads(raw)
        require(
            isinstance(detail, dict) and detail.get("schema") == FILE_SCHEMA,
            f"G3 file schema mismatch: {detail_path.name}",
        )
        require(
            int(detail["h0PatchBytes"]) == int(row["h0PatchBytes"]),
            f"G3 file H0 bytes disagree with compact row: {detail_path.name}",
        )
        require(
            detail["family"] == row["family"]
            and detail["baseVersion"] == row["baseVersion"]
            and detail["targetVersion"] == row["targetVersion"]
            and detail["path"] == row["path"]
            and detail["baseSha256"] == row["baseSha256"]
            and detail["targetSha256"] == row["targetSha256"]
            and int(detail["targetBytes"]) == int(row["targetBytes"]),
            f"G3 compact/detail identity mismatch: {detail_path.name}",
        )

        h0_reads += int(detail["h0BaseBytesRead"])
        detail_lanes_list = detail.get("lanes")
        require(
            isinstance(detail_lanes_list, list)
            and [item.get("lane") for item in detail_lanes_list] == list(LANES),
            f"G3 file lane order differs from frozen order: {detail_path.name}",
        )
        detail_lanes = {item["lane"]: item for item in detail_lanes_list}

        for lane in LANES:
            item = detail_lanes[lane]
            require(
                item.get("reconstructionPass") is True,
                f"G3 reconstruction did not pass: {detail_path.name}/{lane}",
            )
            require(
                int(item["h0PatchBytes"]) == int(row["h0PatchBytes"]),
                f"G3 lane H0 mismatch: {detail_path.name}/{lane}",
            )

            groups = item.get("groups")
            require(isinstance(groups, list), f"G3 groups missing: {detail_path.name}/{lane}")
            removed = 0
            added = 0
            members = 0
            target_bytes = 0
            previous_group = -1

            for group in groups:
                gid = int(group["groupId"])
                require(
                    gid > previous_group,
                    f"G3 group ids not strictly increasing: {detail_path.name}/{lane}",
                )
                previous_group = gid

                indexes = [int(value) for value in group["firstTargetIndexes"]]
                ids = list(group["chunkIds"])
                lengths = [int(value) for value in group["targetLengths"]]
                h0_stored = [int(value) for value in group["h0StoredBytes"]]
                h0_refs = [int(value) for value in group["h0DictionaryReferences"]]
                count = len(indexes)
                require(
                    count >= 2
                    and len(ids) == len(lengths) == len(h0_stored) == len(h0_refs) == count,
                    f"G3 group member arrays malformed: {detail_path.name}/{lane}/{gid}",
                )
                require(
                    indexes == sorted(indexes) and len(set(indexes)) == count,
                    f"G3 group target indexes malformed: {detail_path.name}/{lane}/{gid}",
                )
                require(
                    all(length > 0 for length in lengths)
                    and all(stored > 0 for stored in h0_stored)
                    and all(ref >= 0 for ref in h0_refs),
                    f"G3 group member cost fields invalid: {detail_path.name}/{lane}/{gid}",
                )

                h0_variable = sum(
                    stored + 32 * refs for stored, refs in zip(h0_stored, h0_refs, strict=True)
                )
                require(
                    h0_variable == int(group["h0VariableBytes"]),
                    f"G3 H0 variable-byte equation mismatch: {detail_path.name}/{lane}/{gid}",
                )

                anchor_refs = int(group["anchorDictionaryReferences"])
                anchor_ids = list(group["anchorDictionaryChunkIds"])
                require(
                    anchor_refs == len(anchor_ids) and anchor_refs == h0_refs[0],
                    f"G3 anchor dictionary mismatch: {detail_path.name}/{lane}/{gid}",
                )
                require(
                    sum(lengths) == int(group["groupTargetBytes"]),
                    f"G3 group target-byte sum mismatch: {detail_path.name}/{lane}/{gid}",
                )

                frame = int(group["groupFrameBytes"])
                require(
                    0 < frame <= UINT32_MAX,
                    f"G3 group frame length invalid: {detail_path.name}/{lane}/{gid}",
                )
                frame_sha = str(group["groupFrameSha256"])
                require(
                    len(frame_sha) == 64
                    and all(character in "0123456789abcdef" for character in frame_sha),
                    f"G3 group frame digest malformed: {detail_path.name}/{lane}/{gid}",
                )

                removed += h0_variable
                added += frame + 32 * anchor_refs
                members += count
                target_bytes += sum(lengths)

            expected_factor = int(row["h0PatchBytes"]) - removed + added
            require(
                expected_factor == int(item["factorPatchBytes"]),
                f"G3 file physical-byte equation mismatch: {detail_path.name}/{lane}",
            )
            require(
                expected_factor == int(row["lanePatchBytes"][lane]),
                f"G3 compact/file lane bytes disagree: {detail_path.name}/{lane}",
            )
            require(
                int(item["savedBytes"]) == int(row["h0PatchBytes"]) - expected_factor,
                f"G3 file savedBytes mismatch: {detail_path.name}/{lane}",
            )
            require(
                int(item["coalescedGroupCount"]) == len(groups)
                and int(item["coalescedMemberCount"]) == members
                and int(item["coalescedTargetBytes"]) == target_bytes,
                f"G3 file grouping counters mismatch: {detail_path.name}/{lane}",
            )

            row_groups = int(row["laneCoalescedGroups"][lane])
            row_members = int(row["laneCoalescedMembers"][lane])
            row_target = int(row["laneCoalescedTargetBytes"][lane])
            require(
                row_groups == len(groups)
                and row_members == members
                and row_target == target_bytes,
                f"G3 compact grouping counters mismatch: {detail_path.name}/{lane}",
            )

            detail_reads = int(item["groupDictionaryBaseBytesRead"])
            require(
                detail_reads == int(row["laneGroupDictionaryBaseBytesRead"][lane]),
                f"G3 compact base-read bytes mismatch: {detail_path.name}/{lane}",
            )
            require(
                int(item["groupDictionaryReadCalls"]) >= 0
                and int(item["groupDictionarySeeks"]) >= 0,
                f"G3 negative read counters: {detail_path.name}/{lane}",
            )

            factor_totals[lane] += expected_factor
            extra_reads[lane] += detail_reads
            extra_read_calls[lane] += int(item["groupDictionaryReadCalls"])
            extra_seeks[lane] += int(item["groupDictionarySeeks"])
            group_counts[lane] += len(groups)
            member_counts[lane] += members
            group_target_bytes[lane] += target_bytes

    actual = {path.name for path in detail_dir.glob("*.json")}
    require(
        actual == referenced,
        "G3 detail directory contains stale or unreferenced evidence",
    )

    for aggregate in lanes:
        lane = aggregate["lane"]
        factor = factor_totals[lane]
        require(
            int(aggregate["h0Bytes"]) == h0 and int(aggregate["factorBytes"]) == factor,
            f"G3 aggregate byte totals mismatch: {lane}",
        )

        saved = h0 - factor
        reduction = saved / h0
        require(
            int(aggregate["savedBytes"]) == saved,
            f"G3 aggregate savedBytes mismatch: {lane}",
        )
        require(
            math.isclose(
                float(aggregate["reductionVsCsp"]),
                reduction,
                rel_tol=0.0,
                abs_tol=1e-15,
            ),
            f"G3 aggregate reduction mismatch: {lane}",
        )
        require(
            bool(aggregate["meetsRfcSizeGate"]) == (20 * saved >= 3 * h0),
            f"G3 RFC size gate mismatch: {lane}",
        )

        factor_reads = h0_reads + extra_reads[lane]
        require(
            int(aggregate["h0BaseBytesRead"]) == h0_reads,
            f"G3 H0 base-read total mismatch: {lane}",
        )
        require(
            int(aggregate["factorBaseBytesRead"]) == factor_reads,
            f"G3 factor base-read total mismatch: {lane}",
        )
        expected_amp = None if h0_reads == 0 else factor_reads / h0_reads
        if expected_amp is None:
            require(
                aggregate.get("baseReadAmplification") is None,
                f"G3 unexpected base-read amplification: {lane}",
            )
        else:
            require(
                math.isclose(
                    float(aggregate["baseReadAmplification"]),
                    expected_amp,
                    rel_tol=0.0,
                    abs_tol=1e-12,
                ),
                f"G3 base-read amplification mismatch: {lane}",
            )

        require(
            int(aggregate["coalescedGroups"]) == group_counts[lane]
            and int(aggregate["coalescedMembers"]) == member_counts[lane]
            and int(aggregate["coalescedTargetBytes"]) == group_target_bytes[lane],
            f"G3 aggregate grouping counters mismatch: {lane}",
        )

    return {
        "schema": EXPECTED_SCHEMA,
        "datasetRole": args.dataset_role,
        "files": len(files),
        "h0Bytes": h0,
        "lanes": lanes,
        "extraReadCalls": extra_read_calls,
        "extraSeeks": extra_seeks,
    }


def write_summary(path: Path, result: dict[str, Any], source_commit: str, run_id: str) -> None:
    lines = [
        f"### PATCH-GAP-001 G3 {result['datasetRole']}",
        "",
        f"- RunId: `{run_id}`",
        f"- sourceCommit: `{source_commit}`",
        f"- files: {result['files']}",
        f"- H0 bytes: {result['h0Bytes']:,}",
        "",
    ]
    for lane in result["lanes"]:
        lines.append(
            f"- {lane['lane']}: {int(lane['factorBytes']):,} B; "
            f"saved={int(lane['savedBytes']):,} B; "
            f"reduction={float(lane['reductionVsCsp']):.6%}; "
            f"size-gate={lane['meetsRfcSizeGate']}; "
            f"base-read-amplification={lane.get('baseReadAmplification')}; "
            f"groups={int(lane['coalescedGroups']):,}"
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", type=Path, required=True)
    parser.add_argument("--detail-dir", type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--protocol-commit", required=True)
    parser.add_argument("--pairs-sha", required=True)
    parser.add_argument("--dataset-role", choices=("calibration", "evaluation"), required=True)
    parser.add_argument("--expected-h0", type=int, required=True)
    parser.add_argument("--summary", type=Path, required=True)
    args = parser.parse_args()

    result = verify(args)
    root = load(args.index)
    write_summary(
        args.summary,
        result,
        args.source_commit,
        str(root["provenance"]["runId"]),
    )
    print(
        f"PATCH-GAP-001 G3 {args.dataset_role} independent recomputation: PASS"
    )


if __name__ == "__main__":
    main()
