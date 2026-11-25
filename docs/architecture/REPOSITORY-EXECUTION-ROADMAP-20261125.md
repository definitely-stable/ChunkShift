# Repository implementation roadmap — 2026-11-25

Scope: #10 / #133 / #144 and downstream #138/#146/#145/#147/#148.
This refines the sequencing in PLAN/ROADMAP, not RFC-0001 or persisted formats.
Core/Patching 0.1.1 remains unchanged. R0/R1-A/R1-B1/R1-B2a/H/R1-B2b are
accepted correctness baselines, not production ingest or durability claims.

## External design inputs (not performance evidence for ChunkShift)

- [Restic v0.20.0 file saver](https://github.com/restic/restic/blob/v0.20.0/internal/archiver/file_saver.go),
  [packer manager](https://github.com/restic/restic/blob/v0.20.0/internal/repository/packer_manager.go),
  [uploader](https://github.com/restic/restic/blob/v0.20.0/internal/repository/packer_uploader.go):
  reusable buffers, logical positions independent of physical completion,
  pack-level temporary storage, backpressure at backend boundary.
- [Kopia object writer](https://github.com/kopia/kopia/blob/master/repo/object/object_writer.go),
  [content manager](https://github.com/kopia/kopia/blob/master/repo/content/content_manager.go):
  copy borrowed buffers before asynchronous use, bounded writes and explicit
  pending/writing/failed packs. Mutable branch, not pinned release.
- [Xet](https://github.com/huggingface/xet-core): tiered dedup and locality-aware
  aggregation, independent ingest/remote concurrency; remote adaptation later.
- [Borg 2 repository](https://github.com/borgbackup/borg/blob/master/src/borg/repository.py):
  overlap pack build/store, bound size/count, avoid nested CPU oversubscription.
  Beta branch; not proof of production readiness.
- [Desync v1.1.5 write dedup queue](https://github.com/folbricht/desync/blob/v1.1.5/writededupqueue.go):
  bounded-lifetime in-flight coordination; don't equate claimed hash with verified bytes.

## Binding decisions and non-decisions

Preserve HashSuite-qualified key, immutable pack, separate opaque 256-bit PackId
and physical digest, one suite/pack, logical vs physical ordering, rebuildable
derived index and Catalog/CAS visibility. No public storage interface or mutable
database truth. Finite file-stage session is a correctness oracle and fallback.
Don't optimize loose-file worker concurrency before measuring pack-level paths.
Global physical ordering is not mandatory; logical manifest order is mandatory.
Locality/reproducibility are explicit placement policies, not accidental completion
order. Only verified input may deduplicate; a claimed key never substitutes for
source verification. Single-flight failures must not strand waiters or reuse
invalid bytes. Keep .NET 10 target; no new dependency without demonstrated need.

## Execution and acceptance gates

| Slice | Deliverable | Exit / dependency |
|---|---|---|
| R1-B3a | Private raw payload append | Both suites, exact length/hash/EOF, bounded frames/bytes, poisoned abort; no pack format |
| R1-B3b | Provisional wire layout, physical ceilings, rotation | Preflight header/frame/index/footer/trailer costs, suite rotation, oversized chunk rule; checked arithmetic |
| R1-B3c | Raw self-indexed writer/reader | Fixed trailer, hash-sorted index vs physical order, independent decoder, negative/fuzz corpus, byte-domain for PackDigest |
| R1-B3d | Rebuild and recovery baseline | Exact location multiset from valid packs; reject partial/corrupt packs; no guessing active logical root |
| R1-B4a | Memory/direct append candidates | Borrowed buffer consumed within callback or ownership transferred/copied; identical integrity to file oracle |
| R1-B4b | Early dedup / single-flight | Verified duplicates, bounded pending keys, conflict/fault/cancel convergence, no skipped verification of untrusted streams |
| R1-B4c | Pack build/store overlap | Bounded aggregate memory/pending pack bytes/count, explicit lifecycle, abort and backpressure, no hold-and-wait cycle |
| R1-B4d | Continuous bounded workers, if needed | Compare wave/serial, bounded worker/window/channel; no global reorder unless locality requires it; account nested codec parallelism |
| R1-B4e | Comparative evidence | Source-locked corpora, repeatable hosted runs, raw samples and sealed holdout; ADOPT/DEFER/REJECT |
| R1-C | Durability/failure gates | Start runtime/fsync probe in parallel with B3; real process kill/restart, native I/O error, file+directory ordering; no power-loss claim without evidence |
| #138/#146 | Encoding and locality | Raw baseline, independent compression, placement/read amplification, size-policy matrix, no semantic identity changes |
| #144 freeze | Pack v1 exit | Independent parsing, hostile input, rebuild, physical digest/checksum, bounded writer, measured layout, durability scope |
| #145/#139 | GlobalIndex/Catalog/trust | Bounded segments, exact BatchLocate, 1M/10M/100M physical where feasible; label 1B projections, reader/writer retention and CAS model |
| #147/#148 | GC/repack / remote | Retirement safety before destructive GC; conditional publication and unknown-outcome reconciliation before remote durability claims |

R1-C is not a reason to postpone all raw work; it blocks durability claims and
durable promotion. #10/#133/#144 remain open until their real exit criteria hold.

## Evidence design

First measure raw file-stage vs owned-memory vs direct append under identical
logical workloads; then isolate early dedup, build/store overlap and scheduler
effects. Avoid a full Cartesian matrix: screen fixed 1/2/4/8/16 workers,
16/64/256 KiB chunks, 0/25/50/90/99% duplicates and 16/32/64/128 MiB pack targets,
then confirm selected candidates on preregistered real and adversarial corpora.
Include unequal source latency, tiny/empty chunks, incompressible/low-entropy,
both suites, Linux x64/ARM64 and Windows. Report throughput, CPU, allocations,
peak RSS, temp/pack high-water, open files, bytes read/written per logical byte,
dedup cost, admission wait, writer utilization, p50/p95/p99, random/restore
I/O, finalize/rebuild. Do not equate logical writes with device writes.
Record runner/storage/runtime, commit, raw samples, RunId/EvidenceId per
docs/research; fix acceptance thresholds before confirmation, include noise
and regressions, and choose defaults from evidence. Correctness gates are
non-negotiable; no throughput gain licenses weaker verification.

## Operational constraints

CI-first, GitHub-hosted, exact-head Linux/Windows/Heavy/CodeQL/model gates.
No package publication, Core/CSM/CSP changes, adaptive remote tuning or
destructive GC in current slice. No per-chunk staging optimization without
a measured bottleneck in the actual pack path.
