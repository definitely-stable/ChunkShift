#!/usr/bin/env python3
"""Compact validation/projection of PATCH-ENC-005 candidate trace documents."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


TRACE_SCHEMA = "chunkshift.patch-candidate-trace.v1"


def canonical_bytes(value):
    return json.dumps(
        value, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")


def safe_child(root: Path, relative: str) -> Path:
    root = root.resolve()
    candidate = (root / relative).resolve()
    if candidate != root and root not in candidate.parents:
        raise ValueError(f"trace path escapes evidence root: {relative}")
    return candidate


def _summary(document: dict) -> dict:
    entries = document.get("entries")
    if not isinstance(entries, list):
        raise ValueError("candidate trace entries must be a list")

    source_counts: dict[str, int] = {}
    selected_source_counts: dict[str, int] = {}
    values = {
        "entryCount": len(entries),
        "candidateCount": 0,
        "cheapTrialCount": 0,
        "expensiveTrialCount": 0,
        "totalCompressionTrialCount": 0,
        "level19TrialCount": 0,
        "storedBytes": 0,
        "dictionaryRefs": 0,
    }

    for entry in entries:
        candidates = entry.get("candidates")
        if not isinstance(candidates, list):
            raise ValueError("candidate trace row has no candidate list")
        if int(entry.get("candidateCount", -1)) != len(candidates):
            raise ValueError("candidate trace candidateCount does not match rows")

        for name in (
            "candidateCount",
            "cheapTrialCount",
            "expensiveTrialCount",
            "totalCompressionTrialCount",
            "level19TrialCount",
            "storedBytes",
            "dictionaryRefs",
        ):
            value = int(entry.get(name, -1))
            if value < 0:
                raise ValueError(f"candidate trace {name} must be non-negative")
            values[name] += value

        selected = 0
        for candidate in candidates:
            source = str(candidate.get("source") or "")
            if not source:
                raise ValueError("candidate trace candidate source is empty")
            source_counts[source] = source_counts.get(source, 0) + 1
            if candidate.get("selected") is True:
                selected += 1
                selected_source_counts[source] = selected_source_counts.get(source, 0) + 1
        selected_ordinal = entry.get("selectedCandidate")
        if selected_ordinal is None:
            if selected != 0:
                raise ValueError("candidate trace marks a selected row without selectedCandidate")
        elif selected != 1:
            raise ValueError("candidate trace selectedCandidate requires exactly one selected row")

    values["candidateSourceCounts"] = dict(sorted(source_counts.items()))
    values["selectedSourceCounts"] = dict(sorted(selected_source_counts.items()))
    values["semanticSha256"] = hashlib.sha256(canonical_bytes(values)).hexdigest()
    return values


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
        summary = _summary(document)
        summary["documentSha256"] = hashlib.sha256(raw).hexdigest()
        result[key] = summary

    if not result:
        raise ValueError(f"{lane}: traceDirectory is empty")
    return result
