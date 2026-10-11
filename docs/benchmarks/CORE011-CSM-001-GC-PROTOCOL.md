# CORE011-CSM-001 — isolated repeated/concurrent GC pilot protocol

Date frozen: 2026-10-11. Status: **non-decision measurement**, ExperimentId `CORE011-CSM-001` (#154, parent #152).
Predecessor: [G1 public CSM baseline foundation](CORE011-G1-BASELINE-PROTOCOL.md), PR #287.
Implementation: `CsmGcConcurrencyHarness` (benchmark project only) and independent Python pilot evaluator.
No Core 0.1.0 public API, production `src/`, hash/default profile, CSM v1 bytes, NuGet or release change.

## Why this slice

The public baseline fixture compares `CreateAsync`, `VerifyManifestAsync`, `ManifestReader` enumeration and `VerifyAsync`, but has no process-isolated memory/GC and concurrency topology. #154 requires repeated and concurrent operations before evaluating whether pool-backed CBLK buffers or decode-once caches can reduce LOH churn. One operation per process is insufficient: this pilot performs repeated calls with actual `Task.Run` workers over immutable shared fixtures, and records Gen0/1/2 counts and runtime-reported memory characteristics. It is a **pilot**, not the requested complete 1M/10M/100M-chunk and 1/4/16/64 worker matrix.

## Fixed sample and workload semantics

- Corpus: deterministic SplitMix64 seed `0xC4855A112026`, frozen `fastcdc.gear.chunkshift.v1.64k`. 8 MiB in automatic PR pilot; separate 384 MiB later only behind explicit resource-aware dispatch and never 384 MiB × 4 workers.
- Each scenario runs in its own fresh .NET 10 **Release** process. Fixture construction, manifest serialization, full initial verification and one per-worker operation warmup are excluded from timed rounds.
- Operations are unchanged public Core methods delegated to the already oracle-checked `Core011CsmBaselineBenchmarks`: `CreateAsync` (with exact byte comparison), `VerifyManifestAsync`, `EnumerateAsync` (batch 256), `VerifyContentAsync`.
- For every timed round, `workers` independent Task.Run tasks are registered behind a common asynchronous gate and each executes exactly `repetitions` operations. Wall time covers dispatch/wait/operation; no claim this is a pure kernel CPU figure. MaxActiveOperations is a diagnostic of the achieved execution overlap, not proof of physical simultaneous execution.
- GC compacting is **not requested**. One full Gen2 GC after fixture and warmup sets a common startup boundary, but no manual GC occurs between measured rounds. This choice affects the measurement and must remain frozen across runs.
- Limit `sizeMiB × workers <= 512`; workers 1..16, repetitions 1..20, rounds 1..10, input 1..384 MiB.

### GitHub-hosted PR pilot (12 isolated scenarios)

One Ubuntu 24.04 x64 runner, 8 MiB corpus, 2 rounds and 2 operations per worker:
- BLAKE3, BIDX on, workers 1 and 4, each of the four methods (8 scenarios).
- SHA-256, BIDX off, workers 1, each of the four methods (4 scenarios).

The hash suite and BIDX setting are **confounded across these families**: their wall times are *not* an admissible head-to-head comparison. This pilot checks collection/correctness and the shape of 1-vs-4-worker pressure. Subsequent experiments must run all suite/BIDX combinations on 1/4/16 workers, and larger CBLK-crossing fixtures if they are to inform a CSM candidate.

## Recorded information

JSON schema `chunkshift.core011.csm-gc-isolate.v1`: RunId, checkout `GITHUB_SHA` and PR HEAD SHA, OS/runtime/architecture/GC mode, profile/fixture source SHA-256, exact manifest SHA-256 and physical byte size, method/suite/BIDX/workers/round/repeat controls. Per measured round: elapsed/process CPU ms, process-wide allocation delta, Gen0/1/2 collection deltas, end WorkingSet/private bytes, lifetime peak WorkingSet, and `GC.GetGCMemoryInfo` last-GC LOH/heap size and index.

**Memory labels are not interchangeable.** `GenerationInfo[3].SizeAfterBytes` is LOH size *after the most recent completed GC*, not LOH allocation volume in a timed round; if no GC happened its value can be stale. `PeakWorkingSet64` is the *lifetime process peak*, including the fixture constructed before measurements, not an isolated per-operation peak. `GetTotalAllocatedBytes` is cumulative process activity, not allocations attributed to a specific worker. Concurrent scenarios are one process per scenario, **not** simultaneous independent runner jobs.

Independent fail-closed Python evaluator checks fixed 12-case matrix, run identity, corpus content digest across all lanes, suite+BIDX manifest consistency, all rounds and operation counts, valid numeric metrics and a positive active-worker count. It reports descriptive medians, raw ranges, Gen2 count, process-peak and original per-file SHA-256 with **NO_VERDICT_EXPLORATORY**. Raw inputs and summary are uploaded as Actions artifacts with 90-day retention; archive in the durable research registry after review.

## Gates and exclusions

- PR CI: 1 MiB smoke for all four operations and a 4-worker enumeration; independent Python evaluator fixture/corruption tests. This is **not** 384 MiB soak or a 1M-chunk synthetic manifest.
- Automatic PR pilot: only the bounded 12-case 8-MiB subset; no expensive 32-scenario factorial, no 16/64 workers, no massive allocations.
- Each create operation must reproduce the identical CSM bytes/ManifestId/FileDigest. Every enumerate/verify operation must validate exact totals and semantic ID, as implemented in the parent benchmark.
- Never infer a pooling win from fewer managed allocated bytes if retained pool memory, Gen2 and process RSS increase. Under #154, only separately frozen candidate A/B runs can issue ADOPT/DEFER/REJECT; preserve existing independent CSM vectors/corruption contract.
- Next: archive actual RunIds and files, extend **isolated 384-MiB multi-CBLK** memory lane on a resource-suitable GitHub-hosted runner, then lock a controlled candidate comparison based on measured *end-to-end* significance. Benchmark lab runtime is not a product release.
