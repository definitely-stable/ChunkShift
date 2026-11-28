"""Hosted-only selective ext4 data-read EIO, no live table switch or raw mounted I/O."""
import errno
import fcntl
import json
import mmap
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import uuid

from dm_error_preflight import owned_loop, owned_mapper, run, table
from ext4_read_eio_oracle import BLOCK, OFFSET, SIZE, FIEMAP, decode_extent, error_table, verify_failure
from integrated_crash_oracle import NAME, PACK_BYTES, fixture_witness, check_inspection, require, sha
from run import ROOT, LAB, invoke, digest


def check_mount(point, device, read_only):
    require(os.path.ismount(point) and point.stat().st_dev == device.stat().st_rdev,
            "mounted device identity mismatch")
    result = run("findmnt", "--noheadings", "--output", "FSTYPE,OPTIONS", "--mountpoint", point)
    fs, options = result.split(None, 1)
    require(fs == "ext4" and ("ro" if read_only else "rw") in options.split(","),
            "unexpected filesystem or mount mode")


def mount(point, device, read_only):
    require(not os.path.ismount(point) and not list(point.iterdir()), "mountpoint not fresh/empty")
    options = "nodev,nosuid,noexec,noatime," + ("ro,noload" if read_only else "rw")
    run("sudo", "mount", "-t", "ext4", "-o", options, device, point)
    check_mount(point, device, read_only)


def unmount(point, device, read_only):
    check_mount(point, device, read_only)
    run("sudo", "umount", "--", point)
    require(not os.path.ismount(point) and not list(point.iterdir()), "unmount not confirmed")


def switch(point, name, token, previous, replacement):
    require(not os.path.ismount(point) and not list(point.iterdir()), "refuse live filesystem table change")
    owned_mapper(name, token)
    table(name, " ".join(previous.split()))
    run("sudo", "dmsetup", "suspend", name)
    try:
        run("sudo", "dmsetup", "load", name, "--table", replacement)
    finally:
        run("sudo", "dmsetup", "resume", name)
    owned_mapper(name, token)
    return table(name, " ".join(replacement.split()))


def target_extent(path):
    # File is fsynced and no writer remains. Request only the chosen
    # payload block; accept only an allocated, aligned, ordinary extent.
    buffer = bytearray(88)
    struct.pack_into("<QQIIII", buffer, 0, OFFSET, BLOCK, 0, 0, 1, 0)
    with path.open("rb", buffering=0) as stream:
        fcntl.ioctl(stream.fileno(), FIEMAP, buffer, True)
    return decode_extent(buffer)


def direct_read(path, expected, fault):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECT | os.O_CLOEXEC)
    try:
        with mmap.mmap(-1, BLOCK) as buffer:
            try:
                count = os.preadv(fd, [buffer], OFFSET)
            except OSError as error:
                require(fault and error.errno == errno.EIO, "unexpected direct read failure")
                return error.errno
            require(not fault and count == BLOCK and buffer[:] == expected[OFFSET:OFFSET+BLOCK],
                    "direct read unexpectedly succeeded or bytes differ")
            return 0
    finally:
        os.close(fd)


def inspect(source, path, expected, evidence, name):
    result = invoke(["integrated-inspect", source, path], evidence, name)
    check_inspection(result, expected, expected)
    require(result["accepted"] is True and path.read_bytes() == expected, "positive Pack control differs")


def case(root, evidence, suite):
    source = evidence / (suite + "-source.pack")
    fixture = invoke(["integrated-fixture", suite, source], evidence, suite + "-fixture")
    require(source.stat().st_size == PACK_BYTES, "wrong source extent")
    expected = source.read_bytes()
    fixture_witness(expected, fixture, suite)
    work = Path(tempfile.mkdtemp(prefix="chunkshift-ext4-", dir=root))
    image = work / "backing.img"
    point = work / "mount"
    point.mkdir()
    token = "CHUNKSHIFT-EXT4-" + uuid.uuid4().hex
    name = token.lower()
    loop = device = None
    loop_attempted = mapper_attempted = created = False
    mount_mode = None
    mount_attempted = False
    try:
        with image.open("xb") as stream:
            stream.truncate(SIZE)
        loop_attempted = True
        loop = run("sudo", "losetup", "--find", "--show", "--nooverlap", image)
        identity = owned_loop(loop, image)
        linear = f"0 {SIZE // 512} linear {identity} 0"
        mapper_attempted = True
        run("sudo", "dmsetup", "create", name, "--uuid", token, "--table", linear)
        created = True
        device = owned_mapper(name, token)
        require(int(run("sudo", "blockdev", "--getsize64", device)) == SIZE, "mapper size mismatch")
        table(name, linear)
        # Only this newly created UUID-bound, unmounted mapping is formatted.
        require(not os.path.ismount(point), "unexpected mount")
        run("sudo", "mkfs.ext4", "-F", "-q", "-b", str(BLOCK), "-m", "0",
            "-E", "lazy_itable_init=0,lazy_journal_init=0", device)
        mount_attempted = True
        mount(point, device, False)
        mount_mode = False
        run("sudo", "chown", f"{os.getuid()}:{os.getgid()}", "--", point)
        path = point / NAME
        with path.open("xb") as stream:
            stream.write(expected)
            stream.flush()
            os.fsync(stream.fileno())
        fd = os.open(point, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(fd)
        finally:
            os.close(fd)
        require(path.stat().st_dev == device.stat().st_rdev and path.stat().st_size == PACK_BYTES,
                "candidate not on private mapping")
        target = target_extent(path)
        unmount(point, device, False)
        mount_mode = None
        mount_attempted = False
        # Raw image I/O occurs only after clean unmount. Validate that the
        # selected physical extent contains exactly the intended Pack bytes.
        backing = image.read_bytes()
        require(len(backing) == SIZE and backing[target:target+BLOCK] == expected[OFFSET:OFFSET+BLOCK],
                "FIEMAP target is not the expected payload block")
        backing_sha = sha(backing)
        mount_attempted = True
        mount(point, device, True)
        mount_mode = True
        require(target_extent(path) == target and direct_read(path, expected, False) == 0,
                "read-only baseline extent/control differs")
        inspect(source, path, expected, evidence, suite + "-control")
        unmount(point, device, True)
        mount_mode = None
        mount_attempted = False
        require(digest(image) == backing_sha, "read-only baseline changed image")
        fault = error_table(identity, target)
        observed_table = switch(point, name, token, linear, fault)
        mount_attempted = True
        mount(point, device, True)
        mount_mode = True
        require(target_extent(path) == target, "faulted extent changed")
        native_errno = direct_read(path, expected, True)
        result = invoke(["native-read-eio", source, path], evidence, suite + "-failure")
        verify_failure(result, expected, native_errno)
        unmount(point, device, True)
        mount_mode = None
        mount_attempted = False
        restored_table = switch(point, name, token, fault, linear)
        require(digest(image) == backing_sha, "fault changed physical image")
        mount_attempted = True
        mount(point, device, True)
        mount_mode = True
        require(target_extent(path) == target and direct_read(path, expected, False) == 0,
                "restored direct read failed")
        inspect(source, path, expected, evidence, suite + "-restored")
        unmount(point, device, True)
        mount_mode = None
        mount_attempted = False
        require(digest(image) == backing_sha and source.read_bytes() == expected, "restoration changed source/image")
        row = dict(suite=suite, target=target, logical_offset=OFFSET, block=BLOCK,
                   source_sha256=sha(expected), backing_sha256=backing_sha,
                   fault_table=observed_table, linear=linear, restored_table=restored_table,
                   native_errno=native_errno, reader=result, outcome="pass")
    finally:
        # Unknown mount/allocation outcome retains the fixture. No forced,
        # lazy or recursive cleanup, and never detach a mounted device.
        require(not mount_attempted or mount_mode is not None, "ambiguous mount; fixture retained")
        if mount_mode is not None:
            unmount(point, device, mount_mode)
        require(not os.path.ismount(point), "mount still active")
        require(not loop_attempted or loop is not None, "ambiguous loop; fixture retained")
        require(not mapper_attempted or created, "ambiguous mapper; fixture retained")
        if created:
            owned_mapper(name, token)
            run("sudo", "dmsetup", "remove", name)
        if loop is not None:
            owned_loop(loop, image)
            run("sudo", "losetup", "--detach", loop)
            require(not run("sudo", "losetup", "--associated", image), "loop still associated")
        image.unlink(missing_ok=True)
        point.rmdir()
        work.rmdir()
    row["cleanup"] = "confirmed"
    return row


def main():
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and os.getuid() != 0,
            "restricted to unprivileged hosted Linux")
    root = Path(os.environ["RUNNER_TEMP"]).resolve(strict=True)
    evidence = root / "repository-ext4-read-eio-evidence"
    require(ROOT != root and ROOT not in root.parents and
            not run("git", "-C", ROOT, "status", "--porcelain"), "unsafe or dirty checkout")
    evidence.mkdir()
    meta = dict(commit=run("git", "-C", ROOT, "rev-parse", "HEAD"),
                machine=os.uname().machine, kernel=os.uname().release, capacity=SIZE,
                qualification="selective-ext4-read-eio-only")
    (evidence / "manifest.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    try:
        with (evidence / "build.log").open("x", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.CrashLab.csproj"),
                            "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        with (evidence / "cases.jsonl").open("x", encoding="utf-8") as rows:
            for suite in ("sha256", "blake3"):
                rows.write(json.dumps(case(root, evidence, suite), sort_keys=True) + "\n")
                rows.flush()
        print("Selective ext4 read EIO: 2/2 PASS; no writeback, fsync or durability claim")
    finally:
        with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
