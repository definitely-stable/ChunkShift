"""Actual integrated fsync EIO with same-writer syscall evidence."""
import json
import os
from pathlib import Path
import queue
import shutil
import signal
import subprocess
import tempfile
import threading
import uuid

from dm_error_preflight import owned_loop, owned_mapper, run, table
from ext4_read_eio_oracle import BLOCK, OFFSET, SIZE, error_table
from run_ext4_read_eio import mount, unmount, check_mount, target_extent, direct_read, switch
from integrated_crash_oracle import NAME, PACK_BYTES, fixture_witness, check_inspection, require, sha
from integrated_fsync_eio_oracle import checkpoint, failure, trace_witness
from run import ROOT, LAB, DLL, invoke, digest
from run_integrated import fresh_control, snapshot


def switch_live(point, device, name, token, previous, replacement):
    # Distinct from the unmounted preflight. Only a test-owned mounted
    # device, normal filesystem-locking suspend; never --noflush/--nolockfs.
    check_mount(point, device, False)
    require(owned_mapper(name, token) == device, "mapper changed")
    table(name, " ".join(previous.split()))
    run("sudo", "dmsetup", "suspend", name)
    try:
        run("sudo", "dmsetup", "load", name, "--table", replacement)
    finally:
        run("sudo", "dmsetup", "resume", name)
    require(owned_mapper(name, token) == device, "mapper changed after resume")
    check_mount(point, device, False)
    return table(name, " ".join(replacement.split()))


def case(root, evidence, suite):
    source = evidence / (suite + "-source.pack")
    fixture = invoke(["integrated-fixture", suite, source], evidence, suite + "-fixture")
    require(source.stat().st_size == PACK_BYTES, "fixture extent")
    expected = source.read_bytes()
    fixture_witness(expected, fixture, suite)
    initial = bytes(x ^ 255 for x in expected)
    (evidence / (suite + "-initial.bin")).write_bytes(initial)
    work = Path(tempfile.mkdtemp(prefix="chunkshift-integrated-fsync-", dir=root))
    image = work / "backing.img"
    point = work / "mount"
    point.mkdir()
    token = "CHUNKSHIFT-IFSYNC-" + uuid.uuid4().hex
    name = token.lower()
    loop = device = None
    loop_attempted = mapper_attempted = created = False
    mount_attempted = mounted = False
    transition = False
    active_fault = False
    child = None
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
        require(not os.path.ismount(point), "unexpected mount")
        run("sudo", "mkfs.ext4", "-F", "-q", "-b", str(BLOCK), "-m", "0",
            "-E", "lazy_itable_init=0,lazy_journal_init=0", device)
        mount_attempted = True
        mount(point, device, False)
        mounted = True
        run("sudo", "chown", f"{os.getuid()}:{os.getgid()}", "--", point)
        parent = point / "orphan"
        parent.mkdir()
        path = parent / NAME
        trace_prefix = evidence / (suite + "-trace")
        command = ["strace", "-ff", "-ttt", "-yy", "-s", "16", "-qq",
                   "-e", "trace=pwrite64,write,fsync,fdatasync", "-o", str(trace_prefix),
                   "dotnet", str(DLL), "integrated-fsync-eio", str(source), str(parent)]
        with (evidence / (suite + "-child.stderr")).open("x", encoding="utf-8") as stderr:
            with subprocess.Popen(command, cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=stderr, text=True, encoding="utf-8", start_new_session=True,
                                  env=dict(os.environ, LC_ALL="C")) as child:
                messages = queue.Queue()
                reader = threading.Thread(target=lambda: messages.put(child.stdout.readline()), daemon=True)
                reader.start()
                try:
                    line = messages.get(timeout=90)
                    reader.join(timeout=5)
                    require(not reader.is_alive(), "checkpoint reader still running")
                    (evidence / (suite + "-checkpoint.json")).write_text(line, encoding="utf-8")
                    marker = json.loads(line)
                    require(child.poll() is None, "child not parked")
                    pid = marker["pid"]
                    status = Path(f"/proc/{pid}/status").read_text()
                    require(os.getpgid(pid) == child.pid and
                            any(line.split() == ["TracerPid:", str(child.pid)] for line in status.splitlines()),
                            "marker is not tracee in dedicated process group")
                    checkpoint(marker, expected, initial, pid, path)
                    require(path.stat().st_dev == device.stat().st_rdev and snapshot(parent) == initial,
                            "candidate not on private mount or initial bytes differ")
                    target = target_extent(path)
                    require(direct_read(path, initial, False) == 0, "positive direct read failed")
                    fault = error_table(identity, target)
                    transition = True
                    observed = switch_live(point, device, name, token, linear, fault)
                    transition = False
                    active_fault = True
                    require(target_extent(path) == target, "faulted extent changed")
                    # Do not read the faulted block here: no independent read
                    # may contaminate the writer error sequence before fsync.
                    require(child.poll() is None, "child exited before fault release")
                    child.stdin.write("continue\n")
                    child.stdin.flush()
                    stdout, _ = child.communicate(timeout=90)
                    (evidence / (suite + "-result.json")).write_text(stdout, encoding="utf-8")
                    require(child.returncode == 0, "integrated child failed")
                    result = json.loads(stdout)
                    failure(result, expected)
                    traces = sorted(evidence.glob(suite + "-trace.*"))
                    syscalls = trace_witness([p.read_text(encoding="utf-8") for p in traces], path)
                finally:
                    if child.poll() is None:
                        os.killpg(child.pid, signal.SIGKILL)
                        child.wait(timeout=30)
                    reader.join(timeout=5)
        # Do not restore while dirty pages might retry writes through
        # the repaired mapping. Observe the failed physical block only
        # after the faulted filesystem is cleanly unmounted.
        unmount(point, device, False)
        mounted = mount_attempted = False
        backing = image.read_bytes()
        physical = backing[target:target+BLOCK]
        require(len(backing) == SIZE and physical == initial[OFFSET:OFFSET+BLOCK],
                "faulted physical block changed")
        (evidence / (suite + "-physical.bin")).write_bytes(physical)
        transition = True
        restored = switch(point, name, token, fault, linear)
        transition = False
        active_fault = False
        mount_attempted = True
        mount(point, device, False)
        mounted = True
        require(target_extent(path) == target and direct_read(path, initial, False) == 0,
                "restored extent or failed block differs")
        orphan = snapshot(parent)
        require(orphan is not None and len(orphan) == PACK_BYTES and
                orphan[OFFSET:OFFSET+BLOCK] == initial[OFFSET:OFFSET+BLOCK] and orphan != expected,
                "orphan unexpectedly complete or physical block differs")
        (evidence / (suite + "-orphan.pack")).write_bytes(orphan)
        inspected = invoke(["integrated-inspect", source, path], evidence, suite + "-inspect")
        check_inspection(inspected, orphan, expected)
        require(not inspected["accepted"] and snapshot(parent) == orphan, "orphan accepted or modified")
        fresh_control(source, point / "fresh", expected, evidence, suite + "-fresh")
        require(snapshot(parent) == orphan and source.read_bytes() == expected,
                "fresh preparation modified orphan or source")
        row = dict(suite=suite, target=target, logical_offset=OFFSET, block=BLOCK,
                   source_sha256=sha(expected), initial_sha256=sha(initial),
                   orphan_sha256=sha(orphan), physical_sha256=sha(physical), marker=marker,
                   result=result, syscalls=syscalls, inspection=inspected, fault_table=observed,
                   linear=linear, restored_table=restored, fresh="pass", outcome="pass")
    finally:
        # Unmount before restoring error mapping. On ambiguity retain
        # the private fixture, never force cleanup or retry failed fsync.
        require(not transition, "ambiguous mapper transition; fixture retained")
        require(not mount_attempted or mounted, "ambiguous mount; fixture retained")
        if mounted:
            unmount(point, device, False)
        require(not os.path.ismount(point), "mount still active")
        if active_fault:
            switch(point, name, token, fault, linear)
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
    require(shutil.which("strace") is not None, "strace unavailable")
    evidence = root / "repository-integrated-fsync-eio-evidence"
    require(ROOT != root and ROOT not in root.parents and
            not run("git", "-C", ROOT, "status", "--porcelain"), "unsafe or dirty checkout")
    evidence.mkdir()
    meta = dict(commit=run("git", "-C", ROOT, "rev-parse", "HEAD"),
                machine=os.uname().machine, kernel=os.uname().release, capacity=SIZE,
                strace=run("strace", "-V"), qualification="integrated-fsync-eio-only")
    (evidence / "manifest.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    try:
        with (evidence / "build.log").open("x", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.CrashLab.csproj"),
                            "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        with (evidence / "cases.jsonl").open("x", encoding="utf-8") as rows:
            for suite in ("sha256", "blake3"):
                rows.write(json.dumps(case(root, evidence, suite), sort_keys=True) + "\n")
                rows.flush()
        print("Integrated file fsync EIO: 2/2 PASS; no directory fsync or durability claim")
    finally:
        with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
