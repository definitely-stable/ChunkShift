# ChunkShift implementation plan

Status: Active  
Last reviewed: 2026-09-24

Program order: [ROADMAP.md](ROADMAP.md)  
Release policy: [docs/RELEASES.md](docs/RELEASES.md)

Normative architecture:

- [RFC-0001 — Target Architecture](docs/architecture/RFC-0001-target-architecture-2026.md)
- [RFC-0002 — Embedded Scanner / ASP.NET Boundary](docs/architecture/RFC-0002-embedded-sdk-aspnet-core.md)
- [RFC-0003 — Core-first Public Release](docs/architecture/RFC-0003-core-first-release.md)
- [RFC-0004 — Product concerns deliberately outside Core 0.1.0](docs/architecture/RFC-0004-core-0.1-deferred-product-concerns.md)

This file owns **milestone deliverables, tests, benchmarks and exit criteria**. Detailed implementation acceptance belongs in the linked GitHub issue.

## 1. Package boundary

First public release:

```text
ChunkShift 0.1.0
```

Core 0.1.0 contains:

- deterministic chunking and hashing;
- raw bounded-memory scanner;
- CSM creation;
- concrete streaming CSM reader;
- CSM verification;
- stable identities/profile semantics;
- CLI workflows that exercise those Core capabilities.

Deferred to `ChunkShift.Patching`:

- compare/diff;
- reuse-analysis product APIs;
- CSP create/apply;
- exact reconstruction from base + patch.

Repository and any dedicated ASP.NET package remain later independent tracks.

## 2. Global invariants owned by RFCs

Milestones must preserve the RFC contracts, especially:

- persistent identities are 256-bit;
- HashSuite and chunking profile are orthogonal;
- source Stream ownership remains with the caller in Core;
- processing is streaming/bounded-memory;
- identical semantic profile + bytes + HashSuite yields deterministic results;
- physical representation fields do not leak into logical identity;
- no public strategy-interface zoo without measured substitution need.

Do not duplicate detailed byte layouts here; reference the governing RFC/spec.

## 3. M0 foundation — complete

Issues: [#2](https://github.com/definitely-stable/ChunkShift/issues/2), [#3](https://github.com/definitely-stable/ChunkShift/issues/3).

Evidence already delivered:

- full-value-space `Hash256`;
- `HashSuiteId` and logical identity types;
- semantic ProfileFingerprint candidate;
- BLAKE3/SHA-256 reference hashing path;
- BenchmarkDotNet microbenchmarks;
- deterministic synthetic corpus/mutations;
- portable x64/ARM64 lab result format.

M0 is a foundation, not final performance/profile evidence.

## 4. Preparation — [#33](https://github.com/definitely-stable/ChunkShift/issues/33)

Goal: correct confirmed M0 defects and make Core-first execution unambiguous before M1.

Deliverables:

- exact lexical integer canonicalization for profile semantics;
- explicit numeric resource bounds and regression vectors;
- experiment fingerprint bound to corpus generator/version/size/seed;
- source/target and ordered chunk-sequence evidence digests;
- unique missing-payload metric distinct from actual CSP bytes;
- individual measurement samples retained;
- RFC-0003 and synchronized Core-first roadmap/issues;
- [#4](https://github.com/definitely-stable/ChunkShift/issues/4)/[#5](https://github.com/definitely-stable/ChunkShift/issues/5)/[#16](https://github.com/definitely-stable/ChunkShift/issues/16) implementation-ready.

Exit:

- regression tests cover each confirmed defect;
- no release dependency on [#7](https://github.com/definitely-stable/ChunkShift/issues/7)/CSP remains in [#8](https://github.com/definitely-stable/ChunkShift/issues/8)/[#9](https://github.com/definitely-stable/ChunkShift/issues/9)/[#17](https://github.com/definitely-stable/ChunkShift/issues/17);
- [#4](https://github.com/definitely-stable/ChunkShift/issues/4) is the next critical-path implementation issue.

## 5. M1 semantic preflight — complete

Before kernel implementation, freeze the **measurement and semantic oracle**, not the optimized implementation.

Required outcomes:

- ProfileFingerprint v1 independent from content HashSuite;
- profile-aware experiment identities;
- occurrence-aware Boundary Survival;
- sequence-aware Resynchronization Distance;
- actual random-rewrite changed-byte denominator;
- Int64 benchmark offsets;
- automated x64/ARM64 deterministic evidence comparison;
- exact [FastCDC candidate semantics](docs/architecture/FASTCDC-V1-CANDIDATE.md) and pinned GEAR table;
- CSM zero-only semantic-feature rule, strict section state machine and CRC-32C contract;
- broader official BLAKE3 boundary vectors.

Deferred intentionally:

- incremental vs one-shot chunk cryptographic hashing -> benchmark in #4;
- ChunkerId/ManifestSchemaVersion/nullability public-surface cleanup -> #6 (done by #63: unused types removed, IDs are non-nullable reference value objects);
- final real version-pair corpus decision -> #8.

Exit met by PR #38: #4 can be implemented without choosing any known hidden persisted semantic or relying on the previously identified false metrics.

## 6. Core kernels — complete ([#4](https://github.com/definitely-stable/ChunkShift/issues/4))

Goal: one canonical deterministic chunk/hash kernel used by every later Core path.

Deliverables:

- scalar FastCDC reference candidate;
- fixed-size reference;
- HashSuite dispatch for BLAKE3-256 and SHA-256;
- profile parameter validation;
- deterministic chunk boundary/ID records suitable for scanner and CSM sinks;
- benchmark adapters into the M0 lab.

Required correctness:

- empty, tiny, exact-boundary and max-boundary inputs;
- zero/random/repeated/low-entropy/compressed-like inputs;
- one-byte and randomized short-read segmentation;
- seekable/non-seekable input;
- same boundaries/IDs independent of read segmentation;
- scalar/optimized backends must match exactly;
- no mandatory per-byte virtual/interface dispatch;
- bounded buffering.

Required measurements:

- 64/128/256 KiB candidates compared by measured mean (see §11 calibration rule);
- fixed-size baseline;
- BLAKE3 vs SHA-256;
- throughput, CPU, allocations, bytes copied;
- chunk-size distribution/max-cut;
- reuse, Boundary Survival, Resynchronization Distance and Change Amplification;
- x64/ARM64 output digests.

Exit:

- canonical scalar behavior is normative enough for [#5](https://github.com/definitely-stable/ChunkShift/issues/5)/[#16](https://github.com/definitely-stable/ChunkShift/issues/16);
- no optimized backend is accepted unless it matches reference vectors.

## 7. CSM candidate — complete via PR #52 ([#5](https://github.com/definitely-stable/ChunkShift/issues/5))

Goal: streaming binary manifest create/read/verify over the [#4](https://github.com/definitely-stable/ChunkShift/issues/4) canonical kernel.

Deliverables:

- standalone CSM candidate specification;
- writer/finalization path;
- concrete streaming reader;
- verification path;
- canonical ManifestId calculation;
- bounded resource limits;
- golden vectors.

Tests must include:

- empty/small/large manifests;
- truncation at every structural boundary;
- CRC/checksum corruption where applicable;
- checked arithmetic/overflow;
- impossible counts/lengths;
- unknown required feature rejection;
- unknown optional physical section handling;
- ManifestId invariance under allowed physical block/index differences;
- source larger than working memory through streaming create/read path.

Independent evidence:

- vectors contain enough information for an independent decoder/checker;
- before Core 0.1.0, at least one small independent vector verifier/generator must validate the frozen fixtures.

Status: delivered on `main` for the current candidate by the [#68](https://github.com/definitely-stable/ChunkShift/issues/68) evidence stage — independent stdlib-only Python decoder over committed `vectors.json` (PR #73), deterministic mutational reader fuzzing with differential comparison against that decoder (PR #74), cancellation on every manifest API (PR #71) and a 4 GiB source under a 128 MiB cgroup limit in heavy validation (PR #76). The fixtures must be re-verified once #9 freezes CSM v1.

## 8. Raw scanner — complete ([#16](https://github.com/definitely-stable/ChunkShift/issues/16))

Goal: make Core useful without manifest materialization.

[#20](https://github.com/definitely-stable/ChunkShift/issues/20) selected callback + borrowed contiguous memory as the public shape.

Required semantics/tests are owned by RFC-0002/[#16](https://github.com/definitely-stable/ChunkShift/issues/16) and include:

- caller-owned source;
- sequential ordered callbacks;
- borrowed-memory lifetime;
- callback backpressure;
- cooperative cancellation;
- callback exception propagation;
- no post-callback retention without copy;
- one-byte/random short reads;
- non-seekable sources;
- bounded read-ahead;
- no concurrent source use;
- unspecified post-failure Stream.Position.

Additional Core release requirement:

- heavy validation installs the produced NuGet package into a clean NativeAOT consumer and executes real scanning with both BLAKE3 and SHA-256;
- primitive-only AOT smoke is not sufficient once scanner exists.

Status: delivered by PR #61 (`tests/ChunkShift.PackageSmoke`: JIT in CI, JIT + NativeAOT on x64/ARM64 in heavy validation, NativeAOT in release).

## 9. Scanner API bake-off — complete ([#20](https://github.com/definitely-stable/ChunkShift/issues/20))

Compare prototypes sequentially rather than running a maximal matrix immediately.

Phase 1 quickly eliminates clearly worse shapes using representative memory/file/short-read/slow-consumer scenarios.

Finalists then run the full matrix:

- callback push vs pull reader;
- contiguous vs segmented payload;
- Task vs ValueTask handler;
- public delegate vs direct sink;
- FileStream/MemoryStream/non-seekable/short-read;
- ASP.NET Body vs BodyReader adapter;
- x64/ARM64 and JIT/AOT where practical.

Measure throughput, CPU, allocations, copy bytes, first-chunk latency, cancellation latency and bounded read-ahead.

When measured differences are inside calibrated noise, choose the simpler contract.

## 10. Minimal public Core API — [#6](https://github.com/definitely-stable/ChunkShift/issues/6)

After [#5](https://github.com/definitely-stable/ChunkShift/issues/5)/[#16](https://github.com/definitely-stable/ChunkShift/issues/16)/[#20](https://github.com/definitely-stable/ChunkShift/issues/20) (all complete), freeze the smallest candidate API needed by real consumers. The API surface decisions of [#63](https://github.com/definitely-stable/ChunkShift/issues/63) are applied (PRs #95, #96: unreachable types removed, reference-type IDs, Int64/Int32 integer model, synchronous argument validation, Shipped/Unshipped policy in docs/RELEASES.md §8.1). [#65](https://github.com/definitely-stable/ChunkShift/issues/65) is decided by [RFC-0004](docs/architecture/RFC-0004-core-0.1-deferred-product-concerns.md): no progress, compression, signature or anti-rollback surface in Core 0.1.0, and nothing reserved. The symbol-by-symbol audit is [CORE-0.1-API-FREEZE.md](docs/architecture/CORE-0.1-API-FREEZE.md). Its same-commit evidence run (§6) passed on the PR #104 merge ref, and #6 is closed. Closing #6 does not move entries to `PublicAPI.Shipped.txt`; that happens only in the first release PR.

Acceptance:

- no redundant public interface/registry layer;
- ownership/disposal/cancellation documented;
- no public implementation tuning knobs without evidence;
- PublicAPI analyzer baseline updated intentionally;
- clean NuGet consumer compiles/runs;
- trim/NativeAOT warnings are zero for supported Core scenario.

## 11. Core release evidence

### [#8](https://github.com/definitely-stable/ChunkShift/issues/8) — profile bake-off

Dependencies: [#3](https://github.com/definitely-stable/ChunkShift/issues/3), [#4](https://github.com/definitely-stable/ChunkShift/issues/4). Patching is not a dependency.

Use both synthetic + real/local product corpus evidence. Compare at least 64/128/256 KiB mean classes and the required CDC/reference candidates.

Calibration rule (full text: [M0-LAB.md](docs/benchmarks/M0-LAB.md#mean-chunk-size-versus-target)):

- compare **actual measured means**, reported per corpus next to each result;
- where a candidate's parameters allow (fixed-size baseline, other CDC algorithms), calibrate it to a close measured mean of the candidate it is compared against on the same corpus;
- **never treat the nominal target as the actual mean** — the FastCDC M1 candidate measures roughly 1.1–1.3× its power-of-two target on real-like data and up to 4× on degenerate input, and cannot be tuned continuously, so exact equal-mean calibration is not always reachable; pair the closest measured means and state the ratio.

Lab infrastructure for this is complete ([#67](https://github.com/definitely-stable/ChunkShift/issues/67)): streaming-kernel lane (PR #77), profile-derived maximum (PR #78), per-profile resync coverage, mean/target ratio and sample spread (PR #84), bytes copied (PR #87), same-commit comparison (PR #88), boundary-scan hardware-counter evidence (PR #92) and per-experiment process isolation (PR #93). Remaining #8 inputs are the real version-pair corpus and a broader matrix with repeated traces. The #99 pre-freeze note ([CDC-PREFREEZE-DECISION-2026-09.md](docs/benchmarks/CDC-PREFREEZE-DECISION-2026-09.md)) keeps the current Gear prefix semantics, bounds the boundary-scan Amdahl ceiling and adds the `prefreeze` scorecard with a real-corpus manifest (calibration/holdout split) that #8 runs on real multi-version traces. #99 is complete on semantics. Its §11 moves corpus acquisition, the calibration/holdout run, size selection, the stable ProfileId and the default to #8. Warmed-prefix stays lab-only unless the holdout meets the §3 revisit rule.

Output is the selected Core 0.1.0 profile semantics/ProfileId plus evidence, not a package release by itself.

### [#17](https://github.com/definitely-stable/ChunkShift/issues/17) — direct ASP.NET Core host proof

Dependencies: [#5](https://github.com/definitely-stable/ChunkShift/issues/5), [#16](https://github.com/definitely-stable/ChunkShift/issues/16), [#20](https://github.com/definitely-stable/ChunkShift/issues/20).

Before Core release validate:

- request Body streaming larger than RAM;
- non-seekable/short-read/trickle input;
- RequestAborted propagation;
- handler cooperation/non-preemption;
- backpressure;
- middleware byte transformation/decompression semantics;
- CSM artifact response/range/resource ownership where applicable;
- Body vs BodyReader adapter evidence.

CSP/Patching delivery is deferred.

### [#9](https://github.com/definitely-stable/ChunkShift/issues/9) — Core 0.1.0 release gate

Dependencies: [#5](https://github.com/definitely-stable/ChunkShift/issues/5), [#6](https://github.com/definitely-stable/ChunkShift/issues/6), [#8](https://github.com/definitely-stable/ChunkShift/issues/8), [#17](https://github.com/definitely-stable/ChunkShift/issues/17), [#20](https://github.com/definitely-stable/ChunkShift/issues/20).

Must close (owner decisions feeding this gate: [#65](https://github.com/definitely-stable/ChunkShift/issues/65), decided by RFC-0004; the ProfileId rule and verification matrix of [#64](https://github.com/definitely-stable/ChunkShift/issues/64) are settled in PROFILE-FINGERPRINT-V1 and CSM-V1-CANDIDATE §14):

- Core public API;
- CSM v1;
- ProfileId/default profile;
- HashSuite IDs/default;
- ProfileFingerprint semantics;
- scanner ownership/lifetime/cancellation;
- deterministic x64/ARM64 vectors;
- JIT/NativeAOT package-consumer evidence;
- >RAM streaming evidence;
- corruption/resource-bound/fuzz evidence;
- independent compatibility-vector verification;
- console and ASP.NET clean-package examples.

Already on `main` for the current candidate: JIT/NativeAOT package-consumer evidence (PR #61), >RAM streaming (PR #76), reader fuzzing (PR #74), independent vector verification (PR #73), coverage artifact (PR #75), console and ASP.NET Core package-consumer samples (PR #89). What remains is the freeze itself — API (#6), profile (#8), ASP.NET host proof (#17) — and re-running this evidence against the frozen baseline.

Release mechanics that are still open decisions are tracked in [#69](https://github.com/definitely-stable/ChunkShift/issues/69): preview strategy, CLI packaging, and the package-validation/PublicAPI baseline after the first published version.

CSP is **not** part of this gate.

Publishing the 0.1.0 package, or any earlier preview, additionally requires [#24](https://github.com/definitely-stable/ChunkShift/issues/24): squash-only `main`, release-tag protection and immutable releases enforced on GitHub (docs/RELEASES.md §10 step 4). #24 does not block #6, #8 or #17.

## 12. Patching — [#7](https://github.com/definitely-stable/ChunkShift/issues/7)

Starts after [#9](https://github.com/definitely-stable/ChunkShift/issues/9).

Deliverables:

- compare/diff and reuse analysis;
- base chunk locator;
- declarative CSP;
- patch creation/application;
- exact reconstruction;
- final target verification;
- safe publication behavior.

Evidence:

- wrong base/corrupt patch never publishes an unverified target;
- memory/temp/index behavior is explicit;
- actual CSP bytes recorded by the lab;
- compare against full target delivery and xdelta3 on identical file pairs;
- publish Patching only when its own compatibility fixtures are ready.

## 13. Later tracks

Keep these concise until the preceding evidence exists:

- [#10](https://github.com/definitely-stable/ChunkShift/issues/10) — immutable self-indexed Repository packs; starts after useful Patching evidence;
- [#11](https://github.com/definitely-stable/ChunkShift/issues/11) — global index/catalog/crash/concurrency;
- [#12](https://github.com/definitely-stable/ChunkShift/issues/12) — reachability GC/repack/lifecycle;
- [#13](https://github.com/definitely-stable/ChunkShift/issues/13) — HTTP Range and S3/R2;
- [#18](https://github.com/definitely-stable/ChunkShift/issues/18) — optional ASP.NET package only if repeated integration behavior justifies one;
- [#14](https://github.com/definitely-stable/ChunkShift/issues/14) — research CDC/index/filter candidates; no promotion without normal evidence gates.

## 14. Benchmark and corpus policy

The checked-in synthetic corpus is a deterministic smoke/evidence layer.

Release/profile decisions require local/licensable real workloads, including representative .NET/application artifacts where possible. Large corpora are not committed to Git; their manifests record provenance, size, digest and generation/source definition.

Result evidence must preserve:

- experiment/corpus definition fingerprint;
- actual source/target digest;
- ordered chunk-sequence digest;
- individual measurement samples;
- platform/runtime metadata.

Same-process peak RSS is not sufficient for a release-sensitive memory conclusion; streaming/file scenarios use isolated process evidence when that decision matters (`lab ... --isolate`, PR #93).

## 15. Runtime support

.NET 10 is the recommended development/runtime baseline.

`net8.0` remains a package compatibility target while useful, but runtime compatibility must not be described as vendor support. See [docs/SUPPORT.md](docs/SUPPORT.md) for the Microsoft lifecycle date and tested-platform matrix.
