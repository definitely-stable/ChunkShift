# R1-B4a.1 — borrowed-memory direct append research candidate

Status: candidate for hosted qualification; physical baseline #482/#483/#484 accepted. Not a production default or performance claim.

## Source audit at main 15a492130ff890d3cc64e284df59b6c9617c3895

Core public ChunkScanner.ScanAsync uses ordered nonconcurrent ChunkScanHandler
callbacks. ChunkScanHandler explicitly guarantees borrowed ReadOnlyMemory only
until the returned ValueTask terminates; consumers must copy before retention
beyond that lifetime. ChunkingKernel.ScanAsync awaits EmitAsync before moving
or reusing the buffer and returns it to ArrayPool only after the scan terminates.
EmitAsync computes ChunkId over exact borrowed bytes before callback. Stable
profile is 16/64/256 KiB min/target/max; kernel requests max(profile maximum,
2*64 KiB), currently 256 KiB (pool may return larger). Empty input emits no
chunk. Caller owns source. Read-ahead and cancellation do not promise source
rewind. Core identity and public API remain unchanged.

Source paths: src/ChunkShift/ChunkScanner.cs, ChunkScanHandler.cs, ChunkInfo.cs,
ChunkScanOptions.cs, Chunking/ChunkingKernel.cs, Chunking/ChunkScanConfiguration.cs,
Chunking/FastCdcProfile.cs and Primitives/HashSuiteIds.cs.

## Candidate

Do not introduce owned-buffer allocation merely to hand data across an awaited
single-consumer callback. AppendMemoryAsync borrows ReadOnlyMemory for the
duration of the operation, sharing exact pre-admission with Stream append.
Hash and CRC each bounded window, write the same window directly to destination
and await completion. No per-chunk intermediate file, no payload-sized copy or
writer-side pooled payload window. Destination must obey the Stream contract;
caller must not mutate/alias memory during the operation. Physical digest and
frame logical digest are still computed by writer; no trust shortcut from
Core-supplied ChunkId. Cancellation or fault after admission poisons writer.
No change to layout, CRC domain, or hash suite. Destination owns any internal
copying or buffering; this is not a kernel/device zero-copy claim.

RawPackScannerBridge.WriteSinglePackAsync is a narrow integration probe:
explicit suite (same as scanner options), await append in the callback,
no queue/tasks escaping, no ownership transfer. Single pack only; duplicate
keys are rejected by writer and any rotation/rejection fails the entire probe.
Empty input returns null, no empty pack. Caller owns source/destination and
discards destination on any failure; no automatic rewind. No manifest output,
no cross-pack dedup, no real multi-pack coordinator or durability. Logical
positions are implicit in physical order for this unique-chunk probe only.

## Evidence to run

Both suites, deterministic multi-chunk corpus, differential against stream
writer under same scanner, exact location sequence and full wire bytes
except independently assigned PackId, reader full verification. Gated sink
asserts the same backing array/slice reaches destination and callback cannot
finish while its write remains active. Tests cover active-operation guard,
reuse after completion, cancellation, digest mismatch, admission and empty
input. No performance evidence and no CI on this experiment branch yet.

## Next after baseline acceptance

Integrate a bounded multi-pack direct coordinator with explicit aggregate
admission (incremental, not preflight whole unknown stream), maintain logical
sequence independently of physical placement; on failure abort owned files.
A duplicate must be consumed/verified or safely byte-compared within the
same borrowed callback; never treat claimed hash as proof. Only then evaluate
owned-memory copies for asynchronous stage parallelism with weighted memory
credits; no buffer may outlive callback without ownership transfer.
Benchmark sequential borrowed path vs file oracle before adding scheduler
complexity. Preserve R1-C durability and #144 freeze gates.
