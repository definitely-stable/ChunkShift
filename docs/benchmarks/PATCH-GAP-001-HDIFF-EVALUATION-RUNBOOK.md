# PATCH-GAP-001 — fixed original HDiffPatch evaluation execution gate

**Date:** 2026-11-04. Owner [#183](https://github.com/definitely-stable/ChunkShift/issues/183), release research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251). **PREDECLARED EXECUTION CONTRACT ONLY / EVALUATION NOT RUN UNTIL HOSTED MAIN JOB / NO CSP PRODUCTION CHANGE**.

## Original unretuned frozen reference

The first original [1,049-file calibration report](../research/results/PATCH-GAP-001-HDIFF-CALIBRATION-EVIDENCE-20261101-001.md) has independently matched file identities and H0 reference bytes. Its GitHub-hosted [run #41333408342](https://github.com/definitely-stable/ChunkShift/actions/runs/41333408342), attempt 1, produced real valid measurements and a SHA-verified artifact #13094063839 even though the *last reporting-only step* failed. That has been permanently documented; **do not rerun calibration**.

Frozen fixed evaluation: 844 changed same-path file pairs, families **node-linux-x64 (531), node-win-x64 (283), tzdata (28), chunkshift-source (2)**. The original matched CSP H0 reference is a **separately archived authentic original** [G1 evaluation run #37432365043](https://github.com/definitely-stable/ChunkShift/actions/runs/37432365043), artifact #11409716915, ZIP SHA-256 `dc2a725f1612556f711d07b819f3de17d3b887bcd2e38a598eb557f9ae3ed2b0`, original `g1-index.json` SHA `73be4009cda2e8f4af6a302f4863be04039b2ae327379bfb582eed91fe66a953`, anchor **26,363,364 H0 CSP physical bytes**. This is a previously observed fixed evaluation partition, **not a fresh sealed statistical holdout**.

**No parameter choice from calibration**: both source-pinned HDiffPatch v5.1.3 `memory -m-4` and `stream -s-64` use the same frozen `-SD -d -f -p-1 -c-zstd-21-24` and apply `hpatchz -s-8m -f`. Every patch is a full separate real CLI execution, exact target SHA after apply required. No candidate is excluded for unfavorable size. The original `pairs.json` SHA is `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.

## Dispatch, single execution and provenance

Following squash merge of the workflow **after all required PR CI passes**, the authenticated GitHub user ID `271974079` can create a new issue comment on #183 with **only**:

`/hdiff-evaluation YYYYMMDD`

Issue-comment workflow `.github/workflows/patch-gap-001-hdiff-evaluation.yml` refuses all other users/issues, invalid date/command, non-current `main`, dirty or retried checkout and previous completed/active actual evaluation attempt. Its operator can never select a custom branch, corpus, algorithm flags or file subset.

The hosted job must first inspect the exact archived calibration provenance: actual first attempt jobs/steps show successful **measurement, original report audit and artifact upload**; the final-only reporting-step failure is a documented *exception*. It checks the original SHA-256 artifact digest and exact prior run head before allowing the evaluation. It must independently fetch the original frozen H0 ZIP using a read-only GitHub Actions token and verify the exact published ZIP and nested index SHA-256. GitHub-hosted materialization must pass the immutable full corpus lock before and after evaluation.

The independent report validator cross-joins each of the 844 rows against **both** original `pairs.json` and the separately preserved authentic H0 index, matching `family/base/target/path/base SHA/target SHA/target bytes`, checks both exact native flags, SHA/reconstruction claims, finite timings and patch-byte totals, and independently recomputes fixed whole-population and per-family sums. No zero-byte stand-in is allowed. PR CI uses synthetic negative/contract tests only. Successful report audit must be followed by GitHub Actions artifact upload, its original hash, separately retained compact evidence and a review/decision PR. No experiment numbers are claimed merely because a workflow/test exists.

## Interpretation limits

The external whole-file reference **cannot** directly satisfy the CSP-factor 15% RFC threshold. The runner records its observed aggregate difference to H0 as **descriptive**; do not mutate the CSP format/default, invent an RFC gate, or close #183/#251. Apply correctness is proven by the original producer; report-level independent audit does not replay ephemeral patch binaries, does not prove memory-cost equivalence or bound CPU/RSS and does not establish out-of-corpus generalization. Real Zucchini still requires an independent explicit resource decision. Preserve once-only evaluation even if an archive/reporting step subsequently fails: archive any artifacts and treat later evidence reconstruction separately, not as permission to re-run favorable cases.
