#!/usr/bin/env python3
"""Frozen PATCH-GAP-001 HDiffPatch whole-file reference (not a CSP factor)."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import platform
import subprocess
import sys
import tempfile
import time
import zipfile
from pathlib import Path

PAIRS_SHA = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
SOURCE_COMMIT = "3b9dca715ca492873bf2c49e22e5d5b7d2a78620"
ARCHIVE_SHA = "628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558"
SCHEMA = "chunkshift.patch-gap-hdiff-reference.v1"
LANES = {"memory": "-m-4", "stream": "-s-64"}

class EvidenceError(ValueError):
    pass

def sha(path: Path) -> str:
    d = hashlib.sha256()
    with path.open("rb") as f:
        for part in iter(lambda: f.read(1024 * 1024), b""):
            d.update(part)
    return d.hexdigest()

def require(flag: bool, msg: str) -> None:
    if not flag:
        raise EvidenceError(msg)

def _exec(args: list[str], timeout: int) -> float:
    start = time.perf_counter()
    outcome = subprocess.run(args, capture_output=True, timeout=timeout, check=False,
                             stdin=subprocess.DEVNULL)
    if outcome.returncode:
        raise EvidenceError(
            f"{Path(args[0]).name} exit={outcome.returncode}; "
            f"stderr={outcome.stderr[:1200].decode('utf-8', errors='replace')}")
    return time.perf_counter() - start

def pair_jobs(corpus: Path, role: str) -> list[dict]:
    pairs = corpus / "pairs.json"
    require(pairs.is_file() and sha(pairs) == PAIRS_SHA,
            "Frozen corpus pairs.json SHA-256 mismatch")
    data = json.loads(pairs.read_text(encoding="utf-8"))
    require(data["schema"] == "chunkshift.patch-pairs.v1", "Unexpected pairs schema")
    # Frozen roles are independent of observed savings.
    role_families = {
        "calibration": {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64"},
        "evaluation": {"node-linux-x64", "node-win-x64", "tzdata", "chunkshift-source"},
    }
    jobs = []
    for pair in data["pairs"]:
        family = pair["family"]
        if family not in role_families[role]:
            continue
        for item in pair["changed"]:
            relative = item["path"]
            rel = Path(relative)
            require(relative and not rel.is_absolute()
                    and all(part not in ("..", ".") for part in rel.parts)
                    and "\\" not in relative, "Unsafe frozen relative file path")
            old = corpus / "tree" / family / pair["base"] / rel
            new = corpus / "tree" / family / pair["target"] / rel
            require(old.is_file() and new.is_file(), "Frozen corpus pair missing")
            require(old.stat().st_size == item["baseSize"]
                    and sha(old) == item["baseSha256"],
                    "Frozen base identity mismatch: " + relative)
            require(new.stat().st_size == item["targetSize"]
                    and sha(new) == item["targetSha256"],
                    "Frozen target identity mismatch: " + relative)
            jobs.append({"family": family, "base": pair["base"],
                         "target": pair["target"], "path": relative,
                         "old": str(old), "new": str(new),
                         "baseSha256": item["baseSha256"],
                         "baseBytes": item["baseSize"],
                         "targetSha256": item["targetSha256"],
                         "targetBytes": item["targetSize"]})
    expected = 1049 if role == "calibration" else 844
    require(len(jobs) == expected, f"Frozen role count mismatch: {len(jobs)} vs {expected}")
    return jobs

def run_pair(old: Path, new: Path, hdiffz: Path, hpatchz: Path,
             output_dir: Path, timeout: int = 600) -> dict:
    require(old.is_file() and new.is_file(), "Missing input file")
    require(hdiffz.is_file() and hpatchz.is_file(), "Missing binary")
    old_sha, new_sha = sha(old), sha(new)
    output_dir.mkdir(parents=True, exist_ok=True)
    outcome = {}
    for lane, match_mode in LANES.items():
        patch = output_dir / f"{lane}.hdiff"
        reconstructed = output_dir / f"{lane}.reconstructed"
        require(not patch.exists() and not reconstructed.exists(),
                "Refuse to reuse preexisting reference artifact")
        create_cmd = [str(hdiffz), match_mode, "-SD", "-d", "-f",
                      "-p-1", "-c-zstd-21-24", str(old), str(new), str(patch)]
        apply_cmd = [str(hpatchz), "-s-8m", "-f", str(old), str(patch),
                     str(reconstructed)]
        elapsed_create = _exec(create_cmd, timeout)
        require(patch.is_file(), "hdiffz did not produce patch")
        elapsed_apply = _exec(apply_cmd, timeout)
        require(reconstructed.is_file() and reconstructed.stat().st_size == new.stat().st_size
                and sha(reconstructed) == new_sha, "HDiffPatch reconstructed target mismatch")
        outcome[lane] = {
            "patchBytes": patch.stat().st_size,
            "patchSha256": sha(patch),
            "createSeconds": elapsed_create,
            "applySeconds": elapsed_apply,
            "exactReconstruction": True,
            "matchMode": match_mode,
            "applyCache": "-s-8m",
        }
    return {"oldSha256": old_sha, "targetSha256": new_sha,
            "targetBytes": new.stat().st_size, "lanes": outcome}

def verify_binary_archive(archive: Path, hdiffz: Path, hpatchz: Path) -> dict:
    """Bind both executables to exact bytes in the SHA-locked original ZIP."""
    require(sha(archive) == ARCHIVE_SHA, "HDiffPatch Linux64 release archive SHA mismatch")
    digest_by_name = {}
    with zipfile.ZipFile(archive) as source:
        for name in source.namelist():
            base = Path(name).name
            if base not in ("hdiffz", "hpatchz"):
                continue
            require(base not in digest_by_name, "Duplicate official binary name: " + base)
            digest_by_name[base] = hashlib.sha256(source.read(name)).hexdigest()
    require(set(digest_by_name) == {"hdiffz", "hpatchz"},
            "Official ZIP must contain exactly one of each binary")
    actual = {"hdiffz": sha(hdiffz), "hpatchz": sha(hpatchz)}
    require(actual == digest_by_name,
            "Supplied executable does not match SHA-pinned original ZIP member")
    return actual


def run_smoke(root: Path, hdiffz: Path, hpatchz: Path) -> dict:
    root.mkdir(parents=True, exist_ok=True)
    # Deterministic binary fixture; not decision evidence.
    old = root / "old.bin"
    target = root / "target.bin"
    require(not old.exists() and not target.exists(), "Smoke workspace must be empty")
    old.write_bytes(hashlib.sha256(b"HDIFF-POSITIVE").digest() * 8192)
    data = bytearray(old.read_bytes())
    data[3071:3121] = b"B" * 50
    data.extend(b"append")
    target.write_bytes(data)
    return run_pair(old, target, hdiffz, hpatchz, root / "patches", 60)

def main(argv=None) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--mode", choices=("smoke", "measure"), required=True)
    p.add_argument("--hdiffz", type=Path, required=True)
    p.add_argument("--hpatchz", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--work", type=Path, required=True)
    p.add_argument("--archive", type=Path, required=True,
                   help="Pinned original release ZIP, SHA-256 rechecked before any binary use")
    p.add_argument("--corpus", type=Path)
    p.add_argument("--role", choices=("calibration", "evaluation"))
    p.add_argument("--run-id", default="SMOKE_NOT_DECISION")
    args = p.parse_args(argv)

    hdiffz, hpatchz = args.hdiffz.resolve(), args.hpatchz.resolve()
    binaries = verify_binary_archive(args.archive, hdiffz, hpatchz)
    if args.mode == "smoke":
        require(not args.corpus and not args.role, "Smoke must not consume frozen corpus")
        result = run_smoke(args.work, hdiffz, hpatchz)
        data = {"schema": SCHEMA, "status": "REAL_BINARY_SMOKE_ONLY",
                "rfcSizeGate": "NOT_APPLICABLE", "role": None,
                "runId": args.run_id, "positiveFixture": result}
    else:
        require(args.corpus is not None and args.role is not None,
                "Frozen corpus/role are mandatory for a real reference")
        require(args.run_id != "SMOKE_NOT_DECISION", "Real run requires an explicit RunId")
        jobs = pair_jobs(args.corpus, args.role)
        results = []
        for index, job in enumerate(jobs):
            with tempfile.TemporaryDirectory(prefix="patch-gap-hdiff-", dir=args.work) as tmp:
                measured = run_pair(Path(job["old"]), Path(job["new"]),
                                    hdiffz, hpatchz, Path(tmp))
            results.append({k: v for k, v in job.items() if k not in ("old", "new")}
                           | measured)
            if (index + 1) % 50 == 0:
                print(f"HDIFF_REFERENCE_PROGRESS {index + 1}/{len(jobs)}", flush=True)
        data = {"schema": SCHEMA, "status": "ORIGINAL_REFERENCE_MEASURED",
                "rfcSizeGate": "NOT_APPLICABLE", "role": args.role,
                "runId": args.run_id, "pairsSha256": PAIRS_SHA,
                "files": results,
                "totals": {lane: sum(row["lanes"][lane]["patchBytes"] for row in results)
                           for lane in LANES}}
    data.update({"protocolCommit": PROTOCOL, "hdiffpatchSourceCommit": SOURCE_COMMIT,
                 "archiveSha256": ARCHIVE_SHA, "binarySha256": binaries,
                 "environment": {"platform": platform.platform(),
                                 "architecture": platform.machine(),
                                 "sourceGitCommit": os.environ.get("GITHUB_SHA")}})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, sort_keys=True, indent=2) + "\n",
                           encoding="utf-8")
    print("HDIFF_REFERENCE_STATUS", data["status"])
    return 0

if __name__ == "__main__":
    try:
        sys.exit(main())
    except (EvidenceError, OSError, subprocess.TimeoutExpired) as exc:
        sys.exit(f"PATCH-GAP HDiffPatch FAIL_CLOSED: {exc}")
