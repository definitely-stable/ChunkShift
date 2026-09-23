# Scanner API Phase 2 evidence — 2026-09-23

Status: accepted architecture evidence for #20 before final freeze  
Evidence commit: `c9b318e3c7891f656ac598346ddefb9d6fdee493`  
Platforms: GitHub Actions `ubuntu-24.04` x64 and `ubuntu-24.04-arm` ARM64  
Runtime: .NET 10.0.12  
Protocol: 3 independent launches × 6 warmup × 10 measured iterations

## Decision

Phase 2 does **not** demonstrate a reproducible throughput advantage for either:

- the generic fused struct-sink implementation over the simpler delegate kernel; or
- `Task` over `ValueTask` as the per-chunk handler return type.

The final Core v1 direction is therefore:

- retain push/callback delivery;
- retain borrowed contiguous `ReadOnlyMemory<byte>`;
- retain `ValueTask` for the per-chunk handler;
- retain the simpler delegate-based canonical kernel unless later evidence demonstrates a material advantage;
- continue to the 5×10 final freeze run with the final public shape plus a symmetric Task comparator.

This is a simplicity/API decision under performance equivalence, not a claim that ValueTask won a throughput benchmark.

## Confirmed runtime defect fixed before this evidence

Explicit profile resolution previously recomputed `CandidateProfileId`, including the semantic fingerprint and formatted identifier string, during each scan.

That produced roughly 14–44 KiB of avoidable allocation per explicit scan depending on which candidate ID was compared.

The implementation now caches immutable profile registrations once during type initialization:

```text
ProfileRegistration
  Id
  KernelProfile
```

and repeated `ResolveProfile(id)` is guarded by an allocation regression test.

After that correction, the public end-to-end path no longer carries the earlier tens-of-KiB profile-resolution allocation.

## Task vs ValueTask

ARM64 is the most stable comparative environment in this run.

Aggregate Task-vs-ValueTask differences for the fused core were approximately:

| Scenario | Task vs ValueTask |
| --- | ---: |
| async consumer | +0.07% |
| 64 KiB / MemoryStream | -0.10% |
| 64 KiB / fragmented reads | +0.04% |
| 128 KiB / MemoryStream | -0.07% |
| 128 KiB / fragmented reads | +0.01% |
| 256 KiB / MemoryStream | +0.09% |
| 256 KiB / fragmented reads | +0.07% |
| FileStream / 128 KiB | +0.08% |

Launch-by-launch ARM64 ratios also remain at approximately ±0.2%.

On x64 the sign changes between independent launches and several scenarios show materially wider runner noise. Examples include 64 KiB MemoryStream and FileStream, where the apparent winner changes between launches.

Per the approved #20 decision rule, this is performance equivalence rather than a benchmark winner.

## Generic fused sink

On ARM64, fused ValueTask versus the direct generic baseline is effectively neutral:

- approximately -0.1% to +0.5% across the measured scenarios;
- usually within ±0.1% on the synchronous Memory/short-read matrix.

The older delegate wrapper is also effectively neutral on ARM64.

The generic fused path reduces a small constant per-scan allocation relative to the legacy wrapper (roughly 96 bytes in the synchronous cases), but this is O(1) per complete scan, not O(chunk count), and no stable throughput benefit was demonstrated.

The added production machinery:

```text
IChunkKernelSink
DelegateChunkKernelSink
PublicChunkKernelSink
generic ScanAsync<TSink>
generic async state machine
```

is therefore not justified for Core v1 by the measured benefit.

The simpler delegate kernel remains the preferred production implementation.

## Public end-to-end overhead

After precomputing profile registrations, the public end-to-end path is no longer dominated by semantic-ID construction.

On stable ARM64 cases it is effectively equivalent to the direct kernel within practical measurement resolution.

The x64 shared runner remains noisy enough that sub-percent or low-single-digit differences must not be treated as architectural signal.

## Methodology conclusions

The 3-launch design was necessary.

Aggregate P50/Mean alone hides sign changes between launches on the x64 runner. The evidence pipeline therefore now validates and retains:

- exactly 3 launches;
- exactly 10 measured iterations per launch;
- exactly 6 warmup iterations per launch;
- all 30 actual measurements;
- launch-specific means and ratios;
- aggregate Mean/P50/P95/StdDev/CI;
- allocation/GC evidence;
- commit and environment metadata.

## Scope clarifications

The following do not discriminate Task vs ValueTask once both use the same canonical payload kernel:

- bytes copied/GiB;
- payload-buffer working set.

They only need to be revisited if kernel topology changes.

ASP.NET `Request.Body` vs `BodyReader` is transport/host evidence and remains owned by #17, not by the Core callback return-type decision.

NativeAOT correctness remains covered by Heavy Validation. The final freeze will add focused NativeAOT sanity evidence where practical rather than duplicating the complete BenchmarkDotNet matrix under AOT.

## Final freeze gate

Before #20/#16 are closed, run the final public shape and symmetric Task comparator with:

- 5 independent launches;
- 10 measured iterations per launch;
- x64 and ARM64;
- P50/P95 plus launch-specific ratios;
- representative profile/corpus scenarios;
- retained compact machine-readable evidence.

If Task and ValueTask remain equivalent, freeze ValueTask because it composes directly with synchronous completion and modern ValueTask-returning streaming APIs while preserving single-consumption semantics.


## Final freeze result — accepted

Evidence run: GitHub Actions Benchmark lab #111, run `35839567847`  
Evidence head: `7a3e69e9fe2a40415985a7127814d4b08695a434`  
Protocol: 5 independent launches × 6 warmup × 10 measured iterations  
Platforms: `ubuntu-24.04` x64 and `ubuntu-24.04-arm` ARM64

The final freeze gate completed successfully on both architectures. The compact artifacts retain P50/P95, all 50 measured samples per benchmark and launch-specific ratios. The aggregate means below are included only as a readable cross-check; the decision is based on the complete retained evidence.

| Scenario | x64 ValueTask ms | x64 Task ms | Task vs ValueTask | ARM64 ValueTask ms | ARM64 Task ms | Task vs ValueTask |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| async consumer | 5.577 | 5.638 | +1.09% | 12.65 | 12.72 | +0.55% |
| FileStream | 47.746 | 47.237 | -1.07% | 103.96 | 104.10 | +0.13% |
| fragmented short reads | 21.795 | 21.755 | -0.18% | 49.75 | 49.71 | -0.08% |
| 64 KiB profile | 22.163 | 22.013 | -0.68% | 50.00 | 49.95 | -0.10% |
| 128 KiB profile | 21.714 | 21.831 | +0.54% | 49.80 | 49.86 | +0.12% |
| 256 KiB profile | 21.411 | 21.615 | +0.95% | 49.82 | 49.81 | -0.02% |
| game-pak proxy | 10.794 | 10.819 | +0.23% | 24.93 | 24.91 | -0.08% |
| installer proxy | 10.787 | 10.752 | -0.32% | 24.85 | 24.86 | +0.04% |
| low-entropy proxy | 10.772 | 10.886 | +1.06% | 24.87 | 24.89 | +0.08% |
| random proxy | 10.934 | 11.005 | +0.65% | 24.86 | 24.79 | -0.28% |

The x64 sign changes between scenarios and the ARM64 differences remain small (approximately -0.28% to +0.55%). This does not establish a reproducible throughput winner for either return type.

### Frozen scanner direction for Core v1

Freeze the current public candidate:

- push/callback delivery;
- borrowed contiguous `ReadOnlyMemory<byte>`;
- `ChunkScanHandler : ValueTask`;
- ordered, non-concurrent callbacks with natural backpressure;
- cooperative cancellation with no active-handler preemption;
- caller-owned source stream;
- bounded private buffering and unspecified post-failure/cancellation stream position;
- the simpler delegate-based canonical kernel;
- no public pull reader, segmented payload, sync scan, early-stop or owned-chunk API in Core v1.

`ValueTask` is retained because Task and ValueTask are performance-equivalent under the approved evidence rule, while `ValueTask` directly represents the common synchronous-completion path and composes with modern ValueTask-returning streaming APIs. This is not a claim that ValueTask is faster.

The generic fused struct-sink production experiment remains rejected: its small O(1) allocation reduction did not produce a reproducible throughput advantage sufficient to justify the added machinery.

With the contract-regression additions in this PR, #20's evidence work is complete. #16 may close after the merged branch is the repository truth and its remaining lifetime/cancellation/bounded-stream regressions are green.
