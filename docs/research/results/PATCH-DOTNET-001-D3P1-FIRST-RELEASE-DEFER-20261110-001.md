# PATCH-DOTNET-001 — first-release DEFER: CLR/IL normalization is not justified

**EvidenceId:** `PATCH-DOTNET-001/D3P1-FIRST-RELEASE-DEFER-20261110-001`  
**ExperimentId:** `PATCH-DOTNET-001` · owner [#221](https://github.com/definitely-stable/ChunkShift/issues/221) · gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251) · date **2026-11-10**

**First-release disposition: `DEFER / NO_PRODUCTION_ADOPT`.** Preserve the existing CSP v1 format, the default patch builder and public/CLI interfaces. Do not ship managed-metadata canonicalization, a CDN1 inverse sidecar, a new persisted transform type, or a new decoder dependency. This is a **release-scope decision**, not a universal negative result for CLR/IL/R2R-aware delta construction.

## Dependency, implementation and evidence chain

- Generic executable [PATCH-GAP-001/G4](PATCH-GAP-001-G4-EVIDENCE-20261008-001.md) has been evaluated and closed in [#183](https://github.com/definitely-stable/ChunkShift/issues/183). Negative generic branch conversion evidence does not prove CLR-aware transforms cannot work.
- Research prototype [Draft PR #273](https://github.com/definitely-stable/ChunkShift/pull/273) contains strict PE/CLR classification, IL-only TypeRef operand scanning, reversible `CDN1` raw inverse maps and negative tests. **Not merged into main;** D2 full metadata canonicalization, D4 relocation/RVA and D5 ReadyToRun are **not implemented to equivalent decision-grade physical validation**.
- Frozen application corpus and pipeline were constructed in stacked **draft** [#275](https://github.com/definitely-stable/ChunkShift/pull/275), [#276](https://github.com/definitely-stable/ChunkShift/pull/276), [#278](https://github.com/definitely-stable/ChunkShift/pull/278), [#279](https://github.com/definitely-stable/ChunkShift/pull/279), [#280](https://github.com/definitely-stable/ChunkShift/pull/280), [#281](https://github.com/definitely-stable/ChunkShift/pull/281). Those research branches are not production release code; do not merge their stacked code implicitly as part of this decision.
- Original source-locked [D3P1 hosted run #38123812844](https://github.com/definitely-stable/ChunkShift/actions/runs/38123812844) **SUCCESS**, exact source checkout `0067f7c37eda3db2e0c6f58a001f3c99ddae09a5`, 6 SHA-pinned PowerShell 7.5.3→7.5.4 official archives, unchanged calibration `v2-pairs.json` SHA-256 `5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9`. The frozen H0 and scanner evidence were collected in preceding [run #38123052019](https://github.com/definitely-stable/ChunkShift/actions/runs/38123052019). **SDK/Desktop evaluation family stayed sealed**; none of these are independent held-out statistical adoption evidence.
- First original [ZIP artifact #11695974097](https://github.com/definitely-stable/ChunkShift/actions/runs/38123812844/artifacts/11695974097) is unexpired as of this report, GitHub SHA-256 digest `f27a98829dc0abc3c7dac03e32cd9b383bae8e7219b78d38bb62dbffea8ce729`. On **2026-11-10**, the ZIP was downloaded again and independently checked: **126 ZIP members** (123 binary `.d3p`, two JSON results, one `SHA256SUMS`); **all 125/125 SHA256SUMS** matched their exact member bytes; **123/123 binary envelope SHA-256** matched the corresponding original row, had magic `D3P1`, version `1`, reserved `0`, 24-byte little-endian header, exact declared embedded CSP and inverse-map lengths, `CSP1` and `CDN1` member magic and source target SHA evidence consistency. Recomputed all **1,242** D0/selection totals independently from archived row records. The hosted pilot itself had already successfully applied normalized CSP and independently inverted target bytes. This offline audit confirms envelope integrity/framing and accounting; **it cannot independently re-run the Apply without source content**, so the underlying hosted reconstruction is a separately stated claim.
- The original specialized [D3P1 physical evidence report on research branch](https://github.com/definitely-stable/ChunkShift/blob/research/patch-dotnet-001-d3-physical-pilot/docs/research/results/PATCH-DOTNET-001-D3P1-PHYSICAL-20261011-001.md) records original per-pair details and original preregistration. The compact [committed closeout ledger](../../../benchmarks/experiments/patch-dotnet-001-d3p1-first-release-closeout-20261110.json) retains immutable source/digest references, exact byte conservation and caveats even if the GitHub artifact later expires.

## Original physical costs

Every changed file is in the denominator, including files not eligible for the transform. The three PowerShell deployment variants are **correlated distributions of one product update**, not independent software families.

| Calibration deployment | Changed files | H0 physical CSP | Selected min(H0,D3P1) | Candidate benefit |
|---|---:|---:|---:|---:|
| Linux x64 framework-dependent | 263 | 1,922,166 B | 1,922,166 B | 0 B |
| Windows x64 framework-dependent | 282 | 2,045,059 B | 2,045,059 B | 0 B |
| Windows x64 self-contained | 697 | 12,062,127 B | 12,062,127 B | 0 B |
| **TOTAL** | **1,242** | **16,029,352 B** | **16,029,352 B** | **0.0000%** |

Strict TypeRef scanner + reverse reconstruction succeeded on **123** source pairs. Exactly **123 full physical CSP candidate envelopes** were built, re-applied and inverse-restored on the hosted runner, with zero unhandled original-target SHA reconstruction failures. H0 fallback selected **0 candidates**, because every **complete** envelope was larger than its original H0.

The verified candidate accounting is:

| Component among 123 candidates | Physical bytes |
|---|---:|
| H0 on the same 123 files | 2,172,190 |
| Complete CSP over normalized base/target | 2,162,832 |
| Extra target inverse map (raw CDN1) | 373,884 |
| Outer D3P1 envelope headers (123 × 24 B) | 2,952 |
| **Complete D3P1 candidate envelopes** | **2,539,668** |

`2,162,832 + 373,884 + 2,952 = 2,539,668`. Across all 1,242 files, using D3P1 unconditionally on the 123 candidates and H0 for the remainder costs `16,029,352 − 2,172,190 + 2,539,668 = 16,396,830` B: **+367,478 B / +2.2925% worse than H0**. With physical per-file fallback, all 1,242 stay at H0, **0 selected savings**.

Only **four of 123** candidate normalized CSP members are smaller than original H0 **even before any inverse bytes**; all positive decreases together sum to **9,358 B**, or **0.05838%** of the full H0 denominator. For these *specific observed candidate CSP bytes and frozen encoder/grammar only*, even a hypothetical free inverse map/outer envelope would not reach the proposed >=5% corpus-level gate. This is **not** an upper bound on alternative D2/D4/D5 transforms, a different encoder/search policy or independent product versions.

## Decision and remaining unknowns

1. **STOP/REJECT this fixed D3P1 TypeRef-only/CDATA map variant** on the pinned PowerShell calibration: both actual physical overhead and the observed-CSP conditional bound rule out a worthwhile patch-byte win. Do not expend the sealed holdout for this unchanged candidate.
2. **DEFER all remaining .NET-specific representation work for the first release**. D2 metadata-index/heap canonicalization, D4 RVA/relocations, D5 ReadyToRun, non-TypeRef IL tokens, true NativeAOT controls, and independently selected application/NuGet families remain **unmeasured**. With no positive calibration pilot or versioned reversible representation ready, there is no evidence to authorize a CSP vNext, a public transform contract, apply dependency or initial-release blocker. This deferral is a prioritization, not an empirical verdict for untested representations.
3. Keep existing generic CSP v1 with no CLR-specific changes. Do not merge the research prototype stacks or ship a CDN1 sidecar.
4. A future reopening of #221 requires a **new preregistered token/RVA/R2R coverage-and-cost necessary gate**, source-locked calibration families independent of PowerShell, independently reversible bytes and *fully charged physical CSP* pilot. Only if those produce positive evidence, preregister separate sealed evaluation and Create/Apply wall/CPU/RSS/byte gates. Avoid reuse of previously opened SDK/Desktop membership as a fresh holdout.
5. Record #221 as research **first-release accounted for**, close it as `completed` **after** this evidence PR passes CI and merges, then mark its checkbox in #251. **#251 itself remains OPEN** for #223, #224, #225 and final architecture synthesis. Do not freeze PublicAPI or publish NuGet before #251 exits.

The result does not establish patent novelty, wide population effects, or a new file format. There is no change to `src/`, package defaults, CSP schema, decoder, `ChunkingProfileId`, or public API.
