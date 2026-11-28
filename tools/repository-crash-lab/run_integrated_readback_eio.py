"""Actual integrated readback EIO on a private mounted ext4 mapper."""
import ctypes
import json
import mmap
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import uuid

from dm_error_preflight import owned_loop, owned_mapper, run, table
from ext4_read_eio_oracle import BLOCK, OFFSET, SIZE, error_table
from run_ext4_read_eio import mount, unmount, check_mount, target_extent, direct_read, inspect
from integrated_crash_oracle import NAME, PACK_BYTES, fixture_witness, require, sha
from integrated_readback_eio_oracle import checkpoint, failure
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


def evict_target(path):
    fd = os.open(path, os.O_RDONLY | os.O_CLOEXEC)
    try:
        os.posix_fadvise(fd, 0, 0, os.POSIX_FADV_DONTNEED)
        length = os.fstat(fd).st_size
        page = os.sysconf("SC_PAGE_SIZE")
        require(length == PACK_BYTES and page > 0 and OFFSET < length, "invalid cache geometry")
        pages = (length + page - 1) // page
        vector = (ctypes.c_ubyte * pages)()
        libc = ctypes.CDLL(None, use_errno=True)
        mincore = libc.mincore
        mincore.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_ubyte)]
        mincore.restype = ctypes.c_int
        with mmap.mmap(fd, length, access=mmap.ACCESS_COPY) as mapping:
            # Obtain address without touching a file-backed page.
            address = ctypes.c_char.from_buffer(mapping)
            try:
                require(mincore(ctypes.addressof(address), length, vector) == 0,
                        f"mincore failed errno={ctypes.get_errno()}")
            finally:
                del address
        first = OFFSET // page
        last = (OFFSET + BLOCK - 1) // page
        resident = any(vector[index] & 1 for index in range(first, last + 1))
        require(not resident, "target page still resident; no cache fallback")
        return dict(page_bytes=page, vector=list(vector), target_resident=resident)
    finally:
        os.close(fd)


def case(root, evidence, suite):
    source = evidence / (suite + "-source.pack")
    fixture = invoke(["integrated-fixture", suite, source], evidence, suite + "-fixture")
    require(source.stat().st_size == PACK_BYTES, "fixture extent")
    expected = source.read_bytes()
    fixture_witness(expected, fixture, suite)
    work = Path(tempfile.mkdtemp(prefix="chunkshift-readback-", dir=root))
    image = work / "backing.img"
    point = work / "mount"
    point.mkdir()
    token = "CHUNKSHIFT-READBACK-" + uuid.uuid4().hex
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
        command = ["dotnet", str(DLL), "integrated-readback-eio", str(source), str(parent)]
        with (evidence / (suite + "-child.stderr")).open("x", encoding="utf-8") as stderr:
            with subprocess.Popen(command, cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=stderr, text=True, encoding="utf-8") as child:
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
                    checkpoint(marker, expected, child.pid, path)
                    require(path.stat().st_dev == device.stat().st_rdev and snapshot(parent) == expected,
                            "candidate not on private mount or bytes differ")
                    target = target_extent(path)
                    require(direct_read(path, expected, False) == 0, "positive direct read failed")
                    fault = error_table(identity, target)
                    transition = True
                    observed = switch_live(point, device, name, token, linear, fault)
                    transition = False
                    active_fault = True
                    require(target_extent(path) == target, "faulted extent changed")
                    native_errno = direct_read(path, expected, True)
                    cache = evict_target(path)
                    require(child.poll() is None, "child exited before fault release")
                    child.stdin.write("continue\n")
                    child.stdin.flush()
                    stdout, _ = child.communicate(timeout=90)
                    (evidence / (suite + "-result.json")).write_text(stdout, encoding="utf-8")
                    require(child.returncode == 0, "integrated child failed")
                    result = json.loads(stdout)
                    failure(result, expected, native_errno, cache["target_resident"])
                finally:
                    if child.poll() is None:
                        child.kill()
                        child.wait(timeout=30)
                    reader.join(timeout=5)
        transition = True
        restored = switch_live(point, device, name, token, fault, linear)
        transition = False
        active_fault = False
        # Drop any failed cache page before restored readback. Do not
        # require cache eviction for the positive control, but exact bytes.
        with path.open("rb", buffering=0) as stream:
            os.posix_fadvise(stream.fileno(), 0, 0, os.POSIX_FADV_DONTNEED)
        require(target_extent(path) == target and direct_read(path, expected, False) == 0,
                "restored extent or data differs")
        inspect(source, path, expected, evidence, suite + "-restored")
        original = snapshot(parent)
        require(original == expected, "restored orphan differs")
        (evidence / (suite + "-orphan.pack")).write_bytes(original)
        fresh_control(source, point / "fresh", expected, evidence, suite + "-fresh")
        require(snapshot(parent) == original and source.read_bytes() == expected,
                "fresh preparation modified orphan or source")
        row = dict(suite=suite, target=target, logical_offset=OFFSET, block=BLOCK,
                   source_sha256=sha(expected), orphan_sha256=sha(original), marker=marker,
                   result=result, native_errno=native_errno, cache=cache, fault_table=observed,
                   linear=linear, restored_table=restored, fresh="pass", outcome="pass")
    finally:
        # Never unmount through an error mapping or guess a transition result.
        # On ambiguity retain the private fixture for runner teardown.
        require(not transition, "ambiguous mapper transition; fixture retained")
        if active_fault:
            switch_live(point, device, name, token, fault, linear)
        require(not mount_attempted or mounted, "ambiguous mount; fixture retained")
        if mounted:
            unmount(point, device, False)
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
    evidence = root / "repository-integrated-readback-eio-evidence"
    require(ROOT != root and ROOT not in root.parents and
            not run("git", "-C", ROOT, "status", "--porcelain"), "unsafe or dirty checkout")
    evidence.mkdir()
    meta = dict(commit=run("git", "-C", ROOT, "rev-parse", "HEAD"),
                machine=os.uname().machine, kernel=os.uname().release, capacity=SIZE,
                qualification="integrated-readback-eio-only")
    (evidence / "manifest.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    try:
        with (evidence / "build.log").open("x", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.CrashLab.csproj"),
                            "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        with (evidence / "cases.jsonl").open("x", encoding="utf-8") as rows:
            for suite in ("sha256", "blake3"):
                rows.write(json.dumps(case(root, evidence, suite), sort_keys=True) + "\n")
                rows.flush()
        print("Integrated readback EIO: 2/2 PASS; no writeback, fsync or durability claim")
    finally:
        with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
