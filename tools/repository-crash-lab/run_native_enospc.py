"""Real ENOSPC using only an isolated, explicitly mounted 1 MiB tmpfs."""
import argparse
import errno
import json
import os
from pathlib import Path
import subprocess

from integrated_crash_oracle import NAME, PACK_BYTES, WINDOW, fixture_witness, check_inspection
from native_enospc_oracle import CAPACITY, PHASES, require, sha, verify_failure
from run import ROOT, LAB, digest, invoke
from run_integrated import fresh_control, snapshot


def available(mount):
    stat = os.statvfs(mount)
    return stat.f_bavail * stat.f_frsize


def ensure_mount(mount):
    require(os.path.ismount(mount) and not list(mount.iterdir()), "expected fresh dedicated mount")
    fs = subprocess.check_output(["findmnt", "--noheadings", "--output", "FSTYPE",
                                  "--mountpoint", str(mount)], text=True).strip()
    stat = os.statvfs(mount)
    require(fs == "tmpfs" and stat.f_blocks * stat.f_frsize == CAPACITY and
            available(mount) == CAPACITY and WINDOW % stat.f_frsize == 0,
            "unexpected filesystem, capacity or page geometry")
    return dict(filesystem=fs, capacity=CAPACITY, fragment_size=stat.f_frsize)


def fill(mount, path, free):
    require(available(mount) == CAPACITY, "mount not empty of data")
    block = b"x" * WINDOW
    remaining = CAPACITY - free
    with path.open("xb", buffering=0) as stream:
        while remaining:
            part = block[:min(remaining, WINDOW)]
            require(stream.write(part) == len(part), "short filler write")
            remaining -= len(part)
        os.fsync(stream.fileno())
    require(available(mount) == free, "wrong pre-attempt free capacity")


def native_witness(mount, case):
    require(available(mount) == 0, "filesystem not full")
    path = case / "errno-control"
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        try:
            os.write(fd, b"x")
        except OSError as error:
            require(error.errno == errno.ENOSPC, "wrong native error")
            return error.errno
        raise ValueError("native write unexpectedly succeeded")
    finally:
        os.close(fd)
        path.unlink()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mount", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    mount, root = args.mount.resolve(), args.output.resolve()
    require(ROOT != mount and ROOT not in mount.parents and ROOT != root and ROOT not in root.parents and
            mount != root and mount not in root.parents and root not in mount.parents,
            "fixture paths must be isolated outside checkout")
    geometry = ensure_mount(mount)
    require(not subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip(),
            "dirty checkout")
    root.mkdir(parents=True, exist_ok=False)
    evidence = root / "evidence"
    evidence.mkdir()
    manifest = dict(experiment="R1-C.2b.4-linux-enospc", qualification="bounded-tmpfs-only",
                    commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                    sdk=subprocess.check_output(["dotnet", "--version"], text=True).strip(),
                    machine=os.uname().machine, geometry=geometry, phases=PHASES, suites=["sha256", "blake3"])
    (evidence / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    count = 0
    try:
        with (evidence / "build.log").open("x", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.CrashLab.csproj"),
                            "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        with (evidence / "cases.jsonl").open("x", encoding="utf-8") as rows:
            for suite in ("sha256", "blake3"):
                source = evidence / (suite + "-source.pack")
                fixture = invoke(["integrated-fixture", suite, source], evidence, suite + "-fixture")
                require(source.stat().st_size == PACK_BYTES, "fixture extent")
                expected = source.read_bytes()
                fixture_witness(expected, fixture, suite)
                for phase in PHASES:
                    name = f"{suite}-{phase}"
                    case = mount / name
                    case.mkdir()
                    parent = case / "orphan"
                    parent.mkdir()
                    filler = case / "filler"
                    before_free = WINDOW if phase == "flush" else 0
                    fill(mount, filler, before_free)
                    result = invoke(["native-enospc", source, parent, phase], evidence, name + "-failure")
                    orphan = snapshot(parent)
                    observed = verify_failure(phase, result, expected, orphan)
                    (evidence / (name + "-orphan.pack")).write_bytes(orphan)
                    require(available(mount) == 0, "failure did not exhaust bounded mount")
                    native_errno = native_witness(mount, case)
                    inspection = invoke(["integrated-inspect", source, parent / NAME],
                                        evidence, name + "-inspect")
                    check_inspection(inspection, orphan, expected)
                    require(not inspection["accepted"] and snapshot(parent) == orphan,
                            "partial candidate accepted or modified")
                    filler.unlink()
                    fresh = case / "fresh"
                    fresh_control(source, fresh, expected, evidence, name + "-fresh")
                    require(snapshot(parent) == orphan and source.read_bytes() == expected,
                            "fresh preparation modified failed artifact or source")
                    rows.write(json.dumps(dict(suite=suite, phase=phase, initial_free=before_free,
                                               exhausted_free=0, native_errno=native_errno,
                                               result=result, orphan=observed, inspection=inspection,
                                               fresh="pass", outcome="pass"), sort_keys=True) + "\n")
                    rows.flush()
                    # Delete only verified test-owned entries after evidence was saved.
                    (fresh / NAME).unlink()
                    fresh.rmdir()
                    (parent / NAME).unlink()
                    parent.rmdir()
                    case.rmdir()
                    require(available(mount) == CAPACITY, "fixture allocation not released")
                    count += 1
        require(count == 4, "incomplete native ENOSPC matrix")
        print("Real Linux tmpfs ENOSPC: 4/4 PASS; no device EIO or durability claim")
    finally:
        with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
