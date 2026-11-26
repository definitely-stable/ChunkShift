"""AUDIT-003: fixed-fixture crash oracle, not a general pack parser."""
from collections import Counter
import hashlib
import json
import struct

SOURCE_SHA = "79df7a3236a66a983fc9fbf3a921c2a017fd6310267bf296d150f749d9c54e96"
LAYOUT_SHA = {
    "sha256": "d32520a310d7cb543eda512292880897023c3d404f484fab018c0ff746efeee4",
    "blake3": "f7c656ff69d2af26c278dff4860508479033a6a62f9633df6ce9bed52e66cfd7",
}
# Locked from the original accepted run, not derived from the killed output.
EXPECTED = {
    "BeforeCreate:1": (0, 0, 0),
    "BeforeCreate:2": (1, 0, 976293),
    "AfterCloseBeforeReadback:1": (1, 0, 976293),
    "AfterVerified:1": (1, 0, 976293),
    "BeforeActiveDuplicate:1": (0, 1, 0),
    "BeforeClosedDuplicate:1": (4, 1, 4061599),
    "Ready:1": (5, 0, 4723444),
}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(shape):
    return json.dumps(shape, sort_keys=True, separators=(",", ":"))


def control_payloads(control, source, suite):
    require(len(source) == 8388608 and sha(source) == SOURCE_SHA, "locked source mismatch")
    require(control["layout_sha256"] == LAYOUT_SHA[suite] and
            sha(json.dumps(control["geometry"], separators=(",", ":")).encode()) == LAYOUT_SHA[suite],
            "locked canonical layout mismatch")
    require(control["restore_sha256"] == SOURCE_SHA and
            (control["packs"], control["frames"], control["bytes"], control["occurrences"]) ==
            (5, 59, 4723444, 73), "locked control geometry mismatch")
    require(len(control["geometry"]) == 5 and len(control["logical"]) == 73, "control count mismatch")
    payloads, offset = {}, 0
    for index, item in enumerate(control["logical"]):
        length = item["length"]
        require(item["index"] == index and item["offset"] == offset and
                0 < length <= 256 * 1024 and offset + length <= len(source), "logical extent mismatch")
        data = source[offset:offset + length]
        key = item["key"]
        require(key not in payloads or payloads[key] == data, "logical key aliases unequal source bytes")
        payloads[key] = data
        if suite == "sha256":
            require(sha(data) == key, "source SHA256 chunk mismatch")
        offset += length
    keys = [frame["key"] for pack in control["geometry"] for frame in pack["frames"]]
    require(offset == len(source) and len(keys) == len(set(keys)) == 59 and
            set(keys) == set(payloads), "control source/physical coverage mismatch")
    return payloads


def check_condition(boundary, state, control):
    valid, invalid, extent = EXPECTED[boundary]
    require(state["kind"] == "inspection" and
            (len(state["valid"]), len(state["invalid"]), state["bytes"]) == (valid, invalid, extent) and
            state["packs"] == valid and state["all_accepted"] == (invalid == 0),
            "locked checkpoint classification mismatch")
    physical = state["physical"]
    require(len(physical) == valid and sorted(p["name"] for p in physical) == sorted(state["valid"]),
            "physical file classification mismatch")
    require(len({p["pack_id"] for p in physical}) == valid, "repeated physical PackId")
    actual = [dict(bytes=p["bytes"], frames=p["frames"]) for p in physical]
    expected = control["geometry"][:valid]
    # Random pack names/IDs differ between runs; retain multiplicity and
    # per-pack ordered frame geometry, ignoring only pack enumeration order.
    require(Counter(map(canonical, actual)) == Counter(map(canonical, expected)),
            "closed pack prefix geometry mismatch")
    require(state["frames"] == sum(len(p["frames"]) for p in expected), "frame count mismatch")


def verify_raw(data, physical, payloads, suite):
    """Cross-check exact P0 layout fields and payload against source, independently of C# readback."""
    frames = physical["frames"]
    count = len(frames)
    require(0 < count <= 1024 and len(data) == physical["bytes"] <= 4 * 1024 * 1024,
            "raw extent mismatch")
    require(sha(data) == physical["pack_digest"], "raw PackDigest mismatch")
    require(data[:8] == b"CSRAWPK0" and struct.unpack_from("<HHHH", data, 8) ==
            (1, 64, 2 if suite == "sha256" else 1, 0) and
            data[16:48].hex() == physical["pack_id"] and data[48:64] == bytes(16), "raw header mismatch")
    offset = 64
    found = {}
    for ordinal, frame in enumerate(frames):
        key, length = frame["key"], frame["Length"]
        require(frame["Ordinal"] == ordinal and frame["Offset"] == offset and
                0 < length <= 256 * 1024 and key in payloads and key not in found, "raw frame extent mismatch")
        payload = data[offset:offset + length]
        require(payload == payloads[key] and len(payload) == length, "raw frame differs from source")
        found[key] = payload
        offset += length
    directory = offset
    index = directory + 32 * count
    footer = index + 36 * count
    require(footer + 104 == len(data), "raw section geometry mismatch")
    for ordinal, frame in enumerate(frames):
        require(struct.unpack_from("<QQQIHH", data, directory + ordinal * 32) ==
                (frame["Offset"], frame["Length"], frame["Length"], frame["Crc32C"], 0, 0),
                "raw frame directory mismatch")
    for i, frame in enumerate(sorted(frames, key=lambda f: f["key"])):
        start = index + 36 * i
        require(data[start:start+32].hex() == frame["key"] and
                struct.unpack_from("<I", data, start+32)[0] == frame["Ordinal"], "raw hash index mismatch")
    require(data[footer:footer+8] == b"CSRAWFT0" and
            struct.unpack_from("<IIQQQQQQ", data, footer+8) ==
            (count, 0, directory-64, directory, 32*count, index, 36*count, 0), "raw footer mismatch")
    trailer = footer + 64
    require(data[trailer:trailer+8] == b"CSRAWTR0" and
            struct.unpack_from("<QIHHQ", data, trailer+8) == (footer, 64, 1, 0, len(data)) and
            data[-4:] == bytes(4), "raw trailer mismatch")
    # CRC correctness and BLAKE3 are checked by the full C# reader. This
    # bounded oracle additionally binds every payload to exact source bytes.
    return found


def raw_witness(boundary, state, control, payloads, root, snapshot, suite):
    found, witness = {}, []
    for physical in state["physical"]:
        name = physical["name"]
        require(name in snapshot and snapshot[name]["bytes"] == physical["bytes"] and
                snapshot[name]["sha256"] == physical["pack_digest"], "snapshot/rebuild identity mismatch")
        data = (root / name).read_bytes()
        actual = verify_raw(data, physical, payloads, suite)
        require(not set(found).intersection(actual), "duplicate physical key in orphan prefix")
        found.update(actual)
        witness.append(dict(name=name, pack_digest=physical["pack_digest"],
                            frames=[dict(key=key, sha256=sha(value)) for key, value in actual.items()]))
    restored = None
    if boundary == "Ready:1":
        require(set(found) == set(payloads), "Ready source coverage mismatch")
        digest = hashlib.sha256()
        for item in control["logical"]:
            digest.update(found[item["key"]])
        restored = digest.hexdigest()
        require(restored == SOURCE_SHA, "Ready orphan reconstruction mismatch")
    return dict(packs=witness, restored_sha256=restored)
