# R1-B4e.1 — research ingestion harness

Status: unqualified experiment. Do not execute decision-bearing runs until
#485, logical/comparator and verified dedup have accepted exact-head gates.
This is an executable first screen, not a benchmark result.

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
- Dedicated hosted workflow and run registry records are next; current branch
  intentionally has no automatic CI trigger. Run IDs/evidence records must be
  assigned before a retained evidence run, not inferred from raw output.
- No holdout, performance measurement or adoption claim. Proposed thresholds
  remain subject to preregistration before confirmation.
