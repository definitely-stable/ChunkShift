# CDC 0.1 runtime guardrail execution plan (#8)

Status: **pre-registered execution detail for accepted protocol §8.2**  
Issue: [#8](https://github.com/definitely-stable/ChunkShift/issues/8)  
Governing protocol: [CDC-0.1-BAKEOFF-PROTOCOL.md](CDC-0.1-BAKEOFF-PROTOCOL.md)

This document does **not** amend the accepted bake-off protocol and does not change the already-observed deterministic quality evidence. It fixes the remaining runtime execution details before the runtime result is read.

Runtime is a **veto/guardrail**, not a profile-selection score. The deterministic holdout evidence remains the primary profile-quality evidence.

## 1. Candidate set

Only the three already-registered current-semantics size points participate:

| nominal target | minimum | maximum | candidate ProfileId |
|---:|---:|---:|---|
| 64 KiB | 16 KiB | 256 KiB | `fastcdc.gear.candidate.v1.m16384.t65536.x262144` |
| 128 KiB | 32 KiB | 512 KiB | `fastcdc.gear.candidate.v1.m32768.t131072.x524288` |
| 256 KiB | 64 KiB | 1 MiB | `fastcdc.gear.candidate.v1.m65536.t262144.x1048576` |

All use:

- algorithm `fastcdc.gear.chunkshift.v1`;
- current cold-at-`Minimum` semantics;
- `chunkshift.blake3-256.v1`;
- the production streaming lane, `ChunkingKernel.ScanAsync`.

Not included: warmed-prefix, Google/Stadia, fixed-size, coarse 512 KiB–2 MiB, SHA-256, or any new CDC algorithm.

## 2. Workloads

The runtime guardrail uses two dedicated 64 MiB synthetic entries built from the **same existing deterministic generators and seeds** as the earlier 8 MiB smoke workloads:

- `runtime-game-pak-64m`: `game-pak-like`, seed 1001;
- `runtime-db-vm-64m`: `db-vm-data-like`, seed 1004.

Only the input length is increased. This is deliberate: at 8 MiB, a streaming sample can complete in only a few tens of milliseconds, making `Process.TotalProcessorTime` comparatively coarse. A 64 MiB source means each identity measurement scans 128 MiB (source + target), reducing timing/CPU quantization without introducing a new corpus model after the quality result was observed.

Runtime does not reuse the private/large real-version corpus because §8.2 measures execution cost, not reuse quality. The exact runtime corpus is frozen in `benchmarks/corpus/cdc-0.1-runtime.v1.json`.

The exact experiment set is frozen in `benchmarks/experiments/cdc-0.1-runtime.v1.json`: 3 targets × 2 workloads = 6 experiments, identity input only.

## 3. Isolation and statistical unit

The existing benchmark runner is normative for this execution:

```text
lab --isolate
  -> one child process per experiment
  -> stabilization
  -> 3 warm-up iterations
  -> 5 measured streaming iterations
```

The statistical unit for §8.2 is **one isolated child-process result**.

The five measurements inside one child process are not treated as five independent process samples. The runner's process-level streaming median is one observation. Across ten batches, each experiment therefore has ten independent process-level observations.

All five internal measurements are retained in the raw JSON for audit/debugging.

## 4. Pre-registered order schedule

A ten-run design cannot place three candidates in three positions exactly equally because 10 is not divisible by 3. The following schedule is near-balanced and contains every one of the six permutations at least once:

| run | candidate-group order |
|---:|---|
| 01 | 64 → 128 → 256 KiB |
| 02 | 256 → 128 → 64 KiB |
| 03 | 64 → 256 → 128 KiB |
| 04 | 128 → 256 → 64 KiB |
| 05 | 128 → 64 → 256 KiB |
| 06 | 256 → 64 → 128 KiB |
| 07 | 64 → 256 → 128 KiB |
| 08 | 256 → 128 → 64 KiB |
| 09 | 128 → 64 → 256 KiB |
| 10 | 256 → 64 → 128 KiB |

Position counts:

| target | first | middle | last |
|---:|---:|---:|---:|
| 64 KiB | 3 | 4 | 3 |
| 128 KiB | 3 | 3 | 4 |
| 256 KiB | 4 | 3 | 3 |

Within each candidate group, `runtime-game-pak-64m` runs before `runtime-db-vm-64m`. Since `lab --isolate` starts a fresh child for every experiment, candidate-group ordering is only controlling host-time/order drift, not sharing JIT/runtime state between candidates.

The schedule is hard-coded and hashed by `run_cdc_runtime_guardrail.py`; CI records its SHA-256.

## 5. Architectures

The identical schedule runs independently on:

- GitHub `ubuntu-24.04` x64;
- GitHub `ubuntu-24.04-arm` arm64.

Performance samples are never pooled across architectures.

A cross-architecture verifier requires identical:

- tested commit;
- experiment manifest digest;
- order-schedule digest;
- definition fingerprints;
- source/target byte digests;
- streaming chunk-sequence digests.

A mismatch is a correctness/provenance failure, not benchmark noise.

## 6. Runtime metrics

For every workload/target, across ten isolated process results:

- streaming GiB/s;
- CPU seconds/GiB;
- allocated bytes/GiB;
- bytes copied/input byte;
- process peak RSS.

Peak RSS is the maximum process-lifetime peak observed by the reference or streaming samples in that isolated child. It includes process/runtime/corpus/stabilization overhead and is therefore a coarse guardrail, not a per-buffer memory measurement.

The cross-process report uses:

- median;
- IQR with linear percentile interpolation;
- median absolute deviation (MAD);
- min/max;
- all ten raw process-level values.

No p95 is used as a primary statistic with only ten process-level observations.

Paired numerator/denominator ratios are also computed within the same batch and workload for 64/128, 64/256 and 128/256 KiB. Pairing reduces runner-to-run host variance.

## 7. Decision discipline

There is no new global runtime percentage threshold.

The report must not select the fastest candidate. It asks only whether the deterministic quality leader has a large, reproducible runtime or memory cost that justifies stopping the freeze for explicit review.

Interpretation:

1. if differences are comparable to cross-process dispersion or inconsistent across runs/architectures, runtime does not veto;
2. if a cost is reproducible, directionally consistent and clearly outside dispersion, record it as a real trade-off;
3. a runtime cost does **not** automatically promote 128 or 256 KiB;
4. a possible veto stops #8 and requires an explicit cost justification under protocol §9.2;
5. without a veto, selection continues from the already-frozen deterministic holdout evidence.

No rule in this document may be edited after the first runtime result is inspected without discarding that runtime run and pre-registering a replacement execution.

## 8. Evidence outputs

For each architecture CI retains:

```text
execution.json
manifests/run-01.json ... run-10.json
raw/run-01.json ... run-10.json
logs/run-01.log ... run-10.log
runtime-summary.json
runtime-summary.md
```

A final cross-architecture artifact records deterministic/provenance agreement.

The raw results, not only the markdown summary, are the evidence required to close the runtime checkbox in #8.

## 9. Scope boundary

This work does not:

- choose or rename the stable ProfileId;
- alter `FastCdcProfile`, masks, Gear table, boundaries, ProfileFingerprint or CSM;
- regenerate golden vectors;
- modify the already-recorded real-corpus bake-off evidence;
- start #14 experimental CDC work.

Only after this runtime guardrail is reviewed can the separate #8 freeze change candidate registration into the final Core 0.1.0 stable profile.
