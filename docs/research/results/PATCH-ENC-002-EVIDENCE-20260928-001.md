# PATCH-ENC-002 — four dictionary chunks become the default

EvidenceId: `PATCH-ENC-002/EVIDENCE-20260928-001`  
Status: `ADOPT`  
Owning issue(s): #7  
Implementation PR(s): #165 (lab); the policy change follows this record  
Date: 2026-09-28

## Hypothesis

A different zstd level, dictionary size or dictionary search makes smaller patches than the study's settings (level 19, K = 2, 8 candidates, 256 KiB) at an acceptable create time (PATCHING-DECISIONS D15).

## Frozen decision rule

From `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md` §3, merged at `dba95e4` before any measurement:

- The grid is level ∈ {9, 19} × K ∈ {1, 2, 4} × (8 candidates, 256 KiB | 16 candidates, 1 MiB).
- Measured on the `ubuntu-24.04` x64 lane.
- A setting is eligible when its calibration create time is at most 1.5 × the default's.
- The winner is the eligible setting with the smallest calibration bytes.
- ADOPT the winner if it saves ≥ 2 % of the default's calibration bytes and its holdout bytes are not larger; otherwise ADOPT the current default.

Calibration is the .NET families. Holdout is Node.js, tzdata and the source snapshots.

## Included runs

| RunId | Platform | Raw evidence |
| --- | --- | --- |
| `PATCH-ENC-002/RUN-20260928-1-853c89e-linux-x64` | GitHub `ubuntu-24.04` x64, 4 workers | workflow run 36383093740; [data](data/PATCH-PREFREEZE-20260928/) `x64--patch-lab-sweep-*` |
| `PATCH-ENC-002/RUN-20260928-1-853c89e-linux-arm64` | GitHub `ubuntu-24.04-arm`, 4 workers | `arm64--patch-lab-sweep-*` (byte comparison only) |

## Semantic / compatibility checks

- [x] every sweep setting produced identical patch bytes on x64 and ARM64 (all 12 settings);
- [x] encoder policy is physical (D14): no format, identity or API change.

## Results (x64)

| setting | eligible | calibration create s | calibration MiB | holdout MiB |
| --- | --- | ---: | ---: | ---: |
| L9-K1-C8 | yes | 35.90 | 18.27 | 37.63 |
| L9-K1-C16 | yes | 92.51 | 18.24 | 32.59 |
| L9-K2-C8 | yes | 33.45 | 13.50 | 29.52 |
| L9-K2-C16 | yes | 105.10 | 13.49 | 24.00 |
| L9-K4-C8 | yes | 42.26 | 12.65 | 28.25 |
| L9-K4-C16 | yes | 128.39 | 12.64 | 22.56 |
| L19-K1-C8 | yes | 405.14 | 16.53 | 34.16 |
| L19-K1-C16 | no | 810.14 | 16.50 | 29.32 |
| **L19-K2-C8 (default)** | yes | 461.60 | 12.10 | 26.47 |
| L19-K2-C16 | no | 983.70 | 12.08 | 21.15 |
| **L19-K4-C8 (winner)** | yes | 595.74 | 11.31 | 25.14 |
| L19-K4-C16 | no | 1422.32 | 11.29 | 19.78 |

The winner saves 6.50 % of the default's calibration bytes (minimum 2 %), and its holdout bytes are 5.0 % smaller than the default's (25.14 vs 26.47 MiB).

## Exclusions / invalid runs

None.

## Limitations

- The holdout reacts much more to the wider search than the calibration does.
  - C16 cuts holdout bytes by 13–21 % at equal level and K, but .NET bytes by at most 0.2 %.
  - At L19-K2 the cut is 23.4 % on `node-linux-x64`, 12.4 % on `node-win-x64` and 58.8 % on the source snapshots.
  - The rule selects on calibration, so the wider search was not eligible at level 19 and lost at level 9.
- Level 9 with K = 4 and C16 needs 1/5 of the winner's create time for 12 % more calibration bytes and 10 % fewer holdout bytes. The rule does not trade bytes for time beyond the 1.5× bound, so this is not adopted here. It is the candidate for a follow-up experiment with a new ExperimentId.
- Create times come from shared GitHub runners with 4 workers. Only ratios between settings of the same run are used.

## Decision

```text
ADOPT sweep-L19-K4-C8
```

The winner meets every condition of the frozen rule.

## Consequences

- `CspEncoderPolicy.Default` becomes level 19, 4 dictionary chunks, 8 candidates, 256 KiB.
- D15 is confirmed with this evidence.
- Four chunks is the CSP v1 maximum (decision (j)).
- A follow-up experiment may register the search-radius/level trade-off (for example L9 with C16) against create time.

## References

- #7; `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md`; `PATCH-PREFREEZE-001/EVIDENCE-20260928-001`.
