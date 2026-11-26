# AUDIT-001 — staging close and cleanup failure ownership

Status: IMPLEMENTED / exact-head hosted qualification pending. Refs #133 #144.
Previous staging used await using, which can replace a write/verification error
with a close error. Cleanup also relied on confirmed create without requiring
confirmed close. In a trusted private directory, Unix unlink may succeed with
an outstanding handle, so path deletion alone is not a safe credit release.

## Contract

- One exclusive create attempt. No returned handle means ownership unknown;
  no close or delete of an unknown path. Retain storage reservation.
- Exactly one close attempt for a returned writer, even on cancellation.
  A throwing close (before or after effect) is not a confirmed close.
  No automatic retry, unlink, or release of storage credit after such failure.
- Only confirmed create and confirmed close authorize failure cleanup.
  Release storage credit only after confirmed deletion, or before any create
  was attempted. A throwing delete remains ambiguous even if it took effect.
- Preserve original exception identity if cleanup succeeds or if there is
  only one failure. On primary plus close/delete failure return AggregateException
  with original primary first and original cleanup second, including HRESULTs
  and any cancellation token. Cancellation plus cleanup failure is a fault,
  not a successful cancellation. No returned stage on any failure.
- Quarantine records ownership and close confirmation separately. File/byte
  credit remains charged; ActiveWriters counts active operations, not OS handles.
  No in-session reclaim, automatic close retry, or durable recovery ledger.
  Uncertain native handle cleanup is not guaranteed by this slice.
- Source remains caller-owned; no change to chunk identity, ordinary FlushAsync,
  successful stage ownership, verified dedup, wire format or production defaults.

## Qualification

Nine added cases: four close failures with/without primary and before/after
effect, two delete failures before/after effect, ambiguous create, cancellation
with successful cleanup, cancellation plus failed close. Existing Windows
delete-share test now asserts both failures and confirmed-close quarantine.
Test-only hooks exercise real disposable private files and injected outcomes,
not actual kernel EIO/ENOSPC or power interruption. Require exact-head hosted
CI, Heavy, CodeQL and process-crash regression. Native persistence backend,
reader-side disposal hardening, and durable reconciliation remain separate.
