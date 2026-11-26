# R1-B4e.5 — REPO-INGEST-003 holdout preflight

Status: PLANNED, acquisition-only; no timed measurements. Refs #133 #144 #139 #10.
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

## Proposed decision rule to freeze before measurement

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
Acquire then seal exact lock plus rule and review before timing.
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
