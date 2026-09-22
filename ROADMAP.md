# ChunkShift roadmap

Status: Active roadmap  
Last reviewed: 2026-09-22  
Implementation authority: [PLAN.md](PLAN.md)  
Architecture authority: [RFC-0001](docs/architecture/RFC-0001-target-architecture-2026.md) and [RFC-0002](docs/architecture/RFC-0002-embedded-sdk-aspnet-core.md)  
Program tracker: [#1](https://github.com/definitely-stable/ChunkShift/issues/1)  
Release policy: [docs/RELEASES.md](docs/RELEASES.md)  
Contribution policy: [CONTRIBUTING.md](CONTRIBUTING.md)  
Repository hardening tracker: [#25](https://github.com/definitely-stable/ChunkShift/issues/25)

This document is the project-level delivery map. It answers **what becomes usable when, what blocks the next stage, and what evidence is required to advance**.

It deliberately does not duplicate byte layouts, API contracts or implementation details from the RFCs and PLAN. If this roadmap conflicts with an accepted RFC, the RFC wins. If it conflicts with milestone acceptance criteria in PLAN.md, PLAN.md wins.

## 1. Product destination

ChunkShift is being built in two release lines.

### Core/Patching line

The first public product line is a small .NET content-aware binary update engine:

```text
bytes / Stream
   |
   v
deterministic chunking + hashing
   |
   v
CSM manifest
   |
   +--> inspect / verify / compare
   |
   v
CSP patch
   |
   v
exact target reconstruction
```

The first public release target is `0.1.0`. Its package intent is:

- `ChunkShift` — embedded/local SDK, raw chunk streaming, manifests, verification and comparison;
- `ChunkShift.Patching` — exact delta creation and reconstruction;
- `ChunkShift.Cli` — supported command-line workflows.

### Repository line

After the Core/Patching 0.1.0 baseline, the same identities and manifest model are extended into a content-addressed repository:

```text
immutable packs
   |
self-indexed pack metadata
   |
rebuildable global indexes
   |
catalog generations / refs
   |
crash-safe publication
   |
reachability GC + repack
   |
HTTP / S3 / R2 distribution
```

`ChunkShift.Repository` and remote-storage packages remain preview until the storage, crash-consistency, GC and cloud evidence gates are complete.

### Pre-1.0 release train

ChunkShift follows Semantic Versioning 2.0.0 with an explicit pre-1.0 release policy:

```text
0.1.0 -> 0.1.1 -> 0.1.2 -> 0.1.3 -> ...
```

- `0.1.0` is the first public release target;
- every subsequent normal pre-1.0 release increments only PATCH by one;
- under the current policy, `0.2.0` is not used as a feature-release signal;
- breaking changes are allowed before `1.0.0`, but must be called out explicitly in the PR and changelog;
- `1.0.0` is reached only by an explicit compatibility decision, never automatically from milestone completion;
- package versions do not replace CSM/CSP/pack/index/ProfileId/HashSuite versioning; those compatibility domains remain independent.

The normative release, tag and changelog rules are in [docs/RELEASES.md](docs/RELEASES.md).

## 2. Current program state

As of 2026-09-22:

- target architecture is documented in RFC-0001;
- embedded/local SDK and ASP.NET Core boundaries are documented in RFC-0002;
- the milestone implementation plan is in PLAN.md;
- architecture/API Red Team corrections were merged through PRs [#15](https://github.com/definitely-stable/ChunkShift/pull/15), [#19](https://github.com/definitely-stable/ChunkShift/pull/19) and [#21](https://github.com/definitely-stable/ChunkShift/pull/21);
- implementation issues for M0-M7 are open;
- the low-level raw chunk-stream API is intentionally not frozen;
- M3 establishes the `0.1.0` public baseline; SemVer compatibility stability is not promised until an explicit future `1.0.0` decision.

The next work is not another broad architecture redesign. The immediate objective is to turn the accepted architecture into measured implementation evidence.

## 3. Critical path

The main critical path is:

```text
#2 identity / HashSuite / profile semantics
 + 
#3 benchmark lab / corpus / mutation framework
 |
 v
#4 deterministic chunking + hashing kernels
 |
 +------------------+
 |                  |
 v                  v
#5 CSM           #16 raw chunk-stream API
 |                  |
 |                  v
 |               #20 API bake-off and evidence
 |                  |
 +--------+---------+
          |
          v
#6 minimal Core public API + NativeAOT candidate
          |
          v
#7 declarative CSP patch/reconstruction
          |
          +-------------------+
          |                   |
          v                   v
#8 CDC/profile bake-off    #17 ASP.NET host validation
          |                   |
          +---------+---------+
                    |
                    v
#9 Core/Patching 0.1.0 baseline
                    |
        +-----------+-----------+
        |                       |
        v                       v
#10 Repository M4          #18 ASP.NET M4A
        |
        v
#11 index/catalog/crash/concurrency
        |
        v
#12 GC/repack/lifecycle
        |
        v
#13 HTTP/S3/R2 distribution
```

Research issue [#14](https://github.com/definitely-stable/ChunkShift/issues/14) runs in parallel after the measurement foundation exists. It must not block the stable product unless evidence shows that a candidate materially improves the selected design before a compatibility freeze.

## 4. Delivery stages

| Stage | Main issues | What becomes possible | Gate to advance |
| --- | --- | --- | --- |
| M0 — semantics + measurement | [#2](https://github.com/definitely-stable/ChunkShift/issues/2), [#3](https://github.com/definitely-stable/ChunkShift/issues/3) | trustworthy identities and repeatable evidence | semantics are normative; benchmark lab is reproducible |
| M1 — deterministic Core candidate | [#4](https://github.com/definitely-stable/ChunkShift/issues/4), [#5](https://github.com/definitely-stable/ChunkShift/issues/5), [#16](https://github.com/definitely-stable/ChunkShift/issues/16), [#20](https://github.com/definitely-stable/ChunkShift/issues/20), [#6](https://github.com/definitely-stable/ChunkShift/issues/6) | bounded-memory chunking, hashing, CSM and embedded streaming | deterministic vectors pass; API shape selected by evidence |
| M2 — first useful product loop | [#7](https://github.com/definitely-stable/ChunkShift/issues/7) | create/apply an exact local binary patch without Repository | exact reconstruction, atomic publish, corrupt/wrong-base safety |
| M2A — host validation | [#17](https://github.com/definitely-stable/ChunkShift/issues/17) | prove Core/Patching works naturally inside ASP.NET Core | no Core API workaround or transport-specific leakage required |
| M3 — Core/Patching 0.1.0 public baseline | [#8](https://github.com/definitely-stable/ChunkShift/issues/8), [#9](https://github.com/definitely-stable/ChunkShift/issues/9) | first public Core, Patching and CLI compatibility baseline | no unresolved P0 API/format/profile issue |
| M4 — Repository foundation | [#10](https://github.com/definitely-stable/ChunkShift/issues/10) | immutable self-indexed local repository packs | reliable large local repository and rebuildable index evidence |
| M4A — ASP.NET package decision | [#18](https://github.com/definitely-stable/ChunkShift/issues/18) | optional reusable ASP.NET integration surface | package exists only if repeated behavior justifies it |
| M5 — repository hardening | [#11](https://github.com/definitely-stable/ChunkShift/issues/11) | scalable lookup and crash-safe concurrent publication | injected crashes expose old or complete-new state only |
| M6 — lifecycle | [#12](https://github.com/definitely-stable/ChunkShift/issues/12) | safe GC, repack and retirement | no reachable-data loss under crash/concurrency tests |
| M7 — remote distribution | [#13](https://github.com/definitely-stable/ChunkShift/issues/13) | HTTP Range and S3/R2 operation | bounded request amplification and verified remote restore |
| Research | [#14](https://github.com/definitely-stable/ChunkShift/issues/14) | evidence for future algorithm/index improvements | promotion only through the same benchmark/compatibility gates |

## 5. M0 — Architecture semantics and measurement foundation

### Objective

Remove ambiguity from identities and build the lab that every later architecture choice depends on.

### Work

- [#2](https://github.com/definitely-stable/ChunkShift/issues/2) — correct `Hash256`, `HashSuiteId`, `ProfileFingerprint` and identity semantics;
- [#3](https://github.com/definitely-stable/ChunkShift/issues/3) — benchmark lab, representative corpus, deterministic mutation generator and comparable x64/ARM64 result format.

### Why M0 is first

Without [#2](https://github.com/definitely-stable/ChunkShift/issues/2), persisted identities can freeze the wrong semantics. Without [#3](https://github.com/definitely-stable/ChunkShift/issues/3), chunking, hashing, API and profile choices become opinion-driven and cannot be defended before the first public release.

### Exit evidence

M0 closes only when:

- all-zero `Hash256` and the full 256-bit value space are valid;
- HashSuite and chunking profile are independent;
- semantic profile fingerprinting is defined and tested;
- the corpus and mutation traces are reproducible;
- throughput, allocation, RSS and CDC-quality metrics can be collected consistently;
- equivalent experiments can be run on x64 and ARM64.

### Result for the project

After M0, implementation work can be optimized and compared without changing the measurement methodology every milestone.

## 6. M1 — Deterministic Core and CSM candidate

### Objective

Create the first real bounded-memory Core capable of turning arbitrary streams into deterministic chunk identities and binary manifests.

### Work order

1. [#4](https://github.com/definitely-stable/ChunkShift/issues/4) — canonical chunking and HashSuite kernels.
2. [#5](https://github.com/definitely-stable/ChunkShift/issues/5) — CSM v1 candidate reader/writer and manifest verification.
3. [#16](https://github.com/definitely-stable/ChunkShift/issues/16) — embedded raw chunk-stream capability.
4. [#20](https://github.com/definitely-stable/ChunkShift/issues/20) — compare public API alternatives against the direct internal sink.
5. [#6](https://github.com/definitely-stable/ChunkShift/issues/6) — freeze the smallest viable Core API candidate and NativeAOT contract.

[#5](https://github.com/definitely-stable/ChunkShift/issues/5) and [#16](https://github.com/definitely-stable/ChunkShift/issues/16) can overlap once the canonical kernel from [#4](https://github.com/definitely-stable/ChunkShift/issues/4) is stable enough to share.

### Mandatory evidence

The same source/profile/HashSuite must yield the same chunk sequence across:

- different Stream read segmentation;
- one-byte/random short reads;
- seekable and non-seekable sources;
- supported architectures;
- scalar and optimized backends;
- JIT and NativeAOT where supported.

[#20](https://github.com/definitely-stable/ChunkShift/issues/20) must explicitly measure:

- callback push versus pull-reader prototype;
- contiguous `ReadOnlyMemory<byte>` versus segmented payload;
- `Task` versus `ValueTask` handler;
- public callback overhead versus an internal direct-sink baseline;
- bytes copied/GiB;
- allocations/GiB;
- first-chunk latency;
- cancellation latency and bounded read-ahead.

### M1 completion state

At the end of M1 the project has a usable **preview embedded SDK**, but its public API is not yet a 1.0 promise.

## 7. M2 — Minimum useful patching loop

### Objective

Deliver the first complete end-user value path before building a repository.

### Work

Issue [#7](https://github.com/definitely-stable/ChunkShift/issues/7) adds:

- direct old/new comparison;
- base chunk locator;
- declarative CSP candidate;
- patch creation;
- patch application;
- streaming exact reconstruction;
- final verification;
- CLI diff/create/apply workflows.

### Product gate

A user must be able to start with an old file and a target file, produce a patch containing only required target content, then reconstruct the exact target from the old file plus patch.

Failure must not publish an unverified destination.

### Why Repository is still excluded

Repository complexity is not required to prove that ChunkShift's core value proposition works. Delaying packs, global indexes, cloud and GC prevents storage architecture from masking defects in chunking, manifest or patch semantics.

## 8. M2A — ASP.NET Core validation before the freeze

### Objective

Validate the stable Core/Patching direction in a real server environment before M3 makes it expensive to change.

### Work

Issue [#17](https://github.com/definitely-stable/ChunkShift/issues/17) covers both in-memory host tests and real Kestrel loopback scenarios:

- request streams larger than RAM;
- non-seekable and short-read bodies;
- slow/trickle uploads;
- client disconnect and cooperative cancellation;
- response backpressure;
- Range 206/416;
- ETag/If-Range behavior;
- request-size limits and middleware transformations;
- response resource ownership.

### Gate

Core must remain plain .NET and Stream-oriented. ASP.NET-specific fast paths may exist internally in an adapter/sample, but ASP.NET, DI, Pipeline ownership or host policy must not leak into the stable Core contract.

## 9. M3 — Core/Patching 0.1.0 public baseline

### Objective

Convert measured candidates into the first public `0.1.0` compatibility baseline.

### Work

- [#8](https://github.com/definitely-stable/ChunkShift/issues/8) — calibrated CDC/profile bake-off;
- [#9](https://github.com/definitely-stable/ChunkShift/issues/9) — final API, format, golden-vector and compatibility freeze.

### Decisions that become expensive after M3

M3 must close at least:

- stable ProfileId/default profile semantics;
- HashSuite IDs and defaults;
- CSM v1;
- CSP v1;
- exact Core/Patching public surface;
- raw chunk-stream semantics selected by [#20](https://github.com/definitely-stable/ChunkShift/issues/20);
- borrowed-memory lifetime;
- cancellation/error/ownership behavior;
- short-read invariance;
- default-resolution policy for 1.x;
- golden-vector and compatibility policy.

### Release outcome

This is the release-readiness gate for the first public release:

- `ChunkShift 0.1.0`;
- `ChunkShift.Patching 0.1.0`;
- public `ChunkShift.Cli` baseline.

Repository and ASP.NET integration packages do not have to be stable here.

## 10. Post-Core split: M4 and M4A

After [#9](https://github.com/definitely-stable/ChunkShift/issues/9), two independent tracks can move in parallel.

### M4 — immutable Repository foundation

Issue [#10](https://github.com/definitely-stable/ChunkShift/issues/10) introduces self-indexed immutable packs, filesystem storage, per-frame encoding and rebuildable location metadata.

The goal is to prove the physical repository model locally before introducing global index complexity or remote object stores.

### M4A — conditional ASP.NET Core package

Issue [#18](https://github.com/definitely-stable/ChunkShift/issues/18) asks a narrower question: is there enough reusable ASP.NET behavior to justify a package?

Possible valid outcomes are:

- a small preview `ChunkShift.AspNetCore` package with strongly typed endpoint mapping; or
- a recorded decision that standard ASP.NET Core primitives are sufficient.

M4A is therefore a decision gate, not a mandatory package-delivery milestone.

## 11. M5 — Global index, catalog, crash consistency and concurrency

### Objective

Scale repository lookup and publication without turning a mutable database into repository truth.

### Work

Issue [#11](https://github.com/definitely-stable/ChunkShift/issues/11) adds immutable index segments, catalog generations, roots/refs, immutable read views, conditional publication, rebuild and metadata compaction.

### Gate

Crash injection at every publish boundary must expose either:

- the previous committed repository state; or
- the complete new state.

A half-published repository state is a release blocker.

## 12. M6 — GC, repack and lifecycle

### Objective

Reclaim physical space without invalidating any reachable content or active read view.

Issue [#12](https://github.com/definitely-stable/ChunkShift/issues/12) adds reachability marking, repack planning, retirement states, GC cutoff/epoch rules and reader safety.

### Gate

Long-running, concurrent and crash-injected lifecycle tests must demonstrate that reachable chunks are never deleted.

## 13. M7 — HTTP/S3/R2 distribution

### Objective

Validate the repository architecture under high-latency object-store and CDN conditions.

Issue [#13](https://github.com/definitely-stable/ChunkShift/issues/13) adds:

- HTTP Range content access;
- S3-compatible immutable-object backend;
- R2 validation;
- range grouping/coalescing;
- bounded request concurrency;
- retries/backoff;
- metadata caching;
- conditional ref publication.

### Gate

Remote restore and static/CDN patching must remain exact and content-verified while keeping request amplification bounded. The normal path must not degrade into one HEAD/GET per small chunk.

### Repository release outcome

Repository functionality becomes eligible for a later `0.1.Z` public release only after M4-M7 evidence is complete and no published storage contract remains unsupported by crash, lifecycle and remote-backend tests. A future `1.0.0` remains a separate explicit compatibility decision.

## 14. Parallel research track

Issue [#14](https://github.com/definitely-stable/ChunkShift/issues/14) may evaluate SeqCDC, VectorCDC, Chonkers, UltraCDC, Binary Fuse, Ribbon/BuRR, alternative exact index encodings and optimized ISA-specific kernels.

Research is intentionally isolated from the critical path.

A research candidate may enter stable architecture only after:

```text
implementation/prototype
    +
representative corpus
    +
end-to-end measurement
    +
cross-platform determinism
    +
compatibility review
```

Raw microbenchmark throughput alone is insufficient.

## 15. Quality gates that apply across milestones

Every milestone must preserve the following project-level rules:

- bounded memory for data much larger than RAM;
- deterministic persisted identities;
- corruption is detected rather than silently tolerated;
- caller-owned Stream semantics in Core;
- cooperative cancellation with explicit ownership/lifetime rules;
- no mandatory per-chunk heap object;
- NativeAOT/trim viability for stable Core surfaces;
- architecture decisions backed by reproducible evidence;
- no stable abstraction is introduced solely for hypothetical extensibility;
- performance work cannot change persisted semantics without an explicit compatibility decision.

## 16. Release ladder

The roadmap does not assign calendar dates. Progress is evidence-gated.

```text
Architecture accepted
        |
        v
M0 measurement-ready
        |
        v
M1 Core preview
        |
        v
M2 patching preview
        |
        +--> M2A ASP.NET validation
        |
        v
M3 Core/Patching 0.1.0
        |
        +----------------------+
        |                      |
        v                      v
M4 Repository preview     M4A ASP.NET decision
        |
        v
M5 crash-safe/scalable repository
        |
        v
M6 lifecycle-safe repository
        |
        v
M7 remote-distribution evidence
        |
        v
Repository public-release eligibility
```

## 17. What to work on next

Unless an issue uncovers a P0 architecture contradiction, execution should begin in this order:

1. implement [#2](https://github.com/definitely-stable/ChunkShift/issues/2) and [#3](https://github.com/definitely-stable/ChunkShift/issues/3) in parallel;
2. start [#4](https://github.com/definitely-stable/ChunkShift/issues/4) only against the corrected M0 semantics and measurement harness;
3. build [#5](https://github.com/definitely-stable/ChunkShift/issues/5) and [#16](https://github.com/definitely-stable/ChunkShift/issues/16) on the same canonical kernel;
4. run [#20](https://github.com/definitely-stable/ChunkShift/issues/20) before treating the raw scanner shape as public;
5. close [#6](https://github.com/definitely-stable/ChunkShift/issues/6) only after [#20](https://github.com/definitely-stable/ChunkShift/issues/20) evidence exists;
6. implement [#7](https://github.com/definitely-stable/ChunkShift/issues/7) and validate the first full local patch loop;
7. run [#17](https://github.com/definitely-stable/ChunkShift/issues/17) before public API freeze;
8. run [#8](https://github.com/definitely-stable/ChunkShift/issues/8) and [#9](https://github.com/definitely-stable/ChunkShift/issues/9) as the final Core/Patching `0.1.0` evidence gate;
9. only then make Repository M4+ the main implementation line.

That sequence is the default plan. Deviations should be recorded in issue [#1](https://github.com/definitely-stable/ChunkShift/issues/1) or an architecture decision when they materially affect dependencies or compatibility.

## 18. Roadmap maintenance

- ROADMAP.md owns **program sequence, stage outcomes and release gates**.
- PLAN.md owns **milestone deliverables, invariants, tests, benchmarks and exit criteria**.
- RFCs own **architecture and compatibility contracts**.
- GitHub issue [#1](https://github.com/definitely-stable/ChunkShift/issues/1) owns **live completion tracking**.
- Individual issues own **implementation-specific acceptance evidence**.

When a milestone changes, update the smallest authoritative layer rather than copying the same decision into every document.
