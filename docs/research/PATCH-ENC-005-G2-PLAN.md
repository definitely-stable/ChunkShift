# PATCH-ENC-005 G2 execution plan

Status: **SAMPLE LOCK CANDIDATE**

Owning issue: #181  
Parent: #7  
Frozen protocol: `docs/benchmarks/PATCH-ENC-005-PROTOCOL.md` at `96fd9b296d6998cac397e61041f22df51e6dd43c`

## Purpose

G2 is the frozen exact sampled whole-base byte oracle that follows the Phase-A
`REJECT / no finalists` verdict. It is not a production selector, is not timed
for adoption, does not change D15, and must not open H5/H6 until the §4.3 gate
has been evaluated.

The implementation order is intentionally split so sample membership is locked
before any oracle cost on the real calibration sample can be inspected.

## Slice A — foundation (this branch)

Implement only reusable mechanics:

1. reuse production H0 dictionary-window semantics:
   - K=4;
   - 1 MiB dictionary bound;
   - REF32 cost accounting;
   - production nearest-first H0 candidate enumeration;
   - strict `<` winner replacement;
2. add deterministic G2 sample-key and 64-per-pair selection;
3. add exact whole-base candidate order:
   absolute offset distance, then lower base-record index;
4. add research-only `patch-lab enc005-g2 sample` and
   `patch-lab enc005-g2 oracle` commands;
5. emit the separate frozen `chunkshift.patch-g2-oracle.v1` schema;
6. add deterministic unit tests for key bytes, sample cap/order, K4/1 MiB
   semantics, H0 parity, whole-base ordering and REF32 accounting;
7. add a manual GitHub-hosted sample-lock workflow.

No real G2 oracle run is allowed in this slice.

## Slice B — sample lock

From the merged foundation commit:

1. materialize the frozen development corpus;
2. run only `enc005-g2 sample`;
3. commit the canonical sample document and its `oracleSampleSha256`;
4. review membership, pair counts and provenance;
5. merge the sample lock before any real `enc005-g2 oracle` invocation.

The sample document is derived only from the frozen selection algorithm:
calibration families, distinct missing production entries, six-field NUL-
separated SHA-256 key, digest/path/index order and first 64 entries per pair.
For the canonical document, the four pair blocks are ordered ordinally by
`familyId`, then `baseVersion`, then `targetVersion`; this affects only
document order / `oracleSampleSha256`, never per-pair membership. Measured
oracle costs cannot affect membership.

The sample document's `sourceCommit` binds the commit that materialized the lock. The later oracle artifact binds its own post-sample-lock execution commit; those SHAs are not required to be equal. Membership is carried across that boundary by exact row recomputation plus `oracleSampleSha256`.

## Locked sample candidate

The sample-only workflow run `37143841107` on merged main
`d406a90fdf8d7fc7944975b7f1667129a8ab5df9` produced the canonical lock
candidate documented in
`docs/research/results/PATCH-ENC-005-G2-SAMPLE-20261003-001.md`.

- rows: 256 = 4 × 64;
- sample file SHA-256:
  `4813e90d3860891a3d6766ba4dd6b0d4c0f0e0941ebe2bda0e1d638a9e4a2208`;
- `oracleSampleSha256`:
  `54061f4efe0969af0d772ddbc6d026fdf58bd4f2776514a9fb098753d062001d`.

No real oracle cost was computed while materializing, validating or committing
the lock candidate. Slice C remains forbidden until the lock PR merges.

## Slice C — G2 decision evidence

After the sample lock merges:

1. run the exact oracle on ordinary GitHub-hosted `ubuntu-24.04`;
2. bind `runId`, protocol/source commit, dataset SHA and
   `oracleSampleSha256`;
3. retain the complete `chunkshift.patch-g2-oracle.v1` artifact;
4. independently recompute:
   - `H0sample`;
   - `G2sample`;
   - four pair ratios;
   - the §4.3 gate;
5. commit durable evidence before opening any downstream selector work.

Main gate PASS requires both:

- `G2sample <= 0.95 * H0sample`;
- at least 2/4 calibration pairs have
  `G2pair <= 0.97 * H0pair`.

## Branching after G2

If the main G2 gate passes, full Phase B H5-F/H6-O/H6-P may start.

If it misses, only the already-frozen `H6-O12-SF3-S128` false-negative guard
may run on the fixed evaluation split. H5-F, H6-P and H8 remain forbidden until
that guard passes.

If the guard also misses, record H5-F/H6-P/H8 as STOPPED and retain D15.

## Explicit non-goals

This work must not:

- add G2 to `CspCandidateSelection`;
- change `CspEncoderPolicy.Default`;
- change CSP v1 or public API;
- tune H4/H7/H9 after observing Phase-A results;
- inspect fresh-confirmation content;
- require self-hosted runners;
- run H5/H6 in the foundation or sample-lock slices.
