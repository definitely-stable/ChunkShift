# Core 0.1.x optimization research roadmap

Status: ACTIVE RESEARCH PLAN — implementation is evidence-gated, not permission to change the published release.
Reviewed: 2026-10-11
Baseline: engineering main 913e81ed00b877db4f8d77b3c5dbe9455793f3eb
Program owner: #152; adjacent product program: #7.
Scope: ChunkShift Core (scanner, deterministic chunking, hashing, CSM read/write/verify, API research).
Out of scope: changing CSP v1, Repository packs/indexes, published NuGet package, reselecting the frozen 64 KiB profile.

## 1. Immutable compatibility envelope

The published Core 0.1.0 API has 124 shipped entries. Keep public Stream push/borrowed-memory semantics, callback ordering and backpressure, exact stable profile cut positions, ProfileId/ProfileFingerprint, HashSuite identifiers, all ChunkIds/ManifestIds/FileDigests, persisted CSM v1 bytes and corruption outcomes. Any new API must be additive, have a named consuming workflow and pass the #186 gate. A new semantic chunker/profile must get a new reviewed identity outside Core 0.1.1.

No release publication, release tag, default change, dependency replacement or production adoption follows from an isolated microbenchmark. The engineering repository does not publish NuGet packages.

## 2. Research ordering and bounded resource policy

| Gate | Slice / existing experiment | Owner | Objective | Exit before dependent work |
| --- | --- | --- | --- | --- |
| G0 | CORE011-FUZZ-001 and -002 | #137 | Seeded differential properties, segmentation invariance, low entropy, edit-adjacent, failure/cancel/borrow lifetime | Deterministic CI smoke plus replayable failing input and seed; independent reference and all golden vectors |
| G1 | CORE011-SCAN-001 | #153 | Current .NET 10 baseline with CPU/wall, hash/boundary/I/O attribution, alloc/RSS/read/copy counters | Immutable baseline and no misleading shared-runner throughput claims |
| G1 | CORE011-CSM-001 | #154 | Separate create, manifest-only verify, enumeration and content-verify paths; LOH/GC and concurrent workloads | Identify nontrivial end-to-end components before optimizing |
| G2a | CORE011-HASH-001 | #127 | Compare managed, native and relevant current managed alternatives in full manifest creation | Frozen 25% end-to-end threshold on at least two platforms; identical vectors; RID/NativeAOT and package gates |
| G2b | CORE011-CSM-002..006 | #154 | Logical-record batching, decode-once, buffer/pool topology, read batch, CRC Amdahl | Identical persisted bytes and failure semantics; wins in real path, not only microbench |
| G3 | CORE-VERIFY-003, CORE-BOUNDARY-001, CORE-SOURCE-001 | #186 | Manifest-guided integrity, hash-free boundaries, optional memory/handle source | Explicit distinction between byte integrity and profile-conformance proof; name consumer before API addition |
| G3 | CORE011-SCAN-003..005 | #153 | Read sizes, bounded overlap, pooled raw buffer hygiene | Memory/ownership/cancellation guarantees, measured I/O improvement or rejection |
| G4 | CORE011-SCAN-002, -006, -007, CORE011-JIT-001 | #153 | Exact SIMD candidate, bounded hash pipeline, seekable parallel cutting, cold-start/JIT/AOT | Only after G0/G1 and hash backend verdict; exact scalar differential |
| G5 | PATCH-INCR-001 | #150 | Exact dirty-range manifest reconstruction vs full scan | Mathematical convergence rule or explicit fallback to full scan; identical CSM logical identities |
| G6 | CDC-FUTURE-001..004 | #14, #136 | Matched-mean alternative CDC, stability and bounded resource quality | Separate new semantic ProfileId/fingerprint; calibrated candidates tested once on sealed holdout |

G0 is a correctness prerequisite, not a claim that Core performance is improved. G1 may execute as two small independent measurements once G0 smoke is in place. G2a and G2b can be developed in separate branches and merged only after their own evidence. G3 must re-use the already frozen CORE-VERIFY-003 protocol rather than reinterpreting CORE-VERIFY-002. G4 must be reprioritized using G1/G2 Amdahl results. G5/G6 are research tracks and do not block normal Patching delivery.

## 3. Existing evidence that must not be reopened without a new condition

- #8 selected and froze fastcdc.gear.chunkshift.v1.64k; observed holdout mean around 80.69 KiB, not 64 KiB.
- #127 preliminary Windows/AVX2 backend results suggest hashing is material (~47% of a CPU-bound scan); the ~28% total-scan benefit is a model, not an accepted production measurement.
- CSM-KERNEL-TOPOLOGY-EVIDENCE rejected metadata-only incremental BLAKE3 for v1 (12.8–16.2% slower in measured cases).
- KERNEL-SINGLE-BUFFER-EVIDENCE accepted less copying but did not prove a material end-to-end throughput win.
- SCANNER-API-PHASE2-EVIDENCE did not justify replacing push/delegate/ValueTask delivery.
- CORE-VERIFY-001 is SUPERSEDED by CORE-VERIFY-002 DEFER; CORE-VERIFY-003 is a separate frozen confirmation protocol.
- The 4 KiB CSM reader read-ahead already has a documented decision; do not restart a generic sweep.

## 4. Execution slices (merge independently)

Slice A (merged as PR #286, 2026-10-11): record this roadmap and add CORE011-FUZZ-001 deterministic scanner differential smoke. Compare canonical production ScanAsync with ChunkingReference on fixed, periodic, low-entropy and seeded bytes; verify exact chunks, byte reconstruction, both suites, short/fragmented reads and boundary-adjacent edits. No production source/API changes. The original seeded input and read schedule are explicitly reproducible in test source. #137 remains OPEN for failure/cancel/lifetime coverage expansion and heavy fuzz.

Slice B (in progress; CORE011-CSM-001 memory-only baseline foundation): freeze the G1 baseline launch/workload definitions from #153/#154, reuse the current amdahl, scanner and CSM benchmark harnesses. Record source read count, copied bytes, CPU/wall, bytes hashed, allocations and p50/p95 before implementing a speculative optimization. No heavy runs in ordinary PR CI.

Slice C: implement internal experiment-only ManifestId logical-record staging and BLAKE3 backend comparators under existing ExperimentIds. No release changes; compare one-shot/batched byte stream equivalence against 0.1.0 vectors and independent CSM decoder, then isolate end-to-end deltas before proposing adoption.

Slice D: choose one winning change at a time; validate .NET 8/10, Linux x64/ARM64, Windows x64, JIT, NativeAOT, independent oracle, public-API baseline and package consumer. Reject/DEFER when evidence is missing. Do not combine CSM or hashing changes with experimental SIMD in one decision.

Slice E: resume #186 via CORE-VERIFY-003. If confirmed, make an additive entry point only for named product consumers; keep strict profile-conformance verification separately available.

## 5. Stop conditions, measurement discipline, and CI budget

- Every decision bears an existing ExperimentId; run-specific RunId and durable EvidenceId are required by docs/research/README.md. A source-only test commit is not a RUNNING experiment and supplies no throughput evidence.
- Register the hypothesis, comparison baseline, thresholds, exclusion criteria and exact workload before final decision runs. Calibrate on development/corpus data, never on a sealed holdout.
- Start with deterministic smoke on normal hosted CI, not multiple expensive cross-platform benchmark matrices. Reserve isolated 10-run x64/ARM64/Windows confirmation for a candidate that passed cheap correctness and screening.
- Report CPU seconds/GiB, wall seconds/GiB, p50/p95, spread, memory, allocation, number of source reads, bytes copied and end-to-end outcomes. Never infer throughput from a boundary-only microbenchmark.
- A candidate that changes a shipped digest/CSM byte, exposes pooled data outside callback lifetime, regresses fail-closed validation or violates cancellation is REJECT regardless of speed.
- If a performance difference is indistinguishable from runner variance, mark DEFER rather than claiming an improvement. Negative results remain indexed.
- Avoid allocating full large corpora in test runners; use bounded synthetic smoke and larger isolated hosted jobs. Only GitHub-hosted runners are allowed.
- Core research is parallel to PATCH, not a prerequisite for the Patching release. Updates to the public mirror/nuget occur only through separate publication workflow after acceptance.

## 6. References

- Program: [#152](https://github.com/definitely-stable/ChunkShift/issues/152)
- Scanner/differential: [#137](https://github.com/definitely-stable/ChunkShift/issues/137), [#153](https://github.com/definitely-stable/ChunkShift/issues/153)
- BLAKE3: [#127](https://github.com/definitely-stable/ChunkShift/issues/127)
- CSM: [#154](https://github.com/definitely-stable/ChunkShift/issues/154)
- Verification/API: [#186](https://github.com/definitely-stable/ChunkShift/issues/186)
- Incremental/next-gen: [#150](https://github.com/definitely-stable/ChunkShift/issues/150), [#14](https://github.com/definitely-stable/ChunkShift/issues/14), [#136](https://github.com/definitely-stable/ChunkShift/issues/136)
- Experiment registry: [EXPERIMENT-INDEX.md](EXPERIMENT-INDEX.md)
- Frozen API: [CORE-0.1-API-FREEZE.md](../architecture/CORE-0.1-API-FREEZE.md)
