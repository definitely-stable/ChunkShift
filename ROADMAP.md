# ChunkShift roadmap

Status: Active  
Last reviewed: 2026-10-07

Authority:

- architecture/persisted contracts: [RFC-0001](docs/architecture/RFC-0001-target-architecture-2026.md), [RFC-0002](docs/architecture/RFC-0002-embedded-sdk-aspnet-core.md), [RFC-0003](docs/architecture/RFC-0003-core-first-release.md), [RFC-0004](docs/architecture/RFC-0004-core-0.1-deferred-product-concerns.md);
- executable milestone acceptance: [PLAN.md](PLAN.md);
- live completion tracking: [#1](https://github.com/definitely-stable/ChunkShift/issues/1);
- package/tag policy: [docs/RELEASES.md](docs/RELEASES.md); packages, tags and releases are produced by the publication repository [MrFr3di/ChunkShift](https://github.com/MrFr3di/ChunkShift);
- measurement methodology: [docs/benchmarks/M0-LAB.md](docs/benchmarks/M0-LAB.md).

This file owns **program order, dependencies and release gates**. It does not duplicate RFC byte layouts or issue-level implementation detail.

## Product sequence

The first public release, published on 2026-09-27, is the standalone Core package:

```text
bytes / Stream
   |
deterministic chunking + HashSuite
   |
raw chunk scanner
   |
CSM create / read / verify
   |
ChunkShift Core 0.1.0
```

After Core:

```text
Core 0.1.0
   |
compare / diff / reuse analysis
   |
CSP create / apply
   |
exact target reconstruction
   |
ChunkShift.Patching
   |
Repository / remote distribution
```

Compare/diff and CSP are intentionally **not** Core 0.1.0 requirements. RFC-0003 owns this sequencing decision.

## Current state

Completed foundation:

- [#2](https://github.com/definitely-stable/ChunkShift/issues/2) — identity, HashSuite and ProfileFingerprint semantics; merged via PR #30;
- [#3](https://github.com/definitely-stable/ChunkShift/issues/3) — deterministic benchmark lab/corpus/mutation foundation; merged via PR #31;
- M0 status synchronization — PR #32.

Completed corrective preparation:

- [#33](https://github.com/definitely-stable/ChunkShift/issues/33) — exact profile numeric canonicalization, stronger benchmark evidence and Core-first release correction; merged via PR #35.

Completed semantic preflight: [#37](https://github.com/definitely-stable/ChunkShift/issues/37) hardened measurement-oracle correctness, exact FastCDC candidate semantics and x64/ARM64 deterministic evidence; merged via PR #38.

Completed Core kernel implementation: [#4](https://github.com/definitely-stable/ChunkShift/issues/4) added scalar FastCDC/fixed reference kernels, bounded stream processing, HashSuite chunk identities, golden/segmentation tests and x64/ARM64 determinism; merged via PR #40.

Completed post-kernel hardening: [#42](https://github.com/definitely-stable/ChunkShift/issues/42) closed the confirmed FastCDC/kernel review gaps before downstream consumption.

Completed raw streaming/API evidence: [#16](https://github.com/definitely-stable/ChunkShift/issues/16) delivered the bounded raw scanner and [#20](https://github.com/definitely-stable/ChunkShift/issues/20) froze the evidence-selected callback/borrowed-memory public shape.

Completed CSM candidate: [#5](https://github.com/definitely-stable/ChunkShift/issues/5) delivers bounded create/read/verify, concrete streaming ManifestReader, strict corruption/resource handling, complete golden compatibility fixtures and CLI create/inspect/verify via PR #52.

Completed review hardening (PRs #53–#62, #70, #72, #80–#82, #85, #86): CSM reader/kernel defects from the external review fixed, untested MUST-reject rules and truncation matrices covered, CLI source-overwrite fixed, a real-path package smoke run under JIT in CI and JIT + NativeAOT on x64/ARM64 in heavy validation.

Completed lab infrastructure stage: [#67](https://github.com/definitely-stable/ChunkShift/issues/67) — the lab times the streaming kernel consumers actually run (PR #77), reads the maximum chunk size from the profile (PR #78), reports resync coverage per profile, mean/target and sample spread (PR #84), counts bytes copied (PR #87), compares evidence only within one commit (PR #88), records boundary-scan hardware-counter evidence (PR #92) and can isolate each experiment in its own process (PR #93).

Completed release-evidence stage: [#68](https://github.com/definitely-stable/ChunkShift/issues/68) — independent Python CSM decoder and committed conformance vectors (PR #73), deterministic mutational reader fuzzing with differential decoding (PR #74), cancellation on every manifest API (PR #71), coverage artifact (PR #75) and a 4 GiB source under a 128 MiB memory limit on x64/ARM64 NativeAOT (PR #76). Release hygiene (PRs #79, #91), console and ASP.NET Core package-consumer samples (PR #89) and a README quickstart (PR #90) also landed.

**Where `main` is now:** ChunkShift Core `0.1.0` is published on NuGet.org (2026-09-27) from the publication repository, [MrFr3di/ChunkShift](https://github.com/MrFr3di/ChunkShift). Its source contract — deterministic chunking, bounded raw scanner, CSM create/read/verify, the stable FastCDC profile and the real-Kestrel host proof — was frozen here and ported unchanged. `main` tracks the shipped surface as its compatibility baseline (`PublicAPI.Shipped.txt`, `PackageValidationBaselineVersion=0.1.0`). Nothing is released from this repository.

- [#6](https://github.com/definitely-stable/ChunkShift/issues/6) is **closed**: API/NativeAOT freeze. The API surface decisions of [#63](https://github.com/definitely-stable/ChunkShift/issues/63) are done (PRs #95, #96). [#65](https://github.com/definitely-stable/ChunkShift/issues/65) is decided by [RFC-0004](docs/architecture/RFC-0004-core-0.1-deferred-product-concerns.md) (no progress/compression/signature/anti-rollback surface in Core 0.1.0). The audit is recorded in [CORE-0.1-API-FREEZE.md](docs/architecture/CORE-0.1-API-FREEZE.md); the frozen surface shipped as `0.1.0`;
- [#8](https://github.com/definitely-stable/ChunkShift/issues/8) is **closed**: PR #113 froze `fastcdc.gear.chunkshift.v1.64k` and its persisted semantic identity after the real-corpus/runtime evidence;
- [#17](https://github.com/definitely-stable/ChunkShift/issues/17) is **closed**: PR #114 proved the direct `HttpRequest.Body`/Kestrel host path and recorded the Body-vs-BodyReader decision;
- [#9](https://github.com/definitely-stable/ChunkShift/issues/9) and [#69](https://github.com/definitely-stable/ChunkShift/issues/69) are **complete in the publication repository**: `0.1.0` was released there directly (no preview), the CLI is not published, and `0.1.0` is the package-validation baseline;
- [#24](https://github.com/definitely-stable/ChunkShift/issues/24): publication-specific enforcement is separate and active on the publication repository. The engineering-repository governance issue remains open; `main` is currently not protected by a GitHub branch-protection rule.
- [#152](https://github.com/definitely-stable/ChunkShift/issues/152) is the parallel Core 0.1.1 maintenance/evidence track. It preserves the published 0.1.0 API/profile/format identities by default and does not replace the current #7 Patching product phase.
- [#7](https://github.com/definitely-stable/ChunkShift/issues/7) Patching: CSP v1 is frozen (PR #178, 2026-09-28) and the plan/create/apply loop, the engineering CLI, the independent decoder, fuzzing and the NativeAOT package consumer are on `main`. The remaining exit items are listed under [Patching gate](#patching-gate).

- Patching R&D frontier: [PATCHING-RND-ROADMAP](docs/research/PATCHING-RND-ROADMAP.md) routes work through #220–#225; [RFC-0005](docs/architecture/RFC-0005-patch-compiler-architecture.md) remains **Proposed**. As of 2026-10-04, [#251](https://github.com/definitely-stable/ChunkShift/issues/251) is the formal Research Freeze gate: experiments that can change the first public Patching representation/source/update-set/build contract must reach durable ADOPT/DEFER/REJECT dispositions before CLI/PublicAPI/publication freeze.

## Critical path

```text
#2 identity semantics ──┐
                       ├─> #33 corrective preparation
#3 measurement lab ────┘            |
                                    v
                         #37 M1 semantic preflight
                                    |
                                    v
                         #4 deterministic kernels
                           /                 \
                          v                   v
                       #5 CSM             #16 scanner
                          \                   |
                           \                  v
                            +--------------> #20 API bake-off
                             \                /
                              \              /
                               +----> #6 minimal Core API
                                      |    (#65 decided: RFC-0004)
                         +------------+------------+
                         |                         |
                         v                         v
                     #8 profile                #17 ASP.NET
                       bake-off                 Core host proof
                    (#99 semantics                 |
                        settled)                   |
                         |                         |
                         +------------+------------+
                                      |
                                      v
                         frozen Core source contract ---> ported to MrFr3di/ChunkShift:
                                      |                       Core 0.1.0 published
                                      v
                          #7 Patching/CSP loop
                                      |
                         +------------+------------+
                         |                         |
                         v                         v
                 #10 Repository               #18 optional
                    foundation                ASP.NET package
                         |
                         v
                 #11 index/catalog
                         |
                         v
                 #12 GC/repack
                         |
                         v
                 #13 HTTP/S3/R2
```

[#14](https://github.com/definitely-stable/ChunkShift/issues/14) remains a parallel research track and cannot silently alter a published profile/format.

The patching frontier in #220–#225 is likewise parallel research. Promotion follows the experiment registry and existing accepted RFCs; proposed RFC-0005 is informative until separately accepted. No result becomes a persisted CSP/API semantic without a separate evidence-backed decision.

Core 0.1.1 maintenance runs in parallel after the published Core node:

```text
Core 0.1.0 published
      |
      +----> #152 Core 0.1.1 maintenance
               |- #127 BLAKE3 backend decision
               |- #137 scanner differential fuzz
               |- #153 frozen-profile scanner/backend evidence
               |     (SCAN-002 lanes, SCAN-006 hash parallelism,
               |      SCAN-007 parallel cutting of seekable sources, JIT-001)
               |- #154 CSM hot-path evidence (manifest-only operations)
               `- #186 additive entry points: integrity-only verify,
                     hash-free boundaries, non-Stream sources
```

This branch does not block #7. Only adopted, compatibility-preserving changes are ported to the publication repository for a future 0.1.1.

## Delivery stages

| Stage | Issues | Outcome | Gate |
| --- | --- | --- | --- |
| M0 foundation | [#2](https://github.com/definitely-stable/ChunkShift/issues/2), [#3](https://github.com/definitely-stable/ChunkShift/issues/3) | correct identity model + reproducible lab | complete |
| Preparation | [#33](https://github.com/definitely-stable/ChunkShift/issues/33) | known M0 defects fixed; Core-first plan executable | regression tests + docs/issues synchronized |
| Core kernels | [#4](https://github.com/definitely-stable/ChunkShift/issues/4) | scalar deterministic FastCDC/fixed reference + HashSuite kernel | segmentation-independent boundaries/IDs |
| Core streaming/manifest | [#5](https://github.com/definitely-stable/ChunkShift/issues/5), [#16](https://github.com/definitely-stable/ChunkShift/issues/16) | CSM create/read/verify + bounded raw scanner | complete |
| Core API evidence | [#20](https://github.com/definitely-stable/ChunkShift/issues/20), [#6](https://github.com/definitely-stable/ChunkShift/issues/6) | smallest evidence-selected public Core candidate | #20, [#63](https://github.com/definitely-stable/ChunkShift/issues/63), [#64](https://github.com/definitely-stable/ChunkShift/issues/64) and [#65](https://github.com/definitely-stable/ChunkShift/issues/65) complete; [#6](https://github.com/definitely-stable/ChunkShift/issues/6) closed (surface shipped as 0.1.0 and mirrored as the Shipped baseline) |
| Evidence infrastructure | [#67](https://github.com/definitely-stable/ChunkShift/issues/67), [#68](https://github.com/definitely-stable/ChunkShift/issues/68) | streaming-lane lab; fuzz, independent decoder, cancellation, coverage, >RAM | complete |
| Core source-contract evidence | [#8](https://github.com/definitely-stable/ChunkShift/issues/8), [#17](https://github.com/definitely-stable/ChunkShift/issues/17) | transferable Core source contract | complete: stable profile/identity, vectors, real consumers and host proof |
| Publication handoff | [#9](https://github.com/definitely-stable/ChunkShift/issues/9), [#69](https://github.com/definitely-stable/ChunkShift/issues/69) | **ChunkShift Core 0.1.0** published from the publication repository | complete (2026-09-27) |
| Core 0.1.1 maintenance | [#152](https://github.com/definitely-stable/ChunkShift/issues/152), [#127](https://github.com/definitely-stable/ChunkShift/issues/127), [#137](https://github.com/definitely-stable/ChunkShift/issues/137), [#153](https://github.com/definitely-stable/ChunkShift/issues/153), [#154](https://github.com/definitely-stable/ChunkShift/issues/154), [#186](https://github.com/definitely-stable/ChunkShift/issues/186) | same-semantics performance + hardening candidates; additive entry points only with a named consumer | exact 0.1.0 compatibility + end-to-end evidence; parallel to Patching |
| Repository governance | [#24](https://github.com/definitely-stable/ChunkShift/issues/24) | server-side enforcement of normal `main` policy here; tag/release enforcement is active on the publication repository | independent of local Patching |
| Patching | [#7](https://github.com/definitely-stable/ChunkShift/issues/7), [#181](https://github.com/definitely-stable/ChunkShift/issues/181), [#182](https://github.com/definitely-stable/ChunkShift/issues/182) | compare/diff, CSP create/apply, exact reconstruction | verified output + product benchmark evidence; exit items under [Patching gate](#patching-gate) |
| Patching research | [#183](https://github.com/definitely-stable/ChunkShift/issues/183), [#184](https://github.com/definitely-stable/ChunkShift/issues/184) | CSP size gap decomposition; update sets (tree manifest, cross-file reuse) | informative; a CSP revision or an update-set RFC only on their frozen rules |
| Trust | [#185](https://github.com/definitely-stable/ChunkShift/issues/185) | RFC for the detached trust envelope and update policy (RFC-0004 §4–§5) | before launcher-facing Patching guidance is published |
| Repository | [#10](https://github.com/definitely-stable/ChunkShift/issues/10)-[#13](https://github.com/definitely-stable/ChunkShift/issues/13) | packs → index/catalog → lifecycle → remote | storage-specific crash/scale gates |
| Optional host package | [#18](https://github.com/definitely-stable/ChunkShift/issues/18) | package only if repeated host behavior justifies it | no package by default |
| Research | [#14](https://github.com/definitely-stable/ChunkShift/issues/14) | future CDC/index/filter candidates | promotion only through normal evidence gates |

## Publication handoff

Issue [#9](https://github.com/definitely-stable/ChunkShift/issues/9) is closed here as moved. The publication gate ran in [MrFr3di/ChunkShift](https://github.com/MrFr3di/ChunkShift), which released ChunkShift `0.1.0` on 2026-09-27 with:

- the frozen 0.1.0 ProfileId/ProfileFingerprint and HashSuite behavior;
- the reviewed Core API;
- deterministic x64/ARM64 vectors;
- short-read/read-segmentation and scanner ownership/cancellation semantics;
- CSM corruption/resource-bound/fuzz coverage and independent fixtures/verifiers;
- JIT/NativeAOT clean-consumer evidence;
- >RAM streaming evidence;
- console and ASP.NET Core consumer samples.

The publication repository owns package metadata/versioning, release documentation, the changelog, tags/releases and NuGet publication. Later changes flow one way: developed and validated here, then ported in a separate pull request opened inside the publication repository ([CONTRIBUTING.md](CONTRIBUTING.md#porting-to-the-publication-repository)).

## Patching gate

Patching starts from the frozen Core **source contract** (#6/#8/#17) and the published `0.1.0` surface.

It owns:

- compare/diff and reuse analysis;
- CSP;
- base lookup;
- exact reconstruction;
- verified publication;
- actual patch-size measurement;
- comparison against whole-file delivery and xdelta3 on equivalent workloads.

Patching is ported to the publication repository and published on a later `0.1.Z` release when its own evidence is complete.

D17 create memory is done: `PATCH-ENC-003` made a raw prefix with capped tables the encoder default (#194, #195), and A2 of `PATCH-APPLY-001` holds on every platform ([PATCH-APPLY-001-EVIDENCE-20260929-001](docs/research/results/PATCH-APPLY-001-EVIDENCE-20260929-001.md)).

Remaining exit items (CSP v1 frozen 2026-09-28):

1. D13 re-chunk check cost: #168 is decided for option 3 (no public opt-out); `PATCH-APPLY-002` ([#182](https://github.com/definitely-stable/ChunkShift/issues/182)) makes the check cheaper internally. `PATCH-APPLY-003` adopted the overlapped check (A2) and requires the boundary-only check (A1a): with A2 the check still costs more than 25 % of apply CPU at eight concurrent applies on both Linux platforms ([PATCH-APPLY-003-EVIDENCE-20260929-001](docs/research/results/PATCH-APPLY-003-EVIDENCE-20260929-001.md)). A2 became the apply default (#206). With A1a (lane `boundary`) the check costs 13.39 % (linux-x64), 7.79 % (linux-arm64) and 8.02 % (win-x64) of apply CPU at eight concurrent applies, so rule 2 does not hold after A1a ([PATCH-APPLY-003-EVIDENCE-20260930-001](docs/research/results/PATCH-APPLY-003-EVIDENCE-20260930-001.md)). The boundary-only check is the apply default, and #168 is closed with option 3.
2. Create throughput/search: `PATCH-ENC-004` is complete and ADOPTed (H2-W2 workers; H1 cache rejected). `PATCH-ENC-005` ([#181](https://github.com/definitely-stable/ChunkShift/issues/181)) is complete as REJECT: G2 exact whole-base oracle MISS and the frozen `H6-O12-SF3-S128` fixed-evaluation guard also MISS. H5-F/H6-P/H8 are STOPPED, full Phase B is forbidden and D15/default remains unchanged ([final evidence](docs/research/results/PATCH-ENC-005-H6O-EVIDENCE-20261004-001.md)).
3. Patching Research Freeze: [#251](https://github.com/definitely-stable/ChunkShift/issues/251) blocks the first public compatibility freeze until required Patching research has durable dispositions, required interaction studies are complete, and final synthesis decides CSP v1 vs CSP vNext/new representations.
4. D23 CLI product surface: [#140](https://github.com/definitely-stable/ChunkShift/issues/140), after #251.
5. Public API review of `PublicAPI.Unshipped.txt`, after #251 and including whether an options type (from #168) should also carry progress reporting.
6. Port per [CONTRIBUTING.md](CONTRIBUTING.md#porting-to-the-publication-repository), after #251.

Linked research that does not block the exit:

- `PATCH-GAP-001` ([#183](https://github.com/definitely-stable/ChunkShift/issues/183)): RUNNING. Stage-A locked the G4/G5 populations; G5 is `NOT_PRESENT` on the frozen materialized corpus. G1 is REJECT (best 2.273822% vs the 15% gate); G3 is REJECT (RUN -187.984246%, FILE -202.790255% on evaluation); G4-BCJ is REJECT (0.955265% on evaluation vs the 15% gate, [durable evidence](docs/research/results/PATCH-GAP-001-G4-EVIDENCE-20261008-001.md)); the separately frozen Zucchini reference and overall #183 synthesis remain outstanding;
- `PATCH-TREE-001` ([#184](https://github.com/definitely-stable/ChunkShift/issues/184)): update sets;
- `TRUST-SIG-001` ([#185](https://github.com/definitely-stable/ChunkShift/issues/185)): the trust-envelope RFC.

## Core 0.1.1 maintenance

[#152](https://github.com/definitely-stable/ChunkShift/issues/152) owns the next Core maintenance evidence train.

Order is evidence-driven rather than feature-count driven:

1. refresh the current .NET 10/Amdahl baseline;
2. run [#127](https://github.com/definitely-stable/ChunkShift/issues/127), [#153](https://github.com/definitely-stable/ChunkShift/issues/153) and [#154](https://github.com/definitely-stable/ChunkShift/issues/154) as independent optimization studies;
3. use [#137](https://github.com/definitely-stable/ChunkShift/issues/137) as a stronger semantic regression oracle for scanner/backend work;
4. adopt only changes with exact 0.1.0 identity/format/API compatibility and material end-to-end or hardening value;
5. keep [#135](https://github.com/definitely-stable/ChunkShift/issues/135) optional until a real consumer justifies additive public API;
6. treat the additive entry points of [#186](https://github.com/definitely-stable/ChunkShift/issues/186) the same way. They are integrity-only verification guided by the manifest, hash-free boundary scanning and non-`Stream` sources, and each ships only with a named consumer.
   - `CORE-VERIFY-001` goes next as internal lab research, lanes V0–V2; it does not touch the scanner, so it can start before `FUZZ-001`.
   - `CORE-BOUNDARY-001` stays a Core-internal primitive.
   - `CORE-SOURCE-001` is deferred until the `SCAN-007` and V2 prototypes show whether the internal `FileStream` fast path suffices.

Every candidate is classed as S (same semantics, internal), A (additive API), B (breaking API) or P (new persisted semantics). The search for #152 found no speedup that needs a class B change to the shipped surface; the reasoning is in #186. Candidates that parallelize inside one operation (`SCAN-006`, `SCAN-007`, `CORE-VERIFY-001`) must also beat running several files concurrently, measured in the multi-file lane that #152 defines.

Core 0.1.1 does **not** mean a new chunking algorithm. RepMaxCDC/other CDC candidates stay research-only under #14/#136 and require a distinct future profile identity if promoted.

Research execution/results are discoverable from the common [experiment index](docs/research/EXPERIMENT-INDEX.md); the logging/retention contract is [docs/research/README.md](docs/research/README.md).

## Repository sequence

Repository work follows the useful local Patching loop. It must not become a prerequisite for Core or Patching correctness. Two design inputs come from the Patching side first:

- update sets ([#184](https://github.com/definitely-stable/ChunkShift/issues/184)), because a tree manifest and cross-file reuse shape what the Repository stores;
- the trust-envelope RFC ([#185](https://github.com/definitely-stable/ChunkShift/issues/185)), because signed identities shape what remote clients accept.

Order:

1. [#10](https://github.com/definitely-stable/ChunkShift/issues/10) immutable self-indexed packs:
   - [#133](https://github.com/definitely-stable/ChunkShift/issues/133) logical identity/ingest;
   - [#138](https://github.com/definitely-stable/ChunkShift/issues/138) physical compression evidence;
   - [#144](https://github.com/definitely-stable/ChunkShift/issues/144) Pack v1 format/rebuild;
   - [#146](https://github.com/definitely-stable/ChunkShift/issues/146) locality/multi-location evidence.
2. [#11](https://github.com/definitely-stable/ChunkShift/issues/11) rebuildable global index/catalog/crash-safe publication:
   - [#145](https://github.com/definitely-stable/ChunkShift/issues/145) SoA GlobalIndex v1 + 1B scale;
   - [#139](https://github.com/definitely-stable/ChunkShift/issues/139) immutable verification/trust semantics.
3. [#12](https://github.com/definitely-stable/ChunkShift/issues/12) reachability GC/repack/reader-safe retirement:
   - [#147](https://github.com/definitely-stable/ChunkShift/issues/147) external-memory GC/generation retirement/repack.
4. [#13](https://github.com/definitely-stable/ChunkShift/issues/13) HTTP Range and S3/R2 backends:
   - [#134](https://github.com/definitely-stable/ChunkShift/issues/134) logical range planner;
   - [#148](https://github.com/definitely-stable/ChunkShift/issues/148) multi-source reconstruction + PackFetchPlan execution.

Cross-cutting Repository rules:

- `PackId` and `PackDigest` are separate physical concepts;
- global indexes are immutable bounded segments using compact PackOrdinal/PackTable representation;
- alternate physical locations are sparse policy, never a change to logical ChunkId;
- GC/repack publishes replacements before retirement and uses bounded external-memory reachability;
- remote reconstruction uses batch metadata lookup and transport-neutral range plans rather than per-chunk HEAD/GET.

## Immediate work order

1. land the final `PATCH-ENC-005` H6-O evidence and close #181 with REJECT / `STOP_RESEMBLANCE`; do not retune the frozen selector study.
2. execute the [Patching Research Freeze](https://github.com/definitely-stable/ChunkShift/issues/251). `PATCH-GAP-001` G1, G3 and G4-BCJ are complete/rejected. The next #183 slice is the frozen Zucchini executable reference and final factor synthesis (not a CSP v1 change). Run `PATCH-TREE-001`, `PATCH-META-001` and `PATCH-INCR-001` as compatible parallel research.
3. after their dependencies, complete `PATCH-DICT-001`, `PATCH-DOTNET-001`, and only then evaluate `PATCH-ML-001` against a richer proven source/representation universe. Enter `PATCH-COMPILER-001` only with >=2 decision-grade representation families; defer `PATCH-EXEC-001` implementation until task/representation shape is stable.
4. complete #185 trust/update-policy design and any required cross-factor interaction ExperimentId, then produce the #251 final synthesis: CSP v1 remains sufficient or a versioned CSP vNext/new representation is required.
5. only after #251 closes, make [#140](https://github.com/definitely-stable/ChunkShift/issues/140), the Patching public-API freeze and publication-repository port release-critical.
6. keep [#152](https://github.com/definitely-stable/ChunkShift/issues/152) as a parallel Core 0.1.1 maintenance track; adopt/port a Core candidate only after exact-compatibility and end-to-end evidence gates pass.
7. finish ordinary `main` governance for this repository under [#24](https://github.com/definitely-stable/ChunkShift/issues/24).
