# Streaming Kernel Single-Buffer Evidence (A1-F05) — 2026-09-24

Status: evidence for FINAL-REPORT A1-F05  
Baseline: `main` @ `9e7fd3a` (64 KiB read buffer + chunk buffer; every consumed byte copied once)  
Candidate: `claude/stoic-hopper-n6yd79` (one pooled buffer of `max(Maximum, 128 KiB)`; only the pending chunk prefix moves)  
Environment: one local x64 machine (Intel Xeon @ 2.10 GHz, 4 vCPU), Ubuntu 24.04, .NET 10.0.12. Single-machine evidence.

## Decision

Read directly into the chunk buffer, and move only the pending prefix when less than one read (64 KiB) of space is left.

- Chunk identities, cut positions and CSM output are unchanged. This is checked by the differential tests and golden vectors. Package smoke evidence is byte-identical between JIT and NativeAOT and to the baseline.
- Bytes copied per input byte drop from 1.0 to 0.15–0.27 for FastCDC and to 0 for zero input and for fixed sizes that tile the buffer.
- Pooled kernel memory drops from `Maximum + 64 KiB` to `Maximum` for the M1 candidate profiles (`Maximum` ≥ 256 KiB).
- End-to-end time is unchanged within noise, because hashing dominates.

A first candidate sized the buffer exactly at `Maximum` and was rejected in review. For `Maximum` ≤ 64 KiB (fixed or small FastCDC profiles, used by the lab and the fixed-size reference path), every read after a cut shrank to the space left, and the pending prefix moved on every read. The buffer floor of two reads (128 KiB) keeps reads at the full size.

## Bytes copied: lab harness

`dotnet run -c Release --project benchmarks/ChunkShift.Benchmarks -- lab --corpus benchmarks/corpus/corpus.v1.json --experiments benchmarks/experiments/experiments.v2.json --only <id>`, field `streaming.bytesCopiedPerInputByte` (source and target together):

| Experiment | Baseline | Candidate |
|---|---|---|
| `fastcdc-game-pak-baseline-64k` | 1.000 | 0.232 |
| `fastcdc-game-pak-baseline-128k` | 1.000 | 0.270 |
| `fastcdc-game-pak-baseline-256k` | 1.000 | 0.153 |
| `fastcdc-zero-baseline-64k` | 1.000 | 0 |
| `fastcdc-zero-baseline-256k` | 1.000 | 0 |

## Read calls and moves across profiles

The table shows `ChunkingKernel.ScanAsync` over 64 MiB of random data with 128 KiB zero runs every 1 MiB, from a forward-only source that serves whole requests. It covers the baseline, the rejected exact-`Maximum` buffer, and the candidate. `ChunkingKernelStreamTests.ReadsStayAtTheFullReadSize` now pins the read counts.

| Profile | Reads (baseline / exact / candidate) | Moved per input byte (baseline / exact / candidate) |
|---|---|---|
| FastCDC M1 64 KiB | 1,025 / 1,036 / 1,036 | 1.000 / 0.261 / 0.261 |
| FastCDC M1 256 KiB | 1,025 / 1,025 / 1,025 | 1.000 / 0.248 / 0.248 |
| FastCDC t256/x1024 | 65,537 / 78,682 / 1,025 | 1.000 / 0.201 / 0.003 |
| FastCDC t4k/x16k | 4,097 / 4,914 / 1,025 | 1.000 / 0.199 / 0.046 |
| Fixed 4 KiB | 16,385 / 16,385 / 1,025 | 1.000 / 0 / 0 |
| Fixed 64 KiB | 1,025 / 1,025 / 1,025 | 1.000 / 0 / 0 |
| Fixed 100,000 B | 1,025 / 1,344 / 1,343 | 1.000 / 0 / 0.310 |

The baseline requested `min(64 KiB, Maximum)` per read, which is why small profiles issued one read per chunk. A fixed size that does not divide the read size (100,000 B) needs one short read per buffer wrap. It is not a production profile.

## End-to-end time: BDN

`ChunkingKernelBenchmarks.StreamingFastCdcBlake3`, 16 MiB, in-process toolchain, mean:

| Target | Baseline | Candidate |
|---|---|---|
| 64 KiB | 16.09 ms | 15.65 ms |
| 128 KiB | 15.59 ms | 15.10 ms |
| 256 KiB | 15.23 ms | 14.82 ms |

The differences are within run-to-run spread (0.2–0.6 ms). No throughput gain is claimed.

## Caveats

- One move can copy most of `Maximum` when a long pending chunk reaches the buffer end. Across the input, each byte moves at most once, because a moved prefix is emitted before the next move.
- The sink's borrowed memory can start at any offset in the kernel buffer. RFC-0002 already limits it to the callback.
