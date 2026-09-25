# RFC-0004 — Product concerns deliberately outside Core 0.1.0

Status: Accepted  
Date: 2026-09-25  
Issue: [#65](https://github.com/definitely-stable/ChunkShift/issues/65)  
Refines: [RFC-0001](RFC-0001-target-architecture-2026.md) §5.1, §12.2, §13.2, §17; [RFC-0003](RFC-0003-core-first-release.md) §3

## 1. Decision

Core 0.1.0 adds **no** progress, compression, signature or anti-rollback API, field or persisted slot. This RFC records, for each concern, why its absence is not a breaking change later and which layer owns it.

#65 said that these four decisions "become breaking changes after 0.1.0, whether they are adopted or not". That holds only if Core has to reserve something for them now. Checked against the current surface (`ManifestCreationOptions` = `ProfileId`, `HashSuite`, `IncludeBlockIndex`) and CSM v1, none of them needs a reservation:

| Concern | Owner | Can be added later without breaking Core 0.1.0 because |
|---|---|---|
| Progress | a later Core minor version, if a consumer needs it | an init-only property on the existing options class, or a new overload, is additive |
| Compression | CSP payload encoding, Repository pack encoding, transport | `ChunkId` and `ManifestId` are defined over exact uncompressed bytes (RFC-0001 §5.1); a codec changes only physical encodings that Core 0.1.0 does not define |
| Authenticity | a detached trust envelope outside CSM | the envelope signs existing identities (`ManifestId`, `FileDigest`, `PhysicalLength`); nothing inside CSM changes |
| Anti-rollback | signed update-policy metadata above CSM/CSP | version/sequence/epoch/expiry select *which* manifest is acceptable and are not part of the manifest |

The related non-blocking items in #65 (crash-safe apply, disk-space preflight, Windows file locks) belong to Patching ([#7](https://github.com/definitely-stable/ChunkShift/issues/7)). Core 0.1.0 has no apply operation.

## 2. Progress

**Decision:** Core 0.1.0 has no progress API. No `IProgress<T>` parameter, and no empty options property reserved for one.

Rationale:

- `ChunkScanner` already reports progress. The handler runs once per chunk with `ChunkInfo.Offset` and `Length`, sequentially, with backpressure (RFC-0002 §5). A consumer that needs byte progress for a scan gets it from the handler.
- For `ChunkManifest.CreateAsync`, `VerifyAsync` and `VerifyManifestAsync` no consumer has asked for progress. The console and ASP.NET samples and the CLI do not need it. A caller can already observe progress by wrapping the input `Stream` and counting the bytes read.
- Minimal API (RFC-0003 §7, AGENTS.md): no public surface for hypothetical future use.

How it can be added later without a break (the later RFC or issue picks one):

- an init-only `Progress` property on `ManifestCreationOptions`, and an options type for verification if one is needed;
- or a new overload.

Both are additive under docs/RELEASES.md. Whatever form is chosen, it MUST NOT add a mandatory per-chunk allocation or delegate call to the hot path. Reports are throttled or per block, never per byte (RFC-0001 §10, RFC-0002 §6).

## 3. Compression

**Decision:** compression is not a Core or CSM 0.1.0 semantic concern. No codec, level or dictionary is selected now.

Invariants that remain normative (RFC-0001 §5.1, §12.2; CDC-PREFREEZE-DECISION §8):

- `ChunkId = HashSuite.Hash(exact uncompressed chunk bytes)`. No codec, level, dictionary or framing changes a `ChunkId`, a `ManifestId` or a chunking profile.
- Compression lives only in a physical encoding: CSP payload records ([#66](https://github.com/definitely-stable/ChunkShift/issues/66)/[#7](https://github.com/definitely-stable/ChunkShift/issues/7)), Repository pack chunk frames ([#10](https://github.com/definitely-stable/ChunkShift/issues/10)), or a transport representation ([#13](https://github.com/definitely-stable/ChunkShift/issues/13)).
- Frames are independently decodable per chunk. Whole-pack streaming compression is not used (RFC-0001 §12.2).
- A physical format that supports a dictionary MUST record the codec and the dictionary identity, as a digest of the exact dictionary bytes, in its own persisted metadata, so a reader can reject a frame whose dictionary it lacks. The dictionary identity belongs to that physical format's identity (for example a pack or physical CSP digest). It never enters `ChunkId`.
- Every decoder bounds decompressed output by the expected chunk `Length` before allocating (RFC-0001 §13.5).

Not decided here: zstd vs Brotli vs LZ4, levels, dictionary training and selection, when to store a chunk raw. #66/#7 and #10 decide these with measured evidence.

CSM v1 stays uncompressed. Compressing a CSM file for transport is a host or transport choice and does not change `ManifestId` (RFC-0001 §8.8).

## 4. Authenticity

**Decision:** CSM is an integrity artifact. Authenticity is a **detached trust envelope** outside CSM. RFC-0001 §13.2 already required `ManifestId`/`PatchId` to be signable by a detached layer; this RFC makes that the final Core 0.1.0 position.

Rules:

- A hash is not a signature. `ManifestId` and `FileDigest` give integrity only when compared against a trusted expected value (RFC-0001 §13.1).
- No signature goes into CSM, including `AUX0`. CSM-V1-CANDIDATE §9 already forbids semantic data in `AUX`, and a signature inside the file has a circularity problem: `FileDigest` covers every physical byte before the TRAILER, including any `AUX` section, so a signature over `FileDigest` cannot sit inside the bytes that `FileDigest` covers.
- No envelope slot is reserved in CSM v1. A detached envelope needs none.

A future envelope (not designed here) is expected to bind at least:

```text
artifact type          (CSM, CSP, …)
logical identity       (ManifestId / PatchId)
physical identity      (FileDigest + PhysicalLength of the exact artifact bytes)
HashSuite              (so the digests above are unambiguous)
channel / version / epoch / expiry, where the update-policy layer needs them (§5)
signature algorithm
key identity
signature
```

`ManifestInfo` already exposes `ManifestId`, `FileDigest`, `PhysicalLength` and `HashSuite`, so a producer can build such an envelope from Core 0.1.0 output without any Core change.

Not decided here: the envelope format, the signature algorithm (Ed25519 is a likely first candidate but is **not** fixed as the only algorithm; the envelope carries an algorithm identifier), key distribution, minisign/Sigstore/TUF compatibility, and whether first-party signing ships in ChunkShift at all (RFC-0001 §17). A separate RFC with a threat model owns these.

## 5. Anti-rollback and freeze attacks

**Decision:** anti-rollback does not belong to CSM or CSP. It belongs to a signed **update-policy layer** that says which manifest or patch a client may currently accept.

Rules:

- No monotonic version, sequence, epoch or expiry enters CSM, `ManifestId` or `ChunkId`. `ManifestId` identifies content and profile. Two releases with identical content have the same `ManifestId`, and that is correct.
- Rollback protection (never accept an older release) and freeze protection (never trust stale metadata forever) need a monotonic sequence or version, an epoch, an expiry and the target identity, all under the trust envelope of §4. The client keeps the highest accepted sequence per channel.
- These fields are signed on the update-policy layer: release/channel metadata that references `ManifestId`/`PatchId` and the physical digests.

Not decided here: the metadata format, TUF-style role separation, and client state storage. They belong to the distribution/update work after Patching ([#7](https://github.com/definitely-stable/ChunkShift/issues/7), [#13](https://github.com/definitely-stable/ChunkShift/issues/13)) under a separate design.

## 6. Consequences

- The Core 0.1.0 public API and CSM v1 do not change for #65. [#6](https://github.com/definitely-stable/ChunkShift/issues/6) can freeze the current candidate without a reserved progress, compression, signature or rollback member.
- The following would break these decisions and need a new RFC: putting a signature or version inside CSM, adding a codec to `ChunkId` or `ManifestId`, and adding a progress callback that allocates per chunk.
- RFC-0001 §17 still lists first-party signing and encryption/privacy profiles as deferred. This RFC fixes only where they will live, not what they are.
