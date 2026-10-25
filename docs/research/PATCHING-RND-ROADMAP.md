# Patching R&D roadmap

Status: Active research roadmap  
Last reviewed: 2026-10-25  
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
| Create candidate selection / resemblance / level ladder | #181 / `PATCH-ENC-005` — complete: REJECT / `STOP_RESEMBLANCE`; D15 unchanged |
| CSP size-gap decomposition, generic executable/container factors | #183 / `PATCH-GAP-001` — G1 REJECT; G3 REJECT; G4-BCJ REJECT (0.955265% holdout vs 15%); Zucchini subset reference pending |
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

### #262 — PATCH-RESIDUAL-001 — exact bounded residual representation oracle

Priority: **PHASE A now / Phase B after frozen G4 evidence**. A separate, lab-only representation question: can XOR+zstd or canonical sparse changed-runs over exact verified equal-length base bytes recover headroom not available to existing zstd dictionary candidate selection? A synthetic 85-byte RS01 header, strict decode and corruption tests are a **mechanics foundation only**, not a CSP size result or wire format. Phase B needs a new preregistered matched H0/whole-patch accounting protocol and sealed holdout before any persisted-format RFC. Owner [#262](https://github.com/definitely-stable/ChunkShift/issues/262), [contract](../benchmarks/PATCH-RESIDUAL-001-PROTOCOL.md). Opening this lane does not extend #251 indefinitely: oracle-first and explicit DEFER/REJECT if no decision-grade headroom.

### Critical architecture corrections (2026-10-08)

- **#150 exact incremental manifest:** unchanged logical CSM records can be reused without rehashing source bytes, but current `ManifestIdAccumulator` sequentially hashes every logical record. A Merkle sidecar or external record index does not make existing ManifestId directly composable. Separate source-byte read savings from manifest-record/finalization work.
- **#184 cross-file source:** current CSP apply materializes a `Dictionary<ChunkId,ChunkInfo>` with an operational bound of 4,194,304 distinct base IDs. A concatenated tree cannot silently inherit arbitrary scale; require a bounded exact locator and memory/build/read measurements before a format choice. Keep the first-occurrence semantic rule.
- **#184 T4-C3A publisher authority (2026-10-22):** [decision record](results/PATCH-TREE-001-T4C3-PUBLISHER-BOUNDARY-DECISION-20261022-001.md) applies accepted RFC-0006: external host owns trusted namespace, staging, atomic selector qualification, reader pins, recovery and durability. **REJECT** inode/FileId preflight as atomic authorization; **DEFER** first-party OS publisher and tree API; **NO_PRODUCTION_ADOPT**. T2/T3 and portable tree metadata remain open under #184/#251, with T0 baseline.
- **#184 T3-A exact cross-path (2026-10-23):** [pre-registered lab protocol](../benchmarks/PATCH-TREE-001-T3A-EXACT-CROSS-PATH-PROTOCOL.md); deterministic full-file SHA/length matches for added paths and charged diagnostic reference map, independent synthetic full-target reconstruction; **NOT** real corpus or CSP physical evidence. T3-B frozen calibration and changed-renamed physical CSP remain gated, #184/#251 OPEN.
- **#184 T3-B0 locked real census (2026-10-23):** [preregistered protocol](../benchmarks/PATCH-TREE-001-T3B0-EXACT-REAL-CALIBRATION.md) and original 4-pair .NET calibration after merge; detect exact moved/copied added paths, charge actual diagnostic map, compare a strictly labelled cost substitution against accepted T0 warm baseline. **No new CSP physical measurement, sealed holdout, T3-B1 ADOPT or publisher**; decision record requires successful original hosted evidence.
- **#184 T3-B0 result / next T3-B1:** [original T3-B0 audit](results/PATCH-TREE-001-T3B0-ORIGINAL-CENSUS-EVIDENCE-20261023-001.md) confirms **0 exact old-file matches** for 2 added paths across 4 original .NET pairs; **0 B** T3-A savings against T0 warm baseline. Exact-added branch is NO_ADOPT; next consider the two version-coded mscordaccore changed-renames under a separately frozen real CSP create/apply cost protocol, including compressed T0S baseline and locator costs. #184/#251 still OPEN.
- **#184 T3-B1 actual CSP compare:** [frozen two-DLL protocol](../benchmarks/PATCH-TREE-001-T3B1-RENAMED-CSP-PROTOCOL.md): original full SHA-locked .NET calibration, create/apply with real shipping CSP v1 and exact old-file CSM; compare raw T0, fresh T0S compressed and T3 warm/cold including a charged path reference. No CSP/API changes, no holdout or production decision until original run and independent audit.
- **#184 T3-B1 real result and next T3-B2:** [original two-DLL audit](results/PATCH-TREE-001-T3B1-ORIGINAL-PHYSICAL-EVIDENCE-20261023-001.md): real shipping CSP create/apply and F0 physical audit passed both source-locked renamed DLLs; T3 warm 92,671 B (including 590 B references) vs compressed T0S 1,097,577 B, cold 94,899 B; modelled four-pair T0 14,758,982→12,138,381 B. Strong size gain, **but T3 creation about 4–5x longer in one-shot wall observations**. B1_PROMISING_CALIBRATION_ONLY / NO_ADOPT. Next freeze T3-B2 cross-file candidate selection, repeated create CPU/RSS and locator cost; #184/#251 remain OPEN.
- **#184 T3-B2A conservative source selector:** [pre-registered protocol](../benchmarks/PATCH-TREE-001-T3B2A-STRUCTURAL-RENAME-PROTOCOL.md) implements bounded structural removed→added DLL matching, no arbitrary ties, one-to-one resource and path guards. Original four-pair identity cross-check is main-only, to be validated from independent original pinned B1 oracle. T3-B2B repeated CPU/RSS, source-read/CSM costs and physical transport admission remain open. No production API, format, publisher or holdout work.
- **#184 T3-B2A accepted original calibration / next T3-B2B:** [original audited evidence](results/PATCH-TREE-001-T3B2A-ORIGINAL-STRUCTURAL-EVIDENCE-20261023-001.md): bounded low-recall removed→added versioned DLL policy independently recovered **both** B1 mscordaccore sources, **0** ambiguity on four pinned original .NET pairs; locator diagnostic floor 590 B, **not** physical update-set. Original run #39977481690 and SHA-locked ZIP audited. CALIBRATION_ONLY_NO_ADOPT. Next B2B gate is repeated create CPU/RSS/reads/CSM costs plus measured raw/T0S fallback and complete update-set cost; #184/#251 OPEN.
- **#184 T3-B2B repeat-create and admission gate:** [preregistered protocol](../benchmarks/PATCH-TREE-001-T3B2B-REPEAT-CREATE-ADMISSION-PROTOCOL.md) compares existing public SDK real T0S/T3 create on the same two original mscordaccore DLLs; one warmup then 5 alternating paired rounds. Record per-lane whole-process CPU time, wall time, sampled process WorkingSet/private committed (not private RSS), GC allocation and API source reads, independent CSM setup cost, exact apply. Research-only size+3x CPU/wall admission with T0/T0S fallback; NO production ADOPT and no holdout, source locator, CSP/API or publisher changes.
- **#184 T3-B2B actual repeated original verdict / next T3-B2C:** [locked real five-round evidence](results/PATCH-TREE-001-T3B2B-ORIGINAL-REPEAT-CREATE-EVIDENCE-20261024-001.md): both original renamed DLLs retain B1 strong physical size gain, but T3/T0S median process CPU **11.70x / 4.19x** and median wall **12.99x / 4.27x**, exceeding frozen **3x** research budget. Automatic lab admission **FALLBACK_T0_OR_T0S__CREATE_BUDGET** for each. T3 old-source Stream logical read amplification **~26.6x / ~10.7x** per create; next B2C should isolate base-read amplification and creation CPU stages, not add a shipping rename locator without broader cost and trust evidence. CALIBRATION_ONLY_NO_ADOPT, #184/#251 OPEN.
- **#184 T3-B2C0 source Stream topology:** [preregistered bounded protocol](../benchmarks/PATCH-TREE-001-T3B2C0-BASE-READ-TOPOLOGY-PROTOCOL.md), initial passive-trace slice: 3 paired counter-only/traced real CSP create/apply rounds on two source-locked renamed DLLs. Count unique physical file-offset coverage vs repeated logical Stream reads, backward seeks and observer cost; keep source/payload bytes and CSP unchanged. No production adoption or claim of disk IO/encoder CPU attribution; subsequent B2C1 only after audited original evidence.
- **#184 T3-B2C1 H2/H3 controlled cache ablation:** [fixed C1 protocol](../benchmarks/PATCH-TREE-001-T3B2C1-SOURCE-RANGE-PROTOCOL.md) follows passive C0 source-read topology with shipping H2-W2 no cache versus existing internal H3-W2 bounded candidate cache; source-range exact union, duplicate read ratio, CSM stage outside create, 3 alternating original rounds, matched CSP SHA/apply, CPU and cache bytes. NO production default/CSP change or holdout; original [independently audited result](results/PATCH-TREE-001-T3B2C1-ORIGINAL-H2H3-EVIDENCE-20261025-001.md) shows H3 saves 96.25%/90.66% Stream-level base rereads yet H3/H2 CPU is 1.0087/0.9276, so **NO_CPU_WIN / NO_PRODUCTION_ADOPT**, #184/#251 OPEN.
- **#220 derived features:** never treat stale cache entries or incomplete source-change metadata as integrity evidence. Missing/corrupt caches fall back to authoritative bytes/CSM.
- **#181 selector:** choosing a different dictionary candidate within the same representation family showed negligible exact-oracle headroom; do not claim this rules out other representation families.
- **#183:** G1/G3 and G4-BCJ are REJECT on their frozen lanes; the G4 Zucchini reference remains pending. No residual trial is injected into its calibration/evaluation lanes.
- **Build I/O/apply:** inspect whole-workflow double-reading, temporary spool costs and forward-only CSP apply feasibility as candidate engineering investigations *inside existing owners* before opening new public APIs. Frozen CSP v1 physical order (TCSM before PAYL), verification order and bounded-apply semantics stay authoritative.

### #223 — PATCH-ML-001 — learned reference ranking oracle

Question: after #184/#222 and/or transform work creates a richer bounded source/representation universe, how much exact-oracle selection headroom remains, and can learned ranking expose useful features that should be distilled into a simpler deterministic scorer?

Priority: **EXPERIMENT / gated**. Do not train against the exhausted old single-contiguous-dictionary universe: G2 exact whole-base search saved only 174 bytes over 974,223 B and H6-O missed the frozen byte gate. Start only after a new bounded universe exists and an exact oracle first demonstrates material attainable headroom. No ML requirement is allowed in apply; a retained model must demonstrate deterministic canonical candidate selection on the supported execution set or remain advisory/research-only.

### #224 — PATCH-COMPILER-001 — deterministic cost-aware representation compiler

Question: once multiple representation families have decision-grade tradeoff evidence, can one versioned BuildPolicy choose a better end-to-end representation than independent local byte-minimizing heuristics?

Priority: **NEXT / architectural**, but online implementation is gated. Entry gate: at least two distinct representation families with decision-grade evidence. Phase A is offline replay; wall/CPU/network measurements inform policy design only and are not dynamic artifact-selection inputs.

### #225 — PATCH-EXEC-001 — local adaptive/cacheable deterministic compilation

Question: can expensive builder work become deterministic local tasks that are hardware-aware, cacheable and retryable without scheduler/cache behavior changing semantic selection?

Priority: **DESIGN now / implementation later**. PATCH-ENC-005 is closed as a candidate-selection research program with durable negative evidence; representation/task shape may still change under #251. #220 caches derived features; #225 caches validated pure task results. Freeze task identities only after #183/#184/#221/#222/#224 establish the work units worth caching. Remote/distributed execution, if justified later, gets a new ExperimentId.

## 5. Dependency graph

```text
#181 PATCH-ENC-005
  |
  `--> COMPLETE: REJECT / STOP_RESEMBLANCE / D15 unchanged

#183 PATCH-GAP-001 -----+--> #222 PATCH-DICT-001
  |                     |
  `--> G4 evidence -----+--> #221 PATCH-DOTNET-001

#184 PATCH-TREE-001 ----> cross-file lanes in #220/#222
#150 PATCH-INCR-001 ----> shared plumbing only with #220
#220 PATCH-META-001 ----> invalidation/keying principles for #225

new proven source/representation universe
  |
  `--> exact oracle first --> #223 PATCH-ML-001

>= 2 decision-grade representation families
  |
  v
#224 PATCH-COMPILER-001
  |
stable task/representation shape + #220 keying
  |
  v
#225 PATCH-EXEC-001
  |
  v
#251 final research synthesis --> #140 / PublicAPI freeze / publication
```

#224's BuildPolicy/result-record shape may later consume #225's local task model.

## 6. Technology radar

### DONE

- PATCH-ENC-005: REJECT / `STOP_RESEMBLANCE`; G2 MISS and H6-O guard MISS; D15 unchanged.

### NOW

- continue PATCH-GAP-001 exactly as frozen: G1, G3 and G4-BCJ are complete/rejected; finish the pinned Zucchini reference and final #183 factor synthesis before closing the study;
- run PATCH-TREE-001, PATCH-META-001 and PATCH-INCR-001 as compatible #251 Research Freeze work;
- retain exact/deterministic evidence discipline and fail-closed invalid-run handling.

### NEXT

- composite multi-run dictionaries use the completed negative G1/G2 evidence; require #184 before any cross-file lanes;
- .NET semantic normalization after generic G4 evidence;
- required interaction experiments when multiple individually useful factors are not safely composable.

### EXPERIMENT / GATED

- learned ranking only after a richer source/representation universe has material exact-oracle headroom;
- cost-aware compiler only after its >=2 representation-family entry gate passes;
- local adaptive/cacheable task execution only after task/representation shape is stable.

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

Research promotion to a first public Patching contract is now gated by [#251](https://github.com/definitely-stable/ChunkShift/issues/251). Required experiments may finish as ADOPT, DEFER or REJECT, but #140/PublicAPI/publication remain blocked until required dispositions, cross-factor interaction studies and the final CSP v1 vs CSP vNext synthesis are complete. Repository-only physical storage research is outside this gate unless Patching evidence creates a direct dependency.

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

Primary: #221 and #222; #262 as an exact residual-oracle foundation; #223 only if demonstrated headroom.

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
