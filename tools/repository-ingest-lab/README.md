# R1-B4e.1 — research ingestion harness

Status: hosted harness correctness ACCEPTED in #488 (56/56 smoke samples).
Full screening is separate from smoke and sealed holdout. See screen-lock.json
and report.py. Only descriptive paired results, no adoption decision.

- Dedicated .NET 10 JIT console outside solution; no new package, Core API or
  production behavior. One narrow Repository friend assembly for lab access.
- Python stdlib driver builds once, generates source-locked files with SHAKE256
  and fixed seed, records SHA256, rotates candidate order, launches fresh process
  per sample, preserves stdout/stderr/JSONL and stops on any failure.
- A = finite staged reference, one staging writer, actual scan, materialized
  independent chunk stream copies. This is deliberately the existing oracle
  adapter, not a claim about the fastest possible file-stage adapter; investigate
  bounded lazy source ranges as sensitivity if memory/materialization dominates.
- B = direct unique-only, only on unique corpus. C = serial verified reuse.
- Timed Ready phase includes source open/scan/staging/pack/readback. Common
  post-timing scan oracle, exact ordered occurrence comparison, verified restore
  SHA256 and full physical rebuild equality. Layout signature ignores random
  PackId but binds pack/frame ordering, key, length, offset and CRC. All cases
  must match across paths. Cleanup releases budgets and leaves no owned files.
- CPU, managed process allocation delta, process-lifetime peak working set at
  Ready, counts and modeled payload I/O, restore/rebuild/cleanup time. No device
  I/O, sampled RSS, per-duplicate timing or actual stage high-water claim.
- Source generator profiles: unique, recent zero run, cross zero/random/zero;
  measured CDC ratio comes from actual occurrences. Cache uncontrolled.
- Default 128 MiB, 32 MiB targets, two suites, 5 rounds plus warmup = 84 samples.
  Smoke 8 MiB / 1 MiB / 1 measured round = 28 samples; not acceptance evidence.
  A cross case at smoke scale should rotate; actual hit counts still measured.
- Invocation after qualification (new directory outside checkout):
  python tools/repository-ingest-lab/run.py --output /path/to/new-result --run-id <assigned-run-id>
- Dedicated hosted smoke workflow runs on lab PRs or manual dispatch, on
  Windows and Linux x64 with pinned actions; 28 samples per platform, not
  decision-bearing performance evidence. It assigns RunIds before execution
  from workflow run/attempt, date, exact checkout SHA and runner lane.
- Strict driver record validation rejects absent/nonfinite/negative timings,
  invalid count types, wrong identity/digests, inconsistent geometry or missing
  intended CDC reuse (recent active/cross closed). Seven validator unit tests.
  No benchmark claim until hosted qualification.
- No holdout, performance measurement or adoption claim. Proposed thresholds
  remain subject to preregistration before confirmation.

## Frozen screening

`screen-lock.json` binds parameters and corpus SHA-256 before timing; `run.py --screen-lock` fails closed on any mismatch. `report.py` independently checks the exact inventory, schedule, stdout/JSONL agreement and all sample contracts, then emits five paired Ready ratios (baseline/candidate) per applicable comparison, median/range and per-candidate metrics. Warmups excluded. Workflow uses one isolated job per platform, no same-runner simultaneous samples. No inference or holdout promotion. Raw data retained even on failure.

## Range staging sensitivity (REPO-INGEST-002)

Candidate D = A′, one source handle, independent forward-only positional views,
no payload-wide materialization, same accepted IngestSession and pack coordinator.
D rereads all logical bytes after scan; actual range read count separately recorded.
A/D/C paired on all three original corpus families and both suites. Six measured
rounds cover all permutations, 126 samples/lane (108 measured, 18 warmup).
First run hosted 36-sample/lane correctness smoke plus range self-test; no full
screen before acceptance. `--range-sensitivity --screen-lock --rounds 6` selects
separate frozen lock, RunId REPO-INGEST-002. `report.py --range-sensitivity` audits
that full dataset; original REPO-INGEST-001 format remains readable.

## Real-artifact holdout qualification

After #494 accepted, holdout-geometry-lock.json records the accepted 24-cell
geometry and original artifact hashes without changing holdout-lock.json.
holdout_qualify.py replays the exact corpus lock and exercises D/C once per
family/suite on each of three platforms; structural evidence only, no timing
metrics retained. Check exact geometry, locality and physical layout, source
restore, rebuild and cleanup. Full holdout not launched by qualification.

## Sealed holdout evaluator

After #495, holdout-qualification-lock.json binds eight actual cross-platform
layout fingerprints and qualified source files. holdout_run.py requires explicit
hosted execution. holdout_evaluate.py requires six original evidence directories
and audits exact schedule, identity, raw checksums, source replay and layout before
applying the unchanged rule. No decision-bearing workflow in evaluator qualification.
