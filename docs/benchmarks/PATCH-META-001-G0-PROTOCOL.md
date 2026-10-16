# PATCH-META-001 / G0 — disposable derived-feature cache

ExperimentId: PATCH-META-001; owner #220; research freeze #251.
Date: 2026-10-16.
Status: **G0 lab mechanics only; NO PRODUCT VERDICT, NO RUNID, NO ADOPT**.
RFC-0005 is Proposed, not normative. This file is a preregistration for the mechanics tests and the future G1 design, not a change to any frozen protocol.

## 1. Hypothesis and precise ownership

Can repeated patch builds safely reuse features derived from authoritative CSM/content and thereby avoid expensive recomputation? No cached feature may establish source or target integrity. An absent, stale, corrupt or unknown-version record must trigger exactly the uncached derivation path.

G0 handles only first-occurrence and multiplicity summaries for an ordered, independently validated logical sequence of (ChunkId, Length) pairs. It does not read/verify CSM, consume actual PatchLab data, write/apply a CSP, prove cache-off/on CSP byte identity, or measure create speed. The caller must supply the authoritative identity and records; caller validation is an explicit precondition and must be tested at G1 integration.

PATCH-INCR-001 (#150) handles dirty-range exact rechunking, not repeated-feature reuse. PATCH-TREE-001 (#184) handles cross-file source semantics; this cache may later accelerate only its *derived* occurrence summaries. PATCH-EXEC-001 (#225) caches independently validated task results; META must not duplicate encoded-task caching.

PATCH-ENC-005 (#181) concluded REJECT / STOP_RESEMBLANCE: do not centre META on H6 sketches. Investigate existing consumed feature work first, particularly occurrence and location summaries (#184/#222), structural PE/ELF/container inventory (#183/#221), and compressibility hints only if there is measured create cost to avoid.

Related peer-reviewed motivation: SkySync, FAST '26, https://www.usenix.org/conference/fast26/presentation/zhang-zhihao . It reuses checksums and earlier search work in rsync/dsync; its reported improvements are **not** ChunkShift performance evidence and cannot be transplanted to PatchLab.

## 2. Keying by semantic dependencies

Do not use a universal opaque key. Distinguish domains:

- Ordered-manifest occurrence summary (G0): independently verified ManifestId, ProfileFingerprint, hash suite, canonical ordered logical-record SHA-256, record count, total bytes, extractor and schema/version.
- Per-content statistical feature (future): exact ChunkId, Length, hash suite, extractor id/version and sampling definition; no path/mtime as content identity.
- Whole-file structure (future): exact file-content digest/length, parser and transform version, and relevant parsing flags.
- Boundary-dependent feature: exact manifest/source identity and profile fingerprint.
- Encoder-sensitive feature: additionally include the exact encoder/BuildPolicy fingerprint/version. A policy-independent occurrence summary must not be invalidated solely by an unrelated encoder version.

G0's ordered-record digest hashes a binary concatenation of 32-byte chunk IDs and big-endian uint64 lengths in order. It is a lab cache-key component, **not** a replacement for CSM ManifestId or a new wire identity. Recomputing this digest scans the input record list; its cost must be counted in G1 as cache-lookup overhead, not hidden.

The record only becomes eligible for reuse after the authoritative manifest's integrity has been established independently. Neither filesystem metadata nor a sidecar signature supplies that proof.

## 3. G0 implementation boundary

Files: benchmarks/scripts/patch_meta_001_g0.py and test_patch_meta_001_g0.py, invoked only by .github/workflows/patch-meta-001-g0.yml.

- Advisory canonical entries: (ChunkId, Length, first ordinal, first byte offset, multiplicity), sorted by (ChunkId, Length). Duplicate IDs with different lengths remain separate keys.
- A canonical JSON record binds schema, full cache key and features using SHA-256; unknown schema/key, checksum mismatch, truncation, bad types, invalid bounds, noncanonical ordering and count mismatch are misses.
- Sidecar writes use a same-directory temp file, flush/fsync and atomic rename. Cache write failures do not fail the authoritative computation.
- G0 caps: 8 MiB record read/write, 25,000 distinct chunk keys, signed-64-bit total length. Oversized output is not cached; too many distinct keys return an explicit BYPASS result (no features), requiring the caller to use its authoritative uncached path. Such a result is never usable as a patch decision. These are NOT public/API/production limits. A sharded design may be investigated separately after opportunity profiling.
- SHA-256 detects accidental corruption. An attacker able to rewrite both body and checksum can forge a self-consistent entry; G0 has no authenticity claim. The existing exact source/target verification always remains mandatory. Future selection-changing features also require same-build transparency or an explicitly versioned deterministic policy.

No production code, CSM/CSP bytes or reader, ChunkId/ManifestId semantics, Core or Patching public API, BuildPolicy, default encoder, or release milestone is changed here.

## 4. G0 acceptance matrix: mechanics, not performance

1. Cold miss creates a canonical record; warm hit returns the same advisory data with byte-identical sidecar.
2. Changing ManifestId, ProfileFingerprint, hash suite or ordered records forces recompute.
3. Truncation, mutation, unsupported schema, malformed structure and oversized record force recompute.
4. Basic structural contradictions must be detected even when the outer digest has been recomputed.
5. Inputs are bounded; invalid hash IDs, zero/boolean/overflow lengths and excessive unique records safely BYPASS caching.
6. Linux and Windows GitHub-hosted Python 3.14 jobs pass; no extra NuGet or third-party Python dependency.
7. G0 is never labelled an end-to-end patch verification or performance run. A smoke result alone does not change registry status to RUNNING. A concrete retained RunId is needed first, per docs/research/README.md.

A pass means G0_MECHANICS_PASS only; it is not evidence of effective reuse in real create.

## 5. G1 preregistration requirements (not executed in G0)

G1 must **first profile the uncached production-equivalent lab create path** and identify a consumed derived feature that is expensive enough to cache. Use exact baseline identities: active PatchLab version, D15 default, PATCH-ENC-004 H2-W2 worker configuration, .NET runtime, CI host, manifest/CSP toolchain and corpus digest. Distinguish: manifest scanning/hashing, manifest read, candidate lookup, feature extraction, compression, patch I/O, verification, and source reads. Quantify baseline wall share f of extractable cost and the cache-lookup/hash overhead. Even removing all work in a stage of fraction f can save at most f of wall time, absent other effects (Amdahl). If the measured opportunity is below 30%, stop or choose a better feature; a faster unconsumed feature is not a builder speedup.

Freeze a multi-version corpus before tuning. Include repeated unchanged base with several targets, duplicated chunks, PE/.NET, ELF/container, high- and low-entropy/compressed classes and update sets. Separate unsealed calibration from sealed holdout and record source digests/provenance. Test cache-off, cold, warm, post-invalidation, and damaged/unknown-version fallback. On each supported GitHub-hosted platform use at least five counterbalanced measured paired rounds, with fresh processes and exact semantic Apply/reconstruction oracles; avoid treating page-cache-warm as physically cold I/O. A same-implementation/build/encoder/policy lane must compare CSP SHA-256 as a *non-normative* cache transparency oracle. Across backend/encoder versions, compare exact target bytes, logical identities and verdicts: PATCHING-DECISIONS D7 expressly does not freeze physical CSP bytes.

Retain RunId, commit, clean tree, OS/CPU, runtime and versions, per-file target/source identities, policy, corpus SHA, source reads, bytes rechunked/rehashed, trials avoided, feature read/write, retained metadata, cache hit/miss/invalidation/rebuild costs, CPU/wall p50/p95, memory allocation/residency, and full failure reasons. Measure end-to-end repeated-build economics at one, two, five and ten uses, including cold-write costs. The metadata denominator is the unique logical bytes represented by retained source identities, counted once; repeated reuse must not artificially inflate the denominator.

## 6. Frozen final decision gate from #220

ADOPT requires on decision-grade holdout **all** of: semantic equality to cache-off; same-build CSP equality if a transparency lane is declared; median repeated-create wall or CPU reduction at least 30% (or equivalently strong source-read reduction without material wall regression); derived metadata at most 2% of represented source bytes unless a stronger fully documented tradeoff; bounded resources; safe recomputation for corruption/staleness. Failure -> DEFER or REJECT with durable evidence; no public provider or semantic-format extension. A sidecar prototype passing seven unit-test groups cannot satisfy this gate.

## 7. Next allowed action

Review G0 CI and security/determinism assumptions. Then instrument **read-only stage-cost attribution** in a new lab-only G1 preflight on real PatchLab repeated-build pairs; preregister corpus and thresholds before creating an on/off feature-cache comparison. If no consumed expensive feature currently exists, record a measurable opportunity NO_GO and do not fabricate an optimizer. Do not freeze PATCH-EXEC task identities or promote features into persisted CSM/CSP semantics.
