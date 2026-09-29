#!/usr/bin/env python3
"""Print patch-lab run results (chunkshift.patch-lab.v1) as per-lane totals.

The encoder job of patch-lab.yml prints them into the job log, so a run's
patch bytes and create time per split, and a digest of every patch, can be
read without downloading the artifact (PATCH-ENC-003).

For each lane: files, patch bytes and create seconds of the calibration and
holdout splits, and patchesSha256, the SHA-256 of the sorted lines
"<family> <base>-><target> <path> <patch SHA-256>". Two runs made identical
patches exactly when their digests match.

  print_patch_lab_encoder.py <patch-lab-run.json> [...]
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

SCHEMA = "chunkshift.patch-lab.v1"
REPO = Path(__file__).resolve().parents[2]
CORPUS_MANIFEST = REPO / "docs" / "benchmarks" / "patch-corpus" / "patch-corpus.json"


def splits() -> dict[str, str]:
    manifest = json.loads(CORPUS_MANIFEST.read_text(encoding="utf-8"))
    return {family["id"]: family["split"] for family in manifest["families"]}


def patches_digest(files: list[dict]) -> str:
    lines = sorted(
        f"{item['family']} {item['base']}->{item['target']} {item['path']} {item.get('patchSha256') or '-'}"
        for item in files)
    return hashlib.sha256("".join(line + "\n" for line in lines).encode("utf-8")).hexdigest()


def render(document: dict, families: dict[str, str]) -> list[str]:
    if document.get("schema") != SCHEMA:
        raise ValueError(f"not a {SCHEMA} document: {document.get('schema')!r}")

    totals = {split: {"files": 0, "bytes": 0, "seconds": 0.0} for split in ("calibration", "holdout")}
    for item in document["files"]:
        split = families.get(item["family"])
        if split not in totals:
            raise ValueError(f"family {item['family']!r} has no calibration/holdout split")
        totals[split]["files"] += 1
        totals[split]["bytes"] += item["patchBytes"]
        totals[split]["seconds"] += item["createSeconds"]

    calibration, holdout = totals["calibration"], totals["holdout"]
    return [
        f"runId={document.get('runId')} lane={document['lane']} workers={document['workers']} "
        f"policy={json.dumps(document.get('policy') or {}, sort_keys=True)}",
        f"calibration files={calibration['files']} bytes={calibration['bytes']} "
        f"createSeconds={calibration['seconds']:.2f}",
        f"holdout files={holdout['files']} bytes={holdout['bytes']} "
        f"createSeconds={holdout['seconds']:.2f}",
        f"total bytes={calibration['bytes'] + holdout['bytes']} "
        f"createSeconds={calibration['seconds'] + holdout['seconds']:.2f} "
        f"elapsedSeconds={document['elapsedSeconds']:.1f}",
        f"patchesSha256={patches_digest(document['files'])}",
    ]


def main(arguments: list[str]) -> int:
    if not arguments:
        print(__doc__.strip().splitlines()[-1].strip(), file=sys.stderr)
        return 2
    families = splits()
    for path in arguments:
        document = json.loads(Path(path).read_text(encoding="utf-8"))
        print(f"== {path}")
        print("\n".join(render(document, families)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
