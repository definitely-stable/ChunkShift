# PATCH-TREE-001 / T2-A3A — independent original paired CREATE cost audit

**EvidenceId:** `PATCH-TREE-001/T2A3A-ORIGINAL-20261027-001`  
**Date:** 2026-10-27  
**Issues:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), [#251](https://github.com/definitely-stable/ChunkShift/issues/251) — both stay OPEN.  
**Research disposition:** **`A3A_CREATE_COST_GATE_FAIL__T2_DEFER`** for first-release T2; **`NO_PRODUCTION_ADOPT`**. This is not a rejected GitHub run or a general proof that cross-file patching cannot be efficient.

## Source pin and independent evidence

- Preregistered [T2-A3A protocol](../../benchmarks/PATCH-TREE-001-T2A3A-PAIRED-CREATE-PROTOCOL.md) and real-CSP implementation [PR #421](https://github.com/definitely-stable/ChunkShift/pull/421), squash-merged `main@5b2f1608030b6660c7e0df7454a892b3835808a7`.
- First-attempt GitHub-hosted Ubuntu x64 `main` [Actions run #40299065287](https://github.com/definitely-stable/ChunkShift/actions/runs/40299065287) **SUCCESS**. The original runner's correctness and report generation succeeded; the *scientific cost admission gate* independently failed.
- Original ZIP [artifact #12641767485](https://github.com/definitely-stable/ChunkShift/actions/runs/40299065287), `patch-tree-t2a3a-original-paired-create-NO-ADOPT`: 505,901 bytes, complete ZIP SHA-256 `7570fee0a9f67b49741a7fb333d6e04b6e3fc20082cda1dfd7596d2b671fb511`. Eight exact members, CRC, SHA256SUMS and independently fixed per-member SHA checks. Frozen originals: pairs `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`, inventory `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a`, T0 `d0de7fb182cc15bd590b1335c92705d464764fab0cdfc021ad9f31928e2d5b5c`, T2-A1 `823820571d3534b302e2acb8f0501ac4dbe4c6030093e6e27b3a066945d65fb6`, T2-A2 `9d7de74a2bd44f10a2c299909a2b16e40a8f394ff3afcf8fbf319e6d19b97939`.
- [Independent stdlib Python replay](data/PATCH-TREE-001-T2A3A-EVIDENCE-20261027-001/recompute.py) reuses the **independent original tree/layout/physical T0–T2 audit** from [T2-A2](data/PATCH-TREE-001-T2A2-EVIDENCE-20261026-001/recompute.py), rather than trusting `physicalIdentityVerified` booleans alone. It recomputes each original T0 per-file SHA/byte manifest digest from the archived *actual changed-file CSP and raw-added rows*; validates T2 physical CSP SHA, CSM identities, file classes, full path metadata, and every sample against the pinned A2 output. It recalculates 8 warmups, 32 measured observations, four CPU/wall medians per pair, T2/T0 ratios and frozen per-pair and aggregate gates; checks the [committed machine ledger](data/PATCH-TREE-001-T2A3A-EVIDENCE-20261027-001/census.tsv).
- Evidence CI independently fetches GitHub's run #40299065287, its artifact ID and parent original A2 run #40281148160, plus the exact ZIP; refuses tampered medians, logical order, output digests and cost-gate labels. Original sealed Node/tzdata/ChunkShift-source holdouts were not opened.

## Measured real-CSP CREATE-only results

**T0** encodes a real v1 CSP for every changed same-path file, and raw-transfers added paths. **T2** encodes one real v1 CSP from old/new whole-tree CSM. Both include the same full target regular-file map in the **physical size ledger**. Both *exclude* CSM construction, source verification, hashing, post-encode verification, artifact publication, network transfer and final host installation from timed CREATE. Both lanes ran in the same runner/process under one warmup each, then the preregistered four-round ABBA sequence `T0,T2 / T2,T0 / T2,T0 / T0,T2`.

| Original .NET transition | T0 median CPU | T2 median CPU | T2/T0 CPU | T0 median wall | T2 median wall | T2/T0 wall | A3A cost gate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| ASP.NET Core Win x64 10.0.10→10.0.11 | 127.852 s | 152.793 s | 1.195× | 66.793 s | **76.246 s** | 1.142× | **FAIL** |
| ASP.NET Core Win x64 10.0.11→10.0.12 | 75.839 s | 110.775 s | 1.461× | 40.823 s | 55.261 s | 1.354× | PASS |
| Runtime Linux ARM64 10.0.10→10.0.11 | 126.045 s | 139.628 s | 1.108× | 65.103 s | **69.693 s** | 1.070× | **FAIL** |
| Runtime Linux ARM64 10.0.11→10.0.12 | 81.678 s | 99.706 s | 1.221× | 42.798 s | 49.771 s | 1.163× | PASS |

**Original physical size retained:** complete T0 warm **14,758,982 B**, complete T2 warm **10,828,279 B** (T2 saves **3,930,703 B = 26.632616%**), matching the audited [T2-A2](PATCH-TREE-001-T2A2-ORIGINAL-PHYSICAL-EVIDENCE-20261026-001.md) and not creating a new size claim or holdout. Four repeated T2 output CSP SHA-256 values match original A2 exactly, and T0 per-file output digests match the previous per-changed-path CSP/raw source SHA.

The preregistered decision required **for all four pairs**: T2/T0 median process CPU **≤3×**, T2/T0 median wall **≤3×**, *and* absolute T2 median create wall **≤60 seconds**. All four meet both relative ≤3× gates. **Two violate the independent absolute 60-second wall gate**: ASP.NET 10.0.10→10.0.11 by **16.246 s**, and Runtime ARM64 10.0.10→10.0.11 by **9.693 s**. The absolute release-research threshold was fixed *before* the run and was not relaxed post hoc. Hence **2 PASS / 2 FAIL → T2 first-release DEFER**.

Large T2 logical old-tree `Stream` rereads (approx. 0.81–1.45 GiB/GB per create, depending on pair) recur across repeats. They are *not* proof of physical disk I/O, page faults or a particular codec bottleneck; this experiment has no per-stage profiling. T0 per-file base reads were not captured by the T2 whole-tree stream counter; reporting its zero as absence of T0 IO would be invalid.

## Scope and next action

1. **Close A3A as evidence, preserve code and measurements; choose `DEFER` for T2 in the intended first public Patching release.** Preserve current T0/T0S first-release evaluation and T3 DEFER. Do not silently proceed to full T2-A3B memory qualification, adopt T2, weaken 3×/60-s budgets, alter CSP v1/CSM v1 or open sealed holdouts to rescue this result.
2. A distinct optional future performance research proposal may target the actual create-stage CPU/wall bottleneck and pre-register a new paired protocol, cost and size guards **before** further calibration. In its absence, stop T2 original first-release optimization and proceed with accepted release research synthesis under #251.
3. Not covered by this ZIP: actual original CSP bytes or a new independent binary decoder replay; independently sampled peak private RSS at Apply; full trusted source locator, OS publisher, signed update-set, symlink/mode/ACL semantics; end-to-end create including CSM precomputation; multiple host types or sealed holdout. The observations support only this bounded same-host original CREATE decision.

**Final verdict:** `T2A2_ORIGINAL_SIZE_GAIN_CONFIRMED / T2A3A_PAIRED_CREATE_COST_REJECT / T2_FIRST_RELEASE_DEFER / NO_PRODUCTION_ADOPT`.
