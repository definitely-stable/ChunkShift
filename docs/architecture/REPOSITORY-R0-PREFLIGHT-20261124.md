# Repository R0 — architecture, publication and evidence preflight

Status: **R0 accepted (#475); R0.1 model corrections pending exact-head CI; not a Pack v1 or public API freeze**  
Date: 2026-11-24  
Owners: [#10](https://github.com/definitely-stable/ChunkShift/issues/10), [#133](https://github.com/definitely-stable/ChunkShift/issues/133), [#144](https://github.com/definitely-stable/ChunkShift/issues/144), [#145](https://github.com/definitely-stable/ChunkShift/issues/145)  
Higher authority: [RFC-0001 §12](RFC-0001-target-architecture-2026.md), [PLAN.md](../../PLAN.md), [ROADMAP.md](../../ROADMAP.md).

## Purpose and decision

ChunkShift Core and Patching **0.1.1** were published from the separate publication repository on 2026-11-23. Repository implementation now begins in this *engineering* repository; no public Repository NuGet package, persisted Pack v1 schema or public `IRepository` is authorized by this preflight.

**R0 is the prerequisite to #133 production ingest and #144 Pack v1 work.** First record the full publication/reader/GC invariants and reproduce them with an executable, intentionally small *abstract* state machine. Then implement pack-independent chunk ingest, raw self-indexed packs, independent parsing, and only then #138/#146 experiments before physical format freeze. No extra CDN, signatures, tree-updater or transport SDK at this gate.

This document refines the sequence without overriding accepted RFC-0001. It distinguishes the *current R0 model evidence* from future implementation proof.

## Production and research inputs

| Source | Decision/implication | Limits |
| --- | --- | --- |
| [GitHub Rubicon (2026)](https://github.blog/engineering/architecture-optimization/inside-githubs-new-git-storage-architecture/) | Separate large immutable data from small refs/commit coordination | Their scale results do not predict ChunkShift performance |
| [SOSP 2026: Validating a high-performance cloud object store](https://www.amazon.science/publications/validating-a-high-performance-cloud-object-store-with-lightweight-formal-methods) | Use executable state models, property testing and fault scenarios to make consistency invariants reviewable | Model tests are not production linearizability or durability proof |
| [restic repository design](https://github.com/restic/restic/blob/master/doc/design.rst) | Write packs and index data before publishing snapshots; guard repack/removal | Exact recovery/locking design is different |
| [Kopia architecture](https://kopia.io/docs/advanced/architecture/) | Independently addressable packed content and rebuildable lookup metadata | Pack size cannot be copied without measuring our workloads |
| [Borg 2](https://borgbackup.readthedocs.io/en/stable/internals.html) | Compact immutable index fragments and selective compaction | Verify concrete version-specific behavior before translating layouts |
| [Xet specification](https://github.com/huggingface/xet-core) | Distinguish content chunks, physical containers and reconstruction metadata | Do not adopt its wire format or heuristic by analogy |
| [S3 conditional requests](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html) | Conditional ref writes rather than HEAD-then-unconditional-PUT | Not evidence for local filesystem durability |
| [R2 consistency](https://developers.cloudflare.com/r2/reference/consistency/) | Account for uncertain outcomes after a lost response | Reconciliation protocol belongs to #13 |
| [.NET runtime #135201](https://github.com/dotnet/runtime/issues/135201) | Linux .NET 10 `Flush(flushToDisk: true)` may suppress an fsync failure on affected runtimes | Issue report, not proof of the current runner's runtime behavior |

Research hypotheses, not committed features: locality-aware placement, limited post-dedup delta encoding, experimental compact indexes, and GC/compaction separation. Benchmark them under #138/#146/#145/#147 before adopting. No cited result licenses changing existing Core/CSM/CSP identity semantics.

## Identity and authority boundaries

1. **Logical identity:** `ChunkObjectKey = HashSuiteId + ChunkId`. HashSuite is part of the logical namespace. Neither PackId, storage path, compression nor generation may modify it.
2. **Physical identity:** independently assigned opaque `PackId`, and separately computed strong `PackDigest` over an explicitly specified non-self-referential byte domain.
3. **Immutable truth:** valid packs, manifests/roots, immutable transaction/catalog-generation objects and named refs. **GlobalIndex, filters and caches are rebuildable derived state**; complete loss of refs/roots is *not* recoverable merely by reindexing packs.
4. **Visibility:** sealed pack != committed object. A complete catalog generation is visible only after a successful atomic/conditional update of the named ref.
5. **Reads:** a reader captures a pinned logical view (root + generation). It must be able to finish even after new generations are published. Repack/GC must not delete its last needed physical location.
6. **Multiplicty:** a key may have sparse deliberate alternate locations, but racing duplicate ingest is not permission to multiply active physical copies.
7. **Verification:** decode under bounds, verify raw length and `ChunkId` with the claimed suite before satisfying a logical read. Encoded CRC is neither ChunkId nor a signature.
8. **Trust:** a trusted expected digest establishes integrity; publisher authenticity/anti-rollback remains above Repository as in the existing trust RFC.

## Proposed publication protocol

```text
stream input → bounded verification/staging
  → sealed immutable pack(s) + PackDigest
  → durable immutable index segment(s)
  → durable immutable root/manifest/transaction data
  → immutable CatalogGeneration G+1
  → CompareAndSwap(named ref, expected=G, new=G+1)  ← visibility point
```

The failed CAS writer may retain orphan packs/segments/generation. It must reread the winning generation, rebase its additions and retry conditionally with a bounded retry policy. **A live writer also needs a retention fence for its in-flight staging and sealed objects; GC cannot delete these before commit/abort/lease expiry.** Never overwrite winner data, silently ignore a conflict, or use directory/bucket LIST as the normal index of truth.

A **recovered** index must preserve exactly the physical locations of the retained valid packs; the active logical root/ref must still be supplied by immutable transaction/ref records. The recovery utility must not guess the user's intended published root merely from an orphan pack.

### Reader and GC ordering

A published generation can become unreachable from the current ref while an older pinned reader still uses it. The production system must define a verifiable cross-process safe-retirement protocol (lease/epoch/generation fence or equivalent) *before* enabling deletion. In-process reference counts alone are insufficient for multi-process readers or remote backends. For the first #133 slice, destructive GC is explicitly disabled.

GC candidate selection is reachability based. Mark all retained named roots and reader-safe generations, confirm replacement locations are published, and only then make obsolete objects delete-eligible after the chosen safety horizon.

## Failure and durability contract

| Failure boundary | Required logical result | What R0 proves |
| --- | --- | --- |
| Stage/read/verify fails or is cancelled | No referenced partial chunk | Abstract model only |
| Seal fails or process dies | No referenced partial pack | Abstract model only |
| Pack sealed, segment absent | Orphan, not active | Abstract model only |
| Segment/generation completed, CAS not run | Old ref remains readable | Abstract model only |
| Concurrent CAS conflict | One winner; loser explicitly rebases | Abstract model only |
| Process death immediately after CAS | Published ref resolves to complete data *if durability preconditions hold* | Abstract model only |
| Old reader during publication/repack | Reader view continues to resolve | Abstract model only |
| Power loss, fsync failure, remote uncertain PUT | Backend-specific reconciliation/durability guarantee needed | **NOT verified by R0** |

Minimum backend tiers to specify before accepting #133:

- **Process atomicity:** caller sees only complete immutable data after ref publication. Does not mean data survives OS crash.
- **Host-crash durability:** writes, metadata and parent-directory publication have been flushed/ordered according to a *tested* platform/filesystem protocol. Atomic rename alone is insufficient.
- **Remote acknowledged durability:** conditional update is acknowledged under provider semantics; lost response is `OutcomeUnknown` until reconciliation. An HTTP ETag is not automatically a strong content digest.

**.NET 10 P0 check:** independently reproduce or rule out [dotnet/runtime#135201](https://github.com/dotnet/runtime/issues/135201) on the actual Linux SDK/runtime used by CI. An affected `Flush(flushToDisk: true)` must not be used as sole proof of fsync success. Inject `EIO/ENOSPC`, check errors, and test file + directory sync and same-volume promotion semantics. If deterministic power-cut tests cannot run on GitHub-hosted runners, explicitly limit durability acceptance; do not silently promote model tests to durability evidence.

## Executable model (R0 slice)

`tools/repository-preflight/publication_model.py` is a dependency-free reference *state-machine*, not product code. It models verified stage → seal → prepare immutable complete generation → CAS ref; concurrent stale writers, logical deduplication, reader pins, named refs, crash and orphan GC.

`test_publication_model.py` checks mismatch rejection, immutable physical IDs, no visibility before CAS, duplicate reuse, conflicting content, stale CAS/rebase, retained reader snapshots, multiple GC roots and deterministic crash boundaries. Seeded interleavings exercise the invariants. A dedicated cross-platform GitHub Actions job executes the Python standard-library unittest suite.

The model uses **symbolic keys, lengths and fingerprints**. Active writer pins are simulated inside one process; no cross-process leases, lease expiry or remote fencing are implemented. It has no real BLAKE3/SHA-256 operations, no pack serialization, no filesystem writes, no real process concurrency or CAS primitive. Passing its tests validates the expected logical transitions, **not** that future production implementation obeys them.

The next implementation should reuse these *scenarios* as a common implementation-vs-model trace corpus, without calling the model functions from product code.

## Pre-registered experimental gates

All decisions use source-locked deterministic corpora, fixed environment metadata (OS, runtime, SHA), warm/cold runs and raw individual samples with RunId. Report stored bytes, metadata bytes, bytes/chunk, read operations, read/transfer amplification, end-to-end restore time, per-chunk latency distribution, resident memory and temp storage. Compare on identical logical input.

| Gate | Candidate matrix | Pass criterion |
| --- | --- | --- |
| #133 identity/ingest | SHA-256 + BLAKE3; wrong ID/length; 2/N writers; read/write/cancel faults | Zero incorrect published objects; bounded memory, reproducible failure matrix |
| #144 Pack v1 | raw; 16/32/64/128 MiB target size *policy* | Self-index rebuild exact; independent parser/fuzz; no invalid frame accepted |
| #138 encoding | raw baseline vs independent per-chunk zstd vs bounded blocks | ADOPT only with end-to-end storage/restore gain under stated workload weights |
| #146 locality | strict dedup vs locality policy with measured physical copies | Savings/extra storage and pack fanout, range reads, restore cost all reported |
| #145 index | SoA fixed-width vs optional accelerators; 1M/10M/100M physical where feasible, 1B explicitly measured or projected | Exact point + BatchLocate, bounded RSS, metadata-only compaction; budget agreed from measurements |
| #147 GC | zero/low/high live fractions; crash in every retirement boundary | No last-reachable-copy deletion; retained reader generations safe |
| #148/#13 remote | local, HTTP range, S3-compatible, R2; faults/retries/unknown outcome | Exact verified bytes, bounded resource/request amplification |

**Provisional targets, not format constants:** ≤64 bytes/primary index entry as an initial investigation target (excluding explicit separately accounted auxiliary tables); packed sizes above are test points, not mandatory defaults. A 1B analytical projection must never be labeled a 1B measured test. No number from GitHub/Kopia/Borg/Xet research can be substituted for ChunkShift measurements.

## R0 acceptance and next step

- [ ] Repository model scenarios run on GitHub-hosted Linux and Windows (net8/net10 product unchanged).
- [ ] Independent review confirms no conflicting statements with accepted RFC-0001/PLAN.
- [ ] ROADMAP names published Patching 0.1.1 and Repository R0 as the next active gate.
- [ ] Durability uncertainties and follow-up CI experiments recorded, not asserted solved.
- [ ] PR checks pass on its exact head and findings are reviewed before merge.

After R0 is accepted, **next production code**: #133 pack-independent bounded ingest and real SHA-256/BLAKE3 verification with injected failures. R0 neither closes #133 nor freezes Pack v1. No physical device, TV or phone is required: GitHub-hosted CI is the acceptance venue.

## R0.1 review corrections

Model-only used-PackId history rejects physical identity reuse after GC/restart.
Same-key reconciliation compares symbolic fingerprint and declared length;
readability checks the requested logical key. Regression tests exercise ABA,
length conflict and a fault-injected wrong physical mapping. This is not a
production tombstone scheme or full PackDigest binding. Independently modeled
writer leases, reader pin/retire races, physical digest references and unknown
remote outcomes remain required before their respective implementation gates.
