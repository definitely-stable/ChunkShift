#!/usr/bin/env python3
"""Recover canonical PATCH-ENC-005 Phase-A evidence from immutable GitHub artifacts."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

RUN_ID = 37113348655
RUN_NUMBER = 3
RUN_ATTEMPT = 1
EXPERIMENT_ID = "PATCH-ENC-005"
SOURCE_COMMIT = "bb6ab9dc38f9257e5ad699db3fd4fc59a28d21e9"
EVALUATOR_FIX_COMMIT = "3b697ad42a51165c56e570a619e4bdec5de346c4"
EVIDENCE_ID = "PATCH-ENC-005/EVIDENCE-20261003-002"
ARTIFACTS = {
    "patch-enc-005-calibration-attempt-1-linux-x64": (11272472284, "1b55dfc7f0175bbb3e0061bac1c34702262f97f0227fb4ce135ddcda7220e905"),
    "patch-enc-005-calibration-attempt-1-linux-arm64": (11271733705, "e93da7ad178095b8e34bf3c8430fd4886be7a3e049193be0529eb3bd38489836"),
    "patch-enc-005-calibration-attempt-1-win-x64": (11271835903, "eff6109b575e34fa8d3b5c23cba8505cfbe8693d9aba0dd0c049c660699a582e"),
    "patch-enc-005-calibration-attempt-2-linux-x64": (11272182821, "dd3e491c0a90078b9ae54567c2da32ca191bb82a4dce221ba5835b86b35d7233"),
    "patch-enc-005-calibration-attempt-2-linux-arm64": (11272472313, "a06a0464f03b80b3d86568b5b6e409593f331f25083131524d860998dcfc2529"),
    "patch-enc-005-calibration-attempt-2-win-x64": (11272710358, "f437d24b6b2b82da04256784d4d35680be944ad6325bc9959be9f007fcd89690"),
    "patch-enc-005-calibration-memory-linux-x64": (11271012500, "bcef81b01fb951e37260984246aec7766a7f142d84176efa446ec684be1ddbdd"),
    "patch-enc-005-calibration-memory-linux-arm64": (11270443572, "23a65b91ca780367eba3099ab3fdb1a56168d05de53f3b17eabda1a521d9b2ad"),
    "patch-enc-005-calibration-memory-win-x64": (11270813572, "3d4bd077f5eb5e021267b3e53a0c9abb0b627a7f3a73b9973955121ffa278a31"),
    "patch-enc-005-calibration-paired-linux-x64": (11275930015, "4e61bf30a4852ec889df2fdab8146c11f1f69ca491b8aa1a39d62576818d0cc6"),
    "patch-enc-005-calibration-paired-linux-arm64": (11275451452, "e27c36126c766ebce363a29cc8fe49234949d206f772606f5ea8ff1a333bc95f"),
    "patch-enc-005-calibration-paired-win-x64": (11275151803, "08cc63069500d5d867ce1e5eff4ba41e7be8693d9aba0dd0c049c660699a582e"),
    "patch-enc-005-calibration-cross-platform": (11274788491, "b22f9bdeed13eee5bfa359365da4938e2a09e5edf27962dc46cc6548155597ba"),
}
DOCUMENT_SUFFIXES = {".json", ".jsonl", ".log", ".md", ".txt", ".tsv"}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_bytes(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def load_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"{path}: expected JSON object")
    return value


def artifact_documents(directory: Path) -> list[dict]:
    rows = []
    for path in sorted(p for p in directory.rglob("*") if p.is_file()):
        if path.suffix.lower() not in DOCUMENT_SUFFIXES:
            continue
        rows.append({
            "path": path.relative_to(directory).as_posix(),
            "sha256": sha256_file(path),
            "sizeBytes": path.stat().st_size,
        })
    return rows


def build_artifacts(download_root: Path, metadata_path: Path) -> dict:
    metadata = load_json(metadata_path)
    rows_by_name = {row["name"]: row for row in metadata.get("artifacts", [])}
    if set(rows_by_name) != set(ARTIFACTS):
        missing = sorted(set(ARTIFACTS) - set(rows_by_name))
        extra = sorted(set(rows_by_name) - set(ARTIFACTS))
        raise ValueError(f"artifact metadata set mismatch: missing={missing}, extra={extra}")

    artifacts = []
    for name in sorted(ARTIFACTS):
        expected_id, _legacy_digest = ARTIFACTS[name]
        row = rows_by_name[name]
        digest = str(row.get("digest") or "").lower()
        if digest.startswith("sha256:"):
            digest = digest[7:]
        if int(row["id"]) != expected_id:
            raise ValueError(f"{name}: artifact id changed")
        if len(digest) != 64 or any(ch not in "0123456789abcdef" for ch in digest):
            raise ValueError(f"{name}: GitHub artifact SHA-256 is missing or invalid")
        directory = download_root / name
        if not directory.is_dir():
            raise ValueError(f"{name}: downloaded artifact directory missing")
        artifacts.append({
            "id": expected_id,
            "name": name,
            "sizeInBytes": int(row["size_in_bytes"]),
            "createdAt": row["created_at"],
            "expiresAt": row["expires_at"],
            "sha256": digest,
            "documents": artifact_documents(directory),
        })

    return {
        "experimentId": EXPERIMENT_ID,
        "githubRunId": RUN_ID,
        "githubRunNumber": RUN_NUMBER,
        "runAttempt": RUN_ATTEMPT,
        "sourceCommit": SOURCE_COMMIT,
        "evaluatorFixCommit": EVALUATOR_FIX_COMMIT,
        "workflowUrl": f"https://github.com/definitely-stable/ChunkShift/actions/runs/{RUN_ID}",
        "artifacts": artifacts,
    }


def run_evaluator(repo: Path, download_root: Path, output: Path) -> None:
    evaluator = repo / "benchmarks/scripts/evaluate_patch_enc_005_phase_a.py"
    command = [
        sys.executable,
        str(evaluator),
        "--paired", f"linux-x64={download_root / 'patch-enc-005-calibration-paired-linux-x64' / 'paired.json'}",
        "--paired", f"linux-arm64={download_root / 'patch-enc-005-calibration-paired-linux-arm64' / 'paired.json'}",
        "--paired", f"win-x64={download_root / 'patch-enc-005-calibration-paired-win-x64' / 'paired.json'}",
        "--memory", f"linux-x64={download_root / 'patch-enc-005-calibration-memory-linux-x64'}",
        "--memory", f"linux-arm64={download_root / 'patch-enc-005-calibration-memory-linux-arm64'}",
        "--memory", f"win-x64={download_root / 'patch-enc-005-calibration-memory-win-x64'}",
        "--cross-platform", str(download_root / "patch-enc-005-calibration-cross-platform" / "cross-platform.json"),
        "--source-commit", SOURCE_COMMIT,
        "--output", str(output),
    ]
    subprocess.run(command, cwd=repo, check=True)

    verdict = load_json(output / "verdict.json")
    if verdict.get("status") != "REJECT" or verdict.get("finalists") != [] or verdict.get("pareto") != []:
        raise ValueError("recovered frozen Phase-A verdict is not REJECT/no-finalists")
    if int(verdict["lanes"]["H7-L1-R2-E75"].get("byteOracleOk", 1)) != 0:
        raise ValueError("recovered H7 byte oracle unexpectedly passes")


def run_recompute(repo: Path, output: Path) -> None:
    script = output / "recompute.py"
    subprocess.run(
        [
            sys.executable,
            str(script),
            "--compact", str(output / "compact.json"),
            "--expected-verdict", str(output / "verdict.json"),
        ],
        cwd=repo,
        check=True,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--download-root", type=Path, required=True)
    parser.add_argument("--artifact-metadata", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    repo = args.repo.resolve()
    output = args.output.resolve()
    if output.exists():
        shutil.rmtree(output)
    output.parent.mkdir(parents=True, exist_ok=True)

    run_evaluator(repo, args.download_root.resolve(), output)
    run_recompute(repo, output)

    artifacts = build_artifacts(args.download_root.resolve(), args.artifact_metadata.resolve())
    (output / "artifacts.json").write_bytes(canonical_bytes(artifacts) + b"\n")

    verdict = load_json(output / "verdict.json")
    verdict["evidenceId"] = EVIDENCE_ID
    verdict["evaluatorFixCommit"] = EVALUATOR_FIX_COMMIT
    (output / "verdict.json").write_bytes(canonical_bytes(verdict) + b"\n")

    print(f"Recovered {EVIDENCE_ID}: {verdict['status']}, finalists={verdict['finalists']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
