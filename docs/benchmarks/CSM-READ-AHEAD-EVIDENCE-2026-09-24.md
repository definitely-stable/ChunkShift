# CSM Reader Read-Ahead Evidence (A2-F11) — 2026-09-24

Status: evidence for FINAL-REPORT A2-F11  
Baseline: `main` @ `5d9e068` (one `Stream.ReadAsync` per section header and small payload)  
Candidate: `perf/a2-f11-csm-read-ahead` (bounded 4 KiB read-ahead in `CsmInput`)  
Harness: `CsmReadGranularityBenchmarks` (`dotnet run -c Release --project benchmarks/ChunkShift.Benchmarks -- micro --filter '*CsmReadGranularityBenchmarks*'`), BenchmarkDotNet 0.15.8 default job, in-process toolchain  
Environment: one local x64 machine (Intel Xeon @ 2.10 GHz, 4 vCPU), Ubuntu 24.04, .NET 10.0.12. Single-machine evidence, not a CI lane.

## Decision

Serve small CSM sections from a bounded **4 KiB** read-ahead buffer and read CBLK payload remainders straight into the block buffer.

- Verdicts, identities, errors, the physical digest and the Stream contract checks are unchanged (tests, fuzz, package smoke below).
- Read calls per verification drop: 15 → 2 (16 entries), 15 → 4 (800 entries), 24 → 10 (13,000 entries).
- With a 20 µs cost per read call, verification is 1.9–4.3× faster for small and medium manifests and 1.13× faster for the large one.
- With free read calls, time is unchanged within the measured spread.
- Reader allocation per verification drops by 12 KiB, because the 4 KiB buffer replaces the former 16 KiB skip buffer.

The first candidate used a **16 KiB** buffer. It was rejected: with free reads it was 5–7% slower on the 800- and 13,000-entry manifests (outside the spread). Bytes read ahead of a CBLK payload are copied twice, and a larger buffer copies more of them.

## Workload

`VerifyManifestAsync` of a synthetic CSM (BLAKE3, BIDX present). The entry counts approximate the manifests of 1 MiB, 64 MiB and 1 GiB sources at a 64 KiB target. The source is a forward-only stream that serves whole requests. It is either free per call or busy-waits 20 µs per call, standing in for a request body, pipe or unbuffered file.

## Results (BDN mean, µs; two runs per version)

| Entries (CSM bytes) | µs per read | Baseline | 16 KiB candidate (rejected) | 4 KiB candidate |
|---|---|---|---|---|
| 16 (1,009) | 0 | 44.7, 43.6 | 44.6, 43.3 | 42.3, 41.7 |
| 16 | 20 | 359.2, 359.1 | 87.3, 86.8 | 85.3, 83.4 |
| 800 (29,233) | 0 | 182.0, 176.2 | 191.2, 194.4 | 178.2, 174.2 |
| 800 | 20 | 499.4, 494.8 | 256.3, 254.9 | 264.4, 257.9 |
| 13,000 (468,613) | 0 | 1,765.7, 1,792.9 | 1,862.5, 1,852.5 | 1,809.5, 1,811.4 |
| 13,000 | 20 | 2,264.2, 2,273.3 | 2,009.8, 2,001.9 | 1,992.0, 2,010.5 |

BDN standard deviations are 1–5 µs for the 16- and 800-entry cases and 16–53 µs for the 13,000-entry case.

| | Baseline | 16 KiB | 4 KiB |
|---|---|---|---|
| Read calls (16 / 800 / 13,000 entries) | 15 / 15 / 24 | 2 / 3 / 9 | 2 / 4 / 10 |
| Allocated per verification | 177.5 KB | 177.5 KB | 165.5 KB |

## Correctness evidence

- `ChunkShift.slnx` tests pass on net8.0 and net10.0. `CsmReadAheadTests` checks that every conformance vector and multi-CBLK manifests give the same verdict, identities or error message for whole, single-byte and irregular read splits. It also checks that trailing bytes are rejected whether they are buffered or found by the end-of-stream probe, and that a seekable source returning more bytes than its `Length` is rejected. It pins the request shapes (a 4 KiB fill, the one-byte probe, or a CBLK remainder read in place).
- Extended fuzz with random read sizes: 2 × (4 seeds × 5,000 iterations). The differential comparison with the independent Python decoder found 0 mismatches.
- Package smoke (JIT and NativeAOT `linux-x64`) evidence is byte-identical to the baseline.

## Caveats

- The per-call cost is simulated by a busy-wait. Real request bodies and pipes cost a different amount per call and often return fewer bytes than requested. A source that trickles a few bytes per call gains nothing, because it bounds every read.
- Full CBLKs still take two reads each (the payload remainder read in place, then a fill that carries the next header), so large manifests gain little.
- Up to 4 KiB per CBLK is copied twice. That is at most 2.8% of a full 147 KiB block and only manifest bytes, not content bytes. It is within the free-read spread above.
- Skipping a large unknown optional section now reads 4 KiB at a time instead of 16 KiB. The writer never emits such sections.
- On failure the manifest stream position may be up to 4 KiB past the failing section; the public remarks say so. With a faulting source, a stream error on read-ahead bytes can surface before a format error in earlier bytes.
