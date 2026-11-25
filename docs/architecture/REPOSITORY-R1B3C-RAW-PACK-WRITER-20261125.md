# R1-B3c-W — provisional forward-only raw pack writer

Status: **Candidate; R1-B3b #481 accepted, refreshed combined CI required**.
Owners #144 / #133 / #10. This is actual P0 serialization, not a Pack v1 freeze.
The independent reader, actual multi-pack coordinator and rebuild follow.
Do not treat writer completion as durable storage, verified readback or publication.

## Exact wire rules

Uses [R1-B3b P0 layout](REPOSITORY-R1B3B-RAW-PACK-LAYOUT-20261125.md).
Header magic ASCII CSRAWPK0, footer CSRAWFT0, trailer CSRAWTR0. Version u16 = 1
within this experimental magic namespace only. Suite 1 = BLAKE3-256 v1,
2 = SHA-256 v1. Raw encoding = 0. All flags/reserved bytes zero. Integer fields
unsigned little endian, offsets absolute. Frame directory ordinal implicit;
HashIndex sorts full 32-byte ChunkId lexicographically, stores u32 ordinal.
Each frame CRC32C is reflected Castagnoli (0x82f63b78), init/final xor all ones,
over exact stored bytes. Trailer CRC32C covers bytes [0,32) of the 40-byte
trailer; remaining reserved u32 must be zero. External PackDigest is SHA-256
over all serialized bytes including trailer. Opaque PackId generated from 32
cryptographically random bytes; no allocation registry or collision protocol
yet. PackId and PackDigest are distinct internal types. No self-referential digest.

## Writer and integrity contract

PrivateRawPackWriter takes an initially empty writable caller-owned Stream;
seekable streams are checked for position/length zero. Nonseekable streams
are allowed under the same caller contract. Caller must exclusively control
both source and destination, including absence of aliasing through wrappers.
The writer does not close, delete or promote either stream. The caller must
discard the entire private output on error; a future coordinator owns files,
resource credits and retry cleanup. Header written lazily on first admission.
Append uses RawPackPolicy before reading/writing, including all final physical
metadata bytes. Rotation decisions leave source/destination unchanged; caller
must actually rotate and retry. Writer suite fixed from construction. Duplicate
within a pack never returns successful reuse; a duplicate may first receive a
rotation decision, so the coordinator must separately enforce transaction dedup.

One active operation, fail-closed after admission. Read/hash/write bounded
windows, exact EOF and ChunkId check, no truncation rollback. Hash and CRC of
actual submitted bytes, not readback of storage; a dishonest destination or
corrupt storage is outside writer contract. Complete appends batched directory,
hash-sorted index, footer and fixed trailer, flushes, and checks its logical
byte counter equals exact projected physical length. Only then return descriptor.
No seek or read of destination; no full-file second pass. No power-loss claim.

Memory: requested 64 KiB pool window per active operation and O(frame count)
metadata, seen-key table and sorted array during finalization. Hard count/index
limits bound this; serialized index size is not an RSS bound. CRC implementation
is portable table baseline, not measured hardware-accelerated optimum.
No new package dependency or Core hash identity change.

## Tests and next

Literal section offset checks with both suites, independent whole-file SHA-256
and bitwise trailer CRC, known CRC vector, nonseekable output, rejection,
duplicate, malformed logical content, injected tail failure, multi-window and
large cold sections. These are writer checks, not an independent general
reader/parser or corruption/fuzz evidence. Next R1-B3c-R: independent bounded
parser, exact sections, flags, ordinal bijection and index order, frame CRC,
logical digest and trusted expected descriptor verification, negative corpus.
Then private file/rotation ownership and R1-B3d rebuild. Exact-head hosted CI
required on combined head; no merge without fresh full gates.
