# Core 0.1.0 public API freeze audit (#6)

Status: API audit complete. #6 closes after the same-commit evidence run of [§6](#6-same-commit-evidence-run-required-to-close-6).  
Issue: [#6](https://github.com/definitely-stable/ChunkShift/issues/6)  
Audited surface: `src/ChunkShift/PublicAPI.Unshipped.txt` at `main` `22252b2`, 123 entries; `PublicAPI.Shipped.txt` is empty  
Inputs: [#20](https://github.com/definitely-stable/ChunkShift/issues/20) scanner shape, [#63](https://github.com/definitely-stable/ChunkShift/issues/63) surface decisions (PRs #95, #96), [#64](https://github.com/definitely-stable/ChunkShift/issues/64) profile/verification contract (PR #97), [RFC-0004](RFC-0004-core-0.1-deferred-product-concerns.md) (#65)

## 1. What "freeze" means here

- #6 accepts this surface as the **Core 0.1.0 API candidate**. From now on, a new public Core member needs P0 evidence: a defect, or a consumer that cannot work without it. Evidence belongs in its own issue.
- Nothing moves to `PublicAPI.Shipped.txt` when #6 closes. Entries move only in the release PR that sets `VersionPrefix` for the first published version (docs/RELEASES.md §8.1). Until then `Unshipped.txt` can still change through reviewed PRs, but only under the rule above.
- The profile ProfileId/default selection is [#8](https://github.com/definitely-stable/ChunkShift/issues/8)'s decision and changes no API shape: `ChunkingProfileId` is an open string identity, and `ProfileId = null` selects the default.

## 2. Scanner

| Symbol | Decision | Contract pinned by |
|---|---|---|
| `ChunkScanner.ScanAsync(Stream, ChunkScanHandler, ChunkScanOptions?, CancellationToken)` | keep | `ChunkScannerTests` |
| `ChunkScanHandler` (delegate returning `ValueTask`) | keep (#20 phase-2 evidence) | `HandlerValueTask_IsConsumedExactlyOnce` |
| `ChunkScanOptions` (`ProfileId?`, `HashSuite?`, init-only) | keep | `NullSelections_UseTheDefaults`, `InvalidArgumentsAndUnsupportedSemantics_FailBeforeReading` |
| `ChunkInfo` (`Index`, `Offset`: `long`; `Length`: `int`; `Id`) | keep; shared with `ManifestReader` (#63) | `Offset_IsRelativeToBeginningOfScan` |

XML documentation checked against the contract:

- **Borrowed-memory lifetime:** `content` is valid until the returned `ValueTask` completes (`ChunkScanHandler`). Tests: `BorrowedContent_RemainsStableUntilAsyncHandlerCompletes`, `BorrowedContent_IsReusedAfterCallbackCompletes`.
- **Sequential, non-concurrent delivery with backpressure** (`ChunkScanner` remarks). Test: `Callbacks_AreOrderedAndNeverConcurrent`.
- **Cooperative cancellation; a running handler is not preempted.** Tests: `CancellationDuringHandler_StopsBeforeNextCallback`, `Cancellation_DoesNotPreemptHandlerThatIgnoresToken`, `CancellationAfterFirstChunk_DoesNotReadToEnd`.
- **Index/Offset relative to the start of the scan**, even on a seekable stream at a non-zero position (`ChunkInfo` remarks).
- **The caller owns the stream; it is never disposed; the position after a failure is unspecified.** Test: `HandlerFailure_PropagatesAndDoesNotDisposeSource`.
- **Argument and unsupported-semantics errors are thrown synchronously**, not by the returned task (#63). Tests: `PublicArgumentContractTests`, `InvalidArgumentsAndUnsupportedSemantics_FailBeforeReading`.

No defect was found, so this audit makes no XML change.

## 3. Manifest

| Symbol | Decision |
|---|---|
| `ChunkManifest.CreateAsync` / `VerifyAsync` / `VerifyManifestAsync` | keep: one-shot, `Task`-returning, caller-owned streams |
| `ManifestCreationOptions` (`ProfileId?`, `HashSuite?`, `IncludeBlockIndex`) | keep. RFC-0004 adds nothing and reserves nothing. |
| `ManifestReader` (`OpenAsync`, `ReadAsync(Memory<ChunkInfo>)`, `IsCompleted`, `VerificationResult`, `Dispose`/`DisposeAsync`) | keep: concrete buffered reader with no full materialization (`CsmStreamingScaleTests`) |
| `ManifestVerificationResult` (`IsValid`, `Failures`, `Manifest`) | keep: a mismatch is a result, not an exception (RFC-0001 §10) |
| `ManifestVerificationFailure` (flags: `BlockCrc`, `LogicalTotals`, `ManifestId`, `FileDigest`, `ProfileSemantics`, `Content`) | keep; matrix pinned by `VerificationSemanticsMatrixTests` (#64) |
| `ManifestInfo` | keep all ten properties (§3.1) |

### 3.1 `ManifestInfo` physical properties

`ManifestInfo` describes **one emitted CSM representation**: its logical identity and the physical artifact that carries it. It is not a pure logical record. Its XML summary ("Describes one CSM manifest representation and its logical identity") already says so.

| Property | Kind | Why it stays public |
|---|---|---|
| `HashSuite`, `ProfileId`, `ProfileFingerprint`, `ManifestId`, `ChunkCount`, `ContentLength` | logical | identity and totals every consumer needs |
| `FileDigest` | physical | digest of the exact artifact bytes; used for transport/cache integrity and, under RFC-0004 §4, bound by a detached envelope |
| `PhysicalLength` | physical | exact artifact length; used by hosts for `Content-Length` and range bounds, and bound by the envelope together with `FileDigest` |
| `ChunkBlockCount` | physical | inspection and tooling (`chunkshift inspect` prints `cblk-count`) |
| `HasBlockIndex` | physical | tells the consumer whether BIDX-based positioning is available; `inspect` prints `bidx` |

All four physical values are fixed by the CSM v1 TRAILER/FOOT and change only with a new CSM format major. Exposing them does not bind `ManifestId`, which stays independent of physical encoding (RFC-0001 §8.8). The alternative, a separate physical-info type, would add a public type and has no consumer. This is recorded as an intentional decision, not an accident of the #5 implementation.

## 4. Identities

| Symbol | Shape | Decision |
|---|---|---|
| `Hash256` | 256-bit value struct; hex-lower format/parse; all-zero valid | keep |
| `ChunkId`, `ManifestId`, `ProfileFingerprint` | value structs wrapping `Hash256`; `default` is the valid all-zero value | keep |
| `ChunkingProfileId`, `HashSuiteId` | immutable reference types over a validated string (#63: no null-`Value` default struct) | keep |
| `HashSuiteIds` (`Blake3256V1`, `Sha256V1`, `Default`) | static well-known values | keep |

Not added in 0.1.0, because no consumer needs them yet and each can be added later without a break: `IParsable<T>`, `ISpanFormattable`, `TypeConverter`, JSON converters, profile/hash-suite registries or interfaces. `IChunker`, `IChunkHasher` and similar strategy abstractions are excluded by AGENTS.md and RFC-0001 §18.

## 5. #6 invariants checked

| #6 invariant | Result |
|---|---|
| caller owns streams passed to Core operations | yes (§2, §3) |
| no required full manifest materialization | yes (`ManifestReader`, >RAM smoke, PR #76) |
| no public `IChunker`/`IChunkHasher`/`IChunkBoundaryFinder` | none in the surface |
| no pipeline ownership or buffer/pool/SIMD/worker tuning | none in the surface; options carry only semantic selections plus `IncludeBlockIndex` (physical, identity-neutral) |
| verification mismatch is a result | yes (`ManifestVerificationResult`) |
| no mandatory allocation per chunk | scanner steady state is O(1) (#20 evidence) |
| no compare/diff/CSP types | none |
| no DI/ASP.NET requirement | none; the ASP.NET sample uses plain `Request.Body` |
| no reserved progress/compression/signature/rollback surface | none (RFC-0004) |

## 6. Same-commit evidence run required to close #6

The #98, #100, #101 and #103 changes happened after the last complete release-level run. Their own PRs report unchanged vectors, but #6 closes only on one run of all existing gates **on a single commit** that contains this audit. No new framework is needed.

- [ ] `dotnet restore` / `build -c Release` / `test -c Release` (CI `build-test`, ubuntu + windows)
- [ ] PublicAPI analyzer clean against `PublicAPI.Unshipped.txt` (part of the Release build)
- [ ] package validation + JIT package-consumer smoke (CI `package-smoke`)
- [ ] NativeAOT package smoke x64 and arm64 (heavy validation `native`)
- [ ] large source under a memory limit (heavy validation)
- [ ] independent CSM vector decoder (CI: `tools/csm-fixtures/generate.py --verify`; `CsmIndependentVectorTests`)
- [ ] reader fuzzing (heavy validation)
- [ ] x64/arm64 determinism digests (heavy validation `determinism`)

Identities that must be byte-identical to the committed golden values: the ordered `ChunkId` sequences, `ManifestId`, `ProfileFingerprint`, the CSM v1 vectors, and the JIT/NativeAOT smoke identities. Record the run IDs in #6 when closing it.
