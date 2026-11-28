"""Locked R1-C.2b.3 process-crash oracle; no durability or adoption verdict."""
import hashlib
from crash_oracle import verify_raw

NAME = "0123456789abcdef0123456789abcdef.prepare"
WINDOW = 65536
STAGES = ("CreateExclusive", "WriteVerifiedObject", "FlushFile", "CloseWriter",
          "VerifyClosedObject", "FlushParentDirectory")
BOUNDARIES = tuple(f"{stage}:{edge}" for stage in STAGES for edge in ("before", "after"))
BOUNDARIES = BOUNDARIES[:3] + ("WriteVerifiedObject:partial",) + BOUNDARIES[3:] + ("Prepared",)
FIRST = bytes((i * 31 + 7) & 255 for i in range(WINDOW + 7))
SECOND = bytes((1, 2, 3))
PACK_BYTES = 64 + len(FIRST) + len(SECOND) + 2 * 68 + 104


def require(ok, message):
    if not ok:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def fixture_witness(data, fixture, suite):
    require(fixture["kind"] == "fixture" and fixture["suite"] == suite and
            fixture["bytes"] == len(data) == PACK_BYTES and fixture["sha256"] == sha(data),
            "fixture identity mismatch")
    frames = fixture["frames"]
    require(len(frames) == 2 and [f["Length"] for f in frames] == [len(FIRST), len(SECOND)] and
            [f["Offset"] for f in frames] == [64, 64 + len(FIRST)] and
            len({f["key"] for f in frames}) == 2, "fixture frame geometry")
    payloads = {frames[0]["key"]: FIRST, frames[1]["key"]: SECOND}
    if suite == "sha256":
        require(all(sha(value) == key for key, value in payloads.items()), "fixture logical SHA")
    physical = dict(bytes=len(data), pack_digest=fixture["sha256"],
                    pack_id=fixture["pack_id"], frames=frames)
    require(verify_raw(data, physical, payloads, suite) == payloads, "independent fixture payload mismatch")
    return sha(FIRST + SECOND)


def check_marker(boundary, marker):
    require(boundary in BOUNDARIES and marker["kind"] == "checkpoint" and
            marker["boundary"] == boundary, "checkpoint identity")
    if boundary == "Prepared":
        attempted = completed = STAGES[-1]
        state = "ExperimentalPackPrepared"
        index, after = len(STAGES) - 1, True
    else:
        attempted, edge = boundary.split(":")
        index = STAGES.index(attempted)
        after = edge == "after"
        completed = STAGES[index - 1] if index else "None"
        state = "Running"
    # Complete invokes the after-effect checkpoint before updating LastCompleted.
    def reached(n):
        return index > n or (index == n and after)

    require(marker["state"] == state and marker["attempted"] == attempted and
            marker["completed"] == completed and marker["owned"] is reached(0) and
            marker["writer_closed"] is reached(3) and marker["reader_closed"] is reached(4) and
            marker["parent_owned"] is True and marker["parent_close_attempted"] is reached(5) and
            marker["parent_closed"] is reached(5), "checkpoint lifecycle mismatch")


def check_candidate(boundary, data, expected):
    require(boundary in BOUNDARIES and len(expected) == PACK_BYTES, "invalid oracle input")
    if boundary == "CreateExclusive:before":
        require(data is None, "artifact before create")
        return "absent"
    require(data is not None and len(data) <= len(expected) and
            data == expected[:len(data)], "candidate is not exact pinned prefix")
    if boundary in ("CreateExclusive:after", "WriteVerifiedObject:before"):
        require(len(data) == 0, "unexpected bytes before write")
    elif boundary == "WriteVerifiedObject:partial":
        require(len(data) == WINDOW, "first completed unbuffered write extent mismatch")
    elif boundary in ("WriteVerifiedObject:after", "FlushFile:before"):
        # Last short write may still be in the managed buffer. Do not infer
        # durability from full bytes observed after process termination.
        require(WINDOW <= len(data) <= len(expected), "unexpected pre-flush extent")
    else:
        require(data == expected, "completed file flush did not retain exact bytes")
    return "exact" if data == expected else "prefix"


def check_inspection(inspection, data, expected):
    require(inspection["kind"] == "inspection" and inspection["accepted"] is (data == expected) and
            inspection["bytes"] == len(data) and inspection["sha256"] == sha(data) and
            inspection["expected_sha256"] == sha(expected), "independent inspector disagreement")
