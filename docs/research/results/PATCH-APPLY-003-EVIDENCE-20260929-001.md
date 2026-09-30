# PATCH-APPLY-003 — the overlapped check hides its wall time; its CPU still needs A1a

EvidenceId: `PATCH-APPLY-003/EVIDENCE-20260929-001`  
Status: `ADOPT` (rule 1: A2); rule 2: A1a required; rule 3: #168 stays open until A1a is measured  
Owning issue(s): #182, #168, #7  
Implementation PR(s): #200 (frozen protocol), #201 (A2 and the lab); the default switch and A1a follow this record  
Date: 2026-09-29

## Hypothesis

From [PATCH-APPLY-002-PROTOCOL.md](../../benchmarks/PATCH-APPLY-002-PROTOCOL.md) §1, run as [PATCH-APPLY-003](../../benchmarks/PATCH-APPLY-003-PROTOCOL.md) with win-x64 on a GitHub runner:

1. Does overlapping the check with reconstruction (A2) bring its wall-time overhead down to at most 10 % of apply?
2. With A2, does the check still cost more than 25 % of apply CPU when several applies run at once, where overlap cannot hide CPU? Only then is the boundary-only check (A1a) built.

## Frozen decision rule

From PATCH-APPLY-002 §6, inherited unchanged by PATCH-APPLY-003 §2, with the statistical override of §2.1 and the guardrail of §2.2. Both protocols were merged before any run (#200 at `0a5e637`). Bounds apply per platform and are counted across linux-x64, linux-arm64 and win-x64.

1. **A2 (overlap).** The statistic is the upper 95 % CI bound of the wall-time overhead of `overlap` over `off`: the median over files of per-file ratios, with the paired repetition-block bootstrap of §2.1 (10,000 resamples, seed 20260928).
   - **ADOPT** A2 when that bound is at most 10 % on at least two platforms and, on every measured platform, every output is identical and the memory lane stays at or below 64 MiB over idle for every file.
   - If the wall bound holds but the memory or output condition fails, **DEFER**.
   - If the wall bound exceeds 10 % on at least two platforms, **REJECT**.
   - **Guardrail (§2.2).** A platform whose primary bound is at most 10 % but whose corpus-sum wall overhead or median wall overhead over files of at least 1 MiB is above 10 % is *conflicted*. A conflicted platform cannot vote ADOPT; the other two platforms may still adopt.
2. **A1a (boundary-only check).** Needed only if, with the lane adopted by rule 1, the CPU overhead of the check in the many-file lane at c = 8 (median over repetitions) exceeds 25 % of apply CPU on at least two platforms. Then A1a is built in its own pull request, measured by this protocol as lane `boundary`, and this rule is evaluated again on it. A platform with more than eight logical processors is `not evaluated` (PATCH-APPLY-003 §3).
3. **Public option.** Option 1 of #168 reopens only if rule 1 rejects A2, or if rule 2 still holds after A1a. Otherwise #168 is closed with option 3.

## Compared lanes

All lanes use `CspApplier` at `d62a2f1` through its internal `ChunkingCheck` switch; the public `ApplyAsync` surface is unchanged.

| Lane | Construction |
| --- | --- |
| `off` (baseline) | `ChunkingCheck.Off`: no re-chunk check |
| `seq` (reference) | `ChunkingCheck.Sequential`, today's default: after the last record and the length check, `ChunkManifest.VerifyAsync` re-reads the temporary file and re-parses the embedded CSM |
| `overlap` (candidate, A2) | `ChunkingCheck.Overlapped`: every verified chunk is also copied, in target order, into a bounded `System.IO.Pipelines` pipe (1 MiB pause bound) that a concurrently running `ChunkManifest.VerifyAsync` reads; no re-read of the temporary file |

## Included runs

One dispatch of `patch-lab.yml` with `lanes=apply-check` from `main` at `d62a2f1d472799d55c29f623571f540b15005385` (#201): workflow run [36550787928](https://github.com/definitely-stable/ChunkShift/actions/runs/36550787928), run number 19, started 2026-09-29 09:41 UTC. All three platform jobs and the summary job succeeded. Every job ran .NET 10.0.12 (JIT, Release) and CPython 3.14.7.

| RunId | Job id | Runner image | OS (result JSON) | Processor (result JSON) | Lane step |
| --- | --- | --- | --- | --- | --- |
| `PATCH-APPLY-003/RUN-20260929-19-d62a2f1-linux-x64` | 109348330987 | `ubuntu-24.04` | Ubuntu 24.04.5 LTS, X64 | Intel(R) Xeon(R) 6973P-C, 4 logical | 09:43–10:19 UTC |
| `PATCH-APPLY-003/RUN-20260929-19-d62a2f1-linux-arm64` | 109348330975 | `ubuntu-24.04-arm` | Ubuntu 24.04.5 LTS, Arm64 | Arm64 (Ubuntu 24.04.5 LTS), 4 logical | 09:43–10:28 UTC |
| `PATCH-APPLY-003/RUN-20260929-19-d62a2f1-win-x64` | 109348331000 | `windows-2025` | Microsoft Windows 10.0.26100, X64 | AMD64 Family 25 Model 17 Stepping 1, AuthenticAMD, 4 logical | 09:45–14:31 UTC |

The summary job `patch-lab-summary` (id 109459495323) ran `summarize_apply_check.py --require-all-platforms` over the three platform artifacts and wrote the verdict. Every platform has processorCount 4, so the c = 8 guard of §3 lets rule 2 read all three.

Dataset: [data/PATCH-APPLY-003-20260929-001/](data/PATCH-APPLY-003-20260929-001/).

- `PATCH-APPLY-003-compact-{linux-x64,linux-arm64,win-x64}.json`: the compact samples of §3 (per file and lane the medians over the ten repetitions, per repetition and c the many-file values, per file the memory excess over idle), byte for byte as the summary job wrote them.
- `apply-check-verdict.json`: the summary job's verdict, byte for byte.
- `PATCH-APPLY-003-rule1-wall.jsonl`: what rule 1 consumes that the compact files do not hold. For every platform, file and repetition it has the two measured wall times of each lane, and for every repetition the output-identity bit (`outputsVerified`). It was written by `build_rule1_wall.py` from the 30 raw time documents of the three platform artifacts; the build is deterministic.
- `SHA256SUMS` of those five data files.
- `recompute.py` (standard library, Python ≥ 3.12). It checks `SHA256SUMS`, then recomputes from the committed files:
  - every point estimate of rule 1 and its companions, the CPU statistics, the concurrency statistics with their bootstrap intervals and the memory maxima, from the compact files;
  - the per-file medians of the compact files, and the paired repetition-block bootstrap of §2.1 (10,000 resamples, seed 20260928, the summarizer's random stream) for the primary statistic and both companions of lane `overlap`, from the per-repetition wall times.

  Each value must equal the verdict JSON exactly; the script then applies rules 1–3 to the recomputed values. The block bootstrap takes a few minutes.

The raw CI artifacts (the 22 result JSON files of each platform) expire on 2026-12-28:

| artifact | id | SHA-256 of the zip |
| --- | --- | --- |
| `patch-lab-apply-check-linux-x64` | 11027825695 | `81a21663…cc1f` |
| `patch-lab-apply-check-linux-arm64` | 11027682829 | `7ba9db38…acac` |
| `patch-lab-apply-check-win-x64` | 11040817621 | `19d0f2d1…c4d3` |
| `patch-lab-summary` | 11042120028 | `772747d2…c63d` |

## Reproduction

```text
gh workflow run patch-lab.yml --ref main -f lanes=apply-check
python3 docs/research/results/data/PATCH-APPLY-003-20260929-001/recompute.py
```

`PATCH-APPLY-003-rule1-wall.jsonl` from the raw platform artifacts:

```text
python3 docs/research/results/data/PATCH-APPLY-003-20260929-001/build_rule1_wall.py \
  apply-check-linux-x64 apply-check-linux-arm64 apply-check-win-x64 --output PATCH-APPLY-003-rule1-wall.jsonl
```

From the raw artifacts, with the summarizer and corpus lock of `d62a2f1` (unchanged on `main` when this record was written):

```text
python3 benchmarks/scripts/summarize_apply_check.py apply-check-linux-x64 apply-check-linux-arm64 apply-check-win-x64 \
  --corpus-lock docs/benchmarks/patch-corpus/corpus-lock.json \
  --output apply-check-verdict.json --markdown apply-check-summary.md --compact-dir compact --require-all-platforms
```

Python 3.12 or later is required for bit-identical sums: from 3.12 on, `sum()` over floats is compensated, and the workflow ran 3.14.

## Semantic / compatibility checks

- [x] Output identity: on every platform, in every one of the ten time repetitions, the first measured apply of each lane reproduced the corpus SHA-256 of all 1,893 targets (`outputsVerified: true` in all 30 time documents).
- [x] One commit, one corpus, one dispatch: all 66 result documents record `gitCommit d62a2f1…`, `corpusPairsSha256 8b3b92a9…22dd` (the corpus lock) and a RunId of run number 19 for their own platform.
- [x] Complete: repetitions 0–9 of `time` and of `concurrent` on every platform, both passes of every lane at c ∈ {1, 2, 4, 8}, `cacheState: "warm"`, and one memory document of 75 files per platform with `memoryEnvironment` empty (no allocator or GC variable set).
- [x] No format, profile, hash or identity change: the lanes differ only in the internal check switch. The implementation PR #201 ran the 125 CSP vectors, the P6 failure matrix, the short-read set and the apply-fuzz set in every mode (PATCH-APPLY-002 §7).
- [x] x64 and ARM64 measured.
- [ ] NativeAOT is not part of this protocol; the PR that switches the default runs the Patching package smoke and a NativeAOT smoke.
- [x] Public API unchanged.

**Provenance checks, all passed:**

| # | check |
| ---: | --- |
| 1 | The committed compact files have the SHA-256 the summary job printed (`sha256sum`): `6627f56b…9310` (linux-x64), `7151d72d…0704` (linux-arm64), `c34e57c1…ff53` (win-x64). The Linux platform jobs printed the same two values (`compact-samples-sha256=`). The win-x64 job printed `a0934bff…99e4` for its own compact file, which Python wrote there with CRLF line ends (33,561 of them); with CR removed that file is byte-identical to the committed one. The content is the same; only the line ends differ. |
| 2 | Each artifact zip has the digest its upload step printed. |
| 3 | The per-platform summaries in the artifacts match the ones printed in the job logs, and each platform section of the summary job's `apply-check-summary.md` equals its platform job's summary (win-x64 after CRLF → LF). The summary job's verdict for each platform equals the platform job's verdict except the input directory path. |
| 4 | `summarize_apply_check.py` from `main`, rerun locally (CPython 3.13) over each platform's raw documents, reproduces that platform's compact file and summary byte for byte (win-x64 after CRLF → LF), and its verdict JSON equal to the job's except the input directory path. |
| 5 | The same rerun over all three platforms with `--require-all-platforms` reproduces the three committed compact files and `apply-check-summary.md` byte for byte, and `apply-check-verdict.json` equal to the committed one except the input directory paths; this reproduces the rule-1 intervals, which the compact files cannot. |
| 6 | `recompute.py` over the committed files: every recomputed value equals the verdict JSON, including the rule-1 intervals rebuilt from `PATCH-APPLY-003-rule1-wall.jsonl` (for example the primary upper bounds 0.04357439642477509, 0.057781649245064015 and 0.023575193663555847). |

## Results

Percent rounded to two decimals; every value in full precision is in `apply-check-verdict.json`. Intervals are the paired repetition-block bootstrap of §2.1 unless the row says otherwise.

### Rule 1: wall overhead of `overlap` over `off`, per-file lane

| platform | primary: median of per-file ratios [95 % CI] | corpus sum | median, files ≥ 1 MiB (75) | clean pass |
| --- | ---: | ---: | ---: | --- |
| linux-x64 | 3.55 % [3.20 %, **4.36 %**] | 3.25 % | 3.24 % | yes |
| linux-arm64 | 5.42 % [5.02 %, **5.78 %**] | 2.48 % | 2.31 % | yes |
| win-x64 | 0.94 % [−0.15 %, **2.36 %**] | 1.94 % | 4.20 % | yes |

The three statistics of §2.2 without rounding, as the verdict JSON holds them:

| platform | primary median | primary upper bound | corpus-sum overhead | median, files ≥ 1 MiB |
| --- | --- | --- | --- | --- |
| linux-x64 | 0.03552268546903736 | 0.04357439642477509 | 0.03250379425374317 | 0.03243719766723441 |
| linux-arm64 | 0.054249393755683606 | 0.057781649245064015 | 0.02475270039387345 | 0.02313125935854954 |
| win-x64 | 0.009394668641244053 | 0.023575193663555847 | 0.019379793625514008 | 0.041956989646435794 |

No platform is conflicted. Outputs are verified on all three. The largest memory excess over idle of any file in lane `overlap`:

| platform | idle | `off` worst | `seq` worst | `overlap` worst | ≤ 64 MiB |
| --- | ---: | ---: | ---: | ---: | --- |
| linux-x64 | 34.1 MiB | +41.1 MiB | +37.8 MiB | **+43.8 MiB** | yes |
| linux-arm64 | 33.6 MiB | +39.3 MiB | +39.2 MiB | **+44.6 MiB** | yes |
| win-x64 | 24.2 MiB | +26.2 MiB | +27.8 MiB | **+31.7 MiB** | yes |

The worst file is the `node-linux-x64` `bin/node` pair on every platform and in every lane. Per file, `overlap` peaks at most 3.84 MiB (linux-x64), 5.25 MiB (linux-arm64) and 5.51 MiB (win-x64) above `off`, with medians of 1.89, 1.68 and 1.19 MiB.

For reference (no rule reads it), `seq` over `off` in the same lane: 7.61 % [7.28 %, 8.48 %], 11.49 % [11.52 %, 12.80 %] and 1.71 % [0.46 %, 2.75 %] per-file median; 14.61 %, 18.24 % and 3.61 % corpus sum; 16.15 %, 26.60 % and 14.50 % over files ≥ 1 MiB.

### Rule 2: CPU overhead in the many-file lane

Median over the ten repetitions of `cpu(lane) / cpu(off) − 1` per pass pair, with the percentile bootstrap over repetitions (10,000 resamples, seed 20260928):

| c | linux-x64 `overlap` | linux-arm64 `overlap` | win-x64 `overlap` | linux-x64 `seq` | linux-arm64 `seq` | win-x64 `seq` |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 44.52 % [13.39, 60.77] | 31.30 % [16.11, 44.79] | 28.40 % [14.54, 38.04] | 28.27 % | 28.58 % | 20.45 % |
| 2 | 31.20 % [25.25, 32.09] | 21.85 % [19.89, 22.67] | 23.18 % [21.02, 28.89] | 33.05 % | 27.25 % | 24.84 % |
| 4 | 31.72 % [26.98, 37.75] | 28.50 % [27.10, 29.32] | 20.95 % [10.20, 26.17] | 33.52 % | 28.14 % | 20.32 % |
| **8** | **36.74 %** [29.91, 38.61] | **26.63 %** [24.49, 28.58] | **19.97 %** [14.28, 24.47] | 34.91 % | 29.78 % | 27.55 % |

Rule 2 reads the lane rule 1 adopts, `overlap`, at c = 8. Without rounding: 0.36743521834927806 (linux-x64), 0.2663160856283532 (linux-arm64), 0.19974725009085514 (win-x64). Two platforms are above 25 %.

### Where the check's cost goes

Per-file lane, sums over the corpus of per-file medians (seconds):

| platform | wall `off` / `seq` / `overlap` | CPU `off` / `seq` / `overlap` | CPU corpus-sum overhead `seq` / `overlap` |
| --- | --- | --- | --- |
| linux-x64 | 4.97 / 5.69 / 5.13 | 3.77 / 5.08 / 5.24 | 34.71 % / 38.81 % |
| linux-arm64 | 7.59 / 8.98 / 7.78 | 7.01 / 9.15 / 9.21 | 30.45 % / 31.28 % |
| win-x64 | 34.22 / 35.46 / 34.89 | 2.66 / 3.98 / 3.77 | see Limitations |

A2 removes most of the check's wall time but not its CPU: on Linux the overlapped check costs as much CPU as the sequential one, or more (the pipe copy and the second stream view replace the re-read). The decomposition of the sequential check, share of its wall time:

| component | linux-x64 | linux-arm64 | win-x64 |
| --- | ---: | ---: | ---: |
| CSM re-parse | 3.89 % | 2.76 % | 2.54 % |
| temporary-file re-read | 17.04 % | 14.20 % | 51.92 % |
| boundary scan | 51.05 % | 21.64 % | 31.57 % |
| hashing | 26.85 % | 51.21 % | 24.46 % |
| ManifestId | 0.71 % | 0.86 % | 0.50 % |

Record only (input to #148): 21.55 % of the target bytes lie in records whose `ChunkId` is the base record at the same offset, and 21.38 % in whole 4 KiB blocks inside those extents (identical on every platform, as the corpus is).

## Exclusions / invalid runs

None. No run of `PATCH-APPLY-003` was invalid or discarded; run 36550787928 is the only dispatch.

## Limitations

- **win-x64 wall time is mostly waiting.** Apply over the corpus takes 34.22 s of wall time for 2.66 s of process CPU in lane `off` (linux-x64: 4.97 s for 3.77 s; linux-arm64: 7.59 s for 7.01 s). The lab does not split that time; the flush to disk of D12 is the likely cause, not a measured one. The check's wall share is diluted accordingly, so the win-x64 bound (2.36 %) says little about the check's cost there; the protocol reads ratios only (PATCH-APPLY-003 §4).
- **win-x64 per-apply CPU is quantized** by the 15.625 ms clock tick. Only 116 files have a nonzero `off` CPU median and 13 are above the 20 ms floor. The CPU corpus-sum point estimates lie outside their own intervals (`seq` 29.96 % against [−7.51 %, 21.67 %], `overlap` 22.76 % against [−12.91 %, 15.88 %]), because resamples move files in and out of the nonzero set. No rule reads these statistics; rule 2 reads whole-pass totals of about 6 CPU seconds on win-x64.
- On linux-arm64 the `seq` primary point estimate, 11.49 %, lies just below its interval [11.52 %, 12.80 %], a known property of a percentile bootstrap of medians over ten values. No rule reads `seq`.
- The CPU median of per-file ratios rests on 12 (linux-x64), 24 (linux-arm64) and 13 (win-x64) files above the 20 ms floor.
- With eight applies in flight, the overlapped check adds 11.80 % [6.38 %, 14.29 %] of wall time on linux-arm64 (2.84 % and 2.95 % elsewhere). Rule 1 reads the per-file lane, not this one, and CPU saturation at c = 8 is what rule 2 measures.
- The c = 1 CPU intervals are wide (up to 60 points); at c = 8, the level rule 2 reads, they are 4–10 points wide.
- The raw artifacts expire on 2026-12-28. Every quantity that decides rules 1–3 stays recomputable from the committed files: the rule-1 intervals from `PATCH-APPLY-003-rule1-wall.jsonl`, the rest from the compact files. The per-file CPU intervals and the check decomposition, which no rule reads, need the raw artifacts.
- Shared runners, one dispatch; the paired, rotated, repeated design of PATCH-APPLY-002 §5 is what absorbs runner noise.

## Decision

```text
Rule 1: ADOPT A2 (overlap) — bound met on 3 of 3 platforms, no conflict, outputs identical, memory within 64 MiB
Rule 2: A1a required — overlap CPU overhead at c = 8 above 25 % on linux-x64 and linux-arm64
Rule 3: pending A1a — #168 is not closed; option 1 reopens only if rule 2 still holds after A1a
```

Rule 1: the upper bound of the primary statistic is 4.36 %, 5.78 % and 2.36 %, at most 10 % on all three platforms, and all six companion values are at most 4.20 %. Every output was identical and the worst `overlap` peak is +44.6 MiB. A2 is adopted: the overlapped check becomes the apply default.

Rule 2: with `overlap` at c = 8 the check costs 36.74 % (linux-x64) and 26.63 % (linux-arm64) of apply CPU, above 25 % on two platforms; win-x64 is at 19.97 %. A1a is built and measured as lane `boundary`, and rule 2 is evaluated again on it.

Rule 3: A2 is not rejected, and rule 2 has not yet been evaluated after A1a, so #168 stays open with option 3 in force.

## Consequences

- A pull request makes `ChunkingCheck.Overlapped` the apply default (`DefaultChunkingCheck`), moves D13 to confirmed for A2, and runs the Patching package smoke and a NativeAOT smoke against JIT.
- A second pull request builds A1a: an internal Gear boundary scanner for `fastcdc.gear.chunkshift.v1.64k` only, compared with the embedded record lengths without hashing, cross-checked against `ChunkScanner` and the FastCDC vectors; any other registered profile falls back to the full check. Lane `boundary` is then measured under this ExperimentId and rule 2 is evaluated on it.
- #168 stays open until then.

## References

- #182, #168 ("Decision — 2026-09-28"), #7; #200, #201;
- [PATCH-APPLY-002-PROTOCOL.md](../../benchmarks/PATCH-APPLY-002-PROTOCOL.md), [PATCH-APPLY-003-PROTOCOL.md](../../benchmarks/PATCH-APPLY-003-PROTOCOL.md);
- PATCHING-DECISIONS D2, D9, D12, D13, D21;
- prior evidence: `PATCH-APPLY-001/EVIDENCE-20260928-001`, `PATCH-APPLY-001/EVIDENCE-20260928-002` (rule A1).
