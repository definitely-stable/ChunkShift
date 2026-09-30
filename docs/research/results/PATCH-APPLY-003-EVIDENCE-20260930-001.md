# PATCH-APPLY-003 — the boundary-only check brings the check's CPU under 25 %

EvidenceId: `PATCH-APPLY-003/EVIDENCE-20260930-001`  
Status: `ADOPT` (rule 1: A2, from [EVIDENCE-20260929-001](PATCH-APPLY-003-EVIDENCE-20260929-001.md)); rule 2 does not hold after A1a; rule 3: #168 closes with option 3  
Owning issue(s): #182, #168, #7  
Implementation PR(s): #207 (A1a), #208 (lane `boundary` in the lab); the default switch follows this record  
Date: 2026-09-30

## Hypothesis

Rule 2 of [PATCH-APPLY-002-PROTOCOL.md](../../benchmarks/PATCH-APPLY-002-PROTOCOL.md) §6, run as [PATCH-APPLY-003](../../benchmarks/PATCH-APPLY-003-PROTOCOL.md):

> With A2, does the check still cost more than 25 % of apply CPU when several applies run at once, where overlap cannot hide CPU? Only then is the boundary-only check (A1a) built.

[EVIDENCE-20260929-001](PATCH-APPLY-003-EVIDENCE-20260929-001.md) answered yes (36.74 % on linux-x64, 26.63 % on linux-arm64), so A1a was built (#207). This record evaluates rule 2 again on A1a, lane `boundary`: does the boundary-only check cost at most 25 % of apply CPU at eight concurrent applies?

## Frozen decision rule

From PATCH-APPLY-002 §6, inherited unchanged by PATCH-APPLY-003 §2. Both protocols were merged before any run (#200 at `0a5e637`). Bounds apply per platform and are counted across linux-x64, linux-arm64 and win-x64.

1. **A2 (overlap).** Decided by [EVIDENCE-20260929-001](PATCH-APPLY-003-EVIDENCE-20260929-001.md): ADOPT. It is not measured again for a decision.
2. **A1a (boundary-only check).** Needed only if, with the lane adopted by rule 1, the CPU overhead of the check in the many-file lane at c = 8 (median over repetitions) exceeds 25 % of apply CPU on at least two platforms. Then A1a is built in its own pull request, measured by this protocol as lane `boundary`, and this rule is evaluated again on it. A platform with more than eight logical processors is `not evaluated` (PATCH-APPLY-003 §3).
3. **Public option.** Option 1 of #168 reopens only if rule 1 rejects A2, or if rule 2 still holds after A1a. Otherwise #168 is closed with option 3.

Applied to this run: rule 2 reads the median over the ten repetitions of `cpu(boundary) / cpu(off) − 1` at c = 8. It *still holds* when that value exceeds 25 % on at least two platforms, and *does not hold* when it is at most 25 % on at least two.

## Compared lanes

All lanes use `CspApplier` at `fa77357` through its internal `ChunkingCheck` switch; the public `ApplyAsync` surface is unchanged. The lab (#208) measures three lanes with the §5 method unchanged: the order `L1 L2 L3 L1 L2 L3` rotated by `r mod 3` (`off overlap boundary`, `overlap boundary off`, `boundary off overlap`). `seq` is not measured, because no rule reads it (PATCH-APPLY-002 §6).

| Lane | Construction |
| --- | --- |
| `off` (baseline) | `ChunkingCheck.Off`: no re-chunk check |
| `overlap` (A2, the apply default) | `ChunkingCheck.Overlapped`: every verified chunk is also copied, in target order, into a bounded `System.IO.Pipelines` pipe that a concurrently running `ChunkManifest.VerifyAsync` reads |
| `boundary` (candidate, A1a) | `ChunkingCheck.Boundary`: `BoundaryChunkingCheck` runs inline in the write loop, with no pipe and no hashing. For `fastcdc.gear.chunkshift.v1.64k` with its Core 0.1.0 `ProfileFingerprint` it requires the profile to cut exactly where every record ends; any other profile gets the `Overlapped` check. Every manifest of the corpus uses the pinned profile, so the boundary check runs on every file |

## Included runs

One dispatch of `patch-lab.yml` with `lanes=apply-check` from `main` at `fa77357f337165a0f98ee239adca21bc37de97ab` (#208): workflow run [36672992572](https://github.com/definitely-stable/ChunkShift/actions/runs/36672992572), run number 20, started 2026-09-30 05:21 UTC. All three platform jobs and the summary job succeeded. Every job ran .NET 10.0.12 (JIT, Release) and CPython 3.14.7.

| RunId | Job id | Runner image | OS (result JSON) | Processor (result JSON) | Lane step |
| --- | --- | --- | --- | --- | --- |
| `PATCH-APPLY-003/RUN-20260930-20-fa77357-linux-x64` | 109751854015 | `ubuntu-24.04` | Ubuntu 24.04.5 LTS, X64 | AMD EPYC 9V45 96-Core Processor, 4 logical | 05:21–05:52 UTC |
| `PATCH-APPLY-003/RUN-20260930-20-fa77357-linux-arm64` | 109751853942 | `ubuntu-24.04-arm` | Ubuntu 24.04.5 LTS, Arm64 | Arm64 (Ubuntu 24.04.5 LTS), 4 logical | 05:22–06:03 UTC |
| `PATCH-APPLY-003/RUN-20260930-20-fa77357-win-x64` | 109751854104 | `windows-2025` | Microsoft Windows 10.0.26100, X64 | AMD64 Family 25 Model 1 Stepping 1, AuthenticAMD, 4 logical | 05:23–07:35 UTC |

The summary job `patch-lab-summary` (id 109789712251) ran `summarize_apply_check.py --require-all-platforms --prior-verdict docs/research/results/data/PATCH-APPLY-003-20260929-001/apply-check-verdict.json` over the three platform artifacts and wrote the verdict. The prior verdict it read has SHA-256 `6622a8a4…d1bc3`, the value in the `SHA256SUMS` of the rule-1 record. Every platform has processorCount 4, so the c = 8 guard of §3 lets rule 2 read all three.

Dataset: [data/PATCH-APPLY-003-20260930-001/](data/PATCH-APPLY-003-20260930-001/).

- `PATCH-APPLY-003-compact-{linux-x64,linux-arm64,win-x64}.json`: the compact samples of §3, byte for byte as the summary job wrote them. They hold, per file and lane, the medians over the ten repetitions; per repetition and c, the many-file values; per file, the memory excess over idle.
- `apply-check-verdict.json`: the summary job's verdict, byte for byte.
- `PATCH-APPLY-003-repetitions.jsonl`: what the compact files do not hold. For every platform, file and repetition it has the two measured wall and CPU times of each lane. For every repetition it has the lane order and the output-identity bit (`outputsVerified`). `build_repetitions.py` writes it from the 30 raw time documents of the three platform artifacts; the build is deterministic.
- `SHA256SUMS` of those five data files.
- `recompute.py` (standard library, Python ≥ 3.12). It checks `SHA256SUMS` and the digest of the prior verdict. From the committed files it then recomputes:
  - from the compact files: the concurrency statistics with their bootstrap intervals (10,000 resamples, seed 20260928, the summarizer's random stream), which decide rule 2, and the memory maxima;
  - from the per-repetition times: every per-file median of the compact files, the point estimates of the per-file lane and the lane order of every repetition;
  - with `--time-intervals`: also the paired repetition-block intervals of the per-file lane (§2.1). No rule of this run reads them; this takes about 18 minutes.

  Each value must equal the verdict JSON exactly. The script then applies rules 2 and 3 to the recomputed values, with rule 1 from the prior verdict.

The raw CI artifacts (the 22 result JSON files of each platform) expire on 2026-12-29:

| artifact | id | SHA-256 of the zip |
| --- | --- | --- |
| `patch-lab-apply-check-linux-x64` | 11080460245 | `040fb83f…80f9` |
| `patch-lab-apply-check-linux-arm64` | 11079947072 | `42512d3a…0e5e` |
| `patch-lab-apply-check-win-x64` | 11083771526 | `1d16a3ce…3bf4` |
| `patch-lab-summary` | 11083923246 | `e669bde6…b952` |

## Reproduction

```text
gh workflow run patch-lab.yml --ref main -f lanes=apply-check
python3 docs/research/results/data/PATCH-APPLY-003-20260930-001/recompute.py [--time-intervals]
```

`PATCH-APPLY-003-repetitions.jsonl` from the raw platform artifacts:

```text
python3 docs/research/results/data/PATCH-APPLY-003-20260930-001/build_repetitions.py \
  apply-check-linux-x64 apply-check-linux-arm64 apply-check-win-x64 --output PATCH-APPLY-003-repetitions.jsonl
```

From the raw artifacts, with the summarizer and corpus lock of `fa77357`:

```text
python3 benchmarks/scripts/summarize_apply_check.py apply-check-linux-x64 apply-check-linux-arm64 apply-check-win-x64 \
  --corpus-lock docs/benchmarks/patch-corpus/corpus-lock.json \
  --prior-verdict docs/research/results/data/PATCH-APPLY-003-20260929-001/apply-check-verdict.json \
  --output apply-check-verdict.json --markdown apply-check-summary.md --compact-dir compact --require-all-platforms
```

Python 3.12 or later is required for bit-identical sums: from 3.12 on, `sum()` over floats is compensated, and the workflow ran 3.14.

## Semantic / compatibility checks

- [x] Output identity: on every platform, in every one of the ten time repetitions, the first measured apply of each lane reproduced the corpus SHA-256 of all 1,893 targets (`outputsVerified: true` in all 30 time documents). A boundary mismatch would have failed the apply as `ProfileContent` and the run.
- [x] One commit, one corpus, one dispatch: all 66 result documents record `gitCommit fa77357…`, `corpusPairsSha256 8b3b92a9…22dd` (the corpus lock) and a RunId of run number 20 for their own platform.
- [x] Complete: on every platform there are repetitions 0–9 of `time` and of `concurrent`, in the rotated order of §5, and both passes of every lane at c ∈ {1, 2, 4, 8}. Every document records `cacheState: "warm"`. Each platform has one memory document of 75 files with `memoryEnvironment` empty (no allocator or GC variable set).
- [x] No format, profile, hash or identity change: the lanes differ only in the internal check switch. #207 ran the 125 CSP vectors, the P6 failure matrix, the short-read set and the apply-fuzz set in `Boundary` mode too.
- [x] x64 and ARM64 measured.
- [ ] NativeAOT is not part of this protocol; the PR that switches the default runs the Patching package smoke and a NativeAOT smoke against JIT.
- [x] Public API unchanged.

**Provenance checks, all passed:**

| # | check |
| ---: | --- |
| 1 | The committed compact files have the SHA-256 the summary job printed (`sha256sum`): `d10c1591…e151` (linux-x64), `9c9d7f00…fab1` (linux-arm64), `7f46ede0…67a6` (win-x64). The Linux platform jobs printed the same two values (`compact-samples-sha256=`). The win-x64 job printed `0577408d…9d49` for its own compact file, which Python wrote there with CRLF line ends (33,561 of them); with CR removed that file is byte-identical to the committed one. The content is the same; only the line ends differ. |
| 2 | Each artifact zip has the digest its upload step printed, and the digest the summary job verified on download. |
| 3 | The per-platform summaries in the artifacts match the ones printed in the job logs, and each platform section of the summary job's `apply-check-summary.md` equals its platform job's summary (win-x64 after CRLF → LF). The summary job's verdict for each platform equals the platform job's verdict except the input directory path. |
| 4 | `summarize_apply_check.py` from `main` (`fa77357`), rerun locally (CPython 3.13) over each platform's raw documents with the same `--prior-verdict`, reproduces that platform's compact file and summary byte for byte (win-x64 after CRLF → LF), and a verdict JSON equal to the job's except the input directory path. |
| 5 | The same rerun over all three platforms with `--require-all-platforms` reproduces the three committed compact files, `apply-check-summary.md` and `apply-check-verdict.json` byte for byte. |
| 6 | `recompute.py` over the committed files: every recomputed value equals the verdict JSON, including the rule-2 medians and intervals (for example 0.1339334937676615 [0.10807437203802728, 0.15273590811124782] on linux-x64). With `--time-intervals` it also rebuilds every per-file-lane interval from `PATCH-APPLY-003-repetitions.jsonl` and equals the verdict JSON (about 18 minutes; without it, a few seconds). |

## Results

Percent rounded to two decimals; every value in full precision is in `apply-check-verdict.json`.

### Rule 2: CPU overhead in the many-file lane

Median over the ten repetitions of `cpu(lane) / cpu(off) − 1` per pass pair, with the percentile bootstrap over repetitions (10,000 resamples, seed 20260928):

| c | linux-x64 `boundary` | linux-arm64 `boundary` | win-x64 `boundary` | linux-x64 `overlap` | linux-arm64 `overlap` | win-x64 `overlap` |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 13.27 % [−18.51, 46.72] | 4.95 % [−7.82, 17.46] | 2.45 % [−2.32, 8.93] | 35.82 % | 29.10 % | 22.02 % |
| 2 | 12.42 % [11.82, 14.16] | 5.80 % [5.33, 6.47] | 7.47 % [4.50, 9.12] | 24.12 % | 21.78 % | 26.84 % |
| 4 | 13.92 % [11.21, 19.61] | 7.06 % [6.10, 8.72] | 6.54 % [4.78, 9.10] | 27.01 % | 28.20 % | 22.27 % |
| **8** | **13.39 %** [10.81, 15.27] | **7.79 %** [6.36, 8.67] | **8.02 %** [1.37, 10.45] | 25.53 % | 25.03 % | 24.32 % |

Rule 2 reads `boundary` at c = 8. Without rounding: 0.1339334937676615 (linux-x64), 0.07787713651172956 (linux-arm64), 0.08022842401044894 (win-x64). All three are at most 25 %, and so are the upper ends of their intervals (15.27 %, 8.67 %, 10.45 %).

Whole-pass medians at c = 8, CPU seconds `off` / `overlap` / `boundary`: 2.43 / 3.07 / 2.80 (linux-x64), 4.82 / 5.98 / 5.15 (linux-arm64), 9.34 / 11.29 / 9.92 (win-x64). Wall overhead of `boundary` at c = 8: 0.32 % [−2.94 %, 5.68 %], 2.17 % [1.28 %, 4.59 %] and 1.35 % [−0.64 %, 3.04 %].

For reference, the `overlap` lane of this run at c = 8 is 25.53 %, 25.03 % and 24.32 %. In [EVIDENCE-20260929-001](PATCH-APPLY-003-EVIDENCE-20260929-001.md) it was 36.74 %, 26.63 % and 19.97 %, on different runner hardware (linux-x64: Intel Xeon 6973P-C then, AMD EPYC 9V45 now). No rule reads `overlap` in this run.

### Per-file lane (record only)

No rule of this run reads the per-file lane. Paired repetition-block intervals of §2.1:

| platform | lane | wall, median of ratios | wall, corpus sum | wall, files ≥ 1 MiB (75) | CPU, median of ratios (files ≥ 20 ms) | CPU, corpus sum |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| linux-x64 | `boundary` | 1.68 % [1.40, 2.54] | 4.86 % [3.06, 6.25] | 5.11 % [0.81, 8.07] | 11.90 % [11.18, 16.51] (13) | 12.00 % [10.84, 13.73] |
| linux-x64 | `overlap` | 5.09 % [4.26, 5.56] | 2.95 % [1.78, 4.20] | 1.86 % [−0.41, 3.79] | 32.45 % [30.51, 38.82] (13) | 38.01 % [36.80, 39.09] |
| linux-arm64 | `boundary` | 0.86 % [0.59, 1.29] | 3.72 % [2.90, 4.55] | 6.97 % [5.28, 7.20] | 4.99 % [3.93, 5.75] (23) | 4.25 % [3.39, 4.46] |
| linux-arm64 | `overlap` | 5.53 % [4.87, 5.82] | 3.37 % [2.18, 3.88] | 2.10 % [1.73, 2.41] | 22.88 % [22.05, 25.27] (23) | 31.62 % [30.49, 31.62] |
| win-x64 | `boundary` | 0.24 % [−0.26, 0.89] | 1.72 % [0.98, 2.82] | 2.94 % [0.79, 5.39] | 11.01 % [0.00, 13.39] (19) | see Limitations |
| win-x64 | `overlap` | 1.07 % [0.79, 1.82] | 2.40 % [0.97, 3.81] | 2.29 % [0.53, 4.70] | 35.71 % [22.22, 39.63] (19) | see Limitations |

Sums over the corpus of per-file medians, seconds (`off` / `overlap` / `boundary`):

| platform | wall | CPU |
| --- | --- | --- |
| linux-x64 | 4.32 / 4.44 / 4.53 | 3.31 / 4.57 / 3.71 |
| linux-arm64 | 7.30 / 7.55 / 7.57 | 6.85 / 9.01 / 7.14 |
| win-x64 | 22.88 / 23.43 / 23.28 | 5.00 / 7.18 / 5.71 |

The boundary check does in the write loop what the overlapped check hides behind it. It therefore costs less CPU and, on large files, more wall time than `overlap`. Over files of at least 1 MiB its wall overhead is 5.11 %, 6.97 % and 2.94 %, against 1.86 %, 2.10 % and 2.29 % for `overlap`. The corpus-sum wall overhead is 4.86 %, 3.72 % and 1.72 %, and every upper bound in the table is below 10 %.

### Memory

The largest memory excess over idle of any file:

| platform | idle | `off` worst | `overlap` worst | `boundary` worst |
| --- | ---: | ---: | ---: | ---: |
| linux-x64 | 34.5 MiB | +38.7 MiB | +44.7 MiB | **+37.7 MiB** |
| linux-arm64 | 33.8 MiB | +38.3 MiB | +44.8 MiB | **+38.1 MiB** |
| win-x64 | 24.3 MiB | +27.4 MiB | +31.5 MiB | **+27.6 MiB** |

The worst file is the `node-linux-x64` `bin/node` pair on every platform and in every lane. Per file, `boundary` peaks between −1.02 and +1.11 MiB of `off` (linux-x64), −0.95 and +0.60 MiB (linux-arm64) and −0.91 and +1.20 MiB (win-x64), with medians of +0.02, +0.07 and +0.06 MiB. The boundary check holds no buffer; no rule bounds its memory.

### Where the sequential check's cost goes

Decomposition of the sequential check, measured per file after the timed applies, share of its wall time:

| component | linux-x64 | linux-arm64 | win-x64 |
| --- | ---: | ---: | ---: |
| CSM re-parse | 3.17 % | 2.13 % | 1.97 % |
| temporary-file re-read | 16.07 % | 13.21 % | 48.37 % |
| boundary scan | 31.72 % | 21.75 % | 22.41 % |
| hashing | 40.82 % | 51.53 % | 31.84 % |
| ManifestId | 0.64 % | 0.80 % | 0.38 % |

The boundary check keeps only the boundary scan: no re-read, no second hashing, no ManifestId and no CSM re-parse.

Record only (input to #148): 21.55 % of the target bytes lie in records whose `ChunkId` is the base record at the same offset, and 21.38 % in whole 4 KiB blocks inside those extents (identical on every platform, as the corpus is).

## Exclusions / invalid runs

None. No run of this rerun was invalid or discarded; run 36672992572 is the only dispatch with lane `boundary`.

## Limitations

- **Different runners than the rule-1 run.** The linux-x64 runner is an AMD EPYC 9V45 here and was an Intel Xeon 6973P-C in EVIDENCE-20260929-001; the win-x64 processor model also differs (Family 25 Model 1 against Model 17). Rule 2 compares `boundary` with `off` inside this run, in the same paired, rotated repetitions, so it does not depend on the earlier run. The `overlap` values of the two runs are not comparable one to one.
- **win-x64 per-apply CPU is quantized** by the 15.625 ms clock tick. Only 238 files have a nonzero `off` CPU median and 19 are above the 20 ms floor. The CPU corpus-sum point estimates lie outside their own intervals (`overlap` 25.37 % against [−1.64 %, 20.30 %], `boundary` 0.16 % against [−19.99 %, −3.83 %]), because resamples move files in and out of the nonzero set. No rule reads these statistics; rule 2 reads whole-pass totals of about 9–11 CPU seconds on win-x64.
- **win-x64 wall time is mostly waiting**: 22.88 s of wall time for 5.00 s of process CPU in lane `off` over the corpus, as in the rule-1 run.
- The c = 1 CPU intervals are wide (up to 65 points); at c = 8 they are 2–9 points wide. The win-x64 interval at c = 8, [1.37 %, 10.45 %], is the widest.
- On linux-arm64 the `overlap` CPU corpus-sum point estimate, 31.62 %, sits at the upper end of its interval [30.49 %, 31.62 %], a known property of a percentile bootstrap of medians over ten values. No rule reads it.
- The raw artifacts expire on 2026-12-29. Every quantity that decides rules 2 and 3 stays recomputable from the committed files: the concurrency statistics from the compact files, the per-file lane from the compact and per-repetition files. The check decomposition, which no rule reads, needs the raw artifacts.
- Shared runners, one dispatch; the paired, rotated, repeated design of PATCH-APPLY-002 §5 is what absorbs runner noise.

## Decision

```text
Rule 1: ADOPT A2 (overlap) — from EVIDENCE-20260929-001, carried by its verdict digest 6622a8a4…d1bc3
Rule 2: does not hold after A1a — boundary CPU overhead at c = 8 at most 25 % on 3 of 3 platforms
Rule 3: option 3 stands — #168 is closed with option 3, no public opt-out
```

Rule 2: with the boundary-only check at c = 8 the check costs 13.39 % (linux-x64), 7.79 % (linux-arm64) and 8.02 % (win-x64) of apply CPU, at most 25 % on all three platforms. Rule 2 does not hold after A1a. Every output was identical, and the boundary lane's worst peak is within 1.2 MiB of `off`.

Rule 3: A2 was not rejected and rule 2 does not hold after A1a, so option 1 of #168 does not reopen. #168 is closed with option 3: the check stays on with no public opt-out, and it is made cheaper internally.

## Consequences

- A pull request makes `ChunkingCheck.Boundary` the apply default (`DefaultChunkingCheck`). For the shipped profile the public apply methods then run the boundary-only check, and any other registered profile keeps the overlapped check. The PR moves D13 to confirmed and runs the Patching package smoke (also against Core 0.1.0) and a NativeAOT smoke against JIT.
- #168 is closed with option 3 after that pull request.
- `PATCH-APPLY-002` stays the ExperimentId for the reconstruction lanes A3–A6 of #182.

## References

- #182, #168 ("Decision — 2026-09-28"), #7; #200, #201, #206, #207, #208;
- [PATCH-APPLY-002-PROTOCOL.md](../../benchmarks/PATCH-APPLY-002-PROTOCOL.md), [PATCH-APPLY-003-PROTOCOL.md](../../benchmarks/PATCH-APPLY-003-PROTOCOL.md);
- PATCHING-DECISIONS D2, D3, D9, D12, D13, D21;
- prior evidence: [PATCH-APPLY-003/EVIDENCE-20260929-001](PATCH-APPLY-003-EVIDENCE-20260929-001.md) (rules 1 and 2 on A2).
