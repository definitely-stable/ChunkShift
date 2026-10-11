# PATCH-DOTNET-001 D3-O — preregistered calibration-only physical-byte upper bound

Issue [#221](https://github.com/definitely-stable/ChunkShift/issues/221).
Status: **FROZEN INPUT/ALGORITHM CONTRACT CANDIDATE — NO EMPIRICAL ORACLE RUN, NO ADOPTION VERDICT**.
Dependencies: A1 source inventory, A2 structural classification, A1-v2 PowerShell calibration ([verified record](../research/results/PATCH-DOTNET-001-A1V2-CALIBRATION-20261011-001.md)), and separately reviewed experimental D3 codec [PR #273](https://github.com/definitely-stable/ChunkShift/pull/273). The D3 codec has no production authority.

## Problem

An ILONLY file is not automatically normalizable. A2 found 2,399 structurally possible changed files but did not inspect token scanner success, inverses, CSP bytes or patch apply. The new A1-v2 PowerShell calibration contributes 523 **distinct target SHA-256s** across three correlated product-deployment strata (960 raw structurally possible file rows), all derived from one version transition. A high target-byte fraction alone is not evidence of benefit. Evaluate whether **even a physically impossible ideal zero-byte normalized-patch payload** could meet a minimum worthwhile physical patch saving with the actual D3 inverse-map format.

This is a **calibration-only stop/continue gate**, not a holdout verdict or measured improvement. Evaluation SDK/Desktop family/version pairs remain sealed; their artifacts and membership are never read by D3-O except for the earlier precommitted exact-content exclusion record.

## Fixed source membership and all-byte denominator

The sole D3-O population is the 1,242 changed PowerShell files from A1-v2:

- `sourcePlanSha256 = f50401eed8315e04c171ee79d07f715fa4f29625ce48f1f63ac436565daf5fee`.
- `sourcePairsSha256 = 5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9`.
- Three correlated strata: `ps-linux-fdd` (263 changed), `ps-win-fdd` (282), `ps-win-sc` (697). Base 7.5.3; target 7.5.4.
- Candidate base/target, normalized paths and original SHA-256 identities must match each immutable changed pair. An evaluation duplicate must never be admitted.
- Every changed file must have **one and only one** D0 physical-patch result. No omission, reweighting, best-only selection, or moving evaluation files to calibration.
- Count all changed files, including native/R2R, unsupported, zero slots and failed scans. A structurally ineligible/failing D3 candidate uses unchanged D0 bytes.

If the A1-v2 pair hash, membership or role differs, abort before producing a result.

## Required independently produced input

The input document has schema `chunkshift.patch-dotnet-d3-upper-bound-input.v1` and contains:

- `experimentId: PATCH-DOTNET-001`, `role: calibration`, exact frozen source plan/pairs hashes, `d0PolicyId: CSP_DEFAULT_H0`;
- named baseline/scanner GitHub-hosted evidence objects with exact artifact SHA-256, Git commit SHA and workflow run IDs. **These are caller-supplied declarations**: the oracle verifies shape but does **not** authenticate remote workflow logs, that audit must be performed before accepting a result;
- `rows`: exactly one row for every frozen `(family,base,target,path)`, with original `baseSha256`, `targetSha256`, `baseSize`, `targetSize`, unchanged `d0PhysicalBytes` and **verified H0 apply target SHA-256**;
- a D3 scanner state `VERIFIED_BOTH` only if both base and target accepted strict D3 semantics, have at least one supported IL token operand, both independent reconstruction tests returned **their exact input SHA-256**, and target/base slot counts are within 1..262,144;
- otherwise `FALLBACK` or `INELIGIBLE`, both slot counts zero and reconstruction fields null. No scanner success may be claimed outside structural A2-v2 scope or above 64 MiB.

No production code or persisted format is modified. The scanner must not be allowed to learn, retune or overwrite the existing evaluation split.

## Oracle computation

For changed row `i`, let `H_i` be its exact **whole physical D0 patch bytes**, including CSP framing, dictionary/control headers and padding. Let `N_i` be verified target D3 TypeRef operand count. If scanner verified both images, the **raw CDN1 inverse metadata** of the current research prototype has size exactly `S_i=44+8N_i` bytes. Other D3 candidate physical bytes can never be negative.

Define an **impossibly optimistic** patch for that row that has **zero bytes of normalized payload, patch framing, base normalization references and all other overhead**, but still preserves the complete raw CDN1 inverse map. The oracle may always fall back to D0:

```text
    L_i = min(H_i, 44 + 8*N_i)  if independently scanner-verified
        = H_i                  otherwise
    U_i = H_i - L_i
    maximum theoretical physical saving fraction
        = sum(U_i) / sum(H_i)   over ALL 1,242 changed rows
```

Thus an actual implementation retaining the same raw CDN1 metadata layout cannot exceed this ideal ceiling. If a future proposal **compresses** or otherwise redesigns the inverse format, this raw-layout bound is not valid for that changed format; freeze a separate oracle and prove its different minimum byte cost. The bound is not a general entropy lower bound, and no observed actual patch sizes can be inferred from `U_i`.

The implementation emits exact integer sums, an ideal ceiling percentage, and optional median **distinct (baseSha256,targetSha256) pair** percentage for diagnostic review. Where the same binary pair is repeated across correlated deployments, the distinct-pair diagnostic takes the **minimum** observed ratio, independent of input row ordering. The corpus-weighted denominator always counts every physical changed file instead.

If `sum(U_i)/sum(H_i) < 0.05` under these maximally favorable assumptions, STOP this D3 fixed-sidecar design for the pinned calibration scope and record the negative bound. If at least 0.05, status is only **MAY_CONTINUE_CALIBRATION** after review, not ACCEPT, a predicted improvement or authorization to unseal holdout. Per-family 15%/25% thresholds, decoder/apply CPU/RSS, D1 baseline attribution, independent application/NuGet diversity and fresh held-out decision protocol remain mandatory.

## Implementation and test gate

The independent Python tool is `benchmarks/scripts/patch_dotnet_001_d3_oracle.py`:

```sh
python3 benchmarks/scripts/patch_dotnet_001_d3_oracle.py \
  --pairs /sha256-verified-A1v2/v2-pairs.json \
  --input /independently-audited-calibration/d3-oracle-input.json \
  --output /new-path/d3-ideal-ceiling.json
```

The input report must be generated only after separate **real GitHub-hosted same-population D0 create/apply** and separately verified **D3 scanner+inverse roundtrip** runs. A synthetic test may assert arithmetic/invariants but is **not** evidence. The report string is deliberately `OPTIMISTIC_CEILING_ONLY_NO_PATCH_SIZE_VERDICT`; it must never claim a real patch win. The actual oracle result remains **NOT_RUN** until inputs exist.

Fail-closed cases include: missing/duplicate/extra file, wrong source/role, target/base drift, unauthenticated-scope scanner success, false/zero/noninteger physical byte counts, missing independent SHA matches, excessive slots, contradictory fallback fields, and source lock mismatch. There must be no claim of success before an independently audited evidence artifact exists.

Next **allowed** action: implement a source-backed D0 physical-size collector and standalone full-file D3 slot/roundtrip collector on GitHub-hosted runners, still strictly calibration-only; then run the frozen oracle and decide whether the cost ceiling justifies an actual patch-size experiment.
