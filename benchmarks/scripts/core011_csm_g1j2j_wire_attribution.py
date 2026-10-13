#!/usr/bin/env python3
"""G1-J2J independent, strict current-output CSP v1 physical byte ledger.

Input is the *actual* CSP physical bytes, before the parent isolated-runner
unlinks its private binary files. Nothing in this parser decodes zstd,
reads user files or modifies production Core/Patching code.
"""
import struct

SCHEMA = "chunkshift.core011.g1j2j-csp-wire-ledger.v1"
COMPONENTS = (
    "preamble", "sectionHeaders", "embeddedTargetCsm", "baseManifestId",
    "paylBlockPrefixes", "paylFixedEntryHeaders", "paylDictionaryReferences",
    "storedEncodedPayload", "paylCrc32c", "pidxPrefix",
    "pidxEntryRecords", "pidxCrc32c", "footerPayload", "trailer",
)
MAX_INPUT = 20 * 1024 * 1024


def require(ok, message):
    if not ok:
        raise ValueError("G1J2J CSP wire: " + message)


def _crc_table():
    table = []
    for byte in range(256):
        value = byte
        for _ in range(8):
            value = (value >> 1) ^ (0x82F63B78 if value & 1 else 0)
        table.append(value)
    return tuple(table)


TABLE = _crc_table()


def crc32c(raw):
    value = 0xffffffff
    for byte in raw:
        value = TABLE[(value ^ byte) & 255] ^ (value >> 8)
    return value ^ 0xffffffff


def counted_bytes(raw, expected_target_csm, with_base):
    """Compute an exactly conserving physical ledger, or reject the CSP.

    No raw IDs/payload or source content are present in the return object.
    CSM integrity and the CSP whole-file BLAKE3 digest are verified separately
    by the independent *public* patch create/apply/verify and frozen SHA oracles.
    """
    require(isinstance(raw, bytes) and 128 <= len(raw) <= MAX_INPUT,
            "invalid bounded input")
    require(isinstance(expected_target_csm, bytes) and expected_target_csm,
            "target CSM original bytes required")
    require(type(with_base) is bool, "base association must be explicit")

    require(raw[:4] == b"CSP1", "preamble magic")
    major, pre_size = struct.unpack_from("<HH", raw, 4)
    require((major, pre_size) == (1, 32) and
            raw[8:32] == bytes(24), "preamble version/features/reserved")
    trailer = len(raw) - 64
    require(raw[trailer:trailer + 4] == b"CSPT", "trailer magic at exact EOF")
    tr_major, tr_size = struct.unpack_from("<HH", raw, trailer + 4)
    foot_address, physical_length = struct.unpack_from("<QQ", raw, trailer + 8)
    require((tr_major, tr_size) == (1, 64) and physical_length == len(raw)
            and raw[trailer + 56:] == bytes(8),
            "trailer version/physical size/reserved")

    ledger = dict.fromkeys(COMPONENTS, 0)
    ledger["preamble"] = 32
    ledger["trailer"] = 64
    sections = []
    entries = []
    types = []
    by_type = {}
    counts = {"PAYL": 0, "raw": 0, "zstdPlain": 0, "zstdDictionary": 0,
              "dictionaryReferences": 0, "payloadEntries": 0}
    stored = {"raw": 0, "zstdPlain": 0, "zstdDictionary": 0}
    pos = 32
    first_payl = 0
    while pos < trailer:
        require(pos + 16 <= trailer, "section header truncated")
        kind, flags, length = struct.unpack_from("<4sIQ", raw, pos)
        require(kind in (b"TCSM", b"BASE", b"PAYL", b"PIDX", b"FOOT"),
                "unexpected section kind / extension in emitted production CSP")
        require(flags == (0 if kind == b"BASE" else 1),
                "section required flag drift")
        end = pos + 16 + length
        require(end <= trailer, "section overflows trailer")
        body = raw[pos + 16:end]
        name = kind.decode("ascii")
        require((name in ("TCSM", "BASE", "PAYL") or
                 name not in by_type),
                "duplicate fixed section")
        if name != "PAYL":
            require(name not in by_type, "duplicate section")
        if not types:
            require(name == "TCSM", "TCSM is not the first section")
        elif name == "BASE":
            require(types == ["TCSM"], "BASE is not after TCSM")
        elif name == "PAYL":
            require("PIDX" not in types and "FOOT" not in types,
                    "PAYL found after PIDX/FOOT")
        elif name == "PIDX":
            require("PIDX" not in types and "FOOT" not in types,
                    "PIDX duplicate or misplaced")
        elif name == "FOOT":
            require(types and types[-1] == "PIDX",
                    "FOOT must immediately follow PIDX")
        elif name == "TCSM":
            require(not types, "duplicate TCSM")
        ledger["sectionHeaders"] += 16
        sections.append({"kind": name, "offset": pos, "length": length})
        if name == "TCSM":
            require(body == expected_target_csm,
                    "embedded target CSM differs from verified original target CSM")
            ledger["embeddedTargetCsm"] = length
        elif name == "BASE":
            require(with_base and length == 32, "missing/wrong BASE association")
            ledger["baseManifestId"] = 32
        elif name == "PAYL":
            if first_payl == 0:
                first_payl = pos
            require(length >= 16 + 40 + 4, "short PAYL block")
            block_count, reserved, first = struct.unpack_from("<IIQ", body)
            require(1 <= block_count <= 4096 and reserved == 0
                    and first == len(entries), "PAYL block prefix ordinal/count")
            require(len(body) >= 4, "missing PAYL CRC")
            want_crc = struct.unpack_from("<I", body, len(body) - 4)[0]
            have_crc = crc32c(raw[pos:end - 4])
            require(want_crc == have_crc, "PAYL CRC32C mismatch")
            ledger["paylBlockPrefixes"] += 16
            ledger["paylCrc32c"] += 4
            cursor = 16
            limit = len(body) - 4
            for _ in range(block_count):
                require(cursor + 40 <= limit, "PAYL entry truncated header")
                stored_len = struct.unpack_from("<I", body, cursor + 32)[0]
                encoding, ndict, rest = struct.unpack_from("<BBH", body, cursor + 36)
                require(stored_len > 0 and encoding in (0, 1)
                        and (ndict == 0 if encoding == 0 else ndict <= 4)
                        and rest == 0, "PAYL entry codec/dictionaries/reserved")
                entry_offset = pos + 16 + cursor
                total = 40 + 32 * ndict + stored_len
                require(cursor + total <= limit,
                        "PAYL entry dictionary/stored body overflows block")
                ledger["paylFixedEntryHeaders"] += 40
                ledger["paylDictionaryReferences"] += ndict * 32
                ledger["storedEncodedPayload"] += stored_len
                counts["dictionaryReferences"] += ndict
                bucket = ("raw" if encoding == 0 else
                          "zstdDictionary" if ndict else "zstdPlain")
                counts[bucket] += 1
                stored[bucket] += stored_len
                entries.append((entry_offset, stored_len, encoding, ndict))
                cursor += total
            require(cursor == limit, "unaccounted bytes in PAYL")
            counts["PAYL"] += 1
        elif name == "PIDX":
            require(len(body) >= 12, "short PIDX")
            version, entry_count = struct.unpack_from("<II", body, 0)
            require(version == 1 and entry_count == len(entries) and
                    length == 8 + 24 * entry_count + 4,
                    "PIDX version/count/length mismatch")
            require(struct.unpack_from("<I", body, length - 4)[0] ==
                    crc32c(raw[pos:end - 4]),
                    "PIDX CRC32C mismatch")
            last_first = -1
            for i in range(entry_count):
                first, offset, stored_len, encoding, ndict, reserved = struct.unpack_from(
                    "<QQIBBH", body, 8 + 24 * i)
                require(first > last_first and reserved == 0,
                        "PIDX first target occurrence order/reserved")
                last_first = first
                require((offset, stored_len, encoding, ndict) == entries[i],
                        "PIDX disagrees with physical PAYL entry offset/size/codec")
            ledger["pidxPrefix"] += 8
            ledger["pidxEntryRecords"] += 24 * entry_count
            ledger["pidxCrc32c"] += 4
        elif name == "FOOT":
            require(length == 56, "FOOT length")
            fields = struct.unpack_from("<QQQQQQQ", body)
            tcsm_off, base_off, pidx_off, payl_off, npayl, nentry, reserved = fields
            require(tcsm_off == sections[0]["offset"] and
                    base_off == by_type.get("BASE", 0) and
                    pidx_off == by_type.get("PIDX", 0) and
                    payl_off == first_payl and
                    npayl == counts["PAYL"] and nentry == len(entries) and
                    reserved == 0, "FOOT offsets/counts mismatch")
            require(pos == foot_address, "FOOT offset differs from TRAILER")
            ledger["footerPayload"] = 56
        if name != "PAYL":
            by_type[name] = pos
        types.append(name)
        pos = end

    require(pos == trailer and types[-2:] == ["PIDX", "FOOT"],
            "sections do not end in PIDX/FOOT at trailer")
    require(types.count("TCSM") == 1 and types.count("PIDX") == 1
            and types.count("FOOT") == 1
            and (types.count("BASE") == 1) == with_base,
            "required section/base association mismatch")
    counts["payloadEntries"] = len(entries)
    require(sum(ledger.values()) == len(raw),
            "wire ledger does not conserve physical bytes")
    require(sum(stored.values()) == ledger["storedEncodedPayload"],
            "stored codec buckets do not conserve bytes")
    return {
        "schema": SCHEMA,
        "physicalBytes": len(raw),
        "ledger": ledger,
        "entryCounts": counts,
        "storedByEncodingBytes": stored,
        "sections": len(sections),
        "crc32cVerified": True,
        "pidxOffsetsVerified": True,
        "targetCsmPhysicalIdentityVerified": True,
    }


def component_delta(stable, lab):
    require(stable["schema"] == SCHEMA and lab["schema"] == SCHEMA,
            "wire ledger schema mismatch")
    difference = {
        key: lab["ledger"][key] - stable["ledger"][key] for key in COMPONENTS
    }
    require(sum(difference.values()) == lab["physicalBytes"] - stable["physicalBytes"],
            "A/B component deltas do not sum to measured patch bytes")
    return difference
