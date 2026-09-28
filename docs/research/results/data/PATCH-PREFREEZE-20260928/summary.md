# Patching pre-freeze lab summary

- Corpus lock `pairsSha256`: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` (from `docs/benchmarks/patch-corpus/corpus-lock.json`)
- `platform-x64`: commit `853c89ee450b148a6b4e1df567d1ac05766168c6`, Ubuntu 24.04.5 LTS / X64, 17 evidence files, RunIds: `PATCH-PREFREEZE-001/RUN-20260928-1-853c89e-linux-x64`, `PATCH-ENC-002/RUN-20260928-1-853c89e-linux-x64`
- `platform-arm64`: commit `853c89ee450b148a6b4e1df567d1ac05766168c6`, Ubuntu 24.04.5 LTS / Arm64, 17 evidence files, RunIds: `PATCH-PREFREEZE-001/RUN-20260928-1-853c89e-linux-arm64`, `PATCH-ENC-002/RUN-20260928-1-853c89e-linux-arm64`
- `platform-win-x64`: commit `853c89ee450b148a6b4e1df567d1ac05766168c6`, Microsoft Windows 10.0.26200 / X64, 4 evidence files, RunIds: `PATCH-PREFREEZE-001/RUN-20260928-001-853c89e-win-x64`

Aggregates are sums over changed files. Bytes are MiB and ratios are percent, both with two decimals.

## Verdicts

| experiment | verdict | numbers |
|---|---|---|
| PATCH-PREFREEZE-001 | **ADOPT** | R1 platform-x64: 8.12% (max 75.00%); R1 platform-arm64: 8.12% (max 75.00%); R1 platform-win-x64: 8.12% (max 75.00%); R2 platform-x64: 5.46 s vs 8.74 s at 1 Gbit/s; R2 platform-arm64: 7.93 s vs 10.73 s at 1 Gbit/s; R2 platform-win-x64: 11.09 s vs 15.71 s at 1 Gbit/s |
| PATCH-ENC-002 | **ADOPT sweep-L19-K4-C8** | winner sweep-L19-K4-C8 on platform-x64, saves 6.50% of the default calibration bytes, holdout within default: yes |
| PATCH-APPLY-001 | **DEFER** | A1 platform-x64: 34.91% (max 25.00%); A1 platform-arm64: 27.06% (max 25.00%); A1 platform-win-x64: 26.69% (max 25.00%); A2 platform-x64: worst +151.17 MiB; A2 platform-arm64: worst +185.21 MiB; A2 platform-win-x64: worst +57.74 MiB |

## PATCH-PREFREEZE-001 lanes

| platform | lane | files | bytes MiB | create s | apply s | decode s | 50 Mbit/s s | 1 Gbit/s s |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| platform-x64 | csp | 1893 | 38.56 | 972.72 | 5.14 | - | 11.61 | 5.46 |
| platform-x64 | csp-raw | 1893 | 474.71 | 2.71 | 4.75 | - | 84.40 | 8.74 |
| platform-x64 | csp-zstd | 1893 | 173.90 | 164.79 | 4.97 | - | 34.15 | 6.43 |
| platform-x64 | sweep-L19-K1-C16 | 1893 | 45.82 | 2646.83 | 0.00 | - | 7.69 | 0.38 |
| platform-x64 | sweep-L19-K1-C8 | 1893 | 50.68 | 1201.17 | 0.00 | - | 8.50 | 0.43 |
| platform-x64 | sweep-L19-K2-C16 | 1893 | 33.23 | 3342.29 | 0.00 | - | 5.58 | 0.28 |
| platform-x64 | sweep-L19-K2-C8 | 1893 | 38.56 | 1444.50 | 0.00 | - | 6.47 | 0.32 |
| platform-x64 | sweep-L19-K4-C16 | 1893 | 31.08 | 5077.56 | 0.00 | - | 5.21 | 0.26 |
| platform-x64 | sweep-L19-K4-C8 | 1893 | 36.45 | 2005.53 | 0.00 | - | 6.12 | 0.31 |
| platform-x64 | sweep-L9-K1-C16 | 1893 | 50.83 | 276.05 | 0.00 | - | 8.53 | 0.43 |
| platform-x64 | sweep-L9-K1-C8 | 1893 | 55.89 | 89.32 | 0.00 | - | 9.38 | 0.47 |
| platform-x64 | sweep-L9-K2-C16 | 1893 | 37.49 | 325.88 | 0.00 | - | 6.29 | 0.31 |
| platform-x64 | sweep-L9-K2-C8 | 1893 | 43.02 | 93.57 | 0.00 | - | 7.22 | 0.36 |
| platform-x64 | sweep-L9-K4-C16 | 1893 | 35.19 | 393.92 | 0.00 | - | 5.90 | 0.30 |
| platform-x64 | sweep-L9-K4-C8 | 1893 | 40.90 | 119.44 | 0.00 | - | 6.86 | 0.34 |
| platform-x64 | full | 1893 | 817.98 | - | - | 0.00 | 137.23 | 6.86 |
| platform-x64 | full-zstd | 1893 | 250.78 | - | - | 4.33 | 46.40 | 6.43 |
| platform-x64 | zstd-patch-from | 1893 | 28.90 | - | - | 3.77 | 8.62 | 4.02 |
| platform-x64 | xdelta3 | 1893 | 39.36 | - | - | 5.75 | 12.35 | 6.08 |
| platform-x64 | bsdiff | 1893 | 20.64 | - | - | 5.39 | 8.85 | 5.56 |
| platform-arm64 | csp | 1893 | 38.56 | 879.61 | 7.61 | - | 14.08 | 7.93 |
| platform-arm64 | csp-raw | 1893 | 474.71 | 2.79 | 6.75 | - | 86.39 | 10.73 |
| platform-arm64 | csp-zstd | 1893 | 173.90 | 136.13 | 6.79 | - | 35.96 | 8.25 |
| platform-arm64 | sweep-L19-K1-C16 | 1893 | 45.82 | 1557.33 | 0.00 | - | 7.69 | 0.38 |
| platform-arm64 | sweep-L19-K1-C8 | 1893 | 50.68 | 714.78 | 0.00 | - | 8.50 | 0.43 |
| platform-arm64 | sweep-L19-K2-C16 | 1893 | 33.23 | 2136.64 | 0.00 | - | 5.58 | 0.28 |
| platform-arm64 | sweep-L19-K2-C8 | 1893 | 38.56 | 937.97 | 0.00 | - | 6.47 | 0.32 |
| platform-arm64 | sweep-L19-K4-C16 | 1893 | 31.08 | 3338.02 | 0.00 | - | 5.21 | 0.26 |
| platform-arm64 | sweep-L19-K4-C8 | 1893 | 36.45 | 1425.97 | 0.00 | - | 6.12 | 0.31 |
| platform-arm64 | sweep-L9-K1-C16 | 1893 | 50.83 | 194.16 | 0.00 | - | 8.53 | 0.43 |
| platform-arm64 | sweep-L9-K1-C8 | 1893 | 55.89 | 93.47 | 0.00 | - | 9.38 | 0.47 |
| platform-arm64 | sweep-L9-K2-C16 | 1893 | 37.49 | 244.60 | 0.00 | - | 6.29 | 0.31 |
| platform-arm64 | sweep-L9-K2-C8 | 1893 | 43.02 | 107.07 | 0.00 | - | 7.22 | 0.36 |
| platform-arm64 | sweep-L9-K4-C16 | 1893 | 35.19 | 332.75 | 0.00 | - | 5.90 | 0.30 |
| platform-arm64 | sweep-L9-K4-C8 | 1893 | 40.90 | 162.45 | 0.00 | - | 6.86 | 0.34 |
| platform-arm64 | full | 1893 | 817.98 | - | - | 0.00 | 137.23 | 6.86 |
| platform-arm64 | full-zstd | 1893 | 250.78 | - | - | 3.35 | 45.43 | 5.46 |
| platform-arm64 | zstd-patch-from | 1893 | 28.90 | - | - | 2.86 | 7.71 | 3.10 |
| platform-arm64 | xdelta3 | 1893 | 39.36 | - | - | 4.11 | 10.71 | 4.44 |
| platform-arm64 | bsdiff | 1893 | 20.64 | - | - | 4.69 | 8.16 | 4.87 |
| platform-win-x64 | csp | 1893 | 38.56 | 950.11 | 10.77 | - | 17.24 | 11.09 |
| platform-win-x64 | csp-raw | 1893 | 474.71 | 2.86 | 11.72 | - | 91.37 | 15.71 |
| platform-win-x64 | csp-zstd | 1893 | 173.90 | 187.06 | 12.28 | - | 41.46 | 13.74 |

## PATCH-PREFREEZE-001 CSP details

| platform | csp patch MiB | embedded TCSM | unique missing MiB | study estimate MiB | estimate - csp MiB |
|---|---:|---:|---:|---:|---:|
| platform-x64 | 38.56 | 2.87% | 472.63 | 38.70 | -0.13 |
| platform-arm64 | 38.56 | 2.87% | 472.63 | 38.70 | -0.13 |
| platform-win-x64 | 38.56 | 2.87% | 472.63 | 38.70 | -0.13 |

## PATCH-ENC-002 sweep

x64 Linux platform: `platform-x64`; default lane `sweep-L19-K2-C8`, eligible when create time is at most 1.50 x the default (692.39 s).

| setting | eligible | calibration create s | calibration MiB | holdout MiB |
|---|---:|---:|---:|---:|
| sweep-L9-K1-C8 | yes | 35.90 | 18.27 | 37.63 |
| sweep-L9-K1-C16 | yes | 92.51 | 18.24 | 32.59 |
| sweep-L9-K2-C8 | yes | 33.45 | 13.50 | 29.52 |
| sweep-L9-K2-C16 | yes | 105.10 | 13.49 | 24.00 |
| sweep-L9-K4-C8 | yes | 42.26 | 12.65 | 28.25 |
| sweep-L9-K4-C16 | yes | 128.39 | 12.64 | 22.56 |
| sweep-L19-K1-C8 | yes | 405.14 | 16.53 | 34.16 |
| sweep-L19-K1-C16 | no | 810.14 | 16.50 | 29.32 |
| sweep-L19-K2-C8 | yes | 461.60 | 12.10 | 26.47 |
| sweep-L19-K2-C16 | no | 983.70 | 12.08 | 21.15 |
| sweep-L19-K4-C8 | yes (winner) | 595.74 | 11.31 | 25.14 |
| sweep-L19-K4-C16 | no | 1422.32 | 11.29 | 19.78 |

Saving of the winner against the default calibration bytes: 6.50% (minimum 2.00%); winner holdout within default: yes.
**ADOPT sweep-L19-K4-C8**.

Sweep patch bytes across platforms:

| setting | platforms | identical |
|---|---|---|
| sweep-L9-K1-C8 | platform-x64, platform-arm64 | yes |
| sweep-L9-K1-C16 | platform-x64, platform-arm64 | yes |
| sweep-L9-K2-C8 | platform-x64, platform-arm64 | yes |
| sweep-L9-K2-C16 | platform-x64, platform-arm64 | yes |
| sweep-L9-K4-C8 | platform-x64, platform-arm64 | yes |
| sweep-L9-K4-C16 | platform-x64, platform-arm64 | yes |
| sweep-L19-K1-C8 | platform-x64, platform-arm64 | yes |
| sweep-L19-K1-C16 | platform-x64, platform-arm64 | yes |
| sweep-L19-K2-C8 | platform-x64, platform-arm64 | yes |
| sweep-L19-K2-C16 | platform-x64, platform-arm64 | yes |
| sweep-L19-K4-C8 | platform-x64, platform-arm64 | yes |
| sweep-L19-K4-C16 | platform-x64, platform-arm64 | yes |

## PATCH-APPLY-001

| platform | apply s | check off s | apply/check off - 1 | A1 | memory records | worst excess MiB | A2 |
|---|---:|---:|---:|---|---:|---:|---|
| platform-x64 | 5.14 | 3.81 | 34.91% | DEFER | 75 | 151.17 | DEFER |
| platform-arm64 | 7.61 | 5.99 | 27.06% | DEFER | 75 | 185.21 | DEFER |
| platform-win-x64 | 10.77 | 8.50 | 26.69% | DEFER | 75 | 57.74 | ADOPT |
