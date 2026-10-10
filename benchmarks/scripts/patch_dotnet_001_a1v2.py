#!/usr/bin/env python3
"""PATCH-DOTNET-001 A1-v2: independently pinned managed-application calibration.

Experimental inventory only. Never creates patches, tunes parameters or changes
the original evaluation/holdout population.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import os
import re
import tempfile
from pathlib import Path

import patch_dotnet_001_corpus as a1
import patch_dotnet_001_a2_classify as a2

SCHEMA = "chunkshift.patch-dotnet-calibration-extension.v1"
OUT_SCHEMA = "chunkshift.patch-dotnet-calibration-a1v2-inventory.v1"
GROUP_ID = re.compile(r"^[a-z][a-z0-9-]{1,79}$")
EXPECTED_A2_RUN = 38028439773
EXPECTED_A2_ARTIFACT = 11660869109
A2_FILE_SHA256 = "0cdafee3f5ee6f288ed798bd1f7726a452dd51a919d891855e9436f6e151ad66"
A1_PLAN_SHA256 = "0ee85c6307ea2c6612e52313485aa1bcf5725b457ae0c01413cf0d1a48e552c8"


def validate(doc: dict) -> list[dict]:
    if doc.get("schema") != SCHEMA or doc.get("experimentId") != "PATCH-DOTNET-001":
        raise a1.CorpusError("V2_SCHEMA")
    parent = doc.get("parentA2", {})
    if parent.get("planSha256") != A1_PLAN_SHA256 or parent.get("filesSha256") != A2_FILE_SHA256 \
            or parent.get("artifactRun") != EXPECTED_A2_RUN or parent.get("artifactId") != EXPECTED_A2_ARTIFACT:
        raise a1.CorpusError("V2_PARENT_A2_IDENTITY")
    groups = doc.get("groups")
    if not isinstance(groups, list) or len(groups) != 3:
        raise a1.CorpusError("V2_GROUP_COUNT")
    groups = sorted(groups, key=lambda x: x["id"])
    ids = set()
    names = set()
    deployments = set()
    for group in groups:
        gid = group.get("id")
        if not isinstance(gid, str) or not GROUP_ID.fullmatch(gid) or gid in ids:
            raise a1.CorpusError("V2_GROUP_ID")
        ids.add(gid)
        if group.get("product") != "powershell" or group.get("role") != "calibration":
            raise a1.CorpusError("V2_ROLE_OR_PRODUCT")
        key = (group.get("deployment"), group.get("rid"))
        if key in deployments or key[0] not in {"framework-dependent", "self-contained"} \
                or key[1] not in {"win-x64", "linux-x64"}:
            raise a1.CorpusError("V2_DEPLOYMENT_DUPLICATE_OR_UNSUPPORTED")
        deployments.add(key)
        if group.get("format") not in {"zip", "tar.gz"} \
                or group.get("archivePathRule") != "as-is+payload-prefix" \
                or group.get("candidateAllowPrefixes") != ["payload/"]:
            raise a1.CorpusError("V2_EXTRACTION_POLICY")
        if (group.get("base"), group.get("target")) != ("7.5.3", "7.5.4"):
            raise a1.CorpusError("V2_VERSION_LOCK")
        assets = group.get("assets")
        if not isinstance(assets, list) or len(assets) != 2 \
                or [a.get("version") for a in assets if isinstance(a, dict)] != ["7.5.3", "7.5.4"]:
            raise a1.CorpusError("V2_ASSET_VERSION_ORDER")
        for asset in assets:
            name, url, digest = (asset.get(k) for k in ("name", "url", "digest"))
            if not isinstance(name, str) or not name or Path(name).name != name \
                    or "\\" in name or "/" in name or name in names:
                raise a1.CorpusError("V2_ASSET_NAME")
            names.add(name)
            required = f"https://github.com/PowerShell/PowerShell/releases/download/v{asset['version']}/{name}"
            if url != required or asset.get("digestAlgorithm") != "sha256" \
                    or not isinstance(digest, str) or len(digest) != 64 \
                    or not re.fullmatch(r"[0-9a-f]{64}", digest):
                raise a1.CorpusError("V2_SOURCE_HASH_OR_URL")
            suffix = ".zip" if group["format"] == "zip" else ".tar.gz"
            if not name.endswith(suffix):
                raise a1.CorpusError("V2_ARCHIVE_FORMAT")
    return groups


def eval_digest_set(index_path: Path, expected_sha: str) -> set[str]:
    if a1.sha256_file(index_path) != expected_sha:
        raise a1.CorpusError("V2_A2_EVALUATION_INDEX_SHA")
    known = set()
    count = 0
    for line in index_path.read_text(encoding="utf-8").splitlines():
        row = json.loads(line)
        if row.get("role") == "evaluation":
            digest = row.get("sha256")
            if not isinstance(digest, str) or not re.fullmatch("[0-9a-f]{64}", digest):
                raise a1.CorpusError("V2_INVALID_EVALUATION_DIGEST")
            known.add(digest)
            count += 1
    if not known or count < 2:
        raise a1.CorpusError("V2_MISSING_EVALUATION_POPULATION")
    return known


def materialize(doc: dict, doc_bytes: bytes, groups: list[dict],
                source: Path, root: Path, eval_shas: set[str]) -> dict:
    if root.exists():
        raise a1.CorpusError("V2_ROOT_EXISTS")
    sources = a1.verified_assets(groups, source)
    root.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".patch-dotnet-v2-", dir=root.parent) as tmp:
        stage = Path(tmp)
        extracted = []
        for group in groups:
            for asset in group["assets"]:
                # Wrapper adds the immutable prefix; existing A1 extraction checks
                # traversal, symlink handling, case collisions and bounded bytes.
                dest = stage / "tree" / group["id"] / asset["version"] / "payload"
                dest.mkdir(parents=True)
                copy = dict(group)
                copy["pathRule"] = "as-is"
                stats = a1.extract_archive(source / asset["name"], copy, dest)
                extracted.append({"group": group["id"], "version": asset["version"], **stats})
        pairs = [a1.pair_record(g, stage) for g in groups]
        member_records = []
        summaries = []
        for group, pair in zip(groups, pairs):
            left = a1.walk_files(stage / "tree" / group["id"] / group["base"])
            right = a1.walk_files(stage / "tree" / group["id"] / group["target"])
            local = collections.Counter()
            for version, allfiles in ((group["base"], left), (group["target"], right)):
                for name, (size, digest) in allfiles.items():
                    member_records.append({"group": group["id"], "role": "calibration",
                                           "version": version, "path": name, "bytes": size,
                                           "sha256": digest})
            for row in pair["changed"]:
                name = row["path"]
                b = a2.classify(stage / "tree" / group["id"] / group["base"] / name, 64 * 1024 * 1024)
                t = a2.classify(stage / "tree" / group["id"] / group["target"] / name, 64 * 1024 * 1024)
                same_bytes_as_eval = row["baseSha256"] in eval_shas or row["targetSha256"] in eval_shas
                potential = (not same_bytes_as_eval and b["kind"] == t["kind"] == "ILONLY"
                             and b["machine"] == t["machine"] and b["architecture"] != "unknown")
                local["changedFiles"] += 1
                local["changedTargetBytes"] += row["targetSize"]
                if potential:
                    local["structuralD3Files"] += 1
                    local["structuralD3TargetBytes"] += row["targetSize"]
                if same_bytes_as_eval:
                    local["evalDuplicateFiles"] += 1
                    local["evalDuplicateTargetBytes"] += row["targetSize"]
                row["baseKind"] = b["kind"]
                row["targetKind"] = t["kind"]
                row["baseReason"] = b["reason"]
                row["targetReason"] = t["reason"]
                row["evaluationContentDuplicate"] = same_bytes_as_eval
                row["structuralD3Potential"] = potential
            summaries.append({"family": group["id"], "deployment": group["deployment"],
                              "rid": group["rid"], **dict(local),
                              "addedFiles": len(pair["added"]), "removedFiles": len(pair["removed"]),
                              "identicalFiles": pair["identicalFiles"]})
        record_bytes = b"".join(a2.canon_json(r) for r in sorted(
            member_records, key=lambda r: (r["group"], r["version"], r["path"])))
        pairs_bytes = a1.encode_json({"schema": "chunkshift.patch-dotnet-v2-pairs.v1", "pairs": pairs})
        (stage / "v2-files.jsonl").write_bytes(record_bytes)
        (stage / "v2-pairs.json").write_bytes(pairs_bytes)
        audit = {
            "schema": OUT_SCHEMA, "status": "CALIBRATION_INVENTORY_ONLY_NO_PATCH_DECISION",
            "planSha256": a2.sha256(doc_bytes), "a2EvaluationFilesSha256": doc["parentA2"]["filesSha256"],
            "filesSha256": a2.sha256(record_bytes), "pairsSha256": a2.sha256(pairs_bytes),
            "groups": summaries, "extractions": extracted,
            "sourceAssets": sorted(sources.values(), key=lambda s: s["name"]),
            "totalCalibrationStructuralD3Files": sum(s.get("structuralD3Files", 0) for s in summaries),
            "totalCalibrationStructuralD3TargetBytes": sum(
                s.get("structuralD3TargetBytes", 0) for s in summaries),
            "noPatchBytesMeasured": True, "originalA2HoldoutUnchanged": True,
        }
        (stage / "v2-audit.json").write_bytes(a1.encode_json(audit))
        os.replace(stage, root)
    return audit


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("command", choices=["validate", "materialize"])
    p.add_argument("--plan", type=Path, default=Path(
        "docs/benchmarks/patch-dotnet-001/calibration-extension-v2.v1.json"))
    p.add_argument("--sources", type=Path)
    p.add_argument("--a2-index", type=Path)
    p.add_argument("--root", type=Path)
    a = p.parse_args()
    try:
        raw = a.plan.read_bytes()
        doc = json.loads(raw)
        groups = validate(doc)
        if a.command == "validate":
            print("PATCH-DOTNET-001 V2 CALIBRATION PLAN_SHA256=" + a2.sha256(raw))
            return 0
        if a.root is None or a.sources is None or a.a2_index is None:
            raise a1.CorpusError("V2_REQUIRED_SOURCES_A2_INDEX_ROOT")
        eval_shas = eval_digest_set(a.a2_index, doc["parentA2"]["filesSha256"])
        audit = materialize(doc, raw, groups, a.sources.resolve(), a.root.resolve(), eval_shas)
        print("PATCH-DOTNET-001 V2 STRUCTURAL_ONLY " + json.dumps({
            "candidateFiles": audit["totalCalibrationStructuralD3Files"],
            "candidateTargetBytes": audit["totalCalibrationStructuralD3TargetBytes"],
            "pairsSha256": audit["pairsSha256"]}, sort_keys=True))
        return 0
    except (a1.CorpusError, OSError, KeyError, TypeError, ValueError, json.JSONDecodeError) as err:
        p.error(str(err))


if __name__ == "__main__":
    raise SystemExit(main())
