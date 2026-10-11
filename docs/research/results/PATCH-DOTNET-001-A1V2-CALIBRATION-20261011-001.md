# PATCH-DOTNET-001 A1-v2: PowerShell calibration inventory (2026-10-11)

EvidenceId: `PATCH-DOTNET-001/A1V2-CALIBRATION-20261011-001`
Verdict: **SOURCE_CALIBRATION_INVENTORY_PASS / NO_D3_PATCH_SIZE_VERDICT**
Owner: [issue #221](https://github.com/definitely-stable/ChunkShift/issues/221). 
Contract: [calibration v2 protocol](../../../benchmarks/PATCH-DOTNET-001-CALIBRATION-V2.md); [immutable source manifest](../../../benchmarks/patch-dotnet-001/calibration-extension-v2.v1.json).
Prerequisites: [A1 source inventory](PATCH-DOTNET-001-A1-INVENTORY-20261010-001.md) and [A2 PE/CLR classification](PATCH-DOTNET-001-A2-CLASSIFICATION-20261010-001.md).

## Reproducible, independently checked source identity

- GitHub-hosted [workflow run #38119011975](https://github.com/definitely-stable/ChunkShift/actions/runs/38119011975), attempt 1, **SUCCESS** on `be563de9ada7e9d330476add8faa0a1128cd7155`. Corrected the previously failing `PYTHONPATH` import; the previous [#38091980004](https://github.com/definitely-stable/ChunkShift/actions/runs/38091980004) was a workflow-import failure and is **not** evidence.
- Six upstream PowerShell v7.5.3/v7.5.4 release archives (Windows x64 framework-dependent, Linux x64 framework-dependent, Windows x64 self-contained): original release-asset SHA-256 values verified before extraction; no archive links skipped.
- Exact A2 evaluation membership: [artifact #11660869109](https://github.com/definitely-stable/ChunkShift/actions/runs/38028439773/artifacts/11660869109), `a2-files.jsonl` SHA-256 `0cdafee3f5ee6f288ed798bd1f7726a452dd51a919d891855e9436f6e151ad66`. No original evaluation base or target file SHA-256 overlapped any new changed PowerShell file (exact digest test).
- A1-v2 source-plan SHA-256: `f50401eed8315e04c171ee79d07f715fa4f29625ce48f1f63ac436565daf5fee`.
- A1-v2 `v2-files.jsonl` SHA-256: `70303a828706cba3d979f213ae4f891c93a87e09d499cc553a046323f02f6d43`.
- A1-v2 `v2-pairs.json` SHA-256: `5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9`.
- [GitHub Actions artifact #11694182924](https://github.com/definitely-stable/ChunkShift/actions/runs/38119011975/artifacts/11694182924), ZIP SHA-256 `ebe808e20466c11ab9046b15c14d4f0fdee3925bf944b0b4ba5729ff574ca28a`, 339,222 bytes. Independently downloaded; ZIP digest, embedded `SHA256SUMS` for all three evidence files and the JSONL/pairs digests against `v2-audit.json` all match.
- 3,982 unique family/version/path base+target inventory keys; six adversarial Python tests **PASS**; no skipped source links; source-archive checksum or duplicate-path failures abort materialization.

## Calibration-only content counts

| Product/version pair deployment | Changed files | Changed target bytes | Structurally D3-potential files | Structural D3 candidate bytes | *New* distinct SHA-256 candidate files | Distinct target bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| PowerShell 7.5.3→7.5.4 linux-x64 framework-dependent | 263 | 36,894,282 | 239 | 28,531,264 | 239 | 28,531,264 |
| PowerShell 7.5.3→7.5.4 win-x64 framework-dependent | 282 | 39,671,564 | 244 | 30,373,264 | 26 | 14,436,032 |
| PowerShell 7.5.3→7.5.4 win-x64 self-contained | 697 | 259,977,323 | 477 | 27,392,760 | 258 | 19,705,192 |
| **Sum, correlated deployments** | **1,242** | **336,543,169** | **960** | **86,297,288** | **523** | **62,672,488** |

The three deployment packages of one PowerShell update are **one correlated product/version-pair event**. The non-deduplicated 960 changed rows and 86,297,288 bytes are *not* independent calibration observations. The unique-by-target-SHA subset has 523 items and 62,672,488 bytes; overlaps with the original A1 calibration full-file SHA-256 set were independently examined and were **zero**. This SHA-256 absence does **not** prove semantic independence from .NET SDK or Runtime family code.

Compared with pre-extension A2 calibration (84 structurally potential changed files / 2,351,448 changed target bytes), PowerShell provides a much larger application-specific experimental input. The frozen evaluation still holds 2,315 structurally potential changed files / 208,456,304 bytes from the SDK/Desktop product families. The calibration/evaluation populations thus remain architecturally different even though the raw candidate count imbalance has improved.

## Negative and future gates

- The `ILONLY` designation is **structural PE/CLR recognition only**, not a guarantee that the PR #273 token scanner can normalize or exactly reconstruct any of the 523 unique files.
- No per-file D0 CSP physical patch sizes, normalized candidate patches, sidecar transform bytes, compression-ratio results or apply benchmarks were produced. Percent of target content that can be scanned is **not** an estimate of patch savings.
- Keep the original A2 evaluation sealed; do not move evaluation files into calibration or change candidate prefixes after seeing any candidate-size result. Include fallback/ineligible bytes unchanged in the denominator.
- **Next allowed experimental work:** independently preregister the D3 *same-population* optimistic physical-patch lower-cost oracle, including `44 + 8×N` target-inverse metadata lower bound, with an exact per-file baseline H0 and SHA-256 membership gate. That oracle must run on **calibration only** before a holdout decision; no D2/D4/D5 adoption or production change without independent exact decode and additional product strata.
- For a **general** D3 decision, obtain more independent managed-application and NuGet package families; three PowerShell distributions do not constitute independent samples.

Reproducibility: rerun the original six pinned archive URLs on a GitHub-hosted runner using `.github/workflows/patch-dotnet-001-a1v2.yml`, preserving manifest SHA and A2 index SHA above. Only the calibration extension is added; CSP v1 and production policy remain unchanged.
