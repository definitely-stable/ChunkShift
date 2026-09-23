#!/usr/bin/env python3
"""Generate independent CSM v1 candidate SHA-256 vectors.

This tool intentionally does not call ChunkShift code. It uses only Python's
standard library and the byte layout documented in CSM-V1-CANDIDATE.md.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

FORMAT_MAJOR = 1
PREAMBLE_SIZE = 32
TRAILER_SIZE = 64
REQUIRED = 1
MAX_CHUNKS_PER_BLOCK = 4096

HASH_SUITE_ID = b"chunkshift.sha256.v1"
PROFILE_ID = b"fixture.csm.synthetic.v1"
PROFILE_FINGERPRINT = hashlib.sha256(
    b"chunkshift.csm.fixture.profile.v1"
).digest()
MANIFEST_DOMAIN = b"chunkshift.manifest-id.v1\0"


def section_header(kind: bytes, flags: int, payload_length: int) -> bytes:
    if len(kind) != 4:
        raise ValueError("FourCC must be four bytes")
    return kind + struct.pack("<IQ", flags, payload_length)


def crc32c(data: bytes) -> int:
    state = 0xFFFFFFFF
    polynomial = 0x82F63B78

    for value in data:
        state ^= value
        for _ in range(8):
            state = (state >> 1) ^ (polynomial if state & 1 else 0)

    return (~state) & 0xFFFFFFFF


def manifest_id(entries: list[tuple[bytes, int]]) -> tuple[bytes, int]:
    digest = hashlib.sha256()
    digest.update(MANIFEST_DOMAIN)
    digest.update(struct.pack("<H", len(HASH_SUITE_ID)))
    digest.update(HASH_SUITE_ID)
    digest.update(struct.pack("<H", len(PROFILE_ID)))
    digest.update(PROFILE_ID)
    digest.update(PROFILE_FINGERPRINT)

    total_length = 0
    for chunk_id, length in entries:
        digest.update(chunk_id)
        digest.update(struct.pack("<I", length))
        total_length += length

    digest.update(struct.pack("<QQ", len(entries), total_length))
    return digest.digest(), total_length


def core_section() -> bytes:
    payload = bytearray(56)
    payload[16:48] = PROFILE_FINGERPRINT
    struct.pack_into(
        "<HHI",
        payload,
        48,
        len(HASH_SUITE_ID),
        len(PROFILE_ID),
        0,
    )
    payload += HASH_SUITE_ID
    payload += PROFILE_ID
    return section_header(b"CORE", REQUIRED, len(payload)) + payload


def chunk_block(
    entries: list[tuple[bytes, int]],
    first_index: int,
    first_content_offset: int,
) -> bytes:
    count = len(entries)
    prefix = struct.pack(
        "<IIQQ",
        count,
        0,
        first_index,
        first_content_offset,
    )
    ids = b"".join(chunk_id for chunk_id, _ in entries)
    lengths = b"".join(
        struct.pack("<I", length)
        for _, length in entries
    )
    payload_length = 24 + count * 32 + count * 4 + 4
    header = section_header(b"CBLK", REQUIRED, payload_length)
    without_crc = header + prefix + ids + lengths
    return without_crc + struct.pack("<I", crc32c(without_crc))


def build(
    entries: list[tuple[bytes, int]],
    include_block_index: bool,
) -> tuple[bytes, dict[str, object]]:
    output = bytearray(
        b"CSM1"
        + struct.pack(
            "<HHQQQ",
            FORMAT_MAJOR,
            PREAMBLE_SIZE,
            0,
            0,
            0,
        )
    )

    core_offset = len(output)
    output += core_section()

    block_index: list[tuple[int, int]] = []
    chunk_index = 0
    content_offset = 0
    first_cblk_offset = 0

    for start in range(0, len(entries), MAX_CHUNKS_PER_BLOCK):
        block = entries[start : start + MAX_CHUNKS_PER_BLOCK]
        cblk_offset = len(output)

        if not block_index:
            first_cblk_offset = cblk_offset

        block_index.append((content_offset, cblk_offset))
        output += chunk_block(block, chunk_index, content_offset)

        chunk_index += len(block)
        content_offset += sum(length for _, length in block)

    logical_id, total_length = manifest_id(entries)

    cend_offset = len(output)
    cend_payload = (
        struct.pack("<QQ", len(entries), total_length)
        + logical_id
    )
    output += section_header(
        b"CEND",
        REQUIRED,
        len(cend_payload),
    )
    output += cend_payload

    bidx_offset = 0
    if include_block_index:
        bidx_offset = len(output)
        bidx_payload = struct.pack("<II", 1, len(block_index))
        bidx_payload += b"".join(
            struct.pack("<QQ", content, physical)
            for content, physical in block_index
        )
        output += section_header(
            b"BIDX",
            0,
            len(bidx_payload),
        )
        output += bidx_payload

    foot_offset = len(output)
    foot_payload = struct.pack(
        "<QQQQQQ",
        core_offset,
        cend_offset,
        bidx_offset,
        first_cblk_offset if entries else 0,
        len(block_index),
        0,
    )
    output += section_header(
        b"FOOT",
        REQUIRED,
        len(foot_payload),
    )
    output += foot_payload

    file_digest = hashlib.sha256(output).digest()
    physical_length = len(output) + TRAILER_SIZE
    output += (
        b"CSMT"
        + struct.pack(
            "<HHQQ",
            FORMAT_MAJOR,
            TRAILER_SIZE,
            foot_offset,
            physical_length,
        )
        + file_digest
        + struct.pack("<Q", 0)
    )

    metadata = {
        "manifestId": logical_id.hex(),
        "fileDigest": file_digest.hex(),
        "chunkCount": len(entries),
        "contentLength": total_length,
        "physicalLength": len(output),
        "cblkCount": len(block_index),
        "hasBidx": include_block_index,
    }
    return bytes(output), metadata


def fixtures() -> dict[str, tuple[bytes, dict[str, object]]]:
    one_payload = b"hello chunkshift csm fixture"
    one = [
        (
            hashlib.sha256(one_payload).digest(),
            len(one_payload),
        )
    ]
    multi = [
        (
            hashlib.sha256(f"chunk-{index}".encode("ascii")).digest(),
            (index % 4096) + 1,
        )
        for index in range(4097)
    ]

    definitions = {
        "empty-sha256-no-bidx.csm": ([], False),
        "one-entry-sha256-no-bidx.csm": (one, False),
        "multiblock-sha256-no-bidx.csm": (multi, False),
        "multiblock-sha256-bidx.csm": (multi, True),
    }
    return {
        name: build(entries, bidx)
        for name, (entries, bidx) in definitions.items()
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--out",
        type=Path,
        required=True,
        help="Directory that receives .csm vectors and vectors.json.",
    )
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    metadata: dict[str, object] = {
        "format": "CSM v1 candidate",
        "generator": "tools/csm-fixtures/generate.py",
        "hashSuite": HASH_SUITE_ID.decode("ascii"),
        "profileId": PROFILE_ID.decode("ascii"),
        "profileFingerprint": PROFILE_FINGERPRINT.hex(),
        "vectors": {},
    }

    for name, (data, vector_metadata) in fixtures().items():
        (args.out / name).write_bytes(data)
        metadata["vectors"][name] = vector_metadata

    (args.out / "vectors.json").write_text(
        json.dumps(metadata, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
