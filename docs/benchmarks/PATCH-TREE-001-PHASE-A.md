# PATCH-TREE-001 — tree-source research, Phase A (foundation)

Status: IMPLEMENTED FOUNDATION / NO DECISION RUN. Owner: [#184](https://github.com/definitely-stable/ChunkShift/issues/184); program gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251). Parent [#7](https://github.com/definitely-stable/ChunkShift/issues/7). This work is independent of the unfinished Zucchini reference under #183.

## Question and format boundary

Can the exact, verified *old tree*, rather than only the old file at the same path, supply cross-file reuse for per-file or tree-wide CSP patches at bounded apply cost?

No CSP v1, CSM v1, FastCDC ProfileId, default encoder policy, public SDK/API or publication format is changed. An experimental layout record is NOT a CSM, not a BASE ManifestId, not an update-set wire contract, and not a signed tree manifest. Proof of file-content identity is not proof of safe publication. The canonical path rules here apply to an explicitly restricted research cohort; they do not claim universal Windows/Linux portability.

## Fixed population and comparator denominators

Reuse the pinned materialized PATCH-PREFREEZE corpus with pairs.json SHA-256:

    8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd

Use unchanged family splits from patch-corpus.json: calibration (.NET ASP.NET Core Windows x64 and .NET Runtime Linux ARM64); fixed evaluation (Node Windows/Linux x64, tzdata and ChunkShift source). Construct layouts for *all* regular files of each old/new version, not only the 1,893 changed files. Original materializer strips symlinks and does not preserve every mode/empty directory; the source cohort is therefore a restricted regular-file update set. Record this limitation rather than claiming full production tree publication fidelity.

The versioned input pair includes unchanged, changed, added and removed paths. The whole update's logical goal is the complete new tree; physical update accounting must include added files, removals, path metadata and any required tree/CSM artifacts. Never compare a T2 whole-tree patch against only the changed-file portion of T0 without reconciling coverage. Same-path unchanged files contribute zero patch bytes in T0 but may require explicit tree metadata; T1/T2 may exploit their base bytes only at explicitly costed startup/index expense.

## Phase A implementation — executable and independently testable

- benchmarks/scripts/patch_tree_001_inventory.py builds an exact layout over all materialized regular-file bytes in **NFC UTF-8 encoded path-byte order**, independently of filesystem enumeration, modification timestamps and inode numbers.
- Each entry: relative path, virtual Int64 byte offset, full byte length and SHA-256. Empty files have zero length and never introduce phantom separator bytes. Layout identity hashes length-prefixed UTF-8 paths, unsigned 64-bit lengths and 32-byte content hashes in sorted order. layoutSha256 is a diagnostic research identity only; it is **not** ManifestId or a substitute for per-chunk ChunkIds.
- Reject symlinks/special files, ambiguous paths, non-NFC, case-insensitive collisions, unsafe components, unexpected structures, modification during hashing and Int64 overflow. Fail on changed corpus SHA-256 or mismatch against the frozen per-path changed/added/removed/identical classification.
- read_at(tree, layout, virtualOffset, length) reads across file boundaries with a hard 1 MiB per-call maximum; it does not create an intermediate concatenated file, and it does not cache an entire tree. It is a Python lab oracle, **not** a production .NET Stream/RandomAccess implementation. The later CSP integration must independently verify source content and reference identities.
- Deterministic tests cover UTF-8 ordering, empty files and boundaries, exact pair classification, negative corpus lock, out-of-bounds, casefold/path safety, symlinks, missing source, and layout content/path sensitivity. A GitHub-hosted Linux/Windows contract workflow runs the tests.
- CLI: python3 benchmarks/scripts/patch_tree_001_inventory.py --root <materialized-patch-corpus> --output <layout.json>. The committed lock is mandatory. Output is INVENTORY_ONLY_NOT_PATCH_EVIDENCE; synthetic tests must never be published as patch compression results.

## Subsequent execution stages (NOT implemented by Phase A)

1. **A1: source semantics.** Verify the frozen CSP base locator's first-occurrence/ChunkId behavior over a logical whole-tree stream and enforce the actual operational maximum of 4,194,304 distinct base ChunkIds, per-target chunk count, memory bounds and safe offset arithmetic. Define a bounded seekable concatenating stream for .NET; no full-tree materialization in apply. Record reference-read bytes, seeks and peak RSS.
2. **A2: artifact construction.** T0 = per-file CSP for changed files plus full payload for added files and removals/tree metadata. T1 = one old-tree virtual CSM/locator, then per-file target CSP from this exact old-tree base; every T1 patch BASE names the actual CSM ManifestId of the concatenated old byte stream, never the layout SHA. T2 = one whole-new-tree CSP against whole-old-tree CSM plus exact target path-to-offset/length map. T3 = deterministic bounded rename/similarity-selection reference; H5 resembles a rejected upstream candidate-selection family and cannot be silently revived/retuned here.
3. **A3: controls and full accounting.** Exactly account for CSP physical bytes, tree manifest + directory metadata, added and removed paths, base/target CSM artifacts when distributed, all sidecar/location tables and request count. Report amortized repeated-update cost separately from cold one-update cost; no free preexisting base index. Compare identical semantic populations and verify the complete reconstructed new tree by path, length and SHA-256. Then measure create/apply wall, CPU, peak RSS, base bytes/seeks and temporary disk. Also measure no-rename/rename, split/merge and identical duplicates as synthetic **diagnostics only**; do not contaminate the frozen evaluation denominator.
4. **A4: preregistered calibrated decision.** Before any frozen held-out byte run, pin lane policies, candidate budgets, tree order, source mapping, cost schema, split fingerprints, stopping gates, negative tests and reconstruction oracle in a reviewed protocol. Evaluate all retained lanes once on the unchanged holdout without parameter retuning. A >=10% complete-update physical-byte reduction versus T0 **or** a real renamed-whole-file failure-class fix within the bounded apply envelope may justify an update-set RFC; success is not automatic adoption. If the 4,194,304 locator cap blocks a tree, mark it INELIGIBLE and separately investigate segmented locators rather than quietly truncating it.

## Required safety and product conclusions

Do not equate a single tree-wide CSP output with atomic publication. A future RFC must cover symlinks, modes, empty dirs, delete operations, path traversal/case collisions, staging/journal and crash recovery, signing/trust policy (#185), and version/rollback behavior. Do not publish a new CLI/PublicAPI (#140/#251) until these choices have decision-grade evidence.

Phase A acceptance is only: deterministic exact layout and virtual-read mechanics on synthetic fixtures, frozen corpus validation **when the materialized real corpus is actually available**, and GitHub-hosted contract CI. This is not a passed T0–T3 ratio test.


## A1 integration seam — lab .NET source (no frozen size study)

The research-only \`VerifiedTreeStream\` in \`benchmarks/ChunkShift.Benchmarks/PatchLab/Tree/\` accepts a pinned layout with relative paths, absolute virtual offsets, full lengths and SHA-256. It verifies all source bytes without buffering the tree, rejects gaps/overlaps, bad hashes, unsupported links and non-canonical ordering; after verification it exposes a seekable read-only \`.NET Stream\`. The source retains at most one source-file handle at a time; the in-memory path/offset index is O(file count). Input trees must not mutate concurrently: this first lab implementation does not provide descriptor-pinned/snapshot isolation against adversarial file-system races.

The test \`PatchTreeA1Tests\` uses the **real production SDKs** only as consumers: \`ChunkManifest.CreateAsync\` over the virtual stream and over independently concatenated bytes must return identical ManifestId/physical CSM, and \`ChunkManifest.VerifyAsync\` must validate the result. A synthetic renamed-file example then invokes real \`ChunkPatch.CreateAsync\` and \`ChunkPatch.ApplyAsync\` with a whole-old-tree BASE manifest, verifies its expected ManifestId and exact target bytes, and compares against a self-contained no-old-same-path control. Results are diagnostic fixtures, not the fixed corpus.

The explicit \`MaximumBaseRecords = 4,194,304\` guard follows \`CspPatchBuilder.MaximumBaseRecords\`. The later decision benchmark must count **CSM records**, not merely unique IDs, and separately note the \`ChunkPatch.PlanAsync\` distinct-ID bound. No input may silently split, truncate or sample the base to fit. CSM chunk boundaries can span two adjacent files because the whole tree is one byte stream: therefore cross-file reuse is not mathematically equivalent to treating every old file as a separate chunked source.

This A1 seam is intentionally inside the non-shipping Benchmark lab and grants no standalone update-set artifact. Required before T1/T2 adoption: source snapshot stability, complete tree manifest/path/file-mode/symlink semantics, bounded production locator and apply memory, deterministic update-set construction, full-update physical cost accounting and exact multi-file publication semantics under #185. A1 successful tests alone are NOT decision-grade size evidence.
