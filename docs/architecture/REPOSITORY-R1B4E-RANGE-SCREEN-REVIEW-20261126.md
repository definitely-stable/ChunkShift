# REPO-INGEST-002 — full sensitivity launch and review checklist

Status: launch candidate, no timed evidence. Requires accepted #491 on main
before opening the full-screen PR. This branch does not initiate workflow runs
by itself. Refs #133 #144 #139 #10; prior REPO-INGEST-001/EVIDENCE-20261126-001.

## Frozen run

Use the already registered range-screen-lock.json unchanged: 128 MiB, 32 MiB
target, seed144, exact previous corpus digests, both HashSuite, A/D/C, 6
measured permutations and one warmup, 126 raw and 108 measured per platform.
Windows and Ubuntu x64 in separate jobs; shared concurrency group with the
original screening prevents competing ingestion screens. Exact PR HEAD,
pinned Actions, .NET from global.json, Python 3.14, one process per sample.
No change to timed candidate code or earlier REPO-INGEST-001 lock.

Workflow repository-range-staging-screen.yml uses --range-sensitivity and
--screen-lock on run.py, --range-sensitivity on report.py. It requires the
independent report audit, exact RunId/HEAD, SCREEN_ONLY, 126 raw, 108 measured,
18 warmups and six groups before success. Raw inventory and paired summary
archived even on failure; 90-day retention. Each lane has its own RunId.
Full result is not accepted until both lanes and applicable exact-head gates
pass, then independent download audit.

## Independent review (after run)

1. Download both original ZIPs, verify GitHub artifact digest, ZIP CRC and
   every internal inventory checksum. Require exactly 126 stdout/stderr
   pairs, build.log, manifest.json, samples.jsonl and inventory per raw dir,
   plus separately generated summary.json. No missing samples, exclusions
   or failed rows; no stale or extra files. Check exact checkout, RunId,
   environment, lock and executable/adapter fingerprints.
2. Verify all sample identity, order, source/restore digest, physical
   geometry/layout, cleanup and source range accounting. D must deliver
   exactly L source-range bytes, stage L and materialize zero modeled
   request-copy bytes; A copies L, stages L and has zero range reads; C
   has zero staging, request copies and range reads. Across OS compare
   only exact structural metadata and hashes, not absolute wall times.
3. Independently recompute medians, ranges and each of six within-round
   A/D, D/C, A/C ratios, per family/suite/lane, excluding warmups. 72
   measured pairs for each comparison across both lanes. Report CPU,
   allocations, process-lifetime peak working set at Ready and separate
   restore/rebuild/cleanup medians, not just throughput.
4. A/D changes both memory materialization and source reread/access pattern;
   not an isolated allocation-cost attribution. D/C compares against a
   lower-allocation staging adapter, not all possible staging implementations.
   A/C is an in-run replication, not a pair with previous runner data.
5. Evidence record/registry with exact RunIds and compact reviewed metrics,
   original artifact digests and limitations. SCREEN_ONLY: no preapproved
   adoption threshold. Production default stays DEFER pending preregistered
   real/adversarial holdout with independent runners and ARM64, plus
   separate R1-C durability. No cache/workers/hash-suite changes from
   this screen alone.

If any correctness or evidence audit fails, STOP and preserve original raw
diagnostics; no cherry-picking or post-hoc parameter changes. Smoke results
are qualification only, never included in the timed dataset.
