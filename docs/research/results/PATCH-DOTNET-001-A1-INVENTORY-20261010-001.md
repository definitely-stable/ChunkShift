# PATCH-DOTNET-001 A1: pinned .NET source inventory (2026-10-10)

EvidenceId: PATCH-DOTNET-001/A1-INVENTORY-20261010-001
Verdict: **SOURCE_INVENTORY_PASS / NO_PATCH_SIZE_VERDICT**
Owner: [#221](https://github.com/definitely-stable/ChunkShift/issues/221).
Contract: [A1](../../benchmarks/PATCH-DOTNET-001-CORPUS-A1.md); [pinned plan](../../benchmarks/patch-dotnet-001/corpus-plan.v1.json).
[GitHub-hosted Ubuntu 24.04 run #38025480821](https://github.com/definitely-stable/ChunkShift/actions/runs/38025480821): SUCCESS, attempt 1, HEAD 069503508c2d5cc90303cda6162bc7a939d0b201.
No transform, D0/D3 patch measurement or CSP v1 change occurred.

## Source provenance and independent integrity check

- 10 source assets: all expected SHA-256/SHA-512 verified before extraction, zero skipped links, no malformed path incident.
- Preregistered raw plan SHA-256: 0ee85c6307ea2c6612e52313485aa1bcf5725b457ae0c01413cf0d1a48e552c8
- pairs.json SHA-256: 67b075176a3cbe796cb20831d8caeef602e19253717e07f2c25df619d9dd9942
- files.jsonl SHA-256: f342be7899b6b18384c96b4384c2796ab8a6c3d099a1cfec47c66f432b40377d
- ZIP artifact: [#11659659254](https://github.com/definitely-stable/ChunkShift/actions/runs/38025480821/artifacts/11659659254), SHA-256 f3e1ff4c1dfdf7daf3ff4fbe4d4400c57bfc3abb43397e23c5b0b8b83486a4d1; retention 90 days. Regenerable from the pinned source URLs and digests after expiration.
- The extracted ZIP's SHA256SUMS entries for all three evidence files were independently checked against their actual bytes, and the whole ZIP SHA-256 equals the Actions digest.

The 15,686 inventory rows describe both base and target files. The 3,907 changed files total 896,579,114 bytes of changed **target content**. These are not actual CLR-eligible counts; that classification belongs to A2.

## Full-population and scoped counts

| Family | Role | Changed files | Candidate paths | Changed target bytes | Scoped target bytes | Added/removed | Identical |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| aspnetcore-win-x64 | calibration | 334 | 146 | 106,669,604 | 29,851,722 | 1/1 | 4 |
| runtime-linux-arm64 | calibration | 190 | 188 | 88,697,425 | 88,311,833 | 0/0 | 3 |
| sdk-linux-x64 | evaluation | 3,092 | 2,312 | 514,074,149 | 371,094,247 | 50/50 | 1,888 |
| desktop-win-x64 | evaluation | 284 | 284 | 92,855,824 | 92,855,824 | 0/0 | 3 |
| node-win-x64-control | negative-control | 7 | 0 | 94,282,112 | 0 | 0/0 | 1,987 |
| **Total** | | **3,907** | **2,930** | **896,579,114** | **582,113,626** | | |

Evaluation changed target bytes: 606,929,973; scoped bytes: 463,950,071 (76.442%). Full population scoped share is 64.926%. **These are frozen path-scope shares, not semantic parser eligibility and not patch-size improvement.**

## Limitations and next gate

SDK and WindowsDesktop archives contain embedded core runtime files, so the frozen product-specific candidate path scopes are distinct; overlapping runtime files remain in the entire H0 corpus, not the .NET-specific candidate set. H0 physical bytes for every noncandidate must remain unchanged in all later comparisons.

A general adoption verdict also requires NuGet packages, framework-dependent and self-contained apps, explicit ReadyToRun/composite and provenance-labelled NativeAOT. Node is a native control, not a NativeAOT surrogate.

Next permitted slice: **A2 strict PE/CLR/ILONLY/R2R/mixed/native classification on the pinned full-file inventory**, with exact eligibility/fallback file and byte shares, parser identity and independently checked provenance. No transform-size tuning, decision measurement or production changes yet.
