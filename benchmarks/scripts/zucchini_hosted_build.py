#!/usr/bin/env python3
"""Fail-closed hosted Chromium/Zucchini build preflight and binary provenance.

Research-only tool: it neither generates patch bytes nor changes CSP.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

PIN = Path("docs/research/results/data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json")
TOOL_COMMIT = "ce92a2e156beaaf1f2ed3a651a5c96f55bb78b80"
MIN_DISK_GIB = 100
MIN_MEMORY_GIB = 8
GN_ARGS = "is_debug=false is_component_build=false symbol_level=0"
BUILD_COMMAND = "autoninja -C out/Zucchini components/zucchini:zucchini"
MANIFEST_SCHEMA = "chunkshift.patch-gap-g4-zucchini-build.v1"


def require(value: bool, reason: str) -> None:
    if not value:
        raise ValueError("Zucchini hosted build: " + reason)


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def git(path: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(path), *args], text=True).strip()


def resources(free_disk: int, physical_memory: int, min_disk_gib: int = MIN_DISK_GIB,
              min_memory_gib: int = MIN_MEMORY_GIB) -> dict:
    require(min_disk_gib >= MIN_DISK_GIB and min_memory_gib >= MIN_MEMORY_GIB,
            "resource minima cannot be weakened below the frozen build plan")
    require(free_disk >= 0 and physical_memory >= 0, "negative resource measurement")
    disk_pass = free_disk >= min_disk_gib * 1024**3
    ram_pass = physical_memory >= min_memory_gib * 1024**3
    return {
        "schema": "chunkshift.patch-gap-g4-zucchini-hosted-preflight.v1",
        "freeDiskBytes": free_disk,
        "physicalMemoryBytes": physical_memory,
        "requiredDiskGiB": min_disk_gib,
        "requiredMemoryGiB": min_memory_gib,
        "diskPass": disk_pass,
        "ramPass": ram_pass,
        "canBuild": disk_pass and ram_pass,
        "scope": "resource preflight only; NOT built/benchmarked",
    }


def total_memory() -> int:
    with Path("/proc/meminfo").open(encoding="ascii") as f:
        for line in f:
            if line.startswith("MemTotal:"):
                return int(line.split()[1]) * 1024
    raise ValueError("cannot determine Linux physical memory")


def verify_source(pin: dict, checkout: Path, depot: Path) -> dict:
    require(git(checkout, "rev-parse", "HEAD") == pin["sourceCommit"],
            "incorrect Chromium source commit")
    require(git(checkout, "rev-parse", "HEAD^{tree}") == pin["sourceTreeSha"],
            "incorrect Chromium source tree")
    require(git(checkout, "rev-parse", "HEAD:components") == pin["componentsTreeSha"],
            "incorrect components tree")
    require(git(checkout, "rev-parse", "HEAD:components/zucchini") ==
            pin["componentTreeSha"], "incorrect Zucchini component tree")
    require(git(checkout, "rev-parse", "HEAD:DEPS") == pin["chromiumDepsBlobSha"],
            "incorrect Chromium DEPS")
    require(git(depot, "rev-parse", "HEAD") == TOOL_COMMIT, "incorrect depot_tools checkout")
    return {
        "sourceCommit": pin["sourceCommit"], "sourceTreeSha": pin["sourceTreeSha"],
        "componentsTreeSha": pin["componentsTreeSha"],
        "componentTreeSha": pin["componentTreeSha"],
        "chromiumDepsBlobSha": pin["chromiumDepsBlobSha"],
        "depotToolsCommit": TOOL_COMMIT,
    }


def version(executable: str, *args: str) -> str:
    result = subprocess.run([executable, *args], check=True, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    return result.stdout.strip().splitlines()[0]


def manifest(pin: dict, checkout: Path, depot: Path, binary: Path,
             gn_command: str, ninja_command: str, compiler_command: str,
             compiler_sha256: str = "", dep_snapshot_sha256: str = "") -> dict:
    identity = verify_source(pin, checkout, depot)
    require(binary.is_file() and binary.stat().st_size > 0, "missing compiled Zucchini")
    # The command-line and GN arguments are pinned *exactly*; a different build
    # or a prebuilt substitute is a different research artifact.
    data = {
        "schema": MANIFEST_SCHEMA,
        "chromiumComponentCommit": pin["componentMirrorCommit"],
        "chromiumSourceCommit": pin["sourceCommit"],
        "binarySha256": digest(binary),
        "binaryBytes": binary.stat().st_size,
        "compilerIdentity": compiler_command,
        "compilerBinarySha256": compiler_sha256,
        "dependencySnapshotSha256": dep_snapshot_sha256,
        "buildCommand": BUILD_COMMAND,
        "gnArgs": GN_ARGS,
        "buildSystemIdentity": f"{gn_command}; {ninja_command}",
        "sourceCheckoutProvenance": ";".join(f"{k}={v}" for k,v in sorted(identity.items())),
        "sourceObjects": identity,
        "toolchainProvenanceLimit": (
            "DEPS locks Chromium build dependencies; transient CIPD/toolchain "
            "package hashes must be retained separately before decision evidence"
        ),
        "scope": "built tool identity only, no Zucchini reference/patch-size result",
    }
    require(len(data["binarySha256"]) == 64, "invalid binary digest")
    require(not compiler_sha256 or len(compiler_sha256) == 64, "invalid compiler identity digest")
    require(not dep_snapshot_sha256 or len(dep_snapshot_sha256) == 64, "invalid dependency snapshot digest")
    return data


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--mode", required=True, choices=("preflight", "manifest"))
    p.add_argument("--workspace", type=Path)
    p.add_argument("--checkout", type=Path)
    p.add_argument("--depot", type=Path)
    p.add_argument("--binary", type=Path)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--dependency-snapshot", type=Path)
    args = p.parse_args()
    if args.mode == "preflight":
        require(args.workspace is not None, "preflight workspace is required")
        data = resources(shutil.disk_usage(args.workspace).free, total_memory())
    else:
        require(all((args.checkout, args.depot, args.binary)),
                "source/depot/binary are all required")
        pin = json.loads(PIN.read_text(encoding="utf-8"))
        compiler = args.checkout / "third_party/llvm-build/Release+Asserts/bin/clang"
        require(compiler.is_file(), "Chromium pinned Clang compiler missing")
        require(args.dependency_snapshot is not None and args.dependency_snapshot.is_file(),
                "materialized DEPS dependency snapshot missing")
        data = manifest(pin, args.checkout, args.depot, args.binary,
                        version("gn", "--version"), version("ninja", "--version"),
                        version(str(compiler), "--version"), digest(compiler),
                        digest(args.dependency_snapshot))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n",
                           encoding="utf-8")
    print(json.dumps(data, sort_keys=True))
    if args.mode == "preflight":
        require(data["canBuild"], "RESOURCE_UNAVAILABLE; refusing Chromium download/build")


if __name__ == "__main__":
    main()
