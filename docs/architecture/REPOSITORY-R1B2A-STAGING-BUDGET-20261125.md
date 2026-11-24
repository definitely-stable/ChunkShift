# REPO-133 / R1-B2a — aggregate staging admission

Status: **Candidate — exact-head CI and review required**
Owners: #133 / #10. R1-B1 accepted in #477.

## Contract

All participating stagers share one explicit session-local StagingBudget.
Before any source read, pool rent or output creation, Reserve atomically admits
the declared payload bytes, one staging file (including empty chunks) and one
active writer. Overflow-safe arithmetic; no partial reservation. Admission
failure is a distinct StagingCapacityException and consumes no source bytes.
The unbudgeted R1-A primitive remains available for isolated correctness tests;
it is not a bounded multi-writer session.

The writer slot stays charged until the staging operation exits, including fault
cleanup. Payload/file credits follow the returned handle, including ownership
transfer to VerifiedStageSet. Only successful deletion releases these credits.
Failed handle deletion remains retryable. If staging fails before creation,
release storage; if creation was attempted but ownership is ambiguous, or
cleanup fails, quarantine the credit. Quarantine remains charged until the
session is retired and recovery is performed outside this slice. No guessed
path deletion or runtime orphan-credit reset.

Fail fast rather than enqueue or block inside a stager. A consumer may retain
all credits while awaiting another producer: an implicit wait could deadlock.
A caller must drain/rotate the batch or schedule a retry after confirmed credit
release; impossible-to-fit chunks need a larger explicit budget or rejection.
No fairness, unbounded waiter queue or automatic retry. The next slice owns
grouped producer ownership and scheduling/backpressure.

Bounds are logical payload bytes, file count, and active writer count (not
filesystem blocks, OS cache, pool retention, RSS, caller-owned streams or
duplicate-comparison buffers). For W active writers, requested copy windows
are 64 KiB × W; each serial reconciliation consumer uses two further 64 KiB
windows. Existing retained directories must be trusted and session-exclusive.
External mutations, external processes, and previously orphaned files are not
covered. Do not restart the budget against a dirty directory and call it a quota.

## Evidence

R1-B1 exact head fe89bef6c74a851b4c649ba3c9bc7c8e21d251ee accepted:
Repository 40/40 Windows/Linux x64/ARM64, CI #45454224268, Heavy #45454224121,
CodeQL #45454224089; main 48321d345c7d8c1b975a4997454dd3be489a9a1d.

New tests exercise multi-stager shared admission, byte/file/writer limits,
empty chunks, ownership and duplicate cleanup, mismatch/cancellation/faults,
Windows deletion retry and quarantined cleanup failure, parallel reservation
and long.MaxValue overflow. Boundary faults are simulated, not kernel fault
or power-loss evidence. Exact-head hosted CI required.

## Next

R1-B2b: bounded producer scheduling and grouped ownership under partial failures,
including drain/rotation without deadlock. R1-B3: verified raw pack handoff.
No public API, format change, cross-process quota, publication or durability claim.
Keep #133 open.
