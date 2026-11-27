# R1-C.2b.2c.3 — integrated experimental private preparation

Status: IMPLEMENTED / QUALIFICATION PENDING / CAPABILITY_ONLY. Refs #133 #144.
#511 accepted at d174aaaf365df9827de8769b4d91b20b38f55449, tree
fccc9e78c4b90ab06c0e6762c49adef5c99b5745 equals exact qualified
2bd906bebc5c981edf42d6c0adddfe730a7b5e7c; CI 46075974488, Heavy
46075974492, crash 46075974474, seven applicable workflows PASS;
461 Repository tests per Windows x64/Linux x64/Linux ARM64.

## Scope and ordering

Keep the existing file-only and Pack-only experimental entry points and
success states unchanged. Explicit opt-in internal methods
PrepareWithDirectoryBarrierAsync and PreparePackWithDirectoryBarrierAsync
use the same copy/hash/descriptor/readback implementation, not a second
writer or verifier. After preflight, open one existing parent directory,
validate it and obtain handle-bound filesystem observation before creating
a file. Retain exclusive ownership of that native handle through every
await; all native operations are synchronous and serialized. Caller must
exclusively control the existing stable namespace for the whole attempt.
No directory creation, rename, delete, adoption or publication.

1. Validate arguments, limits and parent path using the existing directory
   barrier policy. Open/observe parent before any create attempt.
2. CreateNew, exact source length/EOF and pinned physical SHA-256, checked
   Flush(true). Observe live writer after file barrier, before writer close.
3. Close writer once. Independently open readback; require exactly matching
   writer observation and same mount/volume + filesystem metadata as parent.
4. Perform the existing full physical byte/Pack descriptor verification on
   that reader. Re-observe live reader before checked close.
5. After reader closes successfully, re-observe the held parent, flush that
   exact handle, re-observe it, checked close, final cancellation check.
   Only then return ExperimentalFilePrepared or ExperimentalPackPrepared.
   These are observations, not durable receipts or publication authority.

Parent/child metadata equality compares filesystem properties after
normalizing only the object identity field; it is not evidence of ancestry.
No path-root inference. Same-volume files elsewhere could pass under a
violated trusted-parent precondition. Writer/readback equality across close
cannot exclude inode/FileId reuse. No stable lifetime lease, hostile path
security, prevention of ABA changes, atomic mount snapshot or filesystem
allowlist. NativeAOT execution is not yet qualified. No public API, format,
dependency, production default or ModelOnly gate changes.

## Failure contract

Before CreateExclusive attempt, failure is RejectedBeforeCreate; parent
handle ownership is recorded separately. After attempted create, any
failure/cancellation is ReconciliationRequired, even if the file/flush
operation took effect. Parent close is attempted once if acquired; never
retry a thrown close. Cleanup of file and parent is independent; if both
fail preserve primary, file cleanup and parent cleanup in order. No cleanup
advances protocol markers. Preserve caller source ownership and retained
artifact, never delete a collision winner. Cancellation is cooperative
before/after synchronous operations; no return while one is in progress.

## Qualification

Hosted Windows x64/Linux x64/Linux ARM64. Real file and Pack paths (both
suites), same held directory handle, identical bytes in a different file
object, collision, native operation seam failures, cancellation, primary
plus both cleanup failures, wrong descriptor. Injected failures are not
real kernel EIO or power loss. Existing process crash workflow is only
regression evidence. Dedicated native AOT execution, integrated process
termination and native failure qualification remain separate gates before
any production promotion. Production DEFER.
