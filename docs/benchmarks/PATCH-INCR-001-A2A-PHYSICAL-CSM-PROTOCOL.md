# PATCH-INCR-001 / A2a — physical CSM v1 emission from incremental logical records

**Date:** 2026-10-29. **Owner:** [issue #150](https://github.com/definitely-stable/ChunkShift/issues/150). **Status:** LAB_ONLY / NO_PRODUCTION_ADOPT. Does not close #150 or #251.

## Decision and motivation

A0 proved conditional single-splice Gear convergence. A1 [multi-splice protocol](PATCH-INCR-001-A1-VERIFIED-MULTISPLICE-PROTOCOL.md) verified an old physical CSM against old bytes, assembled exact new logical records and recomputed the correct ManifestId; however, A1 could not produce actual CSM bytes. A2a adds the missing **logical records to physical CSM v1** seam, reusing the production Core internal `CsmEncoderSession`. There is no second CSM serializer, format revision, new profile or public API.

## Implementation contract

`PatchIncrementalA2a.WriteStagingAsync` accepts a **caller-owned private staging stream**, A1 ordered records, expected target length and expected ManifestId, plus the existing optional BIDX flag. It rejects malformed offsets, zero/overmaximum lengths, wrong totals, unexpected ManifestId and pre-cancelled token **before writing a byte**. It then feeds each record to the existing encoder's bounded CBLK buffer; encoder produces CORE, CBLK, CEND, optional BIDX, FOOT and trailer with canonical FileDigest. It requires resulting ManifestId/length/count to match the already checked logical sequence.

The method does not read any target content; the caller must first authenticate the A1 records and source edit log. It does not write or rename output paths, create a publisher or guarantee atomic publication. An I/O error or mid-write cancellation **can leave incomplete bytes in staging**, which must be discarded. There is no success result or public CSM commitment on failure. Full production staging/durability/atomic publication remains owned by the host, per #184 and RFC-0006.

## Exact differential oracles

GitHub-hosted A2a xUnit tests compare the *entire physical CSM byte array* to the public `ChunkManifest.CreateAsync` output from a fresh full target scan, both **with and without BIDX**. They independently verify structural/physical manifest integrity (`VerifyManifestAsync`) and exact source correspondence (`VerifyAsync`). Test cases include multi-splice, empty, append, delete, whole-file replace, deterministic varied patterns, malformed records/totals/identity, pre-cancellation and a failing destination stream.

This byte-equality check is a **same-build frozen implementation oracle**, not a cross-version physical CSM compatibility promise. It is stronger than logical ManifestId equality and can detect mistaken ordering, CBLK grouping, offsets and CRC/footer/index construction in this lane.

## What A2a does NOT demonstrate

A2a still consumes A1's **in-memory bounded 8 MiB** old/source/target arrays and a whole logical record list. A1's full old-source verification and target materialization remain expensive and are not measured away. A2a's bounded *CSM encoder buffer* does not make the end-to-end workflow seekable, streaming, scalable or producer-transactional. There are no original multi-GiB run results, valid external dirty-range authorities, CPU/RSS or physical I/O savings, and no CSM path publication. Keep Core/PublicAPI, RFC wire identity, D7 and research freeze unchanged.

**Next A2b:** immutable seekable old/new snapshots and authenticated dirty-map source; stream old manifest entries, seek only to affected target ranges, append verified unchanged records and new chunk hashes into a *private staged* physical CSM with bounded memory and interruption-safe cleanup; independently compare its content/identity to a fresh full CSM before publication. Measure old/new source bytes read, bytes rehashed, old CSM bytes, manifest records rehashed, CSM bytes written, wall/CPU and peak RSS. A3 original large workload/holdout evidence is still required for any ADOPT decision.
