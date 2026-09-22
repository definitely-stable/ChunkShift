# CSM v1 candidate binary specification

Status: Candidate for M1/#5; not public-frozen until #9  
Date: 2026-09-22  
Authority: RFC-0001 section 8 + RFC-0003 release scope

This document turns the high-level CSM architecture into an implementable candidate. Any incompatible change before #9 is permitted only with updated vectors/spec; after publication, format-version rules apply independently from the NuGet package version.

## 1. Byte order and common rules

- all integers are little-endian;
- all offsets/counts are unsigned unless explicitly stated;
- persistent IDs are exactly 32 bytes;
- chunk lengths are UInt32;
- identifier strings are UTF-8 ASCII-compatible ChunkShift IDs and MUST be 1..128 bytes;
- checked arithmetic is mandatory before allocation, seek or length multiplication;
- reserved fields MUST be zero;
- unknown required features fail;
- unknown optional physical sections may be skipped without materialization;
- a reader MUST NOT allocate based solely on an untrusted length before validating configured/hard limits.

## 2. Physical order

```text
PREAMBLE
CORE
CBLK*
CEND
[AUX]*
[BIDX]
FOOT
TRAILER
```

The writer emits forward-only through CEND. Optional AUX/BIDX/FOOT/TRAILER finalization may follow after source scanning; no header backpatching is required.

## 3. PREAMBLE — fixed 32 bytes

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | Magic = ASCII `CSM1` |
| 4 | 2 | FormatMajor = 1 |
| 6 | 2 | PreambleSize = 32 |
| 8 | 8 | RequiredPhysicalFeatures |
| 16 | 8 | OptionalPhysicalFeatures |
| 24 | 8 | Reserved = 0 |

A v1 reader rejects a different magic/major, a preamble smaller than 32, non-zero reserved bytes, or unknown required physical bits. A larger future preamble may be skipped only when its added bytes are declared optional by the format-version rules.

## 4. Section header — fixed 16 bytes

Every section after PREAMBLE and before TRAILER starts with:

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | Type FourCC |
| 4 | 4 | Flags |
| 8 | 8 | PayloadLength |

Known v1 types: `CORE`, `CBLK`, `CEND`, `AUX0`, `BIDX`, `FOOT`.

Section flag bit 0 means REQUIRED. All other v1 flag bits are reserved and MUST be zero.

Unknown section behavior:

- REQUIRED set -> fail;
- REQUIRED clear -> skip exactly PayloadLength bytes without allocating the payload.

Section length plus header must fit the remaining physical file/range using checked UInt64 arithmetic.

## 5. CORE payload

Fixed prefix: 56 bytes.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 8 | RequiredSemanticFeatures |
| 8 | 8 | OptionalSemanticFeatures |
| 16 | 32 | ProfileFingerprint |
| 48 | 2 | HashSuiteIdLength |
| 50 | 2 | ChunkingProfileIdLength |
| 52 | 4 | ExtensionBytes |

Then, in order:

```text
HashSuiteId UTF-8 bytes
ChunkingProfileId UTF-8 bytes
ExtensionBytes of canonical TLVs
```

Rules:

- both IDs use the ChunkShift ID grammar and are at most 128 bytes;
- v1 hard limit for ExtensionBytes is 65,536;
- CORE PayloadLength MUST equal `56 + HashSuiteIdLength + ChunkingProfileIdLength + ExtensionBytes`;
- unknown required semantic feature bits fail;
- canonical TLVs are cold semantic metadata only, never per-chunk records.

The exact TLV registry remains empty until a concrete semantic extension is accepted.

## 6. CBLK payload

Each block represents 1..4096 logical chunks.

Fixed block prefix: 24 bytes.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | ChunkCount |
| 4 | 4 | Reserved = 0 |
| 8 | 8 | FirstChunkIndex |
| 16 | 8 | FirstContentOffset |

Then:

```text
ChunkId[ChunkCount]       // 32 bytes each
Length[ChunkCount]        // UInt32 each
BlockCrc32C               // UInt32
```

Expected PayloadLength:

```text
24 + ChunkCount*32 + ChunkCount*4 + 4
```

`BlockCrc32C` covers the 16-byte CBLK section header plus all CBLK payload bytes preceding the CRC field.

Rules:

- ChunkCount 0 is invalid;
- ChunkCount > 4096 is invalid in CSM v1;
- FirstChunkIndex MUST equal the global number of chunks preceding this block;
- FirstContentOffset MUST equal the sum of preceding logical chunk lengths;
- every chunk Length MUST be > 0;
- prefix sums use checked UInt64 arithmetic;
- CBLK grouping is physical and does not enter ManifestId.

## 7. CEND payload — fixed 48 bytes

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 8 | TotalChunkCount |
| 8 | 8 | TotalContentLength |
| 16 | 32 | ManifestId |

The reader verifies that totals match the streamed CBLK sequence.

## 8. ManifestId canonical calculation

ManifestId is logical and independent of physical grouping/indexes.

Using the manifest-selected HashSuite:

```text
ManifestId = HashSuite.Hash(
    UTF8("chunkshift.manifest-id.v1\0")
    || UInt16LE(HashSuiteIdByteLength)
    || UTF8(HashSuiteId)
    || UInt16LE(ChunkingProfileIdByteLength)
    || UTF8(ChunkingProfileId)
    || ProfileFingerprint[32]
    || UInt64LE(TotalChunkCount)
    || UInt64LE(TotalContentLength)
    || repeated {
         ChunkId[32]
         UInt32LE(Length)
       }
)
```

The following MUST NOT enter ManifestId:

- CBLK boundaries;
- CBLK/section offsets;
- BIDX presence/content;
- AUX sections;
- diagnostics/timestamps;
- physical FileDigest;
- optional ContentId.

Changing any canonical semantic field above changes ManifestId.

## 9. AUX sections

`AUX0` is an optional physical section family and MUST NOT affect ManifestId.

CSM v1 defines no mandatory AUX payload. Readers skip unknown optional AUX sections by PayloadLength without materialization. A future semantic extension cannot be smuggled into AUX; semantic data belongs in CORE feature bits/TLVs and requires normal compatibility rules.

## 10. BIDX payload

BIDX is optional and physical.

Fixed prefix:

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | IndexVersion = 1 |
| 4 | 4 | BlockCount |

Each entry is 16 bytes:

| Size | Field |
| ---: | --- |
| 8 | FirstContentOffset |
| 8 | CblkSectionFileOffset |

Entries are strictly increasing by both content offset and file offset and correspond one-to-one with CBLK sections.

BIDX enables content-position-to-block lookup without storing a UInt64 offset per chunk. Presence/absence does not change ManifestId.

## 11. FOOT payload — fixed 48 bytes

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 8 | CoreSectionOffset |
| 8 | 8 | CendSectionOffset |
| 16 | 8 | BidxSectionOffset, 0 when absent |
| 24 | 8 | FirstCblkSectionOffset, 0 when no chunks |
| 32 | 8 | CblkCount |
| 40 | 8 | Reserved = 0 |

Offsets identify section headers, not payload starts.

## 12. TRAILER — fixed 64 bytes

TRAILER is not preceded by a section header.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | Magic = ASCII `CSMT` |
| 4 | 2 | FormatMajor = 1 |
| 6 | 2 | TrailerSize = 64 |
| 8 | 8 | FootSectionOffset |
| 16 | 8 | PhysicalLength |
| 24 | 32 | FileDigest |
| 56 | 8 | Reserved = 0 |

`PhysicalLength` is the complete byte length including TRAILER.

`FileDigest` is the manifest-selected HashSuite digest of every physical byte before the TRAILER. The TRAILER is excluded to avoid self-reference.

A tail reader can fetch the fixed 64-byte TRAILER, locate FOOT, then locate CORE/CEND/BIDX.

## 13. Reader resource bounds

A conforming v1 reader enforces at least:

- CORE extension bytes <= 65,536;
- identifier byte lengths <= 128 each;
- CBLK ChunkCount <= 4096;
- every CBLK computed payload size matches PayloadLength exactly;
- every offset/count/length multiplication/addition is checked;
- section PayloadLength cannot exceed remaining physical bytes;
- optional unknown sections are skipped/streamed, not blindly allocated;
- total content length/count must match CEND and observed chunks;
- lengths requiring a .NET `long`/memory operation must additionally fit that implementation boundary.

Configurable operational limits may be stricter than the format maxima.

## 14. Verification levels

### Structural

Validate magic/version/lengths/order/features/reserved zeros/checked arithmetic.

### Block integrity

Validate every CBLK CRC32C before accepting that block.

### Logical integrity

Recompute ManifestId from CORE semantics plus ordered `(ChunkId, Length)` records and compare with CEND.

### Physical integrity

Recompute FileDigest over all bytes before TRAILER and compare with TRAILER.

Hash equality proves integrity against a trusted expected value; it does not by itself provide authenticity/signature semantics.

## 15. Streaming/finalization behavior

Writer:

1. emits PREAMBLE and CORE;
2. consumes source chunks and emits CBLKs;
3. incrementally computes ManifestId inputs and physical FileDigest;
4. emits CEND;
5. optionally emits AUX/BIDX;
6. emits FOOT;
7. finalizes FileDigest over bytes through FOOT;
8. emits TRAILER.

Reader must support forward streaming through CORE/CBLK/CEND without needing BIDX/FOOT materialization. Seek/tail-index use is an optimization, not a semantic requirement.

## 16. Golden vectors before #9

Frozen Core 0.1.0 fixtures must include at least:

- empty manifest;
- one chunk;
- multiple CBLKs;
- optional BIDX present/absent with identical ManifestId;
- malformed/truncated section headers;
- impossible/overflowing lengths;
- bad CBLK CRC32C;
- mismatched CEND totals;
- bad ManifestId;
- bad FileDigest;
- unknown required section/feature rejection;
- unknown optional section skip;
- equivalent logical manifest encoded with permitted physical differences.

At least one small independent verifier/generator must check the release-candidate vectors before #9 closes.
