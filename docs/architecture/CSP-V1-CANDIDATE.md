# CSP v1 candidate patch specification

Status: Candidate (draft for review; not frozen; no encoder exists)  
Date: 2026-09-27  
Authority: RFC-0001 section 9 + PLAN.md §12 + [#66](https://github.com/definitely-stable/ChunkShift/issues/66)

This document turns the CSP paragraphs of RFC-0001 §9 into an implementable candidate. Nothing in it is accepted, and no encoder, decoder or consumer may treat it as a compatibility baseline, until the owner decisions in section 12 are taken and the fixtures of section 11 exist. The CSP format version is independent from the NuGet package version, from the CSM format version and from the chunking-profile version (RFC-0001 §15): editing this file changes no package, and releasing a package does not freeze this format. Every size and limit below is a candidate value of this draft.

## 1. Scope and non-goals

CSP is a declarative content-update artifact:

- it carries a target manifest and the target chunk bytes that are not already present in a base;
- apply is a finite, pre-described sequence of chunk resolutions: a target chunk taken from the base by `ChunkId`, or a payload entry that is a literal or dictionary-relative encoding of the target chunk bytes;
- there is no instruction stream, no opcode VM and no executable content of any kind. A VCDIFF-style COPY/INSERT instruction VM stays rejected (RFC-0001 §9): it would make the patch executable and its outcome dependent on interpreter state.

Non-goals:

- CSP is not a transport or repository format. Storage, HTTP Range layout and pack placement belong to [#10](https://github.com/definitely-stable/ChunkShift/issues/10)/[#13](https://github.com/definitely-stable/ChunkShift/issues/13) (RFC-0001 §12). A CSP artifact is a byte sequence that can be stored or streamed anywhere.
- CSP does not define signatures or authenticity. A hash is not a signature (RFC-0001 §13.2); authenticity is a detached trust envelope layered outside the patch (RFC-0004 §4). No signature, key or trust slot exists inside CSP, including in optional sections, and none may enter a content identity.
- CSP does not define anti-rollback or update-policy metadata (RFC-0004 §5).
- CSP v1 does not select, bundle or require a compression library. Compression may become a payload encoding only under section 5.3 and RFC-0004 §3.

## 2. Byte order and common rules

CSP v1 inherits CSM-V1-CANDIDATE §1 unchanged; those rules apply verbatim to every CSP field:

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

Additional conventions:

- four-byte ASCII FourCC section types are encoded exactly as CSM §4 encodes them, and CRC-32C uses the exact CSM §6 contract: Castagnoli parameters, `check("123456789") = 0xE3069283`, the numeric value stored UInt32 little-endian, coverage equal to the 16-byte section header plus every payload byte preceding the CRC field;
- CSP defines no semantic-feature registry of its own. HashSuite, `ChunkingProfileId` and `ProfileFingerprint` semantics come from the embedded target CSM.

## 3. Identities a patch binds

A CSP artifact binds a transformation from an optional base identity to a required target identity. All identities are 256-bit.

### 3.1 Target ManifestId (required)

The patch embeds the complete target CSM byte-for-byte (section 4.5), and the target `ManifestId` is the embedded CSM's `ManifestId` as the existing CSM reader verifies it (CSM-V1-CANDIDATE §14). A patch without a valid target `ManifestId` has no defined target and MUST be rejected. Apply produces exactly the content that manifest describes: the ordered concatenation of its `(ChunkId, Length)` records.

### 3.2 Target FileDigest and ContentLength

The embedded target CSM carries, and the patch therefore binds:

- `FileDigest` (CSM TRAILER): the physical digest of the CSM artifact bytes themselves, verified by the CSM reader; it proves the embedded manifest is intact, not that reconstructed content is correct;
- `TotalContentLength` (CSM CEND): the exact reconstructed payload length, which apply MUST compare against the bytes written.

CSP v1 defines no separate whole-content digest. Reconstruction is bound chunk-by-chunk (section 9). If a later revision records an optional `ContentId` in the target manifest (RFC-0001 §5.2), apply MAY verify it in addition; nothing in v1 depends on it.

### 3.3 Expected base ManifestId (optional)

RFC-0001 §9 makes the expected base `ManifestId` optional. Candidate rule: a patch MAY bind one through the optional `BASE` section (section 4.4). Absence means the patch makes no claim about which base the applier holds.

Consequence of absence: apply MUST still verify every reused chunk by `ChunkId` and the finished output against the embedded target CSM (`TotalContentLength` plus per-chunk hashes, section 9). "No expected base" weakens the claim about the input, never the verification of the output. Whether to keep the binding optional is decision (b).

### 3.4 HashSuite and profile compatibility

- The base manifest and the target manifest MUST declare the same `HashSuiteId`. `ChunkId` values are comparable only within one HashSuite, and CSP v1 does not re-hash base chunks under a different suite. A base manifest with a different HashSuite is rejected (section 7, rule 20).
- The base manifest's `ProfileFingerprint` is not an apply requirement. Reuse is decided by `ChunkId` equality alone, and different chunking profiles can still share identical chunk bytes. Patch creation SHOULD use a base with the same `ProfileFingerprint`, because a different profile lowers reuse and inflates the payload. Whether apply should reject a different fingerprint is decision (g).
- Target-side profile semantics are unchanged from CSM §14: an unregistered `ChunkingProfileId` does not block manifest-only checks, while the optional re-chunk verification of section 9.5 requires a reader that registers the embedded CSM's profile and fingerprint.

### 3.5 Patch identity

CSP v1 defines no `PatchId` field. The patch is identified by the target `ManifestId` and, when `BASE` is present, the expected base `ManifestId`, that is, by the pair `(base ManifestId, target ManifestId)`; its physical identity is the CSP TRAILER `FileDigest` plus `PhysicalLength`.

A persisted `PatchId` would either restate the physical `FileDigest` or be a deterministic function of that pair under a domain string; neither adds information a consumer of an update cannot derive. RFC-0001 §13.2 requires `PatchId` to be signable by a detached envelope; the envelope can bind the pair and the physical digests directly, or a later decision can define a domain-separated digest without storing an unused field. Whether to define one is decision (f).

## 4. Physical order

```text
PREAMBLE
TCSM
[BASE]
PAYL*
[AUX0]*
PIDX
FOOT
TRAILER
```

### 4.1 Section state machine

A conforming v1 physical stream follows this exact state machine:

```text
PREAMBLE
  -> exactly one TCSM
  -> zero or one optional BASE
  -> zero or more PAYL
  -> zero or more optional AUX0*
  -> exactly one PIDX
  -> exactly one FOOT
  -> exactly one fixed TRAILER at physical EOF
```

Sections are contiguous: every section starts where the previous one ends, there is no padding, and the TRAILER starts immediately after the FOOT payload. The reader MUST reject:

- duplicate TCSM, BASE, PIDX or FOOT;
- BASE after the first PAYL, and PAYL before TCSM or after the optional phase;
- an optional section outside the optional phase, which lies after the last PAYL (or after TCSM/BASE when no PAYL exists) and before PIDX;
- any section after FOOT, and any bytes after TRAILER;
- missing TCSM, PIDX, FOOT or TRAILER.

Unknown optional physical sections are permitted only in the optional phase and are skipped by `PayloadLength` without materialization. A writer MUST NOT place semantic data in the optional phase; an unknown optional section cannot demand to be understood.

### 4.2 PREAMBLE — fixed 32 bytes (candidate)

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | Magic = ASCII `CSP1` |
| 4 | 2 | FormatMajor = 1 |
| 6 | 2 | PreambleSize = 32 |
| 8 | 8 | RequiredPhysicalFeatures |
| 16 | 8 | OptionalPhysicalFeatures |
| 24 | 8 | Reserved = 0 |

The current candidate defines no physical feature bits: writers MUST emit both fields as zero, and a reader MUST reject a different magic/major, a different `PreambleSize`, non-zero reserved bytes and unknown required physical bits. Optional physical bits are reserved for a future revision that also defines how a reader that does not understand them behaves; this candidate assigns none.

### 4.3 Section header — fixed 16 bytes

Every section after PREAMBLE and before TRAILER starts with the CSM §4 header:

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | Type FourCC |
| 4 | 4 | Flags |
| 8 | 8 | PayloadLength |

Known v1 types: `TCSM`, `BASE`, `PAYL`, `AUX0`, `PIDX`, `FOOT`. Section flag bit 0 means REQUIRED; all other v1 flag bits are reserved and MUST be zero. `TCSM`, `PAYL`, `PIDX` and `FOOT` are required sections and writers MUST set REQUIRED on them. `BASE` and `AUX0` are optional sections: writers MUST clear REQUIRED, and readers MUST reject either one with REQUIRED set, because an optional section cannot demand to be understood. A known section's type, not its REQUIRED flag, decides where it may appear (section 4.1).

Unknown section behavior is the CSM §4 behavior: REQUIRED set -> fail; REQUIRED clear -> skip exactly `PayloadLength` bytes without allocating the payload. Section length plus header MUST fit the remaining physical bytes under checked UInt64 arithmetic.

### 4.4 BASE payload — fixed 32 bytes (candidate)

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 32 | ExpectedBaseManifestId |

Rules:

- at most one `BASE` section; `PayloadLength` MUST equal 32;
- presence means "the applier expects a base manifest whose `ManifestId` equals this value"; absence means no expected base and MUST be represented by the absence of the section, never by a reserved digest value (the all-zero 256-bit value is a valid `ManifestId`, RFC-0001 §5.5).

### 4.5 TCSM payload

The `TCSM` payload is one complete CSM v1 artifact, byte-for-byte, exactly as it would be stored on its own. Rules:

- `PayloadLength` MUST equal the embedded CSM's `PhysicalLength`;
- the bytes MUST be accepted unchanged by the existing CSM reader with its own structural, block-integrity, logical and physical checks (CSM §14);
- the embedded CSM's `HashSuiteId`, `ChunkingProfileId` and `ProfileFingerprint` are the patch's target semantics;
- while CSP v1 is current, an embedded CSM of a format major other than 1 is rejected until a CSP revision defines how other majors are embedded.

Embedding the manifest is what makes a patch self-contained: the target identity travels with the payload, and no out-of-band manifest fetch is needed to verify reconstruction. Whether to embed or to reference an external CSM is decision (c).

### 4.6 PAYL payload blocks (candidate)

A `PAYL` section holds one block of payload entries. Fixed block prefix: 16 bytes.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | EntryCount |
| 4 | 4 | Reserved = 0 |
| 8 | 8 | FirstEntryOrdinal |

Then `EntryCount` entries followed by a trailing `PayloadCrc32C` (UInt32). Each entry:

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 32 | ChunkId |
| 32 | 4 | StoredLength |
| 36 | 1 | Encoding |
| 37 | 3 | Reserved = 0 |
| 40 | StoredLength | Stored bytes |

Expected `PayloadLength`: `16 + sum(40 + StoredLength for each entry) + 4`.

Rules:

- `EntryCount` is 1..4096 (candidate); a `PAYL` section is never empty;
- `FirstEntryOrdinal` MUST equal the total entry count of all preceding `PAYL` sections; entries carry a global ordinal, and `PIDX` entry `i` describes the payload entry with ordinal `i`;
- every `ChunkId` MUST occur in the target manifest, and at most one payload entry may exist per distinct target `ChunkId`; repeated target occurrences reuse one entry;
- `StoredLength` MUST be > 0 and <= the target chunk `Length` for that `ChunkId`, so the total stored payload cannot exceed the target content length;
- `PayloadCrc32C` is CRC-32C over the section header plus all payload bytes preceding the CRC field, exactly as CSM §6 defines it for `CBLK`; `PAYL` grouping is physical and changes no identity, exactly as CSM `CBLK` grouping does not (CSM §8).

A writer that binds an expected base SHOULD emit exactly one entry per distinct target `ChunkId` the expected base cannot supply. That set, summed by `Length`, is the lab's `UniqueMissingPayloadBytes`; the payload section carries the same bytes it counts (section 10).

### 4.7 PIDX payload (candidate)

`PIDX` is the payload index that lets a tail reader locate required payload entries without scanning payload sections. Fixed prefix: 8 bytes.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | IndexVersion = 1 |
| 4 | 4 | EntryCount |

Then `EntryCount` fixed 80-byte entries followed by `PidxCrc32C` (UInt32):

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 32 | ChunkId |
| 32 | 8 | PayloadOffset |
| 40 | 4 | StoredLength |
| 44 | 1 | Encoding |
| 45 | 3 | Reserved = 0 |
| 48 | 32 | DictionaryChunkId |

Expected `PayloadLength`: `8 + EntryCount*80 + 4`.

Rules:

- `IndexVersion` is 1; a different value is rejected until a revision defines it;
- `EntryCount` MAY be 0 when the target is fully supplied by the base;
- `PayloadOffset` is the absolute patch-file offset of the corresponding `PAYL` entry record (its `ChunkId` field). Section headers and payload starts are located by offsets, consistent with CSM §11;
- PIDX entry `i` corresponds to payload ordinal `i` and MUST agree with that `PAYL` entry on `ChunkId`, `StoredLength` and `Encoding`;
- entries MUST be in target first-occurrence order: across entries, the sequence position of the first target record with that `ChunkId` strictly increases. This is the candidate ordering (decision (d)); it lets a forward applier consume target, index and payload in lockstep;
- `DictionaryChunkId` is reserved and MUST be zero while `Encoding` is 0. It is the intended slot for the dictionary base chunk of section 5.3;
- `PidxCrc32C` uses the section coverage rule of section 4.6, and every offset MUST point inside the patch's `PAYL` phase, MUST NOT overlap another entry's stored bytes, and MUST be consistent with that entry's `StoredLength`.

### 4.8 FOOT payload — fixed 56 bytes (candidate)

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 8 | TcsSectionOffset |
| 8 | 8 | BaseSectionOffset, 0 when absent |
| 16 | 8 | PidxSectionOffset |
| 24 | 8 | FirstPaylSectionOffset, 0 when no payload |
| 32 | 8 | PaylCount |
| 40 | 8 | PayloadEntryCount |
| 48 | 8 | Reserved = 0 |

Offsets identify section headers, not payload starts (CSM §11). `PayloadEntryCount` MUST equal both the `PIDX` entry count and the total `PAYL` entry count. `BaseSectionOffset` is zero if and only if no `BASE` section exists. `FirstPaylSectionOffset` is zero if and only if `PaylCount` is zero.

### 4.9 TRAILER — fixed 64 bytes (candidate)

TRAILER is not preceded by a section header.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | Magic = ASCII `CSPT` |
| 4 | 2 | FormatMajor = 1 |
| 6 | 2 | TrailerSize = 64 |
| 8 | 8 | FootSectionOffset |
| 16 | 8 | PhysicalLength |
| 24 | 32 | FileDigest |
| 56 | 8 | Reserved = 0 |

`PhysicalLength` is the complete byte length including TRAILER. `FileDigest` is the digest of every physical byte before the TRAILER, using the HashSuite declared by the embedded `TCSM`; the TRAILER is excluded to avoid self-reference. Because the HashSuite comes from the embedded CSM, the patch digest cannot be checked before that CSM is readable; a reader that does not know the declared HashSuite rejects the patch as unsupported.

A tail reader can fetch the fixed 64-byte TRAILER, locate `FOOT`, then locate `TCSM`, `BASE`, `PIDX` and the payload sections. A forward reader can stream the same structure from the preamble.

## 5. Payload encoding

### 5.1 v1 base layer: raw

`Encoding = 0` means raw: the stored bytes ARE the target chunk bytes, and `StoredLength` MUST equal the target chunk `Length`. Before those bytes are written, the reader MUST compute the chunk hash with the embedded manifest's HashSuite and reject the entry unless it equals the entry's `ChunkId` (which section 7 also requires to be a target `ChunkId`). Raw is the only encoding v1 implements.

### 5.2 Reserved encoding space

Every entry carries an explicit `encoding` field in both `PAYL` and `PIDX`. Value 0 is raw; values 1..255 are reserved. A reader MUST reject an entry whose encoding it does not implement before using that entry: an undecodable entry makes the target unreachable, so this is an unsupported-encoding failure, never a silent skip. Assigning a value requires an owner decision, a format revision and vectors (section 11).

### 5.3 Proposal: dictionary-relative intra-chunk delta (not v1)

CSP is chunk-granular, and on compiled code chunk reuse cannot approach byte-level delta tools. As a public reference point, published update-size figures for Chrome 190.1 -> 190.4 report 10.4 MB as a full update, 705 KB with bsdiff and 79 KB with Courgette; that gap is the class of problem this proposal probes. Those are figures published for another system, not measurements of ChunkShift, and no CSP number is claimed here.

Proposal, contingent on decision (a) and the evidence rule of section 10:

- an entry's `encoding` names a dictionary-relative codec (candidate: `1` = zstd with an explicit dictionary);
- the dictionary is exactly one named base chunk: `DictionaryChunkId` identifies a chunk of the base manifest and the dictionary bytes are that base chunk's exact uncoded bytes;
- the produced identity is the entry's `ChunkId` and the target manifest's `Length`, and the invariant is unchanged and non-negotiable: decoded bytes MUST hash to that `ChunkId` under the embedded manifest's HashSuite and MUST be exactly that `Length` long;
- decoder memory is bounded by the expected target chunk `Length` plus the dictionary chunk size, both bounded by the profile's maximum chunk length; a decoder MUST bound output by the expected `Length` before allocating (RFC-0001 §13.5);
- it remains declarative: the patch carries data plus a codec identifier, with no offsets, copy lengths or interpreter state, so the decoder can address nothing except the one named dictionary; and the dictionary is guaranteed available because it is a base chunk, with an identity that is already a digest of the exact dictionary bytes (the RFC-0004 §3 rule for dictionary-using physical formats);
- it is not part of v1, and adding a compression dependency to Core is out of scope (RFC-0004 §3). Until an owner decision and the section 10 evidence exist, no encoder emits it, no v1 decoder implements it, and a v1 reader MUST reject it; the codec would live in the Patching package. The reserved `DictionaryChunkId` slot exists so that a later assignment is a value-space addition rather than a layout change; if the extension is dropped, the slot remains reserved.

## 6. Apply algorithm (normative)

Inputs: the CSP artifact, an optional base (base manifest plus readable base content), and a destination path. Apply proceeds in target-manifest order:

1. Read the fixed TRAILER, validate magic/major/`TrailerSize`/`PhysicalLength`, locate `FOOT`, and validate the section state machine of section 4.1, including CRC32C for every `PAYL` and `PIDX` section. The patch `FileDigest` (section 9.2) is recomputed over the same bytes, once the embedded CSM has named the HashSuite; a mismatch aborts apply before any output exists.
2. Read the embedded `TCSM` through a bounded substream and run the existing CSM reader over it. Obtain the target `HashSuiteId`, `ChunkingProfileId`, `ProfileFingerprint`, `ManifestId`, `FileDigest`, `TotalChunkCount`, `TotalContentLength` and the ordered `(ChunkId, Length)` records. An invalid embedded CSM aborts apply.
3. Read `PIDX` (bounded by section 8) and validate it against the `PAYL` sections and against the target manifest: every indexed `ChunkId` is a target `ChunkId`, indices agree with payload records, and ordering follows section 4.7.
4. If `BASE` is present: open the base manifest, require its `HashSuiteId` to equal the target's, and require its `ManifestId` to equal `ExpectedBaseManifestId`. If `BASE` is absent, any supplied base may be used, and every reused chunk is still verified by `ChunkId`.
5. Resolve each target record, in order:
   - if a payload entry exists for the record's `ChunkId`, read its stored bytes, decode per `encoding`, verify `Length` and `ChunkId`, and write;
   - otherwise, if the base manifest maps the `ChunkId` to base content, read those bytes, verify `Length` and `ChunkId`, and write;
   - otherwise the patch lacks a needed chunk and MUST be rejected (missing payload).
   The reader MUST verify bytes before writing them. When both sources exist, either may be used; both are bound to the same target `ChunkId`, so the choice cannot change the output.
6. After the last record, require the total bytes written to equal `TotalContentLength`.
7. Optional stronger check: if the target profile is registered, re-chunk the written output with it and compare the result against the embedded manifest (section 9.5).
8. Only after all checks pass, publish the output by atomic rename from a temporary file in the destination directory. On any failure, the temporary file is discarded and the destination path is never replaced.

Wrong base or corrupt patch never publishes a target: a `BASE` mismatch fails before any output exists, and a per-chunk or final mismatch fails before publication. Publication mechanics (temp naming, disk preflight, Windows locks) are Patching behavior under RFC-0004 §1; the format requirement is only that no partial or unverified target becomes visible at the destination path.

Memory bounds, capped by section 8:

- O(1) in target size: the current chunk buffer, hash state and per-chunk I/O buffers. The chunk buffer is bounded by the target profile's maximum chunk length.
- The target manifest is streamed in order; a materialized target record table is capped by the configured record limit (section 8).
- `PIDX` is O(payload entry count) if materialized, and the candidate default cap is 1,048,576 entries (section 8); a lockstep applier may stream it.
- The base locator is O(base chunk count) if materialized, capped by the same configured record limit.
- The reconstructed output lives in a temp file, not in memory.

## 7. MUST-reject rules

Each rule is normative and testable. Category: `malformed` = format error, `unsupported` = well-formed but not implementable by this reader, `verification` = integrity failure that MUST NOT publish.

1. (malformed) preamble magic is not `CSP1`, `FormatMajor` is not 1, `PreambleSize` is not 32, or preamble reserved bytes are non-zero.
2. (unsupported) unknown required physical feature bits are set.
3. (malformed) any non-zero reserved field not listed elsewhere: section flag bits above bit 0, `PAYL`/`PIDX` entry reserved bytes, `FOOT` reserved, TRAILER reserved.
4. (malformed) a known optional section has REQUIRED set, or an unknown section with REQUIRED set is encountered; only unknown optional sections may be skipped.
5. (malformed) the state machine of section 4.1 is violated: duplicate, missing or out-of-order required sections (TCSM/PAYL/PIDX/FOOT/TRAILER), a duplicated or misplaced BASE, a section after FOOT, bytes after TRAILER, a gap or padding between sections, or TRAILER not at physical EOF.
6. (malformed) `PhysicalLength` does not equal the actual artifact length.
7. (malformed) any section `PayloadLength` cannot fit the remaining bytes under checked arithmetic, or a section is truncated.
8. (malformed) `PAYL` computed payload length differs from `PayloadLength`, `EntryCount` is outside 1..4096, `FirstEntryOrdinal` disagrees with preceding entries, or an entry overruns the section.
9. (malformed) `PAYL` or `PIDX` CRC32C mismatch.
10. (malformed) `PIDX` computed payload length differs from `PayloadLength`, or `IndexVersion` is not 1.
11. (malformed) `PIDX` and `PAYL` disagree: entry counts differ, or an entry differs from its payload record in `ChunkId`, `StoredLength` or `Encoding`.
12. (malformed) index out of range, overlapping stored ranges, a `PayloadOffset` outside the `PAYL` phase, or entries not in the order required by section 4.7.
13. (malformed) `BASE` appears more than once, after the first `PAYL`, or with `PayloadLength` other than 32.
14. (malformed) the embedded CSM fails any CSM §14 check, its `PhysicalLength` disagrees with `TCSM.PayloadLength`, or its format major is not 1.
15. (malformed) `FOOT` fields disagree: `BaseSectionOffset`/`FirstPaylSectionOffset`/`PaylCount`/`PayloadEntryCount` do not match the observed sections.
16. (unsupported) the embedded CSM declares an unknown HashSuite, or an entry uses an encoding this reader does not implement.
17. (verification) a payload entry names a `ChunkId` that is not a target-manifest `ChunkId`, or one target `ChunkId` has more than one payload entry.
18. (verification) for raw encoding, `StoredLength` differs from the target chunk `Length`; for any encoding, `StoredLength` exceeds it.
19. (verification) decoded or stored bytes do not hash to the entry's target `ChunkId` under the embedded manifest's HashSuite.
20. (verification) `BASE` is present and the supplied base manifest's `ManifestId` differs from `ExpectedBaseManifestId`, or the base manifest's HashSuite differs from the target's.
21. (verification) a reused base chunk's bytes do not hash to the target `ChunkId`, or its base `Length` differs from the target `Length`.
22. (verification) a target chunk is neither supplied by the base nor present in the payload (missing payload).
23. (verification) the total bytes written differ from `TotalContentLength`.
24. (verification) the optional re-chunk check of section 9.5 was performed and does not reproduce the embedded manifest.
25. (verification) CSP TRAILER `FileDigest` mismatch, or embedded CSM `FileDigest`/`ManifestId` mismatch.
26. (verification) a limit of section 8 is exceeded.

## 8. Resource bounds

A conforming v1 reader/applier enforces at least the following. All values are candidate values.

Fixed sizes:

- `PreambleSize = 32`, `SectionHeaderSize = 16`, `TrailerSize = 64`;
- `BASE` payload = 32 bytes;
- `PAYL` prefix = 16 bytes, entry header = 40 bytes, `EntryCount` 1..4096; `PIDX` prefix = 8 bytes, entry = 80 bytes, `IndexVersion = 1`;
- `FOOT` payload = 56 bytes.

Derived bounds, computed from the embedded CSM before allocating:

- payload entries <= distinct target `ChunkId` count <= `TotalChunkCount`;
- every `StoredLength` <= its target chunk `Length`, therefore total stored payload bytes <= `TotalContentLength`;
- `PIDX` payload length is exactly `8 + EntryCount*80 + 4` and `EntryCount` equals `FOOT.PayloadEntryCount`;
- every `PayloadOffset` and every section range lies inside the artifact, under checked UInt64 arithmetic;
- the embedded CSM is bounded by the CSM reader's own limits and by the remaining patch bytes.

Operational defaults, configurable and not persisted compatibility limits:

- `MaximumPayloadEntries = 1,048,576`. A fully materialized candidate `PIDX` at this cap is about 80 MiB; an applier that streams the index may process more, and a stricter configured cap is allowed.
- `MaximumMaterializedManifestRecords = 4,194,304` for a materialized target record table or base locator. A stricter configured cap is allowed; exceeding it is a limits failure, not malformed input.

Configurable operational limits may be stricter than the format maxima, in the CSM §13 sense. Limits are not persisted compatibility limits unless a later revision says so. Every offset/count/length multiplication or addition is checked; a reader MUST NOT allocate from an untrusted length before validating them.

## 9. Verification levels

### 9.1 Structural

Validate preamble magic/version/size/features/reserved, the section state machine, section lengths and contiguity, `PAYL`/`PIDX` CRC32C, every fixed-layout reserved field, and checked arithmetic. The embedded `TCSM` must pass CSM structural, block-CRC and logical checks; that includes the embedded CSM's own `ManifestId` and physical `FileDigest`.

### 9.2 Patch physical integrity

Recompute `FileDigest` over all bytes before the TRAILER with the embedded CSM's HashSuite and compare it with the TRAILER, and require `PhysicalLength` to match the consumed representation, exactly as CSM §14 maps its physical level.

### 9.3 Payload integrity

Require `PIDX`/`PAYL` agreement, then verify each payload entry used by apply: `StoredLength`, decode, target `Length`, and `Hash(bytes) == ChunkId` under the embedded manifest's HashSuite. This is what ties a payload entry to the same identity model as CSM chunk records.

### 9.4 Base binding

Require the base HashSuite to equal the target HashSuite, require the base `ManifestId` to equal `ExpectedBaseManifestId` when `BASE` is present, and verify every reused base chunk by target `ChunkId` and `Length` before it is written. Without `BASE`, this level is the only base guarantee: per-chunk verification plus the final target checks.

### 9.5 Full reconstruction

Require the total bytes written to equal `TotalContentLength`. Because every output byte belongs to exactly one chunk that was verified against the target manifest's `ChunkId`, the assembled output is bound by the verified target `ManifestId`. When a reader registers the embedded `ChunkingProfileId`/`ProfileFingerprint`, it SHOULD additionally re-chunk the output with that profile and require the ordered `(ChunkId, Length)` result and the recomputed `ManifestId` to match the embedded CSM, mirroring the CSM content level; an unregistered profile does not block the rest of apply but also cannot provide this check.

### 9.6 Profile semantics

Unchanged from CSM §14: the format does not require a reader to know any profile. A reader that registers the declared `ChunkingProfileId` compares its fingerprint with the embedded CSM's recorded one and reports a mismatch as a profile-semantics failure; an identifier it does not register is not a manifest-only failure.

Hash equality proves integrity against a trusted expected value; it does not provide authenticity (RFC-0001 §13.1). The .NET API shape for these outcomes is decided with the implementation ([#66](https://github.com/definitely-stable/ChunkShift/issues/66)/[#7](https://github.com/definitely-stable/ChunkShift/issues/7)); the format contract is the three outcome categories of section 7, and no verification failure may publish a target.

## 10. Measurement and evidence plan

The lab must record the following before this format freezes. No number exists until a real encoder exists; `CspBytes` stays null until then and no framing/index overhead is fabricated (M0-LAB.md "Missing payload and CSP bytes").

Per pre-registered file pair, with the profile fixed:

1. Actual CSP bytes versus `UniqueMissingPayloadBytes`, plus both the absolute difference (container overhead: preamble, section headers, embedded CSM, `PAYL` entry headers, `PIDX`, `FOOT`, TRAILER) and the ratio. Record the embedded-CSM share separately, because decision (c) trades self-containment against patch bytes.
2. Whole-file delivery versus CSP on the identical pair, as a ratio to target bytes.
3. xdelta3 on the identical pair (PLAN.md §12; ROADMAP Patching gate).
4. For compiled-code pairs, additionally bsdiff and zstd `--patch-from`-style baselines, to test the section 5.3 hypothesis. Published figures for other systems are context only.
5. Apply evidence: wrong base and corrupt patch never publish; memory/temp/index behavior is explicit; apply time, CPU and peak memory recorded under the normal lab rules; Patching is ported only when its own compatibility fixtures are ready (PLAN.md §12).

Decision rule for the delta extension, fixed before measuring:

- adopt before the freeze if, on the pre-registered compiled-code pairs, CSP with delta reduces actual patch bytes by at least 25% versus raw CSP at the same targets, stays within 1.5x apply time and CPU of raw apply, and needs no more than one target chunk plus one dictionary chunk of working memory;
- keep it reserved and revisit with more evidence if the reduction is 10-25%;
- drop it and keep raw-only v1 if the reduction is below 10%.

These thresholds are candidate values. What matters is that they are fixed here, before the numbers exist.

## 11. Golden vectors and compatibility

Before this format freezes, committed vectors plus an independent generator/decoder are required, in the shape of `tools/csm-fixtures` (`generate.py`/`decode.py`), so the format is checkable by more than one implementation. Required cases include at least:

- no payload (target fully supplied by the base), one chunk, and a target with repeated missing chunks (one entry, several uses);
- `BASE` present and absent with identical payload;
- multiple `PAYL` sections and a 4096-entry boundary block;
- `PIDX` entry order and offset validation, and a zero-entry index;
- malformed/truncated section headers at every structural boundary;
- bad `PAYL`/`PIDX` CRC32C;
- `PIDX`/`PAYL` disagreement, duplicate entry, entry whose `ChunkId` is not in the target;
- raw length mismatch, bad encoding value, bad embedded CSM, base `ManifestId` mismatch, HashSuite mismatch, missing payload, bad CSP `FileDigest`, bad final length;
- a wrong-base and a corrupt-patch case that must not publish;
- CRC-32C known vector `123456789 -> E3069283` reuse from CSM.

Compatibility rules:

- CSP, CSM, package and profile versions are independent (RFC-0001 §15); adding or changing an encoding, layout or identity requires an owner decision, a format revision and updated vectors, and golden vectors are never edited to make an implementation pass (AGENTS.md);
- unknown major versions and unknown required features are rejected, unknown optional physical sections may be skipped, and reserved fields stay zero. CSP v1 embeds CSM v1 only; a future CSM major inside CSP requires a CSP revision that defines it;
- nothing is published before the vectors and the independent decoder exist: the version tag belongs to the publication repository and follows the fixture gate (PLAN.md §12; ROADMAP Patching gate).

## 12. Open owner decisions

Each item lists the options and the draft's recommendation. None of them is decided by this document.

1. (a) Intra-chunk delta — options: include in v1 / reserve and decide by evidence / never. Recommendation: reserve the encoding value and the `DictionaryChunkId` slot now, ship raw-only v1, and decide with section 10 evidence; the extension is the only candidate answer to compiled-code pairs where chunk reuse leaves a large gap.
2. (b) Base `ManifestId` — options: required / optional. Recommendation: optional, as RFC-0001 §9 states; optional keeps self-contained patches applicable without a base claim, with the weaker-but-still-verified consequence of section 3.3.
3. (c) Target manifest carrier — options: embed the full target CSM / reference an external one. Recommendation: embed byte-for-byte; the patch is self-contained, the existing CSM reader verifies the target identity, and CSP needs no second identity carrier. The cost is the CSM's bytes inside every patch, which section 10 records.
4. (d) Payload ordering and index shape — options: target first-occurrence order with a positional index / strictly `ChunkId`-sorted entries with binary search. Recommendation: target first-occurrence order with the fixed 80-byte index of section 4.7; it supports lockstep streaming, and random access via `PayloadOffset` remains available. A sorted variant needs a documented sort rule and loses the lockstep property.
5. (e) Payload entries for chunks the expected base already supplies — options: forbidden / permitted. Recommendation: permitted; required when `BASE` is absent and the encoder cannot know the applier's base, useful for resumability, and it cannot change the output because both sources verify against the same `ChunkId`. Writers SHOULD omit such entries when the expected base supplies the chunk.
6. (f) `PatchId` — options: define a persisted field / identify the patch by `(base ManifestId, target ManifestId)`. Recommendation: no persisted field (section 3.5); if the update-policy layer needs a single name, define a domain-separated digest over the pair and the physical digests rather than storing an unused member.
7. (g) Base/target `ProfileFingerprint` mismatch — options: reject at apply / treat as creation-time guidance. Recommendation: creation-time guidance, not an apply rejection; reuse is decided by `ChunkId` equality, and rejecting a valid chunk-level patch on profile grounds would be a product rule with no integrity benefit. Revisitable if measurement shows a reason.
8. (h) Apply input model — options: require seekable patch and base content / promise forward-only apply. Recommendation: require seekable inputs in v1, because `FOOT`/TRAILER and payload offsets are designed for them, and treat forward-only apply as an optimization for the candidate order of decision (d).
