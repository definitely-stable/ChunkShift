# CORE011-CSM-004 / E3-R0 — CSM reader LOH ownership preregistration

**Status:** PROSPECTIVE PREREGISTRATION / NO PRODUCT VERDICT (2026-10-14).
**ExperimentId:** CORE011-CSM-004. **Owner:** issue #154, umbrella #152.
**Scope:** reader-only E3-R0, *not* encoder/writer, E2 decode-once, E4 batching, E5 CRC, or previously closed E1.
**Machine contract:** benchmarks/experiments/core011-csm-e3-reader-contract.v1.json.
**Guard:** benchmarks/scripts/core011_csm_e3_guard.py and the dedicated GitHub-hosted preregistration workflow.

## Authority and historical status

CSM v1 is shipped. The frozen byte format and reader failure behavior in docs/architecture/CSM-V1-CANDIDATE.md take priority over a performance hypothesis. Existing CORE011-CSM-001 release closeout is **DEFER / no adoption** and remains immutable. G2-D ManifestId work is also **DEFER / NO_ADOPT**. The historical status of CORE011-CSM-004 stays DEFER. This R0 file is a prospective protocol only; neither reopens the old G1-J decision nor retroactively upgrades any earlier exploratory run.

Prior original evidence: G1-D source-correlated 147,528-byte sampled LOH allocation tick for one new 147,500-byte reader buffer per CSM reader; G1-E 165.5–165.7 KiB per Verify call for both 11- and 5052-chunk CSM; G1-G/H real compiled lazy/hybrid variants; G1-I two-host capped private pool alternatives. Use their checked-in source records and raw sample references, **not** their favourable medians as a fresh sealed holdout:
- docs/research/results/CORE011-CSM-001-LOH-20261012-001.md
- docs/research/results/CORE011-CSM-001-G1E-20261012-001.md
- docs/research/results/CORE011-CSM-001-G1H-20261012-001.md
- docs/research/results/CORE011-CSM-001-G1I-20261012-001.md
- docs/research/results/CORE011-CSM-001-CLOSEOUT-20261013-001.md
- docs/benchmarks/CORE011-CSM-001-G1J-QUALIFICATION-PROTOCOL.md

## Exact reader source and hypothesis

At R0 base commit b085e12edb288137c505d6ed43ad7251ec19b928,
src/ChunkShift/Manifest/CsmReader.cs has Git blob SHA1
ff56ccddaa0efc9970fd0b019e20523d6c2933d6.
The eager reader creates a 147,500-byte block buffer per reader even for empty/short manifests. There is one buffer per reader, not per CBLK. Formula: recordBytes = 16 + 24 + 36 * count + 4, with 1..4096 chunks, or 44 + 36 * count. A 4096-entry CBLK occupies 147,500 bytes. The 80 KiB experimental threshold is 81,920 bytes; the immediately adjacent valid one-block cases are 2274 entries / 81,908 bytes and 2275 entries / 81,944 bytes. A rented 147,500-byte array measured 262,144 bytes in pinned prior G1-I .NET runs; that is an *observed runtime bucket*, not a universal guaranteed capacity.

**Primary question A (short metadata):** does validation-after-header lazy exact-sized ownership materially reduce per-call allocations/Gen2 for short CSM without unacceptable CPU/latency/peak/idle memory regression?
**Primary question B (large/repeated metadata):** does 80-KiB-small-exact / private pooled-large cap2 reduce LOH/GC churn without excess peak/idle retention and without changing observed contract?
A and B may independently end in ADOPT, DEFER or REJECT **only after a separate decision-ready freeze and decision evidence**. A negative pool decision must not veto a separately qualifying lazy candidate.

## Registered candidate lanes

| Lane | Owner policy | Role |
| --- | --- | --- |
| eager | Existing max-sized 147500-byte owned buffer allocated during reader construction | Unchanged compiled Core baseline |
| lazy | Allocate exact record length only after bounded section-length validation, grow for later blocks, no pool | Primary A candidate |
| hybrid2 | Up to 81920 bytes exact owned; large CBLK uses private ArrayPool.Create(147500,2) | Primary B hypothesis, not selected winner |
| hybrid1 | Same hybrid, private large-bucket cap 1 | Diagnostic retention comparator |
| hybrid4 | Same hybrid, private large-bucket cap 4 | Diagnostic LOH-miss comparator |

G1-G/H/I candidate transformations can be reused for discovery **only** if an exact source anchor matches and each compiled variant's source SHA is recorded. Production src/ must not be modified in R0/R1 lab. No user-configurable size knob, API, runtime dependency or package asset change. cap1/2/4 limit cached returned arrays per bucket, *not* live outstanding rents or total process RSS. Do not claim cap2 wins from previous datasets.

Do not introduce a segmented alternative into the initial decision matrix without a separately reviewed prospective amendment and its own holdout.

## Frozen protected workload design

Reader boundary controls: 0, 1, 11, 102, 2274, 2275, 4095, 4096, 4097, 5052, 8193 chunks; verify actual physical CBLK count and record length, including 0 CBLKs and 4097/5052/8193 multi-CBLK cases. For both BLAKE3 and SHA-256 and BIDX enabled/disabled include MemoryStream, FileStream, nonseekable/fragmented/short-read. Do not fabricate many-entry inputs by claiming they were produced by CDC: synthetic canonical CSM controls and genuinely writer-produced content-derived CSM must be labelled separately.

A 384-MiB generated physical CSM/5052-chunk/two-CBLK control is reusable by digest, but it is not a new independent holdout. Add a separately frozen, provenance-reviewed real-versioned mixed-reuse multi-CBLK manifest/Plan corpus before any *product* decision. Freeze byte-level source/CSM hash, ManifestId, FileDigest, suite, profile, BIDX, count, block counts, license/redistribution and builder procedure. In manifest-only consumer processes source content must neither be generated nor opened. Use an independent builder OS process, then a fresh consumer OS process per candidate, class and replicate.

Required public paths: ChunkManifest.VerifyManifestAsync; full ManifestReader enumeration including end-of-stream and VerificationResult; and actual ChunkPatch.PlanAsync over pairs of CSM. Attribute reader costs separately from whole-product costs; only whole-product measurements support product benefit. Protect CreateAsync golden output as a semantic control, but writer-side memory improvements are an *independent future E3 writer slice*, not reader R0 decision evidence.

Concurrency 1/4/16 with true observed maximum simultaneous readers; 64 is stress only after runner feasibility, never silently treated as 64 CPUs. Include repeated thousands of short and medium manifests, sustained large, and long-lived partially enumerated readers. Observe normal disposal, disposal twice, cancellation before/during payload, truncation, exception-throwing streams, CRC corruption in first and second CBLK, malformed count/reserved/offset/length, BIDX mismatch and overflow.

## Invariants that outrank performance

- Only validate and allocate from a bounded CBLK PayloadLength, never an unchecked attacker-supplied length; retain integer-overflow/unsupported-length exception classes and stream count checks.
- Entire CBLK is read, CRC-checked and every entry semantically validated before exposing entries from that block. With a corrupt second block, at most 4096 already validated prior entries may be observed; do not allow later block entries. Preserve corruption flags, ManifestId accumulation, totals, physical digest and exception ordering. A CRC mismatch cannot cause a premature skip of later required verification.
- Return a rented buffer exactly once and only after all asynchronous uses cease. Reader Dispose is not synchronised with an in-flight ReadAsync in the existing API; follow the existing caller contract and test cancellation/fault paths rather than inventing a different API contract.
- Clear the full rented capacity on return and serialize only initialized active spans. Keep source stream caller-owned, CsmInput read-ahead at 4096 bytes, E2 decode/conversion, E4 256-entry batch, and E5 CRC logic unchanged.
- Preserve exact CSM golden vectors, byte-by-byte create output, ChunkId/ManifestId/FileDigest, CRC failure category, public API, .NET 8/10 supported semantics and JIT/NativeAOT x64/ARM64 package compatibility.

## Execution and statistical rules

**R0 is protocol-only.** Its machine guard recognises only stage preregistration, permits no decision runs and requires every numerical budget/corpus-freeze field to be null. Moving beyond R0 requires an independent reviewed change to both the validator and JSON after baseline-only calibration; changing JSON alone must fail closed.

Subsequent R1 candidate implementation: detached checked-out Git worktrees from a single recorded checkout, compiled distinct Core binaries, positive and deliberately corrupted oracles, candidate SHA and CPU runtime variables recorded. Research smoke is not production ADOPT.

R2 baseline-only calibration separately estimates physical memory/GC plateau, cold/warm tiering, timestamp/scheduler noise, independent host drift, the smallest meaningful batch, concurrency and 0/30/300-second natural idle. Before R3, freeze numerical budgets (CPU and end-to-end wall ratios; noninferiority confidence; throughput/latency; allocated bytes/call; Gen2; GC pause distribution; incremental peak and idle working set, private bytes, managed heap/LOH; total memory bound and retention plateau), paired randomized host/block order, sample count, ex ante exclusion policy, negative controls, minimum independent blocks and experimental SHA. Thresholds must not be inferred retroactively from observed candidate wins.

R3 once-only independent sealed decision workload: at least four independently allocated GitHub-hosted CPU blocks, plus Windows x64 and supported NativeAOT/ARM64 evidence where applicable. Compare within-host paired baseline/candidate and repeated baseline; partition by actual hardware/JIT/PGO regime, never average incomparable absolute times. Resample independent host/process blocks, not concurrent calls as independent runs. Record raw per-call and process-batch samples; RSS/private bytes and GC.GetTotalPauseDuration independently, not stale last-GC LOH snapshots as LOH allocation counts. Measurements, failures and artifacts require RunId and permanent summary/EvidenceId in the registry.

Decision precedence: wrong bytes, missing fail-closed/resource bound, changed exceptions/entry exposure or hard memory budget violation -> NO_ADOPT and REJECT the affected candidate; missing corpus, platforms, locked budgets or inconclusive independent CPU/memory coverage -> DEFER; ADOPT only if every primary protected stratum passes preregistered benefit *and* noninferiority gates. Retained static pooling after reader disposal is not inherently a leak; distinguish managed-heap retention, private bytes, peak RSS and natural idle trim.

## Next slices / exit

- E3-R0 (this PR): register prospective experiment, strict fail-closed validation and mutation tests; CI green; zero product source changes, no RunId or EvidenceId invented.
- E3-R1: source-pinned lazy/hybrid compile/test only and new independent positive/negative oracles; no product verdict.
- E3-R2: baseline-only numeric budget calibration and reviewed protocol amendment with evidence.
- E3-R3: independent decision-ready freeze followed by locked public-path qualification; explicit A/B dispositions.
- E3-R4: only after ADOPT, minimal reviewable production reader PR and release-compatibility gates. Writer/encoder follows its own prospective subprotocol and must not be represented as covered here.

Historical release closeout and main branch remain unchanged by this protocol PR.
