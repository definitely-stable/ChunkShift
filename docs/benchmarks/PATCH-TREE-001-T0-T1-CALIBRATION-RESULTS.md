# PATCH-TREE-001 — accepted T0/T0S/T1 calibration evidence (2026-10-11)

**Status:** ACCEPTED CALIBRATION EVIDENCE; **NO-GO for promoting the frozen naive T1 concatenated-old-tree construction on this .NET cohort**. This is **NOT a sealed holdout or shipping/production decision**, and it does not decide T2/T3. Owning [#184](https://github.com/definitely-stable/ChunkShift/issues/184), program gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

## Immutable provenance and independent verification

- First-attempt GitHub-hosted Linux x64 workflow: [calibration run #38142160679](https://github.com/definitely-stable/ChunkShift/actions/runs/38142160679) — **SUCCESS**, main commit `913e81ed00b877db4f8d77b3c5dbe9455793f3eb`, .NET SDK `10.0.401`.
- [Accepted calibration artifact #11703153269](https://github.com/definitely-stable/ChunkShift/actions/runs/38142160679/artifacts/11703153269), with original frozen pairs, exact canonical layouts, full per-file calibration JSON, provenance and `SHA256SUMS`. GitHub Actions retention: **90 days**. This document preserves the numeric conclusions and identifiers beyond artifact expiration; it is **not** a replacement for the full per-file raw evidence.
- Frozen metadata source: successful first-attempt [original pairs recovery #38136336025](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025), [artifact #11700594742](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025/artifacts/11700594742).
- Complete frozen `pairs.json` SHA-256: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.
- Accepted `calibration.json` SHA-256: `d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c`.
- `layouts.json` SHA-256: `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a`.
- `provenance.json` SHA-256: `44a9b66fcdda08070a7e50dc0238acca5683d0a85561ecb7086059c0643cad56`.
- Archive-internal SHA256SUMS and the GitHub-hosted independent `verify_patch_tree_001_calibration.py` succeeded; a separate read-only local replay independently recomputed 1,051 per-file expected SHA/size/role identities and every pair-level physical byte sum against the original frozen pairs.
- **Scope:** exactly two calibration families, four adjacent-version pairs: 1,049 changed, 2 added, 2 removed, 13 unchanged regular files. Held-out Node.js, tzdata, and repository-source trees were not evaluated by the calibration workflow.

## Exact physical byte totals

| Lane / component | Bytes |
| --- | ---: |
| T0 warm total | 14,758,982 |
| T0S full total (self-contained new-file compression control) | 13,143,287 |
| T1 warm total | 83,593,822 |
| T1 cold total | 83,966,486 |
| T0 changed CSP + raw added payload | 14,573,546 |
| T1 changed + added CSP payload | 83,408,386 |
| Common full target regular-file reference map | 185,436 |
| T1 whole-old-tree CSM | 168,292 |
| T1 old-base layout sidecar | 204,372 |

Decimal MB are bytes / 1,000,000. **Aggregate ratio = aggregate T1 bytes divided by aggregate T0/T0S bytes**, not the arithmetic mean of per-file percentages.

- T1 warm / T0 warm: **5.6639x** (466.39% more bytes); T1 warm / T0S: **6.3602x** (536.02% more).
- T1 cold / T0 warm: **5.6892x** (468.92% more); T1 cold / T0S: **6.3885x** (538.85% more).
- The 372,664-byte CSM-plus-locator cold charge is **not** the principal cause of T1 regression. The majority of the cost is larger T1 CSP payloads.

## Complete calibration pair population

| Frozen family | Base → target | Changed + added | T0 warm B | T0S B | T1 warm B | T1 cold B |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| dotnet-aspnetcore-win-x64 | 10.0.10 → 10.0.11 | 335 | 5,748,069 | 4,940,151 | 28,280,016 | 28,391,959 |
| dotnet-aspnetcore-win-x64 | 10.0.11 → 10.0.12 | 336 | 4,409,941 | 3,602,164 | 22,408,918 | 22,520,717 |
| dotnet-runtime-linux-arm64 | 10.0.10 → 10.0.11 | 190 | 2,798,481 | 2,798,481 | 18,471,392 | 18,546,087 |
| dotnet-runtime-linux-arm64 | 10.0.11 → 10.0.12 | 190 | 1,802,491 | 1,802,491 | 14,433,496 | 14,507,723 |

Changed paths only: 1,049 total; T1 is smaller than T0 for **6**, larger for **1,037**, and equal for **6**. Both newly added files have smaller T1 than raw T0, but cannot reverse the overall verdict. Compare T1 against T0S to distinguish cross-file reuse from generic compression.

## Diagnostic runtime observations (not performance gates)

- Sum of individual per-file **create wall times**: T0 172.52 s, T1 251.68 s; of **apply wall times**: T0 13.18 s, T1 11.35 s.
- T1 virtual base stream counters from create+apply: 107,651 reads; 5,987,903,169 **logical bytes** read; 76,688 seeks.
- These are single-run wall times and virtual-stream logical counters; they are not independently repeated CPU time, peak RSS, real disk I/O or end-to-end update publication costs. In particular, do not equate `BytesRead` with physical storage read amplification.

## Interpretation and next constrained work

1. **Decision on the frozen T1 construction only: NO-GO for production promotion by the proposed ≥10% byte reduction gate.** Instead of saving 10%, it increases payload and whole-update bytes by several-fold on every frozen calibration pair. Do not call this an accepted T1 optimization.
2. The likely *mechanism*, still requiring a controlled lab test, is destruction of per-file CDC chunk alignment by bytewise concatenation of the old tree: per-file T0 manifests reset chunk boundaries at each file, whereas T1's one CSM does not. Use calibration-only chunk-ID overlap and alignment attribution, with frozen profiles and no holdout exposure, to establish or falsify this explanation. Do not retrofit the T1 freeze after inspecting results.
3. If a new variant is warranted, freeze its semantics as a **separate candidate** (e.g. file-boundary-preserving shared content index); it cannot be silently substituted for T1 or presumed implementable under unchanged CSP v1 BASE/ManifestId semantics.
4. **T2/T3: NOT_RUN.** Do not promote or eliminate them on T1 evidence alone. Any follow-up should be separately protocol-frozen and measured on calibration before any one-shot sealed holdout. Bounded base locator build cost/peak RSS and receiver-side apply cost remain unresolved.
5. Shipping tree publication, signing/trust, cross-platform paths and modes, symlinks, empty directories, atomicity/crash recovery and release gate #251 remain open. This evidence changes no `src/`, CSP/CSM v1 or public API.

Earlier failed infrastructure executions #38136576432 and #38138008244, and quarantined diagnostic run #38140831430 (artifact #11702821630), **are not replacements for this accepted result**. The true accepted result is run #38142160679 alone.
