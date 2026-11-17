# PATCH-GAP-001 / HDiffPatch — original calibration, independent paired audit

**EvidenceId:** `PATCH-GAP-001/HDIFF-CALIBRATION-EVIDENCE-20261101-001`. **ExperimentId:** `PATCH-GAP-001`. **Owners:** [#183](https://github.com/definitely-stable/ChunkShift/issues/183), [#251](https://github.com/definitely-stable/ChunkShift/issues/251). **Date:** 2026-11-01.

**Status:** **ORIGINAL CALIBRATION MEASURED / MATCHED INDEPENDENT REPORT AUDIT PASS / WORKFLOW FINAL-PROVENANCE STEP FAIL / EVALUATION NOT RUN / NO CSP RFC ADOPTION**.

## Original, source-locked GitHub-hosted execution

- [Workflow run #41333408342](https://github.com/definitely-stable/ChunkShift/actions/runs/41333408342), **attempt 1**, GitHub-hosted `ubuntu-24.04`, from `main@f0224c252503c09174093031650ea2ca1c86ee04`. The **workflow conclusion is failure**; this is not a falsely relabelled SUCCESS.
- All **1,049 original changed-file** calibration rows were processed in both independently executed HDiffPatch v5.1.3 modes (`-m-4`, `-s-64`; frozen `-SD -d -f -p-1 -c-zstd-21-24`). Each was applied via `hpatchz -s-8m -f` and verified against its expected target SHA-256. The frozen materializer's source and `pairs.json` locks passed. The structured report audit passed, and the original ZIP artifact uploaded successfully.
- **Why GitHub marks the job failure:** only the final `Record provenance` shell step failed: `actions/upload-artifact` exposes a **bare 64-hex output digest** `e42db34...`, while the script incorrectly required the prefix `sha256:`. The repository is amended to normalize both output spellings. **No scientific rerun or retuning.**
- New artifact [#13094063839](https://github.com/definitely-stable/ChunkShift/actions/runs/41333408342/artifacts/13094063839) independently downloaded, complete ZIP SHA-256 **`e42db34baf9e5bb1c5374ac151d6d2ebf2a9862377d2964e6d1d0a058411548b`**, size **244,427 B**, all three members independently ZIP-CRC verified; `calibration-reference.json` has all 1,049 original results. The GitHub REST artifact metadata includes the `sha256:` prefix; the raw Action output did not.
- Original H0/G1 calibration artifact from [run #37231286406](https://github.com/definitely-stable/ChunkShift/actions/runs/37231286406), artifact #11316573406, independently downloaded and checked against previously committed ZIP digest **`1380b35ad6f2d5adf6e78710088ca281622dad96564bafb23fbdfc951ac792bf`**. Its `g1-index.json` contains the H0 physical CSP patch bytes per file.
- Independent, separate Python replay joined all **1,049/1,049 rows** by exact family, base version, target version and path; checked original H0 baseline per-file source SHA, target SHA, target length and patch bytes against this new HDiffPatch result. All keys matched exactly, no duplicates, no missing rows. Recomputed full SHA-bound rows digest **`cd6ed78e209625f9988311b6c47cde695f29dfbb47f50a537975e8b86a9876f7`**, matching the original in-run audit. ZIP and source digest checks all pass. This is stronger matched **report** evidence, **not** independent binary reapplication of discarded HDiff patches.

The compact machine ledger is [`benchmarks/experiments/patch-gap-001-hdiff-calibration-20261101.json`](../../../benchmarks/experiments/patch-gap-001-hdiff-calibration-20261101.json). The source artifact remains available under GitHub's **90-day retention**, not indefinitely. Its exact ZIP ID/SHA and original H0 ZIP digest are in the durable ledger.

## Original calibration — matched physical patch bytes

| Original cohort | Changed files | H0 CSP v1 | HDiffPatch memory | HDiffPatch stream |
| --- | ---: | ---: | ---: | ---: |
| ASP.NET Windows x64 10.0.10→10.0.11 | 334 | 4,331,507 | 2,857,597 | 7,731,992 |
| ASP.NET Windows x64 10.0.11→10.0.12 | 335 | 2,993,371 | 2,306,769 | 4,628,979 |
| .NET Runtime Linux ARM64 10.0.10→10.0.11 | 190 | 2,765,693 | 1,598,697 | 5,727,390 |
| .NET Runtime Linux ARM64 10.0.11→10.0.12 | 190 | 1,769,703 | 1,136,685 | 3,338,593 |
| **Total** | **1,049** | **11,860,274** | **7,899,748** | **21,426,954** |

On the frozen *calibration only*, memory saves **3,960,526 B / 33.393208%** versus matched H0; stream increases physical bytes by **9,566,680 B / 80.661543%**. **Memory beats H0 in 1,049/1,049 files.** Stream beats H0 in **829**, ties in **2**, and loses in **218**, demonstrating why file-win count does not replace whole-split physical bytes. By family memory savings: ASP.NET **29.495536%**, Linux ARM64 **39.688133%**. The largest five individual memory savings contribute **1,851,033 B**, about **46.74%** of the total 3,960,526 B calibration gain, led by System.Private.CoreLib and coreclr/libcoreclr variants; this is concentration, not a generalization claim.

## Validity bounds, next gate

1. This is whole-file **reference** compression by a distinct algorithm, *not* a bounded one-factor CSP format relaxation. **The 15% CSP vNext RFC gate is not applicable.** No persisted CSP v1/vNext or default/decoder change is justified by a reference-only calibration win.
2. HDiff memory creation is all-in-memory matching, and this run does not provide matched CSP vs HDiff CPU/RSS/resource profiles or a platform-replicated performance experiment. Per-command process wall times are descriptive only. Apply cache is the frozen `-s-8m`; no implication of end-to-end product update CPU or memory safety.
3. The original archive contains only per-file SHA/size/timing metadata, **not patch binaries**. The producer checked original Apply byte identity; the independent verifier did *not* reconstruct all 2,098 patches independently. No separate sealed statistical holdout was opened.
4. The **844-file fixed evaluation split remains unexecuted**, as does the real pinned Chromium Zucchini reference. The fixed evaluation split is previously observed; it is not independent generalization. Do not change protocol/flags/splits based on this favorable calibration.
5. This evidence is sufficient to **admit planning of the separately pinned HDiff evaluation** after a reviewed `main` merge. A single fixed evaluation run must require this exact calibration artifact SHA/run identity, full frozen population, two unchanged lanes and a separate independently archived result. #183 and #251 stay **OPEN**. Do not rerun the calibration to turn the cosmetic workflow conclusion green; protect against a repeated comment in the workflow.

**Decision:** `CALIBRATION_REFERENCE_MEASURED__REPORT_AUDITED__FROZEN_EVALUATION_PENDING__CSP_V1_UNCHANGED`. Not a full PATCH-GAP-001 closure.
