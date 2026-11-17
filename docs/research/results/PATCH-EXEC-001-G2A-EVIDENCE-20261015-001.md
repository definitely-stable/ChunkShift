# PATCH-EXEC-001 / G2-A — accepted real calibration closeout (2026-10-15)

**Result: CALIBRATION_PASS / NO_ADOPT.** [Issue #225](https://github.com/definitely-stable/ChunkShift/issues/225) · [Frozen G2-A protocol](../../benchmarks/PATCH-EXEC-001-G2A-REAL-CALIBRATION.md) · [GitHub-hosted run #38677655592](https://github.com/definitely-stable/ChunkShift/actions/runs/38677655592).

## Provenance, population and acceptance

- Source and checked-out main HEAD: `36ba15fd434a5f5abe365c269839394433b4e44b`. Exactly one first-attempt manual dispatch with successful warm/cold jobs. .NET 10.0.13, Ubuntu 24.04.5 x64, logical processor count 4 per job.
- Full original calibration metadata SHA-256: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`. Frozen 16-changed-file training set SHA-256: `a7f1e607820f6a637600953ddb634b63cfd17e7edc55a67bae86f74f54b0c6e2`. Two .NET calibration families, versions 10.0.10 → 10.0.11; no sealed holdout.
- Both original artifacts have 35 files, pass ZIP CRC and 34 internal SHA256SUMS entries. All 5 rounds × 6 allocations × 16 individual CSP files **matched the same-build, independently applied H0 physical patch SHA-256** in each job (960/960 checks total, plus 32 H0-verified target applications). Policy, sizes, chunk and payload counts, environment and Git commit were independently compared as well.
- Warm source archive: `patch-exec-001-g2a-linux-x64-warm-csm`, ZIP SHA-256 `af04b7cef1438168f785ac27827816a526a80870ceced9c255688434e55c6276`, runner **AMD EPYC 7763**.
- Cold source archive: `patch-exec-001-g2a-linux-x64-cold-csm`, ZIP SHA-256 `7c5a4ab2288901aea028110fd5d8496d08ae77eb68b50754a40d52b23e00cc17`, runner **AMD EPYC 9V45**.
- All **60 raw measured samples**, including wall/CPU/managed allocated bytes/process-lifetime peak working set, are retained in companion [samples.tsv](data/PATCH-EXEC-001-G2A-20261015-001/samples.tsv). Exact all-16 oracle CSP identities are in [patch-identities.tsv](data/PATCH-EXEC-001-G2A-20261015-001/patch-identities.tsv). These committed records survive the GitHub Actions 90-day artifact expiry. Source ZIPs remain linked by immutable SHA-256.

## End-to-end create batch wall seconds, five fresh-process rounds each

| Fixed allocation | Warm (five samples, s) | Warm median (s) | Cold (five samples, s) | Cold median (s) |
| --- | --- | ---: | --- | ---: |
| F1 / H0 | 8.887, 8.818, 8.656, 8.916, 8.861 | 8.861 | 5.762, 5.679, 5.671, 5.687, 5.813 | 5.687 |
| F1 / H2-W2 | 5.622, 5.672, 5.708, 5.614, 5.542 | 5.622 | 3.779, 4.063, 3.865, 3.890, 3.937 | 3.890 |
| F2 / H0 | 6.606, 6.586, 6.698, 6.313, 6.249 | 6.586 | 4.256, 4.154, 4.170, 4.113, 4.208 | 4.170 |
| F2 / H2-W2 | 5.375, 5.687, 5.212, 5.034, 5.187 | 5.212 | 3.138, 3.477, 3.152, 3.138, 3.303 | 3.152 |
| F4 / H0 | 6.124, 5.598, 5.799, 5.584, 6.289 | 5.799 | 3.422, 3.409, 3.333, 3.354, 3.406 | 3.406 |
| **F4 / H2-W2** | **5.426, 4.737, 4.961, 4.945, 5.095** | **4.961** | **2.982, 3.170, 3.130, 3.026, 3.216** | **3.130** |

Reduction in median wall time of best *observed fixed* F4/H2-W2 versus F1/H2-W2: **11.76% (warm)**, **19.53% (cold)**, within each separate runner. These are *not* the adoption comparison against an adaptive policy versus the best fixed safe baseline; the 25% gate of #225 is not demonstrated.

## CPU and process memory accounting

| Allocation | Warm median CPU s | Warm maximum lifetime working set MiB | Cold median CPU s | Cold maximum lifetime working set MiB |
| --- | ---: | ---: | ---: | ---: |
| F1/H0 | 10.015 | 129.8 | 6.556 | 150.7 |
| F1/H2-W2 | 11.826 | 172.4 | 8.030 | 186.9 |
| F2/H0 | 12.371 | 164.4 | 7.865 | 197.8 |
| F2/H2-W2 | 17.158 | 211.2 | 10.410 | 210.7 |
| F4/H0 | 17.055 | 171.4 | 9.860 | 174.0 |
| F4/H2-W2 | 17.343 | 266.9 | 10.764 | 242.4 |

The wall timer includes CSM resolution/manifest digest, planning, create, CSP readback and patch digest. CPU/GC allocation numbers belong to the *whole process*, not a specific simultaneous file. `PeakWorkingSet64` is a lifetime high-water value, **not** isolated per-create incremental RSS or proof of a memory budget. CPU amplification for F4/H2-W2 is material.

**Do not interpret warm/cold differences causally.** The two jobs ran on *different processors*, and `cold-csm` deletes only the manifest cache rather than clearing OS page caches.

## Decision boundary / next allowed work

**G2-A real fixed-lane experimental mechanics: PASS. Production/adaptive executor: NO ADOPT.** No task cache or adaptive scheduling code was measured; per-create isolated peak RSS, cancellation, straggler accounting, CPU quota/affinity and storage read amplification were not established. Do not claim a 25% improvement over the *best fixed safe baseline*. Keep #225 open and freeze #251 unchanged.

Reserved `10.0.11 → 10.0.12` G2-B unsealed validation remains untouched. Do **not** run it as another tuning matrix: before consuming it, a separate G2-B protocol must freeze a nontrivial deterministic adaptive policy learned only from G2-A, a best-safe-fixed comparator, memory/CPU/cancellation guards, stopping criteria and fail-closed provenance. If a policy cannot plausibly outperform the best fixed safe baseline, defer G2-B rather than spend the reserved validation evidence.


### Independent replay and permanent tables

The source artifact ZIP files remain available in [GitHub Actions run #38677655592](https://github.com/definitely-stable/ChunkShift/actions/runs/38677655592) until their retention expires. The historical SHA-256s above are exact byte-locks. Given these **unchanged original ZIPs**, reproduce the 960-patch audit with:

```bash
python3 benchmarks/scripts/verify_patch_exec_001_g2a_closeout.py \
  --warm patch-exec-001-g2a-warm-csm.zip \
  --cold patch-exec-001-g2a-cold-csm.zip \
  --samples docs/research/results/data/PATCH-EXEC-001-G2A-20261015-001/samples.tsv \
  --identities docs/research/results/data/PATCH-EXEC-001-G2A-20261015-001/patch-identities.tsv
```

The permanent TSVs preserve process-wide resource measurements and the H0 patch identities, **not original CSP bytes or the 35-member raw run JSON archives**. Expiration of Actions artifacts prevents further replay from raw archives unless the exact ZIPs were independently preserved. It does not invalidate the pinned permanent tabular evidence.
