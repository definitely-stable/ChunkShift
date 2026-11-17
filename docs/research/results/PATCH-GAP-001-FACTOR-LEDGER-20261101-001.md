# PATCH-GAP-001 — F0 frozen factor-ledger checkpoint (not final closure)

**Date:** 2026-11-01. **Owner:** #183, release research gate #251. **Status: FACTOR_SYNTHESIS_READY / REFERENCES_UNRESOLVED / NO_CSP_VNEXT_RFC_FROM_TESTED_SINGLE_FACTORS.** This is not a claim that all references or #183 are complete.

## Purpose

Independently reconcile the already-accepted, source-pinned G1/G2/G3/G4/G5 findings using the original committed machine-readable result files. The one-factor frozen protocol is [PATCH-GAP-001-PROTOCOL.md](../../benchmarks/PATCH-GAP-001-PROTOCOL.md) at `5372678ae8451a71cc95eb24f30855cbbd7e0633`. The corpus lock is `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`; calibration H0 = **11,860,274 B**, evaluation H0 = **26,363,364 B**. G2's separate producer protocol is `96fd9b296d6998cac397e61041f22df51e6dd43c`.

This checkpoint adds **no new reference or performance measurements**. It does not retune/redispatch an evaluation and cannot change CSP v1, default D15, public API, build/packaging or publication.

## Exact frozen factor table

Each G1/G3/G4 result is whole-split, including H0 bytes for ineligible files. Savings are `H0 - lane`; negative savings mean **regression**. Threshold is at least **15%** whole-evaluation savings with independent bounded apply costs before an RFC candidate.

| Factor and frozen evaluation lane | H0 bytes | Candidate bytes | Savings bytes | Savings vs H0 | Status |
| --- | ---: | ---: | ---: | ---: | --- |
| G1-B1-R64 | 26,363,364 | 26,020,421 | 342,943 | 1.300832% | REJECT |
| G1-B4-R256 | 26,363,364 | 25,789,917 | 573,447 | 2.175166% | REJECT |
| G1-B8-R512 | 26,363,364 | 25,763,908 | 599,456 | **2.273822%** | REJECT |
| G1-B32-R2048 | 26,363,364 | 25,763,908 | 599,456 | **2.273822%** | REJECT |
| G3-RUN | 26,363,364 | 75,922,335 | −49,558,971 | **−187.984246%** | REJECT |
| G3-FILE | 26,363,364 | 79,825,697 | −53,462,333 | **−202.790255%** | REJECT |
| G4-BCJ | 26,363,364 | 26,111,524 | 251,840 | 0.955265% | REJECT |

**G1 cost:** best reduction saturates at B8; G1-B32 extends the frozen search to **88.491273×** H0 create-side base reads, with only 7/844 evaluation files improving. B32 adds no further reduction.

**G2 candidate search (different protocol and scope):** frozen 256-row *calibration-only* whole-base oracle saved **174 B** (974,223 B H0 versus 974,049 B oracle) and missed its entry gate. The independently executed H6-O evaluation guard emitted **30,248,709 B** versus H0 **26,363,364 B** (14.7377% larger), with verdict `MISS / STOP_RESEMBLANCE`. No unexecuted H5-F/H6-P/H8 or full-evaluation oracle numbers are invented. Source: [#181](https://github.com/definitely-stable/ChunkShift/issues/181), [G2 oracle](data/PATCH-ENC-005-G2-EVIDENCE-20261003-001/verdict.json), [H6-O guard](data/PATCH-ENC-005-H6O-EVIDENCE-20261004-001/verdict.json).

**G5 Puffin:** `NOT_PRESENT` on the **frozen materialized changed-file corpus**. Structural inventory found zero supported compressed pairs in all 1,049 calibration and 844 evaluation file rows. No deflate-aware size score is inferred; no synthetic corpus is added. Source: [Stage-A inventory](PATCH-GAP-001-EVIDENCE-20261003-001.md).

## Machine-verifiable oracles

Source: [G1 evaluation](data/PATCH-GAP-001-G1-EVALUATION-EVIDENCE-20261006-001/evaluation.json), [G3 evaluation](data/PATCH-GAP-001-G3-EVALUATION-EVIDENCE-20261007-001/evaluation.json), [G4 BCJ](data/PATCH-GAP-001-G4-EVIDENCE-20261008-001/summary.json), [G5 inventory](data/PATCH-GAP-001-20261003-001/aggregates.json).

`python3 benchmarks/scripts/assemble_patch_gap_factor_ledger.py --output /tmp/patch-gap-001-factor-ledger.json` reopens the committed inputs; verifies exact protocol/corpus fingerprints; verifies family/subset/file counts and H0 anchor; recomputes each savings number and the 15% threshold; enforces nested G1 monotonicity; checks calibrated G2 selector failure and positive H6-O patch-byte overhead; and keeps missing external reference evidence **explicitly incomplete**. Tampering with lane totals, subset or G5 population, decision state or producer fingerprint fails closed in unit tests. This is an *independent aggregate consistency check*, not a replacement for the already archived detailed per-file recompute scripts.

## Decision and non-composition

The tested, frozen, **CSP-costed single-factor** lanes do not clear the #183 RFC threshold. Accordingly **no CSP vNext change is justified by these lanes**. This statement is strictly narrower than declaring every possible codec/algorithm inferior. Rejected factors cannot be added together to synthesize an imaginary winning interaction; no interaction trial is manufactured. If an interaction is later proposed it needs its own predeclared experiment identity and gate.

**Two important missing reference inputs:**

1. **Zucchini** (G4 §8.3): only source-pin, hosted-build and sealed calibration/evaluation **contract** workflows and synthetic CLI tests are in committed evidence. No authentic pinned binary build + real frozen 1,049-file calibration + independent 814-file evaluation result is presently recorded in the research tree. Whole-file Zucchini bytes, if measured, belong **only** to supported same-file H0 subset attribution: **not** the 15%-CSP-factor gate. Real pinned Chromium needs a configured GitHub-hosted larger runner with at least 100 GiB measured free disk and 8 GiB RAM; existence for the organization is not established by a green PR contract test.
2. **HDiffPatch** independent comparative reference: no committed final, source-locked per-file matched reference table is included in this factor ledger; **NOT_VERIFIED**, not a fabricated zero gain.

Until those reference requirements receive actual evidence or an explicit, separately reviewed feasibility/defer decision consistent with the frozen protocol, **#183 remains OPEN**. This checkpoint cannot mark the #251 PATCH-GAP checkbox, cannot authorize the final CSP v1 versus vNext freeze, and cannot port or release Patching.

**Next permitted action:** inspect GitHub-hosted larger-runner availability and pinned Chromium binary-build feasibility **without self-hosted execution**. If truly unavailable, document a principled reference-only DEFER and ensure no result/absence is presented as a measured patch-byte outcome. Consolidate the independent references and make the #183 final decision only after that limitation is accepted.
