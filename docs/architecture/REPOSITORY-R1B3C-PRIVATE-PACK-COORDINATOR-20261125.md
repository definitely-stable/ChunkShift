# R1-B3c-C — private multi-pack coordinator

Status: **Candidate; depends on #482 writer/reader acceptance**.
Owners #144 / #133 / #10. No Pack v1 freeze, durable promotion or publication.

## Contract

Sequential finite build from a Ready IngestSession, which retains source
ownership and must remain stable throughout BuildAsync. Coordinator owns only
its private pack files and streams, not the input session or directory. Caller
provides a distinct trusted empty output directory under exclusive control.
No lease, hostile filesystem race defense or recovery scan.

Preflight snapshots unique entries and computes contiguous pack plans using
RawPackPolicy. Target, hard physical, frame count and index limits are shared
with reader; per-frame reader cap is also checked. Whole projected pack bytes
(including header/index/footer/trailer) and file count must fit aggregate
limits before reading a staged source or creating a pack file. No wait for
credits held by this transaction. Empty input creates no file. Oversized
target chunks remain single-frame packs. Logical keys preserve source order
and suite changes rotate. This is a finite correctness baseline, not streaming.

Each planned pack reserves its entire physical size before CreateNew, writes
through PrivateRawPackWriter, checks descriptor size/count/suite, closes the
writer destination, then opens for independent full readback with expected
descriptor. Compare readback physical-order key/length with original planned
entries. Reject repeated PackIds within the set. No publication or exposure
until every pack passes and cancellation is checked. At most one open pack
stream at a time; stage reader is disposed after each append. Actual pack
files are private random names unrelated to logical identity.

GetPacks returns immutable verified metadata snapshots, not file handles.
ReadFrameAsync opens the coordinator-owned file, materializes within explicit
cap, re-verifies logical digest and CRC, closes the handle before returning.
It does not recompute whole-pack digest on each access. No path or live
reader is returned. Only one build/read operation at a time; caller must
await before disposal. Admission errors on a Ready read do not invalidate the
set; storage/verification/cancellation after admission aborts it.

## Ownership and failure

Any build fault/cancellation aborts all created packs, including earlier
verified ones. Close then delete; retain ownership and full reservation if
close/delete fails. Aggregate original failure and cleanup errors. Dispose
retries failed cleanup; no new reads after fault/dispose. Input session remains
caller-owned, even on failure. Ambiguous CreateNew failure is conservatively
retained as uncertain path/credit, never deleted by this coordinator; no
automatic reclaim. Diagnostic path list available when idle.

Budget reports retained planned physical bytes and file count, not allocated
filesystem blocks or RSS. It does NOT combine the source session's staging
budget with output quota, and metadata/entry snapshots are bounded by input
session count and pack limits rather than a measured memory budget. This
deliberately retains loose staging as oracle. No cleanup of unknown directory
entries or power-loss guarantee. Fault injection is internal/trusted only.

## Evidence / next

Actual rotation by target/suite, exact aggregate sizes, dedicated oversized
frame, empty frame/batch, aggregate rejection before creation, dirty directory,
corruption between close/readback, cancellation, bounded verified reads after
source disposal, cleanup retry under Windows file lock, active operation
guards. Exact-head hosted CI required; do not merge ahead of #482.

Next R1-B3d: independent rebuild from pack files alone, exact physical location
multiset, duplicate physical locations / PackId conflict policy, hostile/corrupt
file corpus and recovery boundary (never infer active logical root from files).
Then direct-to-pack and owned-memory comparisons, separate R1-C durability.
