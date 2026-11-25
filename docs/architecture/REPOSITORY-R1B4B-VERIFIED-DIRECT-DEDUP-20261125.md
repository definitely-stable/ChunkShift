# R1-B4b.2–5 — serial transaction-local verified duplicate reuse

Status: unqualified separate child experiment. #485 accepted; depends on
#486 logical-occurrences/comparator qualification. No PR or additional CI yet.

## Ownership and admission

Opt-in BuildDeduplicatedAsync; existing BuildDirectAsync still rejects duplicate
keys, preserving its independent qualification contract. Both share the
coordinator's single-use state machine and file lifecycle. No Core changes.

Only the opt-in path opens active files read/write. Writer.VerifyDuplicateAsync
selects its own committed frame by ordinal, derives key/offset/length/CRC
internally, and enters Comparing state. No append, complete or dispose can
overlap. Admitted failure poisons writer; pre-admission refusal still aborts
the owning coordinator. No externally supplied location authorizes reuse.

Coordinator records (owned pack reference, ordinal, length) after successful
append, bounded by unique count. Active reuse invokes writer method; closed
reuse resolves same ordinal and length through independently verified snapshot,
opens the owned file and compares actual bytes through comparator. At most two
pack handles: active writer plus one temporary closed-pack reader. The latter
is retained in ownership tracking until close succeeds. No cache or new frame
on duplicate, no rotation solely for a duplicate. Directory/files stable under
exclusive owner contract. No external mutation defense claim.

LogicalOccurrenceBuilder admits every occurrence before duplicate I/O;
append only after comparison succeeds. IncrementalRawPackAdmission remains
unique-only (its LogicalBytes field now represents unique payload on this
path); total logical bytes and occurrences are controlled independently by
sequence. No duplicate physical reservations. At EOF independently verify
every pack, reconcile exact physical budget/count, independently decoded frame count,
unique payload extent and distinct logical keys.
Snapshot only in Ready. Any fault/cancellation aborts all owned files; no
partial logical sequence. Existing unique-only path remains unchanged.

## Evidence to qualify

Both suites, deterministic long zero/random/zero corpus, actual active and
closed reuse, compare scanner logical sequence, one physical frame per key,
exact physical budget and reconstruct every byte. Constant-source exact quota
without duplicate rotation. Logical byte/count cap, closed payload corruption,
cancellation at active duplicate, writer committed-ordinal guard and poison.
Twelve test cases written; no execution evidence. Added both-suite physical
layout/rotation/budget differential against actual accepted finite staging
oracle using independent source slices, cancellation before closed-pack read,
and empty deduplicated input. Source slices use the immutable original array,
not direct pack payload or locations. This is correctness evidence design,
not timed benchmark. Corpus shape and platform
I/O behavior must be confirmed by hosted CI. Existing unique-only tests must
stay green. No mid-read cancellation fault injection yet.

Next: qualify #486, then this opt-in integration on accepted main.
Benchmark file staging vs unique-only direct vs verified reuse (matched input
semantics); capture active/closed hit counts, comparison/flush cost, logical I/O,
RSS/allocations and restore correctness. No performance, global dedup, durable
publication, format freeze or production-default claim.
