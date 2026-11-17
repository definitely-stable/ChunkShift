# PATCH-TREE-001 / T3-B2C — original BASE content read amplification attribution

Status: PRE-REGISTERED LAB ONLY / NO_PRODUCTION_ADOPT. Owner #184, release freeze #251. Date 2026-10-24.

## Causal question, known prior

Original T3-B2B [repeat-create evidence](../research/results/PATCH-TREE-001-T3B2B-ORIGINAL-REPEAT-CREATE-EVIDENCE-20261024-001.md): old DLL logical source reads **36,133,305 B** (26.6× old 1,356,632 B) and **14,522,936 B** (10.7× old 1,356,632 B) per base-bound real CSP. T3 vs T0S create process CPU medians 11.7× and 4.2×. It is not known how much of that excess is repeated reading versus dictionary compression CPU.

Important already-existing mechanism in main: **shipping default H2-W2 with no base chunk cache** in `CspCreateExecution.ForProcessorCount` from two processors on. The code already contains **H3-W2 with bounded `BaseCandidateCache`**, but it has never been adopted for shipping. H2 reads base records afresh for candidate dictionaries, H3 loads each required record into one bounded producer window and reuses it across candidate work. Thus implement **no new cache yet**; first test this pinned alternative before inventing another implementation.

## Frozen interventions, pairs and outcomes

- **Only** original calibrated ASP.NET Core win-x64 mscordaccore DLL renamed transitions 10.0.10→10.0.11 and 10.0.11→10.0.12 (same source SHA verified in B1/B2A/B2B). Four original .NET calibration transitions are materialized only for original source identity rehash; no sealed holdout.
- **H2**: `CspCreateExecution(WorkerCount:2,UseBaseCandidateCache:false)` with the same exact `CspEncoderPolicy.Default`; this is the shipping default on the GitHub-hosted multicore runner.
- **H3**: same 2 workers, `UseBaseCandidateCache:true` through the existing production-source *research-only* internal execution. No source, CSP format, compression level, chunk profile, candidate count/radius, public SDK API or persisted update-set changes.
- Each mode one warmup and **three** counterbalanced original measured rounds with H2→H3 on even rounds and H3→H2 on odd. All real original CSPs MUST be identical in length **and full SHA-256** across modes and rounds and complete SDK `ChunkPatch.ApplyAsync` must reconstruct the original target with SHA-256 verified.
- For **every** create: wrap only the caller-provided base content stream with a bounded **100,000-event** read-range observer. Count positive returned byte ranges and compute sorted disjoint interval **exact union** (distinct bytes reached at least once), repeated logical bytes = total read returns minus exact union, backward explicit Position/Seek assignments, total seek assignments, count of returned Read calls, and maximum 4-KiB page touches (proxy only). Reject any off-file range or event overflow. This observer is not a transport optimizer; it records ranges, not raw bytes. On a 64-MiB maximum base it retains at most 16,384 per-page counters and 100k compact ranges per create.
- Attribute CSM generation **separately**: one BASE CSM and one target CSM per pair generated via unchanged `ChunkManifest.CreateAsync`, serialized lengths recorded. BASE content read ranges counted in H2/H3 encode are **exclusive of prior source file hash and CSM preparation**, while SDK apply validation/forensic parsing after create is also outside the measured create window.
- Preserve existing `CspCreateStatistics` counters: H3 cache loads, hits and peak retained records/bytes, window peak bytes, and zero residual/outstanding buffers. H2 must not claim cache hits. Capture process CPU time (`Process.TotalProcessorTime`) and Stopwatch wall time; process-global allocated bytes and after-sample WorkingSet/private committed are observations **not private RSS, isolated peak memory or guaranteed resource limits**.
- This is a **stage attribution** comparison: (1) original source checksum+CSM preparation; (2) H2/H3 measured create source-read and encoder CPU; (3) apply/independent F0 checks outside create. Per-encoder candidate CPU, zstd core CPU and actual disk I/O cannot be isolated by the Stream wrapper.

### Frozen interpretation

Report each pair's total, exact unique and repeated logical source bytes, reread multiple, backward seeks, H3 load/hit count and median CPU/wall; no post-hoc thresholds or candidate retuning. Evidence classification:
- `H3_BOUNDED_CACHE_READS_LESS` only when H3 median logical read bytes falls below H2, full CSP SHA equal and exact apply PASS. This does not imply H3 CPU is faster.
- `H3_BOUNDED_CACHE_CPU_PROMISING` only if 3-round median H3 process CPU lower than H2 on BOTH pinned original pairs; otherwise `NO_CPU_WIN`. Even promising original-only is not a production accept.
- If H3 emits different CSP SHA, misses apply, corrupts base hash, breaches event cap or leaves outstanding buffers, mark **UNVERIFIED_NOT_DECISION**.
- Any H3 evaluation for production needs independent memory-capped Windows and Linux holdout study, per-file create CPU SLA and full signed update-set before adoption. Do NOT unfreeze #251 or introduce a shipping default in this slice.

## CI and evidence

PR GH-hosted Ubuntu 24.04 and Windows 2025 run source-free synthetic 2-DLL create/apply + range union/seek/overlap and tamper tests, followed by standard CI/CodeQL/Benchmark/Heavy. Original real benchmark runs **only after reviewed PR merges into main**, first GitHub-hosted Ubuntu x64 push attempt. Lock original pairs run #38136336025, B1 #39967460136, B2A #39977481690 and B2B #40078373627; rehash original assets with `--calibration-only`; verify frozen B1/B2A identities before executing lab. Retain only `ranges.json`, original selections, source checksums and host provenance; delete raw old/new CSP/CSM/targets at end. Independently replay original archived metrics with separate evidence PR.

NO_ADOPT, #184/#251 OPEN.
