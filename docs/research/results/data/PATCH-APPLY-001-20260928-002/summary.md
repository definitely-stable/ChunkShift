# Patching pre-freeze lab summary

- Corpus lock `pairsSha256`: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` (from `docs/benchmarks/patch-corpus/corpus-lock.json`)
- `platform-x64`: commit `2f15c67a48235cb6145c3e6dd6b02f9f027759a9`, Ubuntu 24.04.5 LTS / X64, 4 evidence files, RunIds: `PATCH-PREFREEZE-001/RUN-20260928-2-2f15c67-linux-x64`
- `platform-arm64`: commit `2f15c67a48235cb6145c3e6dd6b02f9f027759a9`, Ubuntu 24.04.5 LTS / Arm64, 4 evidence files, RunIds: `PATCH-PREFREEZE-001/RUN-20260928-2-2f15c67-linux-arm64`

Aggregates are sums over changed files. Bytes are MiB and ratios are percent, both with two decimals.

## Verdicts

| experiment | verdict | numbers |
|---|---|---|
| PATCH-PREFREEZE-001 | **ADOPT** | R1 platform-x64: 7.68% (max 75.00%); R1 platform-arm64: 7.68% (max 75.00%); R2 platform-x64: 7.17 s vs 9.45 s at 1 Gbit/s; R2 platform-arm64: 8.35 s vs 10.19 s at 1 Gbit/s |
| PATCH-ENC-002 | **not evaluated** no x64 Linux sweep | no x64 Linux sweep |
| PATCH-APPLY-001 | **DEFER** | A1 platform-x64: 5.93% (max 25.00%); A1 platform-arm64: 23.41% (max 25.00%); A2 platform-x64: worst +219.57 MiB; A2 platform-arm64: worst +251.34 MiB |

## PATCH-PREFREEZE-001 lanes

| platform | lane | files | bytes MiB | create s | apply s | decode s | 50 Mbit/s s | 1 Gbit/s s |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| platform-x64 | csp | 1893 | 36.45 | 1025.70 | 6.86 | - | 12.98 | 7.17 |
| platform-x64 | csp-raw | 1893 | 474.71 | 1.52 | 5.46 | - | 85.11 | 9.45 |
| platform-x64 | csp-zstd | 1893 | 173.90 | 126.68 | 6.51 | - | 35.69 | 7.97 |
| platform-arm64 | csp | 1893 | 36.45 | 1368.91 | 8.04 | - | 14.16 | 8.35 |
| platform-arm64 | csp-raw | 1893 | 474.71 | 2.62 | 6.21 | - | 85.85 | 10.19 |
| platform-arm64 | csp-zstd | 1893 | 173.90 | 139.14 | 6.34 | - | 35.52 | 7.80 |

## PATCH-PREFREEZE-001 CSP details

| platform | csp patch MiB | embedded TCSM | unique missing MiB | study estimate MiB | estimate - csp MiB |
|---|---:|---:|---:|---:|---:|
| platform-x64 | 36.45 | 3.03% | 472.63 | 36.59 | -0.13 |
| platform-arm64 | 36.45 | 3.03% | 472.63 | 36.59 | -0.13 |

## PATCH-ENC-002 sweep

Not evaluated: no x64 Linux sweep.

Missing sweep lanes: sweep-L9-K1-C8, sweep-L9-K1-C16, sweep-L9-K2-C8, sweep-L9-K2-C16, sweep-L9-K4-C8, sweep-L9-K4-C16, sweep-L19-K1-C8, sweep-L19-K1-C16, sweep-L19-K2-C8, sweep-L19-K2-C16, sweep-L19-K4-C8, sweep-L19-K4-C16.

Sweep patch bytes across platforms:

| setting | platforms | identical |
|---|---|---|
| sweep-L9-K1-C8 | - | single platform |
| sweep-L9-K1-C16 | - | single platform |
| sweep-L9-K2-C8 | - | single platform |
| sweep-L9-K2-C16 | - | single platform |
| sweep-L9-K4-C8 | - | single platform |
| sweep-L9-K4-C16 | - | single platform |
| sweep-L19-K1-C8 | - | single platform |
| sweep-L19-K1-C16 | - | single platform |
| sweep-L19-K2-C8 | - | single platform |
| sweep-L19-K2-C16 | - | single platform |
| sweep-L19-K4-C8 | - | single platform |
| sweep-L19-K4-C16 | - | single platform |

## PATCH-APPLY-001

| platform | apply s | check off s | apply/check off - 1 | A1 | memory records | worst excess MiB | A2 |
|---|---:|---:|---:|---|---:|---:|---|
| platform-x64 | 6.86 | 6.48 | 5.93% | ADOPT | 75 | 219.57 | DEFER |
| platform-arm64 | 8.04 | 6.52 | 23.41% | ADOPT | 75 | 251.34 | DEFER |
