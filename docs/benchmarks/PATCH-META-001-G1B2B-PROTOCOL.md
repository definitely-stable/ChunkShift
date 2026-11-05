# PATCH-META-001 G1B2B — consumed dictionary read vs target-dependent codec profiling

**2026-11-05**. Refs #220 #251. **Stacked** after draft PR #372 (G1B2A), #370 (G1B1), #368 (G1A), #367 (G0). ExperimentId `PATCH-META-001`. **Pre-registered calibration mechanics only — not a product or feature-cache adoption result.**

## Causal question and cost classification

Previous original Roslyn 4.13.0 → 4.14.0 #372 measured **0.573 ms Linux / 0.843 ms Windows** aggregate worker-sum candidate-start enumeration against **9–12 s total** builder wall. That narrow feature cannot reasonably produce the issue's 30% repeated-create speedup. The remaining opaque builder wall *cannot* be declared cacheable.

This slice instruments ONLY the exact existing `ChooseEntryExhaustiveUntracedAsync` producer path under optional internal `CspMetaResearchProbe` (null in public/default API). Measure: (1) no-dictionary `zstd` encode calls, accumulated worker elapsed ticks and target input bytes; (2) dictionary `zstd` encode calls, worker ticks and target/dictionary input bytes; (3) actual base dictionary candidate reads via `BaseChunkSource.ReadAsync`, calls, bytes and wall intervals. Existing G1B2A base/target manifest reads and candidate-start measurements remain. No alternate chooser, trial count, encoder policy, dictionaries, normal CSP default or persisted format change.

**Classification before results:** no-dictionary codec output depends on current target content; dictionary-encoded frames depend on **both** target and dictionary; these are not safely reusable base occurrence metadata across arbitrary target revisions. Dictionary physical reads and authenticity verification cannot simply be removed by an untrusted sidecar. A later task-result cache for exact, authorized full encoding inputs is independently owned by PATCH-EXEC-001 #225, not permission to disguise codec CPU as PatchFeatures reuse. All three counters are descriptive consumed work, **not** a candidate META speedup budget.

## Controls and original input

Use the identical original SHA-pinned Microsoft.CodeAnalysis.CSharp 4.13.0→4.14.0 official release pair as G1B2A. Both Linux/Windows GitHub-hosted, five strictly alternating paired probe-off/on rounds = 10 independent CSP creates and 10 SHA-checked sequential applies each. Reuse already frozen G1B1 download/provenance checks. First cold-manifest round excluded; four warm observations per mode. Every sample verifies exact CSP SHA, manifest IDs and original target SHA; on/off output identity required by **same frozen build** (D7), not across version/builds.

Reject missing/NaN/negative durations, absent fields, unexpected call/read counts, edited source identities, failed apply, changed patch SHA or incomplete paired rounds. Probe OFF always emits explicit null new metrics. Publish original raw stage JSON, independently inspectable summary and Linux/Windows SHA-bound GitHub Actions archive. No full source binaries or physical patch payload bytes archived. All comparisons are observational; worker sums overlap in H2-W2 and off/on differences mix JIT, scheduling, page caches and instrumentation overhead.

## Release and next step

This is **one source family / calibration only**, not independent confirmation. If zstd dominates worker-sum time, that shows expensive **target-dependent encoding**, not a precomputable META sidecar; if dictionary reads dominate, a bounded reuse design would require its own data-integrity and cache-hit cost experiment. After this, decide whether a real safely reusable derived feature remains worth A/B testing on independent source-locked product families. If no plausible >=30% net repeated-create benefit exists, record **first-release DEFER** for #220 instead of introducing new public/persisted cache semantics.

No change to CSP/Core identifiers, API, zstd backend, publication repository or #251 gate; **not** a decision-grade ADOPT/REJECT of all possible metadata reuse.
