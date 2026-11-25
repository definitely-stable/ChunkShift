# R1-B3d — independent physical location rebuild (P0)

Status: **ACCEPTED in #484; physical rebuild only**. Exact head `294a34620ca6fec4db0e5d419374a86bddbadb76`; [CI retry](https://github.com/definitely-stable/ChunkShift/actions/runs/45595671573), [Heavy](https://github.com/definitely-stable/ChunkShift/actions/runs/45595671426), [CodeQL](https://github.com/definitely-stable/ChunkShift/actions/runs/45595671579) PASS; Repository 167/167 Windows/Linux x64/ARM64. Merge `3e205ca9a675ca05b1385f45787ad55aa78535ab`. Owners #144/#133/#10.
This is the packs-alone correctness baseline, not GlobalIndex (#145), Catalog
recovery, durable publication, or a Pack v1 freeze.

## Boundary and invariants

Input is an explicit stable list of local candidate paths owned/controlled by
the caller. This primitive intentionally does not enumerate a repository,
interpret names as identities, follow a recovery policy, select reachable
objects, delete unknown files or infer the active logical root. Paths are
normalized and repeated canonical paths rejected; aliases/hardlinks with
repeated PackId are also rejected. No claim of defense against a concurrently
mutating directory or symlink target. One source handle at a time.

Preflight all candidates before parsing: number of packs, minimum extent,
per-pack physical ceiling and aggregate file length, checked by subtraction.
Reader limits include frame count, index bytes and single frame bytes.
Each subsequent parser is additionally restricted to the remaining aggregate
location count before count-derived allocation. Byte totals bound logical
file lengths, not filesystem blocks, native allocation or total RSS.

Verify every pack through the independent full P0 reader, with no external
descriptor in this packs-alone mode. Retain observed descriptor and exact
physical-order location entries: suite-qualified key, PackId, frame ordinal,
physical offset, raw/stored lengths, raw encoding code and physical checksum.
P0 raw lengths coincide; future encoded formats require actual stored lengths
and codec metadata. Observed SHA256 is not proof of trusted provenance.

Every occurrence in a distinct PackId survives, even if the logical key is
repeated. Never deduplicate away physical replicas. Repeated PackId (including
identical bytes under two filenames) fails the entire candidate set; no winner
selection. Conflicting raw lengths for the same logical key also fail. Same
digest under different suites remains a different logical namespace.

Result is a bounded in-memory immutable metadata snapshot in input pack order,
then physical frame order; ordering is not part of the location multiset.
No partial results on failure or cancellation. No open handles retained.
The caller may materialize copies of metadata arrays. Full payload is not
retained; cold metadata remains O(max locations + max packs + per-pack index).
A segmented streaming rebuild, trusted descriptor mode, recovery classification
and scalable index writer are follow-on work.

## Evidence

Independent oracle from original raw chunks, known absolute offsets, assigned
PackIds and bitwise CRC; exact location multiset under reordered file lists,
cross-pack reuse, suite namespaces and empty chunks. Reject byte-identical
and divergent duplicate PackIds, duplicate paths, aggregate caps, truncation
and corruption; preserve all input files. Coordinator integration: copy
serialized pack files, dispose staging and original private coordinator,
rebuild solely from copied files, compare exact original physical multiset.
Pre-cancelled and empty candidate sets covered.

CI-first: #482 and #483 are accepted; retarget to main and qualify the combined head. No workflow gate is disabled. No workflow gate is disabled.

Next: accept writer/reader/coordinator and rebuild, then R1-B4a verified
owned-memory/direct append comparisons. Start R1-C crash/durability probes
independently; trusted root recovery must never guess from observed packs.
