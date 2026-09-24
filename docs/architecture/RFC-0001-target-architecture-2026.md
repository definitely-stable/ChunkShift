# RFC-0001: ChunkShift Target Architecture 2026

Status: Accepted  

> **RFC-0003 supersession note:** [RFC-0003](RFC-0003-core-first-release.md) owns first-public-release sequencing. Where this RFC couples the first public release to Patching/CSP/compare-diff, RFC-0003 takes precedence. The architecture and persisted-model decisions here remain authoritative.
Target: ChunkShift 1.0 core and patching; repository architecture preview  
Last updated: 2026-09-22

## 1. Purpose

This RFC is the architecture synthesis for ChunkShift after the September 2026 reviews of:

- content-defined chunking;
- hashing;
- binary manifest design;
- future CAS/repository architecture;
- public API ergonomics;
- product/developer experience;
- crash consistency, GC and remote object-store behaviour.

It supersedes the architectural assumptions in the previous root `PLAN.md`. The implementation plan lives in `/PLAN.md`; this RFC defines the target architecture and the decisions the plan must preserve.

The selection criteria are, in order of importance:

1. correctness and format longevity;
2. simplicity of the public model;
3. end-to-end performance rather than isolated microbenchmark throughput;
4. crash and corruption resilience;
5. scalability from a single large file to a petabyte-class repository;
6. developer experience for .NET consumers;
7. implementation cost and the ability to defer complexity safely.

## 2. Product boundary

ChunkShift is a .NET engine for content-aware binary updates and reusable content movement.

The primary product loop is:

```text
old content + target content
        |
        v
 stable chunk identities
        |
        v
 reuse / missing-content analysis
        |
        v
 patch or fetch only missing content
        |
        v
 reconstruct exact target
        |
        v
 verify
```

The first product scenarios are:

- standalone embedded/local chunking and content inspection inside .NET applications;
- custom game launchers and game-content patching;
- desktop/application update distribution;
- large binary/artifact update pipelines;
- ASP.NET Core-hosted distribution and ingestion using standard HTTP primitives.

The future repository is a reusable storage substrate for the same content model. It is not the product definition.

Explicit non-goals for the 1.0 product:

- backup application;
- general-purpose sync client;
- AI dataset/model hosting platform;
- RAG/semantic chunking;
- arbitrary distributed object database;
- proprietary resumable-upload protocol;
- cross-tenant global deduplication.

Those workloads may be used for benchmarks or adapters without becoming core product commitments.

## 3. Decision summary

| Topic | Decision | Status |
| --- | --- | --- |
| Product focus | content-aware binary update/patch engine | ACCEPT |
| Repository | second-layer engine, designed now, stable API later | ACCEPT / DEFER |
| FastCDC | production baseline candidate | ACCEPT |
| FastCDC size/profile | choose by benchmark, not by assumption | EXPERIMENT |
| SeqCDC | high-priority challenger | EXPERIMENT |
| VectorCDC | SIMD research/acceleration | EXPERIMENT |
| Chonkers | patch-locality research | EXPERIMENT |
| UltraCDC | low-entropy research | EXPERIMENT |
| BLAKE3-256 | default persistent hash suite | ACCEPT |
| SHA-256 | compatibility/compliance hash suite | ACCEPT |
| XXH3 as persistent content identity | do not use | REJECT |
| transient fast fingerprint | only if measured useful | EXPERIMENT |
| persistent ID width | 256 bit | ACCEPT |
| all-zero Hash256 invalid | remove this restriction | REJECT |
| profile identity | semantic fingerprint, not exact JSON bytes | ACCEPT |
| primary manifest format | bespoke binary CSM | ACCEPT |
| JSON manifest | diagnostic/export only | REJECT as primary |
| hot chunk length encoding | fixed UInt32 little-endian | ACCEPT |
| Index/Offset/BoundaryKind in logical entry | remove | REJECT |
| materialized manifest as core model | remove | REJECT |
| sparse manifest index | optional BIDX | ACCEPT |
| self-indexed immutable packs | repository physical primitive | ACCEPT |
| one remote object per small chunk | do not use | REJECT |
| fixed universal pack size | backend policy, not format | REJECT |
| generic LSM + WAL as repository truth | do not use | REJECT |
| LSM-like immutable index compaction | use | ACCEPT |
| Protobuf MIDX | do not use | REJECT |
| SQLite/RocksDB as repository truth | do not use | REJECT |
| public IChunker/IHasher/IRepository zoo | do not freeze | REJECT |
| Stream as core public I/O boundary | use | ACCEPT |
| public PipeReader ownership knobs | do not expose | REJECT |
| NativeAOT compatibility | core requirement | ACCEPT |
| standalone embedded/local SDK | core package must be useful without Patching/Repository/DI | ACCEPT |
| low-level raw chunk stream | public capability required; callback/borrowed-memory shape pending #20 evidence | ACCEPT / EXPERIMENT |
| special ChunkShift server for static artifacts | not required | REJECT |
| stable ChunkShift.AspNetCore API before integration evidence | defer; validate through RFC-0002/M4A | DEFER |
| signing/encryption implementation | separate later design | DEFER |
| hostile-input parser hardening | required | ACCEPT |

## 4. Conflicts resolved from the previous plan

### 4.1 Product boundary

Previous direction: deterministic manifest/diff/verify SDK with backup, sync, upload, RAG and storage as possible future products.

Target direction: the first useful product must close the update loop. Manifest and diff are primitives, not the product.

Therefore reconstruction and patching move before repository work.

### 4.2 SHA-256-only vs BLAKE3

Previous direction: SHA-256 is the only v0.1 content hash, BLAKE3 is a future extension package.

Target direction:

```text
chunkshift.blake3-256.v1  -> default
chunkshift.sha256.v1      -> compatibility/compliance
```

The repository or manifest selects one 256-bit HashSuite for content/manifest identities. Hash selection is independent of chunking profile. ProfileFingerprint v1 uses a fixed SHA-256 identity function and therefore does not vary with the selected content HashSuite.

### 4.3 Profile identity

Previous direction:

```text
SpecSha256 = SHA-256(exact normalized profile artifact bytes)
```

Target direction:

```text
ProfileFingerprint =
  SHA-256(domain || canonical semantic profile parameters)
```

Whitespace, JSON property order and documentation edits must not change an algorithm identity.

`ChunkingProfileId` is the short, stable semantic label; `ProfileFingerprint` is the authoritative digest of the semantics. A ProfileId never embeds the fingerprint, and a reader that knows a ProfileId checks the recorded fingerprint against it ([PROFILE-FINGERPRINT-V1](PROFILE-FINGERPRINT-V1.md), #64).

### 4.4 Manifest model

Previous direction: `ManifestIdentity` materializes `ImmutableArray<ChunkDescriptor>` where each descriptor includes Index, Offset, Length, Hash and BoundaryKind.

Target logical manifest:

```text
HashSuite
ChunkingProfileId
ProfileFingerprint
ordered [
  ChunkId,
  Length
]
```

Index is the sequence position. Offset is a prefix sum. BoundaryKind is diagnostic information. None belong in the stable chunk identity record.

### 4.5 JSON-first vs binary-first

Previous direction: JSON is the v0.1 storage/interchange manifestation and binary manifest is later.

Target direction: CSM is the primary persisted and streamed representation from the first serious compatibility release. JSON is inspection/export only.

### 4.6 Interfaces

Previous direction includes `IChunkBoundaryFinder`, `IChunker`, `IChunkHasher`, public registries and a high-level engine facade.

Target direction: hot-path implementations are internal concrete kernels. The public API exposes small immutable values, one-shot operations and concrete buffered readers. Extensibility is not expressed by virtual dispatch in per-byte/per-chunk loops.

### 4.7 Repository index

Previous direction planned SQLite as the local authoritative index.

Target direction:

- packs, roots, manifests and refs are repository truth;
- exact location indexes are immutable, checksummed and rebuildable;
- SQLite may exist only as a disposable local accelerator or operational metadata store.

### 4.8 LSM architecture

ChunkShift does not require a generic mutable LSM database or mandatory WAL.

It uses an LSM-like compaction topology over immutable index segments:

```text
commit
  -> immutable L0 delta run
  -> metadata-only merge
  -> compacted/range-partitioned L1+
```

Normal index compaction never rewrites payload packs.

## 5. Identity model

Persistent identity types are semantically distinct even when they share a 256-bit representation:

- `ChunkId`;
- optional `ContentId`;
- `ManifestId`;
- `ProfileFingerprint`;
- `PackId` / physical pack digest;
- future `RootId` / `CatalogGenerationId`.

### 5.1 ChunkId

```text
ChunkId = HashSuite.Hash(exact uncompressed chunk bytes)
```

Compression, encryption and pack placement do not change the ChunkId.

### 5.2 ContentId

```text
ContentId = HashSuite.Hash(exact original content byte stream)
```

ContentId is optional and independent of chunking profile.

### 5.3 ProfileFingerprint

It hashes the semantic profile definition, including every parameter that can change boundaries:

- algorithm and algorithm version;
- min/target/max semantics;
- gear/table digest where applicable;
- masks/thresholds;
- rolling arithmetic;
- normalization rules;
- EOF behaviour;
- integer overflow semantics.

The source JSON/YAML/text used to author that definition is not itself the identity.

The M0 canonical semantic encoding and domain-separation candidate are specified in [PROFILE-FINGERPRINT-V1.md](PROFILE-FINGERPRINT-V1.md). That candidate is exercised by normative tests now, while M3 remains the public compatibility freeze gate.

### 5.4 ManifestId

ManifestId binds:

- HashSuite;
- chunking profile ID;
- ProfileFingerprint;
- ordered `(ChunkId, Length)` sequence;
- final chunk count and content length.

Optional diagnostic metadata and optional ContentId are not part of ManifestId.

### 5.5 Hash256

`Hash256` represents all 2^256 values. The all-zero value is valid.

Absence must be represented by option/null/owner state rather than stealing one digest value.

## 6. HashSuite

### 6.1 Stable candidate

```text
chunkshift.blake3-256.v1
```

Used by default for:

- chunk identity;
- manifest identity;
- profile fingerprint;
- repository logical IDs.

### 6.2 Compatibility suite

```text
chunkshift.sha256.v1
```

Provided for interoperability and compliance-oriented deployments.

### 6.3 No dual content hash in normal operation

Do not compute XXH3 and BLAKE3 over every chunk payload.

A transient 64/128-bit hash may later be derived from the already-computed 256-bit ID for an in-memory table or static filter if benchmarks justify it.

## 7. Chunking architecture

Chunking semantics and implementation backend are independent.

```text
stable profile semantics
        |
        +-- scalar reference
        +-- x64 SIMD implementation
        +-- ARM64 SIMD implementation
```

Every optimized backend must produce the same boundaries as the scalar reference.

### 7.1 Production baseline

FastCDC remains the production baseline candidate because of maturity, simplicity, deterministic specification and implementation cost.

Do not freeze an average chunk size yet. The measurement gate must compare at least 64 KiB, 128 KiB and 256 KiB actual means on the product corpus.

### 7.2 Research algorithms

SeqCDC, VectorCDC, Chonkers and UltraCDC remain experimental until they pass:

- equal-mean calibration;
- mutation/re-synchronization tests;
- x64/ARM64 deterministic vectors;
- end-to-end patch/repository benchmarks.

Rabin/Buzhash may be kept as reference baselines, not public stable profiles.

## 8. CSM v1 binary manifest

The implementable M1 candidate layout is specified in [CSM-V1-CANDIDATE.md](CSM-V1-CANDIDATE.md). This RFC remains the architectural authority; #9 is the public format freeze gate.

CSM is a bespoke streaming binary container.

High-level structure:

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

### 8.1 Encoding rules

- little-endian;
- 64-bit file offsets and global counts;
- 256-bit fixed IDs;
- UInt32 chunk lengths;
- checked arithmetic;
- reserved bytes must be zero;
- unknown required features fail;
- unknown optional physical sections may be skipped.

### 8.2 CORE

Contains stable semantic metadata:

- HashSuiteId;
- ChunkingProfileId;
- ProfileFingerprint;
- required/optional semantic feature bits;
- small canonical extension TLVs.

TLVs are for cold semantic metadata, not per-chunk fields.

### 8.3 Chunk blocks

Default candidate block capacity: 4096 chunks.

Payload is structure-of-arrays:

```text
ChunkId[0..N)
Length[0..N)  // UInt32 LE
```

A full 4096-entry payload is 144 KiB before the small block header.

This design favors predictable parsing, mmap, batching and NativeAOT implementation over saving approximately one byte per chunk with a varint.

### 8.4 Corruption checks

- block CRC32C for fast accidental-corruption detection;
- ManifestId for semantic integrity;
- physical FileDigest in the final trailer for whole-container physical integrity.

A per-block cryptographic digest is not mandatory in v1 unless measurement or recovery design proves a need.

### 8.5 CEND and finalization

Counts and total length are written at the end because they may be unknown when writing begins.

No header backpatching is required.

### 8.6 BIDX

Optional sparse index containing one first-content-offset per chunk block.

It enables content-byte-position lookup without storing a 64-bit offset per chunk.

### 8.7 FOOT/TRAILER

A fixed-size trailer at EOF locates the footer and final sections, enabling one tail range read for remote readers.

### 8.8 Canonical identity vs physical representation

ManifestId does not depend on:

- chunk-block grouping;
- CSM physical offsets;
- BIDX presence;
- diagnostic metadata;
- timestamps;
- physical section ordering where allowed by the format.

This allows later physical representation improvements without changing logical identity.

## 9. CSP v1 patch format

Patch format is declarative.

A self-contained patch contains:

- target CSM;
- optional expected base ManifestId;
- payload for target chunks not supplied by the base;
- physical index/footer needed for streaming apply.

The patch is not a giant imperative COPY/INSERT VM.

Target ordering already exists in the target manifest. Apply resolves each target chunk from the base or patch payload and writes the target stream in order.

The patch path must support bounded memory and exact post-write verification.

## 10. Public API contracts

The stable v1 surface is intentionally small.

Core principles:

- caller owns all streams passed to one-shot operations;
- ChunkShift never closes caller-owned streams;
- cancellation may leave input/output streams advanced;
- verification mismatches are result values, not exceptions;
- malformed formats, unsupported required features, I/O failures and cancellation use normal .NET exception categories;
- no object allocation per chunk in hot paths;
- async is for actual I/O; CPU kernels are synchronous;
- no dependency on Microsoft.Extensions.DependencyInjection in the core package;
- NativeAOT and trim analysis are release gates.

Candidate shape:

```csharp
public static class ChunkManifest
{
    public static Task<ManifestInfo> CreateAsync(
        Stream content,
        Stream destination,
        ManifestCreationOptions? options = null,
        CancellationToken cancellationToken = default);

    public static Task<VerificationResult> VerifyAsync(
        Stream content,
        Stream manifest,
        CancellationToken cancellationToken = default);

    public static Task<ManifestDiffSummary> CompareAsync(
        Stream oldManifest,
        Stream newManifest,
        CancellationToken cancellationToken = default);
}
```

Large-manifest traversal uses a concrete buffered reader:

```csharp
public sealed class ManifestReader : IDisposable, IAsyncDisposable
{
    public int Read(Span<ChunkInfo> destination);

    public ValueTask<int> ReadAsync(
        Memory<ChunkInfo> destination,
        CancellationToken cancellationToken = default);
}
```

Manifest entries use the same `ChunkInfo` (Index/Offset as `Int64`, Length as `Int32`) that the raw scanner emits; a separate `ChunkEntry` with unsigned fields was removed before the API freeze (#63).

Patching uses similarly small one-shot operations.

The core package also exposes one intentionally low-level embedded capability for processing raw chunks without creating a manifest or repository. The target semantics are defined by [RFC-0002](RFC-0002-embedded-sdk-aspnet-core.md). A sequential callback with borrowed `ReadOnlyMemory<byte>` is the leading candidate, but #20 must compare it against pull/segmented alternatives before M3 freeze. This gives local applications and ASP.NET hosts direct access to chunk bytes while retaining bounded buffering and explicit lifetime semantics.

The core package MUST remain useful without Dependency Injection, ASP.NET Core, Patching or Repository.

Do not expose public stable `IChunker`, `IChunkHasher`, `IChunkBoundaryFinder`, `IRepository` or PipeReader ownership/buffer tuning contracts in v1.

## 11. Internal architecture

Hot-path selection happens once before loops.

```text
ManifestCreateOperation
  -> ChunkingKernel
       -> FastCdcV1Kernel
       -> FixedV1Kernel
       -> experimental kernels
  -> HashSuiteKernel
       -> Blake3V1
       -> Sha256V1
  -> CsmWriter
```

No interface/delegate call is required per byte.

Internal implementation may use:

- Span/Memory;
- ArrayPool/MemoryPool;
- System.IO.Pipelines when useful;
- RandomAccess for files;
- MemoryMappedFile for local immutable metadata where measured useful.

Those are implementation details, not public API promises.

Embedded/local and ASP.NET integration constraints are further specified by RFC-0002. In particular, System.IO.Pipelines may be used internally by an ASP.NET adapter without changing the Stream-based Core contract.

## 12. Repository architecture

Repository design is defined now so stable core choices do not block it, but the public Repository API is deferred until filesystem and object-store implementations validate the abstraction.

### 12.1 Truth vs derived state

Repository truth:

- immutable manifests;
- immutable packs;
- immutable roots/snapshots;
- small mutable refs.

Rebuildable derived state:

- exact global chunk-location index;
- filters;
- metadata caches;
- optional local SQLite caches.

### 12.2 Pack format

```text
PackHeader
ChunkFrame*
PackIndex
PackMetadata
PackFooter
FixedTrailer
```

Chunk frames are independently decodable/compressed. Whole-pack streaming compression is not used.

PackIndex is hash-sorted and sufficient to rebuild global location indexes.

Payload order remains ingest order to preserve locality for sequential reconstruction.

### 12.3 Pack size

Pack size is policy, not format.

Benchmarks must cover local and remote ranges rather than inheriting another project's default.

### 12.4 Global index

The exact global index consists of immutable sorted index segments.

Each segment uses a compact PackTable so a full PackId is not repeated in every entry.

Logical topology:

```text
L0 immutable deltas
  -> metadata-only compaction
  -> range-partitioned L1+
```

This is LSM-like compaction, not a generic mutable LSM database.

### 12.5 Filters

Static approximate membership filters are allowed only where they reduce reads of overlapping immutable runs.

A repository-wide giant filter is not part of the design.

### 12.6 Commit publication

Publication order:

1. build and seal packs;
2. persist packs;
3. persist immutable index deltas;
4. persist manifests/root;
5. persist transaction metadata if needed;
6. publish immutable generation/root;
7. compare-and-swap the named ref last.

A published ref must never point to non-durable objects.

### 12.7 Concurrent writers

There is no distributed per-ChunkId lock.

Two writers may temporarily write duplicate copies of the same chunk. Index compaction can choose preferred locations; GC can later reclaim duplicates.

This trades bounded temporary space for scalable concurrent writes.

### 12.8 Read views

Readers capture an immutable ReadView:

```text
Root + CatalogGeneration + bounded delta set
```

Compaction and GC must not invalidate the reader's view.

### 12.9 GC

GC uses reachability, not refcount decrements as the sole truth.

At large scale marking is external-memory and hash-partitioned.

Pack lifecycle:

```text
ACTIVE
 -> RETIRED(generation)
 -> DELETE_ELIGIBLE(after safety/grace)
 -> DELETED
```

Replacement packs and indexes must be durable and published before old packs become deletable.

## 13. Security model

Threat contexts:

- TrustedLocal;
- PublicDistribution;
- UntrustedRepository;
- MultiTenant.

### 13.1 Integrity

Cryptographic IDs provide content integrity when compared against a trusted expected ID.

CRC32C is only an accidental-corruption detector.

### 13.2 Authenticity

A hash is not a signature.

ManifestId/PatchId must be signable by an external detached-signature layer, but first-party signing is deferred until separately designed and reviewed.

### 13.3 Confidentiality

Encryption is deferred. Future encryption remains a storage encoding unless a separate privacy profile deliberately changes identity semantics.

### 13.4 Dedup privacy

Cross-tenant/global dedup is disabled by default. Chunk equality and CDC behaviour can leak information.

### 13.5 Hostile parsers

CSM, CSP, pack and index readers must use checked arithmetic, explicit bounds, bounded decompression and fuzzing.

## 14. Benchmark and evidence strategy

Architecture freeze decisions require two levels of measurement.

### 14.1 Microbenchmarks

Measure:

- CDC GB/s, cycles/byte, branch/cache behaviour;
- BLAKE3/SHA-256 throughput;
- CSM encode/decode;
- index lookup;
- pack lookup;
- compression.

### 14.2 End-to-end benchmarks

Measure:

- manifest creation;
- direct diff;
- patch create/apply;
- repository ingest/restore;
- remote restore;
- GC/repack;
- index rebuild.

Metrics include:

- elapsed time;
- CPU/TiB;
- allocations/GiB and peak RSS;
- reuse ratio;
- manifest/index bytes;
- patch bytes;
- GET/range count;
- downloaded bytes;
- read/write/compaction amplification;
- random lookup p50/p95/p99.

### 14.3 CDC quality metrics

Required:

- actual mean chunk size;
- distribution and forced-max rate;
- Resynchronization Distance p50/p95/p99/max;
- Boundary Survival;
- Change Amplification = new unique bytes / actually changed bytes.

Algorithms are calibrated to comparable actual means before the holdout comparison.

### 14.4 Corpus

Product-relevant:

- game PAK-like assets;
- large executable/application bundles;
- installers and archives;
- database/VM/data files.

Pathological:

- random;
- zero;
- low-entropy/repeated patterns;
- already compressed;
- encrypted/random-like.

Mutation traces include insert/delete/overwrite/prepend/append/move/reorder and localized/random rewrites at multiple sizes.

### 14.5 Platform matrix

Before a stable chunking profile is frozen:

- Windows/Linux x64;
- Intel/AMD where available;
- ARM64;
- scalar and optimized backend;
- JIT and NativeAOT.

The boundary vector must be identical, not merely statistically similar.

## 15. Compatibility policy

Version these independently:

- NuGet/API version;
- CSM format version;
- CSP format version;
- pack format version;
- index format version;
- HashSuite version;
- chunking profile version.

A stable profile never changes semantics under the same ProfileId.

A new algorithm/table/mask/EOF rule creates a new ProfileId.

Unknown major formats or required features are rejected. Unknown optional physical sections may be skipped.

Golden cross-language vectors are part of the compatibility contract.

## 16. What must be decided before public 1.0

The following are expensive to change after public adoption and must be closed before the 1.0 freeze gate:

1. product boundary;
2. identity taxonomy;
3. 256-bit persistent ID width;
4. HashSuite semantics;
5. all-zero Hash256 validity;
6. semantic ProfileFingerprint rules;
7. logical manifest entry = ChunkId + Length;
8. ManifestId independent of physical encoding;
9. footer/finalize streaming model;
10. fixed-width CSM hot record;
11. stable chunking profile exact semantics;
12. public API ownership/cancellation/result contracts;
13. no mandatory materialized manifests;
14. declarative patch semantics;
15. repository truth vs rebuildable index distinction;
16. immutable/self-indexed pack principles;
17. publication ordering and read-view model;
18. GC reachability/generation model;
19. integrity vs authenticity separation;
20. compatibility/golden-vector policy.

## 17. Deferred decisions

Do not freeze before evidence exists:

- final FastCDC default mean (64/128/256 KiB candidates);
- promotion of SeqCDC/VectorCDC/Chonkers/UltraCDC;
- default pack size;
- default global-index segment size;
- exact filter family;
- stable public Repository API;
- first-party signing;
- encryption/privacy profile;
- Azure/GCS adapters;
- third-party custom chunker/hash plugin API;
- stable `ChunkShift.AspNetCore` protocol/package surface until M4A demonstrates repeated server semantics beyond ordinary ASP.NET Core primitives.

## 18. Definition of architectural success

The architecture is successful if:

- a developer can get useful patch/reuse information within ten minutes;
- the same content model powers patching and future repository storage;
- manifests can contain billions of chunks without object-graph materialization;
- all stable profiles are byte-for-byte deterministic across supported platforms;
- repository indexes can be lost and rebuilt without data loss;
- a crash exposes either an old committed view or a complete new one, never a half-published state;
- optional physical indexes/caches can evolve without changing logical content identity;
- public v1 APIs do not expose internal tuning or strategy types that prevent later optimization.
