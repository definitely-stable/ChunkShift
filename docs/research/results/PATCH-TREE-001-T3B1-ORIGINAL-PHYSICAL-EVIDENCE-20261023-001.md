# PATCH-TREE-001 / T3-B1 — real two-DLL renamed-base CSP physical calibration

EvidenceId: **PATCH-TREE-001/EVIDENCE-20261023-T3B1-001**. Issue [#184](https://github.com/definitely-stable/ChunkShift/issues/184), parent research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251). Date: 2026-10-23.

**Decision: B1_PROMISING_CALIBRATION_ONLY / NO_PRODUCTION_ADOPT.** The two original preregistered renamed-DLL file comparisons both beat raw transport and ordinary self-contained CSP by much more than the protocol's 10% warm gate **after fully charging the research mapping sidecar**, and both remain smaller when charging old-file CSM. This is a real CSP v1 create/apply result, *not* a generic T3 update-set, holdout qualification, reliable create-time benchmark or production publisher approval.

## Original run and source

[Preregistered T3-B1 protocol](../../benchmarks/PATCH-TREE-001-T3B1-RENAMED-CSP-PROTOCOL.md), implemented in merged [PR #401](https://github.com/definitely-stable/ChunkShift/pull/401) at main commit **a64fc8cedb651d9c7f14f886709cfdf65c693f10**. Original first-attempt GitHub-hosted [run #39967460136](https://github.com/definitely-stable/ChunkShift/actions/runs/39967460136) **SUCCESS**, Ubuntu 24.04 Linux x64, original pinned original pairs recovery #38136336025, SHA-256 **8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd**. Source-only .NET calibration families materialized from release assets with original SHA locks, original pair identities and all content reverified. All sealed holdout trees were excluded.

Original retained GitHub artifact **#12506269723**, patch-tree-t3b1-original-two-DLL-NO-ADOPT, physical ZIP size **298,461 bytes** and GitHub artifact SHA-256 **06ce564ef43b07e5ea386551479fe86edaf8c58f266e01f579578acd50293624**.

| Archived member | SHA-256 |
| --- | --- |
| original-pairs.json | 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537bfb910fd22dd |
| layouts.json | 6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a |
| t3b1-selection.json | 2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad |
| t3b1-physical.json | c8a9de0599101e15834a9cd3026214f5176d71b251e79d1d0a6b162dbb4120cd |
| provenance.json | be095ea1eafd7cd5377f5329d9e99b8235956bc583677557b8f96705546b3c86 |

All **six** ZIP members (including SHA256SUMS) pass ZIP CRC, all five SHA256SUMS entries independently match the original archive. [Standalone independent replay](data/PATCH-TREE-001-T3B1-EVIDENCE-20261023-001/recompute.py) reproduces original corpus layout hashes, fixed pair IDs, selection source/target hashes, full binary map SHA and length independently of the producer, full cost arithmetic and host provenance. It imports neither the production patcher nor the experiment's encoder.

## Physical real CSP v1 measurements

Both source and target are version-named mscordaccore_amd64_amd64 DLLs in original ASP.NET Core win-x64. Target bytes verified by SHA-256 after *both* real public SDK CSP apply variants. The old-file BASE and embedded target CSM identity are checked against actual, newly generated old/target CSMs. F0 structural parser verifies complete physical CSP sections and PAYL/PIDX byte reconciliation. Raw CSPs and staged files were deleted after verification, not retained in the evidence ZIP.

| Transition | T0 raw | T0S self-contained CSP | T3 BASE-bound CSP | Charged path map | T3 warm total | T3 cold total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ASP.NET Core 10.0.10 → 10.0.11 | 1,356,632 | 548,714 | 86,432 | 295 | **86,727** | **87,841** |
| ASP.NET Core 10.0.11 → 10.0.12 | 1,356,640 | 548,863 | 5,649 | 295 | **5,944** | **7,058** |
| **Two-file total** | **2,713,272** | **1,097,577** | **92,081** | **590** | **92,671** | **94,899** |

Bytes are literal physical size (not abstract compression ratio). Each old-file CSM is **1,114 B**; cold adds 2,228 B total. The research map includes both paths, source and target full SHA-256, full base ManifestId and file lengths; it is **NOT** a complete production locator/index, signed tree manifest, publisher or wire-format adoption.

- Warm T3-B1 is **1,004,906 bytes (91.56%) smaller than measured self-contained T0S**, and 2,620,601 bytes (96.58%) below raw T0.
- Cold T3-B1 is 1,002,678 bytes (91.35%) below T0S. Both individual file comparisons beat the preregistered >10% warm threshold and remain below the same comparator in cold.
- The *modelled* four-pair update with changed same-path T0 preserved, original target-tree metadata unchanged, and just the original two raw added files replaced by measured B1 warm costs is **12,138,381 B**, versus accepted original T0 warm **14,758,982 B**; modelled cold **12,140,609 B**. This **17.76% modelled improvement over T0** is NOT an independent new end-to-end physical four-pair update: no complete update-set container, locator distribution, publication or receiver-side resource measurement exists.
- Historical original four-pair T0S total is 13,143,287 B. The two freshly observed T0S files total 1,097,577 B. Comparing mixed snapshots across original T0S/T0 and B1 should remain labelled arithmetic context only, not a new full T0S run.

## Creation-time warning: significant time-for-size tradeoff

One-shot original hosted timings (milliseconds, **uncontrolled diagnostic, not stable perf numbers**):

| Transition | T0S create | T3 create | T0S apply | T3 apply |
| --- | ---: | ---: | ---: | ---: |
| 10.0.10 → 10.0.11 | 691.8 | 3,502.0 | 168.0 | 31.3 |
| 10.0.11 → 10.0.12 | 181.5 | 724.9 | 16.9 | 20.0 |

T3 create required about **4–5× more measured elapsed time** than T0S in this single hosted sample, despite a much smaller patch. This is material for any create-time SLA. First-run JIT, host variability, content cache and scheduling prevent a causal CPU-performance conclusion. There is **no peak RSS, warmed-up repeat distribution, base source read-amplification benchmark, durable publisher cost, source-locator lookup CPU or large corpus evaluation**. A subsequent gate must explicitly test these instead of silently adopting the feature.

## Independent audit and bounded interpretation

The independent offline check verified original ZIP SHA against GitHub's published artifact digest, ZIP CRC, every internal SHA256SUMS member, source-lock provenance, exact frozen old/added path membership, old/new file digests against archived independently rehashed full layouts, and all map and CSP size arithmetic. It reconstructed the diagnostic binary locator format and matched both stored SHA-256 values. This is evidence-structure and cost-ledger verification.

**Limit:** raw CSPs and staged targets were intentionally deleted. An external offline auditor cannot independently decode those original patches from this ZIP. The producer GitHub-hosted run *did* create/apply real shipping CSP v1 in both lanes, verify the staged SHA-256, and invoke F0 physical reconciliation; confidence in actual decoding therefore rests on pinned source, CI and original run, not preserved raw CSP bytes. A stronger later holdout should arrange a controlled decoder replay without publishing source binaries.

Decision:
1. **T3-B1 is promising as an opt-in update-set rename mapping**, not a new CSP codec. Existing CSP v1 already exploits old-file bytes strongly when explicitly supplied the correct old-file base.
2. **No production ADOPT**. A two-file cherry-picked-by-structural-class calibration cannot establish broad rename coverage, automatic base selection, compressed transport benefit across full updates, memory bounds or secure/atomic publication.
3. Next required work is **T3-B2 candidate-selection and performance protocol**: freeze a deterministic collision-safe source-path matching policy before additional original corpus or holdout analysis; account exact old-source manifest generation/storage, cache/lookup, bytes read, create CPU/RSS, and complete update-set including modes/deletions. Ensure CPU budget and fallback to existing T0/T0S where not beneficial. Do not revive rejected T1/F2 or H5 heuristic without an independent prior-art and measured justification.
4. #184 and #251 remain OPEN. CSP/CSM v1 and shipping defaults are unchanged.

Verdict: **T3B1_ORIGINAL_TWO_DLL_REAL_CSP_PASS / B1_PROMISING_CALIBRATION_ONLY / NO_PRODUCTION_ADOPT**.
