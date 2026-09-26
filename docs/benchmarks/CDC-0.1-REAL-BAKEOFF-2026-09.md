# CDC 0.1 real-corpus deterministic bake-off (#8)

Status: **deterministic quality phase complete; runtime guardrails pending.**

Issue: [#8](https://github.com/definitely-stable/ChunkShift/issues/8) · Protocol: [CDC-0.1-BAKEOFF-PROTOCOL.md](CDC-0.1-BAKEOFF-PROTOCOL.md) · Frozen corpus: [CDC-0.1-REAL-CORPUS.md](CDC-0.1-REAL-CORPUS.md)

This note records the first measurement performed after the #8 corpus was frozen. It does not select the final 0.1.0 profile: protocol §8.2 still requires 10 isolated runtime runs before the final decision.

## 1. Evidence identity

| item | value |
|---|---|
| protocol baseline | `80058a1177306817ed06c95143a3c54bf16229bd` |
| frozen manifest SHA-256 | `fe497c1717bda7da1681fae7dd4fe216a855690f50cef859a7a40dbddd2e2b49` |
| frozen experiment-plan SHA-256 | `62cb1aa2745a150d29f1e0f9cae1824f58914111e062fcb4bd507d8ffc958b34` |
| measurement PR | #109 |
| measured head | `ef895d5587c6eee197635ba1085203d055590c8c` |
| measured tree | `1a87a66d4e61842ba28dae0bdf986332d5c3a266` |
| merged commit | `9ebcba9d11ba8af445d4c43dbb53daad4b2dca86` |
| merged tree | `1a87a66d4e61842ba28dae0bdf986332d5c3a266` |
| workflow run | [36180970747](https://github.com/definitely-stable/ChunkShift/actions/runs/36180970747) |

The measured PR head and the squash-merged commit have the same Git tree, so the evidence applies to the merged `main` tree without a post-merge repeat.

Before candidate chunking on both runners, the workflow reconstructed all 38 payloads from upstream release assets, verified every source checksum, verified every final payload size/SHA-256, regenerated the corpus lock, and required it to be byte-identical to the committed lock. Both validation reports had 3 eligible calibration families, 3 eligible holdout families, `selectionPossible=true`, and no warnings.

### Raw workflow artifacts

| artifact | id | GitHub artifact digest |
|---|---:|---|
| x64 | 10883963393 | `sha256:d5126f41d366b18f919cac20df4cf726090958100d7c78b9644888d25cf2eb74` |
| arm64 | 10884871193 | `sha256:4744084973c9efac681df2a9210c1c24694aa734051317cca58c75d4c68a8076` |
| cross-architecture verdict | 10884157726 | `sha256:7652902b9533eca8751838e8fcf2fb7f38061d1ecf769915c25b633c629f985c` |

The raw JSON files include timestamp/environment fields, so their complete-file hashes differ across architectures. The deterministic payload-derived sections are compared separately by the cross-architecture gate.

## 2. Cross-architecture determinism

The gate passed:

- fine lane: **702** real transition rows have identical target chunk-sequence digests on x64 and arm64;
- coarse lane: **702** real transition rows have identical target chunk-sequence digests;
- current/warmed divergence digests are identical;
- `families[]` is exactly identical;
- `familyComparison[]` is exactly identical.

This is a correctness result, not a statistical observation.

## 3. Primary holdout result

Protocol §9 reads the selection-eligible holdout, adjacent transitions first. The table below is the `current` FastCDC candidate, with equal family weight across the three holdout families.

| nominal target | mean actual | mean reuse | worst-family reuse | mean missing/target | worst missing/target | mean boundary survival | mean chunks/GiB | mean manifest/GiB |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 64 KiB | 80.69 KiB | 44.697% | 16.888% | 55.303% | 83.112% | 40.982% | 13,001 | 458.75 KiB |
| 128 KiB | 160.18 KiB | 39.353% | 11.059% | 60.647% | 88.941% | 35.955% | 6,547 | 231.03 KiB |
| 256 KiB | 322.11 KiB | 32.845% | 6.994% | 67.155% | 93.006% | 29.712% | 3,258 | 114.98 KiB |

Actual means are reported explicitly; nominal target is not treated as the measured mean.

### Per-family adjacent results

| holdout family | target | reuse | missing/target | boundary survival |
|---|---:|---:|---:|---:|
| OpenRCT2 | 64 KiB | 54.572% | 45.428% | 53.454% |
| OpenRCT2 | 128 KiB | 51.565% | 48.435% | 51.040% |
| OpenRCT2 | 256 KiB | 46.547% | 53.453% | 45.816% |
| PowerShell | 64 KiB | 62.632% | 37.368% | 57.298% |
| PowerShell | 128 KiB | 55.435% | 44.565% | 48.077% |
| PowerShell | 256 KiB | 44.995% | 55.005% | 38.129% |
| ICU data | 64 KiB | 16.888% | 83.112% | 12.195% |
| ICU data | 128 KiB | 11.059% | 88.941% | 8.748% |
| ICU data | 256 KiB | 6.994% | 93.006% | 5.190% |

## 4. The ordering is not an averaging artifact

There are 16 adjacent holdout transitions in total.

- 64 KiB beats 128 KiB on reuse/missing for **16/16** adjacent transitions, and every advantage is material under the pre-registered 0.5 percentage-point rule. The smallest reuse advantage is **1.904 pp**.
- 128 KiB beats 256 KiB for **16/16** adjacent transitions, again materially every time. The smallest reuse advantage is **2.675 pp**.
- Therefore 64 KiB beats 256 KiB for every adjacent transition as well.

The secondary skipped-transition evidence agrees:

- 64 KiB beats 128 KiB materially on **23/23** skipped transitions;
- 128 KiB beats 256 KiB materially on **23/23** skipped transitions.

At the family level, moving 64 → 128 KiB loses:

- OpenRCT2: **3.008 pp** reuse;
- PowerShell: **7.197 pp**;
- ICU: **5.829 pp**.

Moving 128 → 256 KiB loses another:

- OpenRCT2: **5.018 pp**;
- PowerShell: **10.439 pp**;
- ICU: **4.065 pp**.

The trade-off is therefore genuine: larger chunks approximately halve chunk/manifest density at each step, but the real holdout quality loss is material in every family and every adjacent transition.

## 5. Controls

### Fixed-size

At each primary target the current CDC candidate beats fixed-size on every adjacent holdout transition by more than 0.5 pp reuse.

The smallest observed current-over-fixed reuse advantage is:

- 64 KiB: **10.986 pp**;
- 128 KiB: **6.259 pp**;
- 256 KiB: **0.594 pp**.

This establishes that the CDC behavior itself provides measurable reuse value on the frozen holdout; the size ordering is not merely a fixed-block-size effect.

### Warmed-prefix guard

Protocol §10 does **not** reopen semantics.

The largest warmed-prefix improvement in either reuse or missing/target on any eligible holdout family is only **0.0121 pp** (PowerShell, 64 KiB), far below the required **>0.5 pp** threshold. Most family/target pairs are exactly equal on the primary metrics.

Therefore the first condition of the guard is false. Warmed-prefix remains rejected/lab-only and no Core ProfileId is created for it.

## 6. Calibration and negative controls

Calibration behaves consistently with its declared purpose:

- Mindustry retains high reuse and still prefers the smaller target: 92.00% / 91.12% / 87.97% at 64/128/256 KiB;
- the solid-LZMA Notepad++ installer and whole-archive-gzip .NET runtime package produce effectively 0% reuse at all three primary targets, as expected for the compressed negative cases.

Those compressed calibration families do not drive the holdout decision.

## 7. Exploratory coarse lane

The coarse lane does not reveal a quality reversal:

| nominal target | holdout mean reuse | worst-family reuse | mean missing/target |
|---:|---:|---:|---:|
| 512 KiB | 26.679% | 5.471% | 73.321% |
| 1 MiB | 18.629% | 0.167% | 81.371% |
| 2 MiB | 14.896% | 0.320% | 85.104% |

The coarse lane remains exploratory as pre-registered and provides no evidence to expand the Core 0.1.0 primary candidate set.

## 8. What this evidence does and does not decide

The deterministic data establishes:

1. x64/arm64 chunking is identical on the frozen real corpus;
2. the current CDC candidate materially outperforms fixed-size controls;
3. among the primary size points, deterministic reuse/missing quality is ordered **64 KiB > 128 KiB > 256 KiB** on every eligible holdout adjacent transition;
4. larger targets buy substantially lower chunk/manifest density, so the final decision still has a real cost trade-off;
5. warmed-prefix fails its pre-registered reopen gate and stays lab-only.

It does **not** yet select the stable ProfileId/default. Protocol §8.2 still requires 10 process-isolated runtime measurements with balanced order, individual samples retained, and median/dispersion reported. Runtime is a guardrail, not permission to override a material quality regression without the written justification required by §9.

## 9. Finalization

This document remains the historical deterministic-quality record and its pre-runtime wording above is intentionally preserved.

PR #112 subsequently completed the pre-registered runtime guardrail without a runtime veto. The stable profile/identity selection is recorded separately in [CDC-0.1-PROFILE-DECISION-2026-09.md](CDC-0.1-PROFILE-DECISION-2026-09.md). Historical candidate identifiers and measurement results in this document are not rewritten to the later stable ProfileId.

