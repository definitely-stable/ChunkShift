# PATCH-TREE-001 / F2-P2 — original calibration physical counterfactual

Owner: issue #184, research freeze #251. Follows original F1 evidence #365, F2-P0 #366 and F2-P1 #369.

**Status:** CALIBRATION ONLY; NO_ADOPT; original F2 benchmark NOT RUN when this file was added. This document preregisters the protocol before any F2 original-data dispatch.

## The causal test

Original four-pair T0 warm physical: 14,758,982 bytes. Original naive T1 warm physical: 83,593,822 bytes. F1 located +61,981,659 stored-zstd bytes on 981 matched-availability paths with identical emitted chunk triples. A smaller *subset of T1's already tested candidate starts* cannot improve its exhaustive minimum; F2 must actually test different original whole-tree record starts aligned to the matching old-file-relative offset.

All T1 controls must use EXACTLY the same verified old-tree CSM/virtual source, target CSM and target file:

- T0 public: original same-path old-file CSM for 1049 changed, raw full bytes for 2 added paths.
- T1 public: current default H2 create over original virtual-tree BASE.
- T1 H0 sequential: byte-for-byte SHA-256 parity with same-build T1 public.
- T1 relative-local: sequential exhaustive F2 research-only candidate starts near old matching-file offset, up to 8 candidates in 256 KiB; every up-to-four base record chunk must be fully contained within that old file, and each candidate must obey the frozen 1 MiB total dictionary-byte cap before occupying a search slot.
- T1 no-dictionary: same BASE with zstd/raw and zero dictionary candidates.

Changing old-base ManifestId, modifying CSP v1, mixing standalone file CSM with the virtual tree, changing target chunks, using a holdout, or manufacturing new F1 candidate prices is forbidden.

## Original source and population lock

Source only original first-attempt GitHub-hosted recovery run #38136336025; exact pairs.json SHA256: 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd. Only dotnet-aspnetcore-win-x64 and dotnet-runtime-linux-arm64, two frozen transitions each, in frozen order. Full original changed/added/removed/unchanged paths and independently verified file content SHA/length are required; do not materialize evaluation or holdout.

F2 is an opt-in extension to the existing source-locked patch-lab tree-calibration path, with paired --f2-dir and --f2-output flags. F1 and F2 capture may not be combined. Unadorned calibration must be unchanged.

## Source-backed physical accounting

For every file, build actual CSP v1, parse its CRC and physical PAYL/PIDX/stored/raw/zstd sections using the existing F0 inspector, apply via the real public API and hash the complete reconstructed output against its independent target SHA-256. All target ManifestIds and all whole-tree BASE ManifestIds must be equal across matching lanes. Same-run public T0/T1 size and CSP digest must equal the original baseline generated in calibration.json.

F2 evidence must include **exactly 1051 sidecars** (1049 changed and 2 added), named SHA256 of the UTF-8 null-separated full family/base/target/path. Each changed path has 5 physical lanes; each added path has 4 T1 lanes and a raw T0 transport control. Every observation is deleted raw CSP/staging plus a JSON of checksum/size/candidate variant/timing; no raw inputs or reconstructed bytes in artifacts.

Output: original-pairs.json; layouts.json; calibration.json; f2-summary.json; f2-observations/; provenance.json; F2-SHA256SUMS and root SHA256SUMS. Both the .NET evidence gate and independent Python replay must pass all full counts, hashes, exact byte sums and original warm totals. The Python verifier validates structure, not zstd frame bytes independently; the CSPs are deliberately transient and only the .NET producer validates their semantics.

The experiment does not have a separately qualified external native RSS sampler or controlled CPU benchmark. Stopwatch times are diagnostic only: no CPU/memory/throughput ADOPT claims may follow from this slice.

## Operator restriction

Manual workflow_dispatch from latest authorized main SHA only; original reference pairs run #38136336025, GitHub-hosted Ubuntu 24.04 x64, first attempt, one-success-only and owner-authorized source SHA matching the checked out GITHUB_SHA. Separate work cache for checksum-locked calibration releases; workflow timeout 350 min. Code/verification/protocol must merge and final-head CI pass before any run. A timeout, OOM, missing file, provenance failure or interrupted run is UNVERIFIED_NOT_DECISION and does not entitle retries against new populations.

## Frozen outcome interpretation

- F2 locality physical warm at or above 83,593,822 B: NO_BENEFIT_CALIBRATION_ONLY.
- Lower than T1 yet above 14,758,982 B: IMPROVED_T1_BUT_NOT_COMPETITIVE_WITH_T0, NO_ADOPT.
- At or below T0: PROMISING_CALIBRATION_ONLY, not ADOPT; separately preregister per-pair checks, native RSS/CPU/I/O and untouched sealed evaluation before any product decision.
- Always report all four pair deltas, outliers, zero-dictionary fallback, added raw controls, content identity and end-to-end reconstruction. Never cherry-pick 981 matched paths into a product decision.

Issue #184 and research freeze #251 remain open. No public API/defaults/format/tree publication changes are admitted.

## Integration verification after F2-P1

F2-P1 physical foundation PR #369 merged to main as
374c7e8faf42dfaee948ad7ba8513fc6bb80de6c with all its final-head
GitHub-hosted CI, Benchmark lab, native Heavy validation, ASP.NET Core host
validation and specialized physical create/apply contracts successful.
This F2-P2 PR must be tested on GitHub's **current main merge ref** after
that integration, not merely against its earlier stacked research branch.
The baseline publication of F2-P1 is research-only and does not authorize
the real calibration's dispatch, sealed holdout or ADOPT.

The manual real-calibration workflow additionally pins its only admissible
original-pairs recovery artifact to run 38136336025, not just to an equivalent
pairs SHA256. This preserves independently audited provenance rather than
accepting an arbitrary later regeneration.
