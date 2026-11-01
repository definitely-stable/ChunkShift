# PATCH-TREE-001 — first-release research closeout and scope decision

**ExperimentId:** `PATCH-TREE-001`. **DecisionId / EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261101-001`. **Owners:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), release gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).
**Date:** 2026-11-01. **Disposition:** **DEFER tree/update-set representations and publisher for the first Patching release; RETAIN existing per-file CSP v1/T0 reference boundary.**
**Evidence type:** decision-only synthesis of existing frozen original calibration and already accepted architecture. **No new RunId, new original/holdout measurements, new security proof or release authorization.**

## First-release contract

1. The first public `ChunkShift.Patching` contract is **one-file** `PlanAsync / CreateAsync / ApplyAsync`, using existing CSP v1; no change to CSM v1, HashSuite, FastCDC, CSP bytes, decoder semantics or public API is made by this decision.
2. T0 same-path per-file patching remains the **research comparator**, **not** a newly implemented automatic T0/T0S fallback, mandatory source selection, update-set builder or installer. Callers may use the existing independently verified self-contained CSP capability.
3. **DEFER** an owned portable tree manifest/identity, cross-file base locator, rename-aware selector, whole-tree CSP, automatic update-set planner, tree publishing API and OS-native publisher. Nothing under `PatchLab` is a production wire format or security authority.
4. Per-file content-integrity checks are **not** publisher authentication, source authorization, release policy, anti-rollback or a transaction spanning multiple files. Accepted [RFC-0006](../../architecture/RFC-0006-patching-distribution-trust-boundary.md) assigns distribution trust and installation policy to the host. [T4-C3A](PATCH-TREE-001-T4C3-PUBLISHER-BOUNDARY-DECISION-20261022-001.md) also rejects path/inode/FileId preflight as authorization. The host must qualify namespace protection, staging, full-tree verification, reader pins, commit, recovery and durability for its own platforms before advertising atomic update installation.
5. Restricted research tree identity `T4-A` is **not** a production tree ID and does not resolve Unicode, symlink/reparse, modes, ACLs/xattrs, hard links, case folding or trusted release metadata. A successful sequence of per-file applies is **not** an atomic tree update.

## Reconciled original results (no post-hoc gate changes)

| Lane | Original, correctly scoped observation | First-release disposition |
| --- | --- | --- |
| T0 | Four-pair complete warm physical baseline **14,758,982 B**; includes reference target map | **RETAIN cost baseline**, no product selector |
| Naive T1 | Warm **83,593,822 B**, **5.6639×** T0 | **REJECT measured variant** |
| T1/F2 | Archive-only independently replayed **47,002,363 B**, **3.185×** T0; source-lock producer run failed | **NO_ADOPT**, do not mislabel original run as valid |
| T2 | Real restricted regular-file CSP warm **10,828,279 B** vs T0 **14,758,982 B** (**26.632616% saving**); preregistered T2 CREATE wall limit **60 s** failed for **2 of 4** original pairs (**76.246 s**, **69.693 s**) | **DEFER**, despite measured size gain |
| T3-A | **0** exact new-path old-file candidates on four original .NET pairs | **NO_ADOPT for this cohort**, not general impossibility |
| T3-B | Two changed-renamed DLLs: warm **92,671 B** vs measured self-contained T0S **1,097,577 B** (**91.56% saving**); CREATE CPU ratios **11.699× / 4.192×** and wall ratios **12.991× / 4.267×** exceed frozen **3×** limits | **DEFER**, no shipping rename selector |
| T4 | Restricted T4-A candidate, hosted fault/namespace probes, T4-C3A architectural decision | **DEFER** first-party portable tree schema/publisher; **RETAIN** host-owned trust boundary |

Sources: [T2 original size](PATCH-TREE-001-T2A2-ORIGINAL-PHYSICAL-EVIDENCE-20261026-001.md), [T2 original paired CREATE](PATCH-TREE-001-T2A3A-ORIGINAL-PAIRED-CREATE-EVIDENCE-20261027-001.md) and [its archived census](data/PATCH-TREE-001-T2A3A-EVIDENCE-20261027-001/census.tsv); [T3 original closeout](PATCH-TREE-001-T3-FIRST-RELEASE-CLOSEOUT-20261025-001.md) and [machine ledger](../../../benchmarks/experiments/patch-tree-001-t3-closeout-20261025.json); [T1/F2 provenance](PATCH-TREE-001-F2-ORIGINAL-EVIDENCE-20261016-001.md); [the earlier scoped proposal](PATCH-TREE-001-FIRST-RELEASE-TREE-BOUNDARY-DECISION-20261028-001.md), which remains historical rather than silently relabelled as a new experiment.

**Do not mix warm and cold costs**, extrapolate from two renamed DLLs to all updates, treat source-`Stream` reads as disk I/O, convert a green test workflow into an accepted cost gate, treat partial synthetic fixtures as representative production files, or claim a sealed holdout, actual signature, crash-durable install or independent real-tree decoder measurement that did not occur.

## Outcome and reopening rules

This decision **closes #184 only for the first public Patching compatibility boundary**: all T1/T2/T3 candidates have explicit first-release dispositions and T4 ownership is decided. It does not assert that all possible future cross-file compression or update-set designs have been disproved. Future work belongs to a separately scoped issue/RFC with a named consumer, predeclared full update-set **warm↔warm and cold↔cold** physical accounting, authentic source binding, CPU/wall/memory/seek limits, real holdout, portable metadata, platform-specific publisher and migration plan. Do not reopen the rejected historical gate by retuning it after seeing the negative results.

No new source, decoder, CSM/CSP, PublicAPI, shipping CLI, default builder or release-package change is authorized by this record. **#251 remains OPEN** until every other required ExperimentId has a valid decision and the final CSP v1 versus vNext synthesis is accepted. **#140, public API freeze and publication remain blocked.**

**Verification:** the existing research-registry consistency checker and `test_patch_tree_001_first_release_closeout.py` fail closed if the machine/human status drifts or the archived T2/T3 cost gates stop supporting this decision. This verifies *retained decision inputs*, not the original raw source artifacts anew.
