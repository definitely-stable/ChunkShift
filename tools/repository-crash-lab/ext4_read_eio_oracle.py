"""Locked geometry and independent evidence checks for one data-block read EIO."""
import struct
from integrated_crash_oracle import PACK_BYTES, require, sha

BLOCK = 4096
OFFSET = 4096
SECTOR = 512
SIZE = 16 * 1024 * 1024
# Linux little-endian x64/arm64 UAPI: 32-byte fiemap, 56-byte fiemap_extent.
FIEMAP = 0xC020660B


def decode_extent(buffer):
    require(len(buffer) == 88, "wrong FIEMAP buffer size")
    start, length, flags, mapped, count, reserved = struct.unpack_from("<QQIIII", buffer)
    require(start == OFFSET and length == BLOCK and flags == 0 and mapped == count == 1 and
            reserved == 0, "unexpected FIEMAP header")
    logical, physical, extent, r0, r1, eflags, r2, r3, r4 = struct.unpack_from("<QQQQQIIII", buffer, 32)
    # Reject unknown, delayed, encoded, unwritten, shared and all other flags.
    require(eflags in (0, 1) and not (r0 or r1 or r2 or r3 or r4) and
            logical % BLOCK == physical % BLOCK == extent % BLOCK == 0 and
            logical <= OFFSET and OFFSET + BLOCK <= logical + extent and
            0 < physical and physical + extent <= SIZE, "unusable FIEMAP extent")
    target = physical + OFFSET - logical
    require(0 < target and target + BLOCK < SIZE, "target outside private mapping")
    return target


def error_table(identity, target):
    require(0 < target and target % BLOCK == 0 and target + BLOCK < SIZE, "invalid target")
    first = target // SECTOR
    count = BLOCK // SECTOR
    total = SIZE // SECTOR
    return (f"0 {first} linear {identity} 0\n"
            f"{first} {count} error\n"
            f"{first + count} {total - first - count} linear {identity} {first + count}")


def verify_failure(result, expected, native_errno):
    require(len(expected) == PACK_BYTES and native_errno == 5 and
            result == dict(kind="native-read-eio", hresult=5, type="IOException",
                           accepted=False, bytes=PACK_BYTES, expected_sha256=sha(expected)),
            "filesystem EIO witness differs")
