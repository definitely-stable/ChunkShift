#!/usr/bin/env python3
"""PATCH-TREE-001 T4-C0: disposable REAL filesystem single-head replacement probe.

A research measurement, NOT a production publisher or portable durability proof.
Runs only under a newly allocated private temporary directory; no project files
or user installation are modified. Python os.replace is the measured primitive,
not .NET File.Move and not Windows ReplaceFileW.
"""
from __future__ import annotations

import argparse
import hashlib
import os
import pathlib
import platform
import struct
import subprocess
import sys
import tempfile
import threading
import time
from typing import Any

MAGIC = b"T4C0v0!\n"
BODY_BYTES = 8 + 8 + 8 + 32
HEAD_BYTES = BODY_BYTES + 32


def digest_of(a: bytes, b: bytes) -> str:
    return hashlib.sha256(b"chunkshift.t4c0.disposable-tree.v0\0"
                          + struct.pack("<I", len(a)) + a
                          + struct.pack("<I", len(b)) + b).hexdigest()


def encode_head(epoch: int, sequence: int, digest: str) -> bytes:
    if (epoch < 0 or sequence < 0 or epoch >= 2**64 or sequence >= 2**64 or
            len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest)):
        raise ValueError("noncanonical head identity/cursor")
    data = MAGIC + struct.pack("<QQ", epoch, sequence) + bytes.fromhex(digest)
    assert len(data) == BODY_BYTES
    return data + hashlib.sha256(data).digest()


def decode_head(data: bytes) -> tuple[int, int, str]:
    if (len(data) != HEAD_BYTES or data[:8] != MAGIC or
            hashlib.sha256(data[:BODY_BYTES]).digest() != data[BODY_BYTES:]):
        raise ValueError("bad, incomplete or torn pointer")
    epoch, sequence = struct.unpack("<QQ", data[8:24])
    return epoch, sequence, data[24:56].hex()


def write_synced(path: pathlib.Path, content: bytes) -> None:
    with path.open("xb") as writer:
        writer.write(content)
        writer.flush()
        os.fsync(writer.fileno())


def generation(root: pathlib.Path, tag: str) -> str:
    a, b = ((tag + "/a\n").encode() * 64, (tag + "/b\n").encode() * 64)
    digest = digest_of(a, b)
    directory = root / "generations" / digest
    directory.mkdir(parents=True, exist_ok=False)
    write_synced(directory / "a", a)
    write_synced(directory / "b", b)
    return digest


def verify_generation(root: pathlib.Path, digest: str) -> None:
    gen = root / "generations" / digest
    a, b = gen / "a", gen / "b"
    # These checks are safe only in this owned, disposable test directory.
    # They are NOT a handle-relative no-follow walk against hostile inputs.
    if not gen.is_dir() or gen.is_symlink() or any(
            not p.is_file() or p.is_symlink() for p in (a, b)):
        raise AssertionError("missing, linked or invalid generation")
    if digest_of(a.read_bytes(), b.read_bytes()) != digest:
        raise AssertionError("generation changed or mixed content")


def directory_sync(directory: pathlib.Path) -> str:
    """Record an actual POSIX fsync(directory) attempt; never claim Windows."""
    if os.name == "nt":
        return "NOT_ATTEMPTED_WIN32_NO_PORTABLE_DIRECTORY_FSYNC"
    flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0)
    try:
        descriptor = os.open(directory, flags)
    except OSError as exc:
        return f"UNAVAILABLE_OPEN_ERRNO_{exc.errno}"
    try:
        os.fsync(descriptor)
        return "POSIX_DIRECTORY_FSYNC_SYSCALL_SUCCEEDED"
    except OSError as exc:
        return f"UNAVAILABLE_FSYNC_ERRNO_{exc.errno}"
    finally:
        os.close(descriptor)


def setup(root: pathlib.Path) -> tuple[str, str, str]:
    root.mkdir(parents=True, exist_ok=False)
    old = generation(root, "old")
    new = generation(root, "new")
    verify_generation(root, old)
    verify_generation(root, new)
    write_synced(root / "active", encode_head(0, 1, old))
    sync_result = directory_sync(root)
    return old, new, sync_result


def visible(root: pathlib.Path) -> tuple[int, int, str]:
    epoch, sequence, digest = decode_head((root / "active").read_bytes())
    verify_generation(root, digest)
    return epoch, sequence, digest


def promote(root: pathlib.Path, digest: str, sequence: int,
            count_retries: list[int]) -> None:
    verify_generation(root, digest)
    stage = root / f".next-{sequence}"
    write_synced(stage, encode_head(0, sequence, digest))
    # Source + destination guaranteed siblings on one filesystem.
    for attempt in range(201):
        try:
            os.replace(stage, root / "active")
            count_retries[0] += attempt
            return
        except PermissionError:
            # Windows reader sharing can refuse replacement; record all retries
            # instead of mislabeling a retried publication as a single atomic op.
            if attempt == 200:
                raise
            time.sleep(0.002)
    raise AssertionError("unreachable")


def kill_worker(root: pathlib.Path, phase: str, digest: str) -> None:
    verify_generation(root, digest)
    stage = root / ".child-staged"
    write_synced(stage, encode_head(0, 2, digest))
    if phase == "after":
        os.replace(stage, root / "active")
        directory_sync(root)  # attempt recorded separately by the parent
    (root / f"worker-{phase}.ready").write_text("ready\n", encoding="ascii")
    while True:
        time.sleep(1)


def kill_case(case_root: pathlib.Path, phase: str) -> dict[str, Any]:
    old, new, sync_result = setup(case_root)
    proc = subprocess.Popen(
        [sys.executable, str(pathlib.Path(__file__).resolve()), "--worker",
         "--root", str(case_root), "--phase", phase, "--digest", new],
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
        stderr=subprocess.PIPE, text=True)
    try:
        deadline = time.monotonic() + 18
        marker = case_root / f"worker-{phase}.ready"
        while time.monotonic() < deadline and not marker.exists():
            if proc.poll() is not None:
                raise AssertionError("worker exited before marker: " +
                                     (proc.stderr.read() if proc.stderr else ""))
            time.sleep(0.01)
        if not marker.is_file():
            raise AssertionError("worker never reached fault injection marker")
        proc.kill()  # POSIX SIGKILL or Windows TerminateProcess
        proc.wait(timeout=8)
        expected = old if phase == "before" else new
        if visible(case_root) != (0, 1 if phase == "before" else 2, expected):
            raise AssertionError("active selection changed across process termination")
        if not (case_root / "generations" / old).is_dir():
            raise AssertionError("old generation lost while still potentially pinned")
        if not (case_root / "generations" / new).is_dir():
            raise AssertionError("new generation lost")
        # A prepared-but-not-promoted staging file is not a published pointer.
        if phase == "before" and not (case_root / ".child-staged").is_file():
            raise AssertionError("uncommitted stage unexpectedly disappeared")
        return {"phase": phase, "observed": expected, "killExitCode": proc.returncode,
                "directorySyncCapability": sync_result}
    finally:
        if proc.poll() is None:
            proc.kill()
            proc.wait(timeout=8)
        if proc.stderr:
            proc.stderr.close()


def concurrent_case(root: pathlib.Path) -> dict[str, Any]:
    old, new, sync_result = setup(root)
    stop = threading.Event()
    errors: list[str] = []
    counts = [0] * 4
    retry_count = [0]

    def reader(i: int) -> None:
        while not stop.is_set() and not errors:
            try:
                epoch, sequence, identity = visible(root)
                if epoch != 0 or sequence < 1 or identity not in (old, new):
                    raise AssertionError("invalid selected generation/cursor")
                counts[i] += 1
            except (OSError, ValueError, AssertionError) as exc:
                errors.append(f"reader-{i}:{type(exc).__name__}:{exc}")
                stop.set()

    threads = [threading.Thread(target=reader, args=(i,), daemon=True)
               for i in range(4)]
    for t in threads:
        t.start()
    try:
        for sequence in range(2, 202):
            if errors:
                break
            promote(root, new if sequence % 2 == 0 else old,
                    sequence, retry_count)
            if sequence % 8 == 0:
                time.sleep(0.001)
    finally:
        stop.set()
        for t in threads:
            t.join(timeout=8)
    if errors:
        raise AssertionError("visible gap/torn selector/bad generation: " + repr(errors))
    if any(t.is_alive() for t in threads):
        raise AssertionError("reader did not terminate")
    if min(counts) == 0:
        raise AssertionError("not all concurrent readers obtained snapshots")
    if visible(root) != (0, 201, old):
        raise AssertionError("final active generation is not the last promoted")
    return {"publishedReplacements": 200, "readCounts": counts,
            "permissionRetries": retry_count[0],
            "directorySyncCapability": sync_result,
            "observedBadPointersOrMixedTrees": 0}


def negative_case(root: pathlib.Path) -> dict[str, Any]:
    old, new, _ = setup(root)
    assert old != new
    intact = encode_head(0, 1, old)
    rejected = 0
    for invalid in (intact[:-1], intact + b"x", b"\0" * HEAD_BYTES,
                    intact[:24] + bytes.fromhex(new) + intact[56:],
                    intact[:4] + b"XXXXX" + intact[9:]):
        try:
            decode_head(invalid)
        except ValueError:
            rejected += 1
    if rejected != 5:
        raise AssertionError("pointer verifier accepted tampered bytes")
    # Both files must be from one validated generation; corruption must fail.
    (root / "generations" / new / "a").write_bytes(b"tamper")
    try:
        verify_generation(root, new)
    except AssertionError:
        rejected += 1
    if rejected != 6:
        raise AssertionError("corrupted generation accepted")
    if visible(root) != (0, 1, old):
        raise AssertionError("uncommitted tampered generation became visible")
    return {"negativeCasesRejected": rejected}


def run_suite(output: pathlib.Path) -> None:
    with tempfile.TemporaryDirectory(prefix="chunkshift-t4c0-") as private:
        base = pathlib.Path(private)
        results: dict[str, Any] = {
            "schema": "chunkshift.patch-tree-t4c0-fs-probe.v0",
            "status": "LAB_OBSERVED_NOT_PRODUCTION_ADOPT",
            "platform": platform.platform(),
            "python": platform.python_version(),
            "pythonPrimitive": "os.replace",
            "crossVolume": "NOT_TESTED_SOURCE_AND_DESTINATION_SIBLINGS",
            "powerCutDurability": "NOT_TESTED",
            "signatureAuthentication": "NOT_IMPLEMENTED",
            "hostileReparseAndSymlinkWalk": "NOT_TESTED",
        }
        results["concurrent"] = concurrent_case(base / "concurrent")
        results["killBefore"] = kill_case(base / "kill-before", "before")
        results["killAfter"] = kill_case(base / "kill-after", "after")
        results["negative"] = negative_case(base / "negative")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(__import__("json").dumps(
        results, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("T4-C0 REAL FILESYSTEM PROBE PASS — observed execution only, no durability claim")
    print(output.read_text(encoding="utf-8"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--worker", action="store_true")
    parser.add_argument("--root", type=pathlib.Path)
    parser.add_argument("--phase", choices=("before", "after"))
    parser.add_argument("--digest")
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args()
    if args.worker and not args.run and args.root and args.phase and args.digest:
        kill_worker(args.root, args.phase, args.digest)
    elif args.run and not args.worker and args.output:
        run_suite(args.output)
    else:
        parser.error("specify --run --output FILE or --worker --root --phase --digest")


if __name__ == "__main__":
    main()
