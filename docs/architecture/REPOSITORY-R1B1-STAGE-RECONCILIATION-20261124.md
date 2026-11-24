# REPO-133 / R1-B1 — transaction-local verified-stage reconciliation

Status: **Implementation candidate; pending exact-head CI and review**
Owner: #133 / #10. Prerequisite: R1-A accepted in #476.

## Contract

A bounded single-consumer set owns at most a caller-specified number of unique
verified temporary handles. Multiple producers may stage separately, but must
deliver handles serially to the set. This is not a global index, writer lease,
CAS, publication or cross-process deduplication protocol.

- Key is the existing suite-qualified ChunkObjectKey; first admitted stage wins.
- True from TryAddAsync transfers the candidate; false (duplicate) or exception
  leaves ownership with the caller. Never resubmit an already transferred handle.
- Duplicate candidates are compared byte-for-byte with bounded reads and checked
  against the declared length and digest. Even identically corrupted duplicates
  fail; a corrupted winner is not silently replaced. A new entry relies on R1-A
  verification and a trusted staging directory, with a length/readability check.
  Future pack consumption must verify exact bytes again before seal.
- A duplicate may reconcile at the entry limit; a distinct key may not.
- At most two 64 KiB comparison windows, one digest context, and bounded
  dictionary entries per set; this is not an aggregate staging-byte or worker
  budget. The ArrayPool may rent larger arrays; only the declared windows are used.
- No concurrent set operations; callers serialize and close all returned readers
  before cleanup. Dispose blocks new operations, attempts all deletions and
  retains failed handles for retry. No recovery scan/destructive GC.
- Cancellation is cooperative; before admission ownership stays with the caller.
  Comparison does not mutate the set. After admission no cancellation revokes it.

## Acceptance

Linux/Windows and heavy Linux x64/ARM64 exact-head CI, CodeQL, existing consumer
gates. Tests: identical multi-window and empty chunks in both suites, suite
separation, same-key conflict, mutated/truncated/extended stages, identical
corruption, capacity, cancellation, repeated/disposed handles, grouped cleanup
and concurrent producers delivered to a single consumer. Windows tests include
delete-share failure and retry; they are not power-loss or kernel-fault proof.

## Next

R1-B2: reserve aggregate staging bytes and writer slots before starting reads;
define ownership across partially failed producer groups. R1-B3: private raw pack
handoff, verification at consumption, bounded failure/retry and lease design.
R1-C: actual filesystem durability/fault evidence (including runtime #135201).
No Repository public API, persisted schema, release, or Core/CSM/CSP changes.
Keep #133 open.
