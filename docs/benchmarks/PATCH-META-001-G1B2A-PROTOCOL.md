# PATCH-META-001 / G1B2A — consumed feature cost attribution

Issue #220; freeze #251. Date 2026-10-16. Stack on G0 #367, G1A #368 and G1B1 #370.
Status: **internal instrumentation calibration / NO_PRODUCT_VERDICT**. Not a cache A/B or an ADOPT claim.

## Question

G1B1 on original Roslyn DLL releases showed an opaque builder at ~9.8s Linux / ~11.2s Windows; that is not cacheable work. G1B2A isolates three observed operations without changing the patch builder algorithm:

1. Full base manifest read and authoritative verification plus BaseRecord array and distinct ChunkId set materialization; wall-clock stopwatch and record/unique-ID counts. Not all cacheable: source integrity verification remains mandatory.
2. Full target manifest read and authoritative verification; stopwatch. Not cacheable without a new integrity model.
3. Actual exhaustive-selector FindCandidateStarts per missing target entry, measured as aggregate per-worker stopwatch ticks, invocation count and returned candidate starts. The list order and subsequent candidate selection remain unchanged; this is the only potentially derived computation observed here.

Dictionary reads, candidate evaluation, zstd encoder calls, content hash verification, physical CSP writer and source-file SHA-256 are NOT isolated in G1B2A. In particular, even a large builder envelope does not imply a large derived-feature opportunity. Do not add these mandatory costs to any estimated saving.

## Null-path and invariants

The research-only nullable internal CspMetaResearchProbe is on internal CspCreateExecution. Public API and ordinary executions leave it null. Null candidate instrumentation directly invokes existing FindCandidateStarts: no Stopwatch calls or atomic probes. Opt-in lab mode is patch-lab one create with --lane csp --execution h2-w2 --meta-attribution JSON --meta-probe on. Control is --meta-probe off or omitted. The G1A observation emits nullable extra metrics; off values must be null, on finite/nonnegative with base records and positive candidate calls.

All actions still read and check canonical manifest, original content, dictionary candidates, emitted payload and final apply as before. Concurrent H2 workers atomically accumulate per-worker ticks. **Worker stopwatch sums can overlap: they are NOT a critical-path wall interval or a sound standalone upper bound on speedup.**

## Locked original corpus and testing

Original Microsoft.CodeAnalysis.CSharp 4.13.0 -> 4.14.0, exact official NuGet package and PE DLL SHA-256 pins already archived in CORE011-CSM-001/J2C and preregistered in G1B1. These are not synthetic bytes. A mismatched or unavailable release must fail; no replacement, digest re-pin or fallback.

Five alternating off/on rounds on each GitHub-hosted Linux and Windows host, ten independent real CSP creates and ten exact applies per host. First cold CSM round excluded from measured warm medians. Remaining four rounds produce four off and four on real samples. Each is a fresh process. Every stage must bind original source hash, correct physical CSP SHA-256, canonical CSM IDs, full exact reconstructed target SHA-256, and finite timing and stream counters. Physical CSP and manifest IDs must be identical for on vs off *within each platform/build/policy*. Original downloaded binaries and CSP contents are NOT uploaded to Actions artifacts; raw stage JSON, run provenance and summaries are archived.

## Acceptance and decision policy

Require both hosted jobs PASS, Release build + analyzers, fail-closed mutation tests, all ten exact apply/hash oracles per host, off=null / on positive candidate and manifest counters, unmodified CSP bytes. Mark G1B2A_MECHANICS_PASS / NO_PRODUCT_VERDICT; do not promote the experiment to ADOPT or claim 30% performance benefit.

The measured off/on create wall difference estimates instrumentation overhead plus noise, *not feature caching acceleration*. Four warm rounds per mode are diagnostic only. OS page cache, JIT, worker scheduling and zstd cost remain confounded. This is one related source pair, not independent corpus families or a sealed holdout.

Next G1B2B: separately attribute dictionary-trial and codec cost if possible, and distinguish reused data that is stable across builds from target-dependent per-patch work that cannot be cached. Expand to multiple unrelated original source-locked version families with a counterbalanced holdout. Only if a truly consumed, safely reusable expensive feature clears the net lookup/validation cost and approaches the issue #220 30% savings target should a derived-feature cache A/B be attempted. Preserve #251 research freeze and no public/CSM/CSP behavior change.
