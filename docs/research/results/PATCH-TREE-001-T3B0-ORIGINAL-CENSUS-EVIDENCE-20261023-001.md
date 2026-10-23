# PATCH-TREE-001 / T3-B0 — original source-locked .NET exact cross-path census

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261023-001`  
**ExperimentId:** `PATCH-TREE-001`, owner [#184](https://github.com/definitely-stable/ChunkShift/issues/184), freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Date:** 2026-10-23.  
**Disposition:** **EXACT_ADDED_PATH_NO_HEADROOM / REJECT T3-A EXACT REUSE ON ORIGINAL .NET CALIBRATION / NO_PRODUCTION_ADOPT**.  
**Do not generalize** this to changed-and-renamed CSP, T2, other operating systems/corpora, sealed holdout, production update-set representation or the host-owned publisher.

## Frozen test and authoritative run

[Predeclared protocol](../../benchmarks/PATCH-TREE-001-T3B0-EXACT-REAL-CALIBRATION.md); [merged implementation PR #399](https://github.com/definitely-stable/ChunkShift/pull/399), `main@0301d21fa07fe9eec52bd645d73db2f4d55fd880`. The **first main-branch GitHub-hosted run #39915730705** completed **SUCCESS**, `github-hosted` Linux/X64, Ubuntu 24.04 runner, Python 3.11.17, run attempt 1:

https://github.com/definitely-stable/ChunkShift/actions/runs/39915730705

It validated original independent [pairs recovery run #38136336025](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025), before source materialization; retrieved only its original `pairs.json`, not any holdout tree. The `pairs.json` SHA-256 is **`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`** (64 hex digits). The source archive SHA list, materialized pair classification and calibration-only inventory were checked again. The original T0 physical comparator comes from [run #38142160679](https://github.com/definitely-stable/ChunkShift/actions/runs/38142160679) and audited [F1 run #38773779715](https://github.com/definitely-stable/ChunkShift/actions/runs/38773779715). These numbers were **not** produced by new CSP create/apply in T3-B0.

Retained original **artifact #12485021458**, `patch-tree-t3b0-original-calibration-NO-ADOPT`; GitHub artifact ZIP physical **SHA-256 `a1dda5714080c2f3a17a6f0051f86725bc0804267cde1a61b3aadee051c176d9`** (297,812 bytes). Archived member SHA-256:

| Archive member | SHA-256 |
| --- | --- |
| `original-pairs.json` | `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` |
| `layouts.json` | `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a` |
| `t3b0-census.json` | `14a8c652daa65e63bcc6fc786ccfdf9d6ed21ba6d6a6cb44a55d6224f81478d8` |
| `provenance.json` | `b8030a0b00009cfc8de143d8e461ede266222c9b969ba543884ec1c2a3e9fc27` |

## Original real calibration results

All four original pairs; **no sealed holdout data** and **no new CSP artifacts**.

| Pair | Changed | Added | Removed | Unchanged | Raw added bytes | Exact cross-path candidate | Net T3-A savings |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ASP.NET Core win-x64 10.0.10 → 10.0.11 | 334 | 1 | 1 | 4 | 1,356,632 | 0 | 0 B |
| ASP.NET Core win-x64 10.0.11 → 10.0.12 | 335 | 1 | 1 | 3 | 1,356,640 | 0 | 0 B |
| .NET Runtime linux-arm64 10.0.10 → 10.0.11 | 190 | 0 | 0 | 3 | 0 | 0 | 0 B |
| .NET Runtime linux-arm64 10.0.11 → 10.0.12 | 190 | 0 | 0 | 3 | 0 | 0 | 0 B |
| **Total** | **1,049** | **2** | **2** | **13** | **2,713,272** | **0** | **0 B** |

**Complete original T0 warm physical comparator:** 14,758,982 bytes, including 185,436 bytes of common target tree metadata. The modelled T3-A warm substitution is **14,758,982 bytes**, unchanged. `exactCandidateCount=0`, `mappedCount=0`, `mapPhysicalBytes=0`, `mappedContentBytes=0`, `netSavedBytes=0`. The empty map is correctly charged zero bytes.

The **two** new paths occur *only* in ASP.NET Core win-x64. They are successive version-numbered `mscordaccore_amd64_amd64_*.dll` files, each replacing the previous filename, with independently different SHA-256 and, on the second transition, a different length:

- `10.0.1026.32716.dll` → `10.0.1126.37416.dll` (new file 1,356,632 B).
- `10.0.1126.37416.dll` → `10.0.1226.42308.dll` (new file 1,356,640 B).

The above is **name/transition evidence**, not proof of similarity, a known source transform, reusable CDC chunks, a smaller CSP, or any actual changed-rename encoder win.

## Independent post-run audit

The GitHub artifact was downloaded and inspected independently after the hosted run. Its ZIP SHA-256 matches GitHub's published artifact digest, `zipfile.testzip()` passed for **all five members**, and all four `SHA256SUMS` entries match extracted member bytes. The original full pairs SHA, exact four-pair order and original-holdout-exclusion flag were independently checked. A separate recomputation (without importing either `patch_tree_001_inventory.py` or `patch_tree_001_t3_exact.py`) confirmed **each complete tree layout SHA-256** from canonical length-prefixed UTF-8 paths, LE uint64 file sizes and SHA-256 file digests; it independently reconstructed four populations and counted eligible exact old-source matches for each newly added path. Totals exactly matched the hosted producer's JSON.

**Scope of audit:** this independently audits the **archived SHA-locked inventory and result**, not actual source binaries after they leave the hosted runner. The runner's original materializer and second independent source-file SHA walk performed actual source-byte verification there. The archived ZIP contains original path hashes and counts, **not** reconstituted binaries, raw CSPs, CPU profiles or hostile filesystem traces. True power-cut/publisher durability and external cryptographic trust are outside T3-B0.

## Decision and next bounded question

1. **REJECT** promotion of T3-A exact added-path reuse on this original calibration corpus. The result has **zero exact-match opportunities**, not merely insufficient gains after map overhead. Adding a permanent cross-path map for these four transitions would introduce cost/complexity without eliminating any raw added file.
2. Preserve same-path **T0 as the baseline** and **T0S self-contained compressed added-file control** for any subsequent size comparisons. This result supplies **no** positive reason to change CSP v1, tree manifest encoding or publication.
3. The next defensible separate hypothesis is **T3-B1 bounded changed-rename source selection** for the two concrete version-coded `mscordaccore` name transitions. Before any experiments freeze pairing semantics, old-vs-new exact bytes, the T0 raw and T0S compressed baselines, actual old-file CSM and CSP create/apply validation, physical patch plus locator/map overhead, and a strict stop criterion. Do not claim an automatic B1 ADOPT from the current filenames.
4. `#184` and `#251` remain **OPEN**. T2 whole-tree representation and complete production update-set semantics are independently unresolved. This does **not** reopen the rejected T1/F2 experiment or first-party publisher.

**Verdict:** `T3B0_ORIGINAL_FOUR_PAIR_CENSUS_PASS / EXACT_ADDED_PATH_NO_HEADROOM / NO_ADOPT`.
