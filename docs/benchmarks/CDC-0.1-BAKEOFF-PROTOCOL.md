# CDC 0.1 bake-off protocol (#8)

Status: **proposed; must be accepted before any real-corpus result is inspected**
Issue: [#8](https://github.com/definitely-stable/ChunkShift/issues/8)
Inputs: [CDC-PREFREEZE-DECISION-2026-09.md](CDC-PREFREEZE-DECISION-2026-09.md) (§3, §9, §11), `benchmarks/experiments/prefreeze.v1.json`, real-corpus manifest schema 1 (`Lab/Prefreeze/RealCorpus.cs`, `PrefreezeModels.cs`)
Baseline: `main` at `9157131`; Core semantics, API, CSM format and every ProfileId/ProfileFingerprint unchanged

This document fixes how #8 measures and decides **before** the measurements exist. Nothing in it selects a profile. A rule changed after real-corpus results have been seen invalidates the run, and the run is repeated under the amended protocol with a new holdout.

## 1. Already decided (not reopened by #8)

| decision | where |
|---|---|
| algorithm semantics are `fastcdc.gear.chunkshift.v1` | CDC-PREFREEZE §2, FASTCDC-V1-CANDIDATE |
| prefix semantics are current: zero Gear state at `Minimum`, prefix `[0, Minimum)` unhashed | CDC-PREFREEZE §2, §3 |
| warmed-prefix is lab-only and gets no Core ProfileId; it is a regression guard (§10 here), not a contender | CDC-PREFREEZE §3, §11 |
| SIMD, two-pass and pipeline parallelism are post-freeze same-profile optimizations | CDC-PREFREEZE §7 |
| new CDC algorithms (VectorCDC, SeqCDC, WideCDC, Chonkers, threshold/regression Gear, Google/Stadia Gear) belong to #14 | CDC-PREFREEZE §7, §8 |

## 2. What #8 decides

Only these, from real multi-version data:

- the size point(s) among the 64 / 128 / 256 KiB nominal candidates, compared at **actual measured means**;
- `min/target/max` of the stable profile(s);
- how many stable profiles Core 0.1.0 ships;
- the stable ProfileId(s);
- the default profile.

## 3. Candidates

Primary (all with current `fastcdc.gear.chunkshift.v1` semantics):

| candidate | nominal target | notes |
|---|---:|---|
| `current` @ 64 KiB | 65,536 | `min = target/4`, `max = target·4` |
| `current` @ 128 KiB | 131,072 | same rule |
| `current` @ 256 KiB | 262,144 | same rule |

`min/target/max` is selected among these presets (`FastCdcProfile`: `target/4`, `target`, `target·4`). A different min/max ratio is a new candidate: it is added to the plan before the run, never after results are seen.

Controls:

- `fixed` at the same nominal sizes (lower bound on content-defined value);
- `warmed-prefix` at the same nominal sizes, **only** for the §10 regression guard.

The coarse lane (512 KiB / 1 MiB / 2 MiB) runs as exploratory evidence. It can motivate a later profile but cannot by itself be selected as the 0.1.0 default.

No new CDC algorithm is added for #8. Rabin or Buzhash may join as a control only if a reviewed reference implementation already exists and wiring it in does not delay #8; otherwise its absence is recorded as a corpus/method limitation.

Candidates are listed in `prefreeze.v1.json`. Removing or adding one is a diff to that file, made before the run.

## 4. Real corpus

### 4.1 Manifest

The run reads a manifest in real-corpus schema 1. Each family records:

| field | requirement |
|---|---|
| `id` | unique in the manifest |
| `category` | one of the §4.2 categories |
| `split` | `calibration` or `holdout` (§5) |
| `provenance` | where the bytes came from, including producer policy (layout, compression, alignment) |
| `license` | terms that allow the evaluation |
| `retrievedUtc` | retrieval date |
| `versions[]` | ordered oldest → newest; each with `version`, `path`, `sizeBytes`, `sha256` |

`RealCorpus.Validate` rejects a manifest with a missing split, category, provenance, license or retrieval date, fewer than two versions, a duplicate family id, a missing file, a wrong size or a wrong SHA-256, before any chunking.

Payloads never enter Git. The manifest (without payloads) and the SHA-256 list are attached to the #8 decision note so the run can be reproduced by anyone holding the same files.

### 4.2 Families, in priority order

1. game / PAK-like real version history (PAK, IoStore, or equivalent);
2. a second, independent game/assets family (different producer or engine, e.g. Unity bundles);
3. .NET / application builds;
4. installer / archive family;
5. DB / VM / structured binary;
6. compressed or random-like negative control.

Container/tar/zstd and compressed media are welcome as extra families. Synthetic proxies never substitute for a real family in the final decision; the synthetic plan stays a regression and calibration tool (CDC-PREFREEZE §6).

L7 layout variants (stable vs reordered assets, per-asset vs whole-pack compression, aligned vs unaligned) are separate families that share a category, with the producer policy in `provenance`. The decision note must keep bad chunker response, bad producer layout and compression destroying similarity apart.

### 4.3 History length

| versions | label (`history`) | may select the stable profile alone? |
|---:|---|---|
| ≥ 5 | `full-history` | yes |
| 3–4 | `short-history` | yes, but its limitation is recorded |
| 2 | `pair-only` | **no**: it can support or contradict, never decide |

The target is ≥ 5 consecutive versions for the primary game family. Only real `full-history` and `short-history` families count as `selectionEligibleFamilies` in the output; pair-only and synthetic entries do not.

## 5. Calibration / holdout split

- The split is assigned **per family**, never per version of a family.
- It is written into the manifest **before** the first profile comparison is run on any real data, and the manifest is committed to the #8 decision record (without payloads) with its SHA-256 before results are read.
- Each priority category that has two or more families puts at least one in each split; for example `game-family-A → calibration`, `game-family-B → holdout`.
- After results have been viewed, the split cannot change. A family that turns out to be broken (wrong bytes, licensing problem) is **removed** with a written reason; it is not moved to the other split.
- A run with no holdout family can calibrate but cannot select (the runner warns). A run with no calibration family is also warned about: there is nothing to calibrate against before reading the holdout.
- Calibration families are used to check that the candidates behave as expected and that the tooling works on real payloads. The selection in §9 reads the holdout.

## 6. Transitions and aggregation

### 6.1 Transitions

For each family history:

| scope | transitions | role |
|---|---|---|
| `adjacent` | vN → vN+1 | **primary** product signal |
| `skipped` | vN → vN+2, vN → vN+3 | secondary: stress/history evidence |

`prefreeze --real` emits both, one raw row per (transition, target, candidate).

### 6.2 Family is the decision unit

Transition rows are **not** pooled across families. A family with ten versions would otherwise count three times as much as a family with four.

```text
raw transition rows (kept in the output)
        ↓ aggregate within one family, per (lane, target, candidate, scope)
one family-level result  (output: families[])
        ↓ compare with equal weight per family, per (split, target, candidate, scope)
cross-family comparison  (output: familyComparison[])
```

Within a family, byte totals are summed, so reuse and missing/target are byte-weighted over the family's own transitions. Per-transition rates (survival, forced-maximum rate, manifest density) are averaged with equal weight per transition. Across families, every family has weight 1; the output gives the mean and the worst family for reuse and missing bytes. Decisions use the family table first and the cross-family summary second. A summary that hides one family's regression does not count as evidence.

## 7. Metrics

There is **no single weighted score**. Every metric is reported separately per candidate and family.

| group | metric | source column |
|---|---|---|
| primary | ReuseRatio | `reuseRatio` |
| primary | UniqueMissingPayloadBytes | `uniqueMissingPayloadBytes` |
| primary | UniqueMissingPayloadBytes / TargetBytes | `uniqueMissingPayloadRatio` |
| stability | BoundarySurvival | `boundarySurvival` |
| stability | ResynchronizationDistance p50 / p95 / p99 / max | `resynchronizationDistance` |
| stability | ForcedMaximumRate | `forcedMaximumRate` |
| cost | ActualMeanBytes | `actualMeanBytes` |
| cost | ChunksPerGiB | `chunksPerGiB` |
| cost | ManifestBytesPerGiB | `manifestBytesPerSourceGiB` |
| runtime guardrail | streaming throughput, CPU, allocations, bytes copied, RSS where relevant | `lab` streaming lane (§8.2) |

Naming rules:

- `UniqueMissingPayloadBytes` is a chunk-level transfer lower bound. It is **not** a patch size: CSP does not exist yet (#66, #7), and the decision note must not call it one.
- Resynchronization distance needs a known edit position. Real version transitions have none, so their rows report it as not applicable. Resync percentiles come from the synthetic and boundary-anchored mutation lanes at the same targets, reported next to the real-corpus table and labelled as synthetic. The decision note states this limitation.
- `ActualMeanBytes` is the measured mean over the family's target versions. A nominal target is never quoted as the mean.

## 8. Repeatability

### 8.1 Deterministic quality metrics

Boundaries, reuse, resync, missing bytes and manifest density are deterministic functions of the payloads and the profile. **One run** is enough once every input SHA-256 has been verified (the runner does this). They are not repeated.

The x64 and arm64 chunk-sequence digests of that run must be identical (the same comparison as the `cdc-prefreeze-compare` CI job). A difference is a correctness failure, not noise.

### 8.2 Runtime guardrails

Throughput, CPU, allocations, bytes copied and RSS are noisy and are measured separately from quality:

- `lab --isolate` (one child process per experiment), run **10 times** per candidate set;
- candidate order balanced across the 10 runs (for example, alternating forward and reversed experiment order in the manifest, so every candidate appears equally often in each position);
- every individual sample is kept; the note reports median and dispersion from the samples, not a single number;
- runtime is a guardrail, not a selection criterion: it can reject a candidate that is materially slower or uses materially more memory, but it cannot choose among candidates with equivalent quality (#8 decision rule).

## 9. Selection rules (read on the holdout)

1. Compare the three primary size points on the holdout family table, `adjacent` scope first.
2. A size point that materially regresses reuse or missing/target on any eligible holdout family relative to another size point needs a written justification from the cost metrics (actual mean, chunks/GiB, manifest/GiB), or it is rejected.
3. `skipped` results and the calibration families are supporting evidence. They can reveal a problem, but they do not overturn a clean holdout result on their own.
4. When candidates are inside measured noise on product metrics, prefer the simpler/more mature contract and the lower manifest density.
5. The note records every rejected candidate with its evidence, and the corpus limitations (missing categories, pair-only families, synthetic-only resync).

## 10. Warmed-prefix guard

Warmed-prefix is compared with `current` on the holdout **only** as a regression check. It is never ranked against the size points.

Semantics are reopened only if **both** hold:

- warmed-prefix improves ReuseRatio **or** UniqueMissingPayloadBytes by **more than 0.5%** on at least one holdout family; **and**
- no holdout family regresses materially under warmed-prefix.

Otherwise the result is recorded as **rejected / lab-only**, and no Core ProfileId is created for it. The per-file `currentCutsInTransient` / `warmedCutsInTransient` columns are reported either way (CDC-PREFREEZE §3 residual risk).

## 11. Outputs

The #8 decision note attaches:

- the manifest without payloads, its SHA-256, and the commit that ran it;
- the `prefreeze` JSON and markdown for x64 and arm64 (raw rows, `families`, `familyComparison`, divergence);
- the 10 isolated `lab` runtime results;
- the selected ProfileId(s), full semantics, actual means, and the cross-platform vector digest;
- remaining same-profile optimization work that cannot change semantics.

## 12. Acceptance of this protocol

This protocol is accepted when it is merged to `main` and #8's body references it. Real-corpus results produced before that point do not count as #8 evidence.
