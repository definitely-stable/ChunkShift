# Patching — first-release research-freeze architecture synthesis (#251)

**Decision:** **RETAIN CSP v1 / NO CSP vNext required for the scoped first Patching release.**  
**Freeze scope:** first-release *research and persisted-format choice only*; **NOT** Patching PublicAPI freeze, CLI product contract, NuGet publication, security updater, or tree-atomic publisher.  
**Date:** 2026-11-17. **Issue:** [#251](https://github.com/definitely-stable/ChunkShift/issues/251). **Evidence base:** engineering \`main@3f664a0ea36634cf2a972323e3ae5a5470d44845\`, before this decision PR. This is a **source-ledger decision**, not a new experiment, new GitHub Actions benchmark, new format revision, or acceptance of formerly failed thresholds.

## 1. The exact compatibility choice

The first-release product boundary is **one-file CSP v1 (including self-contained or one verified-base patch) over CSM v1**, with exact target bytes and verified base binding. Use existing \`ChunkPatch.PlanAsync\`, the two \`CreateAsync\` overloads and two \`ApplyAsync\` overloads, but keep them **unshipped** until a separate public API review. The first release does **not** claim that a directory is atomically updated, that a cross-file source is available, or that an untrusted patch is a trusted software release.

Normative source of CSP v1: [CSP-V1-CANDIDATE](CSP-V1-CANDIDATE.md), frozen **2026-09-28**, sections 3–8, 11–13. Accepted architecture: [RFC-0001](RFC-0001-target-architecture-2026.md), [RFC-0004](RFC-0004-core-0.1-deferred-product-concerns.md), [RFC-0006](RFC-0006-patching-distribution-trust-boundary.md); confirmed implementation decisions [PATCHING-DECISIONS](PATCHING-DECISIONS.md). **RFC-0005 remains Proposed** and adds no BuildPolicy, payload family or production API. The separate registry of exact source blob IDs is in [the machine ledger](../../benchmarks/experiments/patching-research-freeze-251-20261117.json).

**Persisted contract retained unchanged:**

- CSP v1 PREAMBLE / embedded target TCSM / optional BASE / PAYL* / optional AUX0* / PIDX / FOOT / TRAILER order, size constraints, CRC-32C and stored \`FileDigest\`; existing strict rejection/verdict order and resource bounds.
- Embedded CSM v1 with its \`ManifestId\`, 256-bit \`ChunkId\`, \`HashSuiteId\`, \`ChunkingProfileId\` and \`ProfileFingerprint\`. Do not redefine any logical identity. CSP binds one optional base \`ManifestId\` and required target \`ManifestId\`; no persisted \`PatchId\`.
- Raw and zstd frame encoding 1, optionally referencing **0–4 named verified base chunks**. Exact reuse via \`ChunkId\`, not a generic instruction VM. Existing D15 encoder policy and bounded ordered H2-W2 workers remain internal, not persisted BuildPolicy.
- Independent 125-vector CSP suite, independent decoder / zstd-frame comparison, differential corruption/fuzz, NativeAOT and package-consumer gates remain regression requirements. Do not regenerate golden vectors to bless a regression.
- D7: deterministic reconstruction, validated identities and verdicts are the compatibility contract; **physical patch bytes are not a cross-build/backend compatibility promise**. Physical CSP hash and length nonetheless bind each *particular* delivered artifact at the trust layer.

**What this decision does not do:** freeze the .NET \`PublicAPI.Unshipped.txt\` surface as shipped, introduce mandatory options or progress callbacks, declare \`IsPackable=true\` for CLI, promise portable atomic directory installation, introduce signing fields/anti-rollback, or authorize publication from this engineering repository.

## 2. Evidence and bounded first-release dispositions

Each listed experiment retains its separate cohort, threshold, original evidence and meaning; the table is **not** an aggregated quantitative benchmark. There is no new sealed holdout.

| Experiment / issue | First-release decision | Basis and boundary |
| --- | --- | --- |
| PATCH-ENC-005 / [#181](https://github.com/definitely-stable/ChunkShift/issues/181) | **REJECT** resemblance/search H6-O; retain D15 | [Original final H6-O](../research/results/PATCH-ENC-005-H6O-EVIDENCE-20261004-001.md): candidate search gates missed; no new persisted family |
| PATCH-GAP-001 / [#183](https://github.com/definitely-stable/ChunkShift/issues/183) | **DEFER** CSP vNext factors | [Final closeout](../research/results/PATCH-GAP-001-FIRST-RELEASE-CLOSEOUT-20261104-001.md): tested G1 and G4-BCJ < 15% gate, G3 regressed. HDiffPatch external reference has real alternative tradeoffs but no admitted CSP format; Zucchini original was **not run** (85.45 GiB < 100 GiB disk admission), therefore **RESOURCE_UNVERIFIED**, not an algorithm rejection |
| PATCH-TREE-001 / [#184](https://github.com/definitely-stable/ChunkShift/issues/184) | **DEFER** tree/update-set and cross-file path | [Final closeout](../research/results/PATCH-TREE-001-FIRST-RELEASE-CLOSEOUT-20261101-001.md): T2 size headroom fails absolute create-time budget on 2/4 original pairs; T3 strong changed-rename byte benefit fails 3× CPU cost on both. T0 same-path per-file reference stays, with no portable tree manifest or publisher |
| PATCH-META-001 / [#220](https://github.com/definitely-stable/ChunkShift/issues/220) | **DEFER** disposable cache | [G1B2B closeout](../research/results/PATCH-META-001-G1B2B-FIRST-RELEASE-DEFER-20261105-001.md): codec work measured but reusable cache A/B and adoption threshold not established |
| PATCH-INCR-001 / [#150](https://github.com/definitely-stable/ChunkShift/issues/150) | **DEFER** incremental manifest production path | [A3S0 closeout](../research/results/PATCH-INCR-001-A3S0-DEFER-DECISION-20261101-001.md): original Btrfs source-read and wall-speed gates failed; no new CSM identity semantics |
| PATCH-DICT-001 / [#222](https://github.com/definitely-stable/ChunkShift/issues/222) | **DEFER** composite/disjoint base dictionaries | [D1B0B closeout](../research/results/PATCH-DICT-001-D1B0B-FIRST-RELEASE-DEFER-20261109-001.md): 0 B incremental physical saving over variable contiguous K on two opened files; variable-K itself not product ADOPT |
| PATCH-DOTNET-001 / [#221](https://github.com/definitely-stable/ChunkShift/issues/221) | **DEFER** CLR/IL-specific persisted transform | [D3P1 closeout](../research/results/PATCH-DOTNET-001-D3P1-FIRST-RELEASE-DEFER-20261110-001.md): TypeRef-only matched selection 0 B saved; other CLR families untested, **not** universally rejected |
| PATCH-ML-001 / [#223](https://github.com/definitely-stable/ChunkShift/issues/223) | **DEFER** learned ranking | [Source-bound closeout](../research/results/PATCH-ML-001-FIRST-RELEASE-DEFER-20261110-001.md): exact oracle tiny benefit, H6-O guarded size regression; **no trained model or ML holdout** |
| PATCH-COMPILER-001 / [#224](https://github.com/definitely-stable/ChunkShift/issues/224) | **DEFER** multi-objective BuildPolicy | [Entry-gate closeout](../research/results/PATCH-COMPILER-001-FIRST-RELEASE-DEFER-20261114-001.md): fewer than two *admitted* decision-grade representation families; neither Phase A offline cross-family replay nor Phase B online compiler was executed |
| PATCH-EXEC-001 / [#225](https://github.com/definitely-stable/ChunkShift/issues/225) | **DEFER** new adaptive scheduler/pure task-result cache | [First-release closeout](../research/results/PATCH-EXEC-001-FIRST-RELEASE-DEFER-20261117-001.md): independently audited 960/960 CSP identities on historical fixed lanes; no tested adaptive policy versus best safe fixed baseline and resource/cache gates unmet; retain current H2-W2 |
| TRUST-SIG-001 / [#185](https://github.com/definitely-stable/ChunkShift/issues/185) | **ADOPT ARCHITECTURE ONLY**, not persisted CSP | [R3C evidence](../research/results/TRUST-SIG-001-R3C-ARCHITECTURE-DECISION-20261019-001.md) and accepted [RFC-0006](RFC-0006-patching-distribution-trust-boundary.md): host-managed authenticated distribution boundary; first-party signing/updater/witness deferred, local-only rollback anchor rejected |
| PATCH-RESIDUAL-001 / [#262](https://github.com/definitely-stable/ChunkShift/issues/262) | **FIRST-RELEASE DEFER; future research remains PLANNED/OPEN** | [R1/R2 Phase A protocol](../benchmarks/PATCH-RESIDUAL-001-PROTOCOL.md): synthetic 85-byte RS01 mechanism and reversible tests only. **No matched full-physical CSP H0 headroom screen, no decision-grade second representation, no oracle or sealed validation**. The issue itself allows scoped DEFER; future evidence cannot silently amend CSP v1 |

Earlier **ADOPT** results for PATCH-PREFREEZE-001, PATCH-ENC-001/002/003/004, PATCH-ZSTD-001 and PATCH-APPLY-001/003 are **the source of the already frozen v1 mechanics or same-semantics implementation policy**, not additional newly accepted persisted formats after 2026-09-28. [Pre-freeze evidence](../research/results/PATCH-PREFREEZE-001-EVIDENCE-20260928-001.md) and [PATCHING-DECISIONS](PATCHING-DECISIONS.md) retain their authority.

## 3. Cross-experiment interactions / why no interaction experiment is owed now

The conditional interaction rule in #251/#183 requires **two individually admissible, materially helpful, non-composable persisted representation factors** that could change first-release source or decoding semantics. It is **NOT_TRIGGERED** on the current evidence, *not* permanently disproved:

1. G1, G3, G4-BCJ failed their preregistered factor gates. There is no accepted executable normalization × dictionary source representation pair. G5 lacked population; Zucchini lacks resource-qualified original evidence.
2. T2/T3 demonstrate interesting update-set size potential but fail independent first-release create-resource admission. They cannot be promoted by combining them with compiler, ML or cache abstractions.
3. Composite dictionary D1B0B has zero additional gain beyond a within-CSP-v1 variable contiguous K on two already opened files, not a second format. D3P1 TypeRef-only has no selected physical savings.
4. PATCH-META/ML/EXEC optimize build process, not a second persisted family. PATCH-COMPILER's two-family necessary gate remains NOT_MET. Residual RS01 is a lab envelope, not CSP evidence.
5. RFC-0006 is an orthogonal **external** trust boundary; it does not inject signature, path or policy metadata into CSP and does not interact through persisted encoding.

If future evidence admits two such factors, preregister a **new, separately owned** interaction study and accept any needed vNext RFC *before* changing parser, writer, identity or public contracts. Do not reinterpret historical calibration or reopen an untouched holdout after the fact.

## 4. Complete persisted-semantics ADOPT/RFC audit

**Inventory of first-release research results adopted after the frozen CSP v1 that newly change persisted CSM/CSP semantics: EMPTY (0).** This is a *scoped adoption inventory*, not a claim that no earlier work ever changed format during the pre-freeze design. Therefore the #251 requirement “every persisted-semantic ADOPT has accepted RFC, decoder vectors, migration/versioning plan” is **satisfied vacuously for new semantic ADOPTs**, with pre-freeze choices already governed by the accepted RFC-0001/RFC-0004 and frozen CSP v1 and its 125 vectors. **Do not fabricate a vNext migration requirement where no vNext is adopted.**

| Compatibility axis | First-release verdict | Future reopening requirement |
| --- | --- | --- |
| CSP / CSM binary major and required features | **v1 unchanged**; CSM v1 embedded | Accepted RFC/versioned format, independent vectors, explicit reader rejection/forward-compatibility and a migration/dual-read plan as justified |
| ManifestId, ChunkId, hash/profile identity | **unchanged** 256-bit and registered semantics | Accepted identity RFC, conformance corpus and explicit transition strategy; do not smuggle into codec optimization |
| Semantic target selection / first-occurrence model | **unchanged** | Separate adopted source/representation evidence and deterministic migration/reproducibility contract |
| Raw/zstd/dictionary physical encoding/backend | **existing only** | Compatible backend: same decoder verdicts and fixtures under D7; novel persisted encoding needs vNext/RFC and vectors |
| Tree/update-set metadata and OS publication | **no first-party format or directory atomicity** | Host-owned workflow now; future canonical tree/atomic publish requires distinct RFC, crash/fault and policy tests |
| Authenticity/freshness/rollback | **not claims of ChunkShift** | Host's independent trusted metadata and protected state per RFC-0006; never infer from local CSP digests |
| Patching PublicAPI and CLI | **still unshipped / unfrozen** | Separate #140 and PublicAPI review/tests; publication repo independently decides packages/versions |

**Versioning/migration choice:** ship only existing v1 mechanics when Patching's separate release checks pass. No format migration is needed to consume CSP v1. CSP, CSM, package and profile versions remain independent. A future incompatible representation MUST NOT be silently written as CSP v1; obtain an accepted RFC, independently versioned wire changes, decoder/golden-fixture and corruption gates, reader transition/fallback plan, migration/compatibility matrix and separate source/holdout evidence. Compatible changes to encoder physical bytes are explicitly allowed by D7, while signed distribution metadata binds each delivered physical artifact digest/length.

## 5. Trust and installation boundary (security acceptance)

RFC-0006 says that **integrity is not publisher authorization**. CSP/CSM checks only establish integrity relative to their content identities and verify reconstruction; they do not authorize version, channel, release, source trust or installation. The integrating host owns signed target bytes/length, base/target binding, root continuity, rollback/freshness state, paths, safe staging and installation recovery. A host using TUF-compatible metadata must implement its own verified client/policy; no signer, TUF adapter, witness or updater ships in \`ChunkShift.Patching\`.

A **per-file** \`ChunkPatch.ApplyAsync(..., destinationPath)\` may verify and safely publish a single target file. Do not extrapolate that into an atomic **multi-file update set**. The latter remains explicitly outside first-release scope by #184.

## 6. Exit and sequence — what accepting #251 actually authorizes

After this synthesis **merges on a clean GitHub-hosted CI HEAD**, its machine ledger/negative tests pass, and issues #251/#262 are reconciled, **close #251 as RESEARCH_FREEZE_COMPLETE**. This is authorization to **start** the separately owned product compatibility work, not automatic approval to release:

1. **#140 D23 CLI**: decide grouped taxonomy; verify current create/inspect/plan/verify/patch workflows; versioned \`--json\` machine schema; stdout/stderr and exit codes; destination collision, overwrite/atomic staging, cancellation/path and large-output boundedness; maintain \`src/ChunkShift.Cli/ChunkShift.Cli.csproj\` \`IsPackable=false\` pending publication decision.
2. **Patching PublicAPI review**: treat \`src/ChunkShift.Patching/PublicAPI.Unshipped.txt\` as an *unfrozen draft*. Independently review method/stream/error/option/progress contracts, API compatibility, exact apply and package-consumer/NativeAOT smoke before declaring a shipped surface.
3. **Only after #140 + PublicAPI acceptance**: pass existing spec/vectors, fuzz, determinism, performance and artifact/consumer gates; port approved source one-way by a **separate PR inside the publication repository**, with its independent NuGet/tag/release owner decision. Nothing is published, tagged or ported by this research gate.
4. **Future research**: #262 residual, unverified Zucchini, optional CLR/R2R or tree/cross-file/update-set variants remain possible *vNext* research under separately frozen protocols. They do not retroactively invalidate the present scoped first-release decision or block it indefinitely.

### Closure checklist / failure conditions

- All eleven #251 required owning issues have durable first-release decisions linked above; #181 original REJECT, #185 architecture-only ADOPT, the others DEFER. Conditional #262 separately receives first-release DEFER, stays OPEN and PLANNED for future work.
- No novel admitted persisted representation; no pending first-release ADOPT that changes v1. Proposed RFC-0005 remains unaccepted. No first-release interaction trigger.
- This document is **not** in an accepted RFC hierarchy by itself: it records application of already accepted normative rules and experimental adoption gates. It cannot amend the frozen CSP spec, PUBLIC API or RFC-0006.
- CI must fail if a pinned original source blob, the machine registry's decision, status of conditional #262, CSP/RFC versions, or the two remaining #251 synthesis assertions contradict each other. Archived source ZIPs need not be replayed: original archive verification is documented in their owning experiment; this is a decision-ledger replay only.
- Do **not** mark #251 complete before both the source-bound workflow and normal CI pass on the **exact PR SHA**. If one fails, leave #251 open, report the blocker and repair in the PR first.
