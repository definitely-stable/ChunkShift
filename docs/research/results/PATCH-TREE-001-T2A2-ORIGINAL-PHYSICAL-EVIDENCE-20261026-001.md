# PATCH-TREE-001 / T2-A2 — independent original whole-tree CSP physical audit

**EvidenceId:** `PATCH-TREE-001/T2A2-ORIGINAL-20261026-001`  
**Date:** 2026-10-26  
**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Verdict:** **`ORIGINAL_T2_PHYSICAL_SIZE_GAIN_CONFIRMED__RESOURCE_UNQUALIFIED__NO_PRODUCTION_ADOPT`**.

## Provenance and independent replay

- Preregistered protocol [PR #418](https://github.com/definitely-stable/ChunkShift/pull/418); original pilot code [PR #419](https://github.com/definitely-stable/ChunkShift/pull/419), merged first-main source `fe26c8556c34c0c6f105038733023b1d00cafd49`.
- First-attempt GitHub-hosted Linux x64 original [Actions run #40281148160](https://github.com/definitely-stable/ChunkShift/actions/runs/40281148160) **SUCCESS** (`push` to `main`, attempt 1). Verified parent original T2-A1 CSM-only run [#40204580308](https://github.com/definitely-stable/ChunkShift/actions/runs/40204580308), source `555c992f30473c373f1efd0d6d8b3d18bf87a090`, also success.
- [Original artifact #12632258695](https://github.com/definitely-stable/ChunkShift/actions/runs/40281148160) `patch-tree-t2a2-original-CSP-pilot-NO-ADOPT`: **502,387 bytes**, whole ZIP SHA-256 `be60d2982ae8c8eb904fbd4233dd91d5248b14cf8e36a1c4c2365629573dda8e`. Original ZIP CRC, exact seven-member population, member SHA256SUMS and six pinned member digests checked independently. A1 archived SHA `823820571d3534b302e2acb8f0501ac4dbe4c6030093e6e27b3a066945d65fb6`; previous T0 calibration SHA `d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c`. Original tree inventory SHA `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a` and original frozen pairs SHA `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.
- [Machine ledger](data/PATCH-TREE-001-T2A2-EVIDENCE-20261026-001/census.tsv) preserves the complete four-pair physical size and digest, CSM record counts, full file class populations and 3-round median create/apply CPU/wall; [standalone Python stdlib replay](data/PATCH-TREE-001-T2A2-EVIDENCE-20261026-001/recompute.py) independently reconstructs every original inventory layout digest, contiguous offsets, UTF-8 ordering and path-case uniqueness, historical T0 per-file *actual CSP* + raw added payload, metadata byte lengths, original file classifications and source A1 CSM identities; checks adjacent-version CSM continuity, four physical CSP hashes, **all 16 producer-reported samples** (four warmups, twelve measured), each median and aggregate warm/cold conservation. CI fetches GitHub origin/parent/artifact metadata directly and validates those separately from ZIP-declared claims.

**Independent replay limitation:** the original job verified every reconstructed target path length and SHA256 plus full flat stream and applied each CSP four times. Actual CSP bytes and original executable source trees were *deliberately not archived*, so this independent reviewer checks the SHA/digest/Apply **reports** but cannot independently decompress those original CSPs or rerun source verification without rematerialization. Do not call this an independent binary reconstruction oracle or a third-party signature check.

## Actual physical original calibration (bytes)

| Original .NET transition | Full T0 warm | T2 CSP v1 | Full T2 warm | T2 warm saving | T2 cold* |
| --- | ---: | ---: | ---: | ---: | ---: |
| ASP.NET Core Windows x64 10.0.10→10.0.11 | 5,748,069 | 3,889,566 | 3,949,496 | **31.29%** | 4,061,439 |
| ASP.NET Core Windows x64 10.0.11→10.0.12 | 4,409,941 | 2,747,910 | 2,807,840 | **36.33%** | 2,919,639 |
| Runtime Linux ARM64 10.0.10→10.0.11 | 2,798,481 | 2,498,908 | 2,531,696 | **9.53%** | 2,606,391 |
| Runtime Linux ARM64 10.0.11→10.0.12 | 1,802,491 | 1,506,459 | 1,539,247 | **14.60%** | 1,613,474 |
| **Four-tree sum** | **14,758,982** | **10,642,843** | **10,828,279** | **26.632616%** | **11,200,943** |

**Arithmetic:** T2 warm saves **3,930,703 B** vs matched original T0 warm, satisfying the issue #184 **aggregate 10% physical-size research candidate** threshold. The old CSM plus a **research-only** base locator adds **372,664 B** across four T2 transitions; therefore T2 cold is **11,200,943 B**, **3,558,039 B (24.107618%) below T0 warm**, but this is a *cross-accounting sensitivity*, **not** an apples-to-apples T0-cold comparison. Target map bytes are included in **both** lanes, not double charged. These are restricted ordinary-file reference maps, not signed wire-format tree manifests, authenticated source locators or shipping transport framing.

## CPU, base reads and decoder

Original same-host T2 measurements use one warmup and three measured rounds **per pair**, with identical CSP SHA-256 and physical bytes across rounds. Medians below are *descriptive T2 only*, measured process-wide. They are **not paired T0/T2 speedup ratios**.

| Transition (same order as above) | T2 create CPU | T2 create wall | T2 apply wall | T2 create base Stream bytes |
| --- | ---: | ---: | ---: | ---: |
| ASP.NET #1 | 136.69 s | **68.18 s** | 0.532 s | 1,451,737,089 B |
| ASP.NET #2 | 105.05 s | **52.43 s** | 0.434 s | 1,095,373,850 B |
| Runtime #1 | 128.41 s | **64.08 s** | 0.388 s | 1,124,319,090 B |
| Runtime #2 | 84.93 s | **42.39 s** | 0.348 s | 807,073,251 B |

Repeated **large logical base rereads** and ~42–68-second T2 create wall medians on these roughly 85–105 MiB virtual trees imply a potentially expensive encoder candidate-search and/or dictionary path. Logical Stream bytes are *not measured physical disk reads, storage IOPS, page faults or unique bytes*. There is no same-run paired T0 CPU/wall, so a release CPU cap cannot be adjudicated. Apply wall is only the flat-file CSP SDK apply measured phase; it excludes authenticated multi-file staging/publication.

## Verdict and prioritized continuation

**Accept original four-pair size feasibility; do not accept production T2.** Preserve T0 production baseline, T3 first-release DEFER, no CSP/CSM schema change, no public update-set API, no release claim, no sealed Node/tzdata/source holdout. #184 and #251 stay **OPEN**.

Prioritize a separately preregistered **T2-A3 decision gate**, not an immediate first-release adoption. Before another original timed run, freeze a matched same-host T0-vs-T2 **create CPU/wall** experiment (counterbalanced, real full update-set work and identical encoded config), explicit maximum create budget, independent/private resident peak apply measurement with observer A/A, bounded locator memory/ChunkId uniqueness, and source trust/host publisher semantics under RFC-0006/T4-C3A. Distinguish CPU-saving optimization from effects on physical patch size; do not promote H3 or retune encoder on these four calibration pairs before freeze. The four original transitions are not an independent holdout. If admitted computational cost and receiver resources cannot be met, stop T2 in favor of T0/T0S.

**Final disposition:** `T2A2_ORIGINAL_PHYSICAL_SIZE_PROMISING / CREATE_RESOURCE_RISK / NO_PRODUCTION_ADOPT`.
