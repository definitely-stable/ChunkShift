# Patching R&D roadmap

Status: Active research roadmap  
Last reviewed: 2026-10-02  
Architecture: [RFC-0005](../architecture/RFC-0005-patch-compiler-architecture.md)

## 1. Purpose

This file tracks post-CSP-v1 research that may improve patch construction, patch size, update-set behavior or future Repository integration without silently changing shipped/frozen semantics.

It is intentionally separate from:

- `PLAN.md` — executable milestone acceptance;
- `ROADMAP.md` — product sequencing;
- frozen benchmark protocols — exact experiment contracts;
- RFCs — durable architecture/persisted semantics.

## 2. Research thesis

ChunkShift should make the **builder** progressively smarter while keeping the **artifact and applier** small, deterministic, bounded and independently verifiable.

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
deterministic cost-aware compiler
      |
      v
canonical declarative patch/update set
      |
      v
bounded exact apply + verification
```

The potential long-term differentiation is the composition of these layers, not any single known algorithm.

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

### #220 — PATCH-META-001 — reusable derived patch features

Question: can repeated patch builds reuse deterministic, disposable features and avoid recomputation while producing identical semantics?

Priority: **NOW / high**.

Why first:

- low wire/apply compatibility risk;
- directly composes with #150/#181/#184;
- FAST '26 SkySync provides strong evidence that metadata reuse can remove synchronization-side compute work;
- successful infrastructure can feed later ranking/transform experiments.

Promotion target: internal derived metadata layer only; no stable format unless a later consumer justifies one.

### #221 — PATCH-DOTNET-001 — reversible .NET semantic normalization

Question: after generic executable normalization, how much additional patch-size headroom exists in CLR metadata/IL/RVA/ReadyToRun-aware canonicalization?

Priority: **NEXT / high differentiation**.

Dependency: useful/complete G4 evidence from #183.

Potential value: a .NET-focused specialization aligned with ChunkShift's ecosystem that generic byte-level delta tools do not necessarily optimize for.

Promotion target: only after strong holdout results and a dedicated persisted-format RFC.

### #222 — PATCH-DICT-001 — bounded composite multi-range dictionaries

Question: at the same total dictionary-byte budget, is a deterministic set of disjoint high-value ranges materially better than one contiguous source window?

Priority: **NEXT / high algorithmic value**.

Dependencies: #183 G1/G2 results; #184 before cross-file lanes.

Key constraint: patch bytes are measured together with base-read/range/locality cost.

### #223 — PATCH-ML-001 — learned reference ranking oracle

Question: how much candidate-search headroom remains after PATCH-ENC-005, and can learned ranking expose useful features that should be distilled into a simpler deterministic scorer?

Priority: **EXPERIMENT**.

Blocked by: #181 merged/result-bearing selector work and #183 G2 comparison.

Preferred success path:

```text
learned model
 -> discover useful feature structure
 -> distilled deterministic scorer
 -> production candidate
```

No ML requirement is allowed in apply.

### #224 — PATCH-COMPILER-001 — deterministic cost-aware representation compiler

Question: once multiple representation families exist, can one canonical planner choose a better end-to-end representation than independent local byte-minimizing heuristics?

Priority: **NEXT / architectural**.

Start with offline replay over existing evidence. Do not add a new wire encoding in Phase A.

Candidate costs include bytes, create/apply CPU, apply memory, base reads/ranges and future remote request/locality effects.

### #225 — PATCH-EXEC-001 — adaptive/cacheable deterministic compilation

Question: can expensive builder work become deterministic tasks that are hardware-aware, cacheable, retryable and later remotely executable without scheduler-dependent patch bytes?

Priority: **EXPERIMENT after #181 execution shape settles**.

Phase A is local adaptive execution + content-addressed result cache. Remote workers are explicitly deferred.

## 5. Dependency graph

```text
current frozen work
  |
  +--> #181 PATCH-ENC-005 -------------------+
  |                                          |
  +--> #183 PATCH-GAP-001 -----+             |
  |                            |             |
  |                            +--> #221 PATCH-DOTNET-001
  |                            +--> #222 PATCH-DICT-001
  |                            +--> #223 PATCH-ML-001 <--- #181
  |
  +--> #150 PATCH-INCR-001 ----+--> #220 PATCH-META-001
  |
  +--> #184 PATCH-TREE-001 ----+--> cross-file lanes in #220/#222
  |
  +--> >=2 useful representation families
                               |
                               v
                         #224 PATCH-COMPILER-001
                               |
                               v
                         #225 PATCH-EXEC-001
```

#225 may prototype local task execution earlier, but any production-facing compiler cache/distribution design should be informed by #224's representation/result-record shape.

## 6. Technology radar

### NOW

- finish PATCH-ENC-005 and PATCH-GAP-001 exactly as frozen;
- PATCH-META-001 protocol/design;
- retain exact/deterministic evidence discipline;
- keep cross-links to #150/#184/Repository.

### NEXT

- .NET semantic normalization after generic G4 evidence;
- composite multi-range dictionaries after G1/G2 evidence;
- cost-aware compiler once at least two representation families have meaningful data.

### EXPERIMENT

- learned ranking / distillation;
- adaptive/cacheable compiler execution;
- remote build workers only after local deterministic task economics are proven.

### WATCH

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

## 8. Cross-cutting metrics

Where relevant, report all of:

- patch/wire bytes;
- create wall + CPU;
- apply wall + CPU;
- peak RSS / explicit bound;
- allocations;
- source/base bytes read;
- seek/range count;
- request/overfetch estimate;
- exact reconstruction/identity result;
- deterministic artifact digest;
- per-product-family regressions;
- metadata/index/model overhead.

Do not accept a candidate on compression ratio alone when it materially shifts I/O, memory or apply cost.

## 9. Three-stage R&D sequence

### Stage A — reuse computation

Primary: #220.

Goal: stop recomputing information we can safely derive once and invalidate exactly.

### Stage B — improve representation quality

Primary: #221 and #222; #223 as an oracle/headroom study.

Goal: improve what candidate representations the builder can see.

### Stage C — compile and execute globally

Primary: #224 and #225.

Goal: choose among useful representations by end-to-end cost and execute the build efficiently without sacrificing determinism.

## 10. What would constitute defensible ChunkShift know-how

No claim of novelty is made here.

A technically differentiable system would require measured evidence that the combination provides benefits unavailable from a straightforward CDC + zstd/delta pipeline, for example:

- exact reusable metadata that materially removes repeated builder work;
- deterministic typed semantic transforms for important workload families;
- bounded source composition that improves bytes without destroying locality;
- a reproducible multi-objective compiler that selects representations using system-level costs;
- canonical task execution/caching that preserves identical artifacts across hardware/scheduling.

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
