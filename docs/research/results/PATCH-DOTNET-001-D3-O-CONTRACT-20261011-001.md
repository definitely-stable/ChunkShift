# PATCH-DOTNET-001 D3-O: physical-byte oracle contract validation (2026-10-11)

EvidenceId: `PATCH-DOTNET-001/D3-O-CONTRACT-20261011-001`
Verdict: **CONTRACT_TESTS_PASS / SOURCE_MEMBERSHIP_PASS / EMPIRICAL_ORACLE_NOT_RUN / NO_PATCH_SIZE_VERDICT**.
Owner: [issue #221](https://github.com/definitely-stable/ChunkShift/issues/221).
Protocol: [D3 upper-bound preregistration](../../../benchmarks/PATCH-DOTNET-001-D3-UPPER-BOUND-PROTOCOL.md).
Immutable data source: [A1-v2 PowerShell calibration inventory](PATCH-DOTNET-001-A1V2-CALIBRATION-20261011-001.md).

## Independent GitHub-hosted verification

[Run #38119463420](https://github.com/definitely-stable/ChunkShift/actions/runs/38119463420) succeeded on `7cb355dbccbe058602f0327b3a494b29929d7315`:

- **8/8 synthetic/adversarial oracle tests PASS**: full-population byte-accounting, missing/duplicate/extra files, source/role/target drift, scanner false positives, malformed slot counts and wrong roundtrip hashes, ideal sidecar costs, fallback/no-eligibility cases and repeated content diagnostic.
- Downloaded exact source [A1-v2 GitHub artifact #11694182924](https://github.com/definitely-stable/ChunkShift/actions/runs/38119011975/artifacts/11694182924), validated its artifact SHA-256 and the locked `v2-pairs.json` SHA-256 `5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9`.
- Recomputed that the frozen calibration-only population contains **1,242 changed file rows** across three PowerShell deployment strata. Original A2 evaluation never used as input to this oracle.
- No candidate/persisted patch was produced; no D0 baseline or independent D3 scanner/codec results were available to the oracle. The only percentage assertions are synthetic arithmetic tests and **must never** be interpreted as empirical savings.

## Frozen idealized calculation

For each changed calibration row with measured, independently verified D0 whole physical patch size `H_i` and verified D3 target slot count `N_i > 0`, the fixed prototype `CDN1` inverse map costs `44+8*N_i` bytes. The oracle imagines an impossible **zero-byte normalized patch body and zero additional framing** and chooses the cheaper of ideal D3 and D0 for that row. For every rejected or ineligible file the cost remains unchanged D0:

```text
L_i = min(H_i, 44 + 8*N_i)      for VERIFIED_BOTH
L_i = H_i                       otherwise
U_i = H_i - L_i
max_potential_fraction = sum(U_i) / sum(H_i)   over all 1,242 rows
```

This bounds only the **fixed uncompressed raw CDN1 map design**; changing its persisted cost model requires a new predeclared oracle. An upper bound above 5% is not adoption evidence, while one below 5% can justify an early stop for the same population/design, subject to protocol review.

## Remaining blocking evidence

1. Source-backed real per-file D0 CSP patch-byte evidence on the exact PowerShell calibration membership, including complete CSP framing and independent target SHA verification.
2. Source-backed D3 *strict scanner* evidence for each base/target, with actual slot count, fail-closed scope reason and independently recovered target and base SHA-256.
3. Independent verification of the D0/scanner evidence artifacts referenced by the oracle's input; the current standalone script only validates their declared digests' **syntax**, not remote integrity.
4. An actual calibration-only run of the now tested upper-bound oracle, *before* inspecting any holdout patch sizes. The original SDK/Desktop evaluation population remains sealed.

**No empirical oracle result exists as of this record.** Production CSP v1, D15, PublicAPI and published packages remain unchanged.
