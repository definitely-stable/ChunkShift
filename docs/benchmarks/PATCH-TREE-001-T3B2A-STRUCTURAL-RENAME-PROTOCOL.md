# PATCH-TREE-001 / T3-B2A — deterministic structural rename preselection

**PRE-REGISTERED / NO_PRODUCTION_ADOPT.** Owner #184; release gate #251. Original prior B1: [actual two-DLL CSP evidence](../research/results/PATCH-TREE-001-T3B1-ORIGINAL-PHYSICAL-EVIDENCE-20261023-001.md). This slice addresses **candidate identification**, not CSP codec, optimizations, shipping update-set format or source authority.

## Problem / exclusion of leaked oracle

T3-B1 demonstrated strong physical CSP savings *after someone supplied two known old DLL files*. It did not solve safe deterministic source selection. A mechanism that simply hardcodes those original two pairs, uses holdout filenames, or silently chooses among multiple matching candidates would not be evidence for automatic selection.

Freeze **one conservative low-recall policy**, before original source material is touched by this slice. Every pair is inspected in full: four previously locked .NET calibration transitions, two families, not just the two B1-positive files. Original first-attempt pairs.json SHA256 **8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd** and release asset checksum list unchanged; no held-out trees, no H5 sketch search or candidate ranking.

## Fixed policy and complexity

1. Canonical layout validation via the T3-A independent verifier, plus original `verify_pair` exact classification. Maximum 100,000 files per tree and 4096 UTF-8 bytes per path, inherited from T3-A. Inputs are regular files with exact length/SHA256; source real-file rehash is separately required on hosted calibration via T3-B0 `input_lock`.
2. Build an old-source lookup **only over deleted paths**. A target is considered **only if newly added**. Same-path changes belong to T0; retained-path duplicates belong to exact reuse and are not B2-A rename candidates.
3. Narrow filename grammar, on the final component only: nonempty ASCII alphanumeric prefix with up to 127 ASCII alphanumeric/dot/underscore/plus/hyphen characters, ending in literal underscore, then **four nonnegative dot-delimited decimal numbers**, then literal lowercase `.dll`. Numeric parts must be at most 2,147,483,647 each. Preserve original relative directory and prefix byte-for-byte. No casefold matching, no fuzzy basename or hashes-as-similarity.
4. Require matching parent directory and opaque prefix, newer numeric version tuple strictly above older, both files 64 KiB..64 MiB, max size at most **2×** min size, and different full SHA-256. The ratio is a bounded *eligibility guard*, not proof of similarity; actual CSP may still be larger than T0S.
5. Cap every same-directory/prefix source pool at **8 deleted old files**. With 9 or more source names return `source_pool_limit` without scanning candidates; never sort the pool. Reject a target if no source, or **two or more eligible deleted sources**. If one source would be selected by **two or more** added targets, reject *all* those target associations. No lexicographic arbitrary tie-break. Output fixed UTF-8 byte-sorted target order, exact source/target hash, lengths and versions with a fallback reason for *every* unmatched target.
6. Report `diagnosticLocatorFloorBytes = 5+4+4+UTF8(target)+4+UTF8(source)+16+96` per selected row, equal to B1's diagnostic binary path/reference record with 32-byte BASE ManifestId. This **is not** full source index, authentication, lookup CPU, old CSM generation, reference staging, publisher, physical update-set map, or promised network cost. Never emit a positive patch-size verdict from this number.
7. Algorithmic selection time O(old files + new files + 8 × added targets) with bounded input count, no content similarity scan and no patch creation. Input tree layout/namespace must be externally protected and verified at actual use; this metadata candidate is not permission to open an arbitrary old file.

## Tests and main-only source-locked qualification

Synthetic cross-platform tests cover: unique versioned changed rename, no naming match, same-path T0 exclusion, retained old file exclusion, two-source ambiguity, two-target competition, older version monotonicity, parent directory matching, identical bytes exclusion, target size and 2× ratio bounds, independent multi-pair deterministic ordering, layout/pairs corruption and unsafe path grammar.

First original run after merge: GitHub-hosted Ubuntu 24.04, Python 3.11, verify original run #38136336025 and `SHA256SUMS`, materialize only original four .NET pairs, rehash all real file bytes and canonical source locks through unchanged T3-B0 code. Apply the fixed B2-A preselection across all four pairs. **Acceptance is strictly identification of exactly the two independent previously established B1 old/new DLL pair identities, no other candidates, no ambiguity or unaccounted added target**. Selection equality against the frozen B1 oracle is *an independent cross-check*, not used by the B2-A algorithm. If it fails, stop and record counterexample/negative; do not retune after seeing original data. Save selection JSON with source SHA/provenance and SHA256SUMS; failed experiment artifacts UNVERIFIED.

Green synthetic tests do not prove original population. The original-run result is **T3B2A_PRESELECTION_ORIGINAL_PASS / NO_PRODUCTION_ADOPT** if exact selection agrees with B1 and original input locks hold, not a measured CSP update-set saving or general false-positive rate.

## Next gate, deliberately unimplemented here: B2-B

Separately preregister repeatable create/apply comparisons with deterministic warmup and alternating lane order, repeated samples, process CPU time, peak private memory/RSS, source-read bytes and lookup/CSM-generation overhead, and full physical transport admission against **both** raw T0 and measured self-contained T0S. Require fail-closed fallback on ambiguity, cost exhaustion or absent old base. No new source selection policy or production API acceptance follows from B2-A; #184 and #251 remain OPEN.
