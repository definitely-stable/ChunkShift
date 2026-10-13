# CORE011-CSM-002 / G2-C — immutable public-path source-variant A/B pilot

Status: **FROZEN EXPLORATORY FOUNDATION / PRODUCT ADOPT PROHIBITED**.
Owner: [#154](https://github.com/definitely-stable/ChunkShift/issues/154), parent #152.
Predecessor: [G2-B independent original hosted evidence](../research/results/CORE011-CSM-002-G2B-20261013-001.md), merged PR #336.
Baseline before G2-C: `main@c49685fbf9bb4a9eafd183339d043ed20b53975e`.
Existing release 0.1.1 status: `CORE011-CSM-002=DEFER`, **not** replaced by this experiment.

## Hypothesis / why this experiment exists

G2-B measured a bounded batching micro-gain for both suites, at the cost of additional per-accumulator allocations of ~2.5KiB at 64 entries or ~9.4KiB at 256. G2-B did *not* measure `ChunkManifest.CreateAsync`, `ChunkManifest.VerifyManifestAsync`, or `ManifestReader` with distinct compiled Core implementations. This is a prerequisite implementation for a realistic full-operation A/B qualification, **not** approval to adopt batching.

Do not compare wall-time of unrelated CSM/create or standalone hash calls as additive CPU fractions. Hash-suite, physical file bytes, version, supported target and file-source I/O must match.

## Source transforms — fail-closed, no production commit

A GitHub-hosted job checks out **the identical frozen comparison HEAD** into three **detached** temporary worktrees, builds the real Core and benchmark executable for each, and records compiled target / runtime / SHA provenance:

- **baseline:** unmodified `ManifestIdAccumulator`;
- **stage64:** private per-accumulator 64×36=2,304-byte staging array; flush after 64 valid records and before final totals;
- **stage256:** private 256×36=9,216-byte staging array; same rules.

Only `src/ChunkShift/Manifest/ManifestIdAccumulator.cs` is rewritten in *temporary detached worktrees*. The exact pristine Git blob **`894c61b68bfba111467f061e8a0caf81bc8eea81`**, fixed source anchors and clean/unattached worktree checks fail closed. The tracked repository `src/` and published NuGet/API are unchanged. A SHA-256 of each real transformed source plus the original is in the run receipt. No global pool or user-selectable production toggle. Source code transformations are reviewed in `benchmarks/scripts/core011_csm_g2c_variants.py` and are explicitly research-only. The full experimental source is compiled, not simulated or manually calculated timings.

Important semantic caveat: valid finalized byte streams are identical by construction; exceptional overflow/fault intermediate hasher state may differ because a batch is pending. Tests of malformed CSM, CRC, cancellation, disposed readers and failure ordering must pass before any production discussion. This laboratory experiment does not establish complete side-effect equivalence under all failures.

## Immutable non-decision hosted pilot

Exact runner and fixture protocol, frozen before first measurements:

- GitHub-hosted Ubuntu 24.04 x64, .NET 10 benchmark tool (Core regression projects target net8/net10); source state/OS/runtime in run receipts; one independent runner allocation **not** a release decision sample.
- **Two deterministic source-file control cases**, generated **once** by a separate original-baseline process:
  - 1 MiB, shipped stable FastCDC profile, BLAKE3, BIDX on;
  - 8 MiB, same profile, SHA-256, BIDX off.
  Identical file source bytes + physical reference CSM and expected public identities for every variant, no source reconstruction by timed Verify/Enumerate processes. Fixture builder records source SHA256, manifest SHA256, ManifestId, FileDigest, ChunkCount and physical bytes. Synthetic data explicitly labelled synthetic. **These do not constitute real versioned pairs or qualifying multi-CBLK workloads.**
- Three operations per case: public `ChunkManifest.CreateAsync` from the original on-disk content into a preallocated MemoryStream (same physical CSM bytes, identifiers, FileDigest); public manifest-only `ChunkManifest.VerifyManifestAsync` on an in-memory physical reference CSM; public full `ManifestReader` enumeration with final verification and contiguity checks. Each operation in a fresh consumer process, separate from fixture builder.
- Within each operation 2 measured rounds × 3 full calls after warmup. Order on first case baseline→stage64→stage256→baseline; second case baseline→stage256→stage64→baseline. This records *baseline drift*, not a statistical cross-host confidence bound.
- Capture wall, process CPU, process allocations, GC pause/counts, current/peak working set and private bytes, post-GC managed heap/LOH. Measurements include public operation lifecycle and validation; process allocations include harness checks, so absolute CPU/byte attribution is limited.
- Source-only original fixtures **never archived** as executable/raw content; archival artifact includes small source-free JSON, SHA, toolchain, and samples. A result guard rejects missing, duplicate, nonfinite or identity-disagreeing operations.
- CI includes original Core writer/reader/ManifestId tests against both compiled target frameworks for each source variant. All golden bytes and early invalid CBLK exposure protections must remain intact.

**Pilot result**: accept only **EXPLORATORY_CORRECTNESS_PASS** or **FAIL/DEFER**; even favourable ratios are descriptive. No hidden retuning of batch widths or dataset after observing timings. `core011-csm-g1j-contract.v1.json` retains `decisionRunsAllowed=false` and `budgetsLocked=false`.

## Mandatory G2-C qualification after pilot

G2-C pilot is not enough for product adoption. Further steps must independently freeze:

1. Release CPU non-inferiority/benefit budgets and acceptable short-request incremental allocated bytes/peak RSS/idle retained memory, independently of decision samples; variance and minimum runner count.
2. Broader real versioned multi-CBLK mixed-reuse and short-manifest SHA-pinned corpus; .NET 8 and 10, Linux x64/ARM64 and Windows x64, NativeAOT where supported.
3. End-to-end *paired* fresh-process variants, calibrated warm and cold, 1/4/16 concurrency, Gen2 and pause durations; record all failures.
4. Byte-for-byte CSM, canonical identities, decoder/corruption/cancellation/exception ordering, partial read and stream ownership across both suites/BIDX; preserve old public API.

Do not merge any experimental `src/` changes to main until a distinct reviewed, properly powered G2-C product qualification returns ADOPT. Reject or defer on weak performance, unrepresentative corpus, allocation regression or insufficient coverage. The old 0.1.1 closeout stays authoritative.
