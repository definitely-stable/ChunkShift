# CORE011-SCAN-001 — exact compaction and callback attribution, two hosted pilot runs

Status: **RUNNING / EXPLORATORY / NO ADOPT VERDICT**. Recorded 2026-10-11.
Issue: [#153](https://github.com/definitely-stable/ChunkShift/issues/153); parent [#152](https://github.com/definitely-stable/ChunkShift/issues/152).
Instrumentation: [PR #290](https://github.com/definitely-stable/ChunkShift/pull/290), [frozen attribution protocol](../../benchmarks/CORE011-SCAN-001-ATTRIBUTION-PROTOCOL.md), [earlier public scanner I/O pilot](CORE011-SCAN-001-PILOT-20261011-001.md).
**All 120 raw samples** are stored in [samples.csv](data/CORE011-SCAN-001-ATTRIBUTION-20261011-001/samples.csv), not just aggregated tables.

## Run provenance (do not confuse PR head with GitHub synthetic checkout)

| Run | GitHub-hosted workflow | RunId | PR HEAD | Actual GITHUB_SHA checkout |
| --- | --- | --- | --- | --- |
| A | [#38158574325](https://github.com/definitely-stable/ChunkShift/actions/runs/38158574325) | `CORE011-SCAN-001/ATTR-38158574325-1-b3277022-linux-x64` | `cd61d3b803152b6d5d31da563a6f78e2da7b9438` | `b327702234695d41590000110f3c8194112b3eb2` |
| B | [#38158739586](https://github.com/definitely-stable/ChunkShift/actions/runs/38158739586) | `CORE011-SCAN-001/ATTR-38158739586-1-5a46d22a-linux-x64` | `67d507b51651ca933a657c604f2fc7161bd81f56` | `5a46d22ac648def3de66e48a1b511bc7fd66b977` |

- Actions artifacts: A `core011-attribution-38158574325` (id `11709103809`), B `core011-attribution-38158739586` (id `11708758313`). The artifacts retain the source JSON and independently evaluated JSON for 90 days; all original sample values remain durable in the CSV in this commit.
- Original JSON SHA-256: A `e52ba33220d8ef7c2273edf1380156298acc0a54ba4996202ebd8048c5a1bbe6`; B `ce09dcf3e834aa26e7b369aa54f52de2d596ada3eda5000948ef1911d5b6197f`.
- Independent evaluator JSON SHA-256: A `ddb7521b3400d35cdd22e8e0f3d5f9f42567affc9b90b6f6858ce6234b56b691`; B `cdf6ad0e7ddeda2446a155b27f8cb2a035a9e5c769cfeae6661ad0652752f70d`.
- Frozen input: 16,777,216-byte SplitMix64 seed `0xC4855A112026`, SHA-256 `171fdcb724a51a20ce68aa3aca36783a5ee5627ab0c9d03ff149c49cd8ffdd71`, `fastcdc.gear.chunkshift.v1.64k`, 201 exact chunks, BLAKE3-256/SHA-256.
- Environment: .NET 10.0.12, Ubuntu 24.04.5 LTS, X64 hosted runner, 4 visible logical CPUs. CPU model was **not recorded** by this harness; do not infer identical physical hardware from runner labels.
- Four fixed warmup sweeps, five measured sweeps, three source shapes, two hashes, two callback shapes: **60 complete samples per run**.
- Dataset `samples.csv`: canonical LF line endings, 120 samples plus header. CSV byte SHA-256: `1f0fb3a100ee7348d504dc1368d3eb5d45f6253eff2da7534f6062bc70197466`.

## Exact structural counters

| Source | ReadAsync calls | Source bytes | Kernel pending-prefix BytesCopied | Copied/input | Async yield completions |
| --- | ---: | ---: | ---: | ---: | ---: |
| MemoryStream | 258 | 16,777,216 | 4,079,778 | 24.317% | 0 or 201 per callback kind |
| Fragmented MemoryStream | 1,693 | 16,777,216 | 4,547,596 | 27.106% | 0 or 201 per callback kind |
| Sequential FileStream | 258 | 16,777,216 | 4,079,778 | 24.317% | 0 or 201 per callback kind |

Every sample returned exactly 201 ordered `(Offset,Length,ChunkId)` entries equal to `ChunkingReference`, followed by a single EOF. Borrowed content spot-checks succeeded both on callback entry and after `Task.Yield`. No overlapping callbacks or invalid reads were detected.

On this fixed corpus and source schedule, fragmentation increased calls by approximately 6.56× and copied bytes by 467,818 (11.47%). These are **structural facts for this corpus**, not forecasts of end-to-end speedup. `BytesCopied` is *only* the existing kernel's pending-prefix compaction, not a cache or total DRAM-traffic counter.

## Wall-time medians (ms), five samples per cell and run

These runs used separate runner instances and different PR synthetic checkout SHAs. The table is descriptive; the A/B column names are **run identifiers**, not candidate/baseline variants.

| Mode | Hash | Callback | Run A median | Run B median | Copied bytes |
| --- | --- | --- | ---: | ---: | ---: |
| memory | BLAKE3 | completed | 14.859 | 18.488 | 4,079,778 |
| memory | BLAKE3 | yield-once | 19.888 | 22.323 | 4,079,778 |
| memory | SHA-256 | completed | 22.981 | 18.514 | 4,079,778 |
| memory | SHA-256 | yield-once | 24.968 | 22.113 | 4,079,778 |
| fragmented-memory | BLAKE3 | completed | 17.172 | 18.482 | 4,547,596 |
| fragmented-memory | BLAKE3 | yield-once | 18.709 | 22.344 | 4,547,596 |
| fragmented-memory | SHA-256 | completed | 23.050 | 18.347 | 4,547,596 |
| fragmented-memory | SHA-256 | yield-once | 25.650 | 20.789 | 4,547,596 |
| file-sequential | BLAKE3 | completed | 21.263 | 26.576 | 4,079,778 |
| file-sequential | BLAKE3 | yield-once | 20.326 | 26.989 | 4,079,778 |
| file-sequential | SHA-256 | completed | 27.299 | 26.213 | 4,079,778 |
| file-sequential | SHA-256 | yield-once | 28.256 | 25.550 | 4,079,778 |

All five samples, whole-process CPU, allocation deltas, end-of-sample working set and exact Git provenance are in the linked CSV. Independent evaluator reported `NO_VERDICT_EXPLORATORY` on both runs.

## Limits and next action

1. Public `ChunkScanner.ScanAsync` baseline and internal `ChunkingKernel.ScanAsync` counters are different call paths. Never subtract their wall times as if matched optimization variants.
2. `Task.Yield` forces suspension but is not a realistic asynchronous consumer workload. CPU and allocation deltas include runtime/scheduler effects; WorkingSet64 is not peak RSS or LOH.
3. FileStream was not cold-cache controlled; hosted runner CPU model/frequency and OS scheduling were not pinned. The two sets of medians visibly differ. **Do not claim speedup, p95 reliability or selection of a hash backend.**
4. Keep `CORE011-SCAN-001` `RUNNING`, and freeze a properly isolated multi-run baseline before considering `CORE011-SCAN-003` I/O topology or `CORE011-HASH-001` adoption.
5. Next measurement should join same-corpus `AmdahlHarness` with `CORE011-CSM-001` public create/manifest-only verify/enumeration/content verify, include representative larger CSM and concurrency/LOH/GC. The byte-identical CSM and official hash/profile gates remain mandatory for any optimized production change.
