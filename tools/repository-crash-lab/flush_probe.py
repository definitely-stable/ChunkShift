"""Bounded native persistence capability observations, not a durability proof."""
import argparse
import ctypes
import errno
import hashlib
import json
import os
from pathlib import Path
import platform
import stat
import subprocess
import sys

from filesystem_provenance import linux_handle, WindowsProvenance, volume_key

LAB = Path(__file__).resolve().parent
ROOT = LAB.parents[1]
DLL = LAB / "bin/Release/net10.0/ChunkShift.Repository.CrashLab.dll"
EXPECTED = hashlib.sha256(bytes(i % 251 for i in range(65536))).hexdigest()


def require(ok, message):
    if not ok:
        raise ValueError(message)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def result(operation, phase, error=0):
    return dict(operation=operation, phase=phase, ok=error == 0, error=error)


def posix_sync(path, directory):
    operation = "directory_fsync" if directory else "file_fsync"
    flags = os.O_RDONLY | os.O_DIRECTORY if directory else os.O_RDWR
    try:
        fd = os.open(path, flags)
    except OSError as e:
        return result(operation, "open", e.errno)
    try:
        provenance = linux_handle(fd)
        try:
            os.fsync(fd)
            observed = result(operation, "fsync")
        except OSError as e:
            observed = result(operation, "fsync", e.errno)
    finally:
        os.close(fd)  # close error is a hard failure, not a capability success
    return dict(observed, provenance=provenance)


class WindowsNative:
    def __init__(self):
        from ctypes import wintypes
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.open = self.kernel.CreateFileW
        self.open.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                              ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        self.open.restype = wintypes.HANDLE
        self.flush = self.kernel.FlushFileBuffers
        self.flush.argtypes = [wintypes.HANDLE]
        self.flush.restype = wintypes.BOOL
        self.close = self.kernel.CloseHandle
        self.close.argtypes = [wintypes.HANDLE]
        self.close.restype = wintypes.BOOL
        self.invalid = ctypes.c_void_p(-1).value
        self.provenance = WindowsProvenance(self.kernel)

    def flush_handle(self, handle, operation):
        ctypes.set_last_error(0)
        ok = self.flush(handle)
        error = ctypes.get_last_error()
        require(ok or error != 0, "native failure missing last error")
        return result(operation, "FlushFileBuffers", 0 if ok else error)

    def sync(self, path, directory=False, read_only=False):
        operation = "directory_flush" if directory else "readonly_flush" if read_only else "file_flush"
        ctypes.set_last_error(0)
        handle = self.open(str(path), 0x80000000 if read_only else (0x40000000 | 0x80),
                           7, None, 3, 0x02000000 if directory else 0, None)
        if handle == self.invalid:
            error = ctypes.get_last_error()
            require(error != 0, "CreateFileW failure missing last error")
            return result(operation, "CreateFileW", error)
        require(handle is not None, "unexpected null handle")
        try:
            if read_only:
                return self.flush_handle(handle, operation)
            provenance = self.provenance.observe(handle)
            return dict(self.flush_handle(handle, operation), provenance=provenance)
        finally:
            if not self.close(handle):
                raise ctypes.WinError(ctypes.get_last_error())


def native_invalid(win):
    if win:
        observed = win.flush_handle(win.invalid, "invalid_handle_flush")
        require(observed["error"] == 6, "expected ERROR_INVALID_HANDLE")
        return observed
    # CPython 3.14 rejects negative descriptors before entering libc.
    # Exercise the actual native API; a Python ValueError is not EBADF evidence.
    fsync = ctypes.CDLL(None, use_errno=True).fsync
    fsync.argtypes = [ctypes.c_int]
    fsync.restype = ctypes.c_int
    ctypes.set_errno(0)
    status = fsync(-1)
    error = ctypes.get_errno()
    require(status == -1 and error == errno.EBADF, "expected native fsync(-1) => -1/EBADF")
    return result("invalid_fd_fsync", "fsync", error)


def linux_full():
    # Check the conventional Linux /dev/full character device; no other target.
    info = os.stat("/dev/full", follow_symlinks=False)
    require(stat.S_ISCHR(info.st_mode) and (os.major(info.st_rdev), os.minor(info.st_rdev)) == (1, 7),
            "unexpected /dev/full device")
    fd = os.open("/dev/full", os.O_WRONLY | os.O_CLOEXEC)
    try:
        try:
            os.write(fd, b"x")
        except OSError as e:
            require(e.errno == errno.ENOSPC, "expected ENOSPC")
            return result("dev_full_write", "write", e.errno)
        raise ValueError("/dev/full accepted write")
    finally:
        os.close(fd)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    root = args.output.resolve()
    require(ROOT != root and ROOT not in root.parents, "output inside checkout")
    require(platform.system() in ("Windows", "Linux"), "unsupported probe OS")
    require(not subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip(),
            "dirty checkout")
    root.mkdir(parents=True, exist_ok=False)
    evidence = root / "evidence"
    evidence.mkdir()
    fixture = root / "fixture"
    fixture.mkdir()
    first, renamed = fixture / "initial.bin", fixture / "renamed.bin"
    manifest = dict(experiment="REPO-FLUSH-001", phase="capability-only",
                    commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                    sdk=subprocess.check_output(["dotnet", "--version"], text=True).strip(),
                    host=platform.platform(), machine=platform.machine(),
                    runner_image=os.getenv("ImageOS"), runner_image_version=os.getenv("ImageVersion"),
                    github_run_id=os.getenv("GITHUB_RUN_ID"), github_run_attempt=os.getenv("GITHUB_RUN_ATTEMPT"),
                    fixture_sha256=EXPECTED)
    (evidence / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    rows = []
    def record(stage, observation):
        row = dict(stage=stage, **observation)
        rows.append(row)
        with (evidence / "observations.jsonl").open("a", encoding="utf-8") as out:
            out.write(json.dumps(row, sort_keys=True) + "\n")
    try:
        build = evidence / "build.log"
        try:
            with build.open("w", encoding="utf-8") as log:
                subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.CrashLab.csproj"),
                                "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        except subprocess.CalledProcessError:
            print(build.read_text(encoding="utf-8", errors="replace")[-16000:], file=sys.stderr)
            raise
        child = subprocess.run(["dotnet", str(DLL), "flush-probe", str(first)], cwd=ROOT,
                               capture_output=True, text=True, encoding="utf-8", timeout=120, check=False)
        (evidence / "managed.stdout").write_text(child.stdout, encoding="utf-8")
        (evidence / "managed.stderr").write_text(child.stderr, encoding="utf-8")
        require(child.returncode == 0, "managed probe failed")
        managed = json.loads(child.stdout)
        require(managed["kind"] == "managed-flush" and managed["flush_to_disk"] is True and
                managed["bytes"] == 65536 and managed["sha256"] == EXPECTED and
                managed["create_collision_hresult"] != 0 and
                first.stat().st_size == 65536 and digest(first) == EXPECTED, "managed fixture mismatch")
        win = WindowsNative() if os.name == "nt" else None
        def sync(path, directory=False):
            return win.sync(path, directory) if win else posix_sync(path, directory)
        regular = sync(first)
        record("regular_file", regular)
        require(regular["ok"] and digest(first) == EXPECTED, "native regular sync failed")
        record("invalid_handle", native_invalid(win))
        if win:
            readonly = win.sync(first, read_only=True)
            record("readonly_handle", readonly)
            require(not readonly["ok"] and readonly["phase"] == "FlushFileBuffers",
                    "read-only flush did not fail at requested operation")
            record("native_enospc", dict(operation="write", phase="not-tested", ok=None, error=None))
        else:
            record("native_enospc", linux_full())
        record("after_create", sync(fixture, True))
        require(not renamed.exists(), "unexpected rename target")
        first.rename(renamed)
        require(not first.exists() and digest(renamed) == EXPECTED, "rename identity mismatch")
        record("after_rename", sync(fixture, True))
        require(digest(renamed) == EXPECTED, "fixture changed before deletion")
        renamed.unlink()
        require(not any(fixture.iterdir()), "unexpected retained fixture")
        record("after_delete", sync(fixture, True))
        directory_rows = [r for r in rows if r["stage"].startswith("after_")]
        require(len(directory_rows) == 3, "missing directory observations")
        file_provenance = regular["provenance"]
        attributed = [r["provenance"] for r in directory_rows if "provenance" in r]
        require(all("provenance" in r for r in directory_rows if r["ok"]),
                "successful directory flush lacks handle attribution")
        require(all(volume_key(x) == volume_key(file_provenance) for x in attributed),
                "file and directory mount/volume differ")
        require(len({x["object_id"] for x in attributed}) <= 1, "directory identity changed")
        provenance_status = "HANDLE_BOUND" if len(attributed) == 3 else "INCOMPLETE"
        # Managed Flush(true) ran in a separate process on the trusted fixture
        # before this native open; do not claim the same handle was measured.
        fixture.rmdir()
        summary = dict(experiment="REPO-FLUSH-001", decision="CAPABILITY_ONLY",
                       production_default="DEFER", managed=managed, observations=rows,
                       directory_observed="SUCCESS" if all(r["ok"] for r in directory_rows) else "DEFER",
                       crash_ordering="UNQUALIFIED", fixture_empty=True,
                       provenance_status=provenance_status,
                       managed_attribution="TRUSTED_FIXTURE_REOPEN_ONLY")
        (evidence / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
        print("Persistence capability probe complete; no crash-ordering or power-loss claim")
    finally:
        with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
