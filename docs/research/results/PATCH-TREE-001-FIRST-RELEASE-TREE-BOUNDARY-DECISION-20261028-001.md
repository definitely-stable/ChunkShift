# PATCH-TREE-001 — first-release tree/update-set representation boundary

**DecisionId / EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261028-001` (decision-only synthesis; **no new RunId**).  
**ExperimentId:** `PATCH-TREE-001`. **Owners:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Date:** 2026-10-28. **Evidence base:** engineering `main@d80d7ca23d79c155df779805e4d730d2bbd0fac8`.  
**Status:** **PROPOSED first-release scope decision, subject to PR review**; not an accepted persisted-format RFC or a public release approval.

## 1. Decision proposed for the first Patching release

**Retain the existing CSP v1 per-file reconstruction contract and the same-path T0 cost/reference policy.** Do not add a ChunkShift-owned persisted tree manifest, tree identity, cross-file base locator, tree update-set API, or tree publisher to the first public `ChunkShift.Patching` surface. A host application may construct a tree update from individually verified file artifacts, but it owns its **own** release descriptor, source selection, tree metadata, trust policy, installation, and recovery. No automatic T0/T0S selection or update-set orchestration is implemented by this decision.

This is a **scope and compatibility decision**: there is **no adopted portable update-set format** in v1. An optional host-specific descriptor is outside the ChunkShift persisted compatibility contract. The research T4-A ASCII-only `TreeCandidateId` is neither adopted nor interoperable and **must not** be treated as a CSM `ManifestId`, a CSP BASE, an authenticated tree identity, or a shipping format.

The decision is consistent with [accepted RFC-0006](../../architecture/RFC-0006-patching-distribution-trust-boundary.md), which owns the application trust/publisher boundary, and with [T4-C3A](PATCH-TREE-001-T4C3-PUBLISHER-BOUNDARY-DECISION-20261022-001.md). It does **not** amend RFC-0006, accept proposed RFC-0005, modify CSM/CSP bytes or Core identities, freeze `PublicAPI.Unshipped.txt`, or authorize publication. The [research freeze #251](https://github.com/definitely-stable/ChunkShift/issues/251) remains **OPEN**.

## 2. Decision matrix and independent evidence

| Lane / concern | Audited observation and decision scope | First-release disposition |
| --- | --- | --- |
| **T0**: same-path per-file CSP, raw-added calibration | Baseline used throughout the four pinned original .NET pairs; complete **warm** physical reference **14,758,982 B** including common diagnostic target map. Added files were raw **in the benchmark denominator**, not a newly mandated shipping policy. | **RETAIN** existing per-file CSP v1. T0 is a cost/reference lane, **not** a production tree planner or mandatory raw-added mode. |
| **T0S**: self-contained per-file CSP | Measured compressed self-contained control for new/renamed paths; choose the applicable authorized source independently per file. | **ALLOW** existing CSP v1 self-contained capability, no host automatic-choice API. |
| **T1**: naive whole-old-tree base for per-file CSP | Original four-pair warm **83,593,822 B**, **5.6639× T0**. [F1](PATCH-TREE-001-F1-EVIDENCE-20261016-001.md) attributed large zstd frame regressions despite equal exact reuse availability. | **REJECT / NO_ADOPT** the measured naive T1 selection, not a proof against all future cross-file reuse. |
| **T1/F2**: relative-local variant | Original diagnostic workflow concluded `IMPROVED_T1_BUT_NOT_COMPETITIVE_WITH_T0`: **47,002,363 B**, **3.185× T0**. The producer workflow failed a source-lock transcription; an independent artifact-only replay validated the archived data without transforming it into a successful original run. [F2 provenance and limits](PATCH-TREE-001-F2-ORIGINAL-EVIDENCE-20261016-001.md). | **NO_ADOPT**; retain the failed-original-run and replay distinction. |
| **T2**: single whole-tree CSP v1 | [A2 original physical evidence](PATCH-TREE-001-T2A2-ORIGINAL-PHYSICAL-EVIDENCE-20261026-001.md): T2 warm **10,828,279 B** vs T0 warm **14,758,982 B** (**26.632616%** saving), with real CSP Create/Apply reported on restricted regular-file trees. [A3A paired audit](PATCH-TREE-001-T2A3A-ORIGINAL-PAIRED-CREATE-EVIDENCE-20261027-001.md): **2/4** transitions fail the **pre-frozen absolute 60 s** T2 CREATE wall gate (76.246 s, 69.693 s), despite all relative CPU/wall ratios ≤3×. | **DEFER / NO_PRODUCTION_ADOPT**; do **not** retune gate post hoc, open sealed holdout or auto-run A3B apply-RSS qualification after this negative cost admission. |
| **T3-A**: exact added-path cross-location reuse | Four original .NET pairs, two added files, **zero** exact full-old-file candidates in [B0 original census](PATCH-TREE-001-T3B0-ORIGINAL-CENSUS-EVIDENCE-20261023-001.md). | **NO_ADOPT on this cohort**; not a universal zero-reuse theorem. |
| **T3-B**: changed/renamed source selection | [Original closeout](PATCH-TREE-001-T3-FIRST-RELEASE-CLOSEOUT-20261025-001.md): two genuine changed-renamed DLLs, **92,671 B T3 warm** vs **1,097,577 B T0S** (~91.56% saving); paired process CPU **11.699× / 4.192×**, wall **12.991× / 4.267×**, both over the preregistered **3×** gate. H3 reduced logical BASE rereads but failed the both-pair CPU-improvement rule. | **DEFER / NO_PRODUCTION_ADOPT**. The lab `FALLBACK_T0_OR_T0S__CREATE_BUDGET` is **not** a shipping fallback selector. |
| **T4-A**: portable identity candidate | [Restricted lab design](../../benchmarks/PATCH-TREE-001-T4A-TREE-MANIFEST-PROTOCOL.md) excludes broad Unicode, ACLs, reparse points, hard links, sparse data and other real host metadata. | **DEFER** a normative tree schema/identity for first release; never label the lab digest a compatible tree ID. |
| **T4-C**: tree publication and trust | [T4-C3A](PATCH-TREE-001-T4C3-PUBLISHER-BOUNDARY-DECISION-20261022-001.md) / RFC-0006: native held-handle + identity-preflight tests expose a namespace-authorization race. | **ADOPT existing host-owned boundary as architecture**; **REJECT** preflight-as-authorization; **DEFER** first-party OS publisher and atomic-tree claim. |

These dispositions use **existing** original records. This synthesis has no new calibration, held-out decision data, fresh independent binary CSP decode, peak private RSS measurement, source authorization test or trusted host installation test. In particular, T2 warm vs T0 warm is a matched accounting comparison; T2 **cold** 11,200,943 B must **not** be compared to the archived T0 **warm** as though both were cold.

## 3. Per-file contract versus host tree operation

For the first `ChunkShift.Patching` release, the intended *library* promise remains: validate supplied base/target manifests and CSP under their existing contracts, reconstruct a **single** exact target into its destination path, and report the existing verification/failure result. The code already provides `ChunkPatch.PlanAsync`, `CreateAsync` and `ApplyAsync` for this per-file scope. A successful single-file Apply does **not** authorize an installed release or establish multi-file transaction atomicity.

An integrating application that offers tree updates must independently define and validate all of the following:

1. **Inventory / target binding:** authenticated release metadata binding every target file, required CSP/raw artifact physical length and digest, expected reconstructed content identity, intended base identity, and explicit new/changed/unchanged/deleted entries. CSM `FileDigest` hashes manifest bytes, **not** reconstructed file content; self-consistent CSP/CSM identities do not authenticate a publisher.
2. **Path and metadata policy:** canonical comparison and collision behavior on the actual target filesystem; file names (including Unicode/case), empty directories, symlinks/reparse points, executable bits, modes, ACLs, hard links, xattrs and unsupported types. A host may deliberately reject unsupported entries rather than normalize them silently. No universal tree serialization is specified here.
3. **Source authority:** authorize the specific base file and version before choosing per-file CSP; missing/renamed files cannot be mapped to an arbitrary base based on local names, heuristics, or the patch's self-asserted identifiers.
4. **Staging and commit:** private verified generation, bounded resource/space policy, writer exclusion, reader generation pinning, authorization at the actual commit boundary, recovery and retirement. A successful per-file move or held-directory operation does **not** imply whole-tree atomicity, power-loss durability, or permission to commit into a mutable namespace.
5. **Trust and freshness:** release provenance, policy/channel, signatures where required, rollback/freeze protection and trusted local state belong to the application/trust client per RFC-0006. Neither ChunkShift Core nor Patching is a TUF implementation or signed updater.

There is **no** first-party operation that takes an old installed tree and safely publishes a complete new installed tree as a stable `ChunkShift.Patching` API. A local application using already-trusted input may choose a simpler threat model but must state it; trust guarantees cannot be inferred from a hash.

## 4. Why defer the portable tree schema instead of freezing T4-A

Choosing a portable tree schema now would establish long-lived persisted decisions about path equivalence, contents versus metadata identity, links/permissions, source location, artifact graph and publication authority **without** representative original host acceptance. T4-A's deliberately restricted ASCII subset is useful as a negative/fuzzing research fixture but is not a general launcher/update distribution model.

A host-owned, versioned descriptor can support a real product without forcing ChunkShift to own its encoding; if authenticated delivery is required, the host must bind that descriptor under its own trusted release policy. The descriptor is outside `ManifestId`, `CSP` and the first Patching API. This is a **deliberate absence of a cross-platform format**, not a claim that an arbitrary host descriptor is secure or interoperable.

Future ChunkShift-owned tree format/publisher proposals must start in a **separate** issue/RFC with an explicit adoption gate; no retroactive mutation of CSP v1, CSM v1, frozen profile or public API.

## 5. Follow-up gate for a future update-set product

To revisit T2/T3 or standardize a tree format, require a preregistered, source-locked original plus independently preserved holdout study that:

- compares **complete** T0/T0S and candidate *warm with warm, cold with cold* physical bytes including all metadata/locators/signing or authenticated descriptor overhead, and includes added/removed/unchanged paths;
- records end-to-end create/apply CPU, wall, trusted base reads, seek/range amplification, **independently sampled peak private resident memory**, bounded index occupancy and target verification; the observed CREATE-only 60 s/3× first-release gate is not silently rewritten;
- defines portable path and file metadata policy, host namespace authorization and threat model, versioning and migration, signed release binding, and a qualified platform-specific publisher where any atomicity/durability is claimed;
- requires independent byte-level reconstruction of retained raw source/CSP artifacts when claiming it, rather than treating SHA-checked summary rows as an independent decoder;
- changes persisted semantics **only** after an accepted RFC, versioned vectors/decoder implications and migration plan.

A distinct optimization hypothesis may have its own new prospective budget; the negative A3A/B2B historical gates remain frozen records.

## 6. Status, acceptance and non-effects

This PR proposes **first-release scope closure only**, not the closure of all `PATCH-TREE-001` research. Retain [#184](https://github.com/definitely-stable/ChunkShift/issues/184) **OPEN** pending owner review of whether all tree/metadata research obligations can be explicitly deferred; retain [#251](https://github.com/definitely-stable/ChunkShift/issues/251) **OPEN** for #183/#220/#150/#221/#222/#223/#224/#225 and final CSP architecture synthesis. Do **not** check #251's #184 checkbox, freeze #140/PublicAPI, port to [the publication repository](https://github.com/MrFr3di/ChunkShift), or create a release based on this proposal.

Review acceptance: the first-release boundary is unambiguous; all T1/T2/T3 numeric evidence has the correct original/provenance scope; no inherited tree atomicity/authenticity claim; T4-A not promoted; no shipping code, default, wire bytes, compatibility vectors, dependency or CLI command changed. This is a documentation-only decision with **no new performance measurement**.
