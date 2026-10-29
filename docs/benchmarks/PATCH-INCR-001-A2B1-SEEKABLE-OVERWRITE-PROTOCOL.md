# PATCH-INCR-001 / A2b1 — seekable single-overwrite, bounded-memory physical CSM

Date: 2026-10-29. [#150](https://github.com/definitely-stable/ChunkShift/issues/150), freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251). **LAB_ONLY / NO_PRODUCTION_ADOPT.** Next step after [A2a](PATCH-INCR-001-A2A-PHYSICAL-CSM-PROTOCOL.md), not a release gate.

## Why this scope

A2a proved that a verified ordered record sequence can be emitted as physically canonical CSM v1. A2b1 replaces A1's full in-memory new target + old record array with a **seekable virtual overwrite view** and one-at-a-time ManifestReader traversal, streaming recomputed new chunks directly into production `CsmEncoderSession`. The first correctness slice intentionally accepts exactly ONE same-length overwrite (1..1MiB). Inserts/deletes/multiple edits, external dirty-range hints and large replacement payloads remain unsupported; A1 still covers their bounded correctness oracle.

## Input trust, verification and immutability

The caller provides **three distinct streams**: seekable readable old content, seekable readable old CSM and private unpublished staging CSM destination, plus old-coordinate overwriteStart and replacement byte[]; the replacement array is copied before the first await.

**Precondition**: the old content and its CSM are immutable, stable snapshots for the *entire* call. Seekable or FileStream alone does not enforce snapshot immutability. A2b1 first uses public `ChunkManifest.VerifyAsync(oldContent,oldCsm)` on the **entire old file** and requires zero verification failures, default hash suite and frozen production FastCDC profile/fingerprint. Full old-source verification is deliberately counted as real source I/O and is a dominant cost for big files. After verification, both streams are rewound and old CSM entries are read through public `ManifestReader` one record at a time. Re-read CSM is checked through EOF with logical totals, record continuity and final integrity result.

The target is not a mutable third-party source whose dirty hints may be false. It is defined as a **virtual stream over the verified immutable old content** with replacement bytes substituted in the one declared interval. Every byte outside that range therefore comes from the old verified snapshot. There is no full target allocation, no new-target physical file, and no inferred dirtiness.

## Exact convergence, streaming writer

1. Read old canonical records sequentially and append unchanged prefix records ending at or before overwriteStart; stop at the previous genuine cut. All copied records are from verified old CSM.
2. Seek the virtual new-content view to that cut and restart **actual production ChunkScanner** from reset Gear state. Emit fresh records straight to production `CsmEncoderSession` with no intermediate record array.
3. For every newly emitted real cut Q after the overwrite end but **strictly before EOF**, advance the old CSM record cursor until its next cut P is at/after Q. Only **P == Q** is admissible (same-length rewrite). Both chunkers reset chunkLength and Gear hash at Q/P, and bytes after them are identical because of the virtual overwrite definition. Hence every later cut/ChunkId is identical. No matching ChunkId-only shortcut.
4. At matched P/Q, stop the chunker with a private sentinel and append remaining old CSM records directly to the encoder. If no real cut matches, continue scanning to EOF and reuse no suffix.
5. Drain old CSM reader through EOF and validate its final integrity before encoder completion. Check full target length. Complete the Core encoder, which sequentially hashes all logical records for ManifestId and writes CBLK, CEND, optional BIDX, FOOT, FileDigest trailer.

This single-window algorithm needs O(one 1MiB replacement + ChunkScanner buffering + CSM parser/encoder buffers) process memory independent of source length. It does **not** prove peak native RSS or O(1) filesystem page-cache residency and does not avoid the initial full old-source verification read.

## Metrics and fail-closed boundary

Returned counters distinguish `OldSourceVerificationBytesRead`, `OldCsmVerificationBytesRead`, `OldCsmEntryPassBytesRead`, `TargetLogicalBytesRead` (bytes actually delivered to target scanner, including scanner read-ahead), `OldSourceTargetPassBytesRead` (underlying old-source bytes needed for the target view), and `ManifestRecordsRehashed` (ALL records, because sequential ManifestId). Result also reports prefix/suffix reuse, proven cut and physical CSM length. These counters are byte counts, **not** full filesystem I/O, wall time or resident memory; in particular it would be invalid to claim total I/O savings by looking only at target scanner reads.

A2b1 never creates a production CSM path or publishes/renames staging. Invalid preconditions and old-content/CSM mismatches fail **before any output bytes**. Write failures or mid-run cancellation may leave partial staged bytes: those must be discarded, never published. No atomicity of caller-owned stream is claimed.

## GitHub-hosted differential validation

The test oracle constructs the full new byte array **only inside test code**, uses public full `ChunkManifest.CreateAsync` (both BIDX off and on), requires **byte-for-byte physical CSM equality**, and independently verifies the result's content and FileDigest using `VerifyAsync` / `VerifyManifestAsync`. Scenarios cover cut-adjacent offsets, EOF, whole-file overwrite, zero/repeating/random content, actual prefix/suffix reuse with skipped target reads, invalid ranges, pre-cancellation, wrong old content, corrupt CSM and destination write failure. The actual implementation accepts seekable streams and never materializes the full target or old logical record list.

## Remaining engineering milestones

**A2b2:** authentic multi-range/insert-delete coordinate map with a virtual seekable splice source; monotone CSM cursor and piecewise cut mapping; source-independent oracle and property-based fuzz. **A2c:** enforce pinned disk snapshots, bounded atomic temporary files and cleanup, incomplete staging retention policy, exact counters and CPU/RSS. **A3:** preregistered 10/100 GiB original workloads, real traces, multi-version holdout and explicit ADOPT/DEFER/REJECT. #150 and #251 stay open. Production API and CSM semantics unchanged.
