# PATCH-META-001 / G1A — real CSP stage-mechanics preflight

ExperimentId: PATCH-META-001. Issue #220; freeze #251; stack on G0 PR #367.
Date: 2026-10-16. Status: **synthetic calibration/mechanics only, NO_PRODUCT_VERDICT**.

## Hypothesis / limitations

Instrument the existing, genuine PatchLab CSP creation without changing the production encoder, format, API or default. Record elapsed wall and process CPU for the real builder call and isolate the two source SHA-256 / CSM ensure stages, plus post-create CSP SHA-256. This is a *stage accounting* experiment, not evidence of accelerated repeated create.

Builder internals (codec, candidate search, entropy, transform classification) remain opaque in G1A; the builder bucket is not equivalent to potentially avoidable derived-feature work. G0's occurrence-summary cache is **not** consumed by this path. Real feature reuse is not enabled here.

## Frozen instrumentation

Opt-in laboratory command: patch-lab one create with --lane csp --execution h2-w2 --meta-attribution JSON_OUTPUT. Ordinary patch-lab invocations remain unaffected.

PatchLabManifests.EnsureAsync receives optional probe and continues to hash the entire source on every invocation, including CSM cache hits. Distinguish SHA-256 cost from an actual CSM rebuild, which occurs only when the digest-derived CSM is missing. The content digest remains authoritative: no pathname/mtime metadata shortcut.

CspPatchBuilder.CreateAsync receives read-only counting stream wrappers on base and target manifest/content, and its already-existing CspCreateStatistics. Record builder call elapsed wall, total process CPU, managed allocation, source bytes read and seeks, manifest bytes read, and result ManifestIds. The wrapper never disposes the underlying stream.

After closing the produced CSP, independently hash its bytes. Record this additional read separately: it is measurement overhead, not a production-create requirement. Output stage JSON only after the builder and CSP hash succeed.

## Pilot cohort (synthetic, never decision-grade)

A deterministic SHAKE-256 generator pins three 512-KiB versions, one common base and two target edits: 32 KiB changed in v2; 16 KiB in v3. Two pair definitions have source SHA-256 and locked pairs.json digest. The generator refuses an existing corpus path.

A dedicated initially-empty work directory yields first-pair CSM cold build; later repeated rounds reuse the same CSM. Run 2-3 alternating pair-order smoke rounds on GitHub-hosted Linux x64 and Windows x64. Each measured action runs in a new dotnet process and creates a real CSP, applies that CSP with exact sequential re-chunk verification, independently verifies the reconstructed SHA-256 against target, and verifies same-build per-pair CSP byte transparency. Patch SHA equality is a same-build experimental oracle under D7, NOT a compatibility contract.

Require complete manifest/source identities, bounded nonnegative finite stage timers, real CSP file SHA/size, consistent stage accounting and cache-hit flags. Failures must suppress success summary. Upload raw observations and the self-contained summary. No product efficacy claim can be made from these two or three synthetic rounds.

## Metrics and exclusions

Raw JSON retains exact policy, content SHA-256, ManifestIds, source bytes, CSM cache flags, base/target digest and build wall, builder wall/CPU/managed allocations, base/target data read calls/bytes/seeks, manifest bytes read, CSP SHA-256/length, independently timed post-create hashing, total wall and process peak working set.

These are NOT physical cold-disk read measurements, independent per-stage peak memory, real-product timings, a representative CPU profile, hidden inner encoder timings, proof that hash cost is eliminable, or a comparison against a META cache. The separately timed SHA-256 over produced CSP increases the total G1A observed wall. Any claim of 30% META speedup from this synthetic pilot would be invalid.

## Smoke acceptance

- Both hosted platforms pass all driver unit tests, build Release, and create/apply every frozen pair in every round.
- Every source digest and patch SHA matches independent Python hashes; ManifestIds and same-build per-pair CSP hashes stable.
- First pair has CSM misses and subsequent rounds are CSM hits; all observations have status STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT and summary has SYNTHETIC_STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT.
- CI success means G1A_MECHANICS_PASS, not ADOPT. A retained performance RunId and real multi-version corpus are required to move the ExperimentId to RUNNING.

## Next gate: G1B real cost and feature discovery

Before any META cache-on/off A/B, use pinned original application, .NET/PE, ELF/container and update-set pairs to measure ≥5 counterbalanced fresh-process rounds per supported host. Show the cost of a genuinely *consumed* derived feature and the cost of lookup/revalidation separately. Do not center the architecture on H6 after PATCH-ENC-005 STOP_RESEMBLANCE. If measured avoidable work cannot plausibly reach the 30% repeated-create threshold of #220 after lookup overhead, explicitly DEFER/NO_GO instead of introducing public metadata interfaces. Cache/CSM authoritative semantics, physical CSP D7, final apply and Research Freeze #251 remain unchanged.
