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

## #491 smoke evidence verified before full-screen launch

Exact parent candidate dfb6ef8a26538653dac273822a6770f27f998fe1.
Range smoke [45715703279](https://github.com/definitely-stable/ChunkShift/actions/runs/45715703279)
both lanes PASS; 36/36 each, 13 Python tests each, targeted C# range
self-test PASS on Windows and Ubuntu. Original regression smoke
[45715703286](https://github.com/definitely-stable/ChunkShift/actions/runs/45715703286)
28/28 each and 13 Python tests each. CodeQL 45715703324 PASS.
Full CI 45715703285 and Heavy 45715703352 still in progress at this checkpoint;
smoke acceptance is not merge acceptance.

Original range smoke artifacts independently downloaded:
- [Ubuntu 15070410277](https://github.com/definitely-stable/ChunkShift/actions/runs/45715703279/artifacts/15070410277):
  SHA256 1fc229817428bf90b617b8618e6431b7a938ac15cb6fb728dba2754e156c4e85.
  RunId REPO-INGEST-002/RUN-20261125-457157032791-dfb6ef8a2653-ubuntu-24-04.
- [Windows 15070151162](https://github.com/definitely-stable/ChunkShift/actions/runs/45715703279/artifacts/15070151162):
  SHA256 5a393d282f7a131ef3a62fe290e8df0487230ff6c36b6e9ceaa74b04da0bafe8.
  RunId REPO-INGEST-002/RUN-20261125-457157032791-dfb6ef8a2653-windows-2025.

Independent audit: outer ZIP SHA256, CRC, exact file inventory and 75 file
checksums per archive pass. All 72 records identity, stdout/JSONL agreement,
restore/source/corpus digest equality, geometry P=U+68N+168K and A/D/C
accounting pass. 12 A, 12 D, 12 C per lane; D read 8,388,608 range bytes in
every sample, no modeled materialized request copy, same staging payload as A.
Cross-platform and both-suite occurrence counts unique 101/101, recent 68/53,
cross 65/51 (logical/unique); per-family/suite layout fingerprint and physical
geometry equal across all candidates and both lanes. No smoke timings used.
Next: finish #491 required gates, merge only exact qualified head, refresh
this launch branch on accepted main and open full-screen PR. Do not rerun
full screen merely to improve timing results.
