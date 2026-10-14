# CORE011-CSM-004 / E3-R2-B2A — replicated sampled private-resident high-water

**ExperimentId:** CORE011-CSM-004, owner #154, parent #152. Stack: R0 #343, R1 #344, R2-A #345/#346, R2-B1 #347/#348.
**Experiment decision:** CALIBRATION ONLY; reader variant EAGER only; R0 and release DEFER unchanged. **No R3**, no numeric threshold, no candidate comparison.

## Why this next slice?

B1 established on one Linux and one Windows runner that 16 long-lived partly consumed readers stay backed by ~2.6MiB additional private resident memory at 30s on large CSM, but Windows private working-set can drop after 300s while the managed arrays remain live. B1 snapshots did NOT capture the peak between reader opens and the first held sample. They were also single-host-per-OS. The next falsifiable question is whether independent host repetitions reproduce *sampled peak* private-resident accounting, with fully touched eager buffers and host-specific JIT/CPU provenance.

## Frozen R2-B2A matrix

Exactly **two separately allocated GitHub-hosted runner jobs per OS**, Linux ubuntu-24.04 and Windows windows-2025 (4 independent jobs). Each job:
- Same source SHA256-locked eager reader, 384 MiB synthetic source transformed by an independent external G1-J1 fixture builder into a **182366-byte physical CSM with exactly 5052 chunks / 2 CBLKs**, blake3/BIDX. Builder deletes source content before reader consumer, and exits. Consumers use CSM bytes only, no source.
- **Two fresh separate consumer OS processes**: 0-reader control, followed by 16 actually live partially read public ManifestReaders. Each holds the *same immutable CSM* by MemoryStream wrapper, has read exactly one validated logical entry, and remains live during 30 seconds of natural idle. No premature disposal, no forced GC.
- Existing B1 Linux smaps_rollup Private_Clean+Private_Dirty or Windows PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize. In addition to 100-ms periodic sampling, synchronously capture a checkpoint after **every** reader-open/first ReadAsync(1) step (the 16 creation checkpoints cannot all be guaranteed by periodic polling). Serialize concurrent sampler access under a lock, preserving real order/time. Collect ≥240 samples, full 30-second natural idle and 2-second natural post-disposal trace. Record **sampled high-water**, sampled RSS peak, underlying trace as authoritative JSON, CLR GC pause/Gen2, managed heap, OS, runtime, native CPU model, processor count, and DOTNET_TieredCompilation / TieredPGO / ReadyToRun environment values.
- The reported maximum is **max of recorded samples only**, not a certified instantaneous hardware peak: any page residency spike between samples may be missed and native sampling itself perturbs memory. It cannot support a hard no-regression peak-RSS upper bound. Capture 16 explicitly per-open checkpoints to bound one major undersampling mode. On Windows the private committed size is additional context rather than substitute for resident pages.

Two OS replicas are independent host processes, but within-host 0-reader and 16-reader cases run in **different** processes and are therefore not paired causal estimates. RSS is subject to OS scheduling, eviction, JIT, GC and runtime noise. The previous 300-second B1 control vs 16-reader plateaus remain valid historical evidence; B2A **deliberately** tests 30-second high-water to avoid re-executing two 300s idles on each of four runners. This R2B2A synthetic calibration does NOT examine SHA256 suite, user DLL versions, full Patch.Plan, Windows AOT/ARM64 or active stress 64. Those remain to be done before any prospective numeric budget freeze.

## Acceptance and provenance

- R2-B2A machine contract, source SHA and exact immutable checkout ancestry. No changes to production src/tests/API/CSM/CSP/default; only benchmark scripts and research protocol files.
- All four hosted jobs compile unchanged production Core. Separate external fixture builder PID; two unique consumer PIDs per host, physically identical original CSM SHA across all four. Source file absent; fail-closed on any native OS metric error.
- Strict evaluator recomputes high-water from **every original trace point**, checks 16/16 opening-checkpoint labels, time monotonicity, 30s natural idle and actual 100ms sampling minimum, and rejects mismatched platform/process/source/metrics.
- Original ZIP/trace, original builder fixture bytes, raw source hashes and immutable RunIds archived, including failure logs. Evidence-only PR follows with an independently verified digest and compact aggregate, without changing the frozen protocol or retroactively choosing a product threshold.

**No product verdict.** Further R2-B2 work needs representative unsealed real-versioned calibration, Plan and runtime/AOT qualification, as well as a separately reviewed choice of business performance budget and independent R3 holdout. Previous issue #154 closeout remains DEFER.
