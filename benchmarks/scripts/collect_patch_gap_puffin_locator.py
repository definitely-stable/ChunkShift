#!/usr/bin/env python3
"""Collect pinned Puffin structural-locator evidence for PATCH-GAP-001.

This command is inventory-only. It runs Puffin's puffhuff operation separately
for every base/target file that the committed G5 structural prepass marked as
requiring locator evidence. It never runs puffdiff and never records patch size.

The exact Puffin pin is Android-17.0.0_r1 /
343e23db1b4d81045e91a10244244893f5acd73b.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

SCHEMA = "chunkshift.patch-gap-puffin-locator.v1"
STRUCTURAL_SCHEMA = "chunkshift.patch-gap-g5-structural-prepass.v1"
PROTOCOL_COMMIT = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
CORPUS_PAIRS_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
PUFFIN_VERSION = "Android-17.0.0_r1"
PUFFIN_COMMIT = "343e23db1b4d81045e91a10244244893f5acd73b"
KIND_TO_TYPE = {
    0: "zip",
    1: "gzip",
    2: "zlib",
    "zipCompatible": "zip",
    "gzip": "gzip",
    "zlib": "zlib",
}
EXTENT_RE = re.compile(r"src_deflates_bit:\s*([0-9:,]*)")


class LocatorError(Exception):
    pass


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_sha256(value: str, name: str) -> str:
    value = value.lower()
    if len(value) != 64 or any(ch not in "0123456789abcdef" for ch in value):
        raise LocatorError(f"{name} must be 64 lowercase/uppercase hexadecimal characters")
    return value


def canonical_bytes(value: object) -> bytes:
    return json.dumps(
        value,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=False,
    ).encode("utf-8")


def parse_extent_list(value: str) -> list[dict[str, int]]:
    if value == "":
        return []

    result: list[dict[str, int]] = []
    previous_end = 0
    for item in value.split(","):
        pieces = item.split(":")
        if len(pieces) != 2 or not all(piece.isdecimal() for piece in pieces):
            raise LocatorError(f"invalid Puffin bit extent {item!r}")
        offset, length = map(int, pieces)
        if length <= 0:
            raise LocatorError("Puffin bit extents must have positive length")
        if result and offset < previous_end:
            raise LocatorError("Puffin bit extents must be sorted and non-overlapping")
        end = offset + length
        if end < offset or end > (1 << 64) - 1:
            raise LocatorError("Puffin bit extent overflows UInt64")
        result.append({"bitOffset": offset, "bitLength": length})
        previous_end = end
    return result


def parse_src_deflates_bit(log: str) -> list[dict[str, int]]:
    matches = list(EXTENT_RE.finditer(log))
    if not matches:
        raise LocatorError("Puffin verbose output did not contain src_deflates_bit")
    return parse_extent_list(matches[-1].group(1))


def container_type(kind: object) -> str:
    if kind not in KIND_TO_TYPE:
        raise LocatorError(f"unsupported structural G5 kind {kind!r}")
    return KIND_TO_TYPE[kind]


def corpus_index(corpus: Path) -> tuple[str, dict[tuple[str, str, str, str], dict]]:
    pairs_path = corpus / "pairs.json"
    if not pairs_path.is_file():
        raise LocatorError(f"{pairs_path} does not exist")
    digest = sha256_file(pairs_path)
    if digest != CORPUS_PAIRS_SHA256:
        raise LocatorError(
            f"pairs.json SHA-256 {digest} does not match frozen {CORPUS_PAIRS_SHA256}"
        )

    document = json.loads(pairs_path.read_text(encoding="utf-8"))
    if document.get("schema") != "chunkshift.patch-pairs.v1":
        raise LocatorError("unexpected pairs.json schema")

    result: dict[tuple[str, str, str, str], dict] = {}
    for pair in document.get("pairs", []):
        for changed in pair.get("changed", []):
            key = (pair["family"], pair["base"], pair["target"], changed["path"])
            if key in result:
                raise LocatorError(f"duplicate changed-file identity {key}")
            result[key] = {
                "baseSha256": changed["baseSha256"],
                "targetSha256": changed["targetSha256"],
            }
    return digest, result


def locate_one(
    puffin: Path,
    input_path: Path,
    file_type: str,
    expected_sha256: str,
    work: Path,
) -> dict:
    reconstructed = work / "reconstructed.bin"
    command = [
        str(puffin),
        "--operation=puffhuff",
        f"--src_file={input_path}",
        f"--dst_file={reconstructed}",
        f"--src_file_type={file_type}",
        "--verbose",
    ]
    completed = subprocess.run(
        command,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    log_bytes = completed.stdout + b"\0" + completed.stderr
    log_text = (completed.stdout + b"\n" + completed.stderr).decode(
        "utf-8", errors="replace"
    )
    log_sha = sha256_bytes(log_bytes)

    if completed.returncode != 0:
        return {
            "succeeded": False,
            "detail": f"EXIT_{completed.returncode}",
            "deflateBitExtents": [],
            "reconstructedSha256": None,
            "logSha256": log_sha,
        }

    try:
        extents = parse_src_deflates_bit(log_text)
    except LocatorError as exc:
        return {
            "succeeded": False,
            "detail": f"VERBOSE_PARSE:{exc}",
            "deflateBitExtents": [],
            "reconstructedSha256": None,
            "logSha256": log_sha,
        }

    if not reconstructed.is_file():
        return {
            "succeeded": False,
            "detail": "NO_RECONSTRUCTED_OUTPUT",
            "deflateBitExtents": [],
            "reconstructedSha256": None,
            "logSha256": log_sha,
        }

    actual = sha256_file(reconstructed)
    if actual != expected_sha256.lower():
        return {
            "succeeded": False,
            "detail": f"RECONSTRUCTION_SHA_MISMATCH:{actual}",
            "deflateBitExtents": extents,
            "reconstructedSha256": actual,
            "logSha256": log_sha,
        }

    return {
        "succeeded": True,
        "detail": "OK",
        "deflateBitExtents": extents,
        "reconstructedSha256": actual,
        "logSha256": log_sha,
    }


def help_output(puffin: Path) -> tuple[str, str]:
    completed = subprocess.run(
        [str(puffin), "--help"],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        raise LocatorError(f"puffin --help exited with {completed.returncode}")
    text = (
        completed.stdout.decode("utf-8", errors="replace")
        + "\n---stderr---\n"
        + completed.stderr.decode("utf-8", errors="replace")
    )
    if not text.strip():
        raise LocatorError("puffin --help produced no output")
    return text, sha256_bytes(text.encode("utf-8"))


def load_structural(path: Path, source_commit: str) -> dict:
    raw = path.read_bytes()
    document = json.loads(raw)
    if document.get("schema") != STRUCTURAL_SCHEMA:
        raise LocatorError("unexpected structural-prepass schema")
    if document.get("protocolCommit") != PROTOCOL_COMMIT:
        raise LocatorError("structural prepass has foreign protocol commit")
    if document.get("corpusPairsSha256") != CORPUS_PAIRS_SHA256:
        raise LocatorError("structural prepass has foreign corpus lock")
    if str(document.get("sourceCommit", "")).lower() != source_commit.lower():
        raise LocatorError("structural prepass sourceCommit differs from --source-commit")
    return document


def source_checkout_archive_sha256(source: Path) -> str:
    if not source.is_dir():
        raise LocatorError(f"Puffin source checkout {source} does not exist")
    head = subprocess.run(
        ["git", "-C", str(source), "rev-parse", "HEAD"],
        capture_output=True,
        text=True,
        check=True,
    ).stdout.strip()
    if head != PUFFIN_COMMIT:
        raise LocatorError(
            f"Puffin source HEAD {head} does not match frozen {PUFFIN_COMMIT}"
        )
    dirty = subprocess.run(
        ["git", "-C", str(source), "status", "--porcelain=v1", "--untracked-files=normal"],
        capture_output=True,
        text=True,
        check=True,
    ).stdout
    if dirty:
        raise LocatorError("Puffin source checkout must be clean")
    archive = subprocess.run(
        ["git", "-C", str(source), "archive", "--format=tar", "HEAD"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=True,
    ).stdout
    return sha256_bytes(archive)


def encode_build_provenance(path: Path) -> tuple[str, str]:
    raw = path.read_bytes()
    if not raw:
        raise LocatorError("Puffin build provenance must not be empty")
    return base64.b64encode(raw).decode("ascii"), sha256_bytes(raw)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--structural", type=Path, required=True)
    parser.add_argument("--puffin", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--puffin-source", type=Path, required=True)
    parser.add_argument("--build-provenance", type=Path, required=True)
    parser.add_argument("--build-command", required=True)
    parser.add_argument(
        "--logical-artifact",
        default="tools/puffin/android-17.0.0_r1/puffin",
    )
    args = parser.parse_args()

    try:
        if len(args.source_commit) != 40 or any(
            ch.lower() not in "0123456789abcdef" for ch in args.source_commit
        ):
            raise LocatorError("--source-commit must be a full 40-hex commit")

        source_archive_sha = source_checkout_archive_sha256(args.puffin_source)
        if not args.puffin.is_file():
            raise LocatorError(f"pinned Puffin executable {args.puffin} does not exist")
        if not args.build_provenance.is_file():
            raise LocatorError(
                f"build provenance {args.build_provenance} does not exist"
            )

        build_provenance_base64, build_provenance_sha = encode_build_provenance(
            args.build_provenance
        )
        structural = load_structural(args.structural, args.source_commit)
        pairs_sha, changed = corpus_index(args.corpus)
        rows = []

        for row in structural.get("rows", []):
            if not row.get("puffinLocatorRequired", False):
                continue

            key = (
                row["family"],
                row["baseVersion"],
                row["targetVersion"],
                row["path"],
            )
            if key not in changed:
                raise LocatorError(f"required structural row {key} is absent from pairs.json")
            expected = changed[key]
            typ = container_type(row["base"]["kind"])
            if container_type(row["target"]["kind"]) != typ:
                raise LocatorError(f"required structural row {key} changes container type")

            base_path = (
                args.corpus
                / "tree"
                / row["family"]
                / row["baseVersion"]
                / row["path"]
            )
            target_path = (
                args.corpus
                / "tree"
                / row["family"]
                / row["targetVersion"]
                / row["path"]
            )
            if not base_path.is_file() or not target_path.is_file():
                raise LocatorError(f"materialized file missing for {key}")
            if sha256_file(base_path) != expected["baseSha256"]:
                raise LocatorError(f"base file digest mismatch for {key}")
            if sha256_file(target_path) != expected["targetSha256"]:
                raise LocatorError(f"target file digest mismatch for {key}")

            # Separate directories prevent the second puffhuff from overwriting
            # the first reconstructed output while retaining no large artifacts.
            with tempfile.TemporaryDirectory(prefix="patch-gap-puffin-base-") as temp:
                base_work = Path(temp)
                base = locate_one(
                    args.puffin,
                    base_path,
                    typ,
                    expected["baseSha256"],
                    base_work,
                )
            with tempfile.TemporaryDirectory(prefix="patch-gap-puffin-target-") as temp:
                target_work = Path(temp)
                target = locate_one(
                    args.puffin,
                    target_path,
                    typ,
                    expected["targetSha256"],
                    target_work,
                )

            rows.append(
                {
                    "datasetRole": row["datasetRole"],
                    "family": row["family"],
                    "baseVersion": row["baseVersion"],
                    "targetVersion": row["targetVersion"],
                    "path": row["path"],
                    "type": typ,
                    "baseSha256": expected["baseSha256"],
                    "targetSha256": expected["targetSha256"],
                    "base": base,
                    "target": target,
                }
            )

        rows.sort(
            key=lambda row: (
                row["family"],
                row["baseVersion"],
                row["targetVersion"],
                row["path"],
            )
        )

        help_text, help_sha = help_output(args.puffin)
        logical_artifact = args.logical_artifact.replace("\\", "/")
        if not logical_artifact or logical_artifact.startswith("/"):
            raise LocatorError("--logical-artifact must be a stable relative artifact identity")
        if not args.build_command.strip():
            raise LocatorError("--build-command must not be empty")

        document = {
            "schema": SCHEMA,
            "protocolCommit": PROTOCOL_COMMIT,
            "sourceCommit": args.source_commit.lower(),
            "corpusPairsSha256": pairs_sha,
            "toolVersion": PUFFIN_VERSION,
            "toolCommit": PUFFIN_COMMIT,
            "logicalArtifact": logical_artifact,
            "buildCommand": args.build_command,
            "helpOutput": help_text,
            "executableSha256": sha256_file(args.puffin),
            "executableBytes": args.puffin.stat().st_size,
            "sourceArchiveSha256": source_archive_sha,
            "buildProvenanceBase64": build_provenance_base64,
            "buildProvenanceSha256": build_provenance_sha,
            "helpOutputSha256": help_sha,
            "rows": rows,
        }

        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(canonical_bytes(document))
        return 0
    except (
        LocatorError,
        OSError,
        ValueError,
        json.JSONDecodeError,
        subprocess.CalledProcessError,
    ) as exc:
        print(f"PATCH-GAP Puffin locator failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
