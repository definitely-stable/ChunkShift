# PATCH-TREE-001 / T3-B2D0 — original exhaustive encode stage attribution

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261025-T3B2D0-001`  
**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), parent [#251](https://github.com/definitely-stable/ChunkShift/issues/251)  
**Date:** 2026-10-25  
**Verdict:** **B2D0_ORIGINAL_STAGE_ATTRIBUTION_PASS / DICTIONARY_ENCODE_WORKER_ELAPSED_DOMINANT / NO_PRODUCTION_ADOPT**.

## Original provenance and audit

- Implementation [PR #411](https://github.com/definitely-stable/ChunkShift/pull/411) squash-MERGED at `main@bb38ec50eb12b2da6ac985d6ac052224d63878cc`.
- First-attempt original [GitHub-hosted run #40194727803](https://github.com/definitely-stable/ChunkShift/actions/runs/40194727803) **SUCCESS**, main push, Linux x64.
- Artifact **#12599263437**, `patch-tree-t3b2d0-original-ranges-NO-ADOPT` (name is historical from the C1-based archived workflow; contents are stage accounting, not BASE-range tracing). ZIP **301,530 bytes**, SHA-256 `cc8aeb691d6c1cbdd93db008027d7b12d6f2a606633a623f7f38ef7812f2ddcb`. Original archive ZIP CRC and all seven SHA256SUMS members independently checked.
- Original files: `original-pairs.json` SHA `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`; B1 selection `2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad`; B2A selection `1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b`; canonical `layouts.json` `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a`.
- `stages.json` SHA `7ea88a26992d5ea98c1b0216c08fc26f1ff2407aebcc9575de5821ec1fba1ca0`; `provenance.json` SHA `fcf9183416d2bd7735d9a91fb39bf6a194acff09aa1dbee29fde42da11d8d1f6`.
- Retained all **24 samples** (3 warmups + 9 measured per original DLL pair), complete original CSP SHA and original per-run stage tick/counter values in [samples.tsv](data/PATCH-TREE-001-T3B2D0-EVIDENCE-20261025-001/samples.tsv). Standalone [stdlib replay](data/PATCH-TREE-001-T3B2D0-EVIDENCE-20261025-001/recompute.py) checks exact ZIP/member/SHA/source provenance and every TSV field against original producer JSON, then recomputes all medians from raw samples. This is an independent *archived-observation* audit, not a new physical CSP decode or an independent CPU profile. The original producer performed SDK create/apply and exact target SHA, and the workflow fail-closed on source locks and CSP physical parity. Raw original CSP/CSM/target binaries were intentionally not archived.
- Sealed holdout **not opened**; source/calibration population limited to the same two previously selected ASP.NET Core win-x64 `mscordaccore` renamed DLL transitions, each 1,356,632 B old source.

## Three-lane CPU control (ms)

Each pair ran `OFF_A` and `OFF_B` as **two identical shipping H2-W2 no-cache, no-probe** controls; `ON` used the same exact exhaustive CSP policy in a separate internal instrumented chooser. One warmup per lane then 3 rotating measured cycles; CPU is whole-process accumulated `TotalProcessorTime`, wall is Stopwatch elapsed. CSM build and SDK apply happened **outside** each measured create.

| CPU median / ratio | 10.0.10→10.0.11 | 10.0.11→10.0.12 |
| --- | ---: | ---: |
| OFF_A process CPU | 5,370.515 ms | 1,600.158 ms |
| OFF_B process CPU | 5,263.376 ms | 1,605.323 ms |
| Pooled six OFF samples median | 5,331.0255 ms | 1,602.7405 ms |
| ON measured process CPU | 5,459.857 ms | 1,635.014 ms |
| OFF_B / OFF_A CPU | **0.98005** | **1.00323** |
| ON / pooled OFF CPU | **1.02417** | **1.02014** |

The two-control A/A ratio differs from unity by ~2% on pair 1 and ~0.3% on pair 2. The apparent ~2% probe ON excess is **not a statistically isolated causal overhead bound** from a single hosted process and 3 rounds. No outliers dropped; all complete original samples archived.

Every one of the **24 real physical CSPs** exactly matches the original prior B1 SHA-256 per version transition:
- 10.0.10→10.0.11: **86,432 B**, SHA `24a5579e96411f64c9b790e68d4ff01cbeb42f79500094553007fd1330e5dd29`.
- 10.0.11→10.0.12: **5,649 B**, SHA `de7e1d02606c7663dac0aaaad86da4b4839552e58a6a00ee7cc99b6498c52103`.

Each round: SDK exact Apply and original target SHA **PASS**. The 295-B diagnostic reference charge per pair remains separate from CSP payload; no signed real update-set is measured.

## Worker-summed stage elapsed — instrumented ON measured median

These are **Stopwatch wall ticks accumulated across concurrent encode workers, NOT CPU time**. Async dictionary-read ticks include wait/scheduling overhead. Ratios below partition only the *four instrumented categories*, not the entire create CPU or wall critical path.

| Observed stage (median of three ON creates) | 10.0.10→10.0.11 | 10.0.11→10.0.12 |
| --- | ---: | ---: |
| Missing target payload entries | 20 | 9 |
| Plain L19 zstd trials | 20 | 9 |
| Dictionary candidate reads | 136 | 57 |
| Dictionary L19 zstd trials | **136 (6.80 per target entry)** | **57 (6.33 per target entry)** |
| Chosen-base exact verified records | 77 | 30 |
| Plain zstd encode worker elapsed | 407.889 ms | 197.029 ms |
| Dictionary source read worker elapsed | 12.864 ms | 4.847 ms |
| **Dictionary zstd encode worker elapsed** | **4,927.660 ms** | **1,395.872 ms** |
| Selected-base ChunkId verification | 2.010 ms | 0.844 ms |
| Dictionary zstd share of those four summed groups | **92.10%** | **87.32%** |

This strongly localizes the *measured worker-elapsed cost* to repeated level-19 zstd candidate encodes. It agrees qualitatively with T3-B2C1: eliminating 90–96% of BASE Stream rereads with H3 did **not** materially reduce whole-process CPU on both pairs. It is still not a causal proof that removing 136/57 calls yields proportional CPU savings: compressor contexts, candidate selection and output-size tradeoffs remain interacting factors, and parallel worker elapsed may contain preemption.

## Architecture verdict and gates

1. **RETAIN shipping `CspCreateExecution.Default` = H2-W2 without candidate cache.** No public API/CSP v1/CSM v1/encoder candidate policy, default, or publication change.
2. **Do not rerun H3 caching as a prospective CPU optimization:** base-source logical reads were not a material process-CPU win on this original corpus; the observed expensive path is dictionary trial encoding, already investigated for choice/trial elimination in rejected PATCH-ENC-005 #181. A further optimization needs a **new representation/decision-grade headroom argument** or genuine system-level constraints beyond prior locked universes.
3. **Do not extrapolate stage Stopwatch sums to an achievable CPU lower bound.** Any attempt to change compression trials, reused encoder state or codec version requires a frozen paired physical-CSP + exact-apply admission protocol on broader real families, comparison to T0S and full tree metadata/locator/apply/RSS, with independent holdout before production promotion.
4. **Keep the already demonstrated T3 create-budget fallback to T0/T0S** under the prior research admission. The two original T3B2B calibration pairs failed the 3x CPU/wall gate even though size savings were large; this B2D0 study did **not** remeasure T0S in a paired run and does not override that fallback.
5. #184/#251 remain **OPEN**. Next prudent step is a decision-oriented T3 closeout/update-set synthesis under #184 and a freeze-disposition matrix under #251, not another speculative base-cache implementation.

**Disposition:** `B2D0_ORIGINAL_STAGE_ATTRIBUTION_PASS / NO_PRODUCTION_ADOPT`. Reopen only after new, independent **decision-grade representation/source** headroom is established.
