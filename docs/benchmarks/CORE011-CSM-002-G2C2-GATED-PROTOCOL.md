# CORE011-CSM-002 / G2-C2 — lazy-allocation ManifestId staging screen

**State:** FROZEN NONDECISION PILOT (new hypothesis, NO_ADOPT).  
**Owner:** issue #154 (parent #152); background evidence [G2-B](../research/results/CORE011-CSM-002-G2B-20261013-001.md), [G2-C0](../research/results/CORE011-CSM-002-G2C-20261013-001.md), [G2-C1](../research/results/CORE011-CSM-002-G2C1-20261013-001.md).  
**Base:** `main@da1bb127487458d70eaf45b8719eb8af0944b58a` after PR #338. Frozen pristine accumulator source Git blob `894c61b68bfba111467f061e8a0caf81bc8eea81`.  
**Preregistered trigger and widths:** process first **256** valid logical records using the existing per-entry hash API. On the **257th** valid record, allocate a private staging array and buffer canonical 36-byte records; stage size either **64** records (2,304 data bytes) or **256** records (9,216 data bytes). Flush complete batches and the final partial batch **before** footer. Original prefix/record/footer byte order must remain unchanged. No pooling, no additional allocations on `ChunkCount <= 256` beyond any changed object-header/layout size. Two fixed candidate widths—not an adaptive winner chosen using holdout data.

## Why a new experiment

Previous G2-C0 and G2-C1 found consistent extra per-call managed allocations for unconditional stage64/256 (roughly 2–9 KiB on short operations). G2-C1 multi-CBLK read showed promising but inconclusive timing changes for 5052 chunks/2 CBLKs in both suites. A delayed allocation gate may preserve long-stream throughput while eliminating the unnecessary staging buffer on 11/102-entry short CSM. Gate 256 is fixed *before* running G2-C2 and excludes the old 102-entry short control. A normal 102-chunk manifest must not pay for 2304/9216 staging bytes.

This is a distinct research hypothesis: **no modification to production source in main**. A source-locked script rewrites the exact single production accumulator in detached Git worktrees and compiles each real Core separately. Never add an environment switch or public API option to a shipping package. Fail closed on source drift or dirty worktrees.

## Three source-hashed real binary builds

1. `baseline`: pristine `ManifestIdAccumulator`, no new field or buffer.
2. `gate256-stage64`: lazy 2,304-byte private staging buffer created only on entry 257.
3. `gate256-stage256`: lazy 9,216-byte staging created on entry 257.

`core011_csm_g2c2_variants.py` emits source hashes, pristine blob, exact checkout, width and threshold to independent receipts. The experiment must show distinct transformed SHA256s, equal checkout SHA and pristine original. Compile existing reader/writer/ManifestId tests (net8.0 and net10.0) and benchmark-side **independent canonical byte-oracle tests** across 0/1/255/256/257/4095/4096/4097/8193 counts on all three Core variants. An in-process reflection test in each compiled variant explicitly asserts **no staging array through 256 valid records** and allocated capacity only after record 257 for BLAKE3 and SHA256. A rejected zero-length record must not advance the threshold. Long 5052-record totals must remain correct.

## Immutable fixture and full-product read oracles

Use existing G1-J1 out-of-process baseline builder (real production `ChunkManifest.CreateAsync`, not direct synthetic HashSuite calls). It generates a deterministic content file, builds/validates the physical manifest, records checksum and original builder PID, then deletes original source before any consumer runs. Four immutable cases, pre-pinned from G2-C0/G2-C1:

| Case | Suite | BIDX | Content | Expected chunks | CBLKs | Pinned physical SHA-256 |
| --- | --- | --- | ---: | ---: | ---: | --- |
| short BLAKE3 | BLAKE3 | on | 1 MiB | 11 | 1 | `472b7fcdb8c8c5dd28db9d0e54938824deb862f1153a9bc1de410a30a02c89bb` |
| short SHA256 | SHA256 | off | 8 MiB | 102 | 1 | `f50145ca497cb1a05a91a72e6a696337a0644a46668bbec53042e12ac50f7147` |
| multi BLAKE3 | BLAKE3 | on | 384 MiB | 5052 | 2 | `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a` |
| multi SHA256 | SHA256 | off | 384 MiB | 5052 | 2 | `cb93b8bac6e1bd34fc5e72f7082d6296ea815f1c8a148f9cf0929af2f4a689b6` |

Run **both** public `ChunkManifest.VerifyManifestAsync` and full `ManifestReader` enumeration. Before timing: verify exact physical SHA, profile, ManifestId, FileDigest, offsets/chunk counts, BIDX; flip a byte inside the first ID of CBLK #1 for short or CBLK #2 for multi, without fixing CRC. The entire corrupt later block must be suppressed: short exposes zero entries, multi exposes exactly 4096 earlier entries and both APIs agree on `BlockCrc` failure flags.

Each operation×case uses independent consumer processes, order baseline → stage64 → stage256 → baseline except SHA256 workloads baseline → stage256 → stage64 → baseline. Each process: warmup, **3 rounds × 4 full calls**, source-free, .NET 10 on GitHub-hosted Ubuntu 24.04. Record wall, process CPU, allocations, GC/pause, working set, peak and post-GC LOH/private bytes. Expect **32 process outputs**, **96 complete measured rounds**. Baseline repeat is mandatory to expose drift. No omitted failures, separate source hashes, and no raw content/physical CSM in archived artifacts. All results are screening only.

## Acceptance / explicit disqualifiers

**Semantic gate must PASS:** exact independent one-shot canonical bytes and production identity across empty/256/257 and multi-CBLK; source blob guard; lazy allocation at record 257 only; first/second CBLK CRC early-entry cutoff; identical final ID and FileDigest. Any such failure invalidates the hypothesis. Report small-manifest allocation deltas *in bytes per call* for both suites, with baseline repetition; do not declare allocation improvement simply because the candidate array was never allocated—object fields and harness overhead still cost bytes.

**Performance remains NO_PRODUCT_VERDICT regardless of micro or pilot gain.** No thresholds are calibrated using this same diagnostic run; no product ADOPT, no historical release status changes. G1-J `decisionRunsAllowed=false` and `budgetsLocked=false` stay unchanged.

To qualify production later: separately frozen CPU/wall p50/p95 and allocation/retained RSS budgets on real versioned and short manifests, independent GitHub-hosted runners/platforms, concurrency 1/4/16, writer Create path on large content, cancellation/overflow/disposal/error ordering and goldens. Source original file is never retained or archived by the current read-only consumers. If positive evidence cannot pass these gates, retain `DEFER`.
