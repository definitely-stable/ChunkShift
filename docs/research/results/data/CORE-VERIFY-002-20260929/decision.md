# CORE-VERIFY-002 decision: DEFER

R1 holds on 2 platforms, R1–R3 together on 1

| platform | oracle | R1 | R2 | R3 |
|---|---|---|---|---|
| linux-arm64 | passed | holds | holds | holds |
| linux-x64 | passed | holds | holds | fails |
| win-x64 | passed | missing | missing | holds |

- linux-arm64 R1: S1 warm: V1/V0 = 1.600 GiB per CPU-second (0.973 / 0.608); SL warm: V1/V0 = 1.607 GiB per CPU-second (0.986 / 0.613)
- linux-arm64 R2: warm: best V2 on SL 3.875 GiB/s vs best V1 x K on T 2.172 GiB/s; throttled: best V2 on S1 0.634 GiB/s vs best V1 x K on T 0.165 GiB/s
- linux-arm64 R3: warm: best V2-W2 x K / best V1 x K on T = 0.973 (2.114 / 2.172 GiB/s); throttled: best V2-W2 x K / best V1 x K on T = 1.354 (0.224 / 0.165 GiB/s)
- linux-x64 R1: S1 warm: V1/V0 = 1.943 GiB per CPU-second (1.274 / 0.656); SL warm: V1/V0 = 1.997 GiB per CPU-second (1.311 / 0.656)
- linux-x64 R2: warm: best V2 on SL 3.338 GiB/s vs best V1 x K on T 2.011 GiB/s; throttled: best V2 on S1 0.634 GiB/s vs best V1 x K on T 0.165 GiB/s
- linux-x64 R3: warm: best V2-W2 x K / best V1 x K on T = 0.916 (1.843 / 2.011 GiB/s); throttled: best V2-W2 x K / best V1 x K on T = 1.354 (0.224 / 0.165 GiB/s)
- win-x64 R1: S1 warm: V1/V0 = 1.964 GiB per CPU-second (1.143 / 0.582); SL warm: missing (unverified-warm: V0, V1)
- win-x64 R2: warm: missing (unverified-warm: V2-W2, V2-W4, V2-W8); throttled: best V2 on S1 0.627 GiB/s vs best V1 x K on T 0.165 GiB/s
- win-x64 R3: warm: best V2-W2 x K / best V1 x K on T = 1.003 (1.785 / 1.780 GiB/s); throttled: best V2-W2 x K / best V1 x K on T = 1.349 (0.222 / 0.165 GiB/s)

| platform/workload | suite | mode | pool | lane | K | n | residency (resident) | p50 GiB/s | p50 GiB per CPU-s | p50 cores | p50 wall s | p95 wall s | p50 peak over idle MiB | min probe GiB/s | min resident |
|---|---|---|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| linux-arm64/S1 | blake3 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.651 | 0.608 | 1.069 | 1.537 | 1.558 | 32.697 | 13.710 | 1.000 |
| linux-arm64/S1 | blake3 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 0.981 | 0.973 | 1.008 | 1.019 | 1.029 | 46.547 | 13.538 | 1.000 |
| linux-arm64/S1 | blake3 | warm | spin-0 | V2-W2 | 1 | 10 | resident (10) | 1.956 | 0.963 | 2.031 | 0.511 | 0.516 | 53.500 | 13.434 | 1.000 |
| linux-arm64/S1 | blake3 | warm | spin-0 | V2-W4 | 1 | 10 | resident (10) | 3.685 | 0.953 | 3.861 | 0.271 | 0.275 | 65.598 | 12.849 | 1.000 |
| linux-arm64/S1 | blake3 | warm | spin-0 | V2-W8 | 1 | 10 | resident (10) | 3.645 | 0.949 | 3.846 | 0.274 | 0.280 | 99.592 | 13.439 | 1.000 |
| linux-arm64/S1 | blake3 | cold | spin-0 | V0 | 1 | 10 | n/a (0) | 0.426 | 0.545 | 0.779 | 2.349 | 2.517 | 30.211 | — | — |
| linux-arm64/S1 | blake3 | cold | spin-0 | V1 | 1 | 10 | n/a (0) | 0.428 | 0.931 | 0.459 | 2.338 | 2.489 | 43.125 | — | — |
| linux-arm64/S1 | blake3 | cold | spin-0 | V2-W2 | 1 | 10 | n/a (0) | 0.426 | 0.849 | 0.503 | 2.346 | 2.470 | 50.207 | — | — |
| linux-arm64/S1 | blake3 | cold | spin-0 | V2-W4 | 1 | 10 | n/a (0) | 0.426 | 0.786 | 0.542 | 2.346 | 2.477 | 62.645 | — | — |
| linux-arm64/S1 | blake3 | cold | spin-0 | V2-W8 | 1 | 10 | n/a (0) | 0.427 | 0.769 | 0.555 | 2.342 | 2.443 | 96.951 | — | — |
| linux-arm64/S1 | blake3 | throttled | spin-0 | V0 | 1 | 10 | resident (10) | 0.081 | 0.581 | 0.140 | 12.307 | 12.307 | 34.590 | 9.551 | 1.000 |
| linux-arm64/S1 | blake3 | throttled | spin-0 | V1 | 1 | 10 | resident (10) | 0.081 | 0.799 | 0.101 | 12.341 | 12.342 | 47.426 | 9.411 | 1.000 |
| linux-arm64/S1 | blake3 | throttled | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.162 | 0.834 | 0.194 | 6.188 | 6.189 | 56.898 | 9.661 | 1.000 |
| linux-arm64/S1 | blake3 | throttled | spin-0 | V2-W4 | 1 | 10 | resident (10) | 0.323 | 0.821 | 0.393 | 3.100 | 3.102 | 70.033 | 9.654 | 1.000 |
| linux-arm64/S1 | blake3 | throttled | spin-0 | V2-W8 | 1 | 10 | resident (10) | 0.634 | 0.825 | 0.769 | 1.578 | 1.581 | 96.277 | 9.671 | 1.000 |
| linux-arm64/S1 | sha256 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.844 | 0.778 | 1.083 | 1.185 | 1.210 | 32.512 | 9.536 | 1.000 |
| linux-arm64/S1 | sha256 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 1.529 | 1.484 | 1.031 | 0.654 | 0.662 | 46.717 | 9.270 | 1.000 |
| linux-arm64/S1 | blake3 | warm | default | V0 | 1 | 10 | resident (10) | 0.649 | 0.433 | 1.493 | 1.541 | 1.560 | 32.516 | 9.768 | 1.000 |
| linux-arm64/S1 | blake3 | warm | default | V1 | 1 | 10 | resident (10) | 0.959 | 0.939 | 1.020 | 1.043 | 1.057 | 46.527 | 8.496 | 1.000 |
| linux-arm64/SL | blake3 | warm | spin-0 | V0 | 1 | 5 | resident (5) | 0.648 | 0.613 | 1.058 | 9.256 | 9.271 | 33.410 | 13.751 | 1.000 |
| linux-arm64/SL | blake3 | warm | spin-0 | V1 | 1 | 5 | resident (5) | 0.992 | 0.986 | 1.006 | 6.048 | 6.076 | 56.836 | 13.758 | 1.000 |
| linux-arm64/SL | blake3 | warm | spin-0 | V2-W2 | 1 | 5 | resident (5) | 1.994 | 0.987 | 2.021 | 3.009 | 3.039 | 65.266 | 13.830 | 1.000 |
| linux-arm64/SL | blake3 | warm | spin-0 | V2-W4 | 1 | 5 | resident (5) | 3.875 | 0.983 | 3.941 | 1.548 | 1.556 | 77.754 | 14.000 | 1.000 |
| linux-arm64/SL | blake3 | warm | spin-0 | V2-W8 | 1 | 5 | resident (5) | 3.869 | 0.983 | 3.934 | 1.551 | 1.566 | 109.613 | 14.296 | 1.000 |
| linux-arm64/SL | blake3 | cold | spin-0 | V0 | 1 | 5 | n/a (0) | 0.390 | 0.555 | 0.703 | 15.372 | 15.377 | 30.340 | — | — |
| linux-arm64/SL | blake3 | cold | spin-0 | V1 | 1 | 5 | n/a (0) | 0.390 | 0.946 | 0.413 | 15.371 | 15.510 | 53.621 | — | — |
| linux-arm64/SL | blake3 | cold | spin-0 | V2-W2 | 1 | 5 | n/a (0) | 0.391 | 0.871 | 0.449 | 15.362 | 15.384 | 59.074 | — | — |
| linux-arm64/SL | blake3 | cold | spin-0 | V2-W4 | 1 | 5 | n/a (0) | 0.391 | 0.814 | 0.480 | 15.351 | 15.470 | 74.500 | — | — |
| linux-arm64/SL | blake3 | cold | spin-0 | V2-W8 | 1 | 5 | n/a (0) | 0.391 | 0.794 | 0.492 | 15.353 | 15.369 | 102.781 | — | — |
| linux-arm64/SL | blake3 | warm | default | V0 | 1 | 5 | resident (5) | 0.660 | 0.444 | 1.487 | 9.094 | 9.207 | 33.141 | 10.749 | 1.000 |
| linux-arm64/SL | blake3 | warm | default | V1 | 1 | 5 | resident (5) | 0.975 | 0.957 | 1.019 | 6.152 | 6.155 | 56.879 | 11.297 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.525 | 0.408 | 1.295 | 1.516 | 1.547 | 51.475 | 11.660 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 0.682 | 0.552 | 1.237 | 1.168 | 1.192 | 115.025 | 11.907 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.811 | 0.488 | 1.674 | 0.982 | 1.009 | 133.445 | 12.121 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V0 | 2 | 10 | resident (10) | 1.000 | 0.438 | 2.271 | 0.797 | 0.831 | 52.172 | 12.095 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V1 | 2 | 10 | resident (10) | 1.227 | 0.554 | 2.251 | 0.649 | 0.689 | 146.480 | 12.110 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V2-W2 | 2 | 10 | resident (10) | 1.494 | 0.584 | 2.548 | 0.533 | 0.552 | 201.055 | 11.937 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V0 | 4 | 10 | resident (10) | 1.651 | 0.508 | 3.251 | 0.483 | 0.537 | 63.059 | 11.948 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V1 | 4 | 10 | resident (10) | 2.048 | 0.647 | 3.171 | 0.389 | 0.398 | 188.209 | 12.043 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V2-W2 | 4 | 10 | resident (10) | 2.114 | 0.622 | 3.397 | 0.377 | 0.389 | 248.826 | 12.132 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V0 | 8 | 10 | resident (10) | 1.792 | 0.512 | 3.485 | 0.445 | 0.459 | 78.846 | 11.639 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V1 | 8 | 10 | resident (10) | 2.172 | 0.607 | 3.581 | 0.367 | 0.383 | 233.857 | 11.948 | 1.000 |
| linux-arm64/T | blake3 | warm | spin-0 | V2-W2 | 8 | 10 | resident (10) | 2.089 | 0.566 | 3.680 | 0.381 | 0.398 | 289.562 | 12.148 | 1.000 |
| linux-arm64/T | blake3 | cold | spin-0 | V0 | 1 | 10 | n/a (0) | 0.305 | 0.326 | 0.944 | 2.616 | 2.900 | 39.875 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V1 | 1 | 10 | n/a (0) | 0.315 | 0.427 | 0.728 | 2.532 | 2.935 | 113.238 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V2-W2 | 1 | 10 | n/a (0) | 0.325 | 0.396 | 0.819 | 2.451 | 2.660 | 143.031 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V0 | 2 | 10 | n/a (0) | 0.425 | 0.321 | 1.319 | 1.875 | 2.057 | 40.334 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V1 | 2 | 10 | n/a (0) | 0.424 | 0.418 | 1.003 | 1.881 | 2.159 | 129.951 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V2-W2 | 2 | 10 | n/a (0) | 0.427 | 0.385 | 1.107 | 1.867 | 2.068 | 180.385 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V0 | 4 | 10 | n/a (0) | 0.428 | 0.328 | 1.298 | 1.863 | 1.977 | 42.420 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V1 | 4 | 10 | n/a (0) | 0.428 | 0.433 | 0.985 | 1.863 | 2.024 | 174.859 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V2-W2 | 4 | 10 | n/a (0) | 0.430 | 0.396 | 1.081 | 1.852 | 1.884 | 205.180 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V0 | 8 | 10 | n/a (0) | 0.431 | 0.332 | 1.305 | 1.850 | 1.874 | 46.607 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V1 | 8 | 10 | n/a (0) | 0.429 | 0.433 | 0.989 | 1.859 | 1.891 | 212.500 | — | — |
| linux-arm64/T | blake3 | cold | spin-0 | V2-W2 | 8 | 10 | n/a (0) | 0.429 | 0.395 | 1.095 | 1.856 | 2.229 | 276.793 | — | — |
| linux-arm64/T | blake3 | throttled | spin-0 | V0 | 1 | 10 | resident (10) | 0.031 | 0.230 | 0.134 | 25.849 | 25.849 | 52.023 | 8.481 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V1 | 1 | 10 | resident (10) | 0.025 | 0.331 | 0.075 | 32.182 | 32.182 | 112.896 | 8.524 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.032 | 0.332 | 0.096 | 24.999 | 25.057 | 124.328 | 8.879 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V0 | 2 | 10 | resident (10) | 0.060 | 0.225 | 0.266 | 13.315 | 13.316 | 51.420 | 8.653 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V1 | 2 | 10 | resident (10) | 0.048 | 0.337 | 0.144 | 16.430 | 16.431 | 121.389 | 8.898 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V2-W2 | 2 | 10 | resident (10) | 0.063 | 0.337 | 0.187 | 12.612 | 12.627 | 145.154 | 8.683 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V0 | 4 | 10 | resident (10) | 0.111 | 0.264 | 0.420 | 7.176 | 7.177 | 79.084 | 8.280 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V1 | 4 | 10 | resident (10) | 0.091 | 0.351 | 0.259 | 8.761 | 8.762 | 148.529 | 8.691 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V2-W2 | 4 | 10 | resident (10) | 0.121 | 0.346 | 0.349 | 6.592 | 6.596 | 180.676 | 8.950 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V0 | 8 | 10 | resident (10) | 0.203 | 0.291 | 0.696 | 3.933 | 3.934 | 112.914 | 8.476 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V1 | 8 | 10 | resident (10) | 0.165 | 0.375 | 0.441 | 4.815 | 4.816 | 186.256 | 8.578 | 1.000 |
| linux-arm64/T | blake3 | throttled | spin-0 | V2-W2 | 8 | 10 | resident (10) | 0.224 | 0.362 | 0.618 | 3.557 | 3.566 | 197.295 | 8.765 | 1.000 |
| linux-x64/S1 | blake3 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.757 | 0.656 | 1.145 | 1.321 | 1.419 | 31.938 | 14.481 | 1.000 |
| linux-x64/S1 | blake3 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 1.297 | 1.274 | 1.018 | 0.771 | 0.782 | 46.293 | 14.881 | 1.000 |
| linux-x64/S1 | blake3 | warm | spin-0 | V2-W2 | 1 | 10 | resident (10) | 2.551 | 1.238 | 2.063 | 0.392 | 0.402 | 55.111 | 14.481 | 1.000 |
| linux-x64/S1 | blake3 | warm | spin-0 | V2-W4 | 1 | 10 | resident (10) | 3.241 | 0.842 | 3.867 | 0.309 | 0.339 | 70.883 | 14.465 | 1.000 |
| linux-x64/S1 | blake3 | warm | spin-0 | V2-W8 | 1 | 10 | resident (10) | 3.151 | 0.818 | 3.859 | 0.317 | 0.329 | 99.139 | 14.857 | 1.000 |
| linux-x64/S1 | blake3 | cold | spin-0 | V0 | 1 | 10 | n/a (0) | 0.425 | 0.578 | 0.736 | 2.352 | 2.365 | 29.740 | — | — |
| linux-x64/S1 | blake3 | cold | spin-0 | V1 | 1 | 10 | n/a (0) | 0.430 | 1.185 | 0.364 | 2.324 | 2.339 | 43.957 | — | — |
| linux-x64/S1 | blake3 | cold | spin-0 | V2-W2 | 1 | 10 | n/a (0) | 0.431 | 1.037 | 0.416 | 2.318 | 2.348 | 52.867 | — | — |
| linux-x64/S1 | blake3 | cold | spin-0 | V2-W4 | 1 | 10 | n/a (0) | 0.431 | 0.718 | 0.601 | 2.318 | 2.357 | 69.043 | — | — |
| linux-x64/S1 | blake3 | cold | spin-0 | V2-W8 | 1 | 10 | n/a (0) | 0.429 | 0.642 | 0.664 | 2.331 | 2.363 | 93.377 | — | — |
| linux-x64/S1 | blake3 | throttled | spin-0 | V0 | 1 | 10 | resident (10) | 0.081 | 0.547 | 0.148 | 12.307 | 12.308 | 33.852 | 12.110 | 1.000 |
| linux-x64/S1 | blake3 | throttled | spin-0 | V1 | 1 | 10 | resident (10) | 0.081 | 0.907 | 0.089 | 12.341 | 12.342 | 47.436 | 12.258 | 1.000 |
| linux-x64/S1 | blake3 | throttled | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.162 | 0.874 | 0.185 | 6.188 | 6.189 | 56.178 | 12.331 | 1.000 |
| linux-x64/S1 | blake3 | throttled | spin-0 | V2-W4 | 1 | 10 | resident (10) | 0.322 | 0.815 | 0.396 | 3.101 | 3.103 | 69.012 | 11.899 | 1.000 |
| linux-x64/S1 | blake3 | throttled | spin-0 | V2-W8 | 1 | 10 | resident (10) | 0.634 | 0.712 | 0.890 | 1.578 | 1.580 | 99.607 | 12.300 | 1.000 |
| linux-x64/S1 | sha256 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.725 | 0.633 | 1.141 | 1.378 | 1.426 | 31.676 | 12.117 | 1.000 |
| linux-x64/S1 | sha256 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 1.261 | 1.223 | 1.032 | 0.793 | 0.815 | 45.455 | 10.905 | 1.000 |
| linux-x64/S1 | blake3 | warm | default | V0 | 1 | 10 | resident (10) | 0.742 | 0.421 | 1.752 | 1.348 | 1.412 | 31.877 | 12.065 | 1.000 |
| linux-x64/S1 | blake3 | warm | default | V1 | 1 | 10 | resident (10) | 1.282 | 1.227 | 1.046 | 0.780 | 0.789 | 46.232 | 12.213 | 1.000 |
| linux-x64/SL | blake3 | warm | spin-0 | V0 | 1 | 5 | resident (5) | 0.737 | 0.656 | 1.125 | 8.140 | 8.828 | 32.824 | 14.778 | 1.000 |
| linux-x64/SL | blake3 | warm | spin-0 | V1 | 1 | 5 | resident (5) | 1.326 | 1.311 | 1.012 | 4.525 | 4.547 | 56.289 | 15.252 | 1.000 |
| linux-x64/SL | blake3 | warm | spin-0 | V2-W2 | 1 | 5 | resident (5) | 2.573 | 1.252 | 2.056 | 2.332 | 2.359 | 65.492 | 15.276 | 1.000 |
| linux-x64/SL | blake3 | warm | spin-0 | V2-W4 | 1 | 5 | resident (5) | 3.338 | 0.845 | 3.953 | 1.798 | 1.963 | 81.109 | 14.973 | 1.000 |
| linux-x64/SL | blake3 | warm | spin-0 | V2-W8 | 1 | 5 | resident (5) | 3.193 | 0.812 | 3.938 | 1.879 | 2.038 | 108.957 | 15.408 | 1.000 |
| linux-x64/SL | blake3 | cold | spin-0 | V0 | 1 | 5 | n/a (0) | 0.390 | 0.593 | 0.658 | 15.365 | 15.384 | 29.902 | — | — |
| linux-x64/SL | blake3 | cold | spin-0 | V1 | 1 | 5 | n/a (0) | 0.391 | 1.183 | 0.330 | 15.358 | 15.369 | 53.223 | — | — |
| linux-x64/SL | blake3 | cold | spin-0 | V2-W2 | 1 | 5 | n/a (0) | 0.391 | 1.060 | 0.369 | 15.352 | 15.360 | 62.363 | — | — |
| linux-x64/SL | blake3 | cold | spin-0 | V2-W4 | 1 | 5 | n/a (0) | 0.391 | 0.689 | 0.567 | 15.357 | 15.371 | 74.844 | — | — |
| linux-x64/SL | blake3 | cold | spin-0 | V2-W8 | 1 | 5 | n/a (0) | 0.391 | 0.655 | 0.597 | 15.353 | 15.371 | 106.363 | — | — |
| linux-x64/SL | blake3 | warm | default | V0 | 1 | 5 | resident (5) | 0.788 | 0.444 | 1.774 | 7.619 | 8.235 | 32.465 | 12.877 | 1.000 |
| linux-x64/SL | blake3 | warm | default | V1 | 1 | 5 | resident (5) | 1.318 | 1.268 | 1.039 | 4.553 | 8.165 | 55.945 | 13.410 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.595 | 0.405 | 1.461 | 1.338 | 1.439 | 48.369 | 12.952 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 0.812 | 0.596 | 1.361 | 0.982 | 1.441 | 124.211 | 12.159 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.893 | 0.505 | 1.785 | 0.892 | 0.925 | 138.158 | 12.057 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V0 | 2 | 10 | resident (10) | 0.965 | 0.410 | 2.349 | 0.826 | 0.870 | 48.721 | 13.071 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V1 | 2 | 10 | resident (10) | 1.299 | 0.548 | 2.360 | 0.613 | 0.829 | 132.447 | 12.404 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V2-W2 | 2 | 10 | resident (10) | 1.505 | 0.581 | 2.604 | 0.529 | 0.545 | 178.430 | 12.654 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V0 | 4 | 10 | resident (10) | 1.395 | 0.404 | 3.439 | 0.571 | 0.617 | 53.703 | 13.092 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V1 | 4 | 10 | resident (10) | 1.937 | 0.608 | 3.240 | 0.411 | 0.458 | 179.670 | 12.329 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V2-W2 | 4 | 10 | resident (10) | 1.843 | 0.531 | 3.448 | 0.432 | 0.451 | 224.775 | 12.788 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V0 | 8 | 10 | resident (10) | 1.455 | 0.406 | 3.659 | 0.548 | 0.608 | 67.709 | 2.511 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V1 | 8 | 10 | resident (10) | 2.011 | 0.567 | 3.541 | 0.396 | 0.426 | 229.740 | 13.282 | 1.000 |
| linux-x64/T | blake3 | warm | spin-0 | V2-W2 | 8 | 10 | resident (10) | 1.781 | 0.493 | 3.647 | 0.447 | 0.464 | 278.490 | 12.849 | 1.000 |
| linux-x64/T | blake3 | cold | spin-0 | V0 | 1 | 10 | n/a (0) | 0.312 | 0.333 | 0.942 | 2.554 | 2.806 | 37.588 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V1 | 1 | 10 | n/a (0) | 0.386 | 0.480 | 0.807 | 2.063 | 2.161 | 115.531 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V2-W2 | 1 | 10 | n/a (0) | 0.386 | 0.415 | 0.929 | 2.066 | 2.126 | 134.693 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V0 | 2 | 10 | n/a (0) | 0.426 | 0.300 | 1.427 | 1.869 | 1.877 | 40.045 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V1 | 2 | 10 | n/a (0) | 0.429 | 0.433 | 0.993 | 1.856 | 1.868 | 125.945 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V2-W2 | 2 | 10 | n/a (0) | 0.429 | 0.375 | 1.150 | 1.858 | 1.871 | 179.121 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V0 | 4 | 10 | n/a (0) | 0.430 | 0.281 | 1.526 | 1.851 | 1.868 | 40.924 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V1 | 4 | 10 | n/a (0) | 0.433 | 0.414 | 1.042 | 1.840 | 1.861 | 158.393 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V2-W2 | 4 | 10 | n/a (0) | 0.431 | 0.369 | 1.170 | 1.849 | 1.875 | 230.467 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V0 | 8 | 10 | n/a (0) | 0.434 | 0.286 | 1.508 | 1.837 | 1.869 | 47.762 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V1 | 8 | 10 | n/a (0) | 0.429 | 0.402 | 1.071 | 1.858 | 1.865 | 225.232 | — | — |
| linux-x64/T | blake3 | cold | spin-0 | V2-W2 | 8 | 10 | n/a (0) | 0.432 | 0.347 | 1.246 | 1.846 | 1.857 | 274.873 | — | — |
| linux-x64/T | blake3 | throttled | spin-0 | V0 | 1 | 10 | resident (10) | 0.031 | 0.231 | 0.133 | 25.849 | 25.849 | 48.676 | 11.286 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V1 | 1 | 10 | resident (10) | 0.025 | 0.296 | 0.083 | 32.182 | 32.183 | 107.203 | 11.341 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.032 | 0.302 | 0.105 | 25.075 | 25.136 | 134.523 | 10.626 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V0 | 2 | 10 | resident (10) | 0.060 | 0.230 | 0.260 | 13.315 | 13.316 | 49.105 | 11.024 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V1 | 2 | 10 | resident (10) | 0.048 | 0.316 | 0.153 | 16.429 | 16.431 | 120.771 | 11.200 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V2-W2 | 2 | 10 | resident (10) | 0.063 | 0.319 | 0.198 | 12.644 | 12.667 | 160.795 | 9.661 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V0 | 4 | 10 | resident (10) | 0.111 | 0.253 | 0.438 | 7.176 | 7.178 | 78.805 | 11.256 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V1 | 4 | 10 | resident (10) | 0.091 | 0.339 | 0.268 | 8.761 | 8.762 | 147.734 | 10.885 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V2-W2 | 4 | 10 | resident (10) | 0.121 | 0.336 | 0.359 | 6.590 | 6.601 | 177.582 | 10.693 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V0 | 8 | 10 | resident (10) | 0.202 | 0.264 | 0.768 | 3.935 | 3.936 | 120.246 | 11.166 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V1 | 8 | 10 | resident (10) | 0.165 | 0.357 | 0.464 | 4.815 | 4.816 | 182.414 | 10.843 | 1.000 |
| linux-x64/T | blake3 | throttled | spin-0 | V2-W2 | 8 | 10 | resident (10) | 0.224 | 0.351 | 0.640 | 3.556 | 3.561 | 206.543 | 11.166 | 1.000 |
| win-x64/S1 | blake3 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.603 | 0.582 | 1.028 | 1.660 | 1.889 | 19.232 | 6.732 | — |
| win-x64/S1 | blake3 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 1.135 | 1.143 | 1.001 | 0.881 | 1.155 | 34.203 | 6.804 | — |
| win-x64/S1 | blake3 | warm | spin-0 | V2-W2 | 1 | 10 | resident (10) | 2.089 | 1.050 | 2.008 | 0.479 | 0.601 | 38.984 | 7.451 | — |
| win-x64/S1 | blake3 | warm | spin-0 | V2-W4 | 1 | 10 | resident (10) | 2.682 | 0.781 | 3.588 | 0.373 | 0.636 | 53.795 | 6.908 | — |
| win-x64/S1 | blake3 | warm | spin-0 | V2-W8 | 1 | 10 | resident (10) | 2.684 | 0.762 | 3.593 | 0.373 | 0.493 | 85.955 | 7.274 | — |
| win-x64/S1 | blake3 | throttled | spin-0 | V0 | 1 | 10 | resident (10) | 0.081 | 0.585 | 0.137 | 12.317 | 12.827 | 19.521 | 7.150 | — |
| win-x64/S1 | blake3 | throttled | spin-0 | V1 | 1 | 10 | resident (10) | 0.081 | 0.970 | 0.084 | 12.348 | 12.361 | 32.377 | 7.189 | — |
| win-x64/S1 | blake3 | throttled | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.161 | 0.856 | 0.189 | 6.197 | 6.213 | 38.688 | 6.817 | — |
| win-x64/S1 | blake3 | throttled | spin-0 | V2-W4 | 1 | 10 | resident (10) | 0.321 | 0.731 | 0.440 | 3.114 | 3.228 | 55.084 | 7.454 | — |
| win-x64/S1 | blake3 | throttled | spin-0 | V2-W8 | 1 | 10 | resident (10) | 0.627 | 0.699 | 0.900 | 1.594 | 1.618 | 87.566 | 7.616 | — |
| win-x64/S1 | sha256 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.537 | 0.536 | 1.020 | 1.862 | 2.019 | 19.562 | 6.420 | — |
| win-x64/S1 | sha256 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 1.074 | 1.058 | 0.999 | 0.931 | 1.054 | 32.441 | 6.810 | — |
| win-x64/S1 | blake3 | warm | default | V0 | 1 | 10 | resident (10) | 0.582 | 0.489 | 1.170 | 1.722 | 2.125 | 19.408 | 6.399 | — |
| win-x64/S1 | blake3 | warm | default | V1 | 1 | 10 | resident (10) | 1.061 | 1.068 | 0.998 | 0.946 | 7.415 | 34.020 | 6.461 | — |
| win-x64/SL | blake3 | warm | spin-0 | V0 | 1 | 5 | unverified-warm (0) | 0.218 | 0.489 | 0.445 | 27.488 | 29.532 | 15.883 | 0.398 | — |
| win-x64/SL | blake3 | warm | spin-0 | V1 | 1 | 5 | unverified-warm (0) | 0.381 | 1.043 | 0.392 | 15.743 | 15.792 | 40.105 | 0.388 | — |
| win-x64/SL | blake3 | warm | spin-0 | V2-W2 | 1 | 5 | unverified-warm (0) | 0.407 | 0.921 | 0.441 | 14.732 | 15.781 | 44.648 | 0.389 | — |
| win-x64/SL | blake3 | warm | spin-0 | V2-W4 | 1 | 5 | unverified-warm (0) | 0.409 | 0.855 | 0.473 | 14.685 | 15.013 | 59.242 | 0.510 | — |
| win-x64/SL | blake3 | warm | spin-0 | V2-W8 | 1 | 5 | unverified-warm (0) | 0.418 | 0.814 | 0.510 | 14.349 | 14.942 | 91.148 | 0.458 | — |
| win-x64/SL | blake3 | warm | default | V0 | 1 | 5 | unverified-warm (0) | 0.218 | 0.443 | 0.499 | 27.462 | 28.678 | 15.863 | 0.504 | — |
| win-x64/SL | blake3 | warm | default | V1 | 1 | 5 | unverified-warm (0) | 0.404 | 1.019 | 0.390 | 14.855 | 15.756 | 40.004 | 0.477 | — |
| win-x64/T | blake3 | warm | spin-0 | V0 | 1 | 10 | resident (10) | 0.431 | 0.340 | 1.241 | 1.847 | 2.148 | 42.527 | 4.188 | — |
| win-x64/T | blake3 | warm | spin-0 | V1 | 1 | 10 | resident (10) | 0.648 | 0.505 | 1.257 | 1.230 | 1.430 | 93.561 | 4.385 | — |
| win-x64/T | blake3 | warm | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.730 | 0.438 | 1.646 | 1.092 | 1.151 | 123.885 | 4.184 | — |
| win-x64/T | blake3 | warm | spin-0 | V0 | 2 | 10 | resident (10) | 0.723 | 0.337 | 2.173 | 1.102 | 1.194 | 37.822 | 4.405 | — |
| win-x64/T | blake3 | warm | spin-0 | V1 | 2 | 10 | resident (10) | 1.049 | 0.462 | 2.310 | 0.759 | 0.871 | 115.334 | 4.244 | — |
| win-x64/T | blake3 | warm | spin-0 | V2-W2 | 2 | 10 | resident (10) | 1.215 | 0.457 | 2.698 | 0.656 | 0.670 | 168.223 | 4.181 | — |
| win-x64/T | blake3 | warm | spin-0 | V0 | 4 | 10 | resident (10) | 1.186 | 0.352 | 3.383 | 0.672 | 0.779 | 42.662 | 4.473 | — |
| win-x64/T | blake3 | warm | spin-0 | V1 | 4 | 10 | resident (10) | 1.693 | 0.505 | 3.327 | 0.471 | 0.662 | 149.992 | 4.503 | — |
| win-x64/T | blake3 | warm | spin-0 | V2-W2 | 4 | 10 | resident (10) | 1.785 | 0.513 | 3.472 | 0.446 | 0.654 | 178.123 | 4.045 | — |
| win-x64/T | blake3 | warm | spin-0 | V0 | 8 | 10 | resident (10) | 1.228 | 0.360 | 3.485 | 0.649 | 0.889 | 50.264 | 4.211 | — |
| win-x64/T | blake3 | warm | spin-0 | V1 | 8 | 10 | resident (10) | 1.780 | 0.502 | 3.561 | 0.447 | 0.591 | 180.578 | 4.211 | — |
| win-x64/T | blake3 | warm | spin-0 | V2-W2 | 8 | 10 | resident (10) | 1.746 | 0.515 | 3.467 | 0.456 | 0.608 | 230.156 | 4.269 | — |
| win-x64/T | blake3 | throttled | spin-0 | V0 | 1 | 10 | resident (10) | 0.031 | 0.229 | 0.134 | 25.860 | 25.869 | 39.539 | 4.350 | — |
| win-x64/T | blake3 | throttled | spin-0 | V1 | 1 | 10 | resident (10) | 0.025 | 0.349 | 0.071 | 32.203 | 32.241 | 96.406 | 3.868 | — |
| win-x64/T | blake3 | throttled | spin-0 | V2-W2 | 1 | 10 | resident (10) | 0.032 | 0.329 | 0.096 | 25.077 | 25.148 | 120.230 | 4.006 | — |
| win-x64/T | blake3 | throttled | spin-0 | V0 | 2 | 10 | resident (10) | 0.060 | 0.217 | 0.276 | 13.332 | 13.334 | 66.006 | 4.453 | — |
| win-x64/T | blake3 | throttled | spin-0 | V1 | 2 | 10 | resident (10) | 0.048 | 0.334 | 0.145 | 16.447 | 16.469 | 109.377 | 4.169 | — |
| win-x64/T | blake3 | throttled | spin-0 | V2-W2 | 2 | 10 | resident (10) | 0.063 | 0.334 | 0.188 | 12.670 | 12.723 | 138.887 | 4.080 | — |
| win-x64/T | blake3 | throttled | spin-0 | V0 | 4 | 10 | resident (10) | 0.111 | 0.226 | 0.491 | 7.191 | 7.206 | 98.141 | 4.337 | — |
| win-x64/T | blake3 | throttled | spin-0 | V1 | 4 | 10 | resident (10) | 0.091 | 0.323 | 0.281 | 8.775 | 8.785 | 162.320 | 4.326 | — |
| win-x64/T | blake3 | throttled | spin-0 | V2-W2 | 4 | 10 | resident (10) | 0.120 | 0.328 | 0.366 | 6.623 | 6.642 | 170.893 | 4.068 | — |
| win-x64/T | blake3 | throttled | spin-0 | V0 | 8 | 10 | resident (10) | 0.202 | 0.209 | 0.963 | 3.952 | 3.973 | 120.648 | 4.444 | — |
| win-x64/T | blake3 | throttled | spin-0 | V1 | 8 | 10 | resident (10) | 0.165 | 0.339 | 0.487 | 4.834 | 4.854 | 198.834 | 4.204 | — |
| win-x64/T | blake3 | throttled | spin-0 | V2-W2 | 8 | 10 | resident (10) | 0.222 | 0.321 | 0.691 | 3.582 | 3.767 | 225.859 | 1.993 | — |

Runs:
- CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-arm64 (linux-arm64), plan f7fdb5a7ea7067da58be98d4c4ecb20ca006ca485a516348ea299489af612f73, commit 56e49e2ba2886207e8158d949602128303056bf4, 250 samples, memory 16722010112 bytes, S1 1073741824 bytes, SL 6442450944 bytes
- CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-x64 (linux-x64), plan f7fdb5a7ea7067da58be98d4c4ecb20ca006ca485a516348ea299489af612f73, commit 56e49e2ba2886207e8158d949602128303056bf4, 250 samples, memory 16766414848 bytes, S1 1073741824 bytes, SL 6442450944 bytes
- CORE-VERIFY-002/RUN-20260929-14-56e49e2-win-x64 (win-x64), plan 7cee96c990678ccd38cbf81d12b9aa44c62013a7d9de1f824caec830c641d965, commit 56e49e2ba2886207e8158d949602128303056bf4, 175 samples, memory 17174360064 bytes, S1 1073741824 bytes, SL 6442450944 bytes
- CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-arm64 (linux-arm64), plan b5f7201bfeaadb62345ca2a7e005b69f6a3687a657e82bd4b8f679d67ad48af3, commit 56e49e2ba2886207e8158d949602128303056bf4, 360 samples, memory 16722046976 bytes, T 855440301 bytes
- CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-x64 (linux-x64), plan b5f7201bfeaadb62345ca2a7e005b69f6a3687a657e82bd4b8f679d67ad48af3, commit 56e49e2ba2886207e8158d949602128303056bf4, 360 samples, memory 16766414848 bytes, T 855440301 bytes
- CORE-VERIFY-002/RUN-20260929-14-56e49e2-win-x64 (win-x64), plan dc3471dfa39eeb5145b538f45376933bc354172b5bd8b93db5162b3345c70b1e, commit 56e49e2ba2886207e8158d949602128303056bf4, 240 samples, memory 17174360064 bytes, T 855440301 bytes
