#!/usr/bin/env python3
"""Validation and durable compact projection of PATCH-ENC-005 candidate traces."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


TRACE_SCHEMA = "chunkshift.patch-candidate-trace.v1"
VALID_SOURCES = {"offset", "sketch", "both"}
VALID_ENCODINGS = {"raw", "zstd", "zstd-dictionary"}
EXPECTED_FINAL_LEVEL = {
    "csp": 19,
    "H4-L1-R2": 19,
    "H7-L1-R2-E75": 19,
    "H9-L9-K4-C16-R1M": 9,
    "H9-L12-K4-C16-R1M": 12,
    "H9-L15-K4-C16-R1M": 15,
}


def canonical_bytes(value):
    return json.dumps(
        value, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")


def safe_child(root: Path, relative: str) -> Path:
    if not isinstance(relative, str) or not relative:
        raise ValueError("trace path escapes evidence root: invalid relative path")
    normalized = relative.replace("\\", "/")
    if (
        normalized.startswith("/")
        or normalized.startswith("//")
        or (len(normalized) >= 3 and normalized[1] == ":" and normalized[2] == "/")
    ):
        raise ValueError(f"trace path escapes evidence root: {relative}")
    root = root.resolve()
    candidate = (root / normalized).resolve()
    if candidate != root and root not in candidate.parents:
        raise ValueError(f"trace path escapes evidence root: {relative}")
    return candidate


def non_negative(value, label):
    result = int(value)
    if result < 0:
        raise ValueError(f"{label} must be non-negative")
    return result


def optional_non_negative(value, label):
    if value is None:
        return None
    return non_negative(value, label)


def _candidate_projection(candidate: dict, label: str) -> dict:
    ordinal = non_negative(candidate.get("ordinal", -1), f"{label}/ordinal")
    start_index = non_negative(
        candidate.get("startIndex", -1), f"{label}/startIndex"
    )
    start_offset = non_negative(
        candidate.get("startOffset", -1), f"{label}/startOffset"
    )
    record_count = non_negative(
        candidate.get("recordCount", -1), f"{label}/recordCount"
    )
    if record_count == 0:
        raise ValueError(f"{label}/recordCount must be positive")

    source = str(candidate.get("source") or "")
    if source not in VALID_SOURCES:
        raise ValueError(f"{label}/source must be offset, sketch or both")
    selected = candidate.get("selected")
    if not isinstance(selected, bool):
        raise ValueError(f"{label}/selected must be boolean")

    first_chunk_id = candidate.get("firstChunkId")
    if not isinstance(first_chunk_id, str) or not first_chunk_id:
        raise ValueError(f"{label}/firstChunkId is missing")

    return {
        "ordinal": ordinal,
        "startIndex": start_index,
        "startOffset": start_offset,
        "recordCount": record_count,
        "firstChunkId": first_chunk_id,
        "source": source,
        "cheapLevel": optional_non_negative(
            candidate.get("cheapLevel"), f"{label}/cheapLevel"
        ),
        "cheapFrameBytes": optional_non_negative(
            candidate.get("cheapFrameBytes"), f"{label}/cheapFrameBytes"
        ),
        "cheapCostBytes": optional_non_negative(
            candidate.get("cheapCostBytes"), f"{label}/cheapCostBytes"
        ),
        "finalFrameBytes": optional_non_negative(
            candidate.get("finalFrameBytes"), f"{label}/finalFrameBytes"
        ),
        "finalCostBytes": optional_non_negative(
            candidate.get("finalCostBytes"), f"{label}/finalCostBytes"
        ),
        "l19FrameBytes": optional_non_negative(
            candidate.get("l19FrameBytes"), f"{label}/l19FrameBytes"
        ),
        "l19CostBytes": optional_non_negative(
            candidate.get("l19CostBytes"), f"{label}/l19CostBytes"
        ),
        "selected": selected,
    }


def _projection(document: dict, lane: str) -> dict:
    entries = document.get("entries")
    if not isinstance(entries, list):
        raise ValueError("candidate trace entries must be a list")

    final_level = non_negative(document.get("finalLevel", -1), "finalLevel")
    expected_level = EXPECTED_FINAL_LEVEL.get(lane)
    if expected_level is None or final_level != expected_level:
        raise ValueError(
            f"{lane}: candidate trace finalLevel {final_level} != {expected_level}"
        )

    totals = {
        "entryCount": 0,
        "candidateCount": 0,
        "cheapTrialCount": 0,
        "expensiveTrialCount": 0,
        "totalCompressionTrialCount": 0,
        "level19TrialCount": 0,
        "storedBytes": 0,
        "dictionaryRefs": 0,
        "sourceCounts": {"offset": 0, "sketch": 0, "both": 0},
        "selectedSourceCounts": {"offset": 0, "sketch": 0, "both": 0},
    }
    compact_entries = []
    previous_target_index = -1

    for entry_index, entry in enumerate(entries):
        label = f"entry[{entry_index}]"
        target_index = non_negative(
            entry.get("targetIndex", -1), f"{label}/targetIndex"
        )
        if target_index <= previous_target_index:
            raise ValueError(
                "candidate trace targetIndex is not in production first-occurrence order"
            )
        previous_target_index = target_index

        target_chunk_id = entry.get("targetChunkId")
        if not isinstance(target_chunk_id, str) or not target_chunk_id:
            raise ValueError(f"{label}/targetChunkId is missing")

        target_offset = non_negative(
            entry.get("targetOffset", -1), f"{label}/targetOffset"
        )
        target_length = non_negative(
            entry.get("targetLength", -1), f"{label}/targetLength"
        )
        if target_length == 0:
            raise ValueError(f"{label}/targetLength must be positive")

        candidates = entry.get("candidates")
        if not isinstance(candidates, list):
            raise ValueError(f"{label}/candidates must be a list")

        candidate_count = non_negative(
            entry.get("candidateCount", -1), f"{label}/candidateCount"
        )
        cheap_trials = non_negative(
            entry.get("cheapTrialCount", -1), f"{label}/cheapTrialCount"
        )
        expensive_trials = non_negative(
            entry.get("expensiveTrialCount", -1), f"{label}/expensiveTrialCount"
        )
        total_trials = non_negative(
            entry.get("totalCompressionTrialCount", -1),
            f"{label}/totalCompressionTrialCount",
        )
        level19_trials = non_negative(
            entry.get("level19TrialCount", -1), f"{label}/level19TrialCount"
        )
        if candidate_count != len(candidates):
            raise ValueError(
                f"{label}/candidateCount does not match candidate rows"
            )
        if total_trials != 1 + cheap_trials + expensive_trials:
            raise ValueError(
                f"{label}/totalCompressionTrialCount does not recompute"
            )
        expected_l19 = 1 + expensive_trials if final_level == 19 else 0
        if level19_trials != expected_l19:
            raise ValueError(f"{label}/level19TrialCount does not recompute")

        compact_candidates = []
        seen_ordinals = set()
        selected_ordinals = []
        for candidate_index, candidate in enumerate(candidates):
            projected = _candidate_projection(
                candidate, f"{label}/candidate[{candidate_index}]"
            )
            ordinal = projected["ordinal"]
            if ordinal in seen_ordinals:
                raise ValueError(f"{label}: duplicate candidate ordinal {ordinal}")
            seen_ordinals.add(ordinal)
            source = projected["source"]
            totals["sourceCounts"][source] += 1
            if projected["selected"]:
                selected_ordinals.append(ordinal)
                totals["selectedSourceCounts"][source] += 1
            compact_candidates.append(projected)

        selected_candidate = entry.get("selectedCandidate")
        if selected_candidate is None:
            if selected_ordinals:
                raise ValueError(
                    f"{label}: selected row exists without selectedCandidate"
                )
        else:
            selected_candidate = non_negative(
                selected_candidate, f"{label}/selectedCandidate"
            )
            if selected_ordinals != [selected_candidate]:
                raise ValueError(
                    f"{label}: selectedCandidate does not match selected row"
                )

        selected_encoding = str(entry.get("selectedEncoding") or "")
        if selected_encoding not in VALID_ENCODINGS:
            raise ValueError(f"{label}/selectedEncoding is invalid")
        if selected_encoding == "zstd-dictionary":
            if selected_candidate is None:
                raise ValueError(
                    f"{label}: dictionary encoding requires selectedCandidate"
                )
        elif selected_candidate is not None:
            raise ValueError(
                f"{label}: non-dictionary encoding cannot select a candidate"
            )

        stored_bytes = non_negative(
            entry.get("storedBytes", -1), f"{label}/storedBytes"
        )
        dictionary_refs = non_negative(
            entry.get("dictionaryRefs", -1), f"{label}/dictionaryRefs"
        )
        if selected_encoding == "zstd-dictionary":
            if dictionary_refs == 0:
                raise ValueError(
                    f"{label}: dictionary encoding requires dictionaryRefs"
                )
        elif dictionary_refs != 0:
            raise ValueError(
                f"{label}: non-dictionary encoding must have zero dictionaryRefs"
            )

        compact_entry = {
            "targetIndex": target_index,
            "targetChunkId": target_chunk_id,
            "targetOffset": target_offset,
            "targetLength": target_length,
            "candidateCount": candidate_count,
            "cheapTrialCount": cheap_trials,
            "expensiveTrialCount": expensive_trials,
            "totalCompressionTrialCount": total_trials,
            "level19TrialCount": level19_trials,
            "noDictionaryFrameBytes": non_negative(
                entry.get("noDictionaryFrameBytes", -1),
                f"{label}/noDictionaryFrameBytes",
            ),
            "l19NoDictionaryFrameBytes": optional_non_negative(
                entry.get("l19NoDictionaryFrameBytes"),
                f"{label}/l19NoDictionaryFrameBytes",
            ),
            "baselineCostBytes": non_negative(
                entry.get("baselineCostBytes", -1),
                f"{label}/baselineCostBytes",
            ),
            "selectedEncoding": selected_encoding,
            "selectedCandidate": selected_candidate,
            "storedBytes": stored_bytes,
            "dictionaryRefs": dictionary_refs,
            "candidates": compact_candidates,
        }

        if final_level == 19:
            if (
                compact_entry["l19NoDictionaryFrameBytes"]
                != compact_entry["noDictionaryFrameBytes"]
            ):
                raise ValueError(
                    f"{label}/l19NoDictionaryFrameBytes does not match final L19 frame"
                )
        elif compact_entry["l19NoDictionaryFrameBytes"] is not None:
            raise ValueError(
                f"{label}/l19NoDictionaryFrameBytes must be null for non-L19 lane"
            )

        totals["entryCount"] += 1
        totals["candidateCount"] += candidate_count
        totals["cheapTrialCount"] += cheap_trials
        totals["expensiveTrialCount"] += expensive_trials
        totals["totalCompressionTrialCount"] += total_trials
        totals["level19TrialCount"] += level19_trials
        totals["storedBytes"] += stored_bytes
        totals["dictionaryRefs"] += dictionary_refs
        compact_entries.append(compact_entry)

    projection = {
        "baseManifestId": document.get("baseManifestId"),
        "targetManifestId": document.get("targetManifestId"),
        "finalLevel": final_level,
        "totals": totals,
        "entries": compact_entries,
    }
    if not projection["baseManifestId"] or not projection["targetManifestId"]:
        raise ValueError("candidate trace manifest identities are missing")
    projection["semanticSha256"] = hashlib.sha256(
        canonical_bytes(projection)
    ).hexdigest()
    return projection


def load(
    evidence_root: Path,
    lane_record: dict,
    *,
    experiment_id: str,
    run_id: str,
    protocol_commit: str,
    source_commit: str,
    platform: str,
    lane: str,
    dataset_role: str,
    dataset_sha256: str,
) -> dict[tuple[str, str, str, str], dict]:
    relative = lane_record.get("traceDirectory")
    if not isinstance(relative, str) or not relative:
        raise ValueError(f"{lane}: traceDirectory is missing")
    directory = safe_child(evidence_root, relative)
    if not directory.is_dir():
        raise ValueError(f"{lane}: traceDirectory does not exist")

    result = {}
    for path in sorted(directory.glob("*.json")):
        raw = path.read_bytes()
        document = json.loads(raw)
        if not isinstance(document, dict):
            raise ValueError(f"{path}: candidate trace must be a JSON object")
        expected = {
            "schema": TRACE_SCHEMA,
            "experimentId": experiment_id,
            "runId": run_id,
            "protocolCommit": protocol_commit,
            "sourceCommit": source_commit,
            "platform": platform,
            "lane": lane,
            "datasetRole": dataset_role,
            "datasetSha256": dataset_sha256,
        }
        for field, value in expected.items():
            if document.get(field) != value:
                raise ValueError(f"{path}: trace {field} mismatch")

        key = (
            document["family"],
            document["baseVersion"],
            document["targetVersion"],
            document["path"],
        )
        if key in result:
            raise ValueError(f"{lane}: duplicate trace identity {key}")

        projection = _projection(document, lane)
        projection["documentSha256"] = hashlib.sha256(raw).hexdigest()
        result[key] = projection

    if not result:
        raise ValueError(f"{lane}: traceDirectory is empty")
    return result
