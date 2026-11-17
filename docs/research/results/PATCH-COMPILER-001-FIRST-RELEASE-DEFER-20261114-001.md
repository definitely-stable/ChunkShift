# PATCH-COMPILER-001 — first-release representation compiler entry-gate DEFER

**EvidenceId:** `PATCH-COMPILER-001/FIRST-RELEASE-DEFER-20261114-001`. **ExperimentId:** `PATCH-COMPILER-001`. Date: 2026-11-14. Owner [#224](https://github.com/definitely-stable/ChunkShift/issues/224); first-release research gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

**Disposition: `DEFER / NO_PRODUCTION_ADOPT` for the first public Patching release.** Preserve the existing per-file CSP v1 representation and its existing bounded raw/zstd/dictionary selection. Do not introduce an online multi-objective compiler, BuildPolicy, additional format, decoder, metadata identity, public weighting API or production default change. Proposed [RFC-0005](../../architecture/RFC-0005-patch-compiler-architecture.md) remains **Proposed**, not an accepted production contract.

## Exact decision boundary

Issue #224's **Phase B entry gate** is at least **two distinct representation families with decision-grade, applicable evidence of a system-level tradeoff that the existing local physical-byte selector cannot express**. This is a **necessary gate**, not merely a preference. Issue #251 explicitly permits first-release **DEFER** of #224 if that entry gate is not met.

The currently qualified first-release persisted representation is **one**: CSP v1, with its existing raw/zstd/dictionary encoding choices. These choices and exact reuse do **not** count as multiple newly qualified representation families for #224. There are **zero independently qualified additional first-release representation families** with a safely selectable, cost-admitted multi-objective alternative. Hence the required two-family entry gate is **NOT_MET**. This record makes no claim that offline Pareto optimization is intrinsically impossible; Phase A cross-family replay and Phase B online planner were **not run** and are **not represented as negative experiments**.

## Reconciliation of original source-bound results

All numbers below are quotations of already archived results in their original distinct cohorts; **they are not a newly pooled sample, a causal meta-analysis or comparable denominators across the studies**.

| Source | Actual measured result | Why it does not qualify an additional first-release family |
| --- | --- | --- |
| [PATCH-GAP-001 closeout](PATCH-GAP-001-FIRST-RELEASE-CLOSEOUT-20261104-001.md), #183 | H0 26,363,364 B; G1 best 25,763,908 B (2.273822% saving); G4-BCJ 26,111,524 B (0.955265% saving); G3 RUN 75,922,335 B; original Zucchini reference not run because hosted disk admission failed | Tested CSP factors missed their preregistered 15% representation-RFC gate; no new persisted candidate was admitted |
| [HDiffPatch fixed evaluation](PATCH-GAP-001-HDIFF-EVALUATION-EVIDENCE-20261104-001.md), #183 | External whole-file HDiffPatch memory 11,005,397 B vs H0 26,363,364 B; streaming 33,207,126 B vs H0; exact reconstruction | A **real external reference tradeoff**, but neither an adopted CSP candidate nor a validated builder/apply/transport policy under equivalent cost and trust constraints. It is not an admitted new CSP family |
| [PATCH-TREE-001 closeout](PATCH-TREE-001-FIRST-RELEASE-CLOSEOUT-20261101-001.md), #184 | Restricted T2 warm 10,828,279 vs T0 14,758,982 B (26.63% saving), but 60-s create gate failed 2/4 pairs (76.246 s, 69.693 s). Two changed-renamed DLL T3 warm 92,671 vs T0S 1,097,577 B, but CPU ratios 11.699× / 4.192× exceed 3× | Useful size headroom under restricted prototypes, **first-release T2/T3 DEFER**; neither is an admitted portable per-file/update-set representation |
| [PATCH-DICT-001 closeout](PATCH-DICT-001-D1B0B-FIRST-RELEASE-DEFER-20261109-001.md), #222 | On two already-opened real files, variable contiguous K1..4: 10,568 B vs H0 11,086 B (4.67% saving); disjoint composite: **zero** incremental bytes beyond variable-K | Existing CSP v1 reference semantics; no new admitted representation or heldout apply-cost gate |
| [PATCH-DOTNET-001 closeout](PATCH-DOTNET-001-D3P1-FIRST-RELEASE-DEFER-20261110-001.md), #221 | D3P1 TypeRef-only: H0 16,029,352 B, unconditional variant 16,396,830 B; choosing per-file minimum saves **0 B**. Other CLR/RVA/R2R families untested | No admitted semantic-normalization candidate; do not generalize to untested variants |
| [PATCH-ML-001 closeout](PATCH-ML-001-FIRST-RELEASE-DEFER-20261110-001.md), #223 | Learned model not trained; G2 oracle improved 174 / 974,223 B; H6-O guard increased original CSP bytes 14.74% | Candidate ranking is build-side search, not a second persisted representation. No material new search universe was demonstrated |
| [PATCH-META-001 closeout](PATCH-META-001-G1B2B-FIRST-RELEASE-DEFER-20261105-001.md), #220 | Verified source-bound codec cost, no safely reusable cache-on/off product adoption evidence | Derived cache is not a new persisted representation |

Machine-readable **source-and-decision ledger**: [PATCH-COMPILER-001 first-release JSON](../../../benchmarks/experiments/patch-compiler-001-first-release-defer-20261114.json). It identifies the frozen source base and documented dispositions; this decision intentionally creates **no RunId, no fabricated new original measurement, no cross-family online score**.

## Why implementing the compiler now would be an architectural regression

1. With only CSP v1 legal for the release, a multi-objective BuildPolicy adds a versioned selector surface without a proven system-level decision it can improve. The #224 acceptance thresholds (20% system-cost improvement with ≤2% wire regression, or 5% wire improvement without >10% apply/request regression) **cannot be evaluated by substituting rejected/unadmitted representations into an offline scoreboard**.
2. HDiff whole-file memory and streaming are genuine opposing tradeoffs. However, integrating an external reference requires separately accepted format/decoder, create/apply CPU/RSS, source authority, and update-product constraints before calling it a legal compiler candidate.
3. T2/T3 show that a smaller artifact can fail a frozen cost gate. Reweighting them post hoc through BuildPolicy would silently override independent predeclared acceptance rules.
4. A speculative BuildPolicy must not become public API, persisted identity, a dynamic machine-load heuristic or a dependency for exact reconstruction. Current stable CSP v1 stays sufficient **for the scoped one-file first-release boundary**, not necessarily for arbitrary future ecosystems.

## Reopening and handoff

Reopen only with an independently owned, preregistered candidate that first passes its own format/apply/resource gates and yields at least two **distinct legally selectable, decision-grade** families with a measured tradeoff. Then run #224 Phase A **offline canonical per-candidate replay**, fixed policy/tie-break/overflow, and only after a material Pareto gain consider Phase B. Do not reuse these 2026-11 outcomes as a fresh sealed holdout.

This report is a **release-scope, source-ledger decision**, not a new benchmark. It settles only #224's explicit necessary gate. #225 local deterministic execution and #251 final CSP v1/compatibility synthesis remain independent tasks; no release, public API freeze or publication handoff is authorized by this report alone.
