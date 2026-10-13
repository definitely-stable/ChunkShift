# CORE011-CSM-002 / G2-C4 — EventPipe method-tier trace with CSM-call markers

**Frozen before the first G2-C4 CI run. Status: causal-attribution research, NO_PRODUCT_VERDICT.**  
Owner: [issue #154](https://github.com/definitely-stable/ChunkShift/issues/154), #152.  
Predecessor: [G2-C3 original 768-call two-host results](../research/results/CORE011-CSM-002-G2C3-20261013-001.md), merged PR #340.  
**Exact baseline:** `main@f4079cace34b29a955dbfbd1a7ee7a5e68443307`. No production `src/`, CSM/CSP wire, API, defaults or version changes.

## Why another diagnostic is justified

G2-C3 found reproducible association between BLAKE3 CSM latency phases and runtime Tiered Compilation / Dynamic PGO settings. The EPYC 9V74 host showed distinct ~7ms → ~3ms phases around calls 22–25, yet a different EPYC 9V45 machine showed materially different steady-state behavior. CPU mostly tracked wall; no cgroup throttling supported a pure quota explanation. **No MethodLoadVerbose/JittingStarted events were captured**, so claiming a specific Tier0→Tier1 transition or compilation root cause would be invalid.

G2-C4 is intended to capture *actual* JIT/MethodLoad events relative to exact public CSM call markers, to distinguish three non-equivalent claims:

1. Runtime mode/warmup *associates with* performance phases (already observed).
2. Events report which **named methods** were JIT-compiled or loaded at/near those calls.
3. A named method's tier transition *causes* the speed change. **This is not established** by co-occurrence alone: method publication can lag tier-up decisions, tracing changes timings, method inlining/native CPU specialization are confounders. <https://github.com/dotnet/runtime/issues/129020>

## Frozen implementation / installed diagnostics

- Use documented `dotnet-trace collect -- <dotnet child>` to capture process startup (not mid-flight attach), `Microsoft-Windows-DotNETRuntime` EventPipe **verbose** provider with fixed keyword mask `0x1000000018` (JIT `0x10`, Loader `0x8`, Compilation `0x1000000000`); second provider `ChunkShift-G2C4` with informational call-start/call-end/phase markers. Tool package **`dotnet-trace 10.0.750501`** pinned, installed locally in a GitHub-hosted runner only.
- Parse raw `.nettrace` using separately compiled trace-reader project `benchmarks/tools/Core011G2c4TraceReader` with **`Microsoft.Diagnostics.Tracing.TraceEvent 3.2.8`**, emitting `MethodJittingStarted`, `MethodLoadVerbose` records with original timestamps, MethodID/ModuleID/name/MethodFlags *as exposed*, and EventSource call markers. Any no-JIT/no-marker trace fails closed. TraceEvent parser lives in a research-only project, not in the SDK/library dependency graph.
- One `Release/net10.0` **unmodified Core** + benchmark target build, SHA-256 frozen executable across two independently allocated GitHub-hosted Ubuntu 24.04 x64 runner jobs; the analysis project and local dotnet tool may be built per runner but target Core/benchmark binary must be byte-identical and checksum-verified before use.
- Each runner creates a deterministic synthetic 384MiB source in an independent G1-J1 builder, writes and verifies the *same* original BLAKE3+BIDX two-CBLK CSM SHA256 `342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a` (5052 chunks) and deletes source before traced consumers.
- Registered **3 runtime modes**: `TieredCompilation=1,TieredPGO=1`; `1,0`; `0,0`, all with `QuickJit=1`, `QuickJitForLoops=0`, `ReadyToRun=1`. The settings are set at new child-process launch. Two **prewarm lengths: 0 and 64 public VerifyManifest calls**. Only the public `VerifyManifestAsync` operation is traced in this slice; complete reader enumeration already qualified in G2-C3.
- **One fresh traced process per mode×warmup**; all calls verified against physical source SHA, ManifestId and FileDigest. Each process records all warmup calls and **32 individually timed** subsequent full calls. Events `CallStart`/`CallEnd` enclose public calls including warmup; phase marker indicates end of warmup. Original target JSON samples are retained.
- Each independent runner must produce exactly **6 source-free `.nettrace` files + 6 parsed event JSONs + 6 target JSONs**, each with 32 timed calls and chronological paired EventSource markers; two runners total **12 traces, 384 timed calls** (plus 64 warmup calls per warmed process per runner). Controlled order reverses per runner.
- Capture original `.nettrace` bytes and SHA-256 of each, full per-call timestamps/CPU/wall and all observed JIT events; do NOT upload source `.bin` or original `.csm`. Restrict each raw trace size to 32MiB, fail if larger instead of silently truncating events.

## Acceptance

1. Baseline binary and physical CSM SHA256 must match exactly on both hosts.
2. Startup EventPipe trace must include *real* CLR `MethodJittingStarted` or `MethodLoadVerbose` events plus matching custom call boundaries. If events absent, do not claim attribution; fail diagnostic completeness.
3. No missing/duplicate call or phase markers; 32 measured calls per process with all original per-call times; all canonical ManifestId/FileDigest checked on every public call.
4. Preserve original event names, timestamps, and raw MethodFlags rather than assigning undocumented tiers heuristically. Classify named target methods (`ManifestId`, `CsmReader`, `HashSuite`, `Blake`, `ChunkManifest`) as candidates by timestamp; **temporal association is not causation**.
5. No ADOPT, no release CPU threshold, no production accumulator edits. If no relevant method events appear in transition windows or tier settings diverge across hosts, **report inconclusive** rather than asserting a JIT root cause.
6. Full CI, raw archive SHA/CRC, independent parser audit and evidence note are required before any research PR merge.

Existing Core 0.1.1 `CORE011-CSM-002=DEFER`, G1-J `decisionRunsAllowed=false`/`budgetsLocked=false` remain unchanged. **Stop further batching CPU studies** absent a product-calibrated decision gate and an appropriately representative corpus.
