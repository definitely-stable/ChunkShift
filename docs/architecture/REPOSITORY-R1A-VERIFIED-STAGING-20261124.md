# REPO-133 / R1-A — verified bounded staging

Status: **ACCEPTED in #476; staging only**
Evidence: exact PR head `80bdb56c22e825c61358a1304100fbf0e8ff6670`, [CI #45350628834](https://github.com/definitely-stable/ChunkShift/actions/runs/45350628834), [Heavy #45350628533](https://github.com/definitely-stable/ChunkShift/actions/runs/45350628533), [CodeQL #45350628705](https://github.com/definitely-stable/ChunkShift/actions/runs/45350628705). Repository 26/26 Linux x64/ARM64 and Windows; existing package consumer gates pass. Merge `f805ecd89bbf616f007395bc958b96aac7da85b7`. #133 remains open.  
Owner: [#133](https://github.com/definitely-stable/ChunkShift/issues/133), under [RFC-0001 §12](RFC-0001-target-architecture-2026.md) and [R0 preflight](REPOSITORY-R0-PREFLIGHT-20261124.md).  
Date: 2026-11-24.

## Scope

Implement the first **real byte-processing** part of Repository ingest, without publishing
or fixing a physical pack/index format:

1. `ChunkObjectKey` explicitly binds the already-published Core `HashSuiteId` and `ChunkId` values. Physical location, format version, encoding and generation are excluded.
2. `VerifiedChunkStager.StageAsync` reads caller-owned data with a **64 KiB rented byte buffer**, a caller-selected hard maximum chunk size and exact expected length.
3. Every staged byte contributes to the selected Core-compatible SHA-256 or BLAKE3-256 digest; the observed digest is checked in fixed time against the expected `ChunkId`.
4. Staging files use random non-content paths and `CreateNew`; no CAS semantics, no trust in filenames, no in-place rewrite and no global registration.
5. On success, the caller owns a `VerifiedStagedChunk` handle and must dispose it to remove temporary bytes. On failure/cancellation, the stager attempts best-effort cleanup; a failure may leave an **orphaned unreferenced temp file**, never a published object.
6. Input streams remain caller-owned for all success/error/cancel paths. No `IRepository`, transport or general strategy interface is added.
7. Both code and tests are included in `ChunkShift.slnx`; Ubuntu and Windows hosted CI execute the tests. The assembly is internal/non-packable and targets net10.0 only.

A zero-length byte sequence is permitted at this staging layer. Whether Pack v1 will
admit empty logical chunks is a distinct **format** decision under #144, not accidentally
frozen by this helper.

## Explicit non-goals

- No durable seal, atomic rename, pack publication, `CatalogGeneration`, `ReadView`, CAS, GC or deduplication across writers.
- A concurrent ingest can produce multiple **temporary staged copies**; they are never authoritative physical representations. Their reuse/commit is #133 R1-B/#144 work.
- `FlushAsync` is used to finish ordinary temporary output, but **not** presented as an `fsync`/power-loss guarantee.
- No loose-object store, public NuGet package, stable repository API, or CSM/CSP changes.
- No throughput claim until a registered, isolated-process evidence run is added.

## Failure gates

| Scenario | Required outcome |
| --- | --- |
| Wrong suite/digest/length, or input shorter than declared (including unexpected empty input) | No returned verified stage |
| Trailing byte after expected length | Reject; remove stage |
| Pre-cancel and mid-read cancellation | Reject; caller stream stays open |
| Source read exception / invalid output path | No logical publication; pre-existing files preserved |
| Eight concurrent identical stages | Separate temporary paths, same verified logical key |
| Dispose temporary handle | Stage file removed; further reads rejected; deletion failure is retryable |
| Unsupported HashSuiteId | Fail before creating a stage |
| Data larger than selected maximum | Reject before consuming source |

Runtime behavior is checked with official `abc` SHA-256 and BLAKE3 conformance digests,
plus empty/large chunks. The digest provider versions are unchanged.

## R1-A hardening contract

- Source is a standalone chunk stream ending at EOF, not a shared concatenated
  stream. A non-cooperative read cannot be forcibly interrupted. Cancellation is
  checked after reads (including EOF) and after file close, immediately before
  ownership transfer. A later cancellation does not revoke a returned handle.
- Read requests never exceed 64 KiB even if ArrayPool rents a larger array.
  Invalid negative/oversized read counts fail explicitly.
- The staging directory is trusted, exclusively controlled by the caller/repository.
  A path-based OpenRead is not an integrity guarantee against external modification.
  The pack builder must verify the exact bytes it consumes before sealing or
  discard the private uncommitted pack on mismatch. No trust-by-filename.
- A handle is single-owner and not thread-safe. Readers must close before Dispose.
  Failed deletion prohibits new readers but remains retryable; successful deletion
  is idempotent. Failed StageAsync cleanup is best effort and may leave an orphan.
- Bounds are per operation only. R1-B owns the aggregate writer RAM/temp budget
  and grouped resource ownership. A caller starting multiple StageAsync operations
  must retain/dispose every successful result even if another task fails.
- Tests include Core scanner identity parity for both suites with non-seekable
  short reads, malformed read counts, cancellation at EOF and after close,
  Windows delete-share retry and simulated write/flush/post-close exceptions.
  The internal checkpoint callback simulates faults at those boundaries; it
  does not establish real kernel write/flush/close fault or power-loss behavior.

## Next eligible slice

After CI/peer review of R1-A, #133 R1-B must prove actual reconciliation of duplicate
verified stages, writer lease/failure behavior and non-overwriting commit into the raw
Pack v1 builder. The visibility point remains the ref-CAS from #145, **not** creation of
a staging file. Before claiming durable Repository publication, explicitly resolve
[dotnet/runtime #135201](https://github.com/dotnet/runtime/issues/135201) on the
actual production runtime and test its fsync/rename/directory sync strategy.
