# PATCH-TREE-001 / T2-A1 — independently audited original full-tree CSM capacity

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261025-T2A1-001`  
**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Date:** 2026-10-25  
**Verdict:** **T2A1_ORIGINAL_CSM_CAPACITY_PASS / NO_ORIGINAL_T2_CSP_OR_SIZE_VERDICT / NO_PRODUCTION_ADOPT**.

## Source of authority and reproducibility

- T2-A1 implementation and frozen original protocol [PR #415](https://github.com/definitely-stable/ChunkShift/pull/415) squash MERGED at `main@fd50b79d85e702534ab6addd84880d612b1452de`, after specialist Linux/Windows tests, full CI, CodeQL, Benchmark and Heavy validation PASS.
- Original first main run [#40203715355](https://github.com/definitely-stable/ChunkShift/actions/runs/40203715355) **FAIL / UNVERIFIED_NOT_DECISION**. Its first-Actions-source lock failed before materializing the original source corpus or creating any CSM: the workflow manually repeated a **63-character** instead of 64-character `pairs.json` SHA, which `sha256sum` rejected. No original observations are accepted from this run.
- Exact provenance-only fix [PR #416](https://github.com/definitely-stable/ChunkShift/pull/416) MERGED `main@555c992f30473c373f1efd0d6d8b3d18bf87a090`. Authoritative expected SHA now comes directly from `docs/benchmarks/patch-corpus/corpus-lock.json` with a strict 64-hex test; existing archive SHA256SUMS and Actions provenance checks are retained. No code/encoder/budget changes.
- **Successful original first-attempt `main` run [#40204580308](https://github.com/definitely-stable/ChunkShift/actions/runs/40204580308)** on GitHub-hosted Linux x64, run attempt 1, source `555c992f30473c373f1efd0d6d8b3d18bf87a090`. Origin workflow validates original pairs run #38136336025 and complete physical T0/T1 calibration #38142160679 before materialization and creates **only physical old/new CSM v1 metadata** — no CSP, no held-out dataset.
- Original artifact **#12603112300**, `patch-tree-t2a1-original-CSM-only-NO-ADOPT`, ZIP SHA-256 **`9e5556b11277e33c84d186834ca68da6866d3bc60e77fceef92c78fb7b077fe9`**. ZIP CRC and all five SHA256SUMS members independently verified. Exact member digests: original pairs `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`; layouts `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a`; original prior T0 calibration `d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c`; T2A1 preflight `823820571d3534b302e2acb8f0501ac4dbe4c6030093e6e27b3a066945d65fb6`; provenance `3374d0689b8efe8119fbc6f32c20227516d3c0ce6844ccb6ab419d99f92ea960`.
- [Full four-row machine record](data/PATCH-TREE-001-T2A1-EVIDENCE-20261025-001/census.tsv) retains exact pinned layouts, ManifestIds, CSM physical SHA, record/unique-ID counts, original T0 physical denominator and path classes. [Standalone stdlib replay](data/PATCH-TREE-001-T2A1-EVIDENCE-20261025-001/recompute.py) tests original ZIP/provenance/member hashes, prior baseline/layout, 4-row field parity, all source bounds, complete file counts and adjacent-version `Target -> Base` identity. Its scope is *independent archived-observation auditing*: it cannot independently reconstruct unarchived original CSM chunk IDs from nothing. The original producer uses actual `ChunkManifest.VerifyAsync` and `ManifestReader` on the rehashed original source to enumerate IDs and validate all CSM content.

## Actual original 4-pair capacity census

| Version transition | Old/new logical MiB (approx.) | Full old/new CSM records | Distinct old/new ChunkIds | Newly missing distinct target IDs | Actual old/new CSM physical bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| aspnetcore win-x64 10.0.10→10.0.11 | 105.19 / 105.20 | **1,266 / 1,262** | 1,248 / 1,244 | **702** | **45,970 / 45,826** |
| aspnetcore win-x64 10.0.11→10.0.12 | 105.20 / 105.38 | **1,262 / 1,267** | 1,244 / 1,250 | **530** | **45,826 / 46,006** |
| runtime Linux ARM64 10.0.10→10.0.11 | 84.69 / 84.66 | **1,058 / 1,045** | 1,058 / 1,045 | **543** | **38,482 / 38,014** |
| runtime Linux ARM64 10.0.11→10.0.12 | 84.66 / 84.67 | **1,045 / 1,057** | 1,045 / 1,057 | **385** | **38,014 / 38,446** |

- Original CSM identity **PASS**: first-pair target CSM layout/ManifestId/physical SHA equals the next pair's base CSM for both product families, while rebuilt base CSM IDs/physical bytes match the older T0/T1 original calibration independently.
- Original source path counts, including changed/added/removed/unchanged, and physical **complete T0 warm denominator 14,758,982 B** match the previous actual baseline.
- **All four original pairs meet the preregistered A1 input/CSM lab limits:** ≤160 MiB logical bytes per tree, ≤1,024 files per tree, ≤262,144 CSM records, ≤8 MiB physical CSM. Recorded actual values are far below the operational `4,194,304` CSM record / distinct-ID boundary, but actual receiver materialization and memory bounds have NOT been tested here.
- Four planning charges `48 * oldRecords + 160 * oldUniqueIds + oldLayoutBytes` are **326,421 / 325,589 / 256,277 / 253,573 B**. These are bookkeeping estimates for an unspecified reference representation, **not observed peak memory, proven allocator bounds, current SDK allocation costs, physical publisher locators or apply overhead**.
- `missing distinct ChunkIds` counts **702 / 530 / 543 / 385** are *not* CSP payload counts, patch bytes, transfer sizes, or a proof of cross-file reuse benefit. No byte-optimal T2 CSP has been created.

## Decision and strict next gate

**Accept only T2-A1 original CSM source/ID feasibility.** Retain T3 first-release DEFER and the shipping T0 per-file patch API. Keep #184 and #251 OPEN. No signed target tree manifest, portable links/modes, source authorization, atomic host-owned publisher, first-party updater, public API, CSP/CSM wire-format/default or product release change.

A subsequent **T2-A2 source-locked actual CSP feasibility/pilot** requires a new frozen protocol before execution: bound the full-tree exhaustive zstd candidate search/create CPU and deadline on hosted runner, cap and measure peak private-resident apply memory and verified per-chunk distinct-ID locator, charge actual physical T2 CSP + canonical target map + old CSM/locator + framing and compare against the *same fully accounted* T0 update across all files. Apply and independently verify each reconstructed target path, including zero-length and deletions. Track requests/artifacts, create/apply CPU+wall, throughput, and failure/timeout controls, and keep publisher trust per RFC-0006/T4-C3A. Pilot only allowed **preselected original calibration**; no opening of sealed Node/tzdata/ChunkShift holdout. If CPU or apply resource limits cannot be demonstrated, stop T2 without changing the release gate.

**Disposition:** `T2A1_ORIGINAL_CSM_CAPACITY_PASS / NO_T2_CSP_SIZE_VERDICT / NO_PRODUCTION_ADOPT`.
