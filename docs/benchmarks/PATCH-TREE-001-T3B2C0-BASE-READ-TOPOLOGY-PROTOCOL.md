# PATCH-TREE-001 / T3-B2C0 — base stream read topology (diagnostic only)

**Preregistered 2026-10-24.** Owner #184, parent #251. **No production adoption and no hot-path change.** Dependency: [original five-round B2B result](../research/results/PATCH-TREE-001-T3B2B-ORIGINAL-REPEAT-CREATE-EVIDENCE-20261024-001.md), which found BASE-bound create CPU 11.70×/4.19× T0S, and logical source reads 26.6×/10.7× file lengths.

## Falsifiable question and confounders

Are B2B's high logical source reads predominantly *repeat coverage of the same underlying byte ranges*, or mostly distinct records/candidate windows? Quantify exact byte-range union and backward/discontinuous transitions for the same public SDK CSP v1 create. **Do not infer "cache broken" from repeated reads**: `CspPatchBuilder` already uses bounded `BaseCandidateCache` by default and tests multiple dictionary candidate windows. The physical source may be page-cached; Stream reads do not equal disk IO.

This experiment runs at the caller-controlled **BASE content Stream boundary** without changing shipping patching code. A `TracingBaseStream` forwards the same byte content, positions and read results as a `FileStream` and observes:
- number of read calls, bytes returned, assigned positions and Seek calls;
- for traced mode, exact returned byte intervals `[offset,offset+returnedBytes)`, including overlap; union cardinality and repeated coverage `logicalReadBytes − unionBytes`; backward/noncontiguous transitions counted in chronological read order;
- wall time spent inside wrapped Stream read/seek calls, including scheduling and instrumentation overhead; not encoder CPU stage time.
- trace stores **offset pairs only**, not source bytes, user data or full filenames in event logs. Hard maximum 250,000 returned read intervals per CSP; fail closed rather than drop samples or allocate unbounded telemetry.

**No selective source change or alternate CSP execution/policy**: two previously locked mscordaccore ASP.NET Core win-x64 version transitions only, same SHA-256 original B1/B2A selection. There is no similarity score, format change, policy retuning or holdout access.

## Paired original calibration

On a **first main-push GitHub-hosted Ubuntu 24.04 x64** runner, validate the exact independent original `pairs.json` (SHA-256 `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`), source release archive SHA locks, original B1 run #39967460136 and B2A run #39977481690, and frozen B2B result #40078373627. Materialize **calibration-only** .NET trees, rehash every old/new source, independently check the same two old/new candidates before create.

Per pair, build real original and target CSMs once outside measured create. First run one `counter-only` (minimal wrapper counts total Stream reads/position writes) and one `trace` (same wrapper plus interval tracking) warmup. Then **three measured paired AB/BA rounds**, in order:
- round 0 counter-only → trace;
- round 1 trace → counter-only;
- round 2 counter-only → trace.

Each invokes unchanged public `ChunkPatch.CreateAsync(BASE CSM, old bytes, target CSM, new bytes, CSP)`, exact `ChunkPatch.ApplyAsync` and target SHA-256 validation. For the first measured round of each observation mode, F0 independently checks CSP physical bytes/sections, BASE ManifestId and CSP SHA. Require **every measured CSP digest and physical size** to match the original source-locked B1 archived `t3b1-physical.json`. Any change, truncation, mismatched source/CSP, inconsistent trace union or missing sample fails. No raw patch, CSM or staged source retained in success ZIP.

**Controls:** Both modes use the observer wrapper; the control is *not a plain FileStream or no-overhead baseline*. Report full unfiltered per-round process CPU, wall, GC allocated bytes and observer read/seek wall. Observer overhead ratio is descriptive, not subtracted from CPU as if it measured compression stage time. A stricter internal profiler is separately gated only if read-topology evidence identifies a targeted problem.

## Acceptance/interpretation

- `B2C0_ORIGINAL_READ_TOPOLOGY_PASS`: sources and prior original CSP SHA locked, all 16 warmup/measured CSP outputs (2 pairs × 2 modes × [1 warmup+3 measured]) applied and rehashed, complete trace intervals and exact union arithmetic, no undeclared failures.
- A large repeated read fraction supports investigating **candidate-window selection/eviction versus rereads**, *not* the claim that OS disk I/O is equally large.
- If most logical bytes are unique, investigate wide source scanning rather than automatic cache expansion.
- Either result **DOES NOT ADOPT** a production cache, new selector or relaxed 3× CPU/wall gate. A change to default encoder, CSP structure, memory policy, source trust/pins or host publication needs a subsequent B2C1 frozen A/B and broader validation.
- Original evidence report requires independently rechecking the archived ZIP SHA/CSM/CSP provenance and recomputing all trace invariants; archive contains aggregates, **not raw event intervals**, so an external auditor can verify conservation, but the producer's union computation cannot be independently reconstructed from event bytes. Future exact event replay may require a bounded offset-only auxiliary artifact.

PR synthetic Ubuntu/Windows tests include deterministic overlapping byte range union, counter-only unknown-coverage semantics, real SDK create/apply SHA invariance, source tamper, scratch overwrite refusal and unknown CLI. No self-hosted runners. #184/#251 stay OPEN.
