"""Native syscall preflight oracle, independent of worker's JSON."""
import re
from integrated_crash_oracle import require, sha

LENGTH = 65536
BEFORE = bytes((i * 31 + 7) & 255 for i in range(LENGTH))
AFTER = bytes(x ^ 255 for x in BEFORE)


def witness(trace, path, fault):
    # Single-threaded native worker, strace -yy identifies the exact file.
    tag = r"(?P<fd>\d+)<" + re.escape(str(path)) + r">"
    write = re.compile(r"pwrite64\(" + tag + r', .*, 65536, 0\)\s+= 65536')
    sync = re.compile(r"fsync\(" + tag + r"\)\s+= " +
                      (r"-1 EIO \([^)]*\)" if fault else r"0"))
    selected = [line.strip() for line in trace.splitlines() if f"<{path}>" in line]
    require(len(selected) == 2, "unexpected target syscall count")
    w, s = write.fullmatch(selected[0]), sync.fullmatch(selected[1])
    require(w is not None and s is not None and w["fd"] == s["fd"],
            "no ordered same-fd successful pwrite then required fsync")
    return dict(write=LENGTH, fsync_errno=5 if fault else 0, same_fd=True)


def check_worker(result, fault):
    require(result == dict(kind="fsync-preflight", written=LENGTH,
                           fsync_errno=5 if fault else 0, close_confirmed=True,
                           payload_sha256=sha(AFTER if fault else BEFORE)),
            "worker result mismatch")
