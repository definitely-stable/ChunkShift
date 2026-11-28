"""Strict lifecycle witnesses for the real integrated readback failure."""
from integrated_crash_oracle import PACK_BYTES, require, sha


def checkpoint(marker, expected, pid, path):
    require(marker == dict(kind="readback-checkpoint", pid=pid, path=str(path),
                           state="Running", attempted="VerifyClosedObject", completed="CloseWriter",
                           owned=True, writer_closed=True, reader_closed=False,
                           parent_owned=True, parent_close_attempted=False, parent_closed=False,
                           source_position=PACK_BYTES, pack_sha256=sha(expected)),
            "unexpected integrated checkpoint")


def failure(result, expected, native_errno, resident):
    require(len(expected) == PACK_BYTES and native_errno == 5 and resident is False,
            "native error or cache admission differs")
    require(result == dict(kind="integrated-readback-eio", state="ReconciliationRequired",
                           attempted="VerifyClosedObject", completed="CloseWriter",
                           owned=True, writer_closed=True, reader_closed=True,
                           parent_owned=True, parent_close_attempted=True, parent_closed=True,
                           parent_barrier_started=False, source_open=True, source_position=PACK_BYTES,
                           pack_sha256=sha(expected), hresult=5, type="IOException"),
            "integrated readback EIO lifecycle differs")
