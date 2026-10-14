# CORE011-CSM-004 — E3-R2-B2B external observer calibration

**Experiment:** CORE011-CSM-004, issue #154, parent #152. Stacked on E3-R2-B2A evidence PR #350, no production changes. Reader pinned to original SHA256 a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2; only eager baseline; decisionRunsAllowed=false, budgetsLocked=false, NO_ADOPT.

## Hypothesis

E3-R2-B2A measured suspiciously large Linux no-reader resident high-water under 100-ms in-process sampling. Is the sample collection itself materially perturbing the measured process? This study separates observation from the target by launching a second independent OS process to query private resident memory. The target runs with its internal sampler OFF or ON, while the same external process-based 100-ms observation is applied to both.

Linux external counter: Private_Clean + Private_Dirty from /proc/<target pid>/smaps_rollup; Windows external counter: PSAPI PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize through a remote Process.Handle. There is no virtual/private-commit substitution. Linux smaps_rollup / Windows PSAPI calls can still perturb the target and host through kernel queries and CPU; external observation is not free.

## Fixed matrix, source and orchestration

Two separately scheduled GitHub-hosted Ubuntu 24.04 x64 and two Windows Server 2025 x64 jobs. Each job builds source-derived genuine 384MiB BLAKE3/BIDX CSM via the existing G1-J1 external source-only builder: 182366-byte manifest, 5052 chunks, 2 CBLKs, physical SHA256 342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a. Original source deleted before any consumer process. One independent builder, four fresh target consumers, four fresh observer processes per hosted job (nine distinct PID identities per job).

Each target performs the same three public VerifyManifestAsync warmups and one partial ManifestReader warmup. A ready marker allows external observer to capture target residency BEFORE signalling go. Target then opens 0 or 16 ManifestReader objects, consumes exactly one logical entry from each, retains strong references over 30 seconds natural idle (no forced GC), disposes and marks done. Observer captures native target PID resident pages at 100ms and writes full original trace with exact sampled maximum. When internal ON, target additionally executes B2A 100ms native sampling, and captures 16 explicit first-read checkpoints. When OFF, it creates no native memory samples/list entries (internalSampleCount=0) until a single terminal managed-heap/GC diagnostic.

Predeclared case order per replica:
- Replica 0: OFF-0, ON-0, ON-16, OFF-16.
- Replica 1: ON-0, OFF-0, OFF-16, ON-16.

This counterbalances simple time/host drift, but does not establish within-process randomized causality. Four sequential target/observer pairs share runner resource context, and sampled maximum is NOT a hard instantaneous high-water bound. No SHA256-suite corpus, representative real-versioned CSM, full Patch.Plan, AOT, lazy/hybrid candidate, 300s Windows trimming or prospective numeric product budget is tested. Those must follow only after methodology is qualified.

## Exit and safety

GitHub-hosted jobs build actual unchanged Core .NET10 and run Python negative tests plus original R0 fail-closed preregistration guard. Scope checker refuses any src/tests/package edits. Python aggregator checks original physical CSM SHA and builder/source PID provenance, target/observer distinct PIDs, mode OFF truly no native target sampling, mode ON adequate 100-ms samples and 16 opened checkpoints, 30s natural elapsed time, exact external max computed from all native trace values, counter self-consistency and matching OS source. Archive complete JSON/CSM/process logs including failures.

**Disposition remains BASELINE_CALIBRATION_ONLY, PRODUCT_NOT_QUALIFIED / NO_ADOPT** even on CI success. Next stage must assess external observation cost/no-observer controls and a real-versioned unsealed baseline before any separate prospective numeric freeze, and only then R3 holdout.
