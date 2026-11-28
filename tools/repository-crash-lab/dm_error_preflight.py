"""Hosted-only, unmounted synthetic block device EIO capability preflight.

No repository filesystem is mounted, formatted, filled or faulted. Only a
new private loop file and a UUID-bound device-mapper mapping are touched.
"""
import errno
import hashlib
import json
import mmap
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
import uuid

SIZE = 16 * 1024 * 1024
SECTORS = SIZE // 512
BLOCK = 4096
OFFSET = BLOCK
PAYLOAD = bytes((i * 31 + 7) & 255 for i in range(BLOCK))


def require(ok, message):
    if not ok:
        raise ValueError(message)


def run(*args):
    return subprocess.check_output(list(map(str, args)), text=True, timeout=30).strip()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def owned_mapper(name, token):
    # Never trust a caller-supplied /dev path. The new mapper must match
    # both the UUID and name in sysfs before it is opened or mutated.
    info = run("sudo", "dmsetup", "info", "-c", "--noheadings", "--separator", "|",
               "-o", "uuid,major,minor", name)
    fields = [part.strip() for part in info.split("|")]
    require(len(fields) == 3 and fields[0] == token and
            all(re.fullmatch(r"[0-9]+", part) for part in fields[1:]),
            "mapper UUID or identity mismatch")
    major, minor = map(int, fields[1:])
    sys = Path(f"/sys/dev/block/{major}:{minor}")
    require((sys / "dm/uuid").read_text().strip() == token and
            (sys / "dm/name").read_text().strip() == name, "sysfs mapper mismatch")
    device = (Path("/dev/mapper") / name).resolve(strict=True)
    st = device.stat()
    require(stat.S_ISBLK(st.st_mode) and
            (os.major(st.st_rdev), os.minor(st.st_rdev)) == (major, minor),
            "mapper node mismatch")
    return device


def owned_loop(loop, image):
    require(re.fullmatch(r"/dev/loop[0-9]+", loop) is not None, "not a loop device")
    backing = run("sudo", "losetup", "--list", "--noheadings", "--output", "BACK-FILE", loop)
    require(Path(backing).resolve() == image.resolve(), "loop backing mismatch")
    st = Path(loop).stat()
    require(stat.S_ISBLK(st.st_mode) and
            int(run("sudo", "blockdev", "--getsize64", loop)) == SIZE, "loop size mismatch")
    return f"{os.major(st.st_rdev)}:{os.minor(st.st_rdev)}"


def table(name, expected):
    actual = " ".join(run("sudo", "dmsetup", "table", name).split())
    require(actual == expected, "unexpected mapper table")
    return actual


def switch(name, token, previous, replacement):
    owned_mapper(name, token)
    table(name, previous)
    run("sudo", "dmsetup", "suspend", name)
    try:
        run("sudo", "dmsetup", "load", name, "--table", replacement)
    finally:
        # Even if load fails, resume the UUID-bound mapping; never leave
        # the test device deliberately suspended.
        run("sudo", "dmsetup", "resume", name)
    owned_mapper(name, token)
    return table(name, replacement)


def direct_control(device, write):
    fd = os.open(device, os.O_RDWR | os.O_DIRECT | os.O_CLOEXEC)
    try:
        with mmap.mmap(-1, BLOCK) as buffer:
            if write:
                buffer[:] = PAYLOAD
                require(os.pwritev(fd, [buffer], OFFSET) == BLOCK, "short direct write")
                os.fsync(fd)
            buffer[:] = bytes(BLOCK)
            require(os.preadv(fd, [buffer], OFFSET) == BLOCK and buffer[:] == PAYLOAD,
                    "direct control read mismatch")
            return digest(buffer[:])
    finally:
        os.close(fd)


def direct_errors(device):
    fd = os.open(device, os.O_RDWR | os.O_DIRECT | os.O_CLOEXEC)
    try:
        with mmap.mmap(-1, BLOCK) as buffer:
            buffer[:] = bytes([0xA5]) * BLOCK
            errors = {}
            for name, call in (("read", os.preadv), ("write", os.pwritev)):
                try:
                    call(fd, [buffer], OFFSET)
                except OSError as error:
                    require(error.errno == errno.EIO, f"{name}: unexpected errno {error.errno}")
                    errors[name] = error.errno
                else:
                    raise ValueError(f"{name}: error target unexpectedly succeeded")
            return errors
    finally:
        os.close(fd)


def main():
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and os.getuid() != 0,
            "preflight restricted to unprivileged GitHub-hosted Linux runner")
    root = Path(os.environ["RUNNER_TEMP"]).resolve(strict=True)
    evidence = root / "repository-dm-error-evidence"
    evidence.mkdir()
    checkout = Path(__file__).resolve().parents[2]
    require(checkout not in evidence.parents, "evidence inside checkout")
    require(not run("git", "-C", checkout, "status", "--porcelain"), "dirty checkout")
    manifest = dict(commit=run("git", "-C", checkout, "rev-parse", "HEAD"),
                    machine=os.uname().machine, kernel=os.uname().release,
                    capacity=SIZE, block=BLOCK, offset=OFFSET,
                    targets=run("sudo", "dmsetup", "targets"),
                    qualification="unmounted-synthetic-block-device-only")
    (evidence / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    token = "CHUNKSHIFT-PREFLIGHT-" + uuid.uuid4().hex
    name = token.lower()
    work = Path(tempfile.mkdtemp(prefix="chunkshift-dm-", dir=root))
    image = work / "backing.img"
    loop = None
    created = False
    try:
        with image.open("xb") as stream:
            stream.truncate(SIZE)
        loop = run("sudo", "losetup", "--find", "--show", "--nooverlap", image)
        identity = owned_loop(loop, image)
        linear = f"0 {SECTORS} linear {identity} 0"
        error = f"0 {SECTORS} error"
        run("sudo", "dmsetup", "create", name, "--uuid", token, "--table", linear)
        created = True
        device = owned_mapper(name, token)
        require(int(run("sudo", "blockdev", "--getsize64", device)) == SIZE, "mapper size mismatch")
        table(name, linear)
        # Change permissions only on the just-created UUID-checked node,
        # not on the underlying loop or any existing block device.
        run("sudo", "chown", f"{os.getuid()}:{os.getgid()}", "--", device)
        run("sudo", "chmod", "0600", "--", device)
        before = direct_control(device, True)
        backing_before = digest(image.read_bytes())
        failed_table = switch(name, token, linear, error)
        errors = direct_errors(owned_mapper(name, token))
        restored_table = switch(name, token, error, linear)
        after = direct_control(owned_mapper(name, token), False)
        require(before == after == digest(PAYLOAD) and digest(image.read_bytes()) == backing_before,
                "restored mapping or backing bytes differ")
        result = dict(control_sha256=before, restored_sha256=after,
                      backing_sha256=backing_before, linear=linear,
                      error_table=failed_table, restored_table=restored_table,
                      errno=errors, outcome="pass")
        (evidence / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    finally:
        # Never detach a loop while a mapping might still use it. No force
        # remove, wildcard names, lazy detach or recursive deletion.
        try:
            if created:
                owned_mapper(name, token)
                run("sudo", "dmsetup", "remove", name)
        except Exception:
            # Leave the backing fixture intact if mapping removal fails.
            raise
        if loop is not None:
            owned_loop(loop, image)
            run("sudo", "losetup", "--detach", loop)
            require(not run("sudo", "losetup", "--associated", image), "loop still associated")
        image.unlink(missing_ok=True)
        work.rmdir()
    (evidence / "cleanup.txt").write_text("mapping removed; loop detached; fixture removed\n", encoding="utf-8")
    print("DM error capability: direct read/write EIO 2/2; restored linear witness PASS")


if __name__ == "__main__":
    main()
