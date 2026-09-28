# PATCH-APPLY-002 protocol: an overlapped re-chunk check on apply

Status: **frozen before measurement.** This note is committed before any run of `PATCH-APPLY-002` produces data. Later edits may only add result links; a change to a lane, metric, method or rule is a new ExperimentId.  
Issue: [#182](https://github.com/definitely-stable/ChunkShift/issues/182) · Decision it serves: [#168](https://github.com/definitely-stable/ChunkShift/issues/168) ("Decision — 2026-09-28", option 3) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
Register: [PATCHING-DECISIONS.md](../architecture/PATCHING-DECISIONS.md) D2, D9, D12, D13, D21 · Spec: [CSP-V1-CANDIDATE.md](../architecture/CSP-V1-CANDIDATE.md) §6 step 7, §9.5  
Prior evidence: `PATCH-APPLY-001` rule A1 ([EVIDENCE-20260928-001](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-001.md), [EVIDENCE-20260928-002](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-002.md))  
Registry: `PATCH-APPLY-002` is registered as PLANNED by [#187](https://github.com/definitely-stable/ChunkShift/pull/187); this note does not add a second row.

## 1. Question

`ChunkPatch.ApplyAsync` re-chunks the reconstruction with the embedded profile before it publishes (D13, CSP §9.5). #168 decided to keep that check on with no public opt-out and to make it cheaper internally, overlap first. `PATCH-APPLY-001` could not size the check: the same code measured 34.9 % and 5.9 % on linux-x64, because it compared corpus wall-time sums of single runs on shared runners.

This experiment answers two questions with a protocol built to survive runner noise:

1. Does overlapping the check with reconstruction (A2) bring its wall-time overhead down to at most 10 % of apply?
2. With A2, does the check still cost more than 25 % of apply CPU when several applies run at once, where overlap cannot hide CPU? Only then is the boundary-only check (A1a) built.

## 2. Lanes

All lanes use `CspApplier` of the commit under test, selected by its internal check switch; the public `ApplyAsync` surface is unchanged (D9).

| lane | id | what runs after (or during) reconstruction |
|---|---|---|
| A0 off | `off` | no re-chunk check |
| A0 sequential | `seq` | today's check: after the last record and the length check, `ChunkManifest.VerifyAsync` re-reads the temporary file and re-parses the embedded CSM |
| A2 overlapped | `overlap` | each verified chunk is written to the temporary file in target order and also copied, in the same order, into a bounded in-memory pipe; a concurrently running `ChunkManifest.VerifyAsync` reads the pipe and the embedded CSM. No re-read of the temporary file |
| A1a boundary-only | `boundary` | measured only if rule 2 (§6) requires it: an internal Gear boundary scanner for `fastcdc.gear.chunkshift.v1.64k` compares cut lengths with the embedded record lengths, without hashing; any other registered profile falls back to the full check |

Out of scope here: A1b (a Core boundary API, which #168 found Patching does not need) and the reconstruction lanes A3–A6 of #182 (preallocation, coalesced base reads, parallel hashing, hash backend). They get their own frozen protocol under the same ExperimentId.

In every lane the verdict order of D21 is unchanged: a reconstruction failure is reported as itself, the total length is checked next, and `ProfileContent` is reported only after both passed. Nothing is published on any failure (D12).

## 3. Corpus and patches

- The frozen patch corpus of [PATCH-PREFREEZE-PROTOCOL.md](PATCH-PREFREEZE-PROTOCOL.md) §1: `patch-corpus/corpus-lock.json`, `pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`; all twelve pairs, all 1,893 changed files. Every manifest uses `fastcdc.gear.chunkshift.v1.64k` and BLAKE3, so the check really runs.
- One patch per changed file, created once per platform by the `csp` lane (`CspEncoderPolicy.Default` of the commit under test: level 19, K = 4, 8 candidates, 256 KiB radius) and reused by every lane and repetition. Patch creation is not timed.

## 4. Metrics

Per apply (one `CspApplier.ApplyAsync` call including the commit of D12, which flushes the temporary file to disk and renames it):

- **process CPU time**, user + system (`Environment.CpuUsage.TotalTime` before and after the call) — the primary metric;
- **wall time** (`Stopwatch`) — the metric of rule 1.

Per file and repetition, measured after the timed applies, a **decomposition of the sequential check** into the work it repeats. Each component is timed (wall and CPU) on its own, in the lab, with Core's internal types the lab may use (`InternalsVisibleTo`):

| component | measured as |
|---|---|
| CSM re-parse | `CsmReader.ReadAndVerifyAsync` over the embedded TCSM |
| temporary-file re-read | sequential 64 KiB reads (Core's `ChunkingKernel.IoBufferSize`) of the applied output, bytes discarded |
| boundary scan | `ChunkBoundaryState` over an in-memory copy of the output, no hashing |
| hashing | the HashSuite hash of every chunk the scan cuts, in memory |
| ManifestId | `ManifestIdAccumulator` over the resulting records |
| whole check | `ChunkManifest.VerifyAsync` of the applied output against the embedded TCSM |

The residual (whole check minus the five components) is reported, not attributed.

**Peak memory** (memory lane): for every changed file of at least 1 MiB, one child process per lane applies the prepared patch and reports its peak working set (`Process.PeakWorkingSet64`); the idle baseline is the median of three idle children. Reported per lane as peak − idle; the A2 bound is 64 MiB over idle, as in `PATCH-APPLY-001` A2.

**Record only, no decision** (input to #148, `REPO-REMOTE-003`): per file, from the base and target manifests, the share of target bytes in records whose `ChunkId` is also the base record at the same offset, merged into extents, and the share of target bytes in whole 4 KiB blocks inside those extents. Only such bytes could ever be reflinked or block-cloned, because clone ranges must be block-aligned and CDC chunks are not. Reported per family and for the corpus.

## 5. Method

**Isolation and pairing.**

- A repetition is one lab process over the whole corpus. Ten repetitions, R = 10, form the decision set of one platform.
- Each process first makes one unmeasured pass over the first 20 changed files in all three lanes (JIT warm-up).
- Then, per file: one unmeasured warm-up apply in lane `off`, followed by the three lanes twice, interleaved, in the order `L1 L2 L3 L1 L2 L3`. The starting lane rotates with the repetition: `off seq overlap` for r ≡ 0 (mod 3), `seq overlap off` for r ≡ 1, `overlap off seq` for r ≡ 2. This is the three-lane form of paired ABAB runs, so a drift within a file and the position of a lane are balanced across repetitions.
- The value of a lane in one repetition is the mean of its two runs.

**Statistics.**

- Per file and lane, the value is the median over the R repetitions.
- Overhead of lane X for one file: `value(X) / value(off) − 1`.
- The statistic is the median over files of the per-file overheads, with a 95 % percentile-bootstrap confidence interval: 10,000 resamples of the files with replacement, seed 20260928.
- Also reported, with the same bootstrap: the corpus-sum overhead `Σ value(X) / Σ value(off) − 1`, and the per-file median restricted to files of at least 1 MiB. They do not change a verdict; if one of them lies on the other side of a bound than the statistic, the result record says so under its limitations.
- CPU per-file overheads include only files whose `off` median CPU is at least 20 ms, above the 15.6 ms Windows clock tick; the other files enter the CPU corpus sums only. Wall time uses every file.

**Page cache.** Warm only. The warm-up apply of each file loads the base, the base manifest and the patch into the page cache; every output is a new temporary file, written through the page cache and flushed to disk by the commit, as in production. Caches are not dropped. The result JSON records `cacheState: "warm"`.

**Output identity.** In every repetition, the first measured apply of each lane must reproduce the corpus SHA-256 of the target; a mismatch fails the run.

**Many-file lane.**

- Same R = 10 repetitions (separate processes), concurrency c ∈ {1, 2, 4, 8}.
- One pass applies every changed file with c applies in flight, in corpus order.
- Per repetition and c, the three lanes run as passes in the rotated `L1 L2 L3 L1 L2 L3` order of the per-file lane; a lane's value is the mean of its two passes.
- Metrics per pass: process CPU (primary) and wall.
- CPU overhead of lane X at c: `cpu(X) / cpu(off) − 1` per repetition. The statistic is the median over repetitions, with a 95 % percentile bootstrap over repetitions (10,000 resamples, seed 20260928).
- c = 8 saturates every platform (4, 4 and 8 logical processors) and is the one rule 2 reads; c = 1, 2, 4 are reported.

**Platforms.** JIT, .NET 10:

- linux-x64: GitHub `ubuntu-24.04`, `patch-lab.yml`;
- linux-arm64: GitHub `ubuntu-24.04-arm`, `patch-lab.yml`;
- win-x64: the owner's Windows 11 machine (Intel Core i3-12100F), by the commands of §8.

A platform without a complete, valid dataset counts as not meeting a bound.

**Invalid runs**, fixed in advance: a failed or incomplete lab process, an output digest mismatch, a corpus-lock mismatch, or evidence from more than one commit on a platform. An invalid run is listed in the result record with its reason and is never dropped silently; its platform is rerun as a new RunId.

## 6. Decision rules

The rules restate the #168 decision ("Decision — 2026-09-28"). Bounds apply per platform and are then counted across linux-x64, linux-arm64 and win-x64.

1. **A2 (overlap).** Take the upper 95 % CI bound of the wall-time overhead of `overlap` over `off` (the statistic of §5).
   - **ADOPT** A2 when that bound is at most 10 % on at least two of the three platforms, and in addition, on every measured platform, every output is identical and the memory lane stays at or below 64 MiB over idle for every file. The overlapped check becomes the apply default.
   - If the wall bound holds but the memory or output condition fails, **DEFER**: the default stays sequential and the failing condition is investigated first.
   - If the wall bound exceeds 10 % on at least two platforms, **REJECT** A2 as sufficient: option 1 of #168 (a public `PatchApplyOptions`) reopens.
2. **A1a (boundary-only check).** Needed only if, with the lane adopted by rule 1, the CPU overhead of the check in the many-file lane at c = 8 (median over repetitions) exceeds 25 % of apply CPU on at least two platforms. Then A1a is built in its own pull request, measured by this protocol as lane `boundary`, and this rule is evaluated again on it. Otherwise A1a is not built.
3. **Public option.** Option 1 of #168 reopens only if rule 1 rejects A2, or if rule 2 still holds after A1a. Otherwise #168 is closed with option 3, no public opt-out. If option 1 reopens, the same options type also carries progress reporting (#168).

The sequential lane is reported for reference; no rule reads it.

## 7. Invariants checked outside the lab

The pull request that implements A2 keeps, in CI:

- the same verdicts and outputs on the 125 committed CSP vectors, the created scenario patches and the apply-fuzz set, in every lane of the internal switch;
- `ProfileContent` for a patch whose valid manifest carries the registered profile but record lengths the profile would not cut;
- the P6 failure-point matrix (D12) and the short-read tests (`ShortReadTests`) green;
- tests for cancellation during the overlapped check, an exception inside the check, an unregistered profile (the check is skipped as today) and the pipe's memory bound.

## 8. Commands and records

The lab mode `patch-lab apply-check` is added by the implementation pull request:

```text
patch-lab apply-check prepare    --corpus <root> [--work <dir>]
patch-lab apply-check time       --corpus <root> [--work <dir>] --repetition <r> --output <file> [--run-id <id>]
patch-lab apply-check concurrent --corpus <root> [--work <dir>] --repetition <r> --output <file> [--run-id <id>]
patch-lab apply-check memory     --corpus <root> [--work <dir>] --output <file> [--run-id <id>]
```

On Linux, `patch-lab.yml` runs them with `lanes=apply-check` for R = 10 and uploads `patch-lab-apply-check-<arch>`. On Windows, from a clone at the commit under test:

```text
python benchmarks/scripts/materialize_patch_corpus.py --root C:\patch-corpus --download --lock
dotnet build benchmarks/ChunkShift.Benchmarks.slnx -c Release
$lab = "benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0/ChunkShift.Benchmarks.exe"
$run = "PATCH-APPLY-002/RUN-<yyyymmdd>-001-<commit>-win-x64"
& $lab patch-lab apply-check prepare --corpus C:\patch-corpus
0..9 | % { & $lab patch-lab apply-check time --corpus C:\patch-corpus --repetition $_ --output "out\apply-check-time-$_.json" --run-id $run }
0..9 | % { & $lab patch-lab apply-check concurrent --corpus C:\patch-corpus --repetition $_ --output "out\apply-check-concurrent-$_.json" --run-id $run }
& $lab patch-lab apply-check memory --corpus C:\patch-corpus --output out\apply-check-memory.json --run-id $run
```

The machine stays otherwise idle, on AC power, with the power plan it used for `PATCH-APPLY-001`.

`benchmarks/scripts/summarize_apply_check.py` reads one directory per platform, checks the corpus lock and the single commit, computes §5 and applies §6. Each platform's run gets a RunId `PATCH-APPLY-002/RUN-YYYYMMDD-NNN-<commit>-<platform>`. The result record `docs/research/results/PATCH-APPLY-002-EVIDENCE-YYYYMMDD-NNN.md` follows `RESULT-TEMPLATE.md` and commits the compact per-file samples.

## 9. Result

Pending.
