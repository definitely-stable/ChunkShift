# PATCH-ENC-003 — a raw prefix on a static context bounds the create peak

EvidenceId: `PATCH-ENC-003/EVIDENCE-20260929-001`  
Status: `ADOPT`  
Owning issue(s): #7  
Implementation PR(s): #179 (lab), #190 (frozen protocol); the policy change follows this record  
Date: 2026-09-29

## Hypothesis

From [PATCH-ENC-003-PROTOCOL.md](../../benchmarks/PATCH-ENC-003-PROTOCOL.md) §1: each dictionary goes to zstd as a raw prefix (`ZSTD_CCtx_refPrefix`) on one static context whose workspace the encoder owns (`ZSTD_initStaticCCtx`, grown to the largest estimate and never shrunk), and the level-19 match tables of dictionary entries are capped. This keeps the create peak of every corpus file of at least 1 MiB within 64 MiB over idle on Linux x64, Linux ARM64 and Windows x64. Patches are at most 3 % larger and create is at most 1.25× slower than with the current default.

## Frozen decision rule

From the protocol §5, merged in #190 before any decision run. A candidate c is eligible when all of these hold:

1. memory: `M(c, p) ≤ 64 MiB` and `A(c, p) ≤ 64 MiB` on every platform p;
2. size: `B_cal(c) ≤ 1.03 × B_cal(D)` and `B_hold(c) ≤ 1.03 × B_hold(D)`;
3. time: `T(c) ≤ 1.25 × T(D)`, where `T(D)` is the mean of the two D lanes.

The winner is the eligible candidate with the smallest `B_cal + B_hold`; a tie goes to the smaller largest `M(c, p)`. A winner → ADOPT. No winner, but some candidate meets rule 1 → DEFER. No candidate meets rule 1 → REJECT. The run is invalid if a job fails or the two D lanes differ in time by more than 25 %.

## Compared lanes

All level 19, K = 4, 8 candidates, 256 KiB.

| id | lane | dictionary handling |
| --- | --- | --- |
| D | `enc-L19-K4-C8-copy` | `CspEncoderPolicy.Default`: `LoadDictionary`, a CDict per candidate |
| P0 | `enc-L19-K4-C8-prefix` | raw prefix on the static context, no cap |
| P1 | `enc-L19-K4-C8-prefix-H20C21` | P0, hash log ≤ 20, chain log ≤ 21 |
| P2 | `enc-L19-K4-C8-prefix-H20C20` | P0, hash log ≤ 20, chain log ≤ 20 |
| P3 | `enc-L19-K4-C8-prefix-H19C19` | P0, hash log ≤ 19, chain log ≤ 19 |

## Included runs

All at commit `62c77c8` (main after #190), `patch-lab.yml` with `experiment-id=PATCH-ENC-003`, the whole frozen corpus (`pairsSha256 8b3b92a9…22dd`).

| run | workflow run | RunIds | platforms |
| --- | --- | --- | --- |
| encoder `D P0 P1 P2 P3 D`, 4 workers | 36515484951 | `PATCH-ENC-003/RUN-20260929-12-62c77c8-linux-{x64,arm64}` | `ubuntu-24.04`, `ubuntu-24.04-arm` |
| memory P0 | 36515484951 | `…/RUN-20260929-12-62c77c8-{linux-x64,linux-arm64,win-x64}` | `ubuntu-24.04`, `ubuntu-24.04-arm`, `windows-2025` |
| memory P1 | 36515486862 | `…/RUN-20260929-13-62c77c8-…` | same |
| memory P2 | 36515488913 | `…/RUN-20260929-14-62c77c8-…` | same |
| memory P3 | 36515491024 | `…/RUN-20260929-15-62c77c8-…` | same |

Every job succeeded. Raw evidence: [data/PATCH-ENC-003-20260929-001/](data/PATCH-ENC-003-20260929-001/).

- `memory.json` holds, per candidate and platform, the per-file create and apply excess over idle (MiB, as the job printed it), the idle baseline and the job id.
- `encoder.json` holds, per platform and lane, the bytes and create seconds of both splits and `patchesSha256`, as printed by `print_patch_lab_encoder.py`.
- `decide.py` recomputes every table below and the verdict.

## Reproduction

```text
gh workflow run patch-lab.yml --ref main -f lanes=encoder -f experiment-id=PATCH-ENC-003 \
  -f encoder-lanes="enc-L19-K4-C8-copy enc-L19-K4-C8-prefix enc-L19-K4-C8-prefix-H20C21 enc-L19-K4-C8-prefix-H20C20 enc-L19-K4-C8-prefix-H19C19 enc-L19-K4-C8-copy"
gh workflow run patch-lab.yml --ref main -f lanes=memory -f experiment-id=PATCH-ENC-003 -f memory-lane=<P0 … P3 lane>
python3 docs/research/results/data/PATCH-ENC-003-20260929-001/decide.py
```

## Semantic / compatibility checks

- [x] every created patch reconstructed its target (SHA-256) with the production applier, in the encoder run and in every memory run;
- [x] x64 and ARM64 made identical patches in every lane (`patchesSha256`, six of six);
- [x] the two D lanes made identical patches; D is `CspEncoderPolicy.Default` (#179 compared its patches with `main`'s `csp` lane);
- [x] encoder policy only (D14): no format, identity or public API change; the decoder is not touched.

The adopting PR runs the committed vectors, `generate.py --verify`, `decode.py` over the created scenario patches, `forward_apply.py` and the frame fuzz corpus with frames of the new default (protocol §6).

## Results

### Memory: create and apply peak over idle, files of at least 1 MiB

| cand | platform | `M` worst create | median | files > 64 | `A` worst apply |
| --- | --- | ---: | ---: | ---: | ---: |
| P0 | linux-x64 | **75.6** | 41.0 | 2 | 38.0 |
| P0 | linux-arm64 | **73.0** | 40.2 | 2 | 37.9 |
| P0 | win-x64 | 51.5 | 30.9 | 0 | 28.7 |
| P1 | linux-x64 | 56.8 | 37.6 | 0 | 40.5 |
| P1 | linux-arm64 | 52.7 | 36.3 | 0 | 38.4 |
| P1 | win-x64 | 34.2 | 26.9 | 0 | 27.4 |
| P2 | linux-x64 | 50.2 | 33.0 | 0 | 37.8 |
| P2 | linux-arm64 | 49.5 | 32.5 | 0 | 41.0 |
| P2 | win-x64 | 29.0 | 23.6 | 0 | 26.1 |
| P3 | linux-x64 | 45.6 | 30.0 | 0 | 42.3 |
| P3 | linux-arm64 | 44.6 | 28.2 | 0 | 41.8 |
| P3 | win-x64 | 25.2 | 19.1 | 0 | 30.1 |

MiB, 75 files per run. On Linux the worst file of every candidate is a `node-linux-x64` `bin/node` pair; P0's two files over 64 MiB are both `bin/node` pairs. For comparison, D (the same default as `csp`) peaked at +239.5 / +248.6 MiB with 62 / 51 files over 64 MiB on Linux x64 / ARM64 (`PATCH-APPLY-001/EVIDENCE-20260928-003`, D0); this experiment does not re-measure D's memory.

### Size and time (x64)

The two D lanes differ by 0.3 % in create time (valid run); `T(D)` = 1317.06 s.

| cand | `B_cal` | vs D | `B_hold` | vs D | `T` s | vs `T(D)` | memory | size | time |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- |
| D | 11,860,600 | — | 26,365,088 | — | 1318.90 / 1315.21 | — | — | — | — |
| P0 | 11,860,586 | −0.0001 % | 26,364,114 | −0.004 % | 1281.45 | 0.973× | no | yes | yes |
| P1 | 11,860,586 | −0.0001 % | 26,364,164 | −0.004 % | 1237.45 | 0.940× | yes | yes | yes |
| **P2** | 11,860,274 | −0.003 % | 26,363,364 | −0.007 % | 1328.22 | 1.008× | yes | yes | yes |
| P3 | 11,871,027 | +0.09 % | 26,443,845 | +0.30 % | 1113.77 | 0.846× | yes | yes | yes |

Calibration 1049 files, holdout 844 files. P1, P2 and P3 are eligible. P2 has the smallest `B_cal + B_hold` (38,223,638 bytes, against 38,224,750 for P1 and 38,314,872 for P3).

## Exclusions / invalid runs

- None of the decision runs is invalid.
- Smoke run 36454384579 (lab branch, before #190) is excluded, as the protocol §2 records.

## Limitations

- P2 wins on size by 1,112 bytes over P1 (0.003 %). Both keep the bound with margin, and the rule selects on bytes. P1's create time is 7 % lower in this run, but create times come from shared runners, and on ARM64 P1 and P2 differ by 2 %.
- The Linux worst case of P2 is +50.2 MiB. It is one level-19 workspace sized for the largest dictionary with hash/chain logs capped at 20, plus the pipeline. The frozen corpus already reaches the format's bounds on the Node.js binaries: a 1 MiB dictionary (CSP §5.2) and a 1 MiB window. The workspace estimate depends only on the level, the dictionary size and the entry size, so other inputs are not expected to raise it; this is inferred from zstd's estimates, not measured beyond the corpus. The caps apply only where zstd's own choice exceeds them.
- Memory is measured with one file per child process. A caller that encodes several files in one process reuses the same static context, whose workspace only grows; this is the intended bound.
- Apply is unchanged by construction (control): at most +42.3 MiB in every run.

## Decision

```text
ADOPT P2 (enc-L19-K4-C8-prefix-H20C20)
```

P2 meets all three conditions of the frozen rule and has the smallest total patch bytes of the eligible candidates.

## Consequences

- `CspEncoderPolicy.Default` takes P2's dictionary handling: raw prefix on the encoder-owned static context, hash and chain logs of dictionary entries capped at 20. Level, K, candidates and radius are unchanged (`PATCH-ENC-002`).
- D8 records the low-level zstd API (`ZSTD_initStaticCCtx`, `ZSTD_CCtx_refPrefix`) and the unsafe code it needs. D14, D15 and D17 are updated.
- A2 of `PATCH-APPLY-001` is rerun unchanged (PATCH-PREFREEZE-PROTOCOL §3, lane `csp` = the new default, Linux x64, Linux ARM64 and Windows x64). A2 decides `PATCH-APPLY-001`; this record does not.

## References

- #7; #179; #190; [PATCH-ENC-003-PROTOCOL.md](../../benchmarks/PATCH-ENC-003-PROTOCOL.md);
- `PATCH-APPLY-001/EVIDENCE-20260928-003` (rule 4 of the A2 diagnosis); `PATCH-ENC-002/EVIDENCE-20260928-001`.
