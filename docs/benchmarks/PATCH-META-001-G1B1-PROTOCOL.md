# PATCH-META-001 / G1B1 — original Roslyn source-lock and real create-stage preflight

Owner #220; freeze #251. Date: 2026-10-16.
Status: REAL SOURCE CALIBRATION ONLY, NO PRODUCT VERDICT.
Stack: G0 PR #367 -> G1A PR #368 -> G1B1. The PRs must be reviewed and merged in dependency order.

## Question and non-goals

Can the previously verified G1A lab attribution measure genuine immutable .NET release binaries with independent source locks and exact reconstruction, including reproducible run provenance? No production creator/CSM/CSP/API changes, no derived feature cache, no cache-on/off A/B and NO claim of a 30% benefit.

## Source preregistration

Source of independent literal SHA-256 and exact byte counts: docs/research/results/CORE011-CSM-001-G1J2C-20261012-001.md (archived G1J2C original evidence, selected before this experiment).

Official NuGet package ID Microsoft.CodeAnalysis.CSharp (MIT); versions 4.13.0 and 4.14.0; exact lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll from respective original NUPKG ZIPs.

| Release | Frozen package SHA-256 | Original DLL length | Frozen DLL SHA-256 |
| --- | --- | ---: | --- |
| 4.13.0 | 8f33bbff68fbacfaaee17b66e25861d888da8b0f9a524891fb2627f9af2683f3 | 6,604,072 | 7da8fb18fa6e19a166b35d87b731ecbb040c3ad2cc7894b19953384aee0f5fe4 |
| 4.14.0 | e4cce3dd791860b9300d6875eebd4d11749b5f0c166103e23a6912325828d496 | 6,697,264 | 9acdb971334046d22a07fad02648e2725ef5a1b9d0fbe4fe82f1ada57fa70c9e |

Use official HTTPS NuGet FlatContainer; allow only official NuGet CDN redirect. Hash packages before extracting. ZIP CRC, exact entry, size, source SHA and PE header must all pass. No fallback download, no accepted in-flight/new digest, no alteration to binary data. No source binary redistribution in CI artifacts.

## Procedure

- Two directions: 4.13.0 -> 4.14.0 and 4.14.0 -> 4.13.0. Dependent directions, not two independent software families. Prior exact Plan analysis found zero exact chunk reuse, which does not establish META usefulness.
- Five counterbalanced pair-order rounds per GitHub-hosted Linux x64 and Windows x64, ten genuine CSP creations and ten exact applies per platform. Each create/apply runs in a distinct OS process.
- Actual checked-out HEAD SHA recorded; default csp lane, H2-W2, original G1A opt-in stage profiling. All source digests and ManifestIds must agree with hashes and same-host repeated results.
- Source-identity SHA-256 is performed and charged even when the CSM is already present. Initial first pair misses CSM, later rounds hit. Do not label page-cache-warm as physical-disk-cold.
- Exact sequential re-chunk verification by the real applier, independently verified reconstructed output SHA-256 and a same-build, same-policy CSP SHA-256 transparency check under D7.
- Enforce finite/nonnegative nested timings, stream counters, full target read on this pinned zero-exact-reuse pair, physical CSP size/hash, and no success summary before all apply validations pass.
- Store all per-case raw stage JSON, source/provenance hashes, commit, RunId, host details, result status and raw artifact hashes. Do not upload original NuGet/DLL bytes or CSP payloads.

## Interpretation

G1B1 is an unsealed REAL_ROSLYN_CALIBRATION_NO_PRODUCT_VERDICT preflight, not a feature-cache experiment. The source-file SHA remains authoritative. The measured builder time also covers mandatory validation, compression and output, and cannot be counted as an avoidable feature fraction. G0 occurrence summaries are not consumed by the builder. Any green workflow is G1B1_REAL_CORPUS_MECHANICS_PASS only.

This is one .NET release family with five repeated rounds, not a representative real-product corpus and not an estimator of general significance. Never claim ADOPT or move the registry to evidence-ready from this smoke.

## Acceptance and next action

Both hosted OS jobs must pass pinned source retrieval, Python mutation/fail-closed tests, Release .NET compilation, all 10 exact create/apply verifications, deterministic same-build CSP/source identities and archived provenance. Any error gives FAIL and no summary.

Next G1B2: isolate actual consumed feature-building work inside the builder with opt-in internal statistics, establish instrumentation overhead, freeze independent application/PE/ELF/container families and at least five paired samples per supported platform. Determine avoidable work after mandatory exact verification and lookup costs. If its ceiling cannot reach #220's 30% savings gate, NO_GO/DEFER instead of inventing a public feature cache. Keep #251 release freeze and all public/wire semantics untouched.
