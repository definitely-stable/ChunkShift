# PATCH-DOTNET-001 — A1 preregistered source corpus and audit contract

Experiment: [#221](https://github.com/definitely-stable/ChunkShift/issues/221). Independent foundation: [PR #273](https://github.com/definitely-stable/ChunkShift/pull/273). This is a **premeasurement corpus plan**, not D0–D5 decision evidence and not a claim that the full #221 corpus has been collected. No CSP v1, Production create, PublicAPI or R2R rewriting is affected.

## A1 input lock

Source selection: \`patch-dotnet-001/corpus-plan.v1.json\` (the exact committed Git blob, with a SHA-256 printed by \`validate\`). The role assignment is **whole product family**, not a per-file random split. The archive digests are immutable expectations and must be checked before *any* extraction. Windows/Linux and x64/ARM64 are separate strata; no two .NET product families occur in both calibration and evaluation.

| Role | Product / RID | Base → target | Provenance |
| --- | --- | --- | --- |
| calibration | ASP.NET Core runtime / win-x64 | 10.0.10 → 10.0.11 | source-assets.sha256 from pre-existing frozen patch corpus |
| calibration | .NET runtime / linux-arm64 | 10.0.10 → 10.0.11 | source-assets.sha256 from pre-existing frozen patch corpus |
| evaluation | .NET SDK / linux-x64 | 10.0.111 → 10.0.112 | Microsoft official 10.0 releases.json, exact SHA-512 per archive |
| evaluation | Windows Desktop runtime / win-x64 | 10.0.11 → 10.0.12 | Microsoft official 10.0 releases.json, exact SHA-512 per archive |
| negative-control | Node / win-x64 | 24.20.0 → 24.21.0 | source-assets.sha256 from frozen patch corpus; **not NativeAOT** |

Primary source for the new SHA-512 locks:
<https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json>.
Existing SHA-256 locks: \`docs/benchmarks/patch-corpus/source-assets.sha256\`.

**Independence caveat:** SDK and WindowsDesktop distribute parts of the core runtime; their archives are not independent at the binary-content level. The frozen \`candidateAllowPrefixes\` separate product-specific candidate regions (SDK: \`sdk/\`; Desktop: \`shared/Microsoft.WindowsDesktop.App/\`; ASP.NET: \`shared/Microsoft.AspNetCore.App/\`; Runtime: \`shared/Microsoft.NETCore.App/\`). This avoids treating shared embedded runtime files as eligible new product-specific D2/D3 evidence. Crucially, **all other changed files remain in the full \`changed\` population**, and future candidate runs must preserve their D0 results unchanged. Thus the product-level byte denominator cannot be silently reduced to the most favorable subset. Candidate scope is only a conservative path prefilter; the PE/CLR parser must subsequently classify each file independently.

Node is a native negative control, not evidence about NativeAOT. The full protocol still needs independent NuGet package, framework-dependent-app, self-contained-app, NativeAOT-labelled and R2R-composite strata before a **general** ADOPT/DEFER/REJECT decision. Current source coverage cannot authorize a general persisted-format RFC.

## Implementation

The materializer \`benchmarks/scripts/patch_dotnet_001_corpus.py\` has two modes:

\`\`\`sh
python3 benchmarks/scripts/patch_dotnet_001_corpus.py validate \
  --plan docs/benchmarks/patch-dotnet-001/corpus-plan.v1.json

python3 benchmarks/scripts/patch_dotnet_001_corpus.py materialize \
  --plan docs/benchmarks/patch-dotnet-001/corpus-plan.v1.json \
  --sources /verified-downloads \
  --root /new-empty-path/dotnet-a1
\`\`\`

\`materialize\` is intentionally **offline**: it does not fetch unpinned URLs or autonomously select versions. Download the named archives from the exact source URLs using a GitHub-hosted runner and supply all ten original assets. The materializer verifies every asset's pinned SHA-256/SHA-512 before any extraction; it also records a SHA-256 of each asset. It rejects missing/symlinked sources, unsafe absolute/traversal/member paths, case-insensitive collisions, suspicious member types, mismatched sizes/digests and excessive member/output bytes. Known symlink/hardlink archive entries are skipped and counted, never followed. A staging directory is atomically published only on success; an already existing output directory is rejected.

Outputs:

- \`pairs.json\`: \`chunkshift.patch-pairs.v1\`, unchanged full-population \`changed\`, \`added\`, \`removed\`, \`identical\` accounting, plus \`role\`, \`product\`, \`rid\`, frozen \`candidatePaths\` and \`candidateTargetBytes\`.
- \`files.jsonl\`: **all** extracted base and target paths, byte lengths, per-file SHA-256, role and path-scope flags. \`parserClassification\` is deliberately \`NOT_SCANNED\`; path eligibility is not a CLR/R2R classification.
- \`audit.json\`: raw plan SHA-256, pairs SHA-256, complete file-inventory SHA-256, archive identities and extraction/link-skip counts. No performance or patch-size fields are populated.

A future independent auditor must rerun extraction from the pinned archives and compare both inventory hashes and the signed/immutable CI run identity. Never silently replace a pinned digest after a vendor asset changes.

## Acceptance and next slice

A1 infrastructure acceptance: GitHub-hosted test workflow green, adversarial traversal/collision/corruption tests, deterministic synthetic reconstruction of the inventory, immutable product-split definition. This **does not assert that official source archives have already been downloaded and materialized**. Actual official population evidence needs an audited hosted-run artifact with complete per-file inventory; after that, an independent frozen .NET parser classifier (A2) will enumerate ILONLY/R2R/mixed/native/unsupported and exact byte/eligibility shares.

No transform bytes, compressed candidate sizes or apply measurements may be inspected before holdout lock review and protocol freeze. Full decision experiments require the additional strata listed above, same-population D0/generic comparison and every persisted transform metadata/index byte counted in physical patch size.
