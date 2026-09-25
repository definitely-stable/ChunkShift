# ChunkShift roadmap

Status: Active  
Last reviewed: 2026-09-25

Authority:

- architecture/persisted contracts: [RFC-0001](docs/architecture/RFC-0001-target-architecture-2026.md), [RFC-0002](docs/architecture/RFC-0002-embedded-sdk-aspnet-core.md), [RFC-0003](docs/architecture/RFC-0003-core-first-release.md), [RFC-0004](docs/architecture/RFC-0004-core-0.1-deferred-product-concerns.md);
- executable milestone acceptance: [PLAN.md](PLAN.md);
- live completion tracking: [#1](https://github.com/definitely-stable/ChunkShift/issues/1);
- package/tag policy: [docs/RELEASES.md](docs/RELEASES.md);
- measurement methodology: [docs/benchmarks/M0-LAB.md](docs/benchmarks/M0-LAB.md).

This file owns **program order, dependencies and release gates**. It does not duplicate RFC byte layouts or issue-level implementation detail.

## Product sequence

The first public release is the standalone Core package:

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

**Where `main` is now:** Core is implemented — deterministic chunking, bounded raw scanner, CSM create/read/verify and the CLI that exercises them — and nothing is published yet. The current phase is **freezing** the public API, the default profile and the compatibility baseline, not adding Core features:

- [#6](https://github.com/definitely-stable/ChunkShift/issues/6) API/NativeAOT freeze. The API surface decisions of [#63](https://github.com/definitely-stable/ChunkShift/issues/63) are done (PRs #95, #96). [#65](https://github.com/definitely-stable/ChunkShift/issues/65) is decided by [RFC-0004](docs/architecture/RFC-0004-core-0.1-deferred-product-concerns.md) (no progress/compression/signature/anti-rollback surface in Core 0.1.0). The API audit and its same-commit evidence run are recorded in [CORE-0.1-API-FREEZE.md](docs/architecture/CORE-0.1-API-FREEZE.md). #6 is **closed**: the current surface is the frozen Core 0.1.0 API candidate;
- [#8](https://github.com/definitely-stable/ChunkShift/issues/8) profile selection on real multi-version corpora. The ProfileId rule and verification contract of [#64](https://github.com/definitely-stable/ChunkShift/issues/64) are done (PR #97). [#99](https://github.com/definitely-stable/ChunkShift/issues/99) settled the Gear semantics and handed #8 the real-corpus protocol ([CDC-PREFREEZE-DECISION §11](docs/benchmarks/CDC-PREFREEZE-DECISION-2026-09.md#11-ownership-99-vs-8)). #8 is now a data-driven selection of `min/target/max`, the stable ProfileId and the default, not architecture research;
- [#17](https://github.com/definitely-stable/ChunkShift/issues/17) real-Kestrel host proof;
- [#9](https://github.com/definitely-stable/ChunkShift/issues/9) compatibility baseline, with release-mechanics decisions in [#69](https://github.com/definitely-stable/ChunkShift/issues/69) (preview strategy, CLI packaging, post-release API/package baseline);
- [#24](https://github.com/definitely-stable/ChunkShift/issues/24) GitHub-side enforcement (squash-only `main`, tag protection, immutable releases). It does not block #6, #8 or #17, but it **must be complete before the first NuGet publication**, preview or stable (docs/RELEASES.md §10 step 4).

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
                            #9 Core 0.1.0 gate  <-- #69 release decisions
                                      |
                                      +------> first NuGet publication
                                      |          (also requires #24:
                                      |           repository/tag/immutable-
                                      |           release enforcement)
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

## Delivery stages

| Stage | Issues | Outcome | Gate |
| --- | --- | --- | --- |
| M0 foundation | [#2](https://github.com/definitely-stable/ChunkShift/issues/2), [#3](https://github.com/definitely-stable/ChunkShift/issues/3) | correct identity model + reproducible lab | complete |
| Preparation | [#33](https://github.com/definitely-stable/ChunkShift/issues/33) | known M0 defects fixed; Core-first plan executable | regression tests + docs/issues synchronized |
| Core kernels | [#4](https://github.com/definitely-stable/ChunkShift/issues/4) | scalar deterministic FastCDC/fixed reference + HashSuite kernel | segmentation-independent boundaries/IDs |
| Core streaming/manifest | [#5](https://github.com/definitely-stable/ChunkShift/issues/5), [#16](https://github.com/definitely-stable/ChunkShift/issues/16) | CSM create/read/verify + bounded raw scanner | complete |
| Core API evidence | [#20](https://github.com/definitely-stable/ChunkShift/issues/20), [#6](https://github.com/definitely-stable/ChunkShift/issues/6) | smallest evidence-selected public Core candidate | #20, [#63](https://github.com/definitely-stable/ChunkShift/issues/63), [#64](https://github.com/definitely-stable/ChunkShift/issues/64) and [#65](https://github.com/definitely-stable/ChunkShift/issues/65) complete; [#6](https://github.com/definitely-stable/ChunkShift/issues/6) closed (candidate frozen; Shipped baseline set in the release PR) |
| Evidence infrastructure | [#67](https://github.com/definitely-stable/ChunkShift/issues/67), [#68](https://github.com/definitely-stable/ChunkShift/issues/68) | streaming-lane lab; fuzz, independent decoder, cancellation, coverage, >RAM | complete |
| Core release evidence | [#8](https://github.com/definitely-stable/ChunkShift/issues/8), [#17](https://github.com/definitely-stable/ChunkShift/issues/17), [#9](https://github.com/definitely-stable/ChunkShift/issues/9), [#69](https://github.com/definitely-stable/ChunkShift/issues/69) | **ChunkShift Core 0.1.0** | profile/API/CSM vectors, real consumers, host proof |
| Publication governance | [#24](https://github.com/definitely-stable/ChunkShift/issues/24) | server-side enforcement of the documented `main`/tag/release rules | required before the first NuGet publication; does not block #6/#8/#17 |
| Patching | [#7](https://github.com/definitely-stable/ChunkShift/issues/7) | compare/diff, CSP create/apply, exact reconstruction | verified output + product benchmark evidence |
| Repository | [#10](https://github.com/definitely-stable/ChunkShift/issues/10)-[#13](https://github.com/definitely-stable/ChunkShift/issues/13) | packs → index/catalog → lifecycle → remote | storage-specific crash/scale gates |
| Optional host package | [#18](https://github.com/definitely-stable/ChunkShift/issues/18) | package only if repeated host behavior justifies it | no package by default |
| Research | [#14](https://github.com/definitely-stable/ChunkShift/issues/14) | future CDC/index/filter candidates | promotion only through normal evidence gates |

## Core 0.1.0 gate

Issue [#9](https://github.com/definitely-stable/ChunkShift/issues/9) may close only when Core itself is release-ready. It does **not** wait for CSP or Patching.

Required evidence includes:

- frozen 0.1.0 ProfileId/default HashSuite behavior;
- deterministic x64/ARM64 vectors;
- short-read/read-segmentation invariance;
- CSM create/read/verify corruption and resource-bound coverage;
- selected scanner semantics from [#20](https://github.com/definitely-stable/ChunkShift/issues/20);
- packaged NativeAOT/trim execution of the real scanner/manifest path;
- a source larger than available working memory processed without full materialization;
- clean-package console and ASP.NET consumer examples;
- independent verification of CSM/compatibility vectors.

The fuzz, >RAM, independent-verification, packaged NativeAOT and sample evidence already exists on `main` for the current candidate ([#68](https://github.com/definitely-stable/ChunkShift/issues/68), PRs #61, #89); it is re-run against the frozen baseline rather than built anew.

The exact contract lives in PLAN/[#9](https://github.com/definitely-stable/ChunkShift/issues/9); this section only defines the release gate.

## Patching gate

Patching starts after the Core 0.1.0 baseline.

It owns:

- compare/diff and reuse analysis;
- CSP;
- base lookup;
- exact reconstruction;
- verified publication;
- actual patch-size measurement;
- comparison against whole-file delivery and xdelta3 on equivalent workloads.

Patching is published on a later `0.1.Z` release when its own evidence is complete.

## Repository sequence

Repository work follows the useful local Patching loop. It must not become a prerequisite for Core or Patching correctness.

Order:

1. [#10](https://github.com/definitely-stable/ChunkShift/issues/10) immutable self-indexed packs;
2. [#11](https://github.com/definitely-stable/ChunkShift/issues/11) rebuildable global index/catalog/crash-safe publication;
3. [#12](https://github.com/definitely-stable/ChunkShift/issues/12) reachability GC/repack/reader-safe retirement;
4. [#13](https://github.com/definitely-stable/ChunkShift/issues/13) HTTP Range and S3/R2 backends.

## Immediate work order

1. run [#8](https://github.com/definitely-stable/ChunkShift/issues/8) (real version histories → calibration/holdout → 64/128/256 KiB actual means plus the optional coarse lane → `min/target/max`, stable ProfileId, default) and [#17](https://github.com/definitely-stable/ChunkShift/issues/17) in parallel;
2. decide [#69](https://github.com/definitely-stable/ChunkShift/issues/69) (preview strategy, CLI packaging) and close [#9](https://github.com/definitely-stable/ChunkShift/issues/9) for Core 0.1.0; the first NuGet publication (preview or stable) additionally waits on [#24](https://github.com/definitely-stable/ChunkShift/issues/24), which blocks nothing else;
3. only then start [#7](https://github.com/definitely-stable/ChunkShift/issues/7) Patching ([#66](https://github.com/definitely-stable/ChunkShift/issues/66) writes the CSP candidate spec first).
