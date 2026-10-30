# PATCH-INCR-001 / A2b-S2 — verified-base virtual single-splice source

Date 2026-10-29. [#150](https://github.com/definitely-stable/ChunkShift/issues/150), freeze #251. **LAB_ONLY / NO_PRODUCTION_ADOPT.**

## Background and reason for this slice

Merged PR #427 introduced [A2b-S1](PATCH-INCR-001-A2B-S1-SEEKABLE-PROTOCOL.md), a genuine seekable streaming single-splice algorithm that accepts a caller-owned *new file snapshot* and an asserted edit event. It supports inserts/deletes and byte-exact physical CSM but cannot authenticate unchanged bytes in an externally supplied new snapshot. Concurrent research PR #428 independently implemented streaming same-length overwrite using an overlay of the original verified old source. Rather than merge a **second** CSM cursor/encoder/chunker implementation, S2 extracts that valuable trust-property as a small **source adapter on top of S1**.

S2's target is generated on demand from `old[0..oldStart]`, a copied replacement byte array, and `old[oldEnd..oldLength]`. The target is a seekable read-only stream, **not** another user-supplied mutated file. This *constructively enforces* the claimed unchanged prefix and suffix as long as the old content stays immutable during the call. It supports **one** insertion, deletion, prepend, append or replacement, with replacement bytes limited to 1 MiB. Neither the full target nor its old CSM entries are materialized by the implementation.

## Verification, resource, and correctness boundary

The adapter requires caller-owned seekable readable old source and readable old CSM, private staging, and an old-coordinate half-open interval. It clones the replacement bytes *before* awaiting. It calls unchanged S1 `RebuildAsync` with the original old streams and virtual new source. S1 first fully verifies the old source against CSM and its frozen profile/hash, then streams the old manifest and canonical ChunkScanner into CsmEncoderSession, reuses old records only at mapped genuine reset cuts, and completes physical CSM v1. There is no CSM serializer, cut theorem, or profile duplicated in S2. BIDX/no BIDX and EOF rules remain S1's.

The virtual new source reads the *same underlying old stream* after S1 has finished old-source verification; S1 never simultaneously reads the old source through its verification wrapper and its new-source scanner. The adapter is therefore a strictly sequential sharing exception to S1's independent-stream documentation, valid only under the immutable-old-source precondition. It is **not safe** for a concurrently mutable file, nor does it pin an actual OS/filesystem snapshot, prove file identity after a rename, authenticate replacement provenance, or provide a transaction/atomic publication. If immutability cannot be established, S2 must not be used.

S1's `OldContentBytesRead` includes the full initial verification. S2 separately counts bytes read from the old source to satisfy target-view scanning (`OldSourceTargetPassBytesRead`), and replacement bytes actually delivered. `TotalOldSourceBytesRead` is their arithmetic sum, **not** actual storage-device I/O (cache, prefetch, filesystem overhead excluded). S1's `NewContentBytesRead` counts virtual logical target reads, including read-ahead. Any short target rescanning window cannot imply total I/O speedup while the full old source still needs verification.

Memory use is bounded by 1 MiB copied replacement, S1 scanner/reader/encoder buffers and any optional BIDX entries. The extra adapter uses O(1) state and does not require any old chunk list. All existing ManifestId records are still hashed sequentially; there is no composable cryptographic prefix reuse.

## Differential tests, failure behaviour

The test oracle constructs the actual new array **only for comparison** and invokes the public full `ChunkManifest.CreateAsync`, with and without BIDX. It requires **byte-for-byte physical CSM equality**, independent structural/FileDigest verification, and source-content verification. Coverage includes 12-MiB sparse overwrite and proven suffix reuse, append/delete/prepend, empty/whole-file, 20 deterministic randomized splices, profile-preserving edit application, invalid coordinates, replacement cap, tampered old source, cancellation and staged write failure.

Mismatched old source/CSM fails before any staging writes. Mid-write I/O failure or cancellation can leave partial staged CSM bytes; these are never published. There is no new public API or format change, and #150/#251 remain OPEN.

## Next

A2b-S3 should use an **ordered non-overlapping splice list** to produce one virtual target, and adapt S1's monotone old-CSM cursor to **piecewise** old/new translations and multiple proven cut windows. This may be a meaningful independent algorithm change and must be separately fuzzed against full scans. Beyond it: pinned immutable disk snapshots, staging durability/atomic cleanup, separate old/target/storage byte counts, wall/CPU/RSS and 10/100GiB original+holdout studies before any ADOPT decision.
