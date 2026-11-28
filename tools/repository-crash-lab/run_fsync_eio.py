"""Hosted Linux selective data writeback/fsync EIO syscall capability only."""
import errno
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import uuid

from dm_error_preflight import owned_loop, owned_mapper, run, table
from ext4_read_eio_oracle import BLOCK, OFFSET, SIZE, error_table
from run_ext4_read_eio import mount, unmount, switch, target_extent, direct_read, check_mount
from integrated_crash_oracle import require, sha
from fsync_eio_oracle import LENGTH, BEFORE, AFTER, witness, check_worker
from run import ROOT, digest


def hosted_root():
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and os.getuid() != 0,
            "restricted to unprivileged hosted Linux")
    root = Path(os.environ["RUNNER_TEMP"]).resolve(strict=True)
    require(ROOT != root and ROOT not in root.parents, "fixture inside checkout")
    return root


def worker(path, fault):
    root = hosted_root()
    require(path == path.resolve(strict=True) and path.name == "fsync.bin" and
            path.parent.name == "mount" and path.parent.parent.name.startswith("chunkshift-fsync-") and
            path.parent.parent.parent == root and os.path.ismount(path.parent),
            "not a private test file")
    payload = AFTER if fault else BEFORE
    fd = os.open(path, os.O_WRONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        info = os.fstat(fd)
        require(stat.S_ISREG(info.st_mode) and info.st_size == LENGTH and
                info.st_dev == path.parent.stat().st_dev, "unexpected worker file")
        count = os.pwrite(fd, payload, 0)
        require(count == LENGTH, "buffered write did not fully succeed")
        code = 0
        try:
            os.fsync(fd)
        except OSError as error:
            code = error.errno
        require(code == (errno.EIO if fault else 0), f"unexpected fsync errno {code}")
    finally:
        os.close(fd)
    print(json.dumps(dict(kind="fsync-preflight", written=count, fsync_errno=code,
                          close_confirmed=True, payload_sha256=sha(payload))))


def traced(path, fault, evidence, label):
    trace = evidence / (label + ".strace")
    command = ["strace", "-qq", "-yy", "-s", "32", "-e", "trace=pwrite64,fsync,fdatasync",
               "-o", str(trace), sys.executable, "-B", str(Path(__file__).resolve()),
               "--worker", str(path), "fault" if fault else "control"]
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True,
                            encoding="utf-8", timeout=90, check=False,
                            env=dict(os.environ, LC_ALL="C"))
    (evidence / (label + ".stdout")).write_text(result.stdout, encoding="utf-8")
    (evidence / (label + ".stderr")).write_text(result.stderr, encoding="utf-8")
    require(result.returncode == 0, label + " worker failed")
    row = json.loads(result.stdout)
    check_worker(row, fault)
    return dict(worker=row, syscalls=witness(trace.read_text(encoding="utf-8"), path, fault))


def main():
    root = hosted_root()
    require(shutil.which("strace") is not None, "strace unavailable")
    require(not run("git", "-C", ROOT, "status", "--porcelain"), "dirty checkout")
    evidence = root / "repository-fsync-eio-evidence"
    evidence.mkdir()
    (evidence / "manifest.json").write_text(json.dumps(dict(
        commit=run("git", "-C", ROOT, "rev-parse", "HEAD"), machine=os.uname().machine,
        kernel=os.uname().release, strace=run("strace", "-V"), capacity=SIZE,
        qualification="native-file-fsync-capability-only"), indent=2), encoding="utf-8")
    (evidence / "before.bin").write_bytes(BEFORE)
    (evidence / "attempted.bin").write_bytes(AFTER)
    work = Path(tempfile.mkdtemp(prefix="chunkshift-fsync-", dir=root))
    image, point = work / "backing.img", work / "mount"
    point.mkdir()
    path = point / "fsync.bin"
    token = "CHUNKSHIFT-FSYNC-" + uuid.uuid4().hex
    name = token.lower()
    loop = device = None
    loop_attempted = mapper_attempted = created = False
    mount_attempted = mounted = False
    transition = active_fault = False
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
        require(int(run("sudo", "blockdev", "--getsize64", device)) == SIZE, "mapper extent")
        table(name, linear)
        require(not os.path.ismount(point), "unexpected mount")
        run("sudo", "mkfs.ext4", "-F", "-q", "-b", str(BLOCK), "-m", "0",
            "-E", "lazy_itable_init=0,lazy_journal_init=0", device)
        mount_attempted = True
        mount(point, device, False)
        mounted = True
        run("sudo", "chown", f"{os.getuid()}:{os.getgid()}", "--", point)
        with path.open("xb") as stream:
            stream.write(BEFORE)
            stream.flush()
            os.fsync(stream.fileno())
        fd = os.open(point, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(fd)
        finally:
            os.close(fd)
        target = target_extent(path)
        require(path.stat().st_dev == device.stat().st_rdev and direct_read(path, BEFORE, False) == 0,
                "initial extent or direct data mismatch")
        control = traced(path, False, evidence, "control")
        unmount(point, device, False)
        mounted = mount_attempted = False
        initial = image.read_bytes()
        require(len(initial) == SIZE and initial[target:target+BLOCK] == BEFORE[OFFSET:OFFSET+BLOCK],
                "physical target mismatch")
        fault = error_table(identity, target)
        transition = True
        observed = switch(point, name, token, linear, fault)
        transition = False
        active_fault = True
        mount_attempted = True
        mount(point, device, False)
        mounted = True
        require(target_extent(path) == target and path.stat().st_size == LENGTH,
                "faulted extent or length changed")
        # Fault is armed BEFORE buffered write. No dirty data is flushed by
        # a suspend during the failed operation, no forced cache manipulation.
        failure = traced(path, True, evidence, "fault")
        unmount(point, device, False)
        mounted = mount_attempted = False
        damaged = image.read_bytes()
        require(len(damaged) == SIZE and damaged[target:target+BLOCK] == initial[target:target+BLOCK],
                "faulted physical block was modified")
        (evidence / "faulted-physical-block.bin").write_bytes(damaged[target:target+BLOCK])
        transition = True
        restored = switch(point, name, token, fault, linear)
        transition = False
        active_fault = False
        mount_attempted = True
        mount(point, device, False)
        mounted = True
        require(target_extent(path) == target and direct_read(path, BEFORE, False) == 0,
                "restored target differs from original")
        # This is explicitly repair of a test fixture, not retry of failed
        # fsync or adoption of a repository orphan.
        repair = traced(path, False, evidence, "repair-control")
        require(path.read_bytes() == BEFORE and direct_read(path, BEFORE, False) == 0,
                "repair control bytes differ")
        unmount(point, device, False)
        mounted = mount_attempted = False
        row = dict(target=target, block=BLOCK, logical_offset=OFFSET, control=control,
                   failure=failure, repair=repair, linear=linear, fault_table=observed,
                   restored_table=restored, before_image_sha256=sha(initial),
                   failed_image_sha256=sha(damaged), physical_block_sha256=sha(damaged[target:target+BLOCK]),
                   outcome="pass")
    finally:
        try:
            require(not transition, "ambiguous mapper transition; fixture retained")
            require(not mount_attempted or mounted, "ambiguous mount; fixture retained")
            if mounted:
                # No table switch on a mounted filesystem, even on failure.
                check_mount(point, device, False)
                unmount(point, device, False)
            require(not os.path.ismount(point), "mount still active")
            require(not loop_attempted or loop is not None, "ambiguous loop; fixture retained")
            require(not mapper_attempted or created, "ambiguous mapper; fixture retained")
            if active_fault:
                switch(point, name, token, fault, linear)
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
        finally:
            # Preserve partial failure evidence too.
            with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
                for item in sorted(evidence.iterdir()):
                    if item.is_file() and item.name != "sha256.txt":
                        out.write(f"{digest(item)}  {item.name}\n")
    row["cleanup"] = "confirmed"
    (evidence / "result.json").write_text(json.dumps(row, indent=2), encoding="utf-8")
    # Include final result in the already created manifest.
    with (evidence / "sha256.txt").open("a", encoding="utf-8") as out:
        out.write(f"{digest(evidence / 'result.json')}  result.json\n")
    print("Native file fsync EIO capability: pwrite 65536 PASS; fsync errno 5 PASS; repair control PASS")


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "--worker" and sys.argv[3] in ("fault", "control"):
        worker(Path(sys.argv[2]), sys.argv[3] == "fault")
    else:
        require(len(sys.argv) == 1, "invalid command")
        main()
