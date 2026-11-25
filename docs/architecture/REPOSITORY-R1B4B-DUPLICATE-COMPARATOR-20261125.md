# R1-B4b.1 — bounded positional duplicate comparator

Status: **ACCEPTED in #486**. Exact head `fde07a4ad1d2d4ccaf98dd2227bcfc3c448e2390`; [CI](https://github.com/definitely-stable/ChunkShift/actions/runs/45625365221), [Heavy](https://github.com/definitely-stable/ChunkShift/actions/runs/45625365503), [CodeQL](https://github.com/definitely-stable/ChunkShift/actions/runs/45625365586) PASS; Repository 221/221 Windows/Linux x64/ARM64, Windows solution 3719/3719. Merge `20c9d6b7c54bdc33b52cb19d17ba06dc7574329a`. No duplicate ingestion enabled.

## Contract and rationale

Preserve #133 VerifiedStageSet exact byte comparison + rehash. No bare-key
trust (#139). Borrowed candidate stable through awaited comparison; existing
raw frame metadata bound by the owning private coordinator to the exact file
handle/path under its exclusive directory contract. No concurrent writer,
read, mutation, or disposal. Failure/cancellation must abort owning transaction.
Comparator itself does not own files, writer state, reservations or occurrences.

.NET 10 RandomAccess.ReadAsync uses explicit file offset without advancing file
position for seekable files; short reads possible. FileStream has its own
logical position and buffered writes. Sources:
- https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess.readasync?view=net-10.0
- https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.safefilehandle?view=net-10.0
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/filestream-doesnt-sync-offset-with-os

Active: require exclusive read/write seekable FileStream; recorded raw frame
key/length/ordinal/extent and writer payload end; reject invalid range before
buffer rent. Verify cursor, FlushAsync, access SafeFileHandle, verify physical
length, positional compare, confirm length/cursor unchanged. Flush for buffered
write visibility, not durable flush. Active writer integration must explicitly
expose owner-bound committed frame locations and serialize calls. Current
production coordinator still opens write-only handles; no change in #485.

Closed: caller reopens the exact owner-bound independently verified pack.
Resolve key from VerifiedRawPack, require candidate length and canonical
payload extent; compare actual selected bytes, ensure observed physical length
and cursor unchanged. Same length does NOT prove physical identity; owner
binding required. Does not reverify entire PackDigest on every lookup.

Compare in pooled 64 KiB requested windows, loop short reads, reject conflicts,
recompute CRC32C and logical digest of equal stored bytes. Both inputs must
match exact key; equal corrupted bytes and repaired CRC are insufficient.
No copy of incoming payload, no full-frame allocation. No persistent cache,
logical admission, physical mutation, dedup success registration, or durability.

## Evidence to qualify

Real file handles, buffered small frame, multi-window frame, positional read
then subsequent append and independently verify completed pack, both suites;
closed reopened pack including empty frame. Wrong key/length/extent/cursor,
byte conflict, CRC mismatch, equal corrupt bytes with repaired CRC, later
mutation and truncation of closed file, missing key, pre-cancellation. Test
faults deliberately violate stability for negative evidence. No mid-operation
cancellation injection or platform-specific performance evidence yet.
No tests executed on this branch.

## Next

First accept #485. Qualify logical sequence and comparator on refreshed main
without enabling reuse. Then expose writer-committed physical metadata under
single-owner guard, switch only duplicate-aware direct path to read/write
handle, compare active vs closed frame using owner binding. Record occurrences
only after successful comparison, maintain unique and logical budgets separately,
preserve one physical frame per unique key. Test actual duplicate-heavy restore,
corruption/cancellation and no unnecessary pack rotation. Benchmark flush/read
cost before considering bounded cache. No global single-flight or public API.
