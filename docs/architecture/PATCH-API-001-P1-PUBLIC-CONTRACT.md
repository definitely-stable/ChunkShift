# PATCH-API-001/P1 — first-release Patching public contract decision

**Issue:** [#470](https://github.com/definitely-stable/ChunkShift/issues/470).
**Parent:** [#7](https://github.com/definitely-stable/ChunkShift/issues/7).
**Starting source:** engineering `main@7d7200405f5849efdb4a2d3c6604634d364811a6`.
**Decision:** retain the existing *unpublished* Patching facade as the
first-release **candidate**; accept its engineering API freeze **if and only
if** the exact P1 PR HEAD passes the gates below. Acceptance status and
completed run links live in issue #470, not in a proposed document.
**Not a release:** `PublicAPI.Unshipped.txt` remains unshipped here;
no engineering package, tag, NuGet feed update or publication PR is made
by P1. Moving entries to `Shipped.txt` is reserved for the actual
publication-repository release decision, per [RELEASES.md](../RELEASES.md)
§8.1.

## 1. Complete first-release candidate: precise public inventory

Snapshot at start:
`src/ChunkShift.Patching/PublicAPI.Unshipped.txt` blob
`88a68e73a0d0df955cfa141e26df76f7af6b295a`.

| Public symbol | Frozen candidate contract | Consumer behavior |
| --- | --- | --- |
| `ChunkPatch.PlanAsync(baseManifest, targetManifest, cancellationToken = default)` | Two readable, **forward-only** CSM streams; Task<PatchPlan> | Both CSM integrity verdicts and reuse/missing counts; **not** a comparable plan when HashSuites differ |
| `ChunkPatch.CreateAsync(baseManifest, baseContent, targetManifest, targetContent, destination, cancellationToken = default)` | Base and target CSP v1; readable base manifest, seekable/readable base content and target manifest; readable target content, writable caller-owned CSP output | A CSP with BASE binding; caller must stage output before publishing |
| `ChunkPatch.CreateAsync(targetManifest, targetContent, destination, cancellationToken = default)` | Self-contained CSP v1; seekable/readable target manifest, readable target content and writable output | A patch with no BASE section; same caller-managed output contract |
| `ChunkPatch.ApplyAsync(patch, baseManifest, baseContent, destinationPath, cancellationToken = default)` | Readable/seekable patch, readable base manifest, readable/seekable base content | Task<PatchApplyResult>; verifies and replaces **one path**, accepting intentional in-place replacement with Windows `FileShare.Delete` |
| `ChunkPatch.ApplyAsync(patch, destinationPath, cancellationToken = default)` | Readable/seekable self-contained patch, destination path | Same per-file verified publication; BASE-dependent patch yields `BaseMismatch` rather than silently applying |
| `PatchPlan` | `Base`, `Target`, `IsValid`, `ReusedChunks/Bytes`, `MissingChunks/Bytes`, `UniqueMissingChunks/Bytes` | `IsValid` checks only manifest integrity; caller checks HashSuite equality before using numbers as a viable patch plan |
| `PatchInfo` | `TargetManifestId`, nullable `BaseManifestId`, `HashSuite`, `FileDigest`, `PhysicalLength`, `PayloadEntryCount`, `PayloadBytes` | FileDigest/length bind **that physical CSP instance**, not deterministic patch bytes between versions |
| `PatchApplyResult` | `IsApplied`, `Failures`, nullable `Target` | `IsApplied` true iff `Failures == None` and a verified file was published; `Target` null if parsing never reached embedded CSM |
| `PatchApplyFailure : [Flags]` | `None=0`; `PatchFileDigest=1`, `EmbeddedManifest=2`, `ProfileSemantics=4`, `DuplicatePayload=8`, `PayloadNotInTarget=16`, `PayloadLength=32`, `BaseMismatch=64`, `BaseManifest=128`, `BaseChunk=256`, `DictionaryChunk=512`, `PayloadChunk=1024`, `MissingPayload=2048`, `ContentLength=4096`, `ProfileContent=8192`, `ResourceLimit=16384` | Values are distinguishable, combinable **verification verdicts**, not a general exception taxonomy |

All result objects are immutable to callers (internal constructors, public
getters only). No public encoder policy, comparer implementation, open
stream factory, progress callback, container/tree publisher, signing or
version/channel/authentication abstraction is necessary to exercise the
first-release base/self-contained use cases.

## 2. Error handling, Stream ownership and resource contract

| Condition | Contract / source evidence | Consumer action |
| --- | --- | --- |
| Null, unreadable/unseekable/unwritable role; identical **Stream instance** used for multiple roles; empty `destinationPath` | Synchronous `ArgumentNullException` / `ArgumentException` before method returns its Task; `ChunkPatch.cs` checks all roles | Correct argument and stream capabilities |
| Malformed CSM/CSP structure, invalid content during create | Asynchronous `InvalidDataException` from returned task | Reject input; do not publish incomplete caller-owned output |
| Unsupported hash/encoding, planning `4,194,304` distinct IDs per set or CSP create `1,048,576` entries | `NotSupportedException` from Plan/Create; unsupported representations may throw from Apply | Treat as unsupported/limits rather than an integrity verdict |
| CSP apply integrity failure or operational resource bound | `PatchApplyResult.IsApplied == false`, with one or more `PatchApplyFailure` values (incl. `ResourceLimit`) | Do not infer success from a non-throwing Task; inspect verdict |
| Cooperative cancellation | `OperationCanceledException`; caller-owned streams remain open but their positions may advance | Abandon/retry using explicitly reopened or reset streams |
| File read/write or destination rename/share/permission problem | `IOException` / `UnauthorizedAccessException` (or BCL-specific I/O exception) | Reconcile filesystem, do not treat an exception as a verified apply |
| Incomplete caller-owned CSP destination on Create exception/cancellation | **Allowed**, not rolled back or truncated | Stage into a caller-managed temp file and publish only on successful return |
| `ApplyAsync` file destination | Verified target replaces one destination via sibling staging and rename; existing content preserved on documented pre-commit failures | No crash-durability, hostile path swap, network-FS, or multi-file transaction guarantee |

**File alias boundary:** the public API rejects identical stream **objects**,
not all separate handles pointing to the same underlying file. `ApplyAsync`
allows intentional base-file in-place replacement when the host supplies the
proper sharing mode. Callers otherwise own destination/source alias and path
policy; the unpublished CLI performs stronger product-specific preflight and
no-clobber checks. Neither the public API nor the CLI guarantees hostile
concurrent directory/handle authority.

**Bounded is not constant:** planning materializes up to two distinct-ID
sets with an operational cap of `4,194,304` entries each. Apply/create
have CSP v1 limits, including `1,048,576` default payload entries; a
tested large-resource gate does **not** imply O(1) memory for arbitrary
manifest cardinality. All streams passed to the facade remain caller-owned;
only internal buffers/reader wrappers are disposed. HashSuite IDs and
manifest identities must not be silently reinterpreted.

## 3. Source + binary compatibility and dependencies

- `ChunkShift.Patching` is a **new** `net10.0` library with
  `EnablePackageValidation=true`, `IsAotCompatible=true` and analyzer
  `PublicAPI.Unshipped.txt`. There is **no published Patching baseline**
  against which `PackageValidationBaselineVersion` could honestly run.
  Do not invent one or set a self-baseline; after initial publication use
  the actually published version for the next train.
- Published `ChunkShift` Core `0.1.0` remains the compatibility anchor.
  The Patching project references only Core's shipped public surface;
  real packed Patching consumer is run over both the freshly packed Core
  and **published Core 0.1.0**, requiring identical deterministic evidence.
  Exact NuGet dependency floor/versioning is set and audited separately in
  publication repo, not guessed from an engineering CI package version.
- Numeric flag members, public property names/types/nullability, overload
  arities and optional cancellation token positions are part of the proposed
  first-release API. Do not silently change them after acceptance.
- `PatchInfo.FileDigest` is **not** the target-content digest and cannot
  authenticate the package publisher. Patch binary bytes may differ across
  encoder versions; final target/ManifestId/verdict semantics must not.
- No breaking `ChunkShift` Core 0.1.0 API changes, no mandatory
  `Options` class, `IProgress` overload, `BuildPolicy` or new public
  Core methods; no new public DTOs without a concrete consumer-backed need.
- Package integrity/trust is an **external host policy** by [RFC-0006](RFC-0006-patching-distribution-trust-boundary.md):
  no signing, freshness, monotonic update witness or tree-atomic install
  is provided by these methods.

## 4. Independent P1 acceptance and Windows gap closure

P0 [PR #471](https://github.com/definitely-stable/ChunkShift/pull/471)
was accepted after JIT + NativeAOT x64/arm64, released Core 0.1.0,
both registered HashSuites and real package-consumer cancellation tests.
It did **not** establish a Windows packaged-consumer result.

This P1 change adds a permanent `patching-package-smoke (windows-2025)`
job to regular CI. It builds and packs **both real NuGet packages** at
the exact PR candidate, runs the external `ChunkShift.Patching.PackageSmoke`
app twice, once against newly packed Core and once against published
Core 0.1.0. Both runs must emit `public-facade-contract=pass` for
SHA-256 and BLAKE3; the full evidence files must have equal SHA-256
digests. Windows file sharing/rename and the caller-owned `Stream`
semantics are tested through **the shipped-facing package**, not a
project reference or test-only internal hook.

**P1 decision gate (record actual results in issue #470):**

1. [ ] Exact PR head CI, Windows+Ubuntu build/test, independent CSM/CSP
   vectors/decoder, package-smoke on Linux and the **new Windows NuGet
   consumer** all finish SUCCESS.
2. [ ] Heavy Linux x64/arm64, NativeAOT package consumers, cross-arch
   determinism; CodeQL, dependency review and other applicable checks
   completed without failure.
3. [ ] Independent diff review: only CI and documentation. PublicAPI
   snapshot blob unchanged from `88a68e73a0d0df955cfa141e26df76f7af6b295a`,
   no production Core/Patching/CLI source changes, encoder/default,
   CSM/CSP bytes, runtime dependencies or package metadata changes.
4. [ ] Verify exact tested HEAD and merged main have matching changed
   Git blobs; record run IDs, any open limitations and the explicit
   candidate-API freeze decision in issue #470.
5. [ ] Keep `PublicAPI.Unshipped.txt` untouched in engineering; the
   **publication-repository PR is separate** and its own release-policy,
   dependency-floor, NuGet package version, changelog and publication
   approval are still required.

**Following an ACCEPT here:** the first-release Patching *candidate API*
is frozen for publication handoff. It is **not already shipped** merely
because the engineering issue is closed; any later change to the accepted
candidate requires a new explicit compatibility decision and validation.

## 5. Primary references

- [Current API analyzer snapshot](../../src/ChunkShift.Patching/PublicAPI.Unshipped.txt).
- [Facade and XML exception contracts](../../src/ChunkShift.Patching/ChunkPatch.cs).
- [P0 consumer evidence](PATCH-API-001-P0-CONSUMER-GATE.md).
- [First-release research freeze](PATCHING-FIRST-RELEASE-RESEARCH-FREEZE-20261117.md).
- [Release and compatibility policy](../RELEASES.md).
- [.NET package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/package-validation/overview)
  and [NuGet compatibility rules](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget-package-compatibility-rules).
