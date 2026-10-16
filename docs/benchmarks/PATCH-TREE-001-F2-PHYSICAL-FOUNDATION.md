# PATCH-TREE-001 / F2-P1: physical matched-base locality mechanics

Owner: issue #184 and Research Freeze #251. Based on verified F1 PR #365 and merged F2-P0 PR #366 (main 12aabb83014100ec4a8ab3937268ac718364a4cc).

**Status: SYNTHETIC PHYSICAL FOUNDATION ONLY / NO REAL CALIBRATION / NO_ADOPT.**

## Problem and source constraints

The original four .NET calibration pairs measured T0 warm 14,758,982 bytes and naive virtual-tree T1 warm 83,593,822 bytes. F1 documented a 61,981,659-byte stored-zstd difference on 981 changed files with identical emitted target chunk IDs. Merely filtering candidates that T1 exhaustive selection already tested cannot lower the minimum observed local cost. It is necessary to evaluate *new* candidate starts without changing original BASE identity or CSP v1.

All T1 lanes in this lab use the SAME independently verified whole-old-tree CSM and virtual byte stream. The target CSM/content remains identical. The T0 reference uses a separate independently verified same-path old CSM.

## Explicit physical policies

| Lane | Encoder | Base |
| --- | --- | --- |
| T0-public | public CreateAsync | original matching old file |
| T1-public | public CreateAsync | original old virtual tree |
| T1-H0-parity | internal exhaustive sequential | identical virtual tree; public CSP SHA/length must match |
| T1-relative-local | internal exhaustive sequential + F2-only start selector | identical virtual tree, mapped old-path start plus target-relative offset |
| T1-no-dictionary | internal original zstd/raw without dictionary | identical virtual tree |

The F2 selector scans the eight nearest eligible old-tree CSM record starts within +/-256 KiB of the mapped target-relative anchor, preserving nearest-distance/lower-index ties. Each start and **all up-to-four complete candidate CSM records** must fall within the same old-file half-open virtual interval. Candidates crossing the file boundary are rejected, rather than silently shortened. A candidate's total up-to-four record bytes must also be at most the shipping CSP **1 MiB** raw-dictionary cap before consuming one of the eight candidate slots. This prevents oversized candidates (which the encoder would skip) from starving valid nearby starts. The frozen policy itself is unchanged. Missing same-path old source => empty candidates, then raw/zstd fallback.

All selected dictionary bytes are still hashed and verified against the authoritative CSM ChunkIds by the unmodified existing encode and apply path. Segmentation of the virtual whole-tree CSM can differ from a standalone file CSM; this experiment does not assume or promise equal boundaries.

## Internal-only guard

Before creating any CSP, the F2 physical harness additionally rejects duplicate old-tree source paths, any virtual-layout length that does not exactly equal the virtual content length, a T0 same-path input for a path not in the old-tree inventory, and non-seekable T0 source streams. These are geometry/contract checks; the upstream source-locked materializer must still verify each original file SHA-256 before the later real F2 experiment.\n\nThe new null-default ResearchCandidateStarts delegate on CspCreateExecution is valid only with sequential exhaustive H0 and no candidate cache, no ranked selector and no workers. Results are checked for max eight starts, valid ordinal bounds and duplicates before source reads. The public CreateAsync path never sets it. This change does touch internal source under src/ChunkShift.Patching but does not change public API, production default, CSP or CSM bytes/semantics.

Physical test harness writes actual temporary CSPs, inspects F0 PAYL/PIDX/raw/zstd/other physical byte conservation, applies each to staging, verifies streaming target SHA-256 and destroys CSP/staging files. It verifies H0-public CSP identity, identical virtual BASE identity and identical target ManifestId across lanes. It reports no real production performance result.

## Implementation acceptance (not a product decision)

- .NET 10 GitHub-hosted x64 Release targeted synthetic physical tests, regular CI, Benchmark lab, Heavy validation, CodeQL.
- Synthetic multi-file source case with a displaced target path; every lane exact-apply verified, full CSP physical byte accounting and H0 SHA parity.
- Missing same-path old file must yield raw/zstd fallback, no fabricated cross-file dictionaries.
- Source-file boundary crossing, forged layout, invalid coordinates and callbacks rejected.
- No retained source/CSP bytes in evidence summaries; no source/control changes except internal null-default research seam.
- No adoption, no source-locked real performance claim and no sealed evaluation from this PR.

## Next real F2 experiment: NOT YET AUTHORIZED

Separately preregister an owner-gated GitHub-hosted *calibration-only* run against the original four .NET pairs, using prior authentic recovery run 38136336025 and exact frozen pairs SHA-256 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd. Require all original 1,049 changed and 2 added paths, original T0 added raw control, complete per-file and full warm-update sizes, exact original CSM/source hashes, independently checked F0 accounting, verified create/apply, wall/CPU, external native memory and base read/seek counters. Fix numerical acceptance/kill thresholds and separate later unopened evaluation *before* running any decision data.

Current #184 remains RUNNING, #251 remains OPEN; naive T1 remains NO_GO/NO_ADOPT. No holdout materialization, T2/T3/update-set RFC freeze, production default or public API change authorized.

## Infrastructure validation dependency

The unrelated oversized Content-Length HTTP-413 test failure was isolated and
fixed in PR #371 (main 96b30b1775ebe0afdff6d294c1a52ba31bbbde35).
That fix lives only in the host-test suite; the F2 internal codec seam does not
change Core ASP.NET integration. Re-run all final-head PR #369 workflows against
the updated main merge base before accepting this foundation.
