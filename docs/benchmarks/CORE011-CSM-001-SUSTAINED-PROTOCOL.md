# CORE011-CSM-001 — G1-E sustained small/large metadata control

**State:** preregistered diagnostic only, `RUNNING / NO_VERDICT_EXPLORATORY`.
Owner [#154](https://github.com/definitely-stable/ChunkShift/issues/154);
parent #152. Follows validated [G1-D source-free LOH trace](../research/results/CORE011-CSM-001-LOH-20261012-001.md).

## What is now known and what is not

G1-D's single trace pair observed one 147,528-byte LOH `System.Byte[]`
tick per verify call on both short and real 5,052-chunk manifests.
`CsmStreamReaderCore._blockBuffer` allocates 147,500 data bytes once
per reader, not once per CBLK. Gen2 collections rose with work volume,
but type+size sampling does not prove lifetime, allocation stack or
pooling benefit. `GC.GetGCMemoryInfo().GenerationInfo[3]` is only the
LOH **size after the last GC**, not LOH allocation count or bytes.

**G1-E question:** With identical public API and hash/BIDX, does
repeated, source-free metadata reading exhibit near-constant
managed allocation per call across 1-CBLK vs multi-CBLK CSM, and
what process-level Gen2 and post-GC retention patterns occur under
matched per-worker repetition counts?

## Frozen matrix / oracle

- One independent .NET process for each of **12 cells**:
  1 MiB (11 chunks) / 384 MiB (5,052 chunks) ×
  public manifest-verify / full enumeration × workers 1 / 4 / 16.
- **Five measured rounds, 16 calls per worker per round**:
  16/64/256 calls per batch. This yields **60 rounds** and 5,600
  calls total across the matrix, 2,800 per fixture size.
- Same deterministic SplitMix64 seed `0xC4855A112026`,
  stable FastCDC 64 KiB profile, BLAKE3, BIDX true,
  MemoryStream-backed physical CSM.
- 384-MiB fixture must yield exactly 5,052 chunks, 182,366 CSM
  bytes, SHA256
  `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a`,
  source SHA256
  `ed785261f102a852b63b197d383441a884bb95bb2712cef8d268a3dba722479d`.
  The smaller fixture must match identity **across all six cells**.
- Creation, fixture digest and content validation happen outside the
  measured region. Source array is released and verified collected
  with a WeakReference before warmup or timing.
  Each operation verifies exact logical ManifestId/FileDigest; each
  enumeration checks all chunk offsets, indexes and lengths.
- Before samples, an untimed batch warmup and full GC. No collection
  between measured rounds. One separate forced-GC retention sample
  **after** all timed rounds. No tracer and no LOH positive-control
  arrays (G1-D already validated trace completeness).

## Measurements

Raw per-round wall/CPU time, process allocated bytes, managed heap
before/after, Gen0/1/2, GC last-index/LOH-last-GC, maxActive workers,
completed calls and verified chunks, post-GC managed/LOH-size/RSS.
Normalized metrics per cell:
`allocBytes / completedOperations`,
`completedOperations / batchWallSeconds`, batch p50 and MAD,
Gen2 sum and post-GC retained heap.

**Do not** treat normalized *batch wall* as single-call latency,
claim p95 from five rounds, compare raw batch wall between worker
counts, interpret lifetime RSS as reader peak, infer an LOH owner
from last-GC size, or infer 16 CPUs ran simultaneously.
Runtime scheduling, allocation pressure, GC heuristics and fixture
history remain potential confounders on a shared hosted runner.

## Acceptance / rejection gates

The independent Python evaluator must reject:
missing/duplicate cells; wrong workload/fingerprint/RunId/checkout;
source surviving prior GC; malformed/changed physical 384-MiB CSM;
inconsistent physical manifests among same-size lanes; missing
rounds; incomplete operation/chunk oracle; invalid worker bounds;
negative/nonfinite counters; nonpositive timed wall values; and
non-exploratory verdicts.

Only GitHub-hosted Ubuntu 24.04 + pinned .NET 10 are permitted.
Archive all 12 original scenario JSON files, evaluator summary,
run provenance and hashes. Commit a compact CSV and a caveated
result document in a *separate evidence PR* after successful hosted
execution. Remain `CORE011-CSM-001 = RUNNING`.

An eventual E3 pooled buffer prototype is conditional on an
independent end-to-end use-case Amdahl measurement. G1-E has **no
pooling candidate and no optimization ADOPT/REJECT decision**.
