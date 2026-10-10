# PATCH-DOTNET-001 — A1-v2 independent application calibration (2026-10-11)

**Status: SOURCE-PREREGISTRATION / NO PATCH OR ADOPTION VERDICT.**
Issue [#221](https://github.com/definitely-stable/ChunkShift/issues/221). Stacked research [PR #278](https://github.com/definitely-stable/ChunkShift/pull/278), after A1 [#275](https://github.com/definitely-stable/ChunkShift/pull/275) and A2 [#276](https://github.com/definitely-stable/ChunkShift/pull/276). D3 prototype [#273](https://github.com/definitely-stable/ChunkShift/pull/273) remains separate.

## Motivation / hard stop

A2 has calibration **84 structurally D3-potential changed files / 2,351,448 target bytes**, evaluation **2,315 files / 208,456,304 target bytes**, and no measured patch savings. The native-only generic G4 benchmark cannot substitute for an independent managed evaluation. Do not tune IL tokens or select an encoder from the evaluation cohort.

This amendment freezes **new calibration** source assets separately; it neither changes original A1/A2 role assignments nor replaces or expands the original evaluation population. The denominator retains every changed target and unchanged/fallback classes.

## New calibration data (SHA-256-pinned upstream assets)

Source-of-truth JSON: [calibration-extension-v2.v1.json](patch-dotnet-001/calibration-extension-v2.v1.json). The SHA-256 values are the official release-asset digests published by the [PowerShell/PowerShell v7.5.3](https://github.com/PowerShell/PowerShell/releases/tag/v7.5.3) and [v7.5.4](https://github.com/PowerShell/PowerShell/releases/tag/v7.5.4) GitHub releases.

| Frozen group | Deployment | Base → target | Distinct pairs |
| --- | --- | --- | --- |
| `ps-win-fdd` | Windows x64 framework-dependent ZIP | 7.5.3 → 7.5.4 | 1 |
| `ps-linux-fdd` | Linux x64 framework-dependent tar.gz | 7.5.3 → 7.5.4 | 1 |
| `ps-win-sc` | Windows x64 self-contained ZIP | 7.5.3 → 7.5.4 | 1 |

**Correlation caveat:** these are **three deployment strata of one product and one version transition**, not three statistically independent release/version families. They are deliberately calibration-only. The Windows self-contained files may include shared framework assemblies also present in other .NET distributions; file-hash duplicate exclusion and final semantic overlap review are mandatory.

## Exact eligibility and provenance contract

1. Hash-verify all **six** source assets with the published SHA-256 before extraction; do not reassign missing versions or silently repair checksums. No variable archive versions, external unpinned feeds or runtime build substitution. Sources are materialized only on GitHub-hosted runners.
2. Extract into an artificial stable `payload/` prefix with the same bounded A1 archive parser and case-insensitive duplicate-path detection. Do not follow archive symlinks or use absolute/traversal paths. Preserve all changed/added/removed/identical bytes.
3. Load the original [A2 evidence artifact](https://github.com/definitely-stable/ChunkShift/actions/runs/38028439773/artifacts/11660869109) from immutable run #38028439773 and verify `a2-files.jsonl` exact SHA-256 `0cdafee3f5ee6f288ed798bd1f7726a452dd51a919d891855e9436f6e151ad66`. All evaluation base and target file SHA-256 values (not only candidates) make up the exclusion set.
4. Repeat exact A2 structural PE/CLR classification of **both** versions per changed path; `ILONLY` + same PE machine and length cap are required for potential D3 eligibility. If either original SHA equals a sealed evaluation file SHA, classify the pair as *content-overlap-excluded*. Keep overlapping files in total physical-byte population.
5. Record source and inventory digests, every changed pair class/reason, scope, candidate/fallback counters. Changes to parser or lock require a new preregistration and replay, not a silent update to this evidence.
6. **No D3 patch construction, compression ratio, transform-sidecar size, transform tuning, encoding-policy selection or holdout measurement in A1-v2.**

The A1-v2 exclusion is exact-content identity only. Different bytes can still share assembly/API families. Treat that correlated-distribution risk explicitly in the later D3 study. A1-v2 can improve calibration *coverage*, but cannot itself establish independence from the SDK/Desktop evaluation distributions.

## Reproduction

```sh
python3 benchmarks/scripts/patch_dotnet_001_a1v2.py validate
python3 benchmarks/scripts/patch_dotnet_001_a1v2.py materialize \
  --sources /verified-powershell-release-downloads \
  --a2-index /verified-A2/a2-files.jsonl \
  --root /new-empty-path/calibration-v2
```

GitHub-hosted [PATCH-DOTNET-001 A1-v2 calibration inventory](../../.github/workflows/patch-dotnet-001-a1v2.yml) automates the pinned downloads, SHA verification, source extraction, classification, holdout exclusion, and evidence upload. This workflow does not create CSP patches.

Outputs: `v2-files.jsonl` (all base/target file digests); `v2-pairs.json` (every changed file and eligibility/fallback reasons); `v2-audit.json` (exact plan, source, evaluation-index and result hashes); `SHA256SUMS` (artifact members). The exact source-run identity and artifact SHA must be archived only after a verified run succeeds.

## Progression gate

After hosted materialization, separately review whether the **effective D3-eligible ILONLY calibration** share and distinct-application diversity improved. If still badly mismatched to evaluation, **do not treat PowerShell as a statistical substitute for the SDK/Desktop evaluation**; import more preregistered independent application/NuGet families. The next algorithmic gate is a predeclared same-population D0/D3 **optimistic upper-bound oracle with every inverse metadata byte counted**. Only then consider a separately audited D3 physical-patch pilot.

CSP v1/encoder defaults/PublicAPI, published packages and any persisted transform formats remain unchanged.
