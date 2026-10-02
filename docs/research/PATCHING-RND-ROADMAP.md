# Patching R&D roadmap

Status: Active research roadmap  
Last reviewed: 2026-10-02  
Architecture proposal: [RFC-0005](../architecture/RFC-0005-patch-compiler-architecture.md) (**Proposed**, not yet an accepted authority)

## 1. Purpose

This file tracks post-CSP-v1 research that may improve patch construction, patch size, update-set behavior or future Repository integration without silently changing shipped/frozen semantics.

It is intentionally separate from:

- `PLAN.md` — executable milestone acceptance;
- `ROADMAP.md` — product sequencing;
- frozen benchmark protocols — exact experiment contracts;
- accepted RFCs — durable architecture/persisted semantics.

Existing accepted RFCs, CSP v1 and `PATCHING-DECISIONS.md` remain authoritative. RFC-0005 is a research-architecture proposal until separately accepted.

## 2. Research thesis

ChunkShift should make the **builder** progressively smarter while keeping the **artifact and applier** bounded, exact and independently verifiable.

```text
source/base/target
      |
      v
reusable exact + derived metadata
      |
      v
bounded candidate / transform generators
      |
      v
representation portfolio
      |
      v
versioned deterministic BuildPolicy
      |
      v
declarative patch/update set
      |
      v
bounded exact apply + verification
```

The potential long-term differentiation is the composition of these layers, not any single known algorithm.

Physical CSP bytes remain non-contractual under PATCHING-DECISIONS D7.

## 3. Already covered — do not duplicate

| Area | Existing owner |
| --- | --- |
| Same-profile FastCDC implementation work | #152/#153 |
| Future CDC algorithms / RepMaxCDC / SeqCDC / Chonkers | #14/#136 |
| Exact dirty-range incremental rechunk | #150 / `PATCH-INCR-001` |
| Create candidate selection / resemblance / level ladder | #181 / `PATCH-ENC-005` |
| CSP size-gap decomposition, generic executable/container factors | #183 / `PATCH-GAP-001` |
| Tree/update sets and cross-file reuse | #184 / `PATCH-TREE-001` |
| Trust/signature/update policy | #185 / `TRUST-SIG-001` |
| Repository one-hop delta storage | #151 / `REPO-DELTA-001` |
| Repository indexes/filters | #145 |
| Physical locality/multi-location | #146 |
| Remote reconstruction/range planning | #148 |

New research must reference these owners rather than recreating them.

## 4. New research frontier

### #220 — PATCH-META-001 — reusable derived features across repeated create

Question: can repeated patch builds reuse deterministic, disposable features and avoid recomputation while preserving identical semantics?

Priority: **NOW / high**. It does not own dirty-range incremental rechunking (#150). Promotion target is an internal rebuildable metadata layer only.

### #221 — PATCH-DOTNET-001 — reversible .NET semantic normalization

Question: after the generic executable G4 baseline is complete, how much additional headroom exists in CLR metadata/IL/RVA/ReadyToRun-aware canonicalization?

Priority: **NEXT / high differentiation**. Dependency: **completion of G4 evidence from #183 regardless of ADOPT/DEFER/REJECT**. Evidence must include eligible-subset and corpus-weighted total physical patch impact, including transform metadata.

### #222 — PATCH-DICT-001 — bounded composite multi-run dictionaries

Question: at the same total dictionary-byte budget, is a deterministic composition of disjoint verified base chunk runs materially better than one contiguous run?

Priority: **NEXT / high algorithmic value**. Dependencies: #183 G1/G2 results; #184 before cross-file lanes. The first experiment preserves ChunkId-based base addressing; arbitrary byte ranges require a later ExperimentId/RFC.

### #223 — PATCH-ML-001 — learned reference ranking oracle

Question: how much candidate-search headroom remains after PATCH-ENC-005, and can learned ranking expose useful features that should be distilled into a simpler deterministic scorer?

Priority: **EXPERIMENT**. Blocked by #181 result-bearing selector work and #183 G2 comparison. No ML requirement is allowed in apply; a retained model must demonstrate deterministic canonical candidate selection on the supported execution set or remain advisory/research-only.

### #224 — PATCH-COMPILER-001 — deterministic cost-aware representation compiler

Question: once multiple representation families have decision-grade tradeoff evidence, can one versioned BuildPolicy choose a better end-to-end representation than independent local byte-minimizing heuristics?

Priority: **NEXT / architectural**, but online implementation is gated. Entry gate: at least two distinct representation families with decision-grade evidence. Phase A is offline replay; wall/CPU/network measurements inform policy design only and are not dynamic artifact-selection inputs.

### #225 — PATCH-EXEC-001 — local adaptive/cacheable deterministic compilation

Question: can expensive builder work become deterministic local tasks that are hardware-aware, cacheable and retryable without scheduler/cache behavior changing semantic selection?

Priority: **EXPERIMENT after #181 execution shape settles**. #220 caches derived features; #225 caches validated pure task results. Remote/distributed execution, if justified later, gets a new ExperimentId.

## 5. Dependency graph

```text
current frozen work
  |
  +--> #181 PATCH-ENC-005 --------------------+--> #223 PATCH-ML-001
  |                                           |
  |                                           +--> #225 PATCH-EXEC-001
  |
  +--> #183 PATCH-GAP-001 -----+
  |                            +--> #221 PATCH-DOTNET-001
  |                            +--> #222 PATCH-DICT-001
  |                            `--> #223 PATCH-ML-001
  |
  +--> #150 PATCH-INCR-001 ----> shared plumbing only with #220
  |
  +--> #220 PATCH-META-001 ----> invalidation/keying principles for #225
  |
  +--> #184 PATCH-TREE-001 ----> cross-file lanes in #220/#222

>= 2 decision-grade representation families
  |
  v
#224 PATCH-COMPILER-001
```

#224's BuildPolicy/result-record shape may later consume #225's local task model.

## 6. Technology radar

### NOW

- finish PATCH-ENC-005 and PATCH-GAP-001 exactly as frozen;
- prepare PATCH-META-001 protocol/design;
- retain exact/deterministic evidence discipline.

### NEXT

- .NET semantic normalization after generic G4 evidence completes;
- composite multi-run dictionaries after G1/G2 evidence;
- cost-aware compiler only after its representation-family entry gate passes.

### EXPERIMENT

- learned ranking / distillation;
- local adaptive/cacheable task execution.

### WATCH

- distributed build workers as a future separate ExperimentId after local task economics are proven;
- model/tensor-format-aware reversible transforms;
- privacy-preserving dedup only with a real threat model;
- hardware/GPU offload only if CPU becomes a measured dominant bottleneck;
- version-graph optimization after #184 establishes update-set semantics.

### REJECT / NOT NOW

- LLM-as-codec;
- neural compression in CSP;
- arbitrary recursive delta chains;
- new CDC profile without #14/#136 evidence;
- separate "hierarchical patch block" format duplicating Repository packs/range planning;
- ML/ANN dependency in apply;
- public generic plugin codec/executor interface before concrete consumer evidence;
- ZK/TEE machinery without a product threat model.

## 7. Promotion rules

Every new research question must:

1. own an ExperimentId;
2. freeze the hypothesis and decision rule before final measurements;
3. preserve raw/evidence provenance under `docs/research`;
4. separate tuning data from fresh confirmation where model/heuristic selection is involved;
5. report negative results;
6. avoid persisted-format/API changes until evidence justifies a separate RFC.

Physical byte equality may be used only as a same-build experiment oracle where explicitly frozen; it does not supersede D7.

## 8. Cross-cutting metrics

Where relevant, report total physical patch/wire bytes, create/apply wall + CPU, peak RSS/bounds, allocations, source/base bytes read, seek/range count, request/overfetch estimates, exact reconstruction/identity results, semantic-selection reproducibility, per-family regressions, and metadata/index/model overhead.

A same-build artifact digest is recorded only where the protocol declares it a transparency oracle. Do not accept a candidate on compression ratio alone when it materially shifts I/O, memory or apply cost.

## 9. Three-stage R&D sequence

### Stage A — reuse computation

Primary: #220; local task execution work in #225 may follow #181 when it can reuse the same invalidation/keying discipline.

Goal: stop recomputing information/results we can safely derive, identify and validate.

### Stage B — improve representation quality

Primary: #221 and #222; #223 as an oracle/headroom study.

Goal: improve what candidate representations the builder can see without weakening verification.

### Stage C — compile globally

Primary: #224.

Goal: choose among independently proven representations by a versioned deterministic BuildPolicy. Online compiler work starts only after the entry gate passes.

## 10. What would constitute defensible ChunkShift know-how

No claim of novelty is made here.

A technically differentiable system would require measured evidence that the combination provides benefits unavailable from a straightforward CDC + zstd/delta pipeline, for example:

- exact reusable metadata that materially removes repeated builder work;
- deterministic typed semantic transforms for important workload families;
- bounded source composition that improves bytes without destroying locality;
- a reproducible multi-objective BuildPolicy using static/reproducible system-cost proxies;
- validated task execution/caching that preserves semantic selection across hardware/scheduling.

Any external claim of invention, uniqueness or patentability requires a separate prior-art and patent search.

## 11. Research anchors

- SkySync, FAST '26:
  https://www.usenix.org/conference/fast26/presentation/zhang-zhihao
- DeepSketch, FAST '22:
  https://www.usenix.org/conference/fast22/presentation/park
- LoopDelta, USENIX ATC '23:
  https://www.usenix.org/conference/atc23/presentation/zhang-yucheng
- Chromium Zucchini:
  https://chromium.googlesource.com/chromium/src/+/HEAD/components/zucchini/
- Hugging Face Xet:
  https://huggingface.co/docs/hub/en/xet/deduplication
