# PATCH-EXEC-001 — first-release local execution / task-cache DEFER

**EvidenceId:** `PATCH-EXEC-001/FIRST-RELEASE-DEFER-20261117-001`. **ExperimentId:** `PATCH-EXEC-001`. Date: **2026-11-17**. Owner [#225](https://github.com/definitely-stable/ChunkShift/issues/225). Release gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

**First-release disposition: `DEFER / NO_PRODUCTION_ADOPT`** for a new adaptive local scheduler, pure task-result cache, public executor abstraction or altered CSP construction/default. Retain the existing **per-file CSP v1** encoder and already admitted [PATCH-ENC-004](PATCH-ENC-004-EVIDENCE-20261001-001.md) bounded H2-W2 ordered worker pipeline. Do not confuse this scoped decision with a universal rejection of multi-file parallelism or future scheduler/cache improvements.

## Source-bound evidence and original-data revalidation

1. Synthetic G1 baseline: [PATCH-EXEC-001 G1](PATCH-EXEC-001-G1-EVIDENCE-20261014-001.md) on GitHub-hosted Linux/Windows, deterministic CSP checks; **CALIBRATION_MECHANICS_PASS / NO_PRODUCT_VERDICT**. Only two samples per configuration; not a multi-file adaptive adoption trial.
2. Original G2-A fixed-lane real calibration: [durable G2-A report](PATCH-EXEC-001-G2A-EVIDENCE-20261015-001.md), [60 process-wide samples](data/PATCH-EXEC-001-G2A-20261015-001/samples.tsv), [16 original H0 physical CSP identities](data/PATCH-EXEC-001-G2A-20261015-001/patch-identities.tsv), [original GitHub-hosted run #38677655592](https://github.com/definitely-stable/ChunkShift/actions/runs/38677655592), source `main@36ba15fd434a5f5abe365c269839394433b4e44b`, first-attempt **SUCCESS**.
3. Both original G2-A ZIP archives were downloaded anew and independently rechecked on **2026-11-17**, not reconstructed from the TSVs. Original artifact IDs **11947765369** (warm) and **11947710230** (cold); exact ZIP SHA-256: **`af04b7cef1438168f785ac27827816a526a80870ceced9c255688434e55c6276`** and **`7c5a4ab2288901aea028110fd5d8496d08ae77eb68b50754a40d52b23e00cc17`**. Each archive's ZIP CRC, population of **35 members**, and **34 internal SHA256SUMS** passed. The selection references frozen full pairs digest **`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`** and selected training pairs digest **`a7f1e607820f6a637600953ddb634b63cfd17e7edc55a67bae86f74f54b0c6e2`**.
4. Independent Python re-audit of the **original archives** confirmed the 16 H0 apply-verified physical reference files per host, all **960/960** physical CSP SHA and per-file structural equalities across 5 × 6 × 16 × 2 fixed-lane rounds, and recalculated all 60 process-wide elapsed/CPU/allocation/peak observations and summary medians. This is a **repeat audit of historical calibration**, not a new bench RunId, a new source family or a sealed holdout.
5. A source-bound archive verifier and its permanent-evidence tests from [G2-A evidence PR #360](https://github.com/definitely-stable/ChunkShift/pull/360) are separate from this disposition. The original ZIP archives are not committed; CI retained TSVs do not independently reproduce the discarded patch bytes, and verification cannot be rerun from archives after their retention expires unless the identical original ZIPs are preserved.

## Measured fixed-lane tradeoff — do not infer an adaptive win

Both G2-A modalities used Ubuntu 24.04 x64, .NET 10.0.13, four reported logical processors, 16 original changed files, five fresh processes per fixed allocation. **The warm job ran on AMD EPYC 7763; the cold job ran on AMD EPYC 9V45**. Cold-CSM removed only the manifest cache, *not* OS page cache. Consequently, numbers must be compared **within** a host/modality only and are not a causal warm/cold cache experiment.

| Fixed allocation | Warm wall p50 (s) | Cold wall p50 (s) | Warm process CPU p50 (s) | Cold CPU p50 (s) |
| --- | ---: | ---: | ---: | ---: |
| F1/H2-W2 (existing per-file default) | 5.6215218 | 3.8901538 | 11.826491 | 8.030107 |
| F2/H2-W2 (research outer files) | 5.2118486 | 3.1519184 | 17.157909 | 10.410363 |
| F4/H2-W2 (best *observed* fixed lane) | **4.9605327** | **3.1303854** | **17.343097** | **10.763565** |

Against F1/H2-W2, F4/H2-W2 reduced median wall by **11.7582% warm** / **19.5305% cold**, but increased process CPU median by **46.6462% warm** / **34.0401% cold**. Process-lifetime peak Working Set rose **172.359→266.867 MiB warm** and **186.855→242.375 MiB cold**; this is *not* isolated per-create RSS or a safe memory-limit qualification. Against the already faster F2/H2-W2, F4/H2-W2 added only **4.8220% warm / 0.6832% cold** median wall improvement.

These figures are exact medians/maxima from the original 60 process-wide measurements, not statistically established gains across independent workloads or hardware classes. A greater outer worker count may oversubscribe the existing H2-W2 inner workers and increase peak memory; no first-release resource-safe adaptive selection rule can be derived from these two runners.

## Entry/adoption gate and disposition

Issue #225 requires a **measured** ≥25% wall reduction versus the **best fixed safe** baseline on expensive-build workloads (or comparably strong validated repeated-build cache improvement), plus frozen CPU, peak memory and cancellation/failure constraints, deterministic output, and validated cache keys/bytes. These requirements are **NOT DEMONSTRATED** for this release:

- **No adaptive scheduler policy was frozen or evaluated.** G2-A compared only six fixed allocations, and its best observed choice is not a certified *safe* fixed baseline.
- The two G2-A job environments were different EPYC models; 16 training-only .NET files cannot establish general hardware-class scalability.
- No per-create incremental private RSS bound, cancellation/retry proof, straggler telemetry, outer+inner CPU budget, cache-key trust/decoder proof, cross-family confirmation or fresh G2-B validation was produced. Increases in process-wide CPU and resident high-water are substantial.
- **No pure task-result cache** was built or measured under #225. [#220](PATCH-META-001-G1B2B-FIRST-RELEASE-DEFER-20261105-001.md) separately deferred disposable derived-feature reuse; its target-dependent codec work is not automatically reusable cached task output.
- The frozen reserved **10.0.11→10.0.12** unsealed G2-B validation population remains unopened. Do not use it for exploratory retuning without a separately frozen nontrivial adaptive algorithm, best-safe-fixed comparator and resource/cancellation gates. No sealed holdout was opened.

**Decision: DEFER phase-B adaptive execution and task-result cache for the first release without running a speculative G2-B**. Neither a negative trained-policy experiment nor a proven inability to improve is asserted. Keep H2-W2 and the existing deterministic ordered writer; no CSP v1 format, semantic choice, Core profile, public API or production default change. No distributed worker API or remote execution in #225.

## Future reopening and research-freeze handoff

An independently scoped future execution study must predeclare both a demonstrably promising deterministic workload-sensitive policy and a memory/CPU admission envelope, then compare against the best measured **safe fixed** file-level allocation with at least two supported hardware classes, paired warm/cold controls on equivalent machines, an unsealed validation partition, semantic CSP/Apply equality, cancellation/retry and validated task-result cache on/off lanes. Only after that evidence can G2-B be spent; it cannot retroactively change the original G2-A conclusion.

This is a **first-release research disposition**, not authorization to close [#251](https://github.com/definitely-stable/ChunkShift/issues/251) automatically: #251 still needs a separately reviewed **final CSP v1 vs vNext synthesis**, an inventory of persisted-semantic ADOPTs (if any), and explicit CLI/PublicAPI/publication sequencing. Existing Patching package remains unpublished until that program gate is accepted.
