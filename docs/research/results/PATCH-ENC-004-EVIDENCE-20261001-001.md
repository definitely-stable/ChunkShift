# PATCH-ENC-004 — two encode workers halve create time with the same patch bytes

EvidenceId: `PATCH-ENC-004/EVIDENCE-20261001-001`  
Status: `ADOPT` (workers: H2-W2) · `REJECT` (H1 base-chunk cache)  
Owning issue(s): #181, #7  
Implementation PR(s): #212 (frozen protocol), #211 (executions and lab lanes); the default change follows this record  
Date: 2026-10-01

## Hypothesis

From [PATCH-ENC-004-PROTOCOL.md](../../benchmarks/PATCH-ENC-004-PROTOCOL.md) §1: create wall time falls by at least 1.5× without changing a single patch byte, by encoding independent payload entries on bounded workers and writing them in order. Base reads fall by at least 2× with a bounded cache of base chunks (H1), again without changing a byte.

## Frozen decision rule

From the protocol §7, merged in #212 before any decision run; the thresholds are those of #181.

1. **Bytes.** A lane whose patch differs from H0's on any file of any platform is REJECT.
2. **H1** is ADOPT when rule 1 holds on all three platforms, `R(H0) / R(H1) ≥ 2`, `T(H1, p) ≤ T(H0, p)` on every platform (`T(H0, p)` = mean of the two H0 lanes, no tolerance) and `M(H1, p) ≤ 64 MiB` on every platform. A failed condition → REJECT; a missing platform or memory run → INCOMPLETE.
3. **Workers.** Both families are evaluated; H3 is eligible only when H1 is ADOPT. A lane qualifies with `T(H0) / T(lane) ≥ 1.5` on at least two of the three platforms, rule 1 on all three, and the §3.3 memory bound `64 + (W − 1) × 32 MiB` on all three. ADOPT the qualifying lane with the smallest W (at equal W, H3 before H2); none → REJECT; missing data and none → INCOMPLETE.

A run is invalid (and repeated once) when a job fails, a platform or memory lane is missing, or the two H0 lanes of a platform differ in total create time by more than 25 % (§5).

## Compared lanes

All `CspEncoderPolicy.Default` (level 19, K = 4, 8 candidates, 256 KiB, raw prefix, hash/chain log ≤ 20). Only the internal `CspCreateExecution` differs.

| id | lab execution | change |
| --- | --- | --- |
| H0 | `h0` (twice: first and last) | the current default, sequential; the byte oracle |
| H1 | `h1` | H0 plus the bounded sliding base-chunk cache (§3.1) |
| H2-W{n} | `h2-w1`, `h2-w2`, `h2-w4`, `h2-w8` | n encode workers, base reads serialized by one lock (§3.2) |
| H3-W{n} | `h3-w1`, `h3-w2`, `h3-w4`, `h3-w8` | n encode workers reading dictionaries from the cache (§3.2) |

## Included runs

One dispatch of `patch-lab.yml` on `main` at `844d1e7c931877072eea804164fd145defbdd2c8` (after #212 and #211), `lanes=create-throughput,create-memory`, `experiment-id=PATCH-ENC-004`, the default execution order `h0 h1 h2-w1 h2-w2 h2-w4 h2-w8 h3-w1 h3-w2 h3-w4 h3-w8 h0`, the whole frozen corpus (`pairsSha256 8b3b92a9…22dd`, 1893 files), JIT, .NET 10.0.12, workflow run [36806102359](https://github.com/definitely-stable/ChunkShift/actions/runs/36806102359).

| RunId | runner | CPU | throughput job | memory job |
| --- | --- | --- | --- | --- |
| `PATCH-ENC-004/RUN-20261001-22-844d1e7-linux-x64` | `ubuntu-24.04` | AMD EPYC 7763, 4 logical | 110190672913 | 110190672869 |
| `PATCH-ENC-004/RUN-20261001-22-844d1e7-linux-arm64` | `ubuntu-24.04-arm` | Arm64, 4 logical | 110190672878 | 110190672902 |
| `PATCH-ENC-004/RUN-20261001-22-844d1e7-win-x64` | `windows-2025` | AMD Family 25 Model 1, 4 logical | 110190672784 | 110190672951 |

Every job succeeded. The evaluator ran with `--require-all-platforms --require-memory` in the workflow's summary job and again locally on the downloaded artifacts; both report `valid: true`, `decisionGrade: true`, no invalid reasons and the same decision.

Raw evidence: the run's artifacts `patch-lab-create-throughput-<platform>-runs` and `patch-lab-create-memory-<platform>-runs` (per-file documents, 67 MiB unzipped; retained until 2026-12-30). Committed in [data/PATCH-ENC-004-20261001-001/](data/PATCH-ENC-004-20261001-001/):

- `verdict.json`, `summary.md`: the evaluator's output (`summarize_create_throughput.py decide`);
- `totals.json`: per platform and lane (both H0 lanes kept apart) the corpus totals of every §6 metric and `patchesSha256`;
- `patches.json`: the per-file patch SHA-256 of H0 and, per platform and lane, the number of files that differ from H0;
- `memory.json`: per platform and lane the per-file create peak over idle for every file of at least 1 MiB, `M` and the bound;
- `decide.py`: recomputes the §7 verdict from those three files and checks it against `verdict.json`.

## Reproduction

```text
gh workflow run patch-lab.yml --ref main -f lanes=create-throughput,create-memory -f experiment-id=PATCH-ENC-004
gh run download 36806102359 -p 'patch-lab-create-*-runs' -D evidence
python3 benchmarks/scripts/summarize_create_throughput.py decide evidence/*/ \
  --corpus-lock docs/benchmarks/patch-corpus/corpus-lock.json --require-all-platforms --require-memory
python3 docs/research/results/data/PATCH-ENC-004-20261001-001/decide.py
```

## Semantic / compatibility checks

- [x] **byte oracle:** every lane made H0's patch, file by file, on every platform (1893 files × 10 lanes × 3 platforms, no mismatch);
- [x] H0's patches are identical across linux-x64, linux-arm64 and win-x64 (`patchesSha256 4a1f5c27…81c6` on all three);
- [x] the first H0 lane applied every patch and checked the target SHA-256;
- [x] no CSP format, identity, hash, profile or public API change; the public API still runs H0 (the execution is internal, D9);
- [x] the creation, short-read, P6 failure-point and P9 fuzz tests are unchanged and pass (#211).

The adopting PR runs the Patching package smoke under JIT and NativeAOT, the committed CSP vectors, `generate.py --verify`, `decode.py` over the created scenario patches and `decode.py --compare-frames` (protocol §7).

## Results

### H0 validity

| platform | H0 #1 s | H0 #2 s | spread | `T(H0)` s |
| --- | ---: | ---: | ---: | ---: |
| linux-x64 | 1499.30 | 1465.17 | 2.3 % | 1482.24 |
| linux-arm64 | 1401.54 | 1356.38 | 3.3 % | 1378.96 |
| win-x64 | 1334.88 | 1241.31 | 7.5 % | 1288.10 |

All within the 25 % of §5: the run is valid.

### H1: base reads and time

| platform | `R(H0)` MiB | `R(H1)` MiB | ratio | seeks H0 → H1 | `T(H1)` s | vs `T(H0)` | `M(H1)` MiB | rule |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| linux-x64 | 9399.5 | 603.6 | 15.57 | 123057 → 2377 | 1472.06 | −0.7 % | 53.3 | met |
| linux-arm64 | 9399.5 | 603.6 | 15.57 | 123057 → 2377 | 1424.13 | **+3.3 %** | 49.8 | time not met |
| win-x64 | 9399.5 | 603.6 | 15.57 | 123057 → 2377 | 1294.28 | **+0.5 %** | 31.1 | time not met |

The cache held at most 11 records (`MaxCandidates + K − 1`), 1.46 MiB, on every platform. Bytes are identical and the read ratio (15.6×) and memory conditions hold everywhere; the no-regression condition fails on linux-arm64 and win-x64.

### Workers: create time over the corpus (s) and speedup `T(H0) / T(lane)`

| lane | linux-x64 | linux-arm64 | win-x64 | ≥ 1.5× on | eligible | qualifies |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| H2-W1 | 1486.37 (1.00) | 1397.71 (0.99) | 1306.75 (0.99) | 0 | yes | no |
| **H2-W2** | 796.36 (**1.86**) | 732.30 (**1.88**) | 712.52 (**1.81**) | 3 | yes | **yes** |
| H2-W4 | 577.68 (2.57) | 390.33 (3.53) | 536.49 (2.40) | 3 | yes | yes |
| H2-W8 | 639.41 (2.32) | 422.38 (3.26) | 537.33 (2.40) | 3 | yes | yes |
| H3-W1 | 1506.20 (0.98) | 1462.44 (0.94) | 1276.60 (1.01) | 0 | no (H1 REJECT) | — |
| H3-W2 | 782.85 (1.89) | 660.93 (2.09) | 678.20 (1.90) | 3 | no (H1 REJECT) | — |
| H3-W4 | 585.47 (2.53) | 358.11 (3.85) | 527.30 (2.44) | 3 | no (H1 REJECT) | — |
| H3-W8 | 630.33 (2.35) | 363.48 (3.79) | 532.51 (2.42) | 3 | no (H1 REJECT) | — |

### Workers: CPU, memory and the reorder window

| lane | eff. cores x64 / arm64 / win | `M` MiB x64 / arm64 / win | bound MiB | reorder peak entries (max) | reorder peak bytes (max) |
| --- | --- | --- | ---: | ---: | ---: |
| H0 | 1.01 / 1.01 / 0.99 | 53.5 / 51.1 / 30.6 | 64 | 0 | 0 |
| H2-W1 | 1.01 / 1.01 / 1.00 | 54.9 / 53.8 / 31.1 | 64 | 1 | 111,373 |
| H2-W2 | 1.97 / 1.97 / 1.96 | 70.2 / 69.2 / 45.4 | 96 | 8 | 162,625 |
| H2-W4 | 3.72 / 3.65 / 3.63 | 101.1 / 95.5 / 71.7 | 160 | 13 | 304,027 |
| H2-W8 | 3.72 / 3.67 / 3.64 | 158.7 / 152.0 / 127.0 | 288 | 25 | 425,563 |
| H3-W2 | 1.97 / 1.97 / 1.95 | 73.1 / 69.8 / 45.8 | 96 | 7 | 162,625 |
| H3-W4 | 3.72 / 3.64 / 3.63 | 104.8 / 97.2 / 74.8 | 160 | 13 | 461,343 |
| H3-W8 | 3.75 / 3.66 / 3.63 | 166.6 / 148.6 / 123.6 | 288 | 30 | 1,010,817 |

Every lane met its memory bound on every platform, and every reorder peak stayed inside its window (`4 × W` entries, `W × 4 MiB`). Managed allocations grow from 13.2 GiB (H0) to 14.3 GiB (H2-W2) over the corpus. Total CPU seconds of H2-W2 exceed H0's by 4.0 % (linux-x64), 2.5 % (linux-arm64) and 5.9 % (win-x64): the second worker adds parallelism, little work.

## Exclusions / invalid runs

- None of the decision data is invalid.
- Excluded as exploratory (§8): the local run over 313 files before the protocol merged, and the branch smoke run [36734596803](https://github.com/definitely-stable/ChunkShift/actions/runs/36734596803) (`lanes=create-throughput` only, commit `3bf4f4e`). Both agreed with this run on bytes and on the direction of every result.
- The legacy `memory` group also ran in this dispatch, because the workflow's `contains(lanes, 'memory')` matches `create-memory`. Its jobs measure the `csp` lane for `PATCH-APPLY-001` and are not PATCH-ENC-004 data.

## Limitations

- **H1 is rejected by the strict no-regression condition, not by a measured slowdown.** Its two failing deltas (+3.3 % on linux-arm64, +0.5 % on win-x64) are no larger than the spread between the two H0 lanes of the same job (3.3 %, 7.5 %), and H1 is 0.7 % faster on linux-x64. The protocol adds no tolerance, so the verdict stands. What this run shows is that cutting base reads 15.6× (9.2 GiB → 0.6 GiB, 52× fewer seeks) buys no create time when the base is a local file in the page cache; it may on a slow or remote base stream, which this corpus does not measure. A new ExperimentId would be needed to test that.
- Because H1 is rejected, H3 is not eligible, although H3-W2 is faster than H2-W2 on every platform (1.7 % linux-x64, 10.8 % linux-arm64, 5.1 % win-x64).
- The runners have 4 logical processors. W = 4 keeps 3.6–3.7 cores busy everywhere but reaches 3.5× only on arm64 and 2.4–2.6× on the x64 runners, which suggests SMT siblings rather than 4 full cores there (inferred, not measured); W = 8 adds nothing on any runner. The rule adopts the smallest qualifying W; W = 2 already meets 1.5× on all three platforms. Larger machines are not represented.
- Create times come from shared GitHub-hosted runners in one dispatch; the H0 spread bounds their noise in this run.
- The `MiB/s` columns of the evaluator's `summary.md` for H0 divide by the first H0 lane's time rather than `T(H0)`. They are not used by the rule; the speedups and decision use `T(H0)`.

## Decision

```text
H1 (base-chunk cache): REJECT
Workers: ADOPT H2-W2 (two encode workers, no cache)
```

- **Bytes:** every lane is byte-identical to H0 on all three platforms; no lane is rejected by rule 1.
- **H1:** bytes identical, `R(H0) / R(H1) = 15.57 ≥ 2`, `M(H1) ≤ 64 MiB` everywhere, but `T(H1, p) > T(H0, p)` on linux-arm64 and win-x64 → REJECT.
- **Workers:** H3 is not eligible. H2-W1 does not qualify; H2-W2, H2-W4 and H2-W8 qualify (≥ 1.5× on all three platforms, identical bytes, memory within bound). The smallest qualifying W is 2 → ADOPT H2-W2.

## Consequences

- A follow-up PR makes H2-W2 the internal default of `CspPatchBuilder` (`CspCreateExecution(WorkerCount: 2, UseBaseCandidateCache: false)`), with the checks the protocol §7 lists: Patching package smoke under JIT and NativeAOT, committed CSP vectors, `generate.py --verify`, `decode.py` over the created scenario patches and `decode.py --compare-frames`. No vector changes, since encoder bytes do not change. D15 (create execution) and D17 (the create memory bound becomes `64 + 32 = 96 MiB` for two workers) are updated there.
- The H1 cache stays internal and off; it is not removed by this record.
- The workflow's `memory` substring match is fixed separately.

## References

- #181; #7; #211; #212; [PATCH-ENC-004-PROTOCOL.md](../../benchmarks/PATCH-ENC-004-PROTOCOL.md);
- `PATCH-ENC-003/EVIDENCE-20260929-001` (the per-encoder bound); `PATCH-APPLY-001/EVIDENCE-20260929-001` (the D17 bound of the default).
