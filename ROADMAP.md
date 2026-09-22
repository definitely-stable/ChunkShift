# ChunkShift roadmap

Status: Active  
Last reviewed: 2026-09-22

Authority:

- architecture/persisted contracts: [RFC-0001](docs/architecture/RFC-0001-target-architecture-2026.md), [RFC-0002](docs/architecture/RFC-0002-embedded-sdk-aspnet-core.md), [RFC-0003](docs/architecture/RFC-0003-core-first-release.md);
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

Completed semantic preflight: [#37](https://github.com/definitely-stable/ChunkShift/issues/37) hardened measurement-oracle correctness, exact FastCDC candidate semantics and x64/ARM64 deterministic evidence; merged via PR #38. The next critical-path implementation is [#4](https://github.com/definitely-stable/ChunkShift/issues/4).

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
                                      |
                         +------------+------------+
                         |                         |
                         v                         v
                     #8 profile                #17 ASP.NET
                       bake-off                 Core host proof
                         |                         |
                         +------------+------------+
                                      |
                                      v
                            #9 Core 0.1.0 gate
                                      |
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
| Core streaming/manifest | [#5](https://github.com/definitely-stable/ChunkShift/issues/5), [#16](https://github.com/definitely-stable/ChunkShift/issues/16) | CSM create/read/verify + bounded raw scanner | corruption/stream/lifetime tests |
| Core API evidence | [#20](https://github.com/definitely-stable/ChunkShift/issues/20), [#6](https://github.com/definitely-stable/ChunkShift/issues/6) | smallest evidence-selected public Core candidate | alternatives measured; AOT/trim consumer passes |
| Core release evidence | [#8](https://github.com/definitely-stable/ChunkShift/issues/8), [#17](https://github.com/definitely-stable/ChunkShift/issues/17), [#9](https://github.com/definitely-stable/ChunkShift/issues/9) | **ChunkShift Core 0.1.0** | profile/API/CSM vectors, real consumers, host proof |
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

1. close [#33](https://github.com/definitely-stable/ChunkShift/issues/33) and merge its regression/evidence corrections;
2. implement [#4](https://github.com/definitely-stable/ChunkShift/issues/4);
3. implement [#5](https://github.com/definitely-stable/ChunkShift/issues/5) and [#16](https://github.com/definitely-stable/ChunkShift/issues/16) against the same canonical kernel;
4. run [#20](https://github.com/definitely-stable/ChunkShift/issues/20) and keep only the evidence-selected public scanner surface;
5. close [#6](https://github.com/definitely-stable/ChunkShift/issues/6);
6. run [#8](https://github.com/definitely-stable/ChunkShift/issues/8) and [#17](https://github.com/definitely-stable/ChunkShift/issues/17);
7. close [#9](https://github.com/definitely-stable/ChunkShift/issues/9) and release-readiness work for Core 0.1.0;
8. only then start [#7](https://github.com/definitely-stable/ChunkShift/issues/7) Patching.

No additional repository-governance layer is a prerequisite for [#4](https://github.com/definitely-stable/ChunkShift/issues/4).
