# AUDIT-005 — common CSP enabled-mode fuzz oracle

Status: IMPLEMENTED / hosted qualification pending. Refs #137 #144.
Predecessor AUDIT-004 retains Python differential failure inputs separately.
Old CompareOtherCheck compared only exception type/failure flags/output SHA
and deleted its directory before diagnosing a difference. It did not enforce
the default's allocation and publication/residue oracle on alternate modes.

Apply every mutated case in fresh disjoint directories for default, Sequential,
Overlapped and Boundary (each enabled mode exactly once, default first). Off is
intentionally excluded because it omits a verification check. Use the same
oracle for each: permitted exception types, calling-thread 64 MiB budget,
exactly one destination file for success, empty directory for rejection,
known-target pinned SHA256 and non-null successful target identity. Compare
normalized verdict/output SHA256, exact failure flags including combinations
with ResourceLimit, and successful ManifestId. Do not change Python verdict
encoding or mutation generation. Novel targets still force independent dump.

Collect all mode observations before deciding the case. Each failure bundle
retains the existing exact patch/base inputs and adds per-mode name, verdict,
target identity, flags, allocation count, observed entries and diagnostics.
Output inspection exceptions become oracle failures with the original case,
not an unrecorded test crash. Successful iteration directories are deleted
only after checks/dump; failed directories remain until test fixture teardown.
This is a fresh destination oracle, not an existing destination replacement
test. No output file payload artifact retention or general filesystem sandbox
claim. The allocation counter remains calling-thread only, not peak memory
or all-thread allocation; overlapped workers and asynchronous continuations
require separate resource qualification. No tighter numerical budget or product
behavior change.

Four added focused tests: rejected residue plus allocation excess; valid
residue/missing/directory destination; inspection exception; same normalized
verdict with differing raw flags or successful identity. Existing replacement
target regression now checks every enabled mode against the pinned target.
Require exact-head hosted CI, Heavy x64/ARM64 differential and other applicable
gates. No new dependencies, production API or format changes.
