#!/usr/bin/env python3
"""Generate every PATCH-ENC-005 Phase-A lane on a deterministic mini-corpus and
verify the resulting CSP with the independent Python decoder.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path

LANES = (
    "csp",
    "H4-L1-R2",
    "H7-L1-R2-E75",
    "H9-L9-K4-C16-R1M",
    "H9-L12-K4-C16-R1M",
    "H9-L15-K4-C16-R1M",
)
FAMILY = "phase-a-decoder"
BASE = "1"
TARGET = "2"
FILE = "changed.bin"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_corpus(root: Path) -> tuple[str, str]:
    line = (
        b"ChunkShift PATCH-ENC-005 independent decoder fixture: "
        b"stable dictionary material.\n"
    )
    size = 768 * 1024
    base = (line * ((size + len(line) - 1) // len(line)))[:size]
    target = bytearray(base)
    for offset in range(48 * 1024, len(target), 128 * 1024):
        target[offset : offset + 32] = hashlib.sha256(
            f"phase-a-{offset}".encode("ascii")
        ).digest()
    target = bytes(target)

    for version, content in ((BASE, base), (TARGET, target)):
        path = root / "tree" / FAMILY / version / FILE
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    base_sha = sha256(base)
    target_sha = sha256(target)
    pairs = {
        "schema": "chunkshift.patch-pairs.v1",
        "pairs": [
            {
                "family": FAMILY,
                "base": BASE,
                "target": TARGET,
                "changed": [
                    {
                        "path": FILE,
                        "baseSize": len(base),
                        "baseSha256": base_sha,
                        "targetSize": len(target),
                        "targetSha256": target_sha,
                    }
                ],
                "added": [],
                "removed": [],
                "identicalFiles": 0,
                "identicalBytes": 0,
            }
        ],
    }
    (root / "pairs.json").write_text(
        json.dumps(pairs, sort_keys=True, separators=(",", ":")) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return base_sha, target_sha


def run_lane(
    root: Path,
    output_root: Path,
    lane: str,
    project: Path,
) -> tuple[dict, Path]:
    lane_root = output_root / lane
    patches = lane_root / "patches"
    lane_root.mkdir(parents=True)
    result_path = lane_root / "run.json"
    subprocess.run(
        [
            "dotnet",
            "run",
            "--project",
            str(project),
            "-c",
            "Release",
            "--no-build",
            "--",
            "patch-lab",
            "run",
            "--corpus",
            str(root),
            "--lane",
            lane,
            "--output",
            str(result_path),
            "--workers",
            "1",
            "--execution",
            "h2-w2",
            "--no-apply",
            "--patch-dir",
            str(patches),
        ],
        check=True,
    )
    result = json.loads(result_path.read_text(encoding="utf-8"))
    item = result["files"][0]
    return result, patches / item["savedPatch"]


def verify(
    root: Path,
    output_root: Path,
    project: Path,
    decoder: Path,
) -> None:
    base_sha, target_sha = write_corpus(root)
    base_manifest = root / "work" / "csm" / f"{base_sha}.csm"
    base_content = root / "tree" / FAMILY / BASE / FILE
    patch_shas: dict[str, str] = {}

    for lane in LANES:
        result, patch = run_lane(root, output_root, lane, project)
        if result["execution"] != "h2-w2":
            raise RuntimeError(f"{lane}: expected H2-W2")
        item = result["files"][0]
        patch_digest = hashlib.sha256(patch.read_bytes()).hexdigest()
        if patch_digest != item["patchSha256"]:
            raise RuntimeError(f"{lane}: preserved patch SHA mismatch")
        completed = subprocess.run(
            [
                sys.executable,
                str(decoder),
                str(patch),
                "--base-manifest",
                str(base_manifest),
                "--base",
                str(base_content),
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        verdict = json.loads(completed.stdout)
        if verdict.get("verdict") != "valid":
            raise RuntimeError(f"{lane}: independent decoder rejected patch: {verdict}")
        if verdict.get("outputSha256") != target_sha:
            raise RuntimeError(f"{lane}: independent decoder target SHA mismatch")
        patch_shas[lane] = patch_digest

    if patch_shas["H4-L1-R2"] != patch_shas["H7-L1-R2-E75"]:
        raise RuntimeError("H7/H4 byte oracle failed on independent-decoder fixture")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--project",
        type=Path,
        default=Path("benchmarks/ChunkShift.Benchmarks/ChunkShift.Benchmarks.csproj"),
    )
    parser.add_argument(
        "--decoder",
        type=Path,
        default=Path("tools/csp-fixtures/decode.py"),
    )
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="chunkshift-patch-enc-005-decoder-") as temp:
        temp_root = Path(temp)
        verify(
            temp_root / "corpus",
            temp_root / "results",
            args.project,
            args.decoder,
        )
    print("PATCH-ENC-005 Phase-A independent decoder smoke: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
