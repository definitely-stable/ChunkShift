# REPO-133 / R1-B2b — bounded finite ingest session

Status: **Candidate; exact-head hosted CI and review required**
Owners: #133 / #10. Prerequisite R1-B2a/H and R0.1 accepted in #478.

## Decision

Start with one finite, explicitly bounded batch, not an unbounded input queue.
The session is the sole owner of its budget, producer results and stage set.
The caller supplies a stable list of independent standalone chunk streams and
owns those streams. The staging directory must be trusted and exclusively
controlled by this session; it must be empty at admission. No cross-process
lease or cleanup of unknown files is implemented.

Preflight checks all declared bytes (including duplicates), input count,
per-chunk limits, hash suite and duplicate stream references before any read.
A batch that cannot fit is rejected, not implicitly retried. This is deliberately
conservative: duplicate-heavy input may need more batches than its unique size.
The caller may not alias the underlying stream through multiple wrappers.

Producers run in bounded waves of at most maximumWriters; no Task.Run or
unbounded task queue. All started tasks settle before serial reconciliation,
which preserves first-occurrence input order regardless of completion order.
A producer failure requests cancellation of its peers. Cooperative cancellation
cannot force a stuck source to return. No new wave after a failure/cancellation.
Metadata is O(maximumFiles), active producer tasks O(maximumWriters).
Payload/file credit remains attached to each stage until successful deletion.

## Ownership and failure

Every successful producer places its handle in a session-owned slot before
its task completes. A slot is cleared only after a successful stage-set claim
or deletion. Failed reconciliation, cancellation or producer failure aborts the
whole session; all pending and accepted handles are cleaned after producers
settle. Failed deletions remain owned and retryable through Dispose; primary
and cleanup failures are reported together. Quarantined credit from the stager
remains charged and diagnostic only; there is no runtime reclaim. A failed
session cannot be reused.

Only Ready permits entry snapshots and tracked readers. No partial batch is
exposed, and Ready is not publication. Dispose rejects while Running; cancel
and await first. Dispose blocks new reads even if cleanup fails; close tracked
readers then retry. Returned entries preserve input order and contain only
unique keys. No ownership of stream content is retained beyond staging.

## Gates and limits

Tests: reversed producer progress, duplicate order, concurrent operation rejection,
preflight byte/file/stream alias rejection, dirty directory, later-wave abort,
partial-wave cancellation, external cancellation, reader-safe retry, empty
batches and Windows abort cleanup failure. Existing CI/Heavy/CodeQL and model
gates must pass. This is not a measured performance claim or a crash-safe
transaction; no Pack, ref CAS, durable storage, global index or public API.

Next R1-B3/#144: private raw pack append/verification, ceilings and rotation.
Keep the staging implementation as a correctness baseline, then measure a
borrowed-memory/direct-to-pack alternative. A streaming scheduler can drain
into private packs and release credits only after consumption/cleanup; no
implicit waiting for credits held by the same transaction. #133 stays open.
