# PATCH-INCR-001 A2b-S3 — verified-base, streamed multi-splice CSM

Date: 2026-10-30. Owner [#150](https://github.com/definitely-stable/ChunkShift/issues/150); research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251). Status: **LAB_ONLY / NO_PRODUCTION_ADOPT**.

## Decision

Previously accepted [A2b-S1](PATCH-INCR-001-A2B-S1-SEEKABLE-PROTOCOL.md) proved one trusted seekable splice with real-cut convergence, streaming `ManifestReader` and canonical `CsmEncoderSession`; [S2](PATCH-INCR-001-A2B-S2-VIRTUAL-SPLICE-PROTOCOL.md) constructed a trustworthy single-splice target from an immutable verified old file instead of trusting arbitrary dirty hints. **S3 extends the proof to multiple splices in a single physical CSM emission**, including independent reuse of unchanged gaps.

S3 does **not** add a second CSM serialization format. It reuses Core's frozen `ChunkScanner`, `ManifestReader` and `CsmEncoderSession` for every CSM byte and record hash. A new multi-window coordinator is necessary because S1's fixed old-to-new delta cannot express multiple disjoint inserts/deletes.

## Frozen contract and provenance

Caller supplies a seekable readable **immutable** old content stream, its matching immutable physical CSM, private **empty** seekable/writable staging, and at most 32 ordered nonoverlapping old-coordinate splices. A splice replaces `[OldStart,OldEnd)` with copied replacement bytes; starts must strictly increase, so duplicate-position zero-length inserts are rejected rather than ambiguously ordered. Sum of replacement payload lengths is limited to 4 MiB; old and new content can be much larger. Each replacement is cloned before the first await, before old CSM authentication.

For edit i, let `delta_i = sum_{k<=i}(replacementLength_k - (oldEnd_k-oldStart_k))`. The new edit interval is `[oldStart_i + delta_{i-1}, oldEnd_i + delta_i)`. Outside those intervals the virtual source maps new coordinates back to original old coordinates using the appropriate **piecewise** delta. No external modified file or unverified dirty-map is accepted.

All old bytes are authenticated against the complete old CSM via public `ChunkManifest.VerifyAsync` before any staged output. The old CSM is then rewound and fully verified **again** by streaming `ManifestReader` to EOF. The full old-content verification scan is real O(old file length) I/O; S3 does not claim end-to-end I/O savings from a small target rescan.

**Safety precondition, not enforced:** the underlying old file and old CSM must remain immutable for the entire call, including after verification, and only the caller controls streams and staging. This is not a pinned filesystem snapshot, transactional store, or trusted dirty-range daemon.

## Exact multi-window proof and algorithm

1. Start with the first old record and reset Gear state. For the next edit, reuse whole authenticated records whose old cuts are at/before the edit start, excluding the old terminal EOF (not necessarily a genuine cut). Require offset continuity under the current delta.
2. Restart real `ChunkScanner` over the virtual target from that proven old/new cut and feed every scanned chunk directly into the same production CSM encoder.
3. At each emitted **real new non-EOF cut Q**, find a gap after edit i with `newEditEnd_i <= Q <= nextNewEditStart` (or new EOF after last edit). Map to `P = Q - delta_i`. Require `oldEnd_i <= P <= nextOldEditStart` and P strictly before old EOF. Advance old CSM cursor monotonically and only accept **P equal to an authentic old recorded cut**.
4. At P/Q both frozen FastCDC boundary states reset (Gear hash and chunk length), and the remaining bytes in this unchanged gap are identical under the map. Therefore all following chunk cuts and chunk hashes are the same **until the next edit**. Reuse only complete old records up to the next edit's safe predecessor cut, then restart the scanner there. If no gap converges, continue scanning across later edits and try their mapped candidate cuts.
5. After the final edit's proven convergence, reuse all remaining verified old suffix records. Without any matching cut, scan through new EOF and **never** append speculative old suffix. Empty edit lists copy the fully verified old record sequence unchanged.
6. Drain the old reader to EOF, require final structural/logical/physical verification, validate new target length, and complete the canonical CSM v1 encoder. Sequential `ManifestId` hashing still touches **all** output records; no digest concatenation is assumed.

The candidate requires equal old/new boundary *positions under the splice map* and a real cut on both sides; one matching `ChunkId` is never enough. The proof is induction over gaps: at every matching reset cut the unchanged bytes guarantee the old and new stream emit identical subsequent records until the next edit; the scanner handles all edited portions and uncertain windows.

## Instrumentation and differential oracles

S3 returns independent counters: old source bytes used by full verification, CSM bytes used by verification and second record pass, target virtual scan bytes (including read-ahead), source bytes delivered by the overlay, replacement bytes delivered, **all** manifest records rehashed, reused prefix/interior/suffix records, proven convergences and physical CSM bytes.

The GitHub-hosted xUnit oracle independently constructs the complete new target **only in tests**, runs public `ChunkManifest.CreateAsync` with and without BIDX, and requires **byte-for-byte physical CSM v1 equality**. It also checks public `VerifyManifestAsync` and `VerifyAsync`. Deterministic tests include two separated resynchronizing windows, mixed insert/delete/overwrite, adjacent and EOF changes, empty/whole-file, 45 randomized multi-splice cases, malformed maps, corrupted old CSM/content, pre-cancellation and staged writer failure.

**Memory scope:** streaming chunks and old records, O(32 + 4 MiB payload + bounded CSM/scan buffers), **except optional BIDX retains O(CBLK count)** as in the existing Core encoder. No whole target or old record array in the S3 implementation. Neither native peak RSS nor disk storage I/O is inferred from these counters.

## Explicit promotion boundary

S3 is not a production updater, a public API, a delta patch creation optimization verdict or a new CSM/profile. The staging stream is **never published**. I/O failure/cancellation after partial writes leaves private incomplete bytes that must be discarded. No OS-level snapshot immutability, trusted real-world dirty extents, durable atomic promotion, 10/100GiB calibrated originals, sealed holdout or credible all-in CPU/RSS/I/O win has been demonstrated. #150 and #251 remain OPEN.

**Next A2c:** real immutable seekable snapshots, source authenticity and durable staging/cleanup, count full filesystem I/O + CPU/wall/peak resident memory including old verification and optional BIDX cost, then preregister A3 large original/holdout workloads. Any claimed speedup must be net of old-content verification.
