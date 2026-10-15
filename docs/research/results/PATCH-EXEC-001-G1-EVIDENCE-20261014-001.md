# PATCH-EXEC-001 / G1: synthetic hosted calibration evidence (2026-10-14)

**Verdict: CALIBRATION_MECHANICS_PASS; performance/adoption decision: NOT MADE.**
Research issue [#225](https://github.com/definitely-stable/ChunkShift/issues/225); protocol [PATCH-EXEC-001-G1-PILOT.md](../../benchmarks/PATCH-EXEC-001-G1-PILOT.md); PR [#354](https://github.com/definitely-stable/ChunkShift/pull/354).
Run [#38617205068](https://github.com/definitely-stable/ChunkShift/actions/runs/38617205068) (Linux x64 and Windows x64, both SUCCESS), source PR head `24c3294bf5ecaa0bef1513fe4080194294aaf5df`, workflow checked-out PR merge commit recorded inside raw evidence `2cd5d9ba5de332f8363bf7808cd30022ddf47155`. **The two SHAs have distinct roles**, not an unexplained source mismatch.

## Source provenance and integrity

- No downloaded sources: SHAKE-256 deterministic synthetic generator from this PR; 12 changed files of 393216 bytes (4.5 MiB of target content); three source data strata. Full generated `pairs.json` SHA-256: `19436206f65379757eb47df22364da57d807f7fb7c1c4e30b498726eea4b1b88` **on both hosts**.
- One H0/F1 oracle per host applies 12 patches and verifies full target hashes. Each of 6 configurations × 2 reversed rounds = 12 batch results per platform, each containing 12 SHA-identical CSPs versus the apply-verified oracle (144 file-level comparisons per host). The comparator also checks file identity, sizes, entries, policy, corpus digest, host and checked-out source commit.
- Linux artifact `patch-exec-001-g1-linux-x64`: [GitHub Actions artifact](https://github.com/definitely-stable/ChunkShift/actions/runs/38617205068), ZIP SHA-256 `11b0cd003f291aefa7be8ec0828e52419dc2482e574de6789210ae97517845bc`.
- Windows artifact `patch-exec-001-g1-windows-x64`: same run, ZIP SHA-256 `48d8c161b3ee6c738f1c12ede900166e7acf77e12db1e31f578277b534d72a8d`.
- Both original ZIPs independently opened, CRC-checked, and contained exactly 14 expected JSON documents (H0 oracle + 12 configuration results + summary). All `summary.json` schemas, cohort sizes and source/lock fields checked. GitHub artifacts expire after 14 days; this is a provenance index, **not** permanently archived raw evidence.

## Timing (two independent fresh-process samples per configuration)

Units: seconds for complete batch creation from manifest/digest/planning through CSP readback and SHA-256; median over **only two** measurements. Different platform numbers must not be compared as same-host ratios.

| Outer/inner | Linux x64 median (min–max) | Windows x64 median (min–max) |
| --- | ---: | ---: |
| F1 / H0 | 1.629 (1.587–1.671) | 2.157 (1.982–2.333) |
| F1 / H2-W2 (current inner default) | 1.259 (1.014–1.503) | 1.635 (1.537–1.734) |
| F2 / H0 | 1.124 (0.996–1.251) | 1.494 (1.333–1.655) |
| **F2 / H2-W2** | **0.970 (0.963–0.978)** | 1.480 (1.384–1.577) |
| F4 / H0 | 1.332 (0.907–1.756) | **1.330 (1.153–1.507)** |
| F4 / H2-W2 | 1.048 (0.977–1.118) | 1.367 (1.340–1.395) |

Observed best compared with F1/H2-W2: Linux F2/H2-W2 **22.9%** lower median wall; Windows F4/H0 **18.7%** lower median wall. Neither clears the nominal 25% reduction target **against this comparator**, but G1 is not even an eligible decision experiment: F1/H2-W2 is *not necessarily* the best fixed safe baseline, the population is synthetic, only two rounds were run, per-operation bounded memory is not established, and other confounders remain. These ratios are not an adoption verdict. F4/H0 has especially large Linux variability (0.907–1.756 s).

## Resource caveats and next gate

Linux process CPU medians: F1/H2-W2 2.295 s vs F2/H2-W2 3.167 s; maximum process-lifetime high-water 151.9 vs 176.4 MiB respectively. Windows: F1/H2-W2 CPU 2.977 s vs F4/H0 4.305 s, high-water 79.3 vs 100.9 MiB. The figures are process-wide: high-water is **not idle-subtracted, isolated per-create extra RSS**, and timings include the end-to-end batch pipeline. No CPU quota/affinity, cold vs warm CSM investigation, I/O amplification, disk cache isolation or real update-set validation.

**Next:** freeze an independent G2 unsealed real corpus protocol, explicit population rules and SHA locks, 5+ repetitions/alternating order, cold/warm modalities, CPU/memory budgets, and judge only against the best fixed safe baseline. Do not change SDK/production defaults, task-cache semantics, public surface or #251 gate based on G1.
