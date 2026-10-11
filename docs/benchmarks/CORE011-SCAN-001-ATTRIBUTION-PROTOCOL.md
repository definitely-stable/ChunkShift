# CORE011-SCAN-001 — internal-copy/async-callback attribution protocol

Status: **frozen diagnostic pilot definition, non-decision**. Date: 2026-10-11.
ExperimentId: `CORE011-SCAN-001` (G1, issue #153, parent #152).
Predecessor: [counted-I/O pilot](CORE011-SCAN-001-PILOT-PROTOCOL.md) and [archived result](../research/results/CORE011-SCAN-001-PILOT-20261011-001.md).
No API, Core production implementation, frozen profile, hash suites or persisted CSM format changes.

## Why an additional lane is required

The prior pilot measured **ReadAsync bytes**, not scanner-internal **BytesCopied**, and used only a synchronous completed callback. It cannot isolate pending-prefix compaction or show whether awaiting a genuinely suspended callback changes cost. Crucially, the production `ChunkingKernelCounters.BytesCopied` already exists as an internal diagnostic and is updated on actual buffer prefix `CopyTo`; it must be reused instead of recreating or estimating copy counts. This is **not** bytes copied by OS, hashing, Stream, the BCL or the caller.

The production/public scanner path retains a `ChunkScanner` wrapper. This lab lane directly calls the unchanged internal kernel with its opt-in counters, therefore its timings are **not directly comparable** to the public pilot's timings. Report both without claiming a speedup.

## Fixed matrix

- Corpus: deterministic 16 MiB SplitMix64 seed `0xC4855A112026`; exact content SHA-256 in result; 1 MiB only for CI contract smoke.
- Profile: frozen `fastcdc.gear.chunkshift.v1.64k`, not an M1 candidate.
- Hash suites: `chunkshift.blake3-256.v1`, `chunkshift.sha256.v1`.
- Source modes: memory; cyclic short-read memory `1,7,31,4095,65535,2,257`; warm/uncontrolled sequential FileStream.
- Callbacks: `completed` (returns completed ValueTask) and `yield-once` (Task.Yield **on every delivered chunk**; checks borrowed content again after suspension).
- Four **fixed**, fully validated warmup sweeps and five measured sweeps in alternating order; exactly `3 × 2 × 2 × 5 = 60` retained samples.
- Read count, source bytes/EOF, true `ChunkingKernelCounters.BytesCopied`, number of suspensions, callback ordering and full ordered `(Offset,Length,ChunkId)` are checked against the independent in-process reference for every lane. Sampled payload bytes are checked on entry and after async suspension; no extra per-chunk content hash inside the measurement.
- Record wall milliseconds, process total CPU milliseconds, whole-process allocation delta, end-of-sample working set, input bytes and full raw samples. Warmup results are **not selected** by their speed and are not included in the measured distribution.

## Hosted execution

One bounded Linux x64 GitHub-hosted PR pilot only, with independent evaluator and 90-day artifact. The workflow declares both PR head SHA and synthetic checkout SHA, so changes can be replayed accurately. This is **not** an automatically recurring heavy measurement.

```sh
dotnet restore benchmarks/ChunkShift.Benchmarks.slnx
dotnet build benchmarks/ChunkShift.Benchmarks.slnx -c Release --no-restore
dotnet test benchmarks/ChunkShift.Benchmarks.slnx -c Release --no-build --no-restore
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release --no-build -- \
  core011-attribution --size-mib 16 --warmups 4 --rounds 5 \
  --run-id CORE011-SCAN-001/ATTR-<run-id> --output attribution.json
python3 benchmarks/scripts/evaluate_core011_scan_attribution.py \
  --input attribution.json --output summary.json
```

Evaluator fails closed on missing/duplicated lane/round, incomplete bytes/EOF, invalid counters, inconsistent same-source-mode compaction counts across hash/callbacks, missing async suspension, and nonfinite/negative measures. It reports per-lane median wall and CPU, median copies/input ratio and MAD (with min/max), **not an adoption verdict**.

## Limits / decision gates

1. `Task.Yield` is deterministic *forced asynchronous completion*, not a simulation of storage/network latency or a representative application consumer; benchmark includes thread-pool scheduling and continuation overhead.
2. `BytesCopied` excludes the initial source-to-buffer read, hashes, JIT copies, FileStream buffering, and borrowed-memory reads. Compare source read bytes separately. Do not interpret as total DRAM traffic.
3. Whole-process `GetTotalAllocatedBytes` is not per-lane isolated heap accounting; WorkingSet64 at method end is not peak RSS or LOH.
4. File cache is uncontrolled, CPU frequency/scheduler and tiered PGO are not pinned, and synthetic random corpus cannot represent all real content. Five rounds cannot establish robust p95 or cross-platform A/B evidence.
5. Both hash suites must preserve exact chunk IDs, chunk offsets and lengths across all modes. The test must fail if async payload memory changes before callback completes. No new callback may overlap the previous one.
6. G1 remains OPEN/RUNNING. Before a candidate backend gets ADOPT: separately pin run isolation and multiple runner executions, pin final CSM create/verify benchmarks, compare CPU/wall/alloc/RSS with identical path and provenance, re-run semantic/JIT/AOT/golden/independent gates. Hash suite switch is covered separately by #127.
7. Results must be archived with original raw JSON checksum and source SHA, all samples, evaluator and RunId. An Actions artifact URL alone is not durable evidence.

## Acceptance of this instrumentation slice

CI smoke is green on GitHub-hosted runner; 60 pilot samples are preserved; independent evaluator passes; unchanged production src/public API; no speedup claims; a separate evidence PR archives results after review.
