"""Exact lifecycle and same-file syscall evidence for integrated FlushFile."""
import re
from integrated_crash_oracle import PACK_BYTES, require, sha


def checkpoint(marker, expected, initial, pid, path):
    require(marker == dict(kind="fsync-checkpoint", pid=pid, path=str(path),
                           state="Running", attempted="CreateExclusive", completed="None",
                           owned=True, writer_closed=False, reader_closed=False,
                           parent_owned=True, parent_close_attempted=False, parent_closed=False,
                           source_position=0, pack_sha256=sha(expected), initial_sha256=sha(initial)),
            "unexpected allocation checkpoint")


def failure(result, expected):
    require(result == dict(kind="integrated-fsync-eio", state="ReconciliationRequired",
                           attempted="FlushFile", completed="WriteVerifiedObject", owned=True,
                           writer_closed=True, reader_closed=False, parent_owned=True,
                           parent_close_attempted=True, parent_closed=True, later_stage_started=False,
                           source_open=True, source_position=PACK_BYTES, pack_sha256=sha(expected),
                           hresult=5, type="IOException"), "unexpected flush failure lifecycle")


def trace_witness(traces, path):
    # strace -ff: one file per thread, timestamps merge the sequential
    # operations on the writer. Never accept a sync on another fd/path.
    tag = r"(?P<fd>\d+)<" + re.escape(str(path)) + r">"
    write = re.compile(r"(?P<time>\d+\.\d+) pwrite64\(" + tag +
                       r", .*, (?P<count>\d+), (?P<offset>\d+)\)\s+= (?P<ret>\d+)")
    sync = re.compile(r"(?P<time>\d+\.\d+) fsync\(" + tag +
                      r"\)\s+= (?P<ret>0|-1 EIO \([^)]*\))")
    events = []
    for trace in traces:
        for line in trace.splitlines():
            if f"<{path}>" not in line:
                continue
            m = write.fullmatch(line.strip())
            if m:
                require(int(m["count"]) == int(m["ret"]) > 0, "short or failed pwrite")
                events.append((m["time"], m["fd"], "write", int(m["offset"]), int(m["count"])))
                continue
            m = sync.fullmatch(line.strip())
            require(m is not None, "unexpected candidate syscall")
            events.append((m["time"], m["fd"], "sync", 0 if m["ret"] == "0" else 5, 0))
    events.sort()
    require(events and len({e[1] for e in events}) == 1, "missing or changed writer fd")
    require(len({e[0] for e in events}) == len(events), "ambiguous syscall order")
    phase, offset = 0, 0
    for _, _, kind, value, count in events:
        require(phase < 2, "unexpected syscall after failed fsync")
        if kind == "write":
            require(value == offset and offset + count <= PACK_BYTES, "write range mismatch")
            offset += count
        else:
            require(offset == PACK_BYTES and value == (0 if phase == 0 else 5),
                    "wrong extent or fsync result")
            phase += 1
            offset = 0
    require(phase == 2 and offset == 0, "missing successful prefill or failed final sync")
    return dict(prefill_bytes=PACK_BYTES, overwrite_bytes=PACK_BYTES,
                prefill_fsync_errno=0, fsync_errno=5, same_fd=True)
