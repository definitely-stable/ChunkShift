# PATCH-META-001 — original G1B2B consumed-codec evidence and first-release DEFER

**EvidenceId:** `PATCH-META-001/G1B2B-FIRST-RELEASE-DEFER-20261105-001`. **ExperimentId:** `PATCH-META-001`. Date: **2026-11-05**. Owners [#220](https://github.com/definitely-stable/ChunkShift/issues/220), [#251](https://github.com/definitely-stable/ChunkShift/issues/251).

**First-release decision: `DEFER` introducing any rebuildable PatchFeatures cache or public/provider schema. Retain existing CSP v1 and create behavior.** This is an *evidence-based decision not to adopt an unproven optimization for the first public release*, **NOT** a general rejection of metadata caching on unseen workloads. Existing G0→G1B2B lab prototypes remain stacked research PRs, not product code.

## Original source-locked calibration and independently downloaded evidence

- Source: exact official NuGet `Microsoft.CodeAnalysis.CSharp` 4.13.0→4.14.0, `lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll`, source SHA-256 **`7da8fb18fa6e19a166b35d87b731ecbb040c3ad2cc7894b19953384aee0f5fe4`** → **`9acdb971334046d22a07fad02648e2725ef5a1b9d0fbe4fe82f1ada57fa70c9e`**. Exact original package digests and host-specific generated corpus-manifest digests are retained in the [machine ledger](../../../benchmarks/experiments/patch-meta-001-g1b2b-original-evidence-20261105.json); the source content is the same on both platforms, although generated corpus `pairsSha256` differs by hosted environment.
- GitHub-hosted [run #41953519062](https://github.com/definitely-stable/ChunkShift/actions/runs/41953519062), first attempt **SUCCESS** on Linux x64 and Windows x64, from research head `7dea7c722bfeefd3871ff051903dc62d3eec3881`, [stacked PR #445](https://github.com/definitely-stable/ChunkShift/pull/445). Draft dependencies #372→#370→#368→#367 are **not merged into main**.
- Original Linux artifact [#13382387028](https://github.com/definitely-stable/ChunkShift/actions/runs/41953519062/artifacts/13382387028), **15,531 B ZIP**, SHA-256 `77a7aa4d8dde8544c3914f6279c61d0fd61e368603205b72421b2f85334e291e`.
- Original Windows artifact [#13382636333](https://github.com/definitely-stable/ChunkShift/actions/runs/41953519062/artifacts/13382636333), **15,503 B ZIP**, SHA-256 `cefd98578688bfd4961a80324c17e9a2cbba01d987f3359a3a26a5666483b5c7`.
- Both ZIPs were independently downloaded, original archive SHA-256 and all 12 member CRCs passed. All **20/20 original per-stage JSON** were independently cross-checked against their SHA-256, create/apply summary and same-build CSP SHA; all medians recomputed from the four post-cold warm on/off rounds. Original actions generated ten physical CSP creates and **ten independently SHA-checked exact applies per OS**, retaining no modified source binaries or physical CSP payload files.
- All 20 measured creations produced the same CSP physical SHA-256 on both platforms and both probe modes: **`842b8f8904556e0793e1a788058f33755bdc891eb17fe51689a91a878997f0b6`**. This is a same-build research transparency check, not a cross-backend D7 compatibility requirement.

The durable machine ledger retains **all 20 original stage SHA-256 fingerprints**, ordered round/probe labels, wall/CPU times and individual consumed codec/read counters, not just favorable medians. Original ZIP retention is **30 days**; after expiration only the compact projection, original checksums and provenance persist, not full original stage JSON. No independent binary patch apply was done from the archive; exact apply was performed by the producer and its logs and source/csp identities separately audited.

## Original G1B2B exact measurements (four warm pairs after cold round)

| Metric | GitHub-hosted Linux x64 | GitHub-hosted Windows x64 |
| --- | ---: | ---: |
| Builder wall median, probe OFF | **11.531257 s** | **9.753968 s** |
| Builder wall median, probe ON | **11.529939 s** | **9.789707 s** |
| Candidate starts (G1B2A) per create | 464 / 81 chooser calls | 464 / 81 chooser calls |
| Consumed plain zstd trials | 81 | 81 |
| Consumed dictionary zstd trials | 464 | 464 |
| Plain zstd worker-sum median | **2.767063 s** | **2.194737 s** |
| Dictionary zstd worker-sum median | **19.781644 s** | **16.920458 s** |
| Candidate dictionary ReadAsync worker-sum median | **0.041802 s** | **0.059981 s** |
| Dictionary bytes physically requested per create | **164,725,443** | **164,725,443** |

There is substantial **target-dependent zstd work**: 81 standalone frames and 464 dictionary-trial frames; dictionary/read and frame counts are numerically identical across hosts. **Do not sum codec worker times and divide by builder wall as an exclusive fraction or a causal critical-path upper bound**: H2-W2 workers run concurrently. Differences of OFF/ON wall medians are not attributable to probe overhead or cache benefits, and G1B2B **does not use a cache at all**.

G1B2A previously measured actual candidate-start enumeration as **0.573 ms (Linux) / 0.843 ms (Windows)** worker sum against ~9–12 s builder wall, i.e. about **0.006–0.007%** descriptively on that release pair. G1B2B rules out the fallacy that the remaining builder cost is automatically reusable metadata. Dictionary physical bytes must still be verified against canonical manifest and target content; zstd frames depend on **target data**, so their CPU cannot count as a simple durable base-side occurrence-summary cache saving. Whole task-result memoization is different (#225).

## Scope of decision and future research gate

Issue #220 proposes >=**30% median repeated-create wall/CPU reduction** (or sufficiently strong safely sourced read reduction), <=**2%** sidecar/source size, exact semantic equivalence and corruption/miss fallback. G0 demonstrates disposable sidecar mechanics but is not integrated into CSP builder; G1A/G1B1 are original stage preflights; G1B2A found minuscule consumed candidate-index work; G1B2B found large target-dependent codec work, **not** a safely reusable derived feature. **No actual cache A/B on multiple independent families was performed**. Therefore there is **no admission-grade evidence for ADOPT**, and it is rational to **DEFER** the optional feature-cache representation for the first public release rather than freeze a new persisted metadata or public strategy API.

The first-release DEFER does not claim universal cache inefficacy or forbid later research. To reopen: preregister a *demonstrably expensive and safely reusable* derived operation on independent product families, enforce exact ManifestId/profile/algorithm/build policy binding, checksum and corrupt/stale/missing fallbacks, then run cold/warm/off multi-version A/B and evaluate the exact 30%/2% gates without counting mandatory source integrity, zstd target-dependent frames or whole-task caching as free feature reuse.

**Release implication:** retain existing CSP v1, CSM authoritative semantics, default workers and internal-only PatchLab. No public cache or feature format, no encoder change. #251 remains open for #221/#222/#223/#224/#225 and the final first-public contract synthesis. The original stacked PR #445 remains a research branch, not merged product code.
