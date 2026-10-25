# PATCH-TREE-001 / T3 — first-release admission closeout

**DecisionId:** `PATCH-TREE-001/T3-CLOSEOUT-20261025-001`  
**ExperimentId:** `PATCH-TREE-001`, owner [#184](https://github.com/definitely-stable/ChunkShift/issues/184), freeze owner [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Date:** 2026-10-25. **Decision-only**: no new source-materialization benchmark or newly opened holdout.  
**Decision:** **DEFER T3 for the first production update-set path / retain T0 same-path per-file baseline / do not enable production H3 / NO_PRODUCTION_ADOPT**.

This is the closeout of the **original-calibration T3 program and current admission rule**, not a universal impossibility result for cross-file reuse. The two audited renamed DLLs can benefit very substantially in bytes but fail the already-frozen 3× create-CPU and create-wall gate. The laboratory label `FALLBACK_T0_OR_T0S__CREATE_BUDGET` is a *recommended choice*, **not** a fallback selector deployed in the shipping CLI/SDK. The existing SDK creates exact CSP v1 on caller-selected base/target; it does not implement tree rename discovery, trusted base locators, host-owned publication or this release decision policy.

## 1. Scope, source locks and evidence authority

This record reconciles existing independent evidence; it **does not run a new timing experiment**. The frozen four-pair .NET original calibration is identified by `original-pairs.json` SHA-256 `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd` **(see note below: canonical actual SHA is `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd`; the reference used by the checks is the 64-hex value in the source ledger)**.

**Correction and canonical original identity:** `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd` is copied into this decision from the retained source-lock records; the executable ledger is the authority for its exact bytes. Only the two preselected ASP.NET Core win-x64 `mscordaccore` changed-renamed transitions were actually run through T3. The .NET Runtime Linux ARM64 pair inventory contributed to exact-added-path census, **not** to the two T3 cost admissions. No sealed Node/tzdata/ChunkShift-source holdout was opened.

| Prior evidence | Authoritative recorded observation | Inference boundary |
| --- | --- | --- |
| [T3-B0 exact-added census](PATCH-TREE-001-T3B0-ORIGINAL-CENSUS-EVIDENCE-20261023-001.md), original run [#39915730705](https://github.com/definitely-stable/ChunkShift/actions/runs/39915730705) | All 4 original .NET pairs; 2 added files, **0** equal whole-old-file candidates, **0 B** net exact-added-path headroom | Not a claim about partial, renamed/changed or non-.NET reuse |
| [T3-B1 original physical CSP](PATCH-TREE-001-T3B1-ORIGINAL-PHYSICAL-EVIDENCE-20261023-001.md), original run [#39967460136](https://github.com/definitely-stable/ChunkShift/actions/runs/39967460136) | Source-locked two renamed files: real CSP create/apply; T3 warm **92,671 B** vs actual two-file compressed T0S **1,097,577 B** | Not a complete production/signed tree update-set |
| [T3-B2A structural source selection](PATCH-TREE-001-T3B2A-ORIGINAL-STRUCTURAL-EVIDENCE-20261023-001.md), original run [#39977481690](https://github.com/definitely-stable/ChunkShift/actions/runs/39977481690) | Conservatively recovered **both** source identities with no ambiguity in four calibration pairs | Name/length/path matching does **not** authorize a source or demonstrate general recall |
| [T3-B2B repeated original admission](PATCH-TREE-001-T3B2B-ORIGINAL-REPEAT-CREATE-EVIDENCE-20261024-001.md), original run [#40078373627](https://github.com/definitely-stable/ChunkShift/actions/runs/40078373627) | Five paired measured rounds per lane; both changed-rename pairs fail 3× **CPU and wall** gates | No regression/throughput inference across hosts, no safe peak-private-RSS claim |
| [T3-B2C1 controlled H2/H3](PATCH-TREE-001-T3B2C1-ORIGINAL-H2H3-EVIDENCE-20261025-001.md), original run [#40191549066](https://github.com/definitely-stable/ChunkShift/actions/runs/40191549066) | H3 reduces **logical Stream** old-source reads 96.25%/90.66%; H3 CPU improves on only **one** of two original pairs | Stream read count is NOT physical disk I/O; H3 is not accepted as CPU default |
| [T3-B2D0 stage attribution](PATCH-TREE-001-T3B2D0-ORIGINAL-STAGE-EVIDENCE-20261025-001.md), original run [#40194727803](https://github.com/definitely-stable/ChunkShift/actions/runs/40194727803) | 136/57 dictionary zstd trials; **92.10%/87.32% of four measured worker-elapsed stage groups** | Summed Stopwatch worker elapsed is NOT process CPU, guaranteed avoidable work or a safe build-time bound |
| [T4-C3A publisher authority](PATCH-TREE-001-T4C3-PUBLISHER-BOUNDARY-DECISION-20261022-001.md) | Architecture **ADOPT host-owned publisher**, **REJECT** identity preflight as authorization, **DEFER** first-party OS publisher | No adopted atomic/durable/security-qualified installer; [accepted RFC-0006](../../architecture/RFC-0006-patching-distribution-trust-boundary.md) is authoritative |

**Evidence integrity:** each quantitative run above was separately source-locked and (where specified) independently checked against its original GitHub artifact. This *closeout* repeats no timing measurements. The source of the numerical cost admission is B2B original artifact **#12553937250**, ZIP SHA-256 `9b0562b888952958b1e90ab1d2808f2c12e40d722628095b0127a54870375568`. B2C1 original ZIP SHA `554944ebbec4e69d6b26f88bca6fcf6bf1faa0dc6d999bf0be2f03b45a280efc`; B2D0 original ZIP SHA `cc8aeb691d6c1cbdd93db008027d7b12d6f2a606633a623f7f38ef7812f2ddcb`.

The machine-readable [decision ledger](../../../benchmarks/experiments/patch-tree-001-t3-closeout-20261025.json) and independently implemented [admission auditor](../../../benchmarks/scripts/verify_patch_tree_001_t3_closeout.py) recompute the frozen gates, replay existing 16 B2C1 and 24 B2D0 retained samples, and optionally replay the original B2B ZIP via its pre-existing independent standard-library oracle. They do **not** reconstruct absent raw source binaries or replace a physical T0/T3 multi-file run.

## 2. Complete, explicit pair-level cost gate

The original B2B preregistration requires:

`T3_warm < 0.90 * min(raw_target, T0S_physical)`, `T3_cold <= min(raw_target, T0S_physical)`, AND **both** `T3_CPU / T0S_CPU <= 3` and `T3_wall / T0S_wall <= 3`, per eligible pair. Same-version **T0** has no old same-path file for these changed-renames, so the strong *physical compressed control* is freshly built **T0S**, not raw T0 or the warm full-tree T0 aggregate.

| Metric | Original 10.0.10→10.0.11 | Original 10.0.11→10.0.12 |
| --- | ---: | ---: |
| T0S CSP physical bytes | 548,714 | 548,863 |
| T3 CSP bytes | 86,432 | 5,649 |
| Charged diagnostic path map | 295 | 295 |
| **T3 warm (CSP+map)** | **86,727** | **5,944** |
| Old base CSM physical (cold charge) | 1,114 | 1,114 |
| **T3 cold** | **87,841** | **7,058** |
| T0S process create CPU median | 308.941 ms | 259.708 ms |
| T3 process create CPU median | 3,614.386 ms | 1,088.596 ms |
| **T3/T0S CPU** | **11.699× — FAIL** | **4.192× — FAIL** |
| T0S wall median | 133.935 ms | 128.814 ms |
| T3 wall median | 1,740.003 ms | 549.607 ms |
| **T3/T0S wall** | **12.991× — FAIL** | **4.267× — FAIL** |
| Admission | `FALLBACK_T0_OR_T0S__CREATE_BUDGET` | `FALLBACK_T0_OR_T0S__CREATE_BUDGET` |

**Conservation of two-file physical costs:** T0S = **1,097,577 B**; T3 warm = **92,671 B**; T3 cold = **94,899 B**. Warm two-file saving against *measured T0S* = **1,004,906 B (91.56%)**. Both pairs **pass** the size condition but **fail** the unchanged create-CPU and create-wall thresholds.

**Different denominator — do not conflate it:** the four-original-pair complete *modelled* T0 warm tree aggregate is **14,758,982 B**; the T3-B1 two-file substitution models **12,138,381 B** (2,620,601 B less). This is **not** a newly materialized, signed, end-to-end physical T3 update-set, does not include a real authoritative base selector or installation metadata, and cannot alone justify the original issue's ≥10% complete-update admission. An actual complete update-set would require consistent target population and a charged, cryptographically bound tree manifest and source locator.

B2C1 subsequent CPU ratio H3/H2 = **1.008729 / 0.927600**, so H3 fails its preregistered *both-pairs CPU gain* test even while logical read amplification vanishes. D0 shows numerous expensive zstd L19 dictionary trials, **not** a tested new candidate policy that preserves size or wins create CPU. Do not combine CPU medians from B2B, B2C1 and D0 as paired apples-to-apples speedups.

## 3. Release-scope decision by sublane

| Lane / proposed mechanism | First-release disposition | Reason |
| --- | --- | --- |
| T0 existing per-file CSP with same-path base; T0S compressed self-contained for added paths | **RETAIN baseline (not new feature ADOPT)** | Existing frozen CSP v1 semantics; caller still owns artifact choice and trust |
| T3-A exact copied/moved newly added file | **REJECT promotion on this frozen calibration** | B0 found zero exact whole-old-file candidates; synthetic mechanics alone cannot justify a product policy |
| T3-B changed-rename source selection + base-bound CSP | **DEFER production integration** | Genuine two-DLL byte win, but both B2B create budgets fail; corpus limited; locator/authentication/update-set unknown |
| Existing H3 bounded candidate cache as default | **REJECT CPU-gain promotion for T3 originals** | Original H3 does not improve process CPU on both pairs; no global default change |
| More L19 candidate ranking/early exits in same exhausted universe | **Do not automatically reopen** | PATCH-ENC-005 / #181 previously REJECTED; timing attribution is not proof of selection headroom |
| First-party tree selector/trust/publisher | **DEFER** | Accepted RFC-0006 and T4-C3A keep installation/authenticity/policy with the consuming host |
| T2 whole-tree CSP + target layout | **OPEN; not studied to an acceptance verdict here** | Needs distinct original whole-set admission with all bytes, resource/trust semantics and exact full-tree reconstruction |

**Product meaning:** no new automatic T3 rename selector, patch mode, trusted locator, distribution descriptor, update-set public API, publisher or production fallback has been implemented or authorized. Only the **research decision** to stop promoting T3 under current original calibration is closed.

## 4. Boundary between patch format, source trust and installed tree

- The CSP v1 `BASE` ManifestId verifies a specifically supplied base content/manifests; it does **not** grant access to an arbitrary removed-old-tree file by path, nor prove authorized publisher intent. The external host must authenticate the selected source, CSP physical hash/length, target identity and release scope per RFC-0006.
- A structural rename/matching oracle is merely a **candidate generator**, not an authority. It cannot authorize reading old content under a mutable path or prove a remote/locally selected tree generation is permitted.
- No full-tree manifest wire serialization, cross-platform file modes/symlinks/empty-directories policy, case-fold collision rule or canonical target tree identity is accepted in this closeout. The Phase-A restricted regular-file inventory is not a shipping tree schema.
- A successful sequence of per-file CSP applies does **not** imply tree publication, atomicity, durability or rollback safety. T4-C3A keeps construction, staging, generation pins, selection, crash recovery and protected namespace authorization in the host.
- T0 per-file CSP remains an **engineering cost baseline**. Hosts may compose file patching themselves subject to their own security and installation semantics, but ChunkShift must not claim that it already ships a signed, crash-atomic updater.

## 5. Explicit closeout / reopen conditions

This slice is complete once this decision, machine admission ledger, negative tests and hosted CI replay are committed to `main`, with exact source/evidence IDs linked in #184 and #251. It has **no new measurement RunId**. Mark only the **T3 original calibration** `DEFER`, leave #184 and #251 **OPEN**.

T3 may be reconsidered only if a **new, preregistered** population or representation family offers credible evidence that the create CPU/wall admission can be met **without sacrificing the physical savings**, with clear holdout protection, proper raw vs compressed control, bounded apply peak-private resident memory, actual complete tree artifact/reconstruction, charge for any base CSM/locator, authenticated source bindings and host publication assumptions. New knobs merely tuned on these two original pairs do not qualify. A different explicitly accepted CPU SLA would require a separate product decision—not silently changing this frozen 3× gate.

**Next allowed bounded step under #184:** T2 whole-tree feasibility/admission protocol with a deterministic exact regular-file population and matched T0 comparator, before any held-out data is opened. Reject attempts to infer T2 compression ratios from T3, B2C1 or naive T1. Freeze full old/new layout identities, CSM and source locator limits (4,194,304 base records / apply distinct IDs), path-to-offset/length reconstruction mapping, **all** added/removed/unchanged files, physical overhead and receiver memory. Run a source-locked original calibration and independent physical audit; only then decide whether a larger T2 experiment/format RFC is warranted. Keep RFC-0006/T4-C3A host-owned publisher boundary unchanged. If T2 lacks a finite safe apply envelope or practical complete-tree savings, record `DEFER/REJECT` and proceed to a Patching Research Freeze synthesis rather than inventing a shipping update-set format.

**No opening of #140 / PublicAPI freeze / release or publication-repository port under this decision.**
