# PATCH-DICT-001 — bounded composite dictionaries: first-release DEFER

**EvidenceId:** `PATCH-DICT-001/D1B0B-FIRST-RELEASE-DEFER-20261109-001`. **ExperimentId:** `PATCH-DICT-001`. Date: **2026-11-09**. Owner [#222](https://github.com/definitely-stable/ChunkShift/issues/222). Patching research-freeze gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

**First-release disposition: `DEFER` the composite/disjoint-run dictionary selector. Preserve CSP v1, the default production Patching builder, public APIs and existing decoder/apply semantics.** No new source-addressing model, persisted dictionary representation, CSP vNext RFC, CLI compatibility contract or release behavior follows from this study. This is **not** a mathematical impossibility or a universal `REJECT` across unseen binary families.

## Evidence chain and source identity

Research stages retained:

- D0: bounded synthetic structural oracle, [PR #448](https://github.com/definitely-stable/ChunkShift/pull/448), no physical claim.
- D1-A: real zstd frame/dictionary reference ledger, synthetic only, [PR #449](https://github.com/definitely-stable/ChunkShift/pull/449).
- D1-B0: two pinned original .NET calibration files; full CSP and real Apply; **unmatched H0 versus research pipeline for all nonfocus entries**, [PR #450](https://github.com/definitely-stable/ChunkShift/pull/450). The original 21.1× larger lab patch artifact was **not** a causal demonstration that composite was worse than H0; this artifact was not promoted as an algorithm decision.
- D1-B0A: exact H0 byte-replay and one-PAYL focus substitution on the same original calibration, [PR #451](https://github.com/definitely-stable/ChunkShift/pull/451), two files saved 194 B (1.75%) via shorter contiguous references; composite added zero.
- **D1-B0B**: all PAYL entries, nested **four-arm full CSP** calibration, [PR #452](https://github.com/definitely-stable/ChunkShift/pull/452), squash merged `main@0e53525ca78c61a284949adaf15b4ca967f27579`, from exact verified PR head `5dc50dcc25465423afb0b24f474a8d39a1c6f511`. Hosted Linux 24.04 / Windows 2025 specialized contracts **4/4 PASS each**, original locked calibration and independent Python full CSP decoder [run #42653923507](https://github.com/definitely-stable/ChunkShift/actions/runs/42653923507) **SUCCESS**. Full CI, CodeQL, Benchmark lab and Heavy validation **SUCCESS** on that exact PR head before merge.
- Original first-attempt [ZIP artifact #13692115104](https://github.com/definitely-stable/ChunkShift/actions/runs/42653923507/artifacts/13692115104) was independently downloaded and its SHA-256 verified against GitHub artifact digest: `6e594c8cf60a2c95881cc3a35e5cb34da479f1447405032dd76333b1d8fb6860`. Independently enumerated all **17 ZIP members**: physical CSP SHA-256 and lengths, original base/target CSM and base SHA, per-entry ledger sums/record counts verified; each original H0 CSP, base file and CSM matched the prior independently audited D1-B0A artifact byte-for-byte.
- Input `pairs.json` SHA-256: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`. Input originals: official `dotnet-aspnetcore-win-x64` **10.0.10 → 10.0.11**, first **two** qualifying changed files by frozen manifest order. Only previously opened **calibration** files were used, **not** an independently sealed evaluation/holdout.

Retained compact [machine result ledger](../../../benchmarks/experiments/patch-dict-001-d1b0b-first-release-closeout-20261109.json) stores original/manifest/CSP identities, all nine per-entry savings, full-patch byte totals, runner provenance and measurement limitations after ZIP expiration.

## Matched four-arm whole-CSP experiment

The question was explicitly decomposed to avoid conflating **search radius** with **variable number of references**.

1. **H0**: production default, sequential CSP v1, exact byte-identity through `CspReader`→`CspWriter` before any candidate trial.
2. **Exhaustive fixed K=4**: for every target PAYL entry, evaluate every possible start ordinal with exactly four verified whole base-chunk references, bounded by existing 1 MiB dictionary cap; always keep the full original H0 incumbent.
3. **Exhaustive contiguous K=1..4**: same full set of starts and byte/reference cap, strongest candidate selected by **actual physically reserialized whole CSP bytes** with all other PAYL entries stored-byte-identical to the current incumbent.
4. **Composite extra**: non-adjacent 2–4 complete verified occurrences from the previously frozen <=8-ordinal pool, checked on top of all existing variable-K decisions. The composite stage can never increase physical patch bytes.

Each candidate uses the production zstd L19/Prefix encoder with hash/chain caps20, verifies source ChunkId under the CSM's **actual** HashSuite (not an assumed SHA-256 ChunkId), decodes target bytes exactly, and never modifies CSP v1. Every selected **complete** CSP is structurally checked and applied through `CspApplier` with sequential rechunk verification; independent Python decoder reconstructed the pinned exact SHA-256 target for all four arms.

| Original 10.0.10→10.0.11 file | H0 full CSP | fixed K4 all starts | variable K1..4 | composite-extra |
|---|---:|---:|---:|---:|
| `host/fxr/{version}/hostfxr.dll` (5 PAYL) | 5,566 B | 5,566 B | 5,274 B | 5,274 B |
| `shared/Microsoft.AspNetCore.App/{version}/Microsoft.AspNetCore.Components.Endpoints.dll` (4 PAYL) | 5,520 B | 5,520 B | 5,294 B | 5,294 B |
| **Total (2 original files, 9 PAYL)** | **11,086 B** | **11,086 B** | **10,568 B** | **10,568 B** |

**Start-position expansion at fixed K4: 0 B; variable K incremental: 518 B / 11,086 B = 4.67256% smaller full CSP; composite topology incremental: 0 B / 10,568 B = 0%.** Both file cohorts selected *zero* composite-run winners after the complete contiguous K ladder. Eligible trial counts: fixed K4 **30**, variable K **174**, sparse **219** across both files. Variable K improved **seven of nine** existing PAYL entries.

The 518-B saving is exact: **16 fewer ChunkId references × 32 B = 512 B**, plus **6 B smaller real stored zstd frames**. It does **not** show improved compression from disjoint dictionaries. H0 references were `[4,4,3,2,1]` and `[4,4,2,1]`; the seven variable-K winners chose one contiguous base record while two already-one-reference entries were retained.

Actual Apply base **stream** read calls (not disk-sector operations) decreased: `hostfxr.dll` 14→5 and 1,034,481→379,688 B; `Endpoints.dll` 15→8 and 1,014,940→706,384 B. Apply was byte-exact for every tested patch. Single-run process wall/CPU do **not** qualify as performance evidence; no independent peak RSS, process-isolated repeat distribution or real physical storage I/O-amplification gate was measured.

## Decision and limits

The adoption rule of #222 for a new composite representation requires **>=10% total physical CSP reduction over the strongest same-byte-budget contiguous lane on an independently preregistered unopened holdout**, plus apply CPU/wall, base I/O and memory guardrails. Here the incremental composite gain versus the stronger same-budget contiguous lane is **0%** on the small **already-opened** calibration population; the required resource and heldout gates were **not** attempted. **Do not treat this as a failed statistical holdout, or claim that the >=10% gate has been globally disproven.**

For the first public Patching release, there is **no positive evidence justifying a new composite selector or continued holdout expenditure**. Accept **`DEFER / NO_PRODUCTION_ADOPT`**, close #222 as release research accounted for, and mark the #222 line in #251 complete while leaving #251 itself OPEN for its other issues and final architecture synthesis.

The **variable-K contiguous builder** is a separate potential optimization *within the existing CSP v1 wire model*, not the adopted result of #222. In this narrow calibration its total full-patch size improvement was only 4.67%, and 174 eligible candidate evaluations were needed versus 30 fixed-K4 starts; no paired, process-isolated create CPU/RSS/time distribution or cross-family independent decision run supports changing the production default. Treat variable-K as **optional future builder research**, not a first-release blocker or automatic ADOPT.

Future reopening of #222 needs a **new source-locked independent family** and a preregistered minimum-positive-headroom calibration gate against the best matched variable-K contiguous comparator, followed only then by a fresh sealed holdout with real apply-cost gates. No CSP vNext is justified by current evidence.
