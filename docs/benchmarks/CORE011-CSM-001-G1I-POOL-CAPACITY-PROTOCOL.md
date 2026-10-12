# CORE011-CSM-001 / G1-I — bounded CBLK pool retention capacity calibration

**FROZEN before decision runs;** owner [#154](https://github.com/definitely-stable/ChunkShift/issues/154).
Research state `RUNNING / NO_VERDICT_EXPLORATORY`; predecessor
[G1-H](../research/results/CORE011-CSM-001-G1H-20261012-001.md).
Only GitHub-hosted runners, not personal or self-hosted hardware.
No production `src/`, CSM v1, CRC32C, ManifestId, FileDigest,
CSP, public API, NuGet, or default modifications.

## Known source-backed costs

The validated G1-H hybrid reader allocated exactly sized owned
buffers for CBLKs <=80KiB, and rented from a **262144-byte actual .NET
bucket** for larger bounded CBLK with cap4 returned arrays and
`clearArray:true`. G1-H long 16-worker source-free verify saved ~87%
managed allocations and removed measured Gen2 events, but retained
1,652,328 B post-GC vs 601,968 B for existing eager, a ~1.05MiB
idle managed-heap premium. G1-H also established no significant
short-only idle memory premium.

**G1-I question:** can a smaller `maxArraysPerBucket` of **1** or
**2** retain most throughput/allocation gain while reducing idle
managed memory compared with **4**, at genuine 1/4/16-worker
public metadata pressure?

## Frozen variants and exact source gate

Compile from one identical pinned checkout in four **temporary
detached worktrees** per hosted runner:
- `eager`: original `CsmReaderCore` allocation;
- `hybrid1`: G1-H validated lazy-small + pooled-large, cap=1;
- `hybrid2`: exactly same G1-H code, cap=2;
- `hybrid4`: unchanged G1-H code, cap=4.

The only differences between three hybrid implementations are
`maxArraysPerBucket` literal **1/2/4**. Source substitution requires
the exact frozen G1-H original CsmReader SHA256
`a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2`,
one exact verified `maxArraysPerBucket:4` anchor and four distinct
actual source SHA256 in `build.json`. No source replacement or
strategy silently succeeds when CsmReader changes.

Per worktree: Release compile + 254 targeted Core
CSM/ManifestReader/cancellation/golden/bad-input tests each on net8.0
and net10.0, or **2032 total** for each hosted replicate.
Archive all failures, no cherry-picked passing process samples.

## Experiment unit, workload, runs and ordering

**Two independent GitHub-hosted Ubuntu24.04 jobs**, matrix
`replicate=0|1`, each on a separately allocated runner.
Both test the exact same code, deterministic manifest/versioned inputs,
.NET10 SDK pin and workload corpus. Freeze the variant ordering
per case and replicate; it rotates by case index and reverses for
replicate1. Archive that `order.json` and fail closed if mutated.
This partially controls run-order bias; the jobs can differ in
hardware load/CPU scheduling and are **not paired CPU measurements
across hosts**. Only within-runner matched observations are compared.
There is no opportunistic rerun selection.

Each runner completes **24 fresh-process lanes / 120 timed rounds**:
4 variants x six existing *public API* workloads:
- `ChunkPatch.PlanAsync`, 1MiB and 8MiB changed pairs, positive reuse
  and missing count, same physical CSP and ManifestId, exact verified
  plan oracle, 5 rounds x 8 calls, one worker.
- Source-free public `ChunkManifest.VerifyManifestAsync` for 1MiB
  /11 chunks at worker1, and 384MiB/5052 chunks/2 CBLKs at
  worker1/4/16, 5 rounds x 16 calls/worker. After source creation
  WeakReference is verified collected before measurement, SHA CSM
  `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a`.
  Exact logical chunk/oracle across all readers.
- **Combined two hosted jobs**: 48 distinct process lanes,
  240 timed rounds and 4064 targeted test executions. No artificial
  claim that workers16 means 16 physical CPU cores (prior maxActive
  4-5 on four logical CPUs).

No GC injection *between* timed rounds; source-free cases warmup,
explicit GC, timed runs, then forced-GC separate retention observation.
Plan intentionally retains source content, and its RSS/heap must not
be confused with source-free verify heap.
CPU/wall are measured as **batch**, not single-operation p50/p95.

## Independent direct bucket capacity measurement

Out of all timed loops, for separate
`ArrayPool<byte>.Create(maxArrayLength=147500,maxArraysPerBucket=cap)`
instances, perform simultaneous Rent for cap=1,2,4 and record
`array.Length`, not inferred from LOH size. Accept actual lengths
>=147500, record `cap*array.Length` as a **theoretical max payload
cached per large bucket**. Not a total process-memory or private
reader-pool ownership proof.

## Acceptance and publication rules

Each runner must complete exactly 24 cells x 5 rounds, with all
base/target CSM/CSP SHA and metadata oracles identical across
variants, **no missing lanes or finite/positive-counter violations**,
exact RunId/checkout and frozen execution order, direct bucket gate,
four correctly hashed distinct sources and 2032 passing tests.
Store all original 24 scenario JSON, four build SHA records, order,
pool probe and summary in one artifact per runner, retained 90 days.
Record exact ZIP/summary SHA and commit compact all-240-round CSV
and independent report in a **separate evidence PR**.

Report paired-by-host medians of managed allocations/call, Gen2 and
post-GC managed heap and last GC LOH, CPU/wall batch with uncertainty.
A candidate is merely **feasible** if it preserves all oracles and
offers a measurable lower retained heap while not losing all
allocation saving under 16-worker load. No forced numerical
CPU acceptance from two shared hosts. A lower theoretical retention
cap is not a measured leak fix or proof of best default.

**Production ADOPT is prohibited in G1-I.** Next gate requires
representative real versioned file pairs, Windows/AOT test eligibility,
pool contention/zeroing effects and independent randomized
longer-running end-to-end Plan sampling. A causal CPU Amdahl claim
requires direct in-path attribution, not subtraction of unrelated
public API timings.
