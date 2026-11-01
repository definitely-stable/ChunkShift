# PATCH-INCR-001 A2c-S2b — Linux FICLONE fail-closed private snapshot candidate

Date: 2026-11-01. Owner [#150](https://github.com/definitely-stable/ChunkShift/issues/150); research gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251). **LAB_ONLY / NO_PRODUCTION_ADOPT / NO CROSS-PLATFORM SUPPORT OR DURABLE PUBLICATION.**

## Motivation and evidence gate

A2c-S1 copies all old bytes to scratch and A2c-S2a relies on the host supplying already immutable, seekable inputs. S2b implements ONE specific candidate for that prerequisite: **Linux `ioctl(FICLONE)`**, a full-file copy-on-write reflink clone into a private scratch directory next to the output. The Linux kernel's documented clone is atomic relative to concurrent writes and requires the source and destination to reside on the same reflink-capable filesystem. Subsequent writes to the original do not modify the clone. Original: https://man7.org/linux/man-pages/man2/FICLONERANGE.2const.html.

Unlike a user-space `File.Copy` of the old source, successful FICLONE shares physical file extents instead of copying all N original bytes. This is not equivalent to a universal filesystem snapshot/lease. The private clone remains a writable filesystem object accessible to any actor who can bypass the **trusted host-owned 0700 scratch namespace**. The lab assumes no hostile actor shares this account/namespace and no unsupported remote/network filesystem behavior; it does NOT claim cryptographic authenticity or immutable filesystem enforcement against the host itself.

## Frozen lab contract

- The **host** supplies trusted old source path, old CSM path, absent output path, externally authenticated old `ManifestId`, authenticated ordered old-coordinate edits and lease-validation/authorization callbacks. The host must own the output parent directory and reject symlink, hardlink, path-replacement and privilege-domain races outside this lab.
- Operates on **Linux only**. The entire old source is FICLONE'd to a unique 0700 private sibling directory. Unsupported reflink filesystem/kernel/cross-mount (errno 9/18/22/25/38/95) returns explicit `NotSupportedException`. Other native failures are `IOException`. There is **NO silent byte-copy fallback**. Windows is `PlatformNotSupportedException`.
- The old CSM is **separately copied in bounded 64KiB buffers**, flushed to the file, and closed. This is not a jointly atomic CSM/source snapshot; A2c-S2a independently verifies physical CSM, compares the resulting ID with the *host-authorized* ManifestId, and A2b-S3 independently reads the entire cloned old content to verify that it matches the manifest. The host's edit receipt still must be independently authenticated.
- After the snapshot operation a lab-only callback can mutate the **original** source and CSM paths. Reconstruction must still match the full reference target built from the old snapshot and frozen edits, because all subsequent source/CSM reads use private files.
- A2c-S2a then emits staged canonical CSM v1 through the unchanged A2b-S3, independently verifies the physical output and performs the same fail-closed lease checks and no-overwrite rename. Both nesting levels clean up scratch best-effort on ordinary errors.
- Failure during FICLONE or a denied host lease must **not** publish an output. Existing destinations must never be overwritten. The host must scavenge orphans from process crashes/power failure.
- This lab implementation is not a first-party publisher, a portable snapshot provider, a trusted CAS, an authenticated updater or an OS-level user/namespace security primitive.

## CI and applicability distinction

The differential xUnit test attempts real kernel reflink on the GitHub-hosted Linux runner. If that runner has reflink support, it verifies full physical CSM v1 byte equality against fresh public CreateAsync (BIDX on/off), including post-clone edits of original source/CSM paths. If the runner **does not** support FICLONE, the test asserts fail-closed and cleanup. The latter is **not** evidence of successful reflink operation. Negative tests cover mismatched old file/CSM, unauthorized edits and existing destinations. Windows explicitly verifies unsupported status.

The reported `PinnedSourceLogicalBytes` is the **logical size of reflinked extents**, not bytes read or bytes physically copied. `CopiedCsmLogicalBytes` is the separate CSM copy. `ReflinkElapsed` and `EndToEndElapsed` are wall-clock observations, not calibrated benchmarks; S2a+S3 still read N old bytes for verification and rehash all output CSM records. No physical I/O, per-invocation peak RSS, CPU reduction, 10/100GiB original benchmark, sealed holdout or production ADOPT is asserted.

## Follow-up requirements

1. Verify the **positive reflink branch** on a pinned Btrfs/XFS GitHub-hosted filesystem with an explicit support probe and independent fault/mutation checks. A green job exercising only the unsupported branch does not satisfy this.
2. Qualify authenticated host generation/lease and edit receipt (freshness, rollback, revoked lease, exclusive path and persistent namespace ownership); do not elevate host callbacks to security proof.
3. Measure full CreateAsync, copied S1, host-pinned S2a and reflink S2b under matched original/holdout workloads. Include snapshot acquisition, all old CSM/base verification, stage verification and publication, CPU, wall, physical/logical I/O and isolated peak RSS, plus BIDX on/off.
4. Define durable generation/rename, fsync and journal recovery policy before any production publication claim. If there is no system capable of supplying the required provenance and snapshot, mark S2b **DEFER** instead of relaxing the gate.

No modifications to Core/public API, persisted format, ProfileId, CSP, CSM, package defaults or publication repository. Keep #150 and #251 OPEN.
