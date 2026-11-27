# R1-C.2b.2a — experimental private physical file preparation

Status: PREPARED ON ISOLATED BRANCH / NOT QUALIFIED / NOT ACCEPTED.
Refs #133 #144; implementation contract issue #133 comment 7204391044.
Base `5001149b9b5a4d6fa66f60ee891768b538b9d5b3`. Work prepared without
opening a PR while #505/#506 acceptance is outstanding; integrate accepted main
before opening a PR and qualifying exact head. No competing Heavy workflow.

The model-only PrivatePersistenceAttempt stays unchanged. Concrete one-shot
ExperimentalFilePreparation is internal, Windows/Linux scoped, not called by
a production path. It requires a stable, exclusively controlled existing
absolute parent and a portable lowercase GUID .prepare basename. This narrow
temporary naming policy is not PackId or persisted format. Caller supplies
a readable stream, exact physical length, independent expected SHA-256 and
hard length ceiling. No source ownership transfer. One pooled 64 KiB working
buffer (plus bounded stream/hash internals), exact EOF and digest on both
source copy and independent closed-file readback. The file is created with
CreateNew; no delete, retry, overwrite, parent creation, promotion or adopt.
Collision or ambiguous create is reconciliation-required without ownership.

FileStream.Flush(true) is a synchronous explicit file barrier. Close writer
once and verify through a separate read handle. Require read handle closure.
LastAttempted/LastCompleted reuse stage names, never reach FlushParentDirectory.
A post-create failure or cancellation retains the artifact, including unknown
create/close outcome; cleanup closes only a returned handle not already subject
to a close attempt, without advancing stage markers. Preserve primary exception
or aggregate primary and cleanup; close confirmation is separate from stage
acknowledgement. Cleanup completion does not imply persistence. Cancellation
racing after final check may lose to success; no forced interruption of a
synchronous flush. Diagnostics only after settlement. Success state is
ExperimentalFileVerified, not ModelPrepared, not a durable receipt.

Tests cover exact lengths, preflight, collision, ambiguous create, source
mismatch, independent readback mutation, all five before/after boundary
faults, writer/reader close failures before/after effect, dual failure,
cancellation after each stage and concurrent second call. Injected boundary
and close outcomes are not actual native EIO/ENOSPC or power interruption.
Additional suspended source read/invalid read count/cancellation+cleanup
controls and hosted evidence remain qualification work. No aggregate resource
budget, structural RawPack validation, same-parent handle identity proof,
directory barrier or durable ledger in this slice. Trusted stable parent is
a precondition, not defense against symlink/reparse/replacement attacks.
Production default DEFER, no API or format freeze.
