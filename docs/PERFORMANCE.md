# Performance evidence policy

Status: Active policy; numeric thresholds pending M0 calibration  
Last reviewed: 2026-09-28

ChunkShift treats performance as an architecture constraint, but benchmark noise must not turn normal development into false-red CI.

## What is a regression

Always investigate or fail:

- changed deterministic output for an optimization-only change;
- a new mandatory allocation/object per chunk on a declared zero/low-allocation hot path;
- newly unbounded buffering or read-ahead;
- materially increased bytes copied per GiB;
- a reproducible throughput/CPU/RSS/latency regression outside the calibrated noise band;
- increased patch/request/read/write amplification that harms the product workload.

Small benchmark movement inside the measured noise distribution is reported, not automatically treated as a defect.

## Thresholds

M0/#3 owns measurement methodology and baseline noise calibration.

Do not invent permanent percentage thresholds before M0 has:

- repeatable hardware/runtime lanes;
- representative corpus and mutation traces;
- enough repeated samples to establish variance;
- separated JIT/NativeAOT and x64/ARM64 evidence.

After calibration, thresholds may differ by metric. A single global “5% rule” is not required.

## Evidence

Performance-sensitive PRs should record:

- baseline and candidate commit;
- workload/corpus/profile/HashSuite;
- runtime/OS/architecture/hardware;
- repeated measurements and variance;
- relevant allocation/copy/RSS/amplification metrics;
- whether output/golden vectors are identical.

Prefer the repository benchmark harness over ad-hoc stopwatch measurements.

CI benchmark output should be retained as workflow artifacts or summarized evidence. Do not commit every raw benchmark run into Git history.

## Correctness outranks speed

An optimized backend must produce the same persisted semantics as its reference implementation.

If an optimization requires updating a golden vector, ProfileId, HashSuite identity or persisted-format fixture, treat it as a compatibility change and route it through the corresponding RFC/issue. Do not classify it as a pure performance change.


## Research / experiment logging

Performance and research work that can influence an architectural or release decision uses the registry in [docs/research/README.md](research/README.md).

The minimum linkage is:

```text
Issue
  <-> ExperimentId
  <-> RunId(s)
  <-> EvidenceId
  <-> implementation PR
```

### Before final measurement

- claim a stable `ExperimentId` in [docs/research/EXPERIMENT-INDEX.md](research/EXPERIMENT-INDEX.md);
- write the hypothesis and decision rule before collecting the final decision dataset;
- make the experiment definition/fingerprint reproducible.

### Every retained run

Record a `RunId`, exact commit, runtime/toolchain, platform, workload/corpus identity, launch protocol and raw-result location.

Do not compare runs that differ in experiment definition/commit/corpus semantics without making that difference explicit.

### Decision-bearing evidence

A result that changes code, architecture, dependencies or release direction must get a durable Markdown result under `docs/research/results/` using the standard template.

The summary must link the raw run IDs and contain the final `ADOPT / DEFER / REJECT` decision. An expiring CI artifact alone is not a durable decision record.

When reasonably small, preserve the actual per-sample decision dataset in machine-readable form. For large raw data, preserve a manifest with digest/provenance/reproduction details and a durable artifact/location reference where available.

Rejected candidates remain indexed so the project does not repeatedly rediscover the same losing experiment without new evidence.
