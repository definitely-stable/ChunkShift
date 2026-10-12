# CORE011-CSM-001 — multi-CBLK metadata-only GC/retention confirmation

Protocol frozen: **2026-10-12**, slice `CORE011-CSM-001/G1-C`. Issue [#154](https://github.com/definitely-stable/ChunkShift/issues/154), parent #152. Research **RUNNING**; all outputs exploratory, never ADOPT/REJECT. Prior [8-MiB GC baseline](CORE011-CSM-001-GC-PROTOCOL.md) had only 102 chunks, ~4 KiB CSM and no Gen2 during timed rounds. It cannot assess CBLK allocation/retention under many chunks.

## Hypotheses and essential design correction

H1: CSM metadata-only reader/verify allocations, Gen2 activity or retained managed-heap footprint may become material when physical CBLK data crosses the frozen **4096-entry** block maximum. H2: workers 1/4/16 may exert additional GC or cache pressure even if byte identity and read semantics are unchanged. We do **not** assume either is true.

**Do not time reader operations while retaining the 384 MiB content source.** The lab first creates a real CSM with unchanged `ChunkManifest.CreateAsync` at the stable 64-KiB profile, independently verifies its physical bytes, records SHA-256 of source and CSM, and calls `ReleaseContentForMetadataOnly` on the benchmark fixture. A full Gen2 collection follows, and a `WeakReference` assertion rejects the scenario if the content array survives. No source bytes remain accessible to either measured reader or verifier.

The unchanged `Core011CsmBaselineBenchmarks` fixture is reused; its metadata verification validates the expected *original* content length, chunk count, `ManifestId` and `FileDigest`. It now caches the source SHA-256 during setup and guards `CreateAsync/ScanAsync/VerifyContentAsync` if the content has been released. These lab-only seams do not affect the public default benchmark mode or production.

## Frozen GitHub-hosted confirmation matrix

One hosted `ubuntu-24.04` x64 machine, .NET 10, Release, sequential scenario *processes*, not concurrent matrix jobs. Each case **constructs a new deterministic 384 MiB source, verifies and releases it**. Abort if fewer than **4097 chunks** are found. Require calculated lower bound `ceil(chunkCount/4096) >= 2` and physical CSM at least 64 KiB (not a synthetic fixed-size metadata claim). Real count is measured, not invented.

Each separate process performs the same protocol:
- Generate/verify/release source, explicit full GC and proof weak source dead; `Task.Run` warmup for the selected number of workers; explicit full GC; no forced collection between measurement rounds.
- Two measured rounds × four operations *per worker per round*. Public `VerifyManifestAsync` and `ManifestReader.OpenAsync/ReadAsync` with batch 256 only. Each operation must return the identical expected count and identity; for enumeration all offsets/lengths are validated.
- 12 cases: all 2 hash suites × 2 BIDX states × 2 metadata operations at worker=1 (eight fully matched controls), plus BLAKE3/BIDX=on × workers 4 and 16 × both operations (four concurrency cases).
- Worker launch uses the existing gated `Task.Run` `BatchAsync`; max active counts are diagnostic and not proof of simultaneous core execution. No capping or asymmetric dropping of slow rounds.

This subset controls hash/BIDX effects for workers=1, and tests concurrency on **the same hash/BIDX configuration**. Other suite/BIDX 4/16-worker cells are *not tested*. This is not a sealed performance A/B experiment or evidence of cross-run regression.

## Counters, interpretation, provenance

Every case records `RunId`, exact synthetic PR checkout SHA and separate PR HEAD SHA, .NET/OS/GC mode, seed, original content SHA256, exact manifest SHA256/size, chunk count, lower bound on CBLK blocks, workers/rounds/repetitions/suite/BIDX. Raw samples include process-wide allocated bytes, wall/CPU, Gen0/1/2 deltas, `GC.GetTotalMemory(false)` before/after, end WorkingSet and process-lifetime peak, last-GC LOH size and GC index, completed operations, exact returned total chunks and max-active count.

A separately labeled **untimed GC after the last round** records a *post-GC retained* managed-heap size and LOH size; the large source must remain collectible. This is not measurement of bytes allocated on LOH, of process-only reader buffers, of cold FileStream cache, or of a per-round peak RSS. `GenerationInfo[3].SizeAfterBytes` represents **last-GC LOH size**, and may be stale through rounds with zero GCs. `GC.GetTotalMemory` is a process-wide managed-heap estimate, not true per-thread allocation. The large pre-measure fixture remains in `PeakWorkingSet64` and must never be interpreted as per-round reader RSS.

The independent Python evaluator fails on missing/duplicate cases, wrong corpus/manifest for matched options, missing actual 4096-entry CBLK crossing, retained payload, incomplete enumeration, partial worker batch, nonfinite metrics, mismatched sha/provenance or missing post-GC counters. It saves original raw-file SHA-256, medians/MAD/min/max and a mandatory `NO_VERDICT_EXPLORATORY`. Raw JSON and evaluated summary are retained for 90 days as a GitHub Actions artifact and later copied into a durable research evidence PR with RunId.

## Advancement criteria and risks

- Contract tests run tiny 1-MiB fixtures with a lowered min-chunks gate, plus a test that rejects such fixtures under the production research **4097** gate.
- The production CSM v1 byte contract, ManifestId/FileDigest/CRC, block validation-before-exposure, corruption outcomes, hash suite IDs and public API stay unchanged. `src/` is out of scope.
- Successful bounded hosted run demonstrates multi-CBLK instrumentation and source-data isolation, not a reason to implement pooling. Decision-bearing work needs source-size-matched baseline/candidate, per-scenario true LOH allocation event counts via EventPipe (or defensible alternative), cross-platform evidence and >2 repeat rounds, plus memory-retention accounting under sustained pressure.
- Ignore numeric speedup claims from 1 vs 16 worker batch wall times: operation counts are 4 vs 64. Use normalized per-operation values descriptively, but they are not independent causal comparisons.
