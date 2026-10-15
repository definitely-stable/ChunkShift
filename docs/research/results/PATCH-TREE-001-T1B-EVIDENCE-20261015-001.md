# PATCH-TREE-001 / T1-B — source-locked boundary attribution (2026-10-15)

**ExperimentId:** `PATCH-TREE-001` · **EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261015-T1B-001`  
**Disposition:** `TECHNICAL_CALIBRATION_PASS / PHYSICAL_CAUSALITY_NOT_PROVEN / NO_ADOPT`.  
**Issue:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184) · **Research Freeze:** [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Implementation:** [PR #352](https://github.com/definitely-stable/ChunkShift/pull/352), squash-merged as `e55fb069f383c5589a66769e8675ce8e3b71df21`; production `src/`, Core/Patching API, CSM/CSP bytes and defaults unchanged.

## Answer first

The real, frozen four-pair .NET calibration **confirms loss of exact FastCDC chunk availability** in the naive T1 whole-old-tree concatenation:

- Standalone old-file chunks represent **398,185,191 bytes / 5,373 occurrences**.
- Only **300,579,261 bytes / 3,709 occurrences (75.4873%)** survive at their original mapped tree offsets; the *same* number is found anywhere within virtual-tree CSM. Thus relocated exact original chunks do not recover additional availability on this cohort.
- The overlapping parts of virtual CSM chunks that cross an old-file boundary total **72,926,463 bytes (18.3147%)**; this is an overlap measure, not distinct patch bytes or transmitted data.
- Across changed **and added** target files, T0 old same-path base offers **187,406,471 exact-match target bytes / 2,384 chunk occurrences**, while T1 virtual-old CSM offers **181,452,822 bytes / 2,306 occurrences**. T1's available exact target bytes decrease by **5,953,649 bytes**, or **3.1769%** of the T0 exact-match pool.
- Among **1,049 changed paths**, 68 have lower exact availability under T1, 981 tie, and **zero** have higher availability. Among two added paths, one has T1 exact availability (682,844 bytes); T0 same-path availability is zero by definition.

However, earlier independently verified *physical* T0/T1 calibration on these same four source pairs was **14,758,982 B T0 warm** versus **83,593,822 B T1 warm** — an increase of **68,834,840 B / 5.6639x** (source: [run #38142160679](https://github.com/definitely-stable/ChunkShift/actions/runs/38142160679)). The 5.95 MB decrease in *potential* exact matches does **not** by itself establish how much of the 68.83 MB physical CSP regression it causes. Chunk availability, encoder candidate selection, dictionary validity/quality and compressed frame costs are different quantities; do not subtract them as if causally commensurate.

**Decision:** preserve the accepted `NO_GO` for the specific frozen naive T1 on the calibration cohort. Do **not** claim a sealed-holdout result, reject all cross-file schemes, retune T1, or change CSP v1/defaults. The next eligible slice is independently frozen **per-file PAYL/frame/dictionary/candidate physical accounting** against the original T0/T1 policies (see below).

## Original GitHub-hosted execution and audit

| Provenance | Exact source |
| --- | --- |
| Hosted execution | [run #38736641373](https://github.com/definitely-stable/ChunkShift/actions/runs/38736641373), first attempt, `SUCCESS` |
| Owner-gated dispatch | [issue #184 command](https://github.com/definitely-stable/ChunkShift/issues/184#issuecomment-6192302240) / [operator run #38736626969](https://github.com/definitely-stable/ChunkShift/actions/runs/38736626969), `SUCCESS` |
| RunId | `PATCH-TREE-001/T1-BOUNDARY-20261015-38736641373` |
| Source commit | `main@e55fb069f383c5589a66769e8675ce8e3b71df21` |
| Runner | GitHub-hosted Ubuntu 24.04 / Linux x64, .NET SDK `10.0.402` |
| Frozen original pairs reference | [run #38136336025](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025), first-attempt `SUCCESS` |
| Original `pairs.json` SHA-256 | `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` |
| Original ZIP | [artifact #11974666607](https://github.com/definitely-stable/ChunkShift/actions/runs/38736641373/artifacts/11974666607), ZIP SHA-256 `2918c70f03bf87e2e3351c52399debfa61c54887a3b9c48b8473c362606a4002` |
| Original JSON | `boundary.json` SHA-256 `926a3a83dae334cbc068bed70c04510007c9b880e60b93535f902d729a830ae3` |
| Original inventory | `layouts.json` SHA-256 `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a` |
| Original provenance | `provenance.json` SHA-256 `a92690c91e7e614f5b2896594e36d2a1e283e2fa5264c86c49025cc07eb12fd6` |

The original ZIP was **downloaded and independently reopened**. Its ZIP CRC is valid; all four member digests match the original internal `SHA256SUMS`; the original `pairs.json` has exactly the SHA-256 above. Independent offline replay checks the unmodified original `boundary.json` against the full `layouts.json` and `original-pairs.json` for:

1. Exactly four calibration pair identities in frozen order; no sealed family.
2. SHA-256 rederivation of all four old and target layout identities from UTF-8 ordered paths, lengths, offsets and per-file SHA-256.
3. Complete changed/added/removed/unchanged membership, exact source/target file size and identity, row order and counts; **1,066** rows (1,049 changed, 2 added, 2 removed, 13 unchanged).
4. Per-row count/byte bounds and old/target coverage conservation, preserved-at-offset implies available-anywhere, crossing overlap count at most two per old file, and changed-versus-added T0 semantics.
5. Exact recomputation of all aggregate metrics and original CSM identities/provenance fields.

The hosted .NET path **created/verified real CSM with the production SDK** and checked all file source SHA values before running. The independent Python/offline accounting replay is **not an independent FastCDC implementation**; it cannot prove the individual ChunkIds from original raw source bytes independently or establish causal frame encoding costs. No forced synthetic transformations or sealed holdout paths entered this run.

## Per-pair complete accounting

All byte quantities are decimal bytes; `old preserved` and `old crossing` use old file byte populations, while `T0/T1 exact` count target chunk occurrences (not unique physical transmitted bytes).

| Family and versions | Old bytes | Old preserved at mapped offset | Cross-boundary overlap | Target bytes | T0 exact | T1 exact |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ASP.NET win-x64 10.0.10→10.0.11 | 110,294,985 | 77,196,998 | 24,562,121 | 108,026,236 | 39,581,763 | 36,933,847 |
| ASP.NET win-x64 10.0.11→10.0.12 | 110,310,200 | 77,493,031 | 24,566,022 | 110,409,533 | 56,779,252 | 54,775,296 |
| Runtime Linux ARM64 10.0.10→10.0.11 | 88,803,979 | 72,852,064 | 11,944,600 | 88,697,425 | 38,849,123 | 38,257,427 |
| Runtime Linux ARM64 10.0.11→10.0.12 | 88,776,027 | 73,037,168 | 11,853,720 | 88,703,913 | 52,196,333 | 51,486,252 |
| **Total** | **398,185,191** | **300,579,261** | **72,926,463** | **395,837,107** | **187,406,471** | **181,452,822** |

Machine-readable exact-count data and every pair's original physical old-tree ManifestId are in [`benchmarks/experiments/patch-tree-001-t1-boundary-20261015.json`](../../../benchmarks/experiments/patch-tree-001-t1-boundary-20261015.json). The original artifact contains the complete **1,066-row** raw diagnostic and frozen full-file inventories.

### Computation

```text
Old-preserved share  = 300579261 / 398185191 = 75.4873%
Cross overlap share  =  72926463 / 398185191 = 18.3147%
T0 exact-match share = 187406471 / 395837107 = 47.3443%
T1 exact-match share = 181452822 / 395837107 = 45.8403%
T1 lost availability = 187406471 - 181452822 = 5953649 B
Old T1 physical cost = 83593822 / 14758982 = 5.6639x T0
Physical extra bytes = 83593822 - 14758982 = 68834840 B
```

The overlap metric counts *partial intersections* of virtual chunk intervals with old-file intervals and is not the complement of exact-preserved bytes. The preserved-offset measure and available-anywhere measure equal each other on this corpus but are not mathematically identical in general.

## Next implementation gate — T1 physical component attribution

**Do not rewrite the candidate algorithm yet.** Prepare a separately frozen, **calibration-only**, H0/T0/T1 same-source physical reconciliation, keyed by original unchanged file identities and exact frozen policy:

- For every changed/added file: actual PAYL/PIDX/target CSM and base metadata contributions, each physical frame's compressed byte length, selected dictionary identity and source occurrence, candidate ordering, unmatched target chunks, bytes read/seeked from the virtual base.
- Verify `sum(per-file physical components) + shared metadata == exact original T0/T1 totals`; no unaccounted bytes or synthetic reconstruction of physical cost.
- Distinguish **(i)** exact old-target chunk availability, **(ii)** actual chosen reuse/dictionary, **(iii)** zstd frame effectiveness and **(iv)** additional cross-file references; avoid asserting that an available chunk was actually selected.
- Add the smallest fixed deterministic instrumentation/replay necessary; no hot-loop/product change, no candidate retuning, no new CSP encoding.
- Use preregistered source/candidate identity and byte gates. Calibration evidence alone cannot authorize a T2/T3 or segmented-base RFC, and sealed holdout remains closed.

**Final disposition:** `T1_BOUNDARY_MECHANISM_OBSERVED / PHYSICAL_SIZE_CAUSE_NOT_ISOLATED / T1_NO_GO_CALIBRATION / NO_ADOPT`. #184, #251 and #140 stay open/blocked respectively. 
