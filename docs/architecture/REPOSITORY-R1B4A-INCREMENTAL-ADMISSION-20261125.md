# R1-B4a.2-A — incremental admission for direct pack ingestion

Status: candidate for hosted qualification alongside borrowed append. Physical foundation accepted. Not a coordinator.

Unknown-length sources cannot preflight every frame before reading. The serial
owner must reserve each unique chunk before appending; earlier closed packs
retain their reservation until the whole owning transaction is aborted or
transferred. No wait on credits held by the same transaction.

IncrementalRawPackAdmission shares RawPackPolicy with the writer, aligns index
limits with reader array cap, and adds per-frame reader cap, aggregate physical
bytes/files, unique key count, and logical bytes. Unique-only for now: repeated
suite-qualified keys are rejected across packs, not reused. Logical occurrence
count currently equals unique count; separate occurrence limits and byte
comparison belong to verified dedup, before accepting repeated inputs.

On append to existing pack delta = length + 68; on new pack delta = length +
68 + 168 (including all cold metadata). Full projected pack length, reason,
new-pack indicator, pack ordinal and frame ordinal are returned. Every
admission rejection is nonmutating. Successful reservation is irrevocable in
this single-owner ledger: consumer must honor it exactly once; any later
failure aborts entire owning set. No rollback, waiting, concurrent access,
file allocation, verification, manifest, or actual rotation here. Ledger
has no independent lifetime state: owner must discard it on abort and compare
actual descriptors with reservation. No claim that reservation proves bytes.

Tests: exact charges and dedicated rotation, cross-pack duplicate refusal,
suite namespaces, each aggregate ceiling, empty frame metadata, impossible
chunk, long.MaxValue boundary and 16 x 128 seeded unique traces against an
independent aggregate formula. Synthetic IDs intentionally do not represent
verified content. No benchmark or hosted execution evidence yet.

Next: after #483/#484 accept, qualify borrowed append and ledger together;
reuse private file lifecycle rather than duplicating cleanup/ownership code.
Integrate scan callback with reservations, actual close/readback and group
abort, preserve logical positions independently. Handle verified duplicates
before admitting production workloads; no claimed-key shortcut.
