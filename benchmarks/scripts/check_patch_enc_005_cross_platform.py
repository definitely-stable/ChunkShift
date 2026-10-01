#!/usr/bin/env python3
"""Validate PATCH-ENC-005 Phase-A patch-byte determinism across three runners."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

PLATFORMS = {"linux-x64", "linux-arm64", "win-x64"}
RUN_ID_RE = re.compile(
    r"^PATCH-ENC-005/RUN-(\d{8})-(\d{3})-([0-9a-f]{40})-"
    r"(linux-x64|linux-arm64|win-x64)$"
)


def load_documents(root: Path) -> list[tuple[Path, dict]]:
    documents = []
    for path in sorted(root.rglob("paired.json")):
        documents.append((path, json.loads(path.read_text(encoding="utf-8"))))
    if len(documents) != 3:
        raise ValueError(f"expected exactly three paired.json documents, found {len(documents)}")
    return documents


def canonical_patch_map(document: dict) -> str:
    value = document.get("acceptedPatchShas")
    if value is None:
        raise ValueError("paired evidence has no acceptedPatchShas")
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def validate(documents: list[tuple[Path, dict]]) -> dict:
    platforms: dict[str, tuple[Path, dict]] = {}
    identity: tuple[str, str, str, str, str] | None = None
    patch_map: str | None = None

    for path, document in documents:
        if document.get("schema") != "chunkshift.patch-enc-005-paired.v2":
            raise ValueError(f"{path}: unexpected schema")
        if document.get("status") != "VALID":
            raise ValueError(f"{path}: platform evidence is not VALID")
        platform = document.get("platform")
        if platform not in PLATFORMS or platform in platforms:
            raise ValueError(f"{path}: invalid/duplicate platform {platform!r}")
        match = RUN_ID_RE.fullmatch(str(document.get("runId", "")))
        if match is None or match.group(4) != platform:
            raise ValueError(f"{path}: RunId/platform mismatch")
        date, sequence, embedded_commit, _ = match.groups()
        source_commit = str(document.get("sourceCommit", ""))
        if embedded_commit != source_commit:
            raise ValueError(f"{path}: RunId/source commit mismatch")
        current_identity = (
            date,
            sequence,
            source_commit,
            str(document.get("protocolCommit", "")),
            str(document.get("datasetRole", "")) + ":" + str(document.get("datasetSha256", "")),
            str(document.get("githubRunId", "")),
            str(document.get("githubRunNumber", "")),
            str(document.get("githubRunAttempt", "")),
        )
        if identity is None:
            identity = current_identity
        elif current_identity != identity:
            raise ValueError(f"{path}: evidence identity differs across platforms")

        current_map = canonical_patch_map(document)
        if patch_map is None:
            patch_map = current_map
        elif current_map != patch_map:
            raise ValueError(f"{path}: per-file patch SHA map differs across platforms")
        platforms[platform] = (path, document)

    if set(platforms) != PLATFORMS:
        raise ValueError(f"platform set mismatch: {sorted(platforms)}")

    assert identity is not None and patch_map is not None
    return {
        "schema": "chunkshift.patch-enc-005-cross-platform.v1",
        "experimentId": "PATCH-ENC-005",
        "date": identity[0],
        "sequence": identity[1],
        "sourceCommit": identity[2],
        "protocolCommit": identity[3],
        "dataset": identity[4],
        "githubRunId": identity[5],
        "githubRunNumber": identity[6],
        "githubRunAttempt": identity[7],
        "platforms": sorted(platforms),
        "patchBytesEqual": True,
        "acceptedPatchShas": json.loads(patch_map),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = validate(load_documents(args.input))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(result, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
