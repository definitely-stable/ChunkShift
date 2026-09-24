# Boundary-Scan Latency Evidence (A1-F08) — 2026-09-24

Status: evidence for FINAL-REPORT A1-F08; input to A1-F01/F03/F07/F05 sequencing  
Branch/PR: `perf/f08-boundary-scan-counters` / #92  
Measured commit: `e2fbfeae06d50745859e30919eac31b02939988c`, benchmark-lab run 35998840081 (artifacts `boundary-scan-f08-x64`, `boundary-scan-f08-arm64`). The first run on `70f77bc` warmed the harness variants too little to guarantee Tier1 code (about 14 calls, where tier-up needs 30); it is superseded and not quoted here.  
Environment: GitHub Actions `ubuntu-24.04` (x64) and `ubuntu-24.04-arm` (arm64), .NET 10.0.12, BenchmarkDotNet 0.15.8 default job

## Decision

**The FastCDC boundary loop is not bound by the latency of its gear-hash chain.** The rule was fixed before measuring: latency-bound iff Scan / ChainFloor ≤ 1.15 in every round used. Results:

| | harness rounds | BDN rounds |
|---|---|---|
| x64, 64 KiB | 4.73, 4.88, 4.72 | 2.42, 2.43 — excluded, disagree with the harness |
| x64, 256 KiB | 4.75, 4.58, 4.46 | 4.74, 4.64 — excluded, disagree with the harness |
| arm64, 64 KiB | 7.38, 7.38, 7.39 | 7.34, 7.35 |
| arm64, 256 KiB | 7.38, 7.38, 7.38 | 7.34, 7.34 |

Secondary analysis, added after the first results at review (harness rounds):

| | F01: Scan / ScanLocals | F01+F03: Scan / ScalarLocals | F07 gate: ScalarLocals / ChainFloor |
|---|---|---|---|
| x64, 64 KiB | 1.38, 1.41, 1.38 | 3.16, 3.24, 2.88 | 1.50, 1.50, 1.64 |
| x64, 256 KiB | 1.44, 1.33, 1.29 | 3.15, 3.04, 2.98 | 1.51, 1.51, 1.50 |
| arm64, 64 KiB | 2.16, 2.17, 2.17 | 6.00, 5.99, 6.01 | 1.23, 1.23, 1.23 |
| arm64, 256 KiB | 2.06, 2.04, 2.17 | 6.00, 6.01, 6.00 | 1.23, 1.23, 1.23 |

Consequences:

- A1-F01 (state in locals) and A1-F03 (no per-byte length/mask branches) are worth doing.
- Even after both, the loop is not chain-bound: the F07 gate is above 1.15 on both architectures. These data therefore do not justify A1-F07 (the 2-byte unrolled chain).
- Non-chain overhead remains material after the local/split-loop variants. The predicate/mask work is one measured contributor (see NoChain), but this experiment does not uniquely attribute the entire remaining gap to it.

## Method

All variants run in memory over the same 16 MiB SplitMix64 data, with no I/O, copy or chunk hash (`BoundaryScanKernels`):

- **Scan**: production `ChunkBoundaryState.Scan`, 64 KiB windows as in `ChunkingKernel`, state in a heap object as in the async state machine;
- **ScanLocals**: the same loop and branches, hash/length in locals (A1-F01 prototype);
- **ScalarLocals**: `FastCdcScalar.FindCut`: locals plus split strict/relaxed loops, no per-byte length checks (≈ A1-F01 + F03);
- **ChainFloor**: only `h = (h << 1) + gear[b]` over the bytes each chunk hashes;
- **NoChain**: the same bytes, loads, mask selection and predicate branch, without carrying `h`;
- **Calibrate**: an XOR+ADD dependency chain.

The three cutting variants must produce the identical cut sequence before anything is timed.

Scan and ScanLocals also walk each chunk's unhashed prefix [0, Minimum). That prefix is 21.97% (64 KiB) and 21.88% (256 KiB) of the data; the harness reports it. ScalarLocals, ChainFloor and NoChain skip the prefix. The primary ratio therefore mixes prefix cost, field state and chain cost. The F07 gate compares two variants that hash the same bytes.

Harness (`f08`):

- three rounds with alternating variant order, one process per variant and target;
- warmup of ≥ 64 calls and ≥ 3 s, pausing every 8 calls; the JIT summary in the job log shows every measured method reaching Tier1;
- 5 s measured.

BenchmarkDotNet: two rounds in opposite method order. Every method and case is its own process anyway, so the order only spreads machine drift. BDN rounds enter the rule only if they agree with each other within 10% and with the harness within 15%.

Hardware counters: BenchmarkDotNet's `HardwareCounters` is Windows-only, so `perf stat` counted `cycles:u`, `instructions:u`, `branches:u` and `branch-misses:u` over the harness's measured window only, through `--control fifo`. The PMU probe is fail-closed:

- **arm64:** counted. These are real counter readings, but see the caveats: their absolute meaning failed the calibration cross-check.
- **x64:** not countable, so there are **no x64 cycle or instruction figures**, measured or estimated.

## Results (harness medians, ns per input byte)

| Variant | x64 64K | x64 256K | arm64 64K | arm64 256K |
|---|---|---|---|---|
| Scan | 1.200 | 1.155 | 2.051 | 2.052 |
| ScanLocals | 0.872 | 0.871 | 0.947 | 0.998 |
| ScalarLocals | 0.381 | 0.381 | 0.342 | 0.342 |
| ChainFloor | 0.254 | 0.253 | 0.278 | 0.278 |
| NoChain | 0.407 | 0.404 | 0.462 | 0.464 |

arm64 counter readings, per input byte, 64 KiB (256 KiB within 5%). These are not validated in absolute terms; see the caveats:

| Variant | cycles/B | instructions/B | IPC | branch-miss rate |
|---|---|---|---|---|
| Scan | 6.96 | 26.1 | 3.75 | 0.00005 |
| ScanLocals | 3.21 | 18.6 | 5.80 | 0.00003 |
| ScalarLocals | 1.16 | 6.24 | 5.39 | 0.00002 |
| ChainFloor | 0.942 | 4.68 | 4.97 | 0.00002 |
| NoChain | 1.57 | 8.06 | 5.14 | 0.00003 |

## Interpretation and caveats

- **The arm64 counters are not validated in absolute terms.** The calibration chain's Tier1 loop is 4 instructions per step (`eor; add; sub; cbnz`, printed in the job log). The counters report 12.0 instructions and 2.92 cycles per step for it, at IPC 4.1, which a 2-cycle dependency chain cannot reach. Either the counted window did not run that code, or the runner's `instructions`/`cycles` events do not mean what they are assumed to mean. The cause is not established.
- The decision above rests only on time ratios, which do not depend on the counters. Read the arm64 counter table as relative, same-process-type comparisons at best.
- If the counters were taken at face value: the production loop would execute about 26 instructions per byte at IPC 3.75, making it instruction-bound, not latency-bound. Locals would remove about 7.5 instructions per byte, and splitting the loops about 12 more. ChainFloor would cost about 1.2 cycles per hashed byte (0.94 per input byte ÷ (1 − 0.2197)). These readings are consistent with the time ratios, but they are not independent evidence.
- NoChain is slower than ChainFloor on both architectures. The per-byte mask selection and predicate cost more than the carried chain itself.
- **The calibration premise failed on arm64 and is not used.** The XOR+ADD dependency itself was modeled as a nominal two-cycle floor, but the generated loop also contains loop-control instructions and the observed PMU readings were 2.92 cycles / 12.0 instructions per step despite Tier1 code. The runner therefore does not establish a portable two-cycle calibration. No cycles are derived from this calibration anywhere.
- **x64 BDN results are excluded.** For ChainFloor 64 KiB, both BDN rounds measured 0.49 ns/B against the harness's 0.25 ns/B. BDN ChainFloor 256 KiB matched the harness at 0.25, and so did the first run's BDN rounds in the opposite pairing. The printed BDN disassembly shows identical machine code for both targets: the chain loop is `movzx; mov; lea r15,[r12+r15*2]; inc; cmp; jl`. The 2× difference therefore comes from where that code lands in a given process, not from what the JIT generated. A loop-alignment or front-end effect is likely, but it is not proven. The harness runs agree within a few percent, and arm64 BDN rounds agree with the harness within 3%.
- The copy that A1-F05 removes is not measured here. This evidence says nothing about F05's size relative to F01/F03.
