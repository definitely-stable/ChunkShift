# PATCH-TREE-001 — T0/T1 calibration-only construction, predecision protocol

Status: **IMPLEMENTATION / NOT FROZEN DECISION**. Owner [#184](https://github.com/definitely-stable/ChunkShift/issues/184); program gate #251. Base implementation [PR #274](https://github.com/definitely-stable/ChunkShift/pull/274) at merge \`573d27b6619d9a3960e961880296e87ce40a44fb\`. This document permits calibration study **only**; it does not authorize any holdout or update-set RFC promotion.

## Locked semantic population

Inputs are the existing exact materialized PATCH-PREFREEZE source trees, \`patch-corpus.json\`, and original \`pairs.json\` with SHA-256 \`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd\`.

Do not modify any source archive/version, profile/hash suite, normalization, path order, paired files or calibration/evaluation assignment.

Calibration families:
- \`dotnet-aspnetcore-win-x64\`
- \`dotnet-runtime-linux-arm64\`

Other families are the future sealed holdout and must **not be measured** by this CLI. No parameter search against them. The Python inventory verifies both sides' exact regular-file content and original changed/added/removed/unchanged pair classification. The .NET evaluator checks actual \`pairs.json\` checksum and inventory key/role; it opens independently SHA-256-verified version trees and checks the changed/added/removed path classification again. Files not representable by the corpus materializer (symlinks, modes, empty directories) remain explicitly out of population.

## Compared byte constructions

- **T0**: each changed file receives the ordinary production CSP v1 over its old *same-path file* and its own target CSM. Added files are shipped raw at their exact full byte length; deleted paths ship no payload. Exact complete target-tree regular-file manifest (paths, lengths, SHA-256) is charged to the update. Unchanged same-path bytes are not transmitted.
- **T1**: create one CSM over the exact virtual concatenated whole old tree, sorted by canonical NFC UTF-8 path bytes with no separators, even between files. For **every changed and added target file**, create a production CSP v1 that binds the true shared old-tree \`ManifestId\` through \`BASE\`; unchanged files are unchanged, removed paths are represented by their absence from the full tree manifest. Verify applying every CSP with the same shared old-tree stream, the true old-tree CSM and exact target SHA-256. No CSP v1 modifications.
- **Warm physical update bytes**: \`sum(physical CSP files) + exact target-tree reference manifest bytes\` for T1; for T0 sum(changed CSP bytes + raw added file bytes) + the **identical** reference manifest bytes. No claims about network metadata/encryption/signing until a complete update-set RFC defines them.
- **Cold T1 lower-bound accounting**: warm T1 bytes plus whole-base CSM physical bytes plus UTF-8 JSON base location directory bytes. This is an intentionally conservative distribution model: whether receiver can rebuild these from verified old bytes is an architecture question. Report both warm and cold separately; never hide the locator/CSM in the warm case and call it zero-cost. For T0 the per-file old-manifest creation is builder-side/local work; a full cold receiver setup comparison remains open.
- **Important added-file confound**: raw T0 added-file transmission versus T1 compressed CSP may reflect ordinary compression rather than cross-file reuse. Before decision, include an additional *self-contained-CSP added-file comparator* (T0S) and separately report actually reused \`ChunkId\` bytes. This first slice is mechanics/calibration, not eligibility for >=10% decision.
- Actual create and apply wall times are reported per changed/added file; \`VerifiedTreeStream\` reads/seeks/bytes are measured across create+apply. File-system hashing/preflight, old-tree CSM create and source-snapshot cost are presently not timed as separate stages; **do not make an end-to-end performance claim** from the file timings.
- Record count: the CSP create implementation limits the *whole-old-tree CSM* to **4,194,304 records** (different from \`PlanAsync\`'s distinct-base-ID cap). If exceeded, mark a pair INELIGIBLE and stop instead of silently segmenting, dropping or sampling records.

## Invariants, limits, and integrity

1. Base \`ManifestId\` must equal the real CSM identity produced by Core \`ChunkManifest.CreateAsync\` for the virtual concatenation. No layout digest may stand in for \`ManifestId\`.
2. A full source SHA-256 preflight is required for every old/new regular file. Before each patch, exact source/target identity must still hold; this first implementation assumes an immutable isolated corpus, not a security-grade file-snapshot backend.
3. Every created T0/T1 CSP for a changed file or T1 CSP for an added file must reconstruct the exact target SHA-256 using the production \`ChunkPatch.ApplyAsync\`. Every T0 raw added file is rehashed. Unchanged files are verified during full target-tree inventory opening; no atomic staged tree publication is implemented yet.
4. Metadata cost is exact in the experimental JSON reference encoding and includes every target file; this is **not** a signed canonical production tree manifest. Final physical update policy, file-mode/symlink/empty-dir semantics, atomic publication/journaling and #185 trust are unresolved.
5. Calibration outputs have schema \`chunkshift.patch-tree-t0-t1-calibration.v1\` and status \`CALIBRATION_ONLY_NOT_DECISION\`. They must never be relabeled as an evaluation/holdout or final adoption verdict.
6. Never issue held-out runs until the remaining T0S/T2/T3 scope, accounting, H0 baseline consistency, per-file evidence retention and one-shot workflow/independent verifier are frozen in a subsequent review.

## Reproduction

With the exact corpus in \`$CORPUS\` already materialized by the established frozen recipe:

\`\`\`sh
python3 benchmarks/scripts/patch_tree_001_inventory.py --root "$CORPUS" --output "$RUN_DIR/layouts.json"
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- \
  patch-lab tree-calibration --corpus "$CORPUS" \
  --inventory "$RUN_DIR/layouts.json" --output "$RUN_DIR/t0-t1-calibration.json"
\`\`\`

A \`workflow_dispatch\` hosted Ubuntu x64 calibration lane performs the same source-lock steps with an exact main-branch and single-attempt barrier. Pull request CI executes **only synthetic fixture tests**, not the frozen calibration population or sealed holdout. A successful synthetic contract is not evidence of size savings.

## Release decision

Do not modify shipping \`src/\`, CSP v1, CSM v1, \`CspEncoderPolicy.Default\`, public API, publish pipeline, #140 or #251. T1 acceptance/rejection is impossible from this implementation slice alone.
