# ChunkShift 2026 implementation plan

Status: Proposed  
Architecture authority: [RFC-0001: ChunkShift Target Architecture 2026](docs/architecture/RFC-0001-target-architecture-2026.md)

This plan replaces the previous manifest/diff/verify-first roadmap. It intentionally contains no implementation detail beyond what is required to define sequencing, invariants and exit criteria.

## 1. Product target

ChunkShift 1.0 is a .NET content-aware binary update engine.

The first complete user loop is:

```text
old content
  + target content
        |
        v
   create/diff
        |
        v
 patch only missing content
        |
        v
 reconstruct exact target
        |
        v
      verify
```

The future content-addressed repository reuses the same ChunkId/manifest model. Backup, AI dataset hosting, RAG and generic sync are not product pillars.

## 2. Stable package intent

Target stable 1.0:

```text
ChunkShift
ChunkShift.Patching
ChunkShift.Cli
```

Preview until storage evidence is complete:

```text
ChunkShift.Repository
ChunkShift.Repository.S3
```

Research only:

```text
ChunkShift.Experimental
```

Do not create separate stable packages for BLAKE3, generic abstractions, backup, RAG or AI datasets.

## 3. GitHub issue map

The implementation backlog is tracked by [#1 — ChunkShift 2026 architecture synthesis roadmap](https://github.com/definitely-stable/ChunkShift/issues/1).

| Milestone | Issues |
| --- | --- |
| M0 | #2 identity/HashSuite/profile semantics; #3 benchmark lab/corpus |
| M1 | #4 deterministic chunk/hash kernels; #5 CSM candidate; #6 minimal public API/AOT |
| M2 | #7 declarative patch/reconstruction loop |
| M3 | #8 CDC bake-off/profile selection; #9 Core/Patching 1.0 freeze |
| M4 | #10 immutable self-indexed pack repository |
| M5 | #11 global index/catalog/crash/concurrency |
| M6 | #12 reachability GC/repack/lifecycle |
| M7 | #13 HTTP/S3/R2 distribution |
| Research | #14 next-generation CDC and index/filter candidates |

## 4. Non-negotiable architecture constraints

Before implementation proceeds, all work must preserve:

- 256-bit persistent identities;
- BLAKE3-256 default HashSuite and SHA-256 compatibility suite;
- chunking profile independent of HashSuite;
- semantic ProfileFingerprint rather than exact artifact-byte hash;
- logical chunk entry = `ChunkId + UInt32 Length`;
- no Index/Offset/BoundaryKind in manifest identity;
- streaming/batched manifest access;
- CSM as primary binary manifest;
- caller-owned Stream semantics in core APIs;
- no per-chunk heap object requirement;
- no public stable `IChunker`, `IChunkHasher` or `IRepository` before a real substitution need;
- repository truth in immutable content objects, not SQLite/RocksDB;
- self-indexed immutable packs;
- rebuildable global indexes;
- LSM-like immutable index compaction without mandatory WAL/mutable global DB;
- generation/CAS publication for future repository commits;
- reachability-based GC.

## 5. Dependency graph

```text
M0 Architecture correction + measurement lab
 |
 v
M1 Deterministic core + CSM candidate
 |
 +--------------------------+
 |                          |
 v                          v
M2 Minimum useful           Research track
   patching loop            SeqCDC/VectorCDC/
 |                          Chonkers/UltraCDC
 +-------------+------------+
               |
               v
M3 Evidence + compatibility freeze gate
               |
               v
        Core/Patching 1.0
               |
               v
M4 Immutable repository foundation
               |
               v
M5 Global index + catalog + crash/concurrency
               |
               v
M6 GC/repack/lifecycle
               |
               v
M7 HTTP/S3/R2 distribution
               |
               v
       Repository 1.0 gate
```

## 6. M0 — Architecture correction and measurement lab

### Goal

Correct the existing primitives and create the evidence machinery needed before formats/profiles are frozen.

### Deliverables

- identity taxonomy ADR/spec;
- HashSuite ADR/spec;
- fix `Hash256` semantics so all-zero is a valid value;
- replace `HashAlgorithmId` direction with `HashSuiteId`;
- define semantic `ProfileFingerprint`;
- CSM logical model/spec candidate;
- patch semantic model/spec candidate;
- repository truth/index model ADR;
- BenchmarkDotNet microbenchmark project;
- repeatable system-benchmark harness;
- product corpus manifest and mutation generator;
- cross-platform golden-vector framework.

### Invariants

- no stable format published yet;
- no stable repository API;
- chunk profile and hash suite remain orthogonal;
- every frozen identity remains 256-bit.

### Tests

- primitive property tests including all-zero Hash256;
- ID/profile canonicalization tests;
- endian tests;
- NativeAOT/trim smoke tests;
- benchmark reproducibility tests.

### Benchmarks

At minimum:

- BLAKE3 vs SHA-256;
- FastCDC at 64/128/256 KiB actual means;
- fixed-size baseline;
- Rabin or another classical reference;
- SeqCDC reference/prototype if feasible.

### Exit criteria

- no unresolved P0 identity/format/product-boundary questions;
- benchmark harness runs repeatably on at least x64 and ARM64 environments;
- architecture decisions are written as normative docs rather than implied by code.

### Intentionally not included

- repository implementation;
- patch bundle implementation;
- cloud;
- public 1.0 freeze.

## 7. M1 — Deterministic core and CSM candidate

### Goal

Produce a bounded-memory deterministic content map.

### Deliverables

- FastCDC candidate kernel;
- fixed-size reference kernel;
- BLAKE3 HashSuite;
- SHA-256 compatibility HashSuite;
- CSM writer/reader;
- buffered `ManifestReader`;
- manifest verification;
- CLI: `manifest`, `inspect`, `verify`;
- JSON diagnostic/export projection;
- golden vectors.

### Invariants

- no per-chunk heap object is required;
- logical entry is only ChunkId + Length;
- ManifestId is independent of physical block grouping and optional indexes;
- caller owns passed streams;
- optimized backends must match scalar boundaries exactly.

### Tests

- empty and boundary-size inputs;
- random/zero/low-entropy inputs;
- truncated/corrupt CSM;
- bad CRC/feature bits/lengths/overflow;
- fuzz target for CSM;
- x64/ARM64 deterministic vectors;
- JIT/NativeAOT parity.

### Benchmarks

- CDC GB/s and cycles/byte;
- hash GB/s;
- CSM encode/decode;
- allocations/GiB;
- peak RSS.

### Exit criteria

- a manifest larger than available RAM can be processed with bounded memory;
- stable test vectors match on supported architectures;
- no format/parser P0 defects remain.

### Intentionally not included

- patching;
- repository packs;
- GC;
- cloud.

## 8. M2 — Minimum useful patching loop

### Goal

Close the first real user problem: move/reconstruct only what changed.

### Deliverables

- direct file-to-file diff;
- base-file chunk locator;
- CSP patch candidate format;
- patch create;
- patch apply;
- streaming reconstruction;
- final target verification;
- CLI:
  - `chunkshift diff old new`;
  - `chunkshift patch create old new -o update.csp`;
  - `chunkshift patch apply old update.csp -o new`.

### Invariants

- repository is not required;
- patch does not silently trust a wrong base;
- reconstruction is byte exact;
- CLI publishes the destination atomically after successful verification;
- CSP remains declarative: target manifest + required new payload, not a large instruction VM.

### Tests

- all mutation traces;
- repeated/duplicate chunks;
- wrong base;
- truncated/corrupt patch;
- cancellation at every phase;
- disk-full/partial-output behaviour.

### Benchmarks

- patch ratio;
- create/apply throughput;
- peak RSS;
- read amplification;
- Change Amplification;
- Resynchronization Distance.

### Exit criteria

- ten-minute CLI workflow demonstrates real reuse on product corpus;
- every validation case reconstructs exact target;
- patch failure cannot publish an unverified final file through the CLI.

### Intentionally not included

- global repository;
- S3/R2;
- GC.

## 9. M3 — Evidence and 1.0 freeze gate

### Goal

Freeze only decisions supported by system evidence.

### Deliverables

- FastCDC 64/128/256 KiB calibrated comparison;
- SeqCDC/VectorCDC/Chonkers/UltraCDC research comparison where implementations are reliable enough;
- final stable profile selection;
- CSM v1 spec;
- CSP v1 spec;
- stable Core and Patching public API review;
- cross-language golden vectors;
- compatibility policy;
- fuzz/soak evidence.

### Invariants

- no winner is selected by raw GB/s alone;
- actual mean chunk sizes are calibrated before comparison;
- the stable profile must be deterministic across scalar/SIMD/x64/ARM64 implementations.

### Benchmarks

Primary evidence:

- CPU/TiB;
- patch bytes;
- Change Amplification;
- Resynchronization Distance p50/p95/p99/max;
- Boundary Survival;
- manifest bytes/GiB;
- end-to-end elapsed time;
- allocations and RSS.

### Exit criteria

- no unresolved P0 public API or CSM/CSP format issue;
- exactly documented stable default profile;
- package/API compatibility baseline established.

### Intentionally not included

- stable repository API.

This is the gate for `ChunkShift 1.0`, `ChunkShift.Patching 1.0` and stable CLI contracts.

## 10. M4 — Immutable repository foundation

### Goal

Build the physical storage substrate without compromising the stable content model.

### Deliverables

- candidate CSPACK format;
- self-contained embedded PackIndex;
- filesystem repository backend;
- pack builder;
- per-frame None/Zstd storage encoding;
- put/get;
- reconstruction from repository;
- repository verification.

### Invariants

- packs are immutable and self-indexed;
- ChunkId hashes plaintext/uncompressed bytes;
- global location index is rebuildable;
- SQLite/RocksDB is not repository truth;
- no one-small-chunk-per-object storage model.

### Tests

- truncation/corruption;
- duplicate ChunkId locations;
- salvage;
- concurrent readers;
- index loss/rebuild.

### Benchmarks

- pack-size grid;
- sequential restore;
- random lookup p99;
- pack-index bytes/chunk;
- write/read amplification.

### Exit criteria

- multi-TB local test repository operates reliably;
- pack format has enough evidence for the next milestone but remains preview until repository hardening is complete.

### Intentionally not included

- global compacted index;
- GC;
- cloud.

## 11. M5 — Global index, catalog, crash consistency and concurrency

### Goal

Make the repository scalable and crash-consistent without a mutable authoritative database.

### Deliverables

- immutable IndexSegment format;
- L0 index deltas;
- range-partitioned compacted levels;
- optional static filters where measured useful;
- `CatalogGeneration`;
- `Root`;
- `Ref`;
- immutable `ReadView`;
- conditional/CAS publication;
- fsck and full index rebuild;
- background metadata compaction.

### Invariants

- global index need not be synchronously rewritten on every commit;
- ref update happens last;
- loss of global index does not imply data loss;
- readers see an immutable view;
- duplicate physical chunk locations are legal;
- index compaction does not rewrite pack payloads.

### Tests

- crash injection after every publish boundary;
- concurrent writers;
- reader during compaction;
- stale/failed CAS;
- corrupt/missing index run;
- full index loss and rebuild.

### Benchmarks

- lookup p50/p95/p99;
- index bytes/chunk;
- commit latency;
- metadata write amplification;
- 100M and 1B synthetic-key index tests.

### Exit criteria

After every injected crash the repository exposes either the old committed state or the complete new state, never a half-published state.

### Intentionally not included

- deletion of retired payload packs.

## 12. M6 — GC, repack and repository lifecycle

### Goal

Reclaim space without violating any existing read view.

### Deliverables

- external-memory reachability mark;
- per-pack liveness;
- repack planner;
- pack states: ACTIVE -> RETIRED -> DELETE_ELIGIBLE -> DELETED;
- GC epoch/cutoff;
- reader grace/leases where needed;
- integrity scrub.

### Invariants

- reachable data is never deleted;
- data committed after a GC cutoff cannot be collected by that GC;
- replacement packs/indexes are durable and published before old locations retire.

### Tests

- GC concurrent with writer;
- GC concurrent with long reader;
- crash during repack;
- crash after generation publish;
- duplicate locations;
- missing/corrupt pack.

### Benchmarks

- GC RAM;
- bytes rewritten / bytes reclaimed;
- read amplification;
- compaction amplification.

### Exit criteria

Fault-injection and long-running lifecycle tests complete without loss of reachable chunks.

### Intentionally not included

- multi-cloud adapters.

## 13. M7 — HTTP/S3/R2 distribution

### Goal

Validate the architecture against high-latency object stores and static/CDN distribution.

### Deliverables

- HTTP Range content source;
- S3-compatible backend;
- R2 compatibility validation;
- metadata cache;
- range/coalescing planner;
- bounded concurrency/fairness;
- retry/backoff;
- conditional ref publication;
- backend capability contract.

### Invariants

- no per-chunk HEAD path;
- no per-chunk GET default path;
- LIST is not a normal lookup primitive;
- large immutable objects plus tiny mutable refs;
- remote readers verify content IDs.

### Tests

- latency, timeouts and retry;
- partial ranges;
- stale cache;
- ETag/CAS conflict;
- interrupted upload;
- backend consistency/capability differences.

### Benchmarks

- GET/request count;
- downloaded/logical bytes;
- request amplification;
- first-byte and total latency;
- throughput;
- cost/TiB model.

### Exit criteria

Static/CDN patching and remote repository restore work with bounded request amplification and deterministic verification.

### Intentionally not included

- first-party Azure/GCS adapters without demonstrated demand.

## 14. Parallel research track

Research does not block the main product milestones.

Candidates:

- SeqCDC;
- VectorCDC;
- Chonkers;
- UltraCDC;
- Binary Fuse;
- Ribbon/BuRR;
- alternative exact index block encodings;
- SIMD/ISA-specific optimized backends.

Promotion rule:

```text
research -> stable
only after
corpus evidence
+ end-to-end benchmark
+ deterministic cross-platform vectors
+ compatibility review
```

## 15. Decisions that must be closed before public v1

The M3 freeze gate must explicitly close:

1. exact stable ProfileId and profile semantics;
2. 256-bit identity width and HashSuite contracts;
3. all-zero Hash256 semantics;
4. ProfileFingerprint canonical semantics;
5. CSM v1 byte layout and identity rules;
6. CSP v1 semantics;
7. public stream ownership/cancellation/error/result contracts;
8. no mandatory materialized manifest model;
9. exact ChunkEntry public shape;
10. compatibility/golden-vector policy.

Repository pack/index defaults are not required to freeze with Core/Patching 1.0 unless they are exposed as stable repository formats at the same time.

## 16. Work-management rule

Implementation issues must reference:

- the RFC section they implement;
- milestone;
- dependencies;
- invariants;
- tests;
- benchmarks where relevant;
- explicit non-goals.

An issue is not complete when code merely exists. It is complete only when its milestone exit evidence is satisfied.
