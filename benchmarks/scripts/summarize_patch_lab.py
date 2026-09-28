#!/usr/bin/env python3
"""Summarize the Patching pre-freeze patch lab.

Reads one directory per platform holding the JSON files of the patch lab
(`chunkshift.patch-lab.v1`, `chunkshift.patch-lab-memory.v1`) and of the
reference lanes (`chunkshift.patch-refs.v1`), verifies the corpus lock and the
recorded commit, and applies the frozen decision rules of
docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md:

  PATCH-PREFREEZE-001  R1/R2 against the raw CSP lane
  PATCH-ENC-002        sweep winner on the x64 Linux lane
  PATCH-APPLY-001      A1 re-chunk overhead, A2 peak memory

Aggregates are sums over changed files; bytes are printed in MiB and ratios in
percent, both with two decimals. A rule whose inputs are missing reports
"not evaluated", never a pass.

Usage:
  summarize_patch_lab.py PLATFORM_DIR [PLATFORM_DIR ...]
      --corpus-lock docs/benchmarks/patch-corpus/corpus-lock.json
      --output verdict.json --markdown summary.md
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

LAB_SCHEMA = "chunkshift.patch-lab.v1"
MEMORY_SCHEMA = "chunkshift.patch-lab-memory.v1"
REFS_SCHEMA = "chunkshift.patch-refs.v1"
SUMMARY_SCHEMA = "chunkshift.patch-lab-summary.v1"

PREFREEZE = "PATCH-PREFREEZE-001"
ENC = "PATCH-ENC-002"
APPLY = "PATCH-APPLY-001"

DEFAULT_SWEEP_LANE = "sweep-L19-K2-C8"
SWEEP_LANES = tuple(
    f"sweep-L{level}-K{chunks}-C{candidates}"
    for level in (9, 19)
    for chunks in (1, 2, 4)
    for candidates in (8, 16)
)
REFERENCE_LANES = ("full", "full-zstd", "zstd-patch-from", "xdelta3", "bsdiff")

# PATCH-PREFREEZE-001 (CSP section 10.2, thresholds frozen in the protocol note).
R1_MAX_RATIO = 0.75
R2_BANDWIDTH_BPS = 1_000_000_000
BANDWIDTH_LOW_BPS = 50_000_000
BANDWIDTH_HIGH_BPS = 1_000_000_000

# PATCH-ENC-002 (D15).
CREATE_TIME_FACTOR = 1.5
MINIMUM_SAVING = 0.02

# PATCH-APPLY-001 (D13, D17).
APPLY_OVERHEAD_MAX = 0.25
MEMORY_LIMIT_BYTES = 64 * 1024 * 1024

MIB = 1024 * 1024
BYTE_FIELDS = (
    "targetSize",
    "uniqueMissingBytes",
    "patchBytes",
    "tcsmBytes",
    "payloadEntries",
    "rawEntries",
    "zstdEntries",
    "dictionaryEntries",
    "storedPayloadBytes",
    "dictionaryReferences",
    "targetChunks",
)
TIME_FIELDS = ("createSeconds", "applySeconds", "applyNoCheckSeconds")

NOT_EVALUATED = "not evaluated"


class SummaryError(Exception):
    """the evidence cannot be summarized as requested"""


# --- study formulas -----------------------------------------------------
# Copied from benchmarks/scripts/csp_encoding_study.py (csm_bytes, framing);
# the lab's targetChunks/payloadEntries/dictionaryReferences feed the same
# formulas so the real CSP bytes can be compared with the study's estimate.


def csm_bytes(chunks: int) -> int:
    blocks = max(1, math.ceil(chunks / 4096))
    # PREAMBLE, CORE (IDs), CBLK blocks, CEND, FOOT, TRAILER with 16-byte headers.
    return 32 + 16 + 184 + blocks * (16 + 24 + 4) + chunks * 36 + (16 + 48) + (16 + 48) + 64


def framing(target_chunks: int, entries: int, dict_refs: int) -> int:
    payl_blocks = math.ceil(entries / 4096) if entries else 0
    return (32                                     # PREAMBLE
            + 16 + csm_bytes(target_chunks)        # TCSM
            + 16 + 32                              # BASE
            + payl_blocks * (16 + 16 + 4)          # PAYL headers, prefixes, CRCs
            + entries * 40 + dict_refs * 32        # PAYL entry headers + dictionary references
            + 16 + 8 + entries * 24 + 4            # PIDX
            + 16 + 56 + 64)                        # FOOT, TRAILER


# --- loading ------------------------------------------------------------


def load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise SummaryError(f"{path}: not valid JSON: {error}") from error


def load_corpus_lock(lock_path: Path) -> tuple[str, dict[str, str]]:
    lock = load_json(lock_path)
    pairs_sha256 = lock.get("pairsSha256")
    if not isinstance(pairs_sha256, str) or not pairs_sha256:
        raise SummaryError(f"{lock_path}: the corpus lock has no pairsSha256")
    manifest_path = lock_path.parent / "patch-corpus.json"
    if not manifest_path.is_file():
        raise SummaryError(f"{manifest_path}: corpus manifest not found next to the lock")
    manifest = load_json(manifest_path)
    families: dict[str, str] = {}
    for family in manifest.get("families", []):
        identifier, split = family.get("id"), family.get("split")
        if not identifier or split not in ("calibration", "holdout"):
            raise SummaryError(f"{manifest_path}: every family needs an id and a calibration/holdout split")
        families[identifier] = split
    if not families:
        raise SummaryError(f"{manifest_path}: no families")
    return pairs_sha256, families


def read_platform(directory: Path, pairs_sha256: str) -> dict:
    if not directory.is_dir():
        raise SummaryError(f"{directory}: platform directory does not exist")

    lanes: dict[str, dict] = {}
    memory: dict | None = None
    references: dict | None = None
    evidence_files: list[str] = []
    documents: list[dict] = []

    for path in sorted(directory.rglob("*.json")):
        document = load_json(path)
        schema = document.get("schema")
        if schema == LAB_SCHEMA:
            lane = document.get("lane")
            if not isinstance(lane, str) or not lane:
                raise SummaryError(f"{path}: lab file without a lane")
            if lane in lanes:
                raise SummaryError(f"{path}: lane {lane} appears twice (also {lanes[lane]['path']})")
            lanes[lane] = {"path": path, "document": document}
        elif schema == MEMORY_SCHEMA:
            if memory is not None:
                raise SummaryError(f"{path}: a second memory file appears (also {memory['path']})")
            memory = {"path": path, "document": document}
        elif schema == REFS_SCHEMA:
            if references is not None:
                raise SummaryError(f"{path}: a second reference file appears (also {references['path']})")
            references = {"path": path, "document": document}
        else:
            continue

        recorded = document.get("corpusPairsSha256")
        if recorded != pairs_sha256:
            raise SummaryError(
                f"{path}: corpusPairsSha256 {recorded!r} does not match the corpus lock {pairs_sha256!r}"
            )
        evidence_files.append(str(path.relative_to(directory)))
        documents.append(document)

    commits = []
    for document in documents:
        commit = (document.get("environment") or {}).get("gitCommit")
        if not isinstance(commit, str) or not commit:
            raise SummaryError(f"{directory}: a file does not record environment.gitCommit")
        commits.append(commit)
    unique_commits = sorted(set(commits))
    if len(unique_commits) > 1:
        raise SummaryError(f"{directory}: mixed evidence, gitCommit values {unique_commits}")
    if not documents:
        print(f"warning: {directory}: no patch-lab JSON files", file=sys.stderr)

    environment: dict[str, str] = {}
    for document in documents:
        for key in ("osDescription", "osArchitecture"):
            value = (document.get("environment") or {}).get(key)
            if value and not environment.get(key):
                environment[key] = value

    run_ids: list[str] = []
    for entry in list(lanes.values()) + ([references] if references else []):
        run_id = entry["document"].get("runId")
        if run_id and run_id not in run_ids:
            run_ids.append(run_id)

    return {
        "directory": str(directory),
        "name": directory.name,
        "gitCommit": unique_commits[0] if unique_commits else None,
        "osDescription": environment.get("osDescription"),
        "osArchitecture": environment.get("osArchitecture"),
        "runIds": run_ids,
        "files": evidence_files,
        "lanes": {lane: entry["document"] for lane, entry in lanes.items()},
        "memory": memory["document"] if memory else None,
        "references": references["document"] if references else None,
    }


# --- aggregates ---------------------------------------------------------


def number(value) -> float:
    return 0 if value is None else value


def lab_totals(document: dict) -> dict:
    records = document.get("files") or []
    totals = {field: 0 for field in BYTE_FIELDS}
    totals.update({field: 0.0 for field in TIME_FIELDS})
    totals["files"] = len(records)
    for record in records:
        for field in BYTE_FIELDS:
            totals[field] += int(number(record.get(field)))
        for field in TIME_FIELDS:
            totals[field] += float(number(record.get(field)))
    return totals


def reference_totals(document: dict, lane: str) -> dict:
    totals = {"files": 0, "bytes": 0, "encodeSeconds": 0.0, "decodeSeconds": 0.0}
    for record in document.get("files") or []:
        entry = (record.get("lanes") or {}).get(lane)
        if not isinstance(entry, dict):
            raise SummaryError(f"reference file: a file record has no lane {lane}")
        totals["files"] += 1
        totals["bytes"] += int(number(entry.get("bytes")))
        totals["encodeSeconds"] += float(number(entry.get("encodeSeconds")))
        totals["decodeSeconds"] += float(number(entry.get("decodeSeconds")))
    return totals


def lane_rows(platform: dict) -> dict[str, dict]:
    rows: dict[str, dict] = {}
    for lane in sorted(platform["lanes"]):
        totals = lab_totals(platform["lanes"][lane])
        rows[lane] = {
            "files": totals["files"],
            "bytes": totals["patchBytes"],
            "createSeconds": totals["createSeconds"],
            "applySeconds": totals["applySeconds"],
            "applyNoCheckSeconds": totals["applyNoCheckSeconds"],
        }
    if platform["references"] is not None:
        for lane in REFERENCE_LANES:
            totals = reference_totals(platform["references"], lane)
            rows[lane] = {
                "files": totals["files"],
                "bytes": totals["bytes"],
                "encodeSeconds": totals["encodeSeconds"],
                "decodeSeconds": totals["decodeSeconds"],
            }
    for row in rows.values():
        decode_or_apply = row.get("applySeconds")
        if decode_or_apply is None:
            decode_or_apply = row.get("decodeSeconds") or 0.0
        row["endToEnd50Seconds"] = row["bytes"] * 8 / BANDWIDTH_LOW_BPS + decode_or_apply
        row["endToEnd1000Seconds"] = row["bytes"] * 8 / BANDWIDTH_HIGH_BPS + decode_or_apply
    return rows


def study_estimate(csp_document: dict) -> int:
    total = 0
    for record in csp_document.get("files") or []:
        total += int(number(record.get("storedPayloadBytes"))) + framing(
            int(number(record.get("targetChunks"))),
            int(number(record.get("payloadEntries"))),
            int(number(record.get("dictionaryReferences"))),
        )
    return total


def check_families(records: list[dict], families: dict[str, str], label: str) -> None:
    for record in records:
        family = record.get("family")
        if family not in families:
            raise SummaryError(f"{label}: unknown family {family!r} (not in the corpus manifest)")


def combine(values: list[str | None], failure: str) -> str:
    if any(value == failure for value in values):
        return failure
    if not values or any(value is None or value == NOT_EVALUATED for value in values):
        return NOT_EVALUATED
    return "ADOPT"


# --- experiments --------------------------------------------------------


def evaluate_prefreeze(platforms: list[dict]) -> dict:
    r1_platforms: dict[str, dict] = {}
    r2_platforms: dict[str, dict] = {}
    informative: dict[str, dict] = {}
    missing_platforms: list[str] = []

    for platform in platforms:
        name = platform["name"]
        lanes = platform["lanes"]
        entry = {"directory": platform["directory"], "lanes": lane_rows(platform)}
        if "csp" in lanes:
            csp = lab_totals(lanes["csp"])
            estimate = study_estimate(lanes["csp"])
            entry["csp"] = {
                "patchBytes": csp["patchBytes"],
                "uniqueMissingBytes": csp["uniqueMissingBytes"],
                "tcsmBytes": csp["tcsmBytes"],
                "storedPayloadBytes": csp["storedPayloadBytes"],
                "payloadEntries": csp["payloadEntries"],
                "dictionaryReferences": csp["dictionaryReferences"],
                "targetChunks": csp["targetChunks"],
                "studyEstimateBytes": estimate,
                "studyEstimateDeltaBytes": csp["patchBytes"] - estimate,
            }
        informative[name] = entry

        if "csp" not in lanes or "csp-raw" not in lanes:
            missing_platforms.append(name)
            continue
        csp = lab_totals(lanes["csp"])
        raw = lab_totals(lanes["csp-raw"])
        if csp["files"] == 0 or raw["files"] == 0:
            missing_platforms.append(name)
            continue

        if raw["patchBytes"] > 0:
            ratio = csp["patchBytes"] / raw["patchBytes"]
            r1_platforms[name] = {
                "cspBytes": csp["patchBytes"],
                "cspRawBytes": raw["patchBytes"],
                "ratio": ratio,
                "maxRatio": R1_MAX_RATIO,
                "verdict": "ADOPT" if ratio <= R1_MAX_RATIO else "REJECT",
            }

        if csp["applySeconds"] > 0 and raw["applySeconds"] > 0:
            csp_end_to_end = csp["patchBytes"] * 8 / R2_BANDWIDTH_BPS + csp["applySeconds"]
            raw_end_to_end = raw["patchBytes"] * 8 / R2_BANDWIDTH_BPS + raw["applySeconds"]
            r2_platforms[name] = {
                "bandwidthBitsPerSecond": R2_BANDWIDTH_BPS,
                "cspBytes": csp["patchBytes"],
                "cspRawBytes": raw["patchBytes"],
                "cspApplySeconds": csp["applySeconds"],
                "cspRawApplySeconds": raw["applySeconds"],
                "cspEndToEndSeconds": csp_end_to_end,
                "cspRawEndToEndSeconds": raw_end_to_end,
                "verdict": "ADOPT" if csp_end_to_end <= raw_end_to_end else "REJECT",
            }

    r1_verdict = combine([entry["verdict"] for entry in r1_platforms.values()], "REJECT")
    r2_verdict = combine([entry["verdict"] for entry in r2_platforms.values()], "REJECT")
    if missing_platforms:
        if r1_verdict != "REJECT":
            r1_verdict = NOT_EVALUATED
        if r2_verdict != "REJECT":
            r2_verdict = NOT_EVALUATED
    verdict = combine([r1_verdict, r2_verdict], "REJECT")

    notes = []
    if missing_platforms:
        notes.append("no csp/csp-raw lanes on " + ", ".join(missing_platforms))
    if not r1_platforms:
        notes.append("R1: no usable csp bytes against csp-raw bytes")
    if not r2_platforms:
        notes.append("R2: no usable csp apply timings against csp-raw apply timings")

    used_platforms = [platform["name"] for platform in platforms if platform["lanes"] or platform["references"]]
    run_ids: list[str] = []
    for platform in platforms:
        for run_id in platform["runIds"]:
            if run_id not in run_ids:
                run_ids.append(run_id)

    return {
        "verdict": verdict,
        "reason": "; ".join(notes) if verdict == NOT_EVALUATED else None,
        "rules": {
            "R1": {"verdict": r1_verdict, "maxRatio": R1_MAX_RATIO, "platforms": r1_platforms},
            "R2": {
                "verdict": r2_verdict,
                "bandwidthBitsPerSecond": R2_BANDWIDTH_BPS,
                "platforms": r2_platforms,
            },
        },
        "informative": {"platforms": informative},
        "platforms": used_platforms,
        "runIds": run_ids,
    }


def find_x64_linux(platforms: list[dict]) -> dict | None:
    for platform in platforms:
        architecture = (platform.get("osArchitecture") or "").lower()
        description = (platform.get("osDescription") or "").lower()
        # .NET reports the distribution ("Ubuntu 24.04.5 LTS"), not "Linux",
        # so the RunId platform suffix of the workflow decides as well.
        linux = "linux" in description or any(
            str(run_id).endswith("-linux-x64") for run_id in platform.get("runIds") or []
        )
        if architecture in ("x64", "amd64", "x86_64") and linux:
            return platform
    return None


def cross_platform_sweep_bytes(platforms: list[dict]) -> dict[str, dict]:
    result = {}
    for lane in SWEEP_LANES:
        present = [platform for platform in platforms if lane in platform["lanes"]]
        missing = [platform["name"] for platform in platforms if lane not in platform["lanes"]]
        entry: dict = {"platforms": [platform["name"] for platform in present], "missingOn": missing}
        if len(present) < 2:
            entry["identical"] = None
        else:
            maps = []
            for platform in present:
                maps.append(
                    {
                        (record.get("family"), record.get("base"), record.get("target"), record.get("path")):
                        int(number(record.get("patchBytes")))
                        for record in platform["lanes"][lane].get("files") or []
                    }
                )
            identical = all(mapping == maps[0] for mapping in maps[1:])
            entry["identical"] = identical
            if not identical:
                for key in sorted(set().union(*(mapping.keys() for mapping in maps))):
                    values = [mapping.get(key) for mapping in maps]
                    if len(set(values)) > 1:
                        entry["firstDifference"] = {
                            "family": key[0],
                            "base": key[1],
                            "target": key[2],
                            "path": key[3],
                            "patchBytesPerPlatform": values,
                        }
                        break
        result[lane] = entry
    return result


def evaluate_enc(platforms: list[dict], families: dict[str, str]) -> dict:
    experiment: dict = {"crossPlatformSweepBytes": cross_platform_sweep_bytes(platforms)}
    platform = find_x64_linux(platforms)
    if platform is None:
        experiment.update({"verdict": NOT_EVALUATED, "reason": "no x64 Linux sweep", "platforms": [], "runIds": []})
        return experiment

    missing = [lane for lane in SWEEP_LANES if lane not in platform["lanes"]]
    if missing:
        experiment.update(
            {
                "verdict": NOT_EVALUATED,
                "reason": "no x64 Linux sweep",
                "missingLanes": missing,
                "platforms": [platform["name"]],
                "runIds": list(platform["runIds"]),
            }
        )
        return experiment

    settings: dict[str, dict] = {}
    for lane in SWEEP_LANES:
        records = platform["lanes"][lane].get("files") or []
        settings[lane] = {
            "calibrationBytes": sum(
                int(number(record.get("patchBytes"))) for record in records if families[record["family"]] == "calibration"
            ),
            "calibrationCreateSeconds": sum(
                float(number(record.get("createSeconds"))) for record in records if families[record["family"]] == "calibration"
            ),
            "holdoutBytes": sum(
                int(number(record.get("patchBytes"))) for record in records if families[record["family"]] == "holdout"
            ),
        }

    default = settings[DEFAULT_SWEEP_LANE]
    create_time_limit = CREATE_TIME_FACTOR * default["calibrationCreateSeconds"]
    eligible = [
        lane for lane in SWEEP_LANES if settings[lane]["calibrationCreateSeconds"] <= create_time_limit
    ]
    for lane in SWEEP_LANES:
        settings[lane]["eligible"] = lane in eligible

    winner = min(eligible, key=lambda lane: (settings[lane]["calibrationBytes"], lane))
    if default["calibrationBytes"] > 0:
        saving = 1 - settings[winner]["calibrationBytes"] / default["calibrationBytes"]
    else:
        saving = None
    holdout_within_default = settings[winner]["holdoutBytes"] <= default["holdoutBytes"]
    adopt_winner = (
        winner != DEFAULT_SWEEP_LANE
        and saving is not None
        and saving >= MINIMUM_SAVING
        and holdout_within_default
    )

    experiment.update(
        {
            "verdict": "ADOPT",
            "reason": None,
            "adopted": winner if adopt_winner else "current default",
            "outcome": f"ADOPT {winner if adopt_winner else 'current default'}",
            "platform": platform["name"],
            "defaultLane": DEFAULT_SWEEP_LANE,
            "winner": winner,
            "createTimeFactor": CREATE_TIME_FACTOR,
            "createTimeLimitSeconds": create_time_limit,
            "minimumSaving": MINIMUM_SAVING,
            "savingFraction": saving,
            "winnerHoldoutWithinDefault": holdout_within_default,
            "eligible": eligible,
            "settings": settings,
            "platforms": [platform["name"]],
            "runIds": list(platform["runIds"]),
        }
    )
    return experiment


def evaluate_apply(platforms: list[dict]) -> dict:
    a1_platforms: dict[str, dict] = {}
    a2_platforms: dict[str, dict] = {}
    a1_missing: list[str] = []
    a2_missing: list[str] = []

    for platform in platforms:
        name = platform["name"]
        if "csp" in platform["lanes"]:
            totals = lab_totals(platform["lanes"]["csp"])
            if totals["files"] > 0 and totals["applySeconds"] > 0 and totals["applyNoCheckSeconds"] > 0:
                overhead = totals["applySeconds"] / totals["applyNoCheckSeconds"] - 1
                a1_platforms[name] = {
                    "applySeconds": totals["applySeconds"],
                    "applyNoCheckSeconds": totals["applyNoCheckSeconds"],
                    "overheadFraction": overhead,
                    "maxOverheadFraction": APPLY_OVERHEAD_MAX,
                    "verdict": "ADOPT" if overhead <= APPLY_OVERHEAD_MAX else "DEFER",
                }
        if name not in a1_platforms:
            a1_platforms[name] = {"verdict": None, "reason": "no csp apply timings"}
            a1_missing.append(name)

        if platform["memory"] is not None:
            document = platform["memory"]
            baseline = document.get("idleBaselineBytes")
            records = document.get("files") or []
            if baseline is None or not records:
                a2_platforms[name] = {"verdict": None, "reason": "memory file has no records or baseline"}
            else:
                baseline = int(baseline)
                worst = None
                within = True
                for record in records:
                    excess = (
                        max(
                            int(number(record.get("createPeakBytes"))),
                            int(number(record.get("applyPeakBytes"))),
                        )
                        - baseline
                    )
                    if worst is None or excess > worst["excessBytes"]:
                        worst = {
                            "family": record.get("family"),
                            "base": record.get("base"),
                            "target": record.get("target"),
                            "path": record.get("path"),
                            "createPeakBytes": int(number(record.get("createPeakBytes"))),
                            "applyPeakBytes": int(number(record.get("applyPeakBytes"))),
                            "excessBytes": excess,
                        }
                    within = within and excess <= MEMORY_LIMIT_BYTES
                a2_platforms[name] = {
                    "idleBaselineBytes": baseline,
                    "records": len(records),
                    "worst": worst,
                    "limitBytes": MEMORY_LIMIT_BYTES,
                    "verdict": "ADOPT" if within else "DEFER",
                }
        else:
            a2_platforms[name] = {"verdict": None, "reason": "no memory file"}
            a2_missing.append(name)

    a1_verdict = combine([entry["verdict"] for entry in a1_platforms.values()], "DEFER")
    a2_verdict = combine([entry["verdict"] for entry in a2_platforms.values()], "DEFER")
    if a1_missing and a1_verdict == "ADOPT":
        a1_verdict = NOT_EVALUATED
    if a2_missing and a2_verdict == "ADOPT":
        a2_verdict = NOT_EVALUATED
    verdict = combine([a1_verdict, a2_verdict], "DEFER")

    notes = []
    if a1_missing:
        notes.append("A1: no csp apply timings on " + ", ".join(a1_missing))
    if a2_missing:
        notes.append("A2: no memory records on " + ", ".join(a2_missing))
    if a1_verdict == NOT_EVALUATED and not a1_missing:
        notes.append("A1: no usable csp apply and apply-no-check timings")
    if a2_verdict == NOT_EVALUATED and not a2_missing:
        notes.append("A2: no usable memory records")

    used_platforms = [
        platform["name"] for platform in platforms if "csp" in platform["lanes"] or platform["memory"] is not None
    ]
    run_ids: list[str] = []
    for platform in platforms:
        for run_id in platform["runIds"]:
            if run_id not in run_ids:
                run_ids.append(run_id)

    return {
        "verdict": verdict,
        "reason": "; ".join(notes) if verdict == NOT_EVALUATED else None,
        "rules": {
            "A1": {"verdict": a1_verdict, "maxOverheadFraction": APPLY_OVERHEAD_MAX, "platforms": a1_platforms},
            "A2": {"verdict": a2_verdict, "limitBytes": MEMORY_LIMIT_BYTES, "platforms": a2_platforms},
        },
        "platforms": used_platforms,
        "runIds": run_ids,
    }


def summarize(platform_dirs: list[Path], lock_path: Path) -> dict:
    pairs_sha256, families = load_corpus_lock(lock_path)
    platforms: list[dict] = []
    seen: set[str] = set()
    for directory in platform_dirs:
        platform = read_platform(directory, pairs_sha256)
        if platform["name"] in seen:
            raise SummaryError(f"{directory}: duplicate platform directory name {platform['name']!r}")
        seen.add(platform["name"])
        for lane, document in platform["lanes"].items():
            check_families(document.get("files") or [], families, f"{platform['name']} {lane}")
        if platform["references"] is not None:
            check_families(
                platform["references"].get("files") or [], families, f"{platform['name']} references"
            )
        platforms.append(platform)

    return {
        "schema": SUMMARY_SCHEMA,
        "corpusLock": {"path": str(lock_path), "pairsSha256": pairs_sha256},
        "corpusManifest": {"path": str(lock_path.parent / "patch-corpus.json"), "families": families},
        "platforms": [
            {
                key: platform[key]
                for key in ("directory", "name", "gitCommit", "osDescription", "osArchitecture", "runIds", "files")
            }
            for platform in platforms
        ],
        "experiments": {
            PREFREEZE: evaluate_prefreeze(platforms),
            ENC: evaluate_enc(platforms, families),
            APPLY: evaluate_apply(platforms),
        },
    }


# --- reporting ----------------------------------------------------------


def mib(value: float) -> str:
    return f"{value / MIB:.2f}"


def percent(value: float) -> str:
    return f"{value * 100:.2f}%"


def seconds(value: float) -> str:
    return f"{value:.2f}"


def verdict_cell(experiment: dict, label: str) -> str:
    if experiment["verdict"] == NOT_EVALUATED:
        return f"**{NOT_EVALUATED}** {experiment.get('reason') or ''}".strip()
    return f"**{label}**"


def write_markdown(document: dict) -> str:
    lock = document["corpusLock"]
    lines = [
        "# Patching pre-freeze lab summary",
        "",
        f"- Corpus lock `pairsSha256`: `{lock['pairsSha256']}` (from `{lock['path']}`)",
    ]
    for platform in document["platforms"]:
        run_ids = ", ".join(f"`{run_id}`" for run_id in platform["runIds"]) or "-"
        lines.append(
            f"- `{platform['name']}`: commit `{platform['gitCommit']}`, "
            f"{platform['osDescription']} / {platform['osArchitecture']}, "
            f"{len(platform['files'])} evidence files, RunIds: {run_ids}"
        )
    lines.extend(
        [
            "",
            "Aggregates are sums over changed files. Bytes are MiB and ratios are percent, both with two decimals.",
            "",
            "## Verdicts",
            "",
            "| experiment | verdict | numbers |",
            "|---|---|---|",
        ]
    )

    prefreeze = document["experiments"][PREFREEZE]
    numbers = []
    for name, entry in prefreeze["rules"]["R1"]["platforms"].items():
        numbers.append(f"R1 {name}: {percent(entry['ratio'])} (max {percent(entry['maxRatio'])})")
    for name, entry in prefreeze["rules"]["R2"]["platforms"].items():
        numbers.append(
            f"R2 {name}: {seconds(entry['cspEndToEndSeconds'])} s vs {seconds(entry['cspRawEndToEndSeconds'])} s at 1 Gbit/s"
        )
    if not numbers:
        numbers.append(prefreeze.get("reason") or "no csp/csp-raw lanes")
    lines.append(f"| {PREFREEZE} | {verdict_cell(prefreeze, prefreeze['verdict'])} | {'; '.join(numbers)} |")

    enc = document["experiments"][ENC]
    if enc["verdict"] == NOT_EVALUATED:
        enc_cell = verdict_cell(enc, enc["verdict"])
        enc_numbers = enc.get("reason") or ""
    else:
        enc_cell = f"**ADOPT {enc['adopted']}**"
        saving = enc["savingFraction"]
        enc_numbers = (
            f"winner {enc['winner']} on {enc['platform']}, "
            f"saves {percent(saving) if saving is not None else 'n/a'} of the default calibration bytes, "
            f"holdout within default: {'yes' if enc['winnerHoldoutWithinDefault'] else 'no'}"
        )
    lines.append(f"| {ENC} | {enc_cell} | {enc_numbers} |")

    apply_experiment = document["experiments"][APPLY]
    apply_numbers = []
    for name, entry in apply_experiment["rules"]["A1"]["platforms"].items():
        if entry.get("verdict"):
            apply_numbers.append(f"A1 {name}: {percent(entry['overheadFraction'])} (max {percent(entry['maxOverheadFraction'])})")
    for name, entry in apply_experiment["rules"]["A2"]["platforms"].items():
        if entry.get("verdict"):
            apply_numbers.append(f"A2 {name}: worst +{mib(entry['worst']['excessBytes'])} MiB")
    if not apply_numbers:
        apply_numbers.append(apply_experiment.get("reason") or "no apply or memory evidence")
    lines.append(f"| {APPLY} | {verdict_cell(apply_experiment, apply_experiment['verdict'])} | {'; '.join(apply_numbers)} |")

    lines.extend(
        [
            "",
            "## PATCH-PREFREEZE-001 lanes",
            "",
            "| platform | lane | files | bytes MiB | create s | apply s | decode s | 50 Mbit/s s | 1 Gbit/s s |",
            "|---|---|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    for platform in document["platforms"]:
        rows = prefreeze["informative"]["platforms"][platform["name"]]["lanes"]
        for lane, row in rows.items():
            create = seconds(row["createSeconds"]) if "createSeconds" in row else "-"
            apply_ = seconds(row["applySeconds"]) if "applySeconds" in row else "-"
            decode = seconds(row["decodeSeconds"]) if "decodeSeconds" in row else "-"
            lines.append(
                f"| {platform['name']} | {lane} | {row['files']} | {mib(row['bytes'])} | {create} | {apply_} | "
                f"{decode} | {seconds(row['endToEnd50Seconds'])} | {seconds(row['endToEnd1000Seconds'])} |"
            )

    lines.extend(
        [
            "",
            "## PATCH-PREFREEZE-001 CSP details",
            "",
            "| platform | csp patch MiB | embedded TCSM | unique missing MiB | study estimate MiB | estimate - csp MiB |",
            "|---|---:|---:|---:|---:|---:|",
        ]
    )
    for platform in document["platforms"]:
        entry = prefreeze["informative"]["platforms"][platform["name"]].get("csp")
        if entry is None:
            continue
        share = entry["tcsmBytes"] / entry["patchBytes"] if entry["patchBytes"] else 0.0
        lines.append(
            f"| {platform['name']} | {mib(entry['patchBytes'])} | {percent(share)} | "
            f"{mib(entry['uniqueMissingBytes'])} | {mib(entry['studyEstimateBytes'])} | "
            f"{mib(entry['studyEstimateDeltaBytes'])} |"
        )

    lines.extend(["", "## PATCH-ENC-002 sweep", ""])
    if enc["verdict"] == NOT_EVALUATED:
        lines.append(f"Not evaluated: {enc.get('reason') or 'no sweep evidence'}.")
        if enc.get("missingLanes"):
            lines.append("")
            lines.append(f"Missing sweep lanes: {', '.join(enc['missingLanes'])}.")
    else:
        lines.extend(
            [
                f"x64 Linux platform: `{enc['platform']}`; default lane `{enc['defaultLane']}`, "
                f"eligible when create time is at most {enc['createTimeFactor']:.2f} x the default "
                f"({seconds(enc['createTimeLimitSeconds'])} s).",
                "",
                "| setting | eligible | calibration create s | calibration MiB | holdout MiB |",
                "|---|---:|---:|---:|---:|",
            ]
        )
        for lane in SWEEP_LANES:
            row = enc["settings"][lane]
            marker = "yes" if row["eligible"] else "no"
            if lane == enc["winner"]:
                marker += " (winner)"
            lines.append(
                f"| {lane} | {marker} | {seconds(row['calibrationCreateSeconds'])} | "
                f"{mib(row['calibrationBytes'])} | {mib(row['holdoutBytes'])} |"
            )
        saving = enc["savingFraction"]
        lines.extend(
            [
                "",
                f"Saving of the winner against the default calibration bytes: "
                f"{percent(saving) if saving is not None else 'n/a'} (minimum {percent(enc['minimumSaving'])}); "
                f"winner holdout within default: {'yes' if enc['winnerHoldoutWithinDefault'] else 'no'}.",
                f"**ADOPT {enc['adopted']}**.",
            ]
        )

    lines.extend(["", "Sweep patch bytes across platforms:", "", "| setting | platforms | identical |", "|---|---|---|"])
    for lane, entry in enc["crossPlatformSweepBytes"].items():
        if entry["identical"] is None:
            status = "single platform"
        else:
            status = "yes" if entry["identical"] else "no"
        lines.append(f"| {lane} | {', '.join(entry['platforms']) or '-'} | {status} |")

    lines.extend(
        [
            "",
            "## PATCH-APPLY-001",
            "",
            "| platform | apply s | check off s | apply/check off - 1 | A1 | memory records | worst excess MiB | A2 |",
            "|---|---:|---:|---:|---|---:|---:|---|",
        ]
    )
    for platform in document["platforms"]:
        name = platform["name"]
        a1 = apply_experiment["rules"]["A1"]["platforms"].get(name)
        a2 = apply_experiment["rules"]["A2"]["platforms"].get(name)
        apply_cell = seconds(a1["applySeconds"]) if a1 and a1.get("verdict") else "-"
        check_cell = seconds(a1["applyNoCheckSeconds"]) if a1 and a1.get("verdict") else "-"
        overhead_cell = percent(a1["overheadFraction"]) if a1 and a1.get("verdict") else "-"
        a1_cell = a1["verdict"] if a1 and a1.get("verdict") else (a1 or {}).get("reason", "-")
        records_cell = str(a2["records"]) if a2 and a2.get("verdict") else "-"
        excess_cell = mib(a2["worst"]["excessBytes"]) if a2 and a2.get("verdict") else "-"
        a2_cell = a2["verdict"] if a2 and a2.get("verdict") else (a2 or {}).get("reason", "-")
        lines.append(
            f"| {name} | {apply_cell} | {check_cell} | {overhead_cell} | {a1_cell} | {records_cell} | "
            f"{excess_cell} | {a2_cell} |"
        )
    if apply_experiment["verdict"] == NOT_EVALUATED:
        lines.extend(["", f"Not evaluated: {apply_experiment.get('reason') or 'missing inputs'}."])

    lines.append("")
    return "\n".join(lines)


# --- entry point --------------------------------------------------------


def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("platform_dirs", nargs="+", type=Path, help="one evidence directory per platform")
    parser.add_argument("--corpus-lock", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path, help="verdict JSON")
    parser.add_argument("--markdown", required=True, type=Path, help="summary Markdown")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        document = summarize(args.platform_dirs, args.corpus_lock)
    except (SummaryError, OSError, TypeError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    markdown = write_markdown(document)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.write_text(markdown, encoding="utf-8")

    for name in (PREFREEZE, ENC, APPLY):
        print(f"{name}: {document['experiments'][name]['verdict']}")
    print(f"wrote {args.output} and {args.markdown}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
