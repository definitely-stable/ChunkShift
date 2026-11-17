# PATCH-TREE-001 / T3-B2D0 — exhaustive encode-stage attribution

Date: 2026-10-25. Owners: #184, #251. Status: PRE-REGISTERED / LAB ONLY / NO_PRODUCTION_ADOPT.

## Question / previous limitations

T3-B2B original changed-renamed DLLs produce 92,671 B warm vs 1,097,577 B self-contained T0S, but original T3/T0S CPU 11.70x/4.19x and wall 12.99x/4.27x fail the fixed 3x cost admission. T3-B2C1 proved existing bounded H3 cache cuts BASE Stream logical read bytes 96.25%/90.66%, but H3/H2 process CPU 1.0087x/0.9276x does NOT improve on both pairs. This evidence does not establish what proportion of T3 CPU is dictionary zstd work.

**B2D0 isolates elapsed durations of compression stages on the unchanged H2-W2 default policy. It is NOT an optimization, nor a causal proof of potential codec speedup.**

## Frozen comparison

Same two source-SHA-locked original ASP.NET Core win-x64 `mscordaccore` renamed DLL transitions 10.0.10→10.0.11 and 10.0.11→10.0.12. Reuse archived original pairs #38136336025, actual B1 #39967460136 and independently selected B2A #39977481690. Original source files and path identities independently rehashed. No Node/tzdata/ChunkShift holdout.

- All modes use EXACT `CspEncoderPolicy.Default`, `CspCreateExecution(2, false)`, and real public CSP v1 SDK create/apply. H2 shipping no-cache is the fixed control.
- `OFF_A`, `OFF_B`: identical uninstrumented `ChooseEntryExhaustiveUntracedAsync` with null `StageProbe`. `ON`: research-only exact copy of exhaustive chooser, opt-in `StageProbe` in internal `CspCreateExecution`; counters are aggregated through Interlocked; no payload buffers or candidate metadata retained by probe. Both OFF controls and ON MUST generate identical complete CSP SHA, physical bytes and exact applied target bytes for every sample.
- One warmup per lane, three measured rotating lane orders: A/ON/B, ON/B/A, B/A/ON for each pair. All twelve samples per pair preserved, none excluded.
- Capture process `TotalProcessorTime`, Stopwatch wall, process-global GC allocated bytes on measured create (excludes manifest construction, physical CSP inspection and apply).
- ON records, per process create, **worker-summed Stopwatch ticks** for: zstd encode WITHOUT dictionary (one/entry), bounded BASE Stream dictionary reads, zstd WITH dictionary (one per valid candidate), chosen dictionary record ChunkId verification. Counts: payload entries, plain encodes, dictionary reads, dictionary encodes, verified records.
- Stopwatch elapsed ticks while worker threads overlap are **not CPU time**. Dictionary-read ticks include await/semaphore wait and buffer reads; compressed trial ticks may include run-time contention and scheduler preemption. Sum of overlapping worker timings may exceed process wall and MUST NOT be equated with the critical path. Other costs (candidate start enumeration, dictionary `IsUsable`, target hashing, CSM, pipeline producer, writer, zstd initialization) are not individually measured.

## Gate / falsification

Fail CLOSED (no result) if source selection, original content SHA/length, CSP SHA/bytes between any lanes, independent F0 section ledger or exact apply fails; if a probe ON lane has no entries, plain-encode count != emitted target-entry count, dictionary-encode count exceeds successful reads, or buffers are outstanding.

Report exact per-pair medians for OFF_A, OFF_B, ON CPU and wall; OFF_B/OFF_A and ON/pooled-OFF CPU ratios. `OFF_A` versus `OFF_B` is an A/A environmental noise control, NOT a proven 10% overhead threshold or an independently isolated causal estimate. Do not tune results post-hoc. Only a **subsequent** separately frozen cross-platform, representative population and overhead-qualified phase can determine whether the remaining CPU improvement opportunity clears the 3x T3/T0S budget; default remains H2 and T0/T0S fallback from B2B.

## Execution

GitHub-hosted Ubuntu 24.04 / Windows 2025 contract tests on PR plus normal CI/CodeQL/Benchmark/Heavy. Original real two-pair experiment ONLY on reviewed first main push, first attempt, original repository and Linux x64 hosted runner. Rehash all original files, compare B1/B2A source identities, preserve only numeric `stages.json`, original source selection/layout, provenance and SHA256SUMS, delete raw CSM/CSP/targets and scratch. Independent source-locked archive audit is a separate evidence PR; PR passing CI or first-run success alone does not imply product verdict. No public API, persisted bytes, trust boundary, format change, sealed holdout, or product publication.
