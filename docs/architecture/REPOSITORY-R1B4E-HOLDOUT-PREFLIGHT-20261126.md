# R1-B4e.5 — REPO-INGEST-003 holdout preflight

Status: acquisition (#493) and source/rule lock plus geometry (#494) ACCEPTED; actual D/C qualification candidate. No decision-bearing measurements. Refs #133 #144 #139 #10.
Predecessors REPO-INGEST-001 and 002 reviewed, C leading candidate, default DEFER.
This is a distinct experiment. Holdout may not start until source manifest
and rule are reviewed and committed, harness qualified and actual corpus
geometry checked without timing. No claim of blind data or general production
readiness.

## Fixed selection (before any timings)

Use official .NET 8.0.0 and 8.0.1 Windows x64 ZIP and Linux x64 tar.gz
runtime release assets. Old versions are intentional static byte inputs,
never installed or executed. Fetch official release metadata and verify SHA512
of each archive against its release entry. Freeze exact URL, archive digest,
length, derived corpus SHA256 and member inventory in a reviewed lock after
acquisition. If upstream changes/unavailable or cap fails, STOP; no silent
substitution. Selection is narrow (.NET binaries), not representative of
all file types. No candidate runs on acquisition job.

- win-versioned: concatenate sorted regular files under
  shared/Microsoft.NETCore.App/<version>/ for 8.0.0 then 8.0.1.
- linux-versioned: same recipe for Linux ELF and managed binaries.
- compressed: concatenate the two original Windows ZIP byte streams.
- adverse-repeat: 4 MiB prefix of win-versioned, 40 MiB prefix of
  linux-versioned, then ten repeats of the 4 MiB prefix. 84 MiB total.
  Deliberately constructed from real bytes, not a natural trace. At 32 MiB
  target, exercise separated and recent reuse; actual CDC hits must be
  checked rather than assumed. If not observed, report coverage gap and
  DEFER; no timed parameter tuning.

Unframed concatenation tests single-stream ingestion, not multi-file
manifest semantics. File ordering is ordinal UTF-8 name order; no extraction
to disk paths, no archive code execution. Bounds: each archive <=128 MiB,
each versioned output <=256 MiB, each selected member <=128 MiB, <=4096
selected regular entries/archive. Record exact output size and SHA256,
archive URL/version/rid/hash and member name/size/SHA256. Inputs can be
regenerated on any runner with byte equality checked before timing. Output
outside checkout. Preparation only, no production/API changes.

## Decision rule for pre-measurement lock review

Candidate C vs accepted D; A stays historical reference. Same source file,
suite, limits, final pack geometry, byte verification, independent restore
and rebuild, cleanup. 32 MiB target, same CDC profile, one warmup plus six
measured rounds alternating DC/CD. Windows x64, Linux x64, Linux ARM64,
two separate hosted jobs per platform (six independent runner instances).
Four workloads, both suites, two candidates, 112 raw/96 measured per lane;
672 raw/576 measured overall. Do not pool unrelated machines into a pair.
Check actual CDC ratio and active/closed coverage before timings; adverse
case must exercise both. No forced duplicate expectation on compressed data.

Primary target = equal-weight geometric mean of paired median D/C Ready
speedups over three real-artifact workloads for each suite and runner.
Require >=1.10 for every runner/suite; guardrail every individual workload
median C/D Ready <=1.05 (including adverse). CPU, allocation and peak
working-set median C/D <=1.05 per cell. Restore/rebuild median C/D <=1.10
per cell, cleanup report separately. Each median computed from within-round
ratios with full precision (not ratio of independent medians); no rounded
decision, post-hoc exclusions or best-of-runs. Zero/invalid denominators,
missing evidence or correctness/resource failure STOP; valid but below
target or guardrail => DEFER and targeted profiling; all gates pass =>
performance eligibility only, not publication or durability ADOPT. Repeat
independent runner instances must each pass; no averaging away failures.
Thresholds are a proposed engineering rule, not statistical significance.
Exact source identities and rule are in tools/repository-ingest-lab/holdout-lock.json; require acceptance before timed holdout. Invalid/zero denominators STOP; no threshold changes after results.
No p95/p99 claims from six pairs; record all raw, variability, runtime,
runner image, file size, source provenance, exact HEAD and RunIds. Separate
R1-C mandatory before production default; large-source scaling beyond
256 MiB and multi-file ingestion remain future tests.

## Next stages

1. Run acquisition-only hosted preflight, inspect original manifest/hash.
2. Seal source/derived SHA256 and final rule in a separate reviewed commit.
3. Adapt harness for external locked files (not generator), verify arbitrary
   duplicate coverage and all candidate accounting, qualify smoke across OS.
4. Only then launch timed holdout; audit artifacts independently.
No holdout measurement on this PR. If source acquisition fails, diagnose
without changing decision thresholds or selecting based on timings.

## Acquisition evidence and replay

#493 ACCEPT/MERGED 68b4ab3e88f26667c145525ce008a64cb2596538, acquisition head 88533db86b585f75710dd523bcfe6706637cb2e2; run 45721453289, artifact 15072681600, ZIP SHA256 40c8d55e77637bbdc92e4529a121b71a49c67056e332f6de509032ae29d038ef. CI 45721453029, Heavy 45721453023, CodeQL 45721453297 PASS; five acquisition tests PASS. Independently checked archive digest, ZIP CRC, all three inventory checksums, exact commit, four URL/SHA512 pairs against archived release metadata, 736 member entries and aggregate sizes. No raw binaries retained, so do not claim independent payload regeneration yet. Total derived corpus bytes 444558956 (423.964 MiB). Lock stores exact archive lengths/SHA512, member count and total bytes, SHA256 of canonical UTF-8 JSON member inventory (sorted object keys, compact separators, ensure_ascii=false), and four exact derived source SHA256. Full member list is in original artifact; replay preserves and verifies it against the compact commitment. Locked replay does not fetch mutable release metadata; provenance binds the previously archived metadata digest. No source substitutions. New geometry-only executable uses actual Core scanner and accepted IncrementalRawPackAdmission to model first-placement and active/closed hits on all four corpora and two suites, on Windows x64/Linux x64/Linux ARM64. No candidate pack write, comparison or timing; predicted geometry is not verified duplicate reuse. Require cross-platform match and adverse active/closed coverage. If gap, DEFER, preserve evidence; do not alter corpus after candidate timing. Qualify actual D/C external-source harness only after this source/rule lock passes; no production/default or durability claim.

## Accepted geometry and next qualification

#494 ACCEPT/MERGED 7399f289bc57e9976300d7fa81070e6ba8fd3d1b, qualified head 07d3a9f0a4e3df676ae36c8daf3f432dc796bdca. CI 45767548865, Heavy 45767548757, CodeQL 45767549036, geometry 45767548950, preflight and regression PASS. 19 Python tests and 8 geometry cells per Windows/Linux x64/ARM64. Independently downloaded original ZIPs 15093165420, 15093155245, 15093100431; GitHub digests/CRC and 57 internal checksums PASS, all 24 rows match stdout, geometry arithmetic and source identities; replay manifests identical including 736 selected member records per lane. Exact geometry and original artifact SHA256 preserved in holdout-geometry-lock.json, bound to unchanged source/rule lock e1bf44f06a90d7c20dfa149b78601a7c300a2f836547130bdb03746b95fea9f9.

| Family | Occurrences | Unique frames | Unique payload bytes | Packs | Active | Closed |
|---|---:|---:|---:|---:|---:|---:|
| win-versioned | 1745 | 1572 | 133175367 | 4 | 29 | 144 |
| linux-versioned | 1725 | 1594 | 133659710 | 4 | 0 | 131 |
| compressed | 810 | 790 | 64530547 | 2 | 10 | 10 |
| adverse-repeat | 1087 | 561 | 46461512 | 2 | 16 | 510 |

Next qualify actual existing D/C executable once per family/suite/candidate, 16 samples per OS, 48 total. Each fresh process must pass existing restore/readback/rebuild/cleanup, exact accepted geometry, actual C active/closed hit counts, D range byte count and identical D/C layout. Driver uses original executable unchanged; it internally measures phases as usual, but only structural allowlisted stdout fields are retained, no performance values or ratios archived and no timing decision. Runner logs may expose total job duration, not candidate metrics. Acquisition and build separate from samples. Cross-platform physical layout verification from original downloaded qualification artifacts is required before timed holdout. Do not include qualification in the 672-sample run. No retuning candidates, source lock or rule after qualification; correctness failures must be addressed before timing and disclosed. Next timed driver and independent decision evaluator must be qualified separately on synthetic records; then launch exact sealed matrix. No production default or durability claim.
