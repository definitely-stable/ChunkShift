"""Bounded, exact-input evidence for CSP differential comparators.

Only test-generated inputs; never inspect arbitrary paths or publish product data.
Each comparator retains at most five bundles (16 MiB of binary input each).
A retention failure must not turn a differential failure into a pass.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import platform
import sys
import traceback
from compression import zstd

MAX_BUNDLES = 5
MAX_INPUT_BYTES = 16 * 1024 * 1024
MAX_DIAGNOSTIC_CHARS = 16384
KINDS = {"decode", "forward", "frames"}
INPUT_NAMES = {"case.csp", "base.csm", "base.bin", "frame.zst", "dictionary.bin"}


class FailureBundles:
    def __init__(self, directory: Path, kind: str):
        if kind not in KINDS:
            raise ValueError("unknown comparator")
        self.root = directory / "failures" / ("python-" + kind)
        self.kind = kind
        self.retained = 0

    def record(self, listing: Path, line_number: int, case: dict, inputs: dict[str, bytes | None],
               expected: str, actual: str, reason: str = "", forward_seed: int | None = None) -> bool:
        if self.retained >= MAX_BUNDLES:
            return False
        # Never derive an output path from an untrusted JSONL filename.
        # A bounded scan also prevents repeated invocation from adding more
        # than MAX_BUNDLES in the same comparator's output directory.
        try:
            self.root.mkdir(parents=True, exist_ok=True)
            slots = [self.root / f"{i:02d}" for i in range(1, MAX_BUNDLES + 1)]
            slot = next((p for p in slots if not p.exists()), None)
            if slot is None:
                return False
            if set(inputs) - INPUT_NAMES:
                raise ValueError("unexpected input role")
            total = sum(len(data) for data in inputs.values() if data is not None)
            if total > MAX_INPUT_BYTES:
                raise ValueError(f"binary repro exceeds cap ({total} bytes)")
            # Build metadata before writing. Record null inputs distinctly
            # from present zero-length files.
            files = {name: (None if data is None else
                            {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
                     for name, data in inputs.items()}
            metadata = {
                "schema": "csp-differential-reproducer-v1",
                "comparator": self.kind,
                "listing": listing.name,
                "line": line_number,
                "case": case,
                "expected": expected,
                "actual": actual,
                "reason": reason[:MAX_DIAGNOSTIC_CHARS],
                "forwardSeed": forward_seed,
                "inputs": files,
                "binaryBytes": total,
                "python": sys.version,
                "zstd": zstd.zstd_version_info,
                "platform": platform.platform(),
                "githubSha": os.getenv("GITHUB_SHA"),
                "githubRunId": os.getenv("GITHUB_RUN_ID"),
                "githubRunAttempt": os.getenv("GITHUB_RUN_ATTEMPT"),
                "fuzzSeeds": os.getenv("CHUNKSHIFT_FUZZ_SEEDS"),
            }
            encoded = json.dumps(metadata, indent=2, sort_keys=True, ensure_ascii=True).encode("utf-8")
            if len(encoded) > 128 * 1024:
                raise ValueError("metadata exceeds cap")
            slot.mkdir(exist_ok=False)
            self.retained += 1
            for name, data in inputs.items():
                if data is not None:
                    with (slot / name).open("xb") as out:
                        out.write(data)
            # Written last: absence of failure.json means incomplete evidence.
            with (slot / "failure.json").open("xb") as out:
                out.write(encoded)
            print(f"retained differential reproducer: {slot}", file=sys.stderr)
            return True
        except (OSError, ValueError, TypeError) as error:
            print(f"ERROR retaining {self.kind} reproducer: {error}", file=sys.stderr)
            return False


def exception_reason(error: Exception) -> str:
    return "".join(traceback.format_exception(error))[-MAX_DIAGNOSTIC_CHARS:]
