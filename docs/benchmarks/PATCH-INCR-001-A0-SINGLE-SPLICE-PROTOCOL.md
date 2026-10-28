# PATCH-INCR-001 A0 — exact single-splice research protocol

Owner: [issue #150](https://github.com/definitely-stable/ChunkShift/issues/150); parent research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251). 2026-10-28. **Status: SYNTHETIC / NO_PRODUCTION_ADOPT.** No original RunId or decision-grade size/throughput conclusion.

## Input and trust boundary

An already verified and immutable old CSM (canonical old ordered ChunkId/Length records, same shipped profile and hash suite), an immutable new source and a trustworthy single edit event are prerequisites.

The edit event asserts: new = old prefix [0,oldStart) + inserted bytes + old suffix [oldEnd,oldLength). The mapped new suffix begins at newEditEnd = oldStart + inserted length; coordinate delta = newEditEnd - oldEnd. The caller must **prove** the prefix and suffix really are identical at mapped coordinates. A0 accepts the old record array and new bytes **without** an authenticated edit log or old content, so it cannot detect an unreported change outside the declared range. Never use A0 for untrusted inputs or a concurrently mutable file.

Only one half-open splice is supported: replacement, same-length overwrite, insertion, deletion, prepend, append. A0 stores at most 8 MiB of content; it is not a streaming multi-GiB updater. Invalid coordinate mapping, record totals or cancellation fail without producing a CSM.

## Exact cut proof (frozen 16/64/256 KiB Gear profile)

1. Reuse the longest whole-record prefix whose last cut S is at/before the old edit start. Bytes before the edit are equal, so S is also a genuine new cut. Start the actual production ChunkScanner in reset boundary state from new offset S.
2. Scan and hash new bytes. A candidate convergence point Q must be a **real emitted** new cut before EOF, at/after newEditEnd, AND equal P + delta for a genuine old recorded cut P at/after oldEnd. A matching ChunkId alone cannot establish convergence; repeated equal chunks are not unique positions.
3. At P and Q, ChunkBoundaryState resets BOTH chunkLength and gearHash to zero. The trusted edit contract establishes exact equality of suffix bytes old[P..] and new[Q..]. The canonical deterministic Gear state transitions, strict and relaxed masks, maximum cut and EOF must therefore produce identical future lengths; equal content produces identical ChunkIds. Thus it is safe to append the exact old suffix record sequence with translated offsets. If no qualifying cut occurs, scan through EOF and reuse no old suffix.
4. Rebuild ManifestId using the unchanged domain-separated sequential ManifestIdAccumulator over ALL ordered records. The old cryptographic prefix/suffix digests cannot be concatenated.

This is a conditional correctness proof. It depends critically on the truthful edit mapping, an already verified old CSM and immutable content snapshots. The scanner may read ahead beyond its last delivered cut; the A0 measured SourceBytesRead is from actual ReadAsync calls, not merely the returned chunk lengths. Early exit uses a private lab-only exception, without writing a physical manifest.

## Tests / limitations

The A0 xUnit suite compares every ordered absolute offset, length and ChunkId with a **new complete ChunkScanner scan**; additionally compares reconstructed ManifestId with the **public** ChunkManifest.CreateAsync output. It covers dirty edits at min/target/max adjacent offsets, insertions, deletions, prepend, append, empty and full replacements, repeated content, zero/forced-max, 36 deterministic randomized splices, invalid records/coordinates and cancellation. A synthetic zero-pattern case requires actual read avoidance after a proven mapped cut.

Passing synthetic tests is not performance admission: no real multi-version or gigabyte study, trusted dirty event provenance, multi-range mapping, bounded seekable streaming, physical CSM generation or native peak RSS. A0 **does not close** issue #150 / #251 and does not change any public API/CSM/CSP/profile semantics.

Next A1: trusted multiple ranges, old CSM verified reader and safe immutable snapshot. A2: seekable streaming with final CSM physical roundtrip and separate sourceBytesRead/sourceBytesRehashed/oldCsmBytesRead/manifestRecordsRehashed/csmBytesWritten/CPU/RSS metrics. A3: preregistered large original localized-change workloads and fresh heldout decision.
