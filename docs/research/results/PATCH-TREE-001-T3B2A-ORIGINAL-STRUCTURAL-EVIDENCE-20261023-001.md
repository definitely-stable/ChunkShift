# PATCH-TREE-001 / T3-B2A — original structural rename-source preselection

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261023-T3B2A-001`  
**Status:** **T3B2A_ORIGINAL_PRESELECTION_PASS / NO_PRODUCTION_ADOPT**.  
**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184); parent [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Date:** 2026-10-23.

## Research question and frozen policy

B1's [two-file original CSP size result](PATCH-TREE-001-T3B1-ORIGINAL-PHYSICAL-EVIDENCE-20261023-001.md) demonstrated that the unchanged existing CSP v1 creates much smaller base-bound patches **when the old source is supplied by an external oracle**. B2A asks whether a simple conservative structural source rule can discover those source files **without hardcoding these two mscordaccore version-named paths or using resemblance ranking**.

[Preregistered B2A protocol](../../benchmarks/PATCH-TREE-001-T3B2A-STRUCTURAL-RENAME-PROTOCOL.md) fixed the algorithm, size and pool caps, ambiguity fallback and original four-pair calibration before the original run. Merged source [PR #403](https://github.com/definitely-stable/ChunkShift/pull/403), main SHA **`1282fd6f1d01384f2858acf1192ab0f3600ec4f8`**.

**Selector:** only removed-old files versus newly added target DLLs in exactly matching relative parents, opaque stable ASCII filename prefix + underscore + monotonically increasing 4-part numeric version, complete differing SHA-256, lengths 64 KiB..64 MiB and ratio no worse than 2:1. A source name pool above **8** fails closed; multiple sources for one target or one source claimed by multiple targets fail closed. No H5/sketch, no chunk content comparison, no alternative CSP format, no public SDK/API or publisher.

## Source lock and original hosted run

Original first-attempt [GitHub-hosted run #39977481690](https://github.com/definitely-stable/ChunkShift/actions/runs/39977481690) **SUCCESS**, GitHub-hosted Ubuntu 24.04 Linux/X64, first main-branch push, run attempt 1. Original `pairs.json` SHA-256 **`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`** recovered only from original independent [pairs run #38136336025](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025). Release assets were re-materialized under their original SHA locks, then all original source/target regular-file bytes rehashed and original pair identities and exact file classifications checked using existing T3-B0 locks.

Independent prior-positive **B1 control** came from [run #39967460136](https://github.com/definitely-stable/ChunkShift/actions/runs/39967460136), separately checksum verified, and was used only **after** B2A emitted its own candidates. No candidate was supplied to the B2A algorithm from this B1 control.

Original GitHub artifact **#12511608541**: `patch-tree-t3b2a-original-structural-NO-ADOPT`; physical ZIP **298,401 bytes**, SHA-256 **`7270b0425f923204c7f9021d6e9df1194d32fc36a94f64607ce7ecfefdc3971b`**, exactly matching GitHub's artifact digest. All six ZIP entries pass ZIP CRC, and all five manifest SHA-256 entries match the independent extracted bytes:

| Archived file | SHA-256 |
| --- | --- |
| `original-pairs.json` | `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` |
| `original-b1-selection.json` | `2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad` |
| `layouts.json` | `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a` |
| `t3b2a-selected.json` | `1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b` |
| `provenance.json` | `080f0a53f5be82151b59d649717bfdaa234fab1a8389df5d16bf3b11fcedc926` |

## Original four-pair result

| Original calibration | Added | Removed | Structural source candidates | Ambiguous/unmatched added targets |
| --- | ---: | ---: | ---: | ---: |
| ASP.NET Core win-x64 10.0.10 → 10.0.11 | 1 | 1 | **1** | 0 |
| ASP.NET Core win-x64 10.0.11 → 10.0.12 | 1 | 1 | **1** | 0 |
| .NET Runtime linux-arm64 10.0.10 → 10.0.11 | 0 | 0 | 0 | 0 |
| .NET Runtime linux-arm64 10.0.11 → 10.0.12 | 0 | 0 | 0 | 0 |
| **Total** | **2** | **2** | **2** | **0** |

Both selected `sourcePath`, `targetPath`, lengths and complete SHA-256 values **exactly equal** the two original B1 preselected CSP source/target identities. This positive result was independently confirmed from the retained original archive (full ZIP/member hashes and SHA256SUMS; all **eight base/target layout SHA-256** values independently recomputed from canonical UTF-8 path, LE size and complete file digest entries).

The **590 bytes** figure (`diagnosticLocatorFloorBytes`) is the charged binary B1 path/reference *floor* for those two matches, 295 bytes per reference. It does **not** include an authenticated update-set index, source-access policy, source CSM cache/update, on-disk namespace proof, locator lookup cost, CPU/RSS or publication. Do not relabel it as a new physical CSP or network benchmark.

A standalone independent archive replay is [`recompute.py`](data/PATCH-TREE-001-T3B2A-EVIDENCE-20261023-001/recompute.py). It verifies original artifact and per-member SHA, reconstructs the canonical layouts, applies the structural selection rule independently of the producer, enforces source one-to-one and pool cap, and compares exact previously frozen B1 identities. It cannot rehash original binaries absent from the artifact: the original hosted materializer and T3-B0 verification did so during the source-locked run.

## Interpretation / next allowed gate

1. **ACCEPT B2A only as a constrained original-calibration source-selection lab**. It reproduced both original mscordaccore identities without B1 oracle input; no additional candidate on this calibration set. The 13 synthetic negative tests passed on each of Ubuntu 24.04 and Windows 2025. This is **not** evidence of recall/precision over arbitrary update trees, and no adversarial open namespace tests were attempted.
2. The B1 size findings remain valid for those two files: actual original warm T3 CSP+map **92,671 bytes** versus T0S **1,097,577 bytes**, cold **94,899 bytes**. B2A **created no new CSP**, so it adds no new physical patch-size, create-time, apply-time or memory measurement.
3. **T3-B2B is next**: independently preregister repeated warming and alternating T0S/T3 runs; capture actual per-lane creation CPU time, wall time, allocation/peak private resident memory, source-read amplification and old-file CSM lookup/generation costs. Predeclare source availability, authenticated locator and total update-set fallback policy, then decide whether extra `create` cost is justified by size savings under a budget. B1 showed a material **one-shot** ~4–5× creation-time penalty; it cannot be ignored or interpreted as repeatable CPU evidence.
4. T3-B2A is **NOT** a source authorization/publisher. RFC-0006 host-owned trust boundary remains; `#184` and `#251` stay **OPEN**. No CSP/CSM v1 wire changes, public API, packages or production base-selection defaults.

**Verdict:** `T3B2A_ORIGINAL_FOUR_PAIR_PRESELECTION_PASS / CALIBRATION_ONLY / NO_PRODUCTION_ADOPT`.
