# PATCH-TREE-001 / T3-A — bounded exact-content cross-path reference foundation

Status: PREREGISTERED LAB FOUNDATION / SYNTHETIC CONTRACTS ONLY / NO_ADOPT.
Owner: #184, research freeze #251; prerequisite [T4-C3A](../research/results/PATCH-TREE-001-T4C3-PUBLISHER-BOUNDARY-DECISION-20261022-001.md).

## Question and scope

Can an added file in the new tree be reconstructed from an exact (length, SHA-256) matching regular file at another old-tree path, without modifying frozen CSP v1? Is the encoded cross-path reference strictly cheaper than sending the full new file? This T3-A step does NOT test rename-with-changes; that is separately gated as T3-B.

This study is synthetic-only, no frozen real-corpus calibration or sealed holdout, no shipping update-set format, no public API, no publisher and no security ADOPT. Matching layout hashes alone are not authorization or proof of current bytes; fixture reconstruction independently hashes and verifies the old source bytes and full new tree.

## Predeclared algorithm

Use the existing patch_tree_001_inventory.py canonical NFC UTF-8 sorted regular-file layout. Independently recompute layout digest from per-file path, exact length and SHA-256 plus contiguous virtual offsets. Reject malformed schema, noncanonical order, unsafe paths, casefold collisions, invalid hashes, offset overflow. Caps: 100,000 files per side; 4,096 UTF-8 bytes per path; 16 MiB diagnostic map. No truncation on overflow.

1. Preserve same-path entries for T0 (whether unchanged or changed). Only consider target paths absent from the old tree.
2. Build exact lookup by (length,SHA-256) across all old files. The old candidate may also remain at its original path in the new tree; this is cross-path duplication, not necessarily a rename.
3. Deterministic tie-break: shortest UTF-8 encoded old source path, then smallest UTF-8 byte path. Repeated references to one source are permitted.
4. The research-only cost ledger is binary: five bytes PT3A NUL, uint32 mapping count, and canonical target-ordered records. A record is uint32 target path length, target UTF-8 bytes, uint32 source path length, source UTF-8 bytes, uint64 file length and 32 SHA-256 bytes (little endian integers). This ledger is NOT a proposed persisted update-set format.
5. Only admit an exact match when its source reference record is strictly smaller than the target's raw content; only emit a nonempty map if saved raw bytes exceed the FULL map length including nine-byte header. Otherwise use raw added target content at zero map cost.
6. Record actual map length and SHA-256, mapped count, added count, T0 raw added bytes, T3 raw added bytes and delta: saved raw minus map physical bytes. Never call this complete update cost: existing T0 changed-file CSP, tree metadata and publication overhead are not measured in T3-A.
7. Exact mapping must be recomputed before fixture materialization. Old-source file bytes are copied via bounded 1-MiB blocks while SHA-256, length, inode/file identity and modification metadata are checked. Changed/unmapped target files are copied from the new fixture as RAW controls, not from CSP. Independently recompute the entire staged layout and require exact equality.

## Negative matrix / acceptance

Two GH-hosted platforms: ubuntu-24.04 and windows-2025, Python 3.11 standard-library only. Tests must include: renamed exact large file, duplicate source and targets, stable tie break and creation-order invariance, same-path changed and changed-rename exclusions, tiny/empty controls, independent binary map decode, physical accounting conservation, corrupted layout/hash/path/offset, changed source bytes, synthetic source symlink substitution, tampered mapping, existing stage, sidecar cap, deterministic JSON replay. Synthetic fixtures only; no downloaded corpus, real patch, performance claim or OS atomicity.

Accepted result: T3-A SYNTHETIC_CONTRACT_PASS / NO_PRODUCTION_ADOPT only after same-commit Linux and Windows jobs and repository CI. All CSP v1, CSM v1, PublicAPI and src/ remain unchanged.

## Next: decision-grade T3-B

Before any frozen holdout, preregister and execute real calibration against source-locked original inventories and physical T0: evaluate exact cross-path population and actual bytes saved, count common tree metadata and all maps/indexes, independently confirm complete tree, then assess bounded changed-and-renamed candidate heads. Any changed-file base from another path must use real ChunkPatch create/apply, exact base/target CSM, measured source I/O, apply time/memory and complete physical accounting. Where no material eligible population exists, record negative evidence instead of claiming synthetic gains. T2 and full #184/#251 remain OPEN, and T4-C3A remains the external host-owned publication boundary.
