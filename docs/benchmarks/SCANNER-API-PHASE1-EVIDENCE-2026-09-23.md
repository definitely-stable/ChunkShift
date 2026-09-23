# Scanner API Phase 1 Evidence — 2026-09-23

Status: Phase 1 decision for #20  
PR: #50 `perf/20-scanner-api-phase1`  
Environment: GitHub Actions `ubuntu-24.04`, .NET 10, BenchmarkDotNet 0.15.8, ShortRun (3 warmups, 3 measured iterations, 1 launch)

## Phase 1 decision

**ADVANCE to Phase 2:** callback push + borrowed contiguous `ReadOnlyMemory<byte>`.

**ADVANCE to Phase 2:** handler return type comparison, `ValueTask` vs `Task`.

**REJECT as the primary Core v1 scanner shape:** public segmented `ReadOnlySequence<byte>`.

**REJECT as the primary Core v1 scanner shape:** stateful pull reader.

**KEEP:** direct internal sink as the performance baseline only.

No public API change is made by Phase 1.

The current public candidate therefore remains:

```csharp
ChunkScanner.ScanAsync(
    Stream source,
    ChunkScanHandler handler,
    ChunkScanOptions? options = null,
    CancellationToken cancellationToken = default)
```

with borrowed contiguous `ReadOnlyMemory<byte>`. The `ValueTask` handler return remains provisional until Phase 2.

## Methodology

All candidates use the same canonical FastCDC boundary semantics through `ChunkBoundaryState`.

Phase 1 prototypes:

1. direct internal `ChunkingKernel` + ValueTask sink baseline;
2. current public callback + borrowed `ReadOnlyMemory<byte>` + ValueTask handler;
3. Task-returning callback adapter over the same canonical kernel;
4. stateful pull-reader prototype with borrowed contiguous payload;
5. segmented callback with `ReadOnlySequence<byte>`.

Correctness tests require identical:

- ChunkId sequence;
- Length sequence;
- Index/Offset recurrence;
- complete byte coverage;

for normal input, one-byte reads and randomized short reads.

The pull and segmented prototypes additionally verify that delivered payload hashes back to the reported ChunkId.

## Important benchmark constraints

The Stream-based segmented prototype does not possess a true zero-copy upstream segmented source. It copies accepted bytes once into reusable pooled segments, the same copy-count class as the contiguous path, and hashes incrementally because no complete contiguous chunk exists.

Therefore this experiment answers:

> Is exposing segmented payload worth its API/lifetime complexity for the current Stream-based Core path?

It does **not** answer whether an ASP.NET `BodyReader`-specific internal path can benefit from upstream `ReadOnlySequence<byte>` without copying. That remains a Phase 2 ASP.NET integration question and does not require a public Core sequence API.

The GitHub runner is shared and ShortRun has only three measured iterations. Large differences are architecture evidence; small differences are directional only.

## Synchronous/no-op consumer — 16 MiB

### MemoryStream

| Candidate | Mean | Allocated / scan | vs direct | vs current public |
| --- | ---: | ---: | ---: | ---: |
| direct kernel | 31.204 ms | 64 B | baseline | -6.9% |
| public ValueTask callback | 33.513 ms | 232 B | +7.4% | baseline |
| Task callback | 33.645 ms | 232 B | +7.8% | +0.4% |
| pull reader | 32.654 ms | 208 B | +4.6% | -2.6% |
| segmented callback | 35.620 ms | 576 B | +14.1% | +6.3% |

Interpretation:

- Task vs ValueTask is effectively tied at this resolution.
- Pull is only about 2.6% faster than the current public callback on MemoryStream, not enough to justify its larger public lifetime/disposal surface by itself.
- Segmented is materially slower and allocates more.

### Representative fragmented reads

| Candidate | Mean | Allocated / scan | vs direct | vs current public |
| --- | ---: | ---: | ---: | ---: |
| direct kernel | 31.296 ms | 96 B | baseline | -4.7% |
| public ValueTask callback | 32.831 ms | 264 B | +4.9% | baseline |
| Task callback | 33.081 ms | 264 B | +5.7% | +0.8% |
| pull reader | 65.450 ms | 240 B | +109.1% | +99.4% |
| segmented callback | 37.344 ms | 608 B | +19.3% | +13.7% |

This is the strongest Phase 1 elimination result.

The current pull prototype is approximately 2x the current callback time under fragmented reads. A public primary API for arbitrary Stream sources must not make fragmented/short-read sources a pathological case.

The segmented path is also consistently worse without providing a copy-count advantage.

### FileStream

| Candidate | Mean | Allocated / scan |
| --- | ---: | ---: |
| direct kernel | 35.403 ms | 912 B |
| public ValueTask callback | 44.944 ms | 1,247 B |
| Task callback | 34.551 ms | 1,251 B |
| pull reader | 40.817 ms | 53,884 B |
| segmented callback | 45.231 ms | 1,444 B |

The FileStream ShortRun is noisy enough that its ordering must not be used to select Task vs ValueTask. In particular, the apparent Task advantage over the public ValueTask callback conflicts with the much tighter MemoryStream/short-read evidence.

The pull reader's ~53 KiB allocation on this FileStream run is a real warning sign for its state-machine/source interaction and another reason not to promote it from Phase 1.

## Genuinely asynchronous consumer — 4 MiB

Each handler suspends with `Task.Yield()`.

| Candidate | Mean | Allocated / scan |
| --- | ---: | ---: |
| direct kernel | 9.692 ms | 9,064 B |
| public ValueTask callback | 8.482 ms | 9,808 B |
| Task callback | 8.864 ms | 9,808 B |
| pull reader | 8.707 ms | 432 B |
| segmented callback | 10.250 ms | 18,160 B |

The confidence intervals are too wide to rank direct/public/Task/pull.

Useful conclusions are narrower:

- no material Task-vs-ValueTask winner is established;
- segmented remains slower and allocates substantially more;
- asynchronous consumer allocation is dominated by the consumer suspension pattern, so it must not be confused with steady-state scanner allocation.

## Slow consumer / backpressure

Each delivered chunk waits approximately 1 ms.

| Candidate | Mean |
| --- | ---: |
| direct kernel | 20.245 ms |
| public ValueTask callback | 20.171 ms |
| Task callback | 20.251 ms |
| pull reader | 20.715 ms |
| segmented callback | 20.695 ms |

All candidates converge when downstream work dominates.

This confirms that the current callback design provides the intended natural backpressure: ChunkShift does not run away into an unbounded producer queue while the consumer is slow.

## First-chunk / early-stop experiment

Measured values:

| Candidate | Memory | File |
| --- | ---: | ---: |
| direct push | 974 us | 1.121 ms |
| public push | 1.275 ms | 1.522 ms |
| Task push | 1.278 ms | 2.090 ms |
| pull reader | 132 us | 188 us |
| segmented push | 1.140 ms | 1.371 ms |

These numbers are **not a fair pure first-byte latency benchmark**.

Push candidates have no early-stop primitive, so the benchmark must throw/catch a sentinel exception after the first callback. This introduces exception cost and very large allocation/noise. Pull naturally returns one result and stops.

Therefore the only valid conclusion is semantic:

> pull has a substantially cleaner early-stop capability.

Phase 1 does not establish early-stop as a common Core v1 requirement. The current v1 product path is full-stream scanning. A later pull/early-stop API can be added if a real workload proves the need.

The first-chunk allocations from this benchmark are intentionally excluded from scanner allocation decisions.

## Why segmented is rejected for Core v1

The segmented candidate adds:

- `ReadOnlySequence<byte>` lifetime semantics;
- multi-segment consumer handling;
- more public conceptual surface;
- incremental hashing on the current Stream topology;
- more benchmark allocations.

It produced no compensating Phase 1 benefit:

- +6.3% vs current public on MemoryStream;
- +13.7% on representative short reads;
- slower with the async consumer;
- approximately 2x current public async-consumer allocations.

A future ASP.NET adapter may still use `BodyReader` / segmented data internally. That does not require exposing `ReadOnlySequence<byte>` in stable Core.

## Why pull is rejected as the primary Core v1 scanner

Pull has two real advantages:

- natural early stop;
- slightly lower MemoryStream overhead in the synchronous benchmark.

But promoting it would permanently add:

- reader object lifetime;
- Dispose/DisposeAsync policy;
- payload validity-until-next-read rules;
- concurrent ReadAsync rules;
- EOF/result-state representation;
- a second public control-flow model.

Against that larger surface:

- MemoryStream gain over current callback is only ~2.6%;
- fragmented reads are ~99% slower than current public callback in the tested prototype;
- FileStream showed ~53 KiB allocation per scan;
- early stop is not a demonstrated primary v1 workload.

Therefore the primary public shape remains push/callback.

The benchmark-only pull prototype is retained until #20 closes so the evidence stays reproducible. It does not move into production/public Core.

## Task vs ValueTask

Phase 1 does not justify a final decision.

On the most stable synchronous comparisons:

- Memory: Task is ~0.4% slower than ValueTask;
- fragmented reads: Task is ~0.8% slower;
- allocation is identical.

The asynchronous run is within ShortRun noise.

Phase 2 must answer this as an API ergonomics/allocation decision, not pretend Phase 1 has a performance winner.

`ValueTask` remains the leading candidate because:

- the callback is invoked once per chunk;
- synchronous completion is common;
- common consumers such as `Stream.WriteAsync(ReadOnlyMemory<byte>, CancellationToken)` already return ValueTask.

But Task has a simpler consumption contract. The final choice remains open.

## Current public callback overhead

The current public callback path is approximately:

- +7.4% vs direct on MemoryStream;
- +4.9% vs direct on representative fragmented reads.

That is small in allocation terms (232–264 B for the entire 16 MiB scan, not per chunk) but material enough in CPU time to investigate before freeze.

The likely source is not payload copying: both paths share the same canonical kernel and one-copy chunk buffer.

The public path adds an adapter layer:

```text
ChunkingKernel
  -> ChunkKernelSink delegate
  -> PublicChunkSink.OnChunkAsync
  -> ChunkScanHandler delegate
```

Phase 2 should measure a fused internal callback adapter that preserves the exact public API while removing one per-chunk delegate hop. This is an implementation optimization experiment, not a new public API candidate.

## Phase 2 finalists

Carry forward:

1. callback + borrowed contiguous `ReadOnlyMemory<byte>` + ValueTask handler;
2. the same callback shape with Task-returning handler;
3. direct internal sink baseline;
4. fused callback implementation prototype for wrapper-overhead attribution.

Do not carry forward as public candidates:

- segmented ReadOnlySequence;
- stateful pull reader.

Targeted pull/segmented tests may remain where they answer early-stop or ASP.NET-internal questions.

## Phase 2 required evidence

Before #20 can close:

- optimize/attribute the current +5–7% public wrapper overhead;
- 64/128/256 KiB candidate profiles;
- x64 and ARM64;
- JIT and NativeAOT where practical;
- non-seekable streams;
- cancellation and bounded read-ahead;
- representative real files;
- ASP.NET `Request.Body` vs internal `BodyReader` adapter;
- final Task vs ValueTask decision.

If Task and ValueTask remain within calibrated noise, choose based on the smaller/safer stable API contract and documented common consumer composition rather than inventing a performance distinction.

## Prototype retention

Until #20 closes:

- keep benchmark-only Task/pull/segmented prototypes for reproducibility;
- none are production dependencies;
- none enter `PublicAPI.Unshipped.txt`.

After the final #20 decision, remove prototypes that no longer support a continuing regression/evidence purpose.
