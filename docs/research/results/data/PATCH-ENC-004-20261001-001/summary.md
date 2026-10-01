# PATCH-ENC-004 create throughput

- commit: `844d1e7c931877072eea804164fd145defbdd2c8`
- corpusPairsSha256: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`
- valid: true
- linux-arm64: `PATCH-ENC-004/RUN-20261001-22-844d1e7-linux-arm64`
- linux-x64: `PATCH-ENC-004/RUN-20261001-22-844d1e7-linux-x64`
- win-x64: `PATCH-ENC-004/RUN-20261001-22-844d1e7-win-x64`

## linux-arm64

| execution | T s | speedup | CPU s | eff. cores | base MiB read | seeks | alloc MiB | reorder entries | reorder bytes | M MiB | bound MiB | bytes identical |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| h0 | 1378.96 | 1.00 | 1409.59 | 1.01 | 9399.5 | 123057 | 13468.8 | 0 | 0 | 51.1 | 64 | yes |
| h1 | 1424.13 | 0.97 | 1428.50 | 1.00 | 603.6 | 2377 | 13787.9 | 0 | 0 | 49.8 | 64 | yes |
| h2-w1 | 1397.71 | 0.99 | 1408.36 | 1.01 | 9399.5 | 123057 | 13628.9 | 1 | 111373 | 53.8 | 64 | yes |
| h2-w2 | 732.30 | 1.88 | 1444.88 | 1.97 | 9399.5 | 123057 | 14619.9 | 5 | 162625 | 69.2 | 96 | yes |
| h2-w4 | 390.33 | 3.53 | 1424.18 | 3.65 | 9399.5 | 123057 | 15534.2 | 13 | 304027 | 95.5 | 160 | yes |
| h2-w8 | 422.38 | 3.26 | 1548.98 | 3.67 | 9399.5 | 123057 | 15993.7 | 25 | 425563 | 152.0 | 288 | yes |
| h3-w1 | 1462.44 | 0.94 | 1469.56 | 1.00 | 603.6 | 2377 | 13988.9 | 1 | 111373 | 53.4 | 64 | yes |
| h3-w2 | 660.93 | 2.09 | 1298.93 | 1.97 | 603.6 | 2377 | 14999.6 | 6 | 162625 | 69.8 | 96 | yes |
| h3-w4 | 358.11 | 3.85 | 1304.88 | 3.64 | 603.6 | 2377 | 15932.8 | 13 | 444901 | 97.2 | 160 | yes |
| h3-w8 | 363.48 | 3.79 | 1328.79 | 3.66 | 603.6 | 2377 | 16313.2 | 29 | 774616 | 148.6 | 288 | yes |

## linux-x64

| execution | T s | speedup | CPU s | eff. cores | base MiB read | seeks | alloc MiB | reorder entries | reorder bytes | M MiB | bound MiB | bytes identical |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| h0 | 1482.24 | 1.00 | 1509.75 | 1.01 | 9399.5 | 123057 | 13469.6 | 0 | 0 | 53.5 | 64 | yes |
| h1 | 1472.06 | 1.01 | 1477.23 | 1.00 | 603.6 | 2377 | 13790.4 | 0 | 0 | 53.3 | 64 | yes |
| h2-w1 | 1486.37 | 1.00 | 1499.07 | 1.01 | 9399.5 | 123057 | 13630.9 | 1 | 111373 | 54.9 | 64 | yes |
| h2-w2 | 796.36 | 1.86 | 1570.49 | 1.97 | 9399.5 | 123057 | 14620.3 | 6 | 162625 | 70.2 | 96 | yes |
| h2-w4 | 577.68 | 2.57 | 2151.76 | 3.72 | 9399.5 | 123057 | 15536.8 | 13 | 248194 | 101.1 | 160 | yes |
| h2-w8 | 639.41 | 2.32 | 2379.98 | 3.72 | 9399.5 | 123057 | 15982.1 | 25 | 425563 | 158.7 | 288 | yes |
| h3-w1 | 1506.20 | 0.98 | 1514.42 | 1.01 | 603.6 | 2377 | 13991.0 | 1 | 111373 | 56.0 | 64 | yes |
| h3-w2 | 782.85 | 1.89 | 1539.07 | 1.97 | 603.6 | 2377 | 15000.3 | 6 | 162625 | 73.1 | 96 | yes |
| h3-w4 | 585.47 | 2.53 | 2180.71 | 3.72 | 603.6 | 2377 | 15932.9 | 13 | 461343 | 104.8 | 160 | yes |
| h3-w8 | 630.33 | 2.35 | 2361.12 | 3.75 | 603.6 | 2377 | 16316.5 | 29 | 1010817 | 166.6 | 288 | yes |

## win-x64

| execution | T s | speedup | CPU s | eff. cores | base MiB read | seeks | alloc MiB | reorder entries | reorder bytes | M MiB | bound MiB | bytes identical |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| h0 | 1288.10 | 1.00 | 1317.88 | 0.99 | 9399.5 | 123057 | 13458.7 | 0 | 0 | 30.6 | 64 | yes |
| h1 | 1294.28 | 1.00 | 1288.64 | 1.00 | 603.6 | 2377 | 13776.3 | 0 | 0 | 31.1 | 64 | yes |
| h2-w1 | 1306.75 | 0.99 | 1308.11 | 1.00 | 9399.5 | 123057 | 13615.6 | 1 | 111373 | 31.1 | 64 | yes |
| h2-w2 | 712.52 | 1.81 | 1395.06 | 1.96 | 9399.5 | 123057 | 14610.6 | 8 | 162625 | 45.4 | 96 | yes |
| h2-w4 | 536.49 | 2.40 | 1949.38 | 3.63 | 9399.5 | 123057 | 15523.9 | 13 | 295222 | 71.7 | 160 | yes |
| h2-w8 | 537.33 | 2.40 | 1954.59 | 3.64 | 9399.5 | 123057 | 15975.9 | 18 | 335812 | 127.0 | 288 | yes |
| h3-w1 | 1276.60 | 1.01 | 1269.86 | 0.99 | 603.6 | 2377 | 13977.1 | 1 | 111373 | 33.1 | 64 | yes |
| h3-w2 | 678.20 | 1.90 | 1322.42 | 1.95 | 603.6 | 2377 | 14989.2 | 7 | 162625 | 45.8 | 96 | yes |
| h3-w4 | 527.30 | 2.44 | 1912.34 | 3.63 | 603.6 | 2377 | 15918.1 | 13 | 444901 | 74.8 | 160 | yes |
| h3-w8 | 532.51 | 2.42 | 1933.77 | 3.63 | 603.6 | 2377 | 16270.8 | 30 | 973590 | 123.6 | 288 | yes |

## Decision

- H1: REJECT (read ratio linux-arm64=15.57, linux-x64=15.57, win-x64=15.57; no regression {'linux-x64': True, 'linux-arm64': False, 'win-x64': False}; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True})
- Workers: ADOPT (h2-w2); H3 eligible: false
  - h3-w1: not eligible (H1 not adopted); speedup linux-arm64=0.94, linux-x64=0.98, win-x64=1.01; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
  - h3-w2: not eligible (H1 not adopted); speedup linux-arm64=2.09, linux-x64=1.89, win-x64=1.90; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
  - h3-w4: not eligible (H1 not adopted); speedup linux-arm64=3.85, linux-x64=2.53, win-x64=2.44; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
  - h3-w8: not eligible (H1 not adopted); speedup linux-arm64=3.79, linux-x64=2.35, win-x64=2.42; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
  - h2-w1: does not qualify; speedup linux-arm64=0.99, linux-x64=1.00, win-x64=0.99; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
  - h2-w2: qualifies; speedup linux-arm64=1.88, linux-x64=1.86, win-x64=1.81; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
  - h2-w4: qualifies; speedup linux-arm64=3.53, linux-x64=2.57, win-x64=2.40; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
  - h2-w8: qualifies; speedup linux-arm64=3.26, linux-x64=2.32, win-x64=2.40; memory {'linux-x64': True, 'linux-arm64': True, 'win-x64': True}
- h0 patchesSha256 equal across platforms: true
