# CORE011-CSM-002 / G2-B — bounded logical-record staging prototype

Status: **LAB ONLY / diagnostic screen / NO PRODUCT VERDICT**.  
Parent: #154, #152; reason for prioritization: [G2-A original hosted CBLK evidence](../research/results/CORE011-CSM-001-G2A-20261013-001.md).

## Hypothesis frozen before first G2-B timing

The production `ManifestIdAccumulator.Append` hashes **36 bytes per chunk** via a separate incremental `HashSuiteIncrementalHasher.Append` call. On the 4,096-entry G2-A synthetic physical CBLK, 1 hash call/entry took ~310 µs BLAKE3 and ~193 µs SHA-256 per complete identity, versus ~8 µs for decode-only. A bounded staging buffer can reduce backend update **call count** without changing input bytes, but it may increase per-operation allocations and could worsen small-manifest latency. These measurements are not additive Amdahl fractions.

**Independent variable:** producer accumulates 36-byte records and updates the existing hash backend once for each 16 / 64 / 256 records, versus existing 1 record per update. Prefix (domain, two UTF-8 length-prefixed IDs, fingerprint) and footer (two LE UInt64 totals) are encoded identically. Trailing partial batches MUST flush before final totals. Staging is per-operation, privately owned, <=9216 bytes; no static pool/LOH. No public knobs, new dependencies, kernel, or CSM physical-byte changes.

## Frozen diagnostic sequence

1. Instantiate real HashSuite BLAKE3-256 / SHA-256 and registered shipped FastCDC profile.
2. Generate deterministic synthetic chunks with bounded positive lengths; compare `ManifestId` against BOTH unmodified production `ManifestIdAccumulator` and an independently constructed complete canonical byte stream hashed one-shot. Test empty, 1, 15/16/17, 63/64/65, 255/256/257, 4095/4096/4097, and 8193 entries. For every width (1/16/64/256), assert exactly ceil(chunks/width) entry backend calls, identical count/content totals, lifecycle/zero-length rejection, and flush order.
3. Benchmark on .NET 10 GitHub-hosted x64, ShortRun/memory: baseline + staged16/64/256, N=64/4096/8193, two suites (24 cases), exclude data generator from timed methods. Capture original per-iteration samples and env, preserve failed attempts.
4. Keep candidate source strictly under `benchmarks/`. Neither profile nor production reader/writer/accumulator is modified in this phase. No real CSM create/verify speedup conclusion is allowed from this micro-screen.

**Decision lock:** historical Core 0.1.1 `CORE011-CSM-001/EVIDENCE-20261013-006 = DEFER` stands, as do `CORE011-CSM-002=DEFER` administrative statuses; `decisionRunsAllowed=false` and `budgetsLocked=false` in the existing G1-J product contract may **not** be changed by an exploratory PR. G2-B outcome is either `SCREEN_PASS` or `SCREEN_FAIL`, not ADOPT.

## Acceptance and post-screen work

- All logical inputs and resulting IDs exactly match the independent one-shot/original tests (both suites), including empty and cross-batch cases.
- No missing/duplicated record; no stale buffer capacity hashed.
- Worst-case per-instance staging capacity <= 9 KiB and no per-entry managed allocation.
- If the staged option *appears* faster, first qualify an independent full create/manifest-only verify/ManifestReader **A/B** built in isolated source-controlled variants, with byte-for-byte CSM golden/corrupt-later-block comparisons, CPU/wall/allocations/peak and post-idle RSS budgets pinned **before** independent decision runs. Net8/net10, Windows/Linux x64, ARM64, NativeAOT as required.
- If BLAKE3 or SHA-256 is flat/regressed, retain the negative result; no production adoption. Do not auto-retune widths on decision data.

The high-variance ShortRun is only a filter for whether a longer product study is worth the resources. Any apparent micro speedup is NOT a product-level improvement.
