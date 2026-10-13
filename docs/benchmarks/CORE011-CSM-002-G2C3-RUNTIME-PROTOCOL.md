# CORE011-CSM-002 / G2-C3 — BLAKE3 runtime-phase attribution

**Frozen before G2-C3 measurements. Status: diagnostic-only / NO_PRODUCT_VERDICT.**  
Parent: [#154](https://github.com/definitely-stable/ChunkShift/issues/154), #152.  
Reference: G2-C1 `CORE011-CSM-002/EVIDENCE-20261013-003` and G2-C2 `CORE011-CSM-002/EVIDENCE-20261013-004`.  
Baseline: `main@e1ed235ce7fd69f16fe0e6d91a1e7dfd8b33b542` after merge of G2-C2 PR #339.  
**No Core source, hash suite, CSM wire, public API or defaults are changed.**

## Motivation and falsifiable questions

G2-C2 on an original 5,052-chunk/two-CBLK BLAKE3 CSM showed **distinct within-process CPU time regimes** (about 24 ms and 6–9 ms per full call, including a factor >3 variation of the original repeated baseline). The CPU readings were often comparable to wall time and no Gen2 collections were observed, so merely blaming idle scheduling/GC is not justified.

Question A: Is the transition concentrated in the **first 8 calls of a fresh process** rather than the last 16 calls? Does a fixed, **64-call untimed warmup** remove it?

Question B: Does the transition depend on **Tiered Compilation** or **Dynamic PGO**? Compare exactly these independently configured *runtime processes*, not different source builds:

| Registered mode | `DOTNET_TieredCompilation` | `DOTNET_TieredPGO` | QuickJit / QuickJitForLoops / ReadyToRun |
| --- | --- | --- | --- |
| `tiered-pgo-on` | 1 | 1 | 1 / 0 / 1 |
| `tiered-pgo-off` | 1 | 0 | 1 / 0 / 1 |
| `tiered-off` | 0 | 0 | 1 / 0 / 1 |

The documented .NET runtime flags are from Microsoft's [Compilation config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation). Note that this experiment **does not trace actual Tier0/Tier1 transitions**; the settings establish controlled runtime configurations, not their internal state. Correlation is not proof of JIT causation. Cgroup CPU throttling counters are recorded whenever exposed by the Linux runner.

## Same-byte experiment

1. Build and publish the **unmodified** benchmark project and production `ChunkShift.dll` **once** on a GitHub-hosted Ubuntu 24.04 builder, using the pinned `global.json`. Record Git SHA and cryptographic SHA256 of the Core assembly, benchmark assembly, runtimeconfig and deps. Upload exactly one immutable executable artifact.
2. Start **two separate GitHub-hosted Ubuntu 24.04 runners**, both download the **same** build artifact, check all four SHA256s and source checkout SHA. No per-host rebuild of measured assembly.
3. Each runner uses the unchanged G1-J1 builder, creating the deterministic **384 MiB, 5,052-chunk, two-CBLK BLAKE3+BIDX physical CSM** once in a separate builder process, verifying the existing manifest identity and SHA256 `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a`. Original source content deleted before any consumer process. Every mode/call in that runner consumes the **identical** pinned physical CSM. No physical source or CSM bytes may be uploaded.
4. Each host runs all three modes × two public operations (`ChunkManifest.VerifyManifestAsync`, complete `ManifestReader` enumeration in 31-entry batches) × two preregistered initial warmup counts **0** and **64**, each in an **independent process**: **12 OS processes / 384 individually timed full CSM calls per host**.
5. Host 0 executes mode order PGO on → PGO off → tiering off, prewarm 0 → 64. Host 1 reverses mode order and prewarm order to expose host/order sensitivity. The environment variables are set **at process launch**, not after .NET initialises.
6. Each independent process measures **32 consecutive full calls** with wall-time, process CPU, allocated bytes, GC collections/pauses, current working-set, and cgroup throttled counts/microseconds as available. Per-process preregistered phase summaries: **median of calls 1–8**, **median of calls 17–32** (last 16) for CPU and wall. Do not drop any individual call, even outliers.
7. Each measured call verifies the exact canonical ManifestId, FileDigest, and counts; enumerate additionally checks all indexes and offsets. The fixture's SHA256 and physical count are independently revalidated before measuring. Existing G2-C1/G2-C2 negative second-CBLK CRC exposure proofs remain a separate completed correctness gate; this is **runtime attribution**, not another format or error-semantics decision.
8. A strict source-free JSON guard requires exactly **12 independent process JSONs and 384 raw call records per host**, all mode/environment/fixture settings, same pinned binary SHA, identities, finite CPU/wall timings, sample ordering, and no archived `.csm` or `source.bin` files. Incomplete hosts invalidate overall attribution. Correlate both original ZIP artifacts and SHA256 before interpreting results.

**Rejection/stop criteria:** build/checksum mismatch; corrupted fixture; unverifiable source deletion; missing/duplicate measurements or runtime mode; binary changes; any invalid hash or reader enumeration; one failed hosted runner; nonsensical raw counters. Failed attempts are retained and not cherry-picked.

## Interpretation gate

This study can support:
- `PHASE_CORRELATION_OBSERVED`, `NO_STABLE_PHASE_DIFFERENCE` or `INCONCLUSIVE` **as descriptive research findings**, with both hosts and individual raw rounds visible.
- It **cannot** establish `ADOPT`, a production CPU budget, JIT causation, or a safe default change.
- Even if disabling tiering eliminates the transition, that does **not** prove Tiered Compilation is the root cause unless an independent diagnostic trace establishes actual method tier changes. Likewise process CPU vs wall and cgroup statistics are explanatory covariates, not root-cause evidence.

If a stable within-mode phase transition survives same-binary and both-host controls, the **next** slice should collect a narrowly scoped EventPipe/JIT tiering trace using official dotnet diagnostics or a source-level MethodJitStarted/MethodLoad trace to establish whether Tier1 re-JIT occurred. If results remain noisy or no material throughput advantage persists, stop ManifestId batching research and keep production per-entry accumulator.

The historical Core 0.1.1 `CORE011-CSM-002=DEFER`, G1-J `decisionRunsAllowed=false`, `budgetsLocked=false` are unchanged.
