# PATCH-TREE-001 / F1 — original matched physical attribution evidence

**EvidenceId:** `PATCH-TREE-001/F1-EVIDENCE-20261016-001`  
**Decision:** `ORIGINAL_ARCHIVE_AUDIT_PASS / CALIBRATION_ONLY / OBSERVATIONAL_DICTIONARY_SELECTION_MECHANISM / NAIVE_T1_NO_GO / NO_ADOPT`.  
**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184) (OPEN, corrected after accidental early issue closure) · [Research Freeze #251](https://github.com/definitely-stable/ChunkShift/issues/251) (OPEN).  
**Implementation:** [PR #364](https://github.com/definitely-stable/ChunkShift/pull/364), `main@2565a2cf0af9959e53c9d245e353337a8be9c91e`. No production `src/`, encoder, CSP/CSM or public API change.

## Answer first

A real, source-locked, GitHub-hosted **F1 calibration-only** run completed on the original four .NET pairs and was independently audited against the earlier *original* T0/T1 physical and T1-B boundary artifacts. F1's public-default CSP SHA-256 equals its traced sequential H0 CSP SHA-256 **for every one of the 2,100 actual CSP observations**. All 1,051 changed/added file identities, target SHAs, CSP SHAs and physical byte lengths also **match the previously accepted physical calibration** from a different source commit. That is reproducibility evidence of the emitted CSP bytes on this corpus, not a performance or general determinism guarantee.

The 68,834,840 B net T1-over-T0 regression decomposes physically as follows:

| Real physical component | T1 − T0 (B) |
| --- | ---: |
| Stored zstd frame bytes | **+71,500,296** |
| PAYL entry/section metadata | +42,232 |
| PIDX physical bytes | +2,864 |
| Other physical bytes (including per-file static section cost) | +2,720 |
| Stored raw payload bytes | 0 |
| **T0 raw transport of two added files, not CSP** | **−2,713,272** |
| **Net warm update difference** | **+68,834,840** |

This is an **exact byte accounting identity**, not causal allocation of the size increase.

The frozen T1-B *equal-exact-availability* cohort is now stronger: for all **981 changed paths** not only is the number of exact matching bytes equal, but the *ordered emitted* `(TargetIndex, TargetChunkId, TargetLength)` sequence matches identically in T0 and T1 for each path. Each lane emits **2,682** entries on these files. Nevertheless, T1 emits **+61,981,659 B more stored zstd frame data**, accounting for nearly all of this cohort's **+62,003,683 B** physical regression.

## Primary matched per-entry observation

| F1 paired encoding, for the same emitted target chunk | Entries | T1 − T0 stored frame bytes |
| --- | ---: | ---: |
| zstd dictionary → zstd dictionary, **different first selected base ChunkId** | **1,927** | **+45,607,296** |
| zstd dictionary → zstd with no dictionary | **736** | **+16,379,287** |
| zstd without dictionary → zstd dictionary | 1 | −4,924 |
| zstd without dictionary → zstd without dictionary | 7 | 0 |
| raw → raw | 11 | 0 |
| **Total** | **2,682** | **+61,981,659** |

For all 1,927 dictionary→dictionary entries, the chosen first dictionary ChunkId differs between the two lanes. This does **not** assert a distinct byte sequence for the complete concatenated dictionary without the raw CSP/old content, or prove that an alternative offset policy would restore T0 quality. Across *all* T1 selected dictionary entries, 2,202 of 2,220 name a different old-tree **first-source file** from the target path, and only 18 name that target path. The label describes the first base chunk only; it does **not** identify the complete dictionary span or prove that it remains inside one file.

**Interpretation:** F1 strongly supports a **dictionary/candidate selection failure mechanism** on the naive T1 representation. It falsifies a pure *more missing target chunks* explanation for the dominant 981-file cohort. It does not isolate the effect of offsets, candidate ranking, dictionary content, zstd context or target layout in a causal counterfactual. No T1 retuning, T2/T3, holdout opening or production promotion is authorized by this result.

## Complete four-pair population and cohort arithmetic

| Frozen T1-B cohort | Files | T0 physical B | T1 physical B | Δ B |
| --- | ---: | ---: | ---: | ---: |
| Changed: equal exact target availability **and identical emitted target entries** | 981 | 11,514,584 | 73,518,267 | **+62,003,683** |
| Changed: T1 loses exact target availability | 68 | 345,690 | 9,085,396 | **+8,739,706** |
| Added: T0 transports raw bytes; T1 has a base-bound CSP | 2 | 2,713,272 | 804,723 | **−1,908,549** |
| **Total** | **1,051** | **14,573,546** | **83,408,386** | **+68,834,840** |

Add the identical four-pair target-tree metadata **185,436 B** to obtain T0 warm **14,758,982 B** and T1 warm **83,593,822 B**, a **5.6639287×** T1/T0 size ratio. T0 has 2,924 emitted CSP payload entries across its 1,049 changed-file CSPs; T1 has 3,041 entries across its 1,051 changed/added-file CSPs. T0 has **2,904** selected dictionary entries versus **2,220** for T1. The T1 full-population recorded candidate trials rise from T0's **14,748** to **18,891**. These are deterministic observer counts, **not** create-time or CPU benchmark outcomes.

Within the equal-exact cohort, 2,663 T0 payload entries select a dictionary and 1,928 T1 entries do; 8 T0 vs 743 T1 use zstd without a dictionary. Stored raw bytes are unchanged, and the cohort's PIDX length is unchanged because its emitted entries are identical. Changed-but-lost-exact cohort size contributes +8,739,706 B; the two added paths reduce T1-relative cost by 1,908,549 B versus T0's raw added-file transport control.

Top original regressions include `System.Private.CoreLib.dll` (ASP.NET win-x64 10.0.10→10.0.11, **+4,136,499 B**), `System.Private.CoreLib.dll` (runtime Linux ARM64 10.0.10→10.0.11, **+3,778,128 B**) and `libcoreclr.so` (runtime Linux ARM64 10.0.10→10.0.11, **+2,013,025 B**). These are individual source-locked file observations, not independent statistical replications.

## Original provenance and independently verified archive integrity

| Evidence | Immutable reference |
| --- | --- |
| Real F1 run | [#38773779715](https://github.com/definitely-stable/ChunkShift/actions/runs/38773779715), first attempt, `workflow_dispatch`, `SUCCESS` |
| F1 ZIP | [artifact #11993992185](https://github.com/definitely-stable/ChunkShift/actions/runs/38773779715/artifacts/11993992185), SHA-256 `a3b994d94121efaaa7b4915877c007c63744b065d629d7d02e6991b7628e4a9c` |
| Original physical T0/T1 comparison | [run #38142160679 / artifact #11703153269](https://github.com/definitely-stable/ChunkShift/actions/runs/38142160679/artifacts/11703153269), ZIP SHA-256 `e5707a7334ebc909486945edaf3eed8832a121b715ba5e9cd1354e2447865812` |
| Original T1-B CDC boundary accounting | [run #38736641373 / artifact #11974666607](https://github.com/definitely-stable/ChunkShift/actions/runs/38736641373/artifacts/11974666607), ZIP SHA-256 `2918c70f03bf87e2e3351c52399debfa61c54887a3b9c48b8473c362606a4002` |
| Original frozen pairs recovery | Run `38136336025`, full original `pairs.json` SHA-256 `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` |
| F1 source and runner | `main@2565a2cf0af9959e53c9d245e353337a8be9c91e`, GitHub-hosted Linux x64, .NET `10.0.402`, attempt 1 |

Original F1 ZIP has **2,107 members**: 2,100 `f1-observations/*.json` sidecars plus `F1-SHA256SUMS`, `SHA256SUMS`, `original-pairs.json`, `layouts.json`, `calibration.json`, `f1-summary.json`, and `provenance.json`. All three downloaded ZIP CRC checks PASS, all root and all 2,100 per-sidecar SHA-256 match their ORIGINAL manifests. F1 `calibration.json` SHA `6dc7b2afab66dc1ad0eb5cc632c9ecd581726a0bc8997de6ec3f1e4f6eaa65e0`; `f1-summary.json` SHA `d5524c3ace1f16fb2c78fd78b72beff73012db03e94e55e9a2aa465ee5153ec3`; `provenance.json` SHA `0c80486d252816bb01aaa6734f11af8b427fe12f920002f3d74f826f01616307`.

All three original `pairs.json` **and full `layouts.json` bytes are identical**. Per-file target lengths/hashes, all 2,100 public CSP SHA-256 hashes and byte lengths, and all four old-tree ManifestIds from F1 match the *prior* physical calibration; no convenient file subset or post-hoc dropped CSP observations. Every observer's public/traced CSP SHA matches, and `raw + zstd stored + PAYL metadata + PIDX + other == full CSP bytes` for every sidecar. Full source-lock, GitHub-run provenance, exact row ordering and first-occurrence sidecar filename hashes pass the independent replay.

Reproduce from the **original artifacts only**, using the checked-in [machine ledger](../../../benchmarks/experiments/patch-tree-001-f1-original-evidence-20261016.json) and the [independent three-ZIP replay](data/PATCH-TREE-001-F1-EVIDENCE-20261016-001/recompute.py). Run: `python3 docs/research/results/data/PATCH-TREE-001-F1-EVIDENCE-20261016-001/recompute.py --f1 original-f1.zip --boundary original-t1b.zip --physical original-t0t1.zip --ledger benchmarks/experiments/patch-tree-001-f1-original-evidence-20261016.json`. Only unmodified original GitHub Actions ZIPs match the pinned SHA-256. This replay is deliberately NOT an independent byte-exact CSP or zstd implementation. The originating hosted C# test and create/apply oracle validate semantics; only the original SHA and summary/trace JSON were archived, **not** all raw CSP bytes.

## Decision and next permitted study

**F1 technical acceptance: PASS.** The exact source-locked naive whole-tree T1 stays **NO-GO** on this calibration cohort. Neither F1 nor its earlier T1-B diagnostic is a new sealed holdout. No causal share of the ~68.8 MB can yet be assigned to *offset distance alone*, *dictionary window alone*, *zstd alone* or any prospective alternate base representation.

**Next only if justified:** separately preregister **F2 within-build matched dictionary-location counterfactual** for the 981 equal-exact paths, using a frozen set of offset-preserving candidate sets, an explicit no-dictionary and same-file dictionary control, and exact whole-patch bytes/CPU/RSS guardrails. Compare against T0 default and original T1 default without retuning on held-out data. Require a *real evaluated physical size improvement* and source-provenance checks before allowing T2/T3 or an update-set RFC.

**#184 remains OPEN until an explicit final update-set architecture/publication semantics decision. #251 remains OPEN; #140/Patching PublicAPI compatibility freeze and publication remain BLOCKED.**
