# Boundary-Scan Latency Evidence (A1-F08) — 2026-09-24

Status: evidence for FINAL-REPORT A1-F08; input to A1-F01/F03/F07/F05 sequencing  
Branch/PR: `perf/f08-boundary-scan-counters` / #92  
Measured commit: `70f77bc4ec3a32cc11179c08a626e1ade6401432`, benchmark-lab run 35995900680 (artifacts `boundary-scan-f08-x64`, `boundary-scan-f08-arm64`)  
Environment: GitHub Actions `ubuntu-24.04` (x64) and `ubuntu-24.04-arm` (arm64), .NET 10, BenchmarkDotNet 0.15.8 default job

## Decision

**The FastCDC boundary loop is not bound by the latency of its gear-hash chain.** Scan / ChainFloor, the rule fixed before measuring (latency-bound iff ≤ 1.15 in every round), was:

| | BDN rounds | harness rounds |
|---|---|---|
| x64, 64 KiB | 4.70, 2.37 | 4.58, 4.58, 4.60 |
| x64, 256 KiB | 2.40, 4.68 | 4.56, 4.29, 4.55 |
| arm64, 64 KiB | 7.34, 7.34 | 7.05, 7.05, 7.05 |
| arm64, 256 KiB | 7.33, 7.34 | 7.05, 7.05, 7.05 |

Consequences: A1-F01 (state in locals) and A1-F03 (no per-byte length/mask branches) are worth doing; A1-F07 (2-byte unrolled chain) has little headroom left once they are done.

## Method

All variants run in memory over the same 16 MiB SplitMix64 data, with no I/O, copy or chunk hash (`BoundaryScanKernels`):

- **Scan**: production `ChunkBoundaryState.Scan`, 64 KiB windows as in `ChunkingKernel`, state in a heap object as in the async state machine;
- **ScanLocals**: the same loop and branches, hash/length in locals (A1-F01 prototype);
- **ScalarLocals**: `FastCdcScalar.FindCut`: locals plus split strict/relaxed loops, no per-byte length checks (≈ A1-F01 + F03);
- **ChainFloor**: only `h = (h << 1) + gear[b]` over the bytes each chunk hashes;
- **NoChain**: the same bytes, loads, mask selection and predicate branch, without carrying `h`;
- **Calibrate**: an XOR+ADD dependency chain.

The three cutting variants must produce the identical cut sequence before anything is timed. BenchmarkDotNet ran two rounds with opposite method order. The `f08` harness ran three rounds with alternating variant order, one process per variant and target, 5 s measured after ≥ 3 s / ≥ 5 rounds of stabilization.

Hardware counters: BenchmarkDotNet's `HardwareCounters` is Windows-only, so `perf stat` (`cycles:u`, `instructions:u`, `branches:u`, `branch-misses:u`) counted only the harness's measured window, through `--control fifo`. The PMU probe is fail-closed:

- **arm64:** the probe counted all four events, so the arm64 counter values are **measured**.
- **x64:** the probe could not count them, so there are **no x64 counter values**. The x64 "cycles" in the job summary are an estimate that assumes 2 cycles per calibration step.

## Results (harness medians, ns per input byte)

| Variant | x64 64K | x64 256K | arm64 64K | arm64 256K |
|---|---|---|---|---|
| Scan | 1.200 | 1.199 | 2.048 | 2.049 |
| ScanLocals | 0.839 | 0.866 | 0.975 | 0.979 |
| ScalarLocals | 0.415 | 0.451 | 0.342 | 0.343 |
| ChainFloor | 0.262 | 0.264 | 0.291 | 0.291 |
| NoChain | 0.429 | 0.426 | 0.484 | 0.474 |

arm64 counters, per input byte, 64 KiB (256 KiB within 1%):

| Variant | cycles/B | instructions/B | IPC | branch-miss rate |
|---|---|---|---|---|
| Scan | 6.96 | 25.5 | 3.67 | 0.00006 |
| ScanLocals | 3.31 | 18.6 | 5.63 | 0.00005 |
| ScalarLocals | 1.16 | 6.24 | 5.38 | 0.00003 |
| ChainFloor | 0.985 | 4.97 | 5.04 | 0.00004 |
| NoChain | 1.64 | 8.48 | 5.17 | 0.00003 |

Time ratios between neighbouring variants:

| | x64 64K | x64 256K | arm64 64K | arm64 256K |
|---|---|---|---|---|
| Scan / ScanLocals (A1-F01) | 1.43 | 1.38 | 2.10 | 2.09 |
| ScanLocals / ScalarLocals (A1-F03) | 2.02 | 1.92 | 2.85 | 2.85 |
| ScalarLocals / ChainFloor (A1-F07 headroom) | 1.58 | 1.71 | 1.18 | 1.18 |

## Interpretation and caveats

- On arm64, the production loop executes 25.5 instructions per byte at IPC 3.67. Branch misses are negligible. The loop is **instruction-bound**, not latency-bound. Moving the state into locals removes about 7 instructions per byte. Splitting the loops removes about 12 more.
- NoChain is slower than ChainFloor on both architectures. The per-byte mask selection and predicate cost more than the carried chain itself.
- ChainFloor and NoChain hash only chunk-relative positions [Minimum, length). Scan also walks the first `Minimum` bytes of each chunk, cheaply. Their ns per *input* byte therefore understate the cost per *hashed* byte. Assuming mean chunk ≈ 1.22 × target and Minimum = target / 4, about 79% of bytes are hashed. On that assumption, which is an estimate, the arm64 floor is about 1.24 cycles per hashed byte, and Scan / ChainFloor stays ≥ 3.6 (x64 harness) and ≥ 5.6 (arm64).
- **The calibration assumption failed where it could be checked.** On arm64 the XOR+ADD step measured 2.81 cycles, not the assumed 2. The x64 cycles/byte estimates in the job summary should therefore not be quoted. The decision above uses only time ratios within one machine and run.
- x64 BenchmarkDotNet rounds are bimodal between processes: ChainFloor was 0.25 vs 0.49 ns/B and ScanLocals 0.84 vs 1.02 ns/B. The harness processes agree within 2%, and arm64 BDN rounds agree within 1%. The likely cause is process-to-process code placement, which is not proven. Even the lowest x64 BDN ratio (2.37) is far above the threshold.
- JIT disassembly of every variant is in the artifacts (`jit-disasm-*.txt`, BDN `*-asm.md`). This document does not rely on it.
- The copy that A1-F05 removes is not measured here, so this evidence says nothing about F05's size relative to F01/F03.
