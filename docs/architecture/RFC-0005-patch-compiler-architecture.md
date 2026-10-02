# RFC-0005: Patch compiler research architecture

Status: Proposed

Target: ChunkShift.Patching research and future persisted-format revisions  
Last updated: 2026-10-02

## 1. Purpose

ChunkShift already separates stable content identity, deterministic chunking, CSM manifests, CSP patch semantics and future Repository transport/storage concerns. The next research frontier adds increasingly sophisticated build-side analysis: reusable metadata, resemblance search, structure-aware transforms, learned ranking, composite source material and cost-aware planning.

This RFC is a **proposal** that collects architectural constraints for that research. Until accepted, existing accepted RFCs, CSP v1 and `PATCHING-DECISIONS.md` remain authoritative where wording conflicts.

It does **not** select a new CSP encoding, change CSP v1, change Core 0.1.x identities, or approve any specific experimental candidate.

Owning research roadmap: [PATCHING-RND-ROADMAP](../research/PATCHING-RND-ROADMAP.md).

## 2. Core thesis

Patch construction and patch consumption have different complexity budgets.

```text
BUILD / CREATE SIDE
may use:
- large indexes;
- reusable derived metadata;
- executable/container parsers;
- expensive bounded oracles;
- learned ranking;
- local parallel execution;
- offline profiling.

WIRE / APPLY SIDE
must remain:
- bounded-memory;
- independently verifiable;
- exact;
- versioned;
- free from mandatory ML/ANN/GPU/runtime-specific services.
```

Builder intelligence may grow without turning the patch format into a general instruction VM.

## 3. Authoritative truth vs derived optimization

Authoritative semantics remain the existing exact identities and persisted contracts.

Derived metadata, learned features, caches, search indexes and scheduler state are optimization data.

Hard rule:

```text
delete(derived state)
    =>
slower or more expensive build
    !=
different correctness semantics
```

A corrupt, stale or unknown-version optimization record must be rejected/recomputed. It cannot establish integrity.

## 4. Approximate search is never correctness

Similarity sketches, LSH, learned sketches, ANN and heuristic rankers may propose candidates.

They may not prove that content is correct.

Any candidate selected through approximate search still flows through the exact verification rules required by the active patch format and target identity.

## 5. Reproducible semantic selection without making patch bytes a contract

`PATCHING-DECISIONS.md` D7 is authoritative: **patch bytes are not a compatibility contract**. Compatible encoder/backend/search improvements may change physical CSP bytes while preserving required semantics.

### 5.1 Semantic reproducibility

For a fixed versioned BuildPolicy and fixed candidate-result set, scheduler order, worker count, cache-hit order and machine timing must not change the **semantic choice** of source/representation or any correctness verdict.

Where parallel execution is used:

```text
parallel candidate computation
        |
        v
validated result records
        |
        v
deterministic semantic selection
        |
        v
ordered writer
```

Tie-breaking and numeric scoring rules must be frozen before decision-grade runs.

### 5.2 Same-build physical transparency tests

A specific frozen experiment may additionally require byte-identical CSP output between cache-on/cache-off or scheduler variants of the **same implementation/build/backend/policy**. That is a non-normative regression oracle proving that the execution optimization is transparent.

It does not create a cross-version or cross-backend byte contract. Cross-version/backend compatibility is judged by semantic results: target bytes, ManifestId, ChunkIds and required verdicts. Physical CSP bytes and `FileDigest` may legitimately differ under D7.

## 6. Representation portfolio, not one universal codec

Future patch construction may evaluate multiple bounded representations:

```text
exact reuse
raw / zstd
bounded dictionary
bounded composite dictionary
executable normalization
container-aware normalization
.NET-specific semantic normalization
other representations only after an owning issue/ExperimentId exists
```

A representation candidate is not adopted because it wins one local byte-count comparison. It must be evaluated with its apply memory, CPU, base-read, request/locality and metadata consequences.

## 7. Separate cost domains

ChunkShift must keep these concepts distinct:

```text
logical identity granularity
    !=
compression/delta granularity
    !=
physical pack granularity
    !=
remote request granularity
```

This separation already exists in the Core/Repository direction and must remain explicit in Patching research.

A future cost-aware compiler may account for multiple domains, but must not make transport-specific details part of CSP semantic identity unless a later accepted RFC explicitly does so.

## 8. Multi-objective planning and BuildPolicy

Patch bytes remain an important metric, not the only metric.

Research may measure a cost vector including wire bytes, create/apply wall and CPU, apply peak memory, base bytes read, seek/range count, remote request/overfetch cost, and dependency/locality cost.

Those **runtime measurements are offline evidence** used to design and freeze a policy. They are not dynamic online inputs that may change artifact selection according to current machine load.

A versioned internal **BuildPolicy** may use only deterministic inputs whose semantics are frozen, for example exact encoded byte counts, static representation cost constants derived from prior evidence, deterministic read/range/dependency estimates, and integer/fixed-point score terms.

Avoid the word `profile` for this concept because ChunkShift already uses `ChunkingProfileId` and `ProfileFingerprint`.

A BuildPolicy must define its identifier/version, representation set, exact scoring formula, arithmetic/overflow semantics and canonical tie-breaking. It governs semantic selection; D7 still permits compatible encoder/backend changes to alter physical CSP bytes.

## 9. Semantic transforms

Structure-aware transforms are allowed only when all of the following are true:

- eligibility is deterministic and fail-closed;
- unsupported/malformed inputs fall back to a generic path;
- inverse reconstruction is exact;
- transform metadata is bounded and explicitly versioned;
- every metadata/header/index byte is included in physical accounting;
- final target identity verification remains authoritative;
- adoption is supported by both eligible-subset and corpus-weighted evidence.

Generic executable/container experiments and .NET-specific semantic research remain separate until evidence justifies convergence.

## 10. ML boundary

ML is allowed as a build-side research/optimization mechanism.

ML is not allowed to become:

- integrity truth;
- a mandatory apply dependency;
- a hidden semantic input to a supposedly stable BuildPolicy;
- an excuse for unversioned/non-reproducible semantic selection.

A successful learned experiment may result in either a retained versioned build-side model, if it produces material value and deterministic candidate selection can be demonstrated on the supported execution set, or preferably a distilled deterministic scorer without an ML runtime dependency.

If deterministic canonical inference cannot be demonstrated, the learned model remains research/advisory only.

## 11. Derived metadata boundary

Reusable patch features may be persisted only as rebuildable derived state.

At minimum, records that affect build behavior must bind the relevant source/manifest identity, ProfileFingerprint when boundary-derived, feature schema/version, algorithm/model version, and BuildPolicy/encoder identity where relevant.

Durable records need explicit corruption detection. A checksum/digest failure, truncation or unknown version is a miss/recompute path, not acceptance.

No derived sidecar becomes required to read a CSM or apply a CSP.

## 12. Execution architecture

Expensive patch construction may evolve toward deterministic **local** task decomposition.

A task identity may bind target identity, base/candidate identities, transform/feature version, codec version, BuildPolicy version, backend/implementation version when cached results contain physical encoded bytes, and dictionary/source-material identity and ordering.

Caching and retries may reuse task results only if the result is independently validated and canonical semantic selection is preserved.

If physical encoded bytes are cached, the cached bytes need their own digest/checksum and must be decoded or otherwise verified against expected output length and ChunkId before inclusion in a newly built patch. Final Apply verification is defense in depth, not the first validation.

Distributed/remote build execution is a separate hypothesis. If local task decomposition proves valuable, it requires a new ExperimentId and threat/economics protocol rather than being silently added to `PATCH-EXEC-001`.

## 13. Promotion rule

The research sequence is:

```text
issue
 -> ExperimentId
 -> frozen protocol / decision rule
 -> implementation in lab/internal seam
 -> decision-grade evidence
 -> ADOPT / DEFER / REJECT
 -> only then persisted-format/API RFC if required
```

No experiment is promoted into CSP/Core/Repository semantics merely because a prototype works.

Negative results remain indexed.

## 14. Current research map

The first research set associated with this proposal is:

- #220 — `PATCH-META-001`: reusable derived features across repeated create;
- #221 — `PATCH-DOTNET-001`: .NET semantic normalization after completion of generic G4 evidence;
- #222 — `PATCH-DICT-001`: bounded composite dictionaries built from verified base chunk runs;
- #223 — `PATCH-ML-001`: learned reference ranking as a build-side oracle/distillation experiment;
- #224 — `PATCH-COMPILER-001`: deterministic cost-aware representation compiler with a versioned BuildPolicy;
- #225 — `PATCH-EXEC-001`: local adaptive/cacheable deterministic patch compilation.

These are non-blocking research tracks. They do not expand the frozen scope of PATCH-ENC-005 (#181/#216/#218) or PATCH-GAP-001 (#183/#217/#219).

## 15. Prior art and research anchors

These references motivate specific questions but do not define ChunkShift semantics:

- SkySync, FAST '26 — reuse of existing storage metadata/checksum/search work to reduce synchronization-side recomputation:
  https://www.usenix.org/conference/fast26/presentation/zhang-zhihao
- DeepSketch, FAST '22 — learned sketches for reference search in post-deduplication delta compression:
  https://www.usenix.org/conference/fast22/presentation/park
- LoopDelta, USENIX ATC '23 — joint treatment of similarity, locality, prefetch/cache behavior and restore cost:
  https://www.usenix.org/conference/atc23/presentation/zhang-yucheng
- Chromium Zucchini — executable-aware normalization/diffing:
  https://chromium.googlesource.com/chromium/src/+/HEAD/components/zucchini/
- Hugging Face Xet — modern CDC/dedup storage with separate chunk/index/storage grouping concerns:
  https://huggingface.co/docs/hub/en/xet/deduplication

## 16. Non-goals

This RFC does not approve:

- a physical CSP byte-identity compatibility guarantee;
- neural compression;
- LLM-based patch encoding;
- arbitrary recursive delta chains;
- public plugin-codec interfaces;
- GPU requirements;
- trusted-execution or zero-knowledge patch construction;
- transport semantics inside CSP;
- a new CDC profile;
- remote/distributed patch compilation under PATCH-EXEC-001.

Those may be revisited only with a concrete product need, an owning issue/ExperimentId and separate evidence.

