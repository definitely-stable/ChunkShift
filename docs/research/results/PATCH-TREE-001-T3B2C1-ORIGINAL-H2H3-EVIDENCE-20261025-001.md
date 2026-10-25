# PATCH-TREE-001 / T3-B2C1 — original H2 versus H3 base-read attribution

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261025-T3B2C1-001`  
**Owner:** #184; research-freeze #251.  
**Date:** 2026-10-25.  
**Disposition:** **H3_BOUNDED_CACHE_READS_LESS / NO_CPU_WIN / NO_PRODUCTION_ADOPT**.

## Original run / independent physical archive checks

- Implementation [PR #409](https://github.com/definitely-stable/ChunkShift/pull/409) squash-MERGED to `main@df61c8534735e1e8576d3702ab8774248dd41250`.
- [GitHub-hosted run #40191549066](https://github.com/definitely-stable/ChunkShift/actions/runs/40191549066): **SUCCESS**, first attempt, Linux/x64, original push to main.
- Original artifact **#12597788284**, `patch-tree-t3b2c1-original-ranges-NO-ADOPT`; ZIP length **301,789 B**, SHA-256 `554944ebbec4e69d6b26f88bca6fcf6bf1faa0dc6d999bf0be2f03b45a280efc`. Original ZIP CRC and member SHA256SUMS independently checked.
- Preserved source locks: `original-pairs.json` SHA `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`; B1 selection `2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad`; B2A selection `1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b`; `layouts.json` `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a`.
- Measured `ranges.json` SHA `2d5fc41b882b83df940e88a6f28a4c45d5019f4d494eef7228af7026e7554a5e`; `provenance.json` SHA `886d2e7aa00292b02ee7b0c006e5b7e1cd2f472834bee5cd64a689e82c873d81`.
- Independent stdlib-only exact archive/provenance/median/range/CPU/physical-CSP replay: `docs/research/results/data/PATCH-TREE-001-T3B2C1-EVIDENCE-20261025-001/recompute.py`. A complete original ZIP is required for provenance replay; the committed `samples.tsv` preserves all 16 observed samples after artifact expiration. The replay validates the archived results, **not** raw CSP decoding or a physical disk I/O trace.

## Fixed paired experiment

Two original source-locked ASP.NET Core win-x64 `mscordaccore` DLL rename transitions, 10.0.10→10.0.11 and 10.0.11→10.0.12. Each BASE DLL is 1,356,632 bytes. Same frozen `CspEncoderPolicy.Default`, 2 workers. H2: shipping no-cache. H3: pre-existing internal bounded `BaseCandidateCache`. One warmup per mode then three alternating paired measured creates, each exact SDK apply + target digest; 8 physical CSPs per pair, **16/16 exact apply and original B1 CSP SHA identity**. No sealed holdout.

| Metric | 10.0.10→10.0.11 H2 | H3 | 10.0.11→10.0.12 H2 | H3 |
| --- | ---: | ---: | ---: | ---: |
| BASE Stream logical bytes/create | 36,133,305 | **1,356,632** | 14,522,936 | **1,356,632** |
| Unique exact BASE byte coverage | 1,356,632 | 1,356,632 | 1,356,632 | 1,356,632 |
| Repeated logical bytes | 34,776,673 | **0** | 13,166,304 | **0** |
| Source read calls | 516 | 20 | 207 | 20 |
| Explicit seek/position sets | 516 | 1 | 207 | 1 |
| Cache loads / hits | 0 / 0 | 20 / 161 | 0 / 0 | 20 / 53 |
| Peak bounded cache bytes | 0 | 838,323 | 0 | 805,512 |
| Median CPU, process ms | 5,373.395 | 5,420.301 | 1,714.139 | 1,590.036 |
| Median wall ms | 2,655.3067 | 2,634.1250 | 858.7989 | 834.4542 |
| H3/H2 median CPU ratio | — | **1.008729** | — | **0.927600** |
| H3/H2 median wall ratio | — | 0.992023 | — | 0.971653 |
| Physical CSP incl. diagnostic map | 86,727 B | 86,727 B | 5,944 B | 5,944 B |

The independent replay recalculates medians and both ratios from individual archived samples; `samples.tsv` retains both warmups and all six measured samples per pair, without outlier removal.

**Source read reduction:** 96.2455% and 90.6587%, respectively. Each H2 create logically rereads most BASE bytes many times; H3 touches each old-byte offset only once on this source. These are **Stream-level returned bytes**, not physical disk reads, page-cache misses, remote requests or a network savings estimate.

**CPU admission:** The pre-registered `H3_BOUNDED_CACHE_CPU_PROMISING` condition requires a lower median H3 CPU on **both** pinned original pairs. It fails for the first pair (H3 +0.87% CPU), although the second pair is faster by 7.24%. Consequently: **NO_CPU_WIN**. These measurements include RangeObserver instrumentation and whole-process CPU; the prior T3B2B run on another hosted execution must not be treated as a paired performance comparator or as proof that T3 passes the 3× create-cost budget.

**Memory:** H3 uses ~0.8 MiB peak candidate-cache bytes, plus other live window buffers. The process-global GC allocated metric increases on H3 in this run; after-only WorkingSet/PrivateMemory are **not** private RSS, isolated peak memory, or production safety bounds.

## Architecture disposition and next action

**RETAIN the H2-W2 shipping default.** H3 is a genuine bounded read-amplification reduction, but there is no two-pair CPU win, full signed update-set cost result, independent capped resident-memory holdout, or production decision. CSP v1, CSM v1, public API and default encoder remain unchanged. Neither #184 nor #251 is closed.

Do not add another source-byte cache. If further T3 work is justified, isolate zstd dictionary-trial CPU versus candidate-search/metadata work using same-source fair A/B and full update-set denominator; otherwise retain T0/T0S fallback and explicitly close the T3 calibration lane with NO_ADOPT.