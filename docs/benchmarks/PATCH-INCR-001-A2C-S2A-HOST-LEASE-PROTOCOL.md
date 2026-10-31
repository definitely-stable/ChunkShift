# PATCH-INCR-001 A2c-S2a — host-pinned immutable-lease contract (no-copy preflight)

Date: 2026-11-01. Owners: [#150](https://github.com/definitely-stable/ChunkShift/issues/150), release research gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251). **LAB_ONLY / NO_PRODUCTION_ADOPT / NO_AUTHENTICITY_OR_CRASH_DURABILITY_CLAIM**.

## Purpose and boundary

A2c-S1 copies the complete old file and old physical CSM into private scratch, then S3 fully verifies the copy and scans the virtual target. For old length N this requires at least N copied old-source bytes plus N old-source verification bytes, and writes N temporary old bytes. It cannot demonstrate an all-in I/O saving against full CreateAsync.

This A2c-S2**a** slice adds a second **lab-only** host-owned execution entry point which accepts already acquired, seekable, immutable-for-the-entire-call old content/CSM streams, an externally authorized old ManifestId and host callbacks for *edit-log authorization* and *snapshot lease assertions*. It avoids the old-source and CSM **copy** passes and reuses the exact A2b-S3 coordinator. It does not attempt to construct or authenticate an OS snapshot, trust root, signed edit receipt, mutable target or filesystem journal. These capabilities belong to the host under accepted RFC-0006.

The host must independently guarantee:
- old bytes and physical CSM cannot change between any verification/read and the final publication, **including between lease assertions**;
- the authorized old ManifestId and the exact ordered old-coordinate edit payloads originate from an authenticated, replay/freshness-checked external authority, not attacker-controlled inputs;
- callbacks can revoke an expired lease and reject unauthorized edits, but success **does not** enforce immutability or establish authenticity;
- destination path and parent namespace are trusted, private and cannot be swapped by an adversary; no symlink/path-race protections are supplied;
- the host owns cleanup of orphan scratch after crash, platform-specific file/directory fsync, durable generation commits, concurrency exclusion and recovery.

**A callback + a read-only FileStream are not sufficient evidence of an immutable snapshot on Linux, Windows or network filesystems.** The xUnit stream fixtures only demonstrate the seam, never OS trust qualification.

## Exact processing

1. Validate distinct seekable readable streams, destination absence and bounded splice count (32) / total payload bytes (4 MiB); deep-copy every payload before awaiting or invoking host callbacks. Hand the authorization callback a *separate* deep-cloned receipt view, so a callback that retains or mutates its input cannot modify the immutable payload copy subsequently passed to S3.
2. Assert the host lease, ask the host to authorize the copied edit transcript, assert the lease again.
3. Independently verify the complete old physical CSM with Core `ChunkManifest.VerifyManifestAsync`; fail closed if its authenticated logical `ManifestId` differs from the **externally supplied** expected value, or if unconsumed bytes remain.
4. Use the *same* A2b-S3 engine on the pinned streams and a private empty staged CSM. S3 fully checks the old **content** against that CSM, proves exact mapped reset-cut convergence across all splices and emits canonical CSM v1 using Core `CsmEncoderSession`. This still reads the entire old source for content verification and rehashes all logical manifest records; it does **not** make total old-source I/O sublinear.
5. Flush staged bytes to disk and close the writer. Independently verify staged physical CSM v1 including ManifestId, FileDigest, content size, chunk count, BIDX, physical length and complete read.
6. Assert the host lease again, allow lab-injected prepublication cancellation/failure, assert the host lease once more; only then perform no-overwrite same-volume `File.Move`. Best-effort scratch cleanup on normal completion or failure.

`File.Move` is a local **visibility** step under a host-assumed filesystem contract, not authenticated publication, guaranteed atomic namespace authorization, multi-file commit or power-loss durability. Snapshot lease callbacks are **checks, not locks**. No independent target-origin trust is established by the CSM hash.

## Evidence and limitations

Linux/Windows hosted test contracts cover complete physical-byte equality against public full CreateAsync (with/without BIDX), separately authorized old ManifestId, unauthorized edit rejection, lease revocation after creating the staged CSM, wrong base/corrupt CSM, cancellation, injected fault, existing output, ordinary scratch cleanup and caller-owned stream lifetime.

Instrumentation includes S3 old-source full verification bytes, old-Csm verification/entry pass bytes, virtual-target scanner reads, reused records, plus old-CSM binding read bytes, staged write/verification bytes and per-invocation process CPU/wall. These counters are **logical API I/O**, not physical disk counters, and process CPU is not isolated. Memory high-water and real 10/100 GiB timing are **not measured in this slice**.

No success threshold is claimed; no 10/100 GiB original/holdout evaluation, OS snapshot provider, signed event log, isolated peak private RSS or reproducible all-in physical I/O evidence exists. `#150` and `#251` remain OPEN. No Core, CSP, CSM, ProfileId, PublicAPI, production default, packaging or publication changes.

## Next strictly gated A2c-S2b / A3

Before any production adoption, separately qualify an actual host source (e.g., immutable content-addressed object or platform snapshot with held lease and authenticated edit receipts), including mutation/revocation and path-race threat tests. Run baseline full `ChunkManifest.CreateAsync` and both A2c-S1 copied and S2a no-copy paths under matched pinned workloads, BIDX on/off, measuring **all-in** CPU/wall, OS-level logical/physical I/O and isolated peak private RSS. Only then preregister large original and sealed holdout with ADOPT / DEFER / REJECT and an explicit fallback strategy. Do not quietly weaken an old protocol's gate.
