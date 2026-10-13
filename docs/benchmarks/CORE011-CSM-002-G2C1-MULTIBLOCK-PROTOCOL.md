# CORE011-CSM-002 / G2-C1 — multi-CBLK ManifestId product-path A/B

**Status:** FROZEN NONDECISION PILOT / NO_ADOPT, preregistered before first successful G2-C1 run.  
**Parent:** issue #154; previous evidence [G2-B](../research/results/CORE011-CSM-002-G2B-20261013-001.md) and [G2-C0](../research/results/CORE011-CSM-002-G2C-20261013-001.md).  
**Base source:** `main@52f342423573cc7dfb1f07e4d5038212a72883ba` after G2-C0 PR #337, pristine `ManifestIdAccumulator.cs` Git blob `894c61b68bfba111467f061e8a0caf81bc8eea81`.  
**Candidate widths:** baseline (one 36-byte logical record/update), stage64 (2,304 bytes) and stage256 (9,216 bytes). Do not retune these after seeing results.

## Narrow question

G2-B showed large microbenchmark savings for 4,096/8,193 logical entries, but G2-C0 failed to establish consistent public-path improvement for 11/102-chunk single-CBLK CSM and demonstrated extra staging allocations. This experiment crosses the real **4,096-entry CBLK boundary** using an existing **source-free**, independent-process builder and verifies the two public metadata paths actually using the production `ManifestIdAccumulator`.

Do **not** use a synthetic 4,096-ID hash loop or rewrite the original CSM bytes to claim a full-product improvement. The public read and enumerate paths must validate real, writer-produced bytes.

## Frozen fixture / isolation

- Build three independently compiled production Core assemblies in separate detached worktrees from the **same** CI commit, using existing `core011_csm_g2c_variants.py`. Refuse any unexpected original source Git blob, source drift, working tree dirtiness or unavailable hash-suite support. Tracked `src/` in the PR and main must remain unmodified.
- Use unchanged `CsmG1jIsolatedHarness.Build` in a different baseline builder process; it generates real CSM from an on-disk deterministic SplitMix64 content stream, verifies it with public API, persists both SHA256 and exact `ManifestId` / `FileDigest`, then **deletes original source bytes before the consumer is allowed**.
- Frozen cases:
  1. **Short control:** 1 MiB, BLAKE3, BIDX on, expected 1 CBLK.
  2. **Large primary:** 384 MiB, BLAKE3, BIDX on, **>4096 chunks and >=2 CBLKs** (earlier qualified G1-J CSM contained 5052).
  3. **Hash-suite/format cross-check:** 384 MiB, SHA-256, BIDX off, **>4096 chunks and >=2 CBLKs**.
- Each builder must record original source SHA256 (used only as immutable provenance), physical manifest SHA256, chunk count, CBLK count, logical ManifestId, physical FileDigest, builder PID and exact synthetic PR checkout SHA. Consumer refuses remaining source file and corrupt or inconsistent descriptor. Same original CSM is used for *every* compiled Core variant.
- The builder is not timed. Source bytes are never checked into Git or included in Actions artifact.

## Frozen operations / correctness oracles

- Public `ChunkManifest.VerifyManifestAsync` and public `ManifestReader.OpenAsync/ReadAsync` (31-entry destination to cross both intra-CBLK and inter-CBLK boundaries).
- Both operations must end with a matching ManifestId, FileDigest, all logical/physical chunk counts, BIDX flag, physical manifest length and logical content length.
- The enumerator must observe exact zero-based monotonically ordered chunk indexes and contiguous logical offsets, with no early/late/missing entries. Final `VerificationResult` must be valid.
- **Additional negative oracle, executed before any timed runs in *each* consumer:** parse real CSM section headers to locate second CBLK, flip one ID byte *without updating its CRC*, then compare public Verify and public ManifestReader failure flags. They must include `BlockCrc`, agree exactly, and the reader must expose **only 4096 entries from CBLK #1**, not any entry of bad CBLK #2. On the short control, damage CBLK #1 and require **zero exposed entries**. No method is allowed to turn this integrity failure into success.
- Existing net8/net10 Core CSM/ManifestId reader/writer tests run in all three separately compiled variants. Do not claim this narrow negative oracle exhaustively verifies cancellation, malformed later sections, partial I/O or concurrency.

## Measurement protocol and artifact

- GitHub-hosted **Ubuntu 24.04 x64**, .NET 10, one runner allocation. Fresh consumer process **per** operation × variant, not concurrent. For each of 3 cases × 2 operations use **baseline→stage64→stage256→baseline**, except SHA256 case baseline→stage256→stage64→baseline to reduce order bias. A baseline repeat is mandatory for drift interpretation.
- Per process one warmup, **3 measured rounds × 4 full calls**. Record process CPU and wall, managed allocated bytes, GC pauses/counts, current and peak working-set/private memory, and post-forced-GC heap and LOH. Result values **must not** be interpreted as independent single-request latency percentiles; CPU includes harness/identity check overhead.
- Total expected **24 process JSONs and 72 measured round records**, plus three source transforms and exact physical SHA fixture records.
- Strict `core011_csm_g2c1_guard.py` rejects incomplete results, mismatched fixture hashes, changed suite/BIDX/CBLK boundary, corrupt block leaks, missing baseline repeats, missing process provenance and non-finite measurements. Guard must print `NO_VERDICT_EXPLORATORY`, **never** `ADOPT`.
- Archive *only* source-free JSON/raw samples, small receipts and environment metadata. Physical CSM and 384-MiB originals must **never** be uploaded; the original physical CSM stays only on the hosted runner until guard work completes.

## Interpretation and next gate

The G2-C1 **pilot** is a targeted real multi-CBLK diagnostic, not product acceptance: one hosted runner, 3 rounds, synthetic content, only single-worker 384MiB *manifest metadata* (not full Create/Apply). It may produce `CORRECTNESS_PASS` or `FAIL`; performance stays `NO_PRODUCT_VERDICT` regardless of mean ratio. Baseline drift must be shown beside each variant. Exact BIDX/CRC, no damaged-block exposure and both HashSuites are non-negotiable.

If timing suggests a stable benefit on multi-CBLK reads, qualify **another explicitly frozen independent study** with real versioned workloads, calibrated allocation ceilings for short-metadata calls, CPU/wall/RSS noninferiority, independent GitHub runners/platforms, 1/4/16-worker memory and idle retention, and writer-side Create. The historical Core 0.1.1 `CORE011-CSM-002=DEFER` and G1-J `decisionRunsAllowed=false`, `budgetsLocked=false` remain immutable. **No production changes** without a new reviewed decision.
