# PATCH-INCR-001 A2b-S1 — seekable single-splice streamed CSM research

Date: 2026-10-29. Owner #150, parent #251. Status: **LAB_ONLY / NO_PRODUCTION_ADOPT**. No public ChunkManifest API, CSM format or production writer change. A0/A1 and [A2a physical CSM stage](PATCH-INCR-001-A2A-PHYSICAL-CSM-PROTOCOL.md) remain the previously checked baselines.

## What the first streaming slice proves

Old source, old CSM and new source are **independent, readable, seekable, immutable snapshots** supplied by the host. They must remain immutable for the entire operation. The trusted ONE-splice event asserts exactly: new = old[0..oldStart] + replacement + old[oldEnd..oldLength]. The caller proves equality of all untouched bytes and provides the mapped new edit end; a dirty-range heuristic is **not** a trusted event. A2b-S1 currently cannot authenticate these unchanged new bytes and must never ingest untrusted live file modifications.

1. Require empty private staging stream; validate coordinates and lengths **before writing**. Fully verify old CSM against the old source using production `ChunkManifest.VerifyAsync`, demand known shipped profile, matching fingerprint and hash suite. This unconditional old-source scan is the necessary initial trust oracle; it is explicitly charged to old-content I/O.
2. Rewind the old CSM and stream canonical records with `ManifestReader` in batches of 256. Do NOT allocate the whole manifest record list.
3. Feed unchanged complete prefix records into existing `CsmEncoderSession` until the last real pre-edit old cut. Never treat old terminal EOF as a guaranteed reset cut (especially for append).
4. Seek the new stream to that cut and resume the real `ChunkScanner`, feeding new chunk records directly into the encoder. At each real new non-EOF cut Q at/after the new edit end, map P=Q-delta. Advance the old streaming record cursor until its cut reaches or exceeds P. Only if P equals a non-EOF old recorded cut at/after oldEnd does the pair prove convergence. Matching ChunkId values never count.
5. If converged, emit the rest of the old CSM records in order through the same encoder; if not, scan new content to EOF. In either case consume the old reader to EOF and demand valid final physical/logical integrity verdict. Check emitted length and encoder totals, complete staged physical CSM v1.
6. **No publication** occurs. Any failure or cancellation may leave private staging bytes incomplete; they must be discarded. A successful in-memory/lab result is not an atomic file-replacement, crash-consistent package or production acceptance.

The encoder maintains O(one CBLK) record buffers, old-reader O(one CBLK) and scanner bounded buffering, independent of source file length. Optional BIDX retains O(number of CBLK) index entries; disable it for strict bounded indexing. Neither full old source nor full new target is copied into a lab array by the *A2b-S1 implementation*. The test oracle independently allocates arrays to validate full physical byte identity. All record hashing for existing ManifestId remains sequential.

## Measurements and decision gate

The result reports oldContentBytesRead (full initial verification), oldCsmBytesRead (verification + record streaming), newContentBytesRead (actual ReadAsync with read-ahead), newContentBytesRehashed (sum of reconstructed chunk lengths), manifestRecordsHashed (all output records), staged physicalLength, reused prefix/suffix record counts and convergence point. These counts cannot establish end-to-end speedup: full old-source verification dominates and this first slice measures neither wall/CPU nor peak RSS. A large-file ADOPT verdict would require pinned immutable snapshots, credible dirty-range authority, separate baseline/holdout, CPU/RSS and filesystem storage accounting.

## Explicit unsafe boundary and next slices

The caller must authenticate source/edit provenance, detect changes across verification and reconstruction, and ensure the new target is exactly the claimed old-prefix/edit/old-suffix mapping; this code cannot enforce that against mutable inputs. Merely matching the old CSM, old source length and a claimed dirty interval is insufficient. Target bytes mutated outside the declared region **could** lead to invalid reused ChunkIds. The independent full-source oracle in tests would detect this, but is not a production authentication mechanism.

**Next A2b-S2:** stream a bounded set of verified, ordered, non-overlapping splices with piecewise delta, or implement a virtual source whose bytes are generated from the verified immutable old snapshot plus pinned replacements. Add cancellation/fault injection, long-file sparse stream tests and real CPU/RSS. Preserve #150/#251 OPEN; no API/format/release promotion.
