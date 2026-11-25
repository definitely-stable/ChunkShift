"""Fail-closed validation of successful ingestion sample records."""
import math
import re

SIGNATURE = ("layout_sha256", "logical_bytes", "logical_occurrences", "unique_frames",
             "unique_payload_bytes", "pack_bytes", "pack_count")
TIMINGS = ("wall_ms_ready", "cpu_ms_ready", "restore_ms", "rebuild_ms", "cleanup_ms")
COUNTS = ("logical_bytes", "logical_occurrences", "unique_frames", "unique_payload_bytes",
          "pack_bytes", "pack_count", "allocated_bytes_delta_ready",
          "process_peak_working_set_bytes_at_ready", "stage_files_completed",
          "modeled_stage_payload_bytes", "modeled_request_copy_bytes",
          "active_duplicate_count", "closed_duplicate_count", "modeled_duplicate_comparison_bytes",
          "processors")


def validate(result, candidate, suite, family, source_hash, source_bytes, target_mib):
    if not isinstance(result, dict):
        raise ValueError("sample must be a JSON object")
    if (result["verdict"] != "pass" or result["restore_sha256"] != source_hash or
            result["source_sha256"] != source_hash or result["candidate"] != candidate or
            result["suite"] != suite or result["target_mib"] != target_mib):
        raise ValueError("sample identity/verdict/digest mismatch")
    if not re.fullmatch(r"[0-9a-f]{64}", result["layout_sha256"]):
        raise ValueError("invalid layout fingerprint")
    for key in TIMINGS:
        value = result[key]
        if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
            raise ValueError(f"invalid timing: {key}")
    if result["wall_ms_ready"] <= 0:
        raise ValueError("nonpositive Ready interval")
    for key in COUNTS:
        value = result[key]
        if type(value) is not int or value < 0:
            raise ValueError(f"invalid count: {key}")
    for key in ("runtime", "os", "arch"):
        if not isinstance(result[key], str) or not result[key].strip():
            raise ValueError(f"missing environment: {key}")
    n = result["logical_occurrences"]
    u = result["unique_frames"]
    logical = result["logical_bytes"]
    payload = result["unique_payload_bytes"]
    packs = result["pack_count"]
    active = result["active_duplicate_count"]
    closed = result["closed_duplicate_count"]
    if (logical != source_bytes or not 0 < u <= n or not 0 < payload <= logical or
            not 0 < packs <= u or result["processors"] < 1 or
            result["pack_bytes"] != payload + 68 * u + 168 * packs):
        raise ValueError("inconsistent geometry or counts")
    if family == "unique":
        if n != u or payload != logical:
            raise ValueError("unique corpus contains duplicate chunks")
    elif n == u or payload == logical:
        raise ValueError("duplicate corpus did not produce CDC reuse")
    if candidate in ("A", "D"):
        if (result["stage_files_completed"] != n or
                result["modeled_stage_payload_bytes"] != logical or
                result["modeled_request_copy_bytes"] != (logical if candidate == "A" else 0)):
            raise ValueError("staging accounting mismatch")
    elif any(result[k] != 0 for k in ("stage_files_completed",
                                      "modeled_stage_payload_bytes", "modeled_request_copy_bytes")):
        raise ValueError("unexpected staging in direct path")
    range_bytes = result.get("range_source_read_bytes", 0)
    if type(range_bytes) is not int or range_bytes != (logical if candidate == "D" else 0):
        raise ValueError("range source read accounting mismatch")
    if candidate == "C":
        if active + closed != n - u or result["modeled_duplicate_comparison_bytes"] != logical - payload:
            raise ValueError("duplicate accounting mismatch")
        if family == "recent" and active == 0:
            raise ValueError("recent corpus did not exercise active reuse")
        if family == "cross" and closed == 0:
            raise ValueError("cross corpus did not exercise closed reuse")
    elif active or closed or result["modeled_duplicate_comparison_bytes"]:
        raise ValueError("unexpected direct reuse accounting")
    return tuple(result[key] for key in SIGNATURE)
