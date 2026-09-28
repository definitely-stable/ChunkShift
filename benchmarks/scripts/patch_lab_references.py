#!/usr/bin/env python3
"""Reference patch lanes for the frozen Patching corpus (PATCH-PREFREEZE-001).

Runs every changed file of <root>/pairs.json through the whole-file and
byte-level reference codecs of docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md
section 2, so the CSP lanes of the lab have a baseline:

  full              no encode/decode; bytes = target size
  full-zstd         zstd -19 -q -c <target>  (decoded with zstd -d -o)
  zstd-patch-from   zstd -19 --long=31 -q --patch-from=<base> <target> -o <out>
  xdelta3           xdelta3 -e -9 -f -s <base> <target> <out>
  bsdiff            bsdiff4.diff / bsdiff4.patch (Python package)

Encode runs once and decode runs --decode-repeats times (the median is kept);
every decoded output is verified against the target SHA-256 of the corpus.
A decode mismatch exits 1, a missing tool exits 2 (never a silent skip).
Files are processed in parallel with --workers.

Usage:
  patch_lab_references.py --corpus ROOT --output result.json
      [--families a,b] [--workers N] [--decode-repeats N] [--run-id ID]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import shutil
import statistics
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

try:
    import bsdiff4
except ImportError as bsdiff4_import_error:  # pragma: no cover - environment check
    bsdiff4 = None
    BSDIFF4_IMPORT_ERROR = bsdiff4_import_error
else:
    BSDIFF4_IMPORT_ERROR = None

SCHEMA = "chunkshift.patch-refs.v1"
CORPUS_SCHEMA = "chunkshift.patch-pairs.v1"

EXIT_OK = 0
EXIT_VERIFICATION = 1
EXIT_MISSING_TOOL = 2


class ToolMissing(Exception):
    """an external tool of the protocol is not installed"""


class VerificationError(Exception):
    """a decoded lane did not reproduce its target"""


class ReferenceError(Exception):
    """a reference tool or the corpus input failed"""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_tool(name: str) -> None:
    if shutil.which(name) is None:
        raise ToolMissing(f"required tool '{name}' is not on PATH")


def tool_version(command: list[str]) -> str:
    completed = subprocess.run(command, capture_output=True, text=True)
    text = (completed.stdout or "").strip() or (completed.stderr or "").strip()
    if completed.returncode != 0 or not text:
        raise ToolMissing(f"{' '.join(command)} did not report a version")
    return text.splitlines()[0].strip()


def detect_tools() -> dict[str, str]:
    require_tool("zstd")
    require_tool("xdelta3")
    if bsdiff4 is None:  # pragma: no cover - environment check
        raise ToolMissing(f"the 'bsdiff4' package is not installed: {BSDIFF4_IMPORT_ERROR}")
    version = getattr(bsdiff4, "__version__", None)
    if not version:
        raise ToolMissing("the 'bsdiff4' package does not report __version__")
    return {
        "zstd": tool_version(["zstd", "--version"]),
        "xdelta3": tool_version(["xdelta3", "-V"]),
        "bsdiff4": str(version),
    }


def run_command(command: list[str]) -> float:
    """Run one command and return its wall time in seconds."""
    started = time.perf_counter()
    completed = subprocess.run(command, capture_output=True, stdin=subprocess.DEVNULL)
    elapsed = time.perf_counter() - started
    if completed.returncode != 0:
        message = completed.stderr.decode("utf-8", errors="replace").strip()
        raise ReferenceError(
            f"{' '.join(command)} failed with exit code {completed.returncode}: {message}"
        )
    return elapsed


def run_command_to_file(command: list[str], output: Path) -> float:
    """Run one command whose stdout is the encoded stream."""
    started = time.perf_counter()
    with output.open("wb") as sink:
        completed = subprocess.run(
            command, stdout=sink, stderr=subprocess.PIPE, stdin=subprocess.DEVNULL
        )
    elapsed = time.perf_counter() - started
    if completed.returncode != 0:
        message = completed.stderr.decode("utf-8", errors="replace").strip()
        raise ReferenceError(
            f"{' '.join(command)} failed with exit code {completed.returncode}: {message}"
        )
    return elapsed


def verify_file(path: Path, target_sha256: str, lane: str) -> None:
    actual = sha256_file(path)
    if actual != target_sha256:
        raise VerificationError(
            f"{lane}: decoded output SHA-256 {actual} does not match target {target_sha256}"
        )


def measure_decode(
    command: list[str], output: Path, target_sha256: str, lane: str, repeats: int
) -> float:
    """Time --decode-repeats decode runs and verify each decoded file."""
    times = []
    for _ in range(repeats):
        # zstd refuses to overwrite an existing -o target without -f; the
        # protocol command stays unchanged, so the result of the previous run
        # is removed instead.
        output.unlink(missing_ok=True)
        times.append(run_command(command))
        verify_file(output, target_sha256, lane)
    return statistics.median(times)


def process_file(job: dict, temp_root: str, repeats: int) -> dict:
    work = Path(tempfile.mkdtemp(prefix="file-", dir=temp_root))
    try:
        base = Path(job["basePath"])
        target = Path(job["targetPath"])
        target_size = int(job["targetSize"])
        target_sha256 = job["targetSha256"]
        lanes: dict[str, dict[str, float]] = {}

        # Whole-file delivery: no encode, no decode, the bytes are the target.
        lanes["full"] = {"bytes": target_size, "encodeSeconds": 0.0, "decodeSeconds": 0.0}

        frame = work / "full.zst"
        encode_seconds = run_command_to_file(["zstd", "-19", "-q", "-c", str(target)], frame)
        decoded = work / "full.dec"
        decode_seconds = measure_decode(
            ["zstd", "-d", "-q", str(frame), "-o", str(decoded)], decoded, target_sha256, "full-zstd", repeats
        )
        lanes["full-zstd"] = {
            "bytes": frame.stat().st_size,
            "encodeSeconds": encode_seconds,
            "decodeSeconds": decode_seconds,
        }

        frame = work / "patch-from.zst"
        encode_seconds = run_command(
            ["zstd", "-19", "--long=31", "-q", f"--patch-from={base}", str(target), "-o", str(frame)]
        )
        decoded = work / "patch-from.dec"
        decode_seconds = measure_decode(
            ["zstd", "-d", "--long=31", "-q", f"--patch-from={base}", str(frame), "-o", str(decoded)],
            decoded,
            target_sha256,
            "zstd-patch-from",
            repeats,
        )
        lanes["zstd-patch-from"] = {
            "bytes": frame.stat().st_size,
            "encodeSeconds": encode_seconds,
            "decodeSeconds": decode_seconds,
        }

        frame = work / "delta.xdz"
        encode_seconds = run_command(
            ["xdelta3", "-e", "-9", "-f", "-s", str(base), str(target), str(frame)]
        )
        decoded = work / "delta.dec"
        decode_seconds = measure_decode(
            ["xdelta3", "-d", "-f", "-s", str(base), str(frame), str(decoded)],
            decoded,
            target_sha256,
            "xdelta3",
            repeats,
        )
        lanes["xdelta3"] = {
            "bytes": frame.stat().st_size,
            "encodeSeconds": encode_seconds,
            "decodeSeconds": decode_seconds,
        }

        base_bytes = base.read_bytes()
        target_bytes = target.read_bytes()
        started = time.perf_counter()
        patch = bsdiff4.diff(base_bytes, target_bytes)
        encode_seconds = time.perf_counter() - started
        times = []
        for _ in range(repeats):
            started = time.perf_counter()
            decoded_bytes = bsdiff4.patch(base_bytes, patch)
            times.append(time.perf_counter() - started)
            actual = hashlib.sha256(decoded_bytes).hexdigest()
            if actual != target_sha256:
                raise VerificationError(
                    f"bsdiff: decoded output SHA-256 {actual} does not match target {target_sha256}"
                )
        lanes["bsdiff"] = {
            "bytes": len(patch),
            "encodeSeconds": encode_seconds,
            "decodeSeconds": statistics.median(times),
        }

        return {
            "family": job["family"],
            "base": job["base"],
            "target": job["target"],
            "path": job["path"],
            "targetSize": target_size,
            "lanes": lanes,
        }
    finally:
        shutil.rmtree(work, ignore_errors=True)


def load_jobs(root: Path, families: set[str] | None) -> tuple[str, list[dict]]:
    pairs_path = root / "pairs.json"
    if not pairs_path.is_file():
        raise ReferenceError(f"{pairs_path} does not exist; materialize the corpus first")
    document = json.loads(pairs_path.read_text(encoding="utf-8"))
    if document.get("schema") != CORPUS_SCHEMA:
        raise ReferenceError(f"{pairs_path}: unexpected schema {document.get('schema')!r}")

    jobs = []
    seen_families: set[str] = set()
    for pair in document.get("pairs", []):
        family = pair["family"]
        seen_families.add(family)
        if families is not None and family not in families:
            continue
        for changed in pair.get("changed", []):
            target_sha256 = changed.get("targetSha256")
            if not target_sha256:
                raise ReferenceError(f"{family} {changed.get('path')}: no targetSha256 in pairs.json")
            base_path = root / "tree" / family / pair["base"] / changed["path"]
            target_path = root / "tree" / family / pair["target"] / changed["path"]
            if not base_path.is_file() or not target_path.is_file():
                raise ReferenceError(
                    f"{family} {changed['path']}: materialized base or target file is missing"
                )
            jobs.append(
                {
                    "family": family,
                    "base": pair["base"],
                    "target": pair["target"],
                    "path": changed["path"],
                    "basePath": str(base_path),
                    "targetPath": str(target_path),
                    "targetSize": int(changed["targetSize"]),
                    "targetSha256": target_sha256,
                }
            )

    unknown = sorted((families or set()) - seen_families)
    if unknown:
        raise ReferenceError(f"unknown families requested: {', '.join(unknown)}")
    return sha256_file(pairs_path), jobs


def default_run_id() -> str:
    commit = os.environ.get("GITHUB_SHA", "").strip()
    short_commit = commit[:7] if commit else "local"
    stamp = time.strftime("%Y%m%d", time.gmtime())
    machine = platform.machine().lower() or "unknown"
    return f"PATCH-PREFREEZE-001/RUN-{stamp}-local-{short_commit}-{machine}"


def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--corpus", required=True, type=Path, help="corpus root with pairs.json and tree/")
    parser.add_argument("--output", required=True, type=Path, help="result JSON")
    parser.add_argument("--families", help="comma list of families (default: all)")
    parser.add_argument("--workers", type=int, default=1, help="files processed in parallel")
    parser.add_argument("--decode-repeats", type=int, default=3, help="decode runs per file")
    parser.add_argument("--run-id", help="RunId recorded in the result")
    args = parser.parse_args(argv)
    if args.workers < 1:
        parser.error("--workers must be at least 1")
    if args.decode_repeats < 1:
        parser.error("--decode-repeats must be at least 1")
    if args.families is not None:
        args.families = [name.strip() for name in args.families.split(",") if name.strip()]
        if not args.families:
            parser.error("--families must name at least one family")
    return args


def build_document(
    args: argparse.Namespace, tools: dict[str, str], pairs_sha256: str, files: list[dict]
) -> dict:
    return {
        "schema": SCHEMA,
        "runId": args.run_id or default_run_id(),
        "corpusPairsSha256": pairs_sha256,
        "environment": {
            "osDescription": platform.platform(),
            "osArchitecture": platform.machine(),
            "processorCount": os.cpu_count(),
            "pythonVersion": platform.python_version(),
            "gitCommit": os.environ.get("GITHUB_SHA") or None,
        },
        "tools": tools,
        "workers": args.workers,
        "decodeRepeats": args.decode_repeats,
        "files": files,
    }


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        tools = detect_tools()
        pairs_sha256, jobs = load_jobs(args.corpus, set(args.families) if args.families else None)
    except ToolMissing as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_MISSING_TOOL
    except ReferenceError as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_VERIFICATION

    temp_root = Path(tempfile.mkdtemp(prefix="chunkshift-patch-refs-"))
    try:
        if args.workers == 1:
            files = [process_file(job, str(temp_root), args.decode_repeats) for job in jobs]
        else:
            with ProcessPoolExecutor(max_workers=args.workers) as pool:
                files = list(
                    pool.map(
                        process_file,
                        jobs,
                        [str(temp_root)] * len(jobs),
                        [args.decode_repeats] * len(jobs),
                        chunksize=1,
                    )
                )
    except (VerificationError, ReferenceError) as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_VERIFICATION
    finally:
        shutil.rmtree(temp_root, ignore_errors=True)

    document = build_document(args, tools, pairs_sha256, files)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=1) + "\n", encoding="utf-8")
    families = len({file["family"] for file in files})
    print(f"{len(files)} changed files in {families} families -> {args.output}")
    return EXIT_OK


if __name__ == "__main__":
    raise SystemExit(main())
