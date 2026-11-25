# R1-B4e — preregistered serial ingestion screening protocol

Status: proposed screening protocol, no runs, no decision-bearing evidence.
Run only after #485, logical/comparator child and verified reuse qualify on
exact main-derived heads. Do not add optimization candidates before screen.

## Matched work and timing boundaries

A = actual Core scan -> finite IngestSession file staging -> PrivatePackCoordinator.
B = unique-only BuildDirectAsync (only zero-duplicate workloads).
C = BuildDeduplicatedAsync. Both suites, same source bytes, CDC profile, target,
per-pack and aggregate ceilings; one scanner and one writer. A uses bounded
finite batches, identical logical inputs; report materialization of source
requests and copies separately, include in end-to-end time. Do not silently
exclude the staging preparation cost or add artificial copies to C. A's
logical order oracle comes from Core scan metadata including duplicate entries.
All paths must reconstruct exact input and verify every output descriptor;
compare suite-qualified unique key set, lengths, pack boundaries/physical
bytes (except random PackId), exact occurrence sequence and restore SHA256.
For A, logical occurrence sequence comes from the independent scan oracle,
not IngestSession's unique-entry list. B must be marked N/A on duplicates.

Primary phase: from source open until verified private set Ready, including
scan, staging, admission, writes, rotation and readback; not restore/cleanup.
Separate phases: restore exact bytes, physical rebuild from explicit candidates,
cleanup. Record prep/validation cost outside timed phase only when genuinely
shared (e.g. corpus creation). Use source files, not preloaded corpus arrays.
No OS cache flush claim; label warm/cold-ish and record run order.

## First screen (not a Cartesian product)

Start with 128 MiB deterministic source, 32 MiB pack target, stable Core
16/64/256 KiB CDC profile, one worker, SHA256/BLAKE3, and 3 corpus families:
seeded pseudorandom (unique), long repeat runs (recent duplicates), repeated
blocks separated by unique regions (cross-pack duplicates). Record actual
CDC distribution, actual unique/duplicate payload ratio and active/closed
hits; do not call requested block reuse the actual CDC reuse. Source SHA256,
generator revision, seed, bytes and corpus name pinned before measurement.
One warmup per scenario/path; at least five paired measured rounds, rotate
order AB/BA or AC/CA and ABC/BCA/CAB for unique inputs. Linux x64 and
Windows first; ARM64 confirmation once harness stable. No simultaneous timed
runs on same runner. No comparison between unrelated runner instances.

## Raw sample schema

RunId, commit, candidate, platform/arch, runner image, .NET SDK/runtime,
processor count, storage/temp directory, corpus SHA256/seed/bytes, profile,
suite, pack target, repetition/order, warmup, outcome, wall_ms_ready,
cpu_ms_ready, process_allocated_bytes_delta, process_peak_working_set_bytes
(lifetime, not phase unless independently sampled), stage_file_count,
stage_payload_bytes, verified_pack_count/bytes, unique_frames/payload_bytes,
logical_occurrences/payload_bytes, active/closed_duplicate_count and bytes,
flush_count, comparison_wall_ticks, restore_ms/hash, rebuild_ms, cleanup_ms,
cleanup_failures. Actual logical I/O counters or explicitly modeled counts
must be distinguishable; never label as device writes. Use fresh process per
sample for allocation/peak comparability or label process-lifetime limitations.
Capture raw JSONL and machine-readable manifest; archive checksums and all
failed/aborted samples. Instrumentation overhead separately assessed; do not
use timings from unit tests or coverage execution as benchmark evidence.

## Interpretation and gating

Screen only, no production default. Correctness failure, leaked owned files,
resource violation or verification bypass = STOP regardless of throughput.
Report each paired ratio and median/range; no arbitrary significance claim
with five rounds. A proposed practical promotion threshold for later sealed
holdout: at least 10% median end-to-end speedup on the target class, no more
than 5% median slowdown on other supported classes, no new unbounded resource
or correctness regression; treat noisy/ambiguous results as DEFER. Thresholds
must be approved/frozen before holdout, not tuned after looking at results.
Scale screen to 16/64/128 MiB targets and measured duplicate fractions
0/25/50/90/99% only if needed; use source-locked real corpus plus adverse
short reads and locality as holdout. Choose cache only if active flush or
closed open/read costs dominate; build/store overlap only if measured useful.
Concurrent single-flight and owned buffers remain separate hypotheses.

## Implementation boundary

Next implement a small research-only executable/runner with narrow access
to Repository internals and source-locked generator, fail-closed assertions,
raw sample serialization and a dedicated hosted workflow. Avoid benchmark
logic in production API, dependencies or mandatory correctness CI. Follow
docs/research/README.md for RunId/EvidenceId and decision records. No
measurement, acceptance or cache/worker design is claimed by this document.
