"""Bounded real Linux ENOSPC oracle. No physical persistence inference."""
from integrated_crash_oracle import NAME, PACK_BYTES, WINDOW, require, sha

CAPACITY = 1024 * 1024
PHASES = ("write", "flush")


def verify_failure(phase, result, expected, orphan):
    require(phase in PHASES and len(expected) == PACK_BYTES, "invalid fixture")
    late = phase == "flush"
    extent = WINDOW if late else 0
    require(result["kind"] == "native-enospc" and result["phase"] == phase and
            result["state"] == "ReconciliationRequired" and
            result["attempted"] == ("FlushFile" if late else "WriteVerifiedObject") and
            result["completed"] == ("WriteVerifiedObject" if late else "CreateExclusive"),
            "wrong failure boundary")
    require(result["owned"] is True and result["reader_closed"] is False and
            result["parent_owned"] is True and result["parent_close_attempted"] is True and
            result["parent_closed"] is True and result["source_open"] is True and
            result["source_position"] == (PACK_BYTES if late else WINDOW) and
            result["pack_sha256"] == sha(expected), "failure lifecycle or source mismatch")
    errors = result["errors"]
    require(len(errors) in (1, 2) and all(e == dict(type="IOException", hresult=28) for e in errors) and
            result["writer_closed"] is (len(errors) == 1), "native errno or cleanup mismatch")
    require(orphan == expected[:extent], "orphan is not exact expected prefix")
    return dict(bytes=extent, sha256=sha(orphan), errors=errors)
