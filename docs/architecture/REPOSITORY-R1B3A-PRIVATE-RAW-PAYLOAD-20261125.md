# R1-B3a / #144 — private raw payload append

Status: **Candidate; exact-head CI and review required**. Owners: #133, #144, #10.

This slice creates ONE private payload spool for many verified chunks, not a
persisted Pack v1 candidate. It contains raw concatenated payload bytes and
bounded in-memory frame metadata. It deliberately has no header, index,
trailer, PackId, PackDigest, encoding decision, catalog, promotion or durability
claim. A consumer must not treat the spool as a repository object.

## Contract

- One HashSuite per builder, independent of logical key and future PackId.
- Exact standalone source stream; read at most 64 KiB per operation, check
  malformed read counts, length, EOF and digest of bytes actually appended.
- Admission checks suite, duplicate key, payload byte cap and frame-count cap
  before consuming any source. Duplicate rejection is NOT verified reuse.
  Oversized chunk vs full payload is distinct for future rotation.
- Empty logical chunks consume a frame slot; zero-frame completion is rejected.
- On any failure/cancellation after admission, poison the entire payload;
  no truncation/rollback-and-continue. No metadata returned for a failed build.
- Completion flushes/closes the file, then returns frame metadata snapshot in
  append order; no host-crash claim. Builder retains file deletion ownership,
  retry Dispose on error. No concurrent append/complete/dispose.
- Directory must be trusted and exclusively controlled; a consumer must close
  any independently opened file handles before disposal. External readers,
  dirty directory recovery and ambiguous create failures are outside this slice.
- Limits are payload bytes and frame count, NOT final serialized physical/index
  bytes. The next slices must account for all sections and aggregate pending packs.

## Tests

Both suites, multi-window/empty frames and exact offsets; preflight rejection
without source reads; truncate/overlong/digest mismatch; cancellation; active
operation guard; session handoff with dedup and changed-stage-byte rejection.
These are correctness tests, not kernel crash or throughput evidence.

Next: R1-B3b provisional format and exact physical/index ceilings, one-suite
rotation/oversize policy; R1-B3c writer tail/index and independent reader/parser.
No public API or format freeze. See the pipeline roadmap for evidence gates.
