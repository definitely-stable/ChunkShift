# R1-B4b.2–5 — serial transaction-local verified duplicate reuse

Status: **ACCEPTED in #487; opt-in private correctness only**. Exact head `92be26b7814603dc09bd288bd549d5f16e492e85`; [CI](https://github.com/definitely-stable/ChunkShift/actions/runs/45633393300), [Heavy](https://github.com/definitely-stable/ChunkShift/actions/runs/45633393399), [CodeQL](https://github.com/definitely-stable/ChunkShift/actions/runs/45633393119) PASS; Repository 233/233 Windows/Ubuntu/Linux x64/ARM64, Windows 3731/3731, both package smokes. Merge `87c9756d0105b960880e5868476e682ce2e45602`.

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

## Accepted correctness evidence

Both suites, deterministic long zero/random/zero corpus, actual active and
closed reuse, compare scanner logical sequence, one physical frame per key,
exact physical budget and reconstruct every byte. Constant-source exact quota
without duplicate rotation. Logical byte/count cap, closed payload corruption,
cancellation at active duplicate, writer committed-ordinal guard and poison.
Twelve integration test cases passed on all four CI lanes. Added both-suite physical
layout/rotation/budget differential against actual accepted finite staging
oracle using independent source slices, cancellation before closed-pack read,
and empty deduplicated input. Source slices use the immutable original array,
not direct pack payload or locations. This is correctness evidence design,
not timed benchmark. Corpus shape and platform I/O behavior for these fixtures confirmed by hosted CI; existing unique-only tests passed. No mid-read cancellation fault injection yet.

Next: qualify the ingestion research harness, then execute the screening protocol.
Benchmark file staging vs unique-only direct vs verified reuse (matched input
semantics); capture active/closed hit counts, comparison/flush cost, logical I/O,
RSS/allocations and restore correctness. No performance, global dedup, durable
publication, format freeze or production-default claim.
