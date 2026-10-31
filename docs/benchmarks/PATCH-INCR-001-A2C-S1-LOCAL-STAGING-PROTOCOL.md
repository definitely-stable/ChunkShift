# PATCH-INCR-001 A2c-S1 — local copied snapshots and staged CSM publication lab

**Date:** 2026-10-31. Owner [#150](https://github.com/definitely-stable/ChunkShift/issues/150), research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251). **LAB_ONLY / NO_PRODUCTION_ADOPT / NO_CRASH_DURABILITY_CLAIM**.

## Decision

[A2b-S3](PATCH-INCR-001-A2B-S3-MULTISPLICE-PROTOCOL.md) proves multi-splice exactness against a fully verified old CSM using an immutable old content stream, but cannot ensure that caller-owned streams are unchanged between verification and reconstruction or safely manage CSM output files. **A2c-S1 adds a single, private local-file execution harness**, not a new Core/Patching API, storage format, or update-set publisher.

The harness takes three caller-controlled local paths (old source, old CSM, **new output which must not yet exist**) and S3's bounded in-memory edit payload list, and performs:

1. Validate that paths are distinct, the output parent already exists and output does not exist. Clone every replacement *before the first await* (max 32 edits, 4MiB total); S3 validates all old-coordinate ranges and rejects malformed maps.
2. Create an unpredictable private sibling directory inside the output parent, then **copy** the complete old source and old CSM into separate, private, seekable files using a fixed 64KiB buffer. Flush each copy to disk and close its write handle. Inputs can change later without changing these copied bytes. Copies are not atomic filesystem snapshots; concurrently modified input can yield an inconsistent copy, which the next verification rejects if it disagrees with the CSM.
3. Open the copies read-only and an empty private staged output file. Run **unchanged A2b-S3**: it verifies the copied source fully against copied CSM *before output*, streams authentic CSM records, proves mapped FastCDC reset-cut convergence for ordered splices, and writes **canonical CSM v1** through Core's `CsmEncoderSession`.
4. Flush staged output and close its writer. Reopen and verify the complete physical staged CSM with public `ChunkManifest.VerifyManifestAsync`; require exact `ManifestId`, `FileDigest`, content length, chunk count, physical length and BIDX flag from S3's result.
5. Allow cancellation and an internal injected pre-publication failure to abort. Then perform `File.Move(stagedPath, outputPath, overwrite: false)` from the **same filesystem parent tree**, refusing replacement of an existing output. Normal exceptions and cancellation attempt best-effort scratch cleanup; no public partial output is intentionally created.

**Visibility claim only:** a successful rename on a local filesystem that supports atomic same-filesystem rename makes the *completed* CSM visible at its final path in one operation. This is **not a universal filesystem guarantee**, multi-file transactional installation, authorization of a file, hostile symlink/path-race defense, power-loss durability, directory fsync, rollback prevention or a trusted updater. Network filesystems may not support the assumed semantics. Directory metadata is not fsynced; a crash may leave orphan scratch files or lose a seemingly successful rename. The caller controls input/output paths and authorization under RFC-0006. No production application should invoke this research harness.

## Provenance and authenticity boundary

Copied snapshots eliminate accidental in-process mutation of source bytes during S3 reconstruction and make the *generated target* equal to those private verified source bytes plus frozen replacement bytes. However, the snapshot operation is itself an ordinary non-atomic copy. A hostile external writer/path replacement, symlink or filesystem with weaker semantics is **out of scope**. An old CSM authenticates content identity/consistency, not publisher provenance; an external host trust authority must authenticate any original source and edit events. The edits cannot be inferred safely from an arbitrary mutable new file.

No persisted identity/profile/CSM/CSP change. **Keep #150/#251 OPEN.** No production compatibility/API freeze or publication repository port.

## Accounting and CI oracles

The `Result` contains S3's full counters for old verification, old CSM verification and record pass, virtual-new reads, reused records and final physical bytes. Additionally A2c-S1 explicitly accounts complete **source-snapshot copy bytes**, **CSM-snapshot copy bytes**, **staged CSM written**, **physical CSM verification reads**, elapsed wall time, process CPU-time delta and rough working-set observations.

**Important:** `ProcessLifetimePeakWorkingSet` is the high-water mark of the **whole process lifetime**, not per-invocation peak RSS; `WorkingSetBefore/After` are two samples, not a sampling profiler. CPU time is process-wide, not isolated per task. Counted logical bytes omit OS page-cache prefetch, actual physical storage traffic, metadata overhead and flush write amplification. Copying the old base plus S3's full verification gives at least two old-source passes; these counters cannot establish a positive end-to-end efficiency verdict.

The xUnit file-based oracle compares emitted CSM bytes **byte-for-byte** with a fresh full `ChunkManifest.CreateAsync` (BIDX on/off), including zero-length and mixed edits. Fault cases cover old-source/CSM mismatch, changing the original input paths after the private copies are complete, cancellation and injected failure before rename, existing final output and input/output path aliases. It requires no partial published output and no residual scratch directory on ordinary tested failures.

**Next A2c-S2 (not included):** explicit trusted snapshot provenance/lease and host-owned path authority, staged file lease/recovery/scavenge policy, OS-specific crash/rename durability tests, isolated process CPU/RSS profiling on pinned large local files, wall/I/O baselines that count **both** snapshot copying and old full verification. **Next A3:** preregistered original 10/100GiB and sealed holdout with an ADOPT/DEFER/REJECT outcome. A2c-S1 is an execution integrity seam, not performance adoption evidence.
