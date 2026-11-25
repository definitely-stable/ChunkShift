# R1-B3b — provisional raw pack layout and admission

Status: **ACCEPTED in #481; not a frozen wire format**. Exact head `9e41998a2b96ebca0768a88179ee96d03a68dac0`: [CI](https://github.com/definitely-stable/ChunkShift/actions/runs/45568377266), [Heavy](https://github.com/definitely-stable/ChunkShift/actions/runs/45568377302), CodeQL and host PASS; Repository 86/86 on Windows/Linux x64/ARM64. Merge `15a492130ff890d3cc64e284df59b6c9617c3895`. Owners #144 / #133 / #10.
Independent arithmetic/policy slice, no writer dependency. R1-B3a #480 accepted;
combined-head CI passed. See #144 for layout alternatives, compression, locality and freeze.

## Provisional experiment: raw layout P0

All integers unsigned little endian on wire; implementation uses checked
nonnegative Int64 offsets/lengths and Int32 counts for .NET bounds. No padding,
no inline frame header. Section order:

| Section | Bytes | Fields in order |
|---|---:|---|
| Header | 64 | magic[8], version u16, headerBytes u16, suiteCode u16, flags u16, opaque PackId[32], reserved[16] |
| Raw payload | D | concatenated raw frames, physical order |
| FrameDirectory | 32*N | each: absolute offset u64, raw length u64, stored length u64, CRC32C u32, encoding u16, flags u16 |
| HashIndex | 36*N | each: ChunkId[32], frame ordinal u32 |
| Footer | 64 | magic[8], frame count u32, flags u32, payload length u64, directory offset/length u64/u64, hash index offset/length u64/u64, reserved u64 |
| Fixed trailer | 40 | magic[8], footer offset u64, footer length u32, version u16, flags u16, full physical length u64, CRC32C u32, reserved u32 |

Provisional distinct magic bytes, version, suite-code mapping, CRC byte domains
and exact reserved/feature validation must be specified alongside the first
writer/independent reader. No claim that any serialized bytes exist yet.
Use one suite/pack. Raw encoding only: stored length = raw length, no dictionaries.
HashIndex ordered lexicographically by exact digest bytes; ordinals are a
bijection onto the directory. Directory order is physical append order.
Zero-length logical frames allowed, equal adjacent offsets only as implied by
zero length. Empty pack invalid. Duplicate logical keys forbidden within pack;
reject unverified claimed duplicates, do not treat as successful reuse.

PackId generated independently, 256-bit opaque value; PackDigest is an
external strong SHA-256 digest of the complete serialized file (including
trailer), not embedded in its own hash domain. Expected digest must be bound
by trusted future physical metadata; CRC is accidental corruption detection,
not authentication. This is the raw experimental proposal, not the freeze.

## Exact arithmetic

D = sum raw lengths; N = frame count.
Fixed = 64+64+40 = 168. Index = (32+36)*N = 68*N.
Physical = 168+D+68*N. Minimum one-frame pack = 236 bytes.
Offsets computed forward with overflow rejection, no allocation based on count.
A zero-frame planning state has 168 overhead but is not serializable.

The separate limits are physical bytes, frame count, index bytes and target
physical bytes. Target is policy, not wire semantics. A candidate that cannot
fit in an empty pack is rejected before rotation. An oversized target chunk
that fits hard ceilings is admitted as a dedicated one-frame pack. On nonempty
pack rotate on suite change, frame limit, index limit, physical limit or target
overflow (priority in that order). Exact fit is allowed. The next chunk after
a dedicated oversized pack must rotate; no splitting/rechunking. A caller must
re-evaluate in empty state after rotation. No silent data dropping, no dedup
claim based on this pure policy.

This slice supplies decisions, not actual file rotation or aggregate pack
resource accounting. R1-B3c integrates admission with a forward-only writer,
serializes sections, verifies physical size and digest, and creates an independent
reader/parser; actual coordinator must use the same limits before source reads.
A provisional layout may change after #138/#146/#144 metadata benchmarks.

## Evidence

Exact arithmetic, offset boundaries, long.MaxValue/int.MaxValue, both suites,
zero-length frames, impossible chunks, each independent ceiling, invalid
configuration/current states, and 32 x 128 seeded admission/rotation steps
against a simple independent size formula. No runtime storage, performance,
fuzz parser, process-crash or power-loss evidence. Exact-head hosted CI required.
