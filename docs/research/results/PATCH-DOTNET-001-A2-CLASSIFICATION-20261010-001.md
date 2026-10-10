# PATCH-DOTNET-001 A2: structural PE/CLR inventory (2026-10-10)

EvidenceId: \`PATCH-DOTNET-001/A2-CLASSIFICATION-20261010-001\`
Status: **STRUCTURAL_INVENTORY_PASS / NO_PATCH_SIZE_VERDICT**
Owner: [#221](https://github.com/definitely-stable/ChunkShift/issues/221).
Frozen prerequisite: [A1 evidence](PATCH-DOTNET-001-A1-INVENTORY-20261010-001.md).
Protocol: [PATCH-DOTNET-001-A2](../../../benchmarks/PATCH-DOTNET-001-A2-PROTOCOL.md).
Source: branch commit \`546e2dbf3c46faccbffc8a97806e326d197d0711\`; [GitHub-hosted run #38028439773](https://github.com/definitely-stable/ChunkShift/actions/runs/38028439773), attempt 1, SUCCESS.

## Independently verified artifact

- [A2 artifact #11660869109](https://github.com/definitely-stable/ChunkShift/actions/runs/38028439773/artifacts/11660869109); ZIP SHA-256 \`bb590a48f373bac2d6ac49c370e52933b7c192f4a4786a5223dbf06fb0d7f4c7\`; GitHub artifact digest matched downloaded bytes exactly.
- \`a2-files.jsonl\` SHA-256 \`0cdafee3f5ee6f288ed798bd1f7726a452dd51a919d891855e9436f6e151ad66\`.
- \`a2-summary.json\` SHA-256 \`8f3b46af8e1ed4cda11af88b173bd7b3ce9e43b82e4c54ffd6e27f2144765194\`.
- Independent verification checked both ZIP \`SHA256SUMS\` entries; classification row hash, 15,686 unique family/version/path identities, no retained \`NOT_SCANNED\` statuses, and all totals against pinned A1 byte and file counts.
- Re-materialized A1 archive identities: \`pairs.json\` SHA-256 \`67b075176a3cbe796cb20831d8caeef602e19253717e07f2c25df619d9dd9942\`; \`files.jsonl\` SHA-256 \`f342be7899b6b18384c96b4384c2796ab8a6c3d099a1cfec47c66f432b40377d\`.

## Exact changed-target classification

| Product family | Split | All changed files | Path candidate files | Structural D3-potential files | Structural D3-potential target bytes |
| --- | --- | ---: | ---: | ---: | ---: |
| ASP.NET Core win-x64 | calibration | 334 | 146 | 4 | 848,584 |
| .NET Runtime linux-arm64 | calibration | 190 | 188 | 80 | 1,502,864 |
| .NET SDK linux-x64 | evaluation | 3,092 | 2,312 | 2,087 | 189,696,680 |
| Windows Desktop win-x64 | evaluation | 284 | 284 | 228 | 18,759,624 |
| Node win-x64 | negative control | 7 | 0 | 0 | 0 |
| **Total** | | **3,907** | **2,930** | **2,399** | **210,807,752** |

The candidate file count is the **intersection** of exact A1 path scope with ILONLY structural recognition in *both* source and target, and a matching PE machine. It is *not* proof of valid IL body/token recognition, semantic equality, reconstructibility or physical patch savings.

For the full 896,579,114 changed-target bytes, 210,807,752 B = **23.5125%** are structurally D3-potential; within the 582,113,626 path-candidate bytes, the share is **36.2142%**. Neither percentage is an upper bound on **physical CSP patch savings**, because target content bytes and encoded patch bytes have different denominators, and no independent patch oracle has yet been executed.

## Classification by file kind

The full **15,686** base+target inventory, not only changed targets, contains:

| Kind | Files |
| --- | ---: |
| ILONLY | 6,644 |
| R2R_HEADER | 1,534 |
| ELF_NATIVE_UNKNOWN | 78 |
| PE_NATIVE_UNKNOWN | 52 |
| OTHER | 7,376 |
| UNSUPPORTED | 2 |

R2R_HEADER means only the mapped native-header signature matched; R2R version/fixups are not parsed. PE_NATIVE_UNKNOWN and ELF_NATIVE_UNKNOWN are not provenance-backed NativeAOT identifications. Unsupported includes an over-64-MiB native Node binary. A2 did not rewrite or decode any file and **did not** measure CSP D0, D1, D2, D3, D4 or D5.

## Critical split imbalance — holdout remains sealed

The calibration cohorts have **84** structurally D3-potential changed files (2,351,448 B), just **1.2036%** of their 195,367,029 changed-target bytes.

The evaluation cohorts have **2,315** structurally D3-potential files (208,456,304 B), **34.3460%** of their 606,929,973 changed-target bytes. Thus the evaluation population contains **27.56 times as many** D3-potential files as calibration and is strongly dominated by SDK IL-only assets, while calibration is dominated by ReadyToRun.

This is **not a sound split for choosing or tuning D3 parameters using calibration and then asserting generalization to evaluation**. The holdout has not been used to measure transform bytes or pick encoding parameters. Preserve it unchanged; if D3 must be developed beyond hand-authored vectors, preregister **additional IL-only-heavy calibration families/source hashes** in a new A1-v2 amendment before any size experiment. Do not reassign existing evaluation files to calibration, silently change candidate scopes or silently mix R2R with ILONLY.

## Decision and next permitted work

**ACCEPT A2 structural classifier and evidence only. D3 physical patch evaluation remains BLOCKED** until:

1. A1 and A2 research PRs are reviewed and provenance/evidence records merged.
2. An independently specified calibration extension provides a comparable managed IL-only population with immutable hashes and no evaluation-family/asset leakage; keep existing A1 evaluation unchanged.
3. A predeclared same-population baseline/oracle and exact byte accounting (including mapping/header/index/fallback overhead) are frozen.
4. Phase-A PR #273 TypeRef identity collision/assembly identity concerns are resolved, and independent decode/final SHA-256 verification is built into any D3 candidate.

No CSP v1, production path, PublicAPI, D15 or release/package change is authorized by this result.
