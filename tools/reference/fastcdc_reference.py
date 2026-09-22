#!/usr/bin/env python3
"""Independent scalar verifier for ChunkShift FastCDC v1 candidate semantics."""

from __future__ import annotations

import argparse
import hashlib
import pathlib
import re
import struct
import sys

EXPECTED_GEAR_SHA256 = "91a3061015ae351cd3701852712bcd6aa4a1ce26c8a231d3969432b00f028f88"
MASKS = {
    5: 0x0000000001804110,
    6: 0x0000000001803110,
    7: 0x0000000018035100,
    8: 0x0000001800035300,
    9: 0x0000019000353000,
    10: 0x0000590003530000,
    11: 0x0000D90003530000,
    12: 0x0000D90103530000,
    13: 0x0000D90303530000,
    14: 0x0000D90313530000,
    15: 0x0000D90F03530000,
    16: 0x0000D90303537000,
    17: 0x0000D90703537000,
    18: 0x0000D90707537000,
    19: 0x0000D91707537000,
    20: 0x0000D91747537000,
    21: 0x0000D91767537000,
    22: 0x0000D93767537000,
    23: 0x0000D93777537000,
    24: 0x0000D93777577000,
    25: 0x0000DB3777577000,
}
EXPECTED_RANDOM_1M_64K = [
    (0, 100081),
    (100081, 33106),
    (133187, 69903),
    (203090, 49442),
    (252532, 103705),
    (356237, 144977),
    (501214, 214287),
    (715501, 145480),
    (860981, 50981),
    (911962, 37677),
    (949639, 79457),
    (1029096, 19480),
]


def load_gear(root: pathlib.Path) -> list[int]:
    path = root / "docs" / "architecture" / "FASTCDC-GEAR-V1.txt"
    values: list[int] = []

    for line in path.read_text(encoding="utf-8").splitlines():
        match = re.fullmatch(r"\d{3}\s+0x([0-9a-fA-F]{16})", line)
        if match:
            values.append(int(match.group(1), 16))

    if len(values) != 256:
        raise ValueError(f"expected 256 GEAR values, got {len(values)}")

    serialized = b"".join(struct.pack("<Q", value) for value in values)
    actual = hashlib.sha256(serialized).hexdigest()
    if actual != EXPECTED_GEAR_SHA256:
        raise ValueError(f"GEAR digest mismatch: {actual}")

    return values


def xorshift_bytes(length: int, seed: int) -> bytes:
    state = seed & 0xFFFFFFFF
    output = bytearray(length)

    for index in range(length):
        state ^= (state << 13) & 0xFFFFFFFF
        state ^= state >> 17
        state ^= (state << 5) & 0xFFFFFFFF
        state &= 0xFFFFFFFF
        output[index] = state & 0xFF

    return bytes(output)


def find_cut(source: memoryview, minimum: int, target: int, maximum: int, gear: list[int]) -> int:
    remaining = min(len(source), maximum)
    if remaining <= minimum:
        return remaining

    bits = target.bit_length() - 1
    strict = MASKS[bits + 1]
    relaxed = MASKS[bits - 1]
    center = min(target, remaining)
    rolling = 0

    for index in range(minimum, center):
        rolling = ((rolling << 1) + gear[source[index]]) & 0xFFFFFFFFFFFFFFFF
        if rolling & strict == 0:
            return index

    for index in range(center, remaining):
        rolling = ((rolling << 1) + gear[source[index]]) & 0xFFFFFFFFFFFFFFFF
        if rolling & relaxed == 0:
            return index

    return remaining


def chunk(source: bytes, minimum: int, target: int, maximum: int, gear: list[int]) -> list[tuple[int, int]]:
    view = memoryview(source)
    output: list[tuple[int, int]] = []
    offset = 0

    while offset < len(source):
        length = find_cut(view[offset:], minimum, target, maximum, gear)
        if length <= 0:
            raise ValueError("non-positive FastCDC cut")
        output.append((offset, length))
        offset += length

    return output


def verify(root: pathlib.Path) -> None:
    gear = load_gear(root)
    source = xorshift_bytes(1024 * 1024, 0x12345678)
    actual = chunk(source, 16 * 1024, 64 * 1024, 256 * 1024, gear)

    if actual != EXPECTED_RANDOM_1M_64K:
        raise ValueError(f"golden vector mismatch:\nexpected={EXPECTED_RANDOM_1M_64K}\nactual={actual}")

    print(f"FastCDC independent vector OK: {len(actual)} chunks; gear={EXPECTED_GEAR_SHA256}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    root = pathlib.Path(__file__).resolve().parents[2]

    if args.verify:
        verify(root)
        return 0

    gear = load_gear(root)
    source = xorshift_bytes(1024 * 1024, 0x12345678)
    print(chunk(source, 16 * 1024, 64 * 1024, 256 * 1024, gear))
    return 0


if __name__ == "__main__":
    sys.exit(main())
