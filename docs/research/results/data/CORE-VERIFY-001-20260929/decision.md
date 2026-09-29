# CORE-VERIFY-001 decision: ADOPT

R1, R2 and R3 hold on 2 of 3 platforms

| platform | oracle | R1 | R2 | R3 |
|---|---|---|---|---|
| linux-arm64 | passed | holds | holds | holds |
| linux-x64 | passed | holds | holds | holds |
| win-x64 | passed | holds | fails | holds |

- linux-arm64 R1: S1 warm: V1/V0 = 2.177 GiB per CPU-second (0.963 / 0.443); S10 warm: V1/V0 = 2.172 GiB per CPU-second (0.976 / 0.449)
- linux-arm64 R2: warm: best V2 on S10 3.930 GiB/s vs best V1 x K on T 2.205 GiB/s; throttled: best V2 on S1 0.634 GiB/s vs best V1 x K on T 0.165 GiB/s
- linux-arm64 R3: warm: best V2-W2 x K / best V1 x K on T = 0.981 (2.164 / 2.205 GiB/s); throttled: best V2-W2 x K / best V1 x K on T = 1.354 (0.224 / 0.165 GiB/s)
- linux-x64 R1: S1 warm: V1/V0 = 4.223 GiB per CPU-second (2.728 / 0.646); S10 warm: V1/V0 = 4.527 GiB per CPU-second (2.878 / 0.636)
- linux-x64 R2: warm: best V2 on S10 8.148 GiB/s vs best V1 x K on T 3.672 GiB/s; throttled: best V2 on S1 0.635 GiB/s vs best V1 x K on T 0.165 GiB/s
- linux-x64 R3: warm: best V2-W2 x K / best V1 x K on T = 0.994 (3.649 / 3.672 GiB/s); throttled: best V2-W2 x K / best V1 x K on T = 1.357 (0.225 / 0.165 GiB/s)
- win-x64 R1: S1 warm: V1/V0 = 2.196 GiB per CPU-second (1.196 / 0.545); S10 warm: V1/V0 = 2.639 GiB per CPU-second (0.986 / 0.374)
- win-x64 R2: warm: best V2 on S10 0.380 GiB/s vs best V1 x K on T 1.657 GiB/s; throttled: best V2 on S1 0.626 GiB/s vs best V1 x K on T 0.165 GiB/s
- win-x64 R3: warm: best V2-W2 x K / best V1 x K on T = 1.047 (1.735 / 1.657 GiB/s); throttled: best V2-W2 x K / best V1 x K on T = 1.347 (0.222 / 0.165 GiB/s)

| platform/workload | suite | mode | lane | K | n | p50 GiB/s | p50 GiB per CPU-s | p50 cores | p50 wall s | p95 wall s | p50 peak over idle MiB |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| linux-arm64/S1 | blake3 | warm | V0 | 1 | 10 | 0.661 | 0.443 | 1.493 | 1.512 | 1.536 | 32.006 |
| linux-arm64/S1 | blake3 | warm | V1 | 1 | 10 | 0.983 | 0.963 | 1.020 | 1.017 | 1.048 | 44.980 |
| linux-arm64/S1 | blake3 | warm | V2-W2 | 1 | 10 | 1.967 | 0.954 | 2.060 | 0.508 | 0.511 | 50.168 |
| linux-arm64/S1 | blake3 | warm | V2-W4 | 1 | 10 | 3.772 | 0.979 | 3.860 | 0.265 | 0.267 | 70.836 |
| linux-arm64/S1 | blake3 | warm | V2-W8 | 1 | 10 | 3.760 | 0.977 | 3.851 | 0.266 | 0.270 | 98.984 |
| linux-arm64/S1 | blake3 | cold | V0 | 1 | 10 | 0.424 | 0.395 | 1.075 | 2.357 | 2.369 | 30.211 |
| linux-arm64/S1 | blake3 | cold | V1 | 1 | 10 | 0.427 | 0.920 | 0.464 | 2.345 | 2.350 | 42.994 |
| linux-arm64/S1 | blake3 | cold | V2-W2 | 1 | 10 | 0.429 | 0.836 | 0.513 | 2.333 | 2.352 | 48.299 |
| linux-arm64/S1 | blake3 | cold | V2-W4 | 1 | 10 | 0.428 | 0.787 | 0.542 | 2.338 | 2.367 | 68.635 |
| linux-arm64/S1 | blake3 | cold | V2-W8 | 1 | 10 | 0.427 | 0.761 | 0.565 | 2.342 | 2.365 | 97.123 |
| linux-arm64/S1 | blake3 | throttled | V0 | 1 | 10 | 0.081 | 0.532 | 0.153 | 12.307 | 12.308 | 34.350 |
| linux-arm64/S1 | blake3 | throttled | V1 | 1 | 10 | 0.081 | 0.721 | 0.112 | 12.341 | 12.342 | 46.664 |
| linux-arm64/S1 | blake3 | throttled | V2-W2 | 1 | 10 | 0.162 | 0.752 | 0.215 | 6.188 | 6.189 | 51.736 |
| linux-arm64/S1 | blake3 | throttled | V2-W4 | 1 | 10 | 0.322 | 0.761 | 0.424 | 3.101 | 3.102 | 70.891 |
| linux-arm64/S1 | blake3 | throttled | V2-W8 | 1 | 10 | 0.634 | 0.775 | 0.817 | 1.577 | 1.579 | 99.283 |
| linux-arm64/S1 | sha256 | warm | V0 | 1 | 10 | 0.863 | 0.525 | 1.642 | 1.159 | 1.175 | 32.012 |
| linux-arm64/S1 | sha256 | warm | V1 | 1 | 10 | 1.548 | 1.487 | 1.045 | 0.646 | 0.650 | 45.846 |
| linux-arm64/S10 | blake3 | warm | V0 | 1 | 5 | 0.666 | 0.449 | 1.487 | 15.022 | 15.132 | 32.770 |
| linux-arm64/S10 | blake3 | warm | V1 | 1 | 5 | 0.993 | 0.976 | 1.018 | 10.067 | 10.100 | 63.805 |
| linux-arm64/S10 | blake3 | warm | V2-W2 | 1 | 5 | 2.001 | 0.971 | 2.063 | 4.999 | 5.009 | 69.504 |
| linux-arm64/S10 | blake3 | warm | V2-W4 | 1 | 5 | 3.930 | 0.995 | 3.952 | 2.544 | 2.576 | 93.305 |
| linux-arm64/S10 | blake3 | warm | V2-W8 | 1 | 5 | 3.917 | 0.990 | 3.955 | 2.553 | 2.567 | 117.289 |
| linux-arm64/S10 | blake3 | cold | V0 | 1 | 5 | 0.388 | 0.401 | 0.968 | 25.784 | 25.791 | 30.641 |
| linux-arm64/S10 | blake3 | cold | V1 | 1 | 5 | 0.388 | 0.933 | 0.416 | 25.773 | 25.780 | 61.109 |
| linux-arm64/S10 | blake3 | cold | V2-W2 | 1 | 5 | 0.388 | 0.866 | 0.449 | 25.757 | 25.762 | 67.090 |
| linux-arm64/S10 | blake3 | cold | V2-W4 | 1 | 5 | 0.388 | 0.808 | 0.481 | 25.755 | 25.782 | 90.559 |
| linux-arm64/S10 | blake3 | cold | V2-W8 | 1 | 5 | 0.388 | 0.779 | 0.498 | 25.770 | 25.832 | 114.773 |
| linux-arm64/T | blake3 | warm | V0 | 1 | 10 | 0.533 | 0.302 | 1.762 | 1.495 | 1.515 | 52.258 |
| linux-arm64/T | blake3 | warm | V1 | 1 | 10 | 0.662 | 0.436 | 1.517 | 1.204 | 1.260 | 138.408 |
| linux-arm64/T | blake3 | warm | V2-W2 | 1 | 10 | 0.832 | 0.404 | 2.044 | 0.958 | 0.975 | 138.541 |
| linux-arm64/T | blake3 | warm | V0 | 2 | 10 | 1.001 | 0.381 | 2.655 | 0.796 | 0.814 | 52.715 |
| linux-arm64/T | blake3 | warm | V1 | 2 | 10 | 1.261 | 0.494 | 2.562 | 0.632 | 0.671 | 142.381 |
| linux-arm64/T | blake3 | warm | V2-W2 | 2 | 10 | 1.518 | 0.535 | 2.857 | 0.525 | 0.586 | 159.170 |
| linux-arm64/T | blake3 | warm | V0 | 4 | 10 | 1.760 | 0.509 | 3.467 | 0.453 | 0.484 | 57.057 |
| linux-arm64/T | blake3 | warm | V1 | 4 | 10 | 2.205 | 0.652 | 3.367 | 0.361 | 0.405 | 175.361 |
| linux-arm64/T | blake3 | warm | V2-W2 | 4 | 10 | 2.164 | 0.612 | 3.511 | 0.368 | 0.407 | 222.338 |
| linux-arm64/T | blake3 | warm | V0 | 8 | 10 | 1.825 | 0.507 | 3.587 | 0.437 | 0.456 | 77.648 |
| linux-arm64/T | blake3 | warm | V1 | 8 | 10 | 2.141 | 0.604 | 3.556 | 0.372 | 0.399 | 212.248 |
| linux-arm64/T | blake3 | warm | V2-W2 | 8 | 10 | 2.070 | 0.564 | 3.685 | 0.385 | 0.403 | 257.656 |
| linux-arm64/T | blake3 | cold | V0 | 1 | 10 | 0.185 | 0.208 | 0.891 | 4.304 | 4.869 | 39.051 |
| linux-arm64/T | blake3 | cold | V1 | 1 | 10 | 0.191 | 0.289 | 0.661 | 4.174 | 5.304 | 107.621 |
| linux-arm64/T | blake3 | cold | V2-W2 | 1 | 10 | 0.222 | 0.270 | 0.825 | 3.591 | 4.353 | 123.676 |
| linux-arm64/T | blake3 | cold | V0 | 2 | 10 | 0.340 | 0.203 | 1.679 | 2.347 | 2.944 | 40.230 |
| linux-arm64/T | blake3 | cold | V1 | 2 | 10 | 0.346 | 0.280 | 1.245 | 2.300 | 2.401 | 132.268 |
| linux-arm64/T | blake3 | cold | V2-W2 | 2 | 10 | 0.401 | 0.270 | 1.485 | 1.988 | 2.200 | 154.234 |
| linux-arm64/T | blake3 | cold | V0 | 4 | 10 | 0.417 | 0.225 | 1.863 | 1.909 | 2.113 | 41.709 |
| linux-arm64/T | blake3 | cold | V1 | 4 | 10 | 0.422 | 0.297 | 1.427 | 1.886 | 1.907 | 189.977 |
| linux-arm64/T | blake3 | cold | V2-W2 | 4 | 10 | 0.429 | 0.285 | 1.502 | 1.858 | 1.869 | 195.406 |
| linux-arm64/T | blake3 | cold | V0 | 8 | 10 | 0.427 | 0.244 | 1.751 | 1.865 | 1.883 | 45.180 |
| linux-arm64/T | blake3 | cold | V1 | 8 | 10 | 0.425 | 0.321 | 1.323 | 1.873 | 1.887 | 223.312 |
| linux-arm64/T | blake3 | cold | V2-W2 | 8 | 10 | 0.429 | 0.298 | 1.441 | 1.857 | 1.877 | 265.533 |
| linux-arm64/T | blake3 | throttled | V0 | 1 | 10 | 0.031 | 0.158 | 0.195 | 25.849 | 25.850 | 52.162 |
| linux-arm64/T | blake3 | throttled | V1 | 1 | 10 | 0.025 | 0.195 | 0.127 | 32.182 | 32.183 | 109.145 |
| linux-arm64/T | blake3 | throttled | V2-W2 | 1 | 10 | 0.032 | 0.206 | 0.154 | 25.083 | 25.138 | 112.910 |
| linux-arm64/T | blake3 | throttled | V0 | 2 | 10 | 0.060 | 0.161 | 0.371 | 13.316 | 13.317 | 54.875 |
| linux-arm64/T | blake3 | throttled | V1 | 2 | 10 | 0.048 | 0.209 | 0.232 | 16.430 | 16.432 | 134.434 |
| linux-arm64/T | blake3 | throttled | V2-W2 | 2 | 10 | 0.063 | 0.218 | 0.288 | 12.658 | 12.673 | 140.305 |
| linux-arm64/T | blake3 | throttled | V0 | 4 | 10 | 0.111 | 0.196 | 0.566 | 7.176 | 7.178 | 82.773 |
| linux-arm64/T | blake3 | throttled | V1 | 4 | 10 | 0.091 | 0.226 | 0.402 | 8.761 | 8.763 | 149.502 |
| linux-arm64/T | blake3 | throttled | V2-W2 | 4 | 10 | 0.121 | 0.238 | 0.509 | 6.602 | 6.610 | 176.195 |
| linux-arm64/T | blake3 | throttled | V0 | 8 | 10 | 0.203 | 0.229 | 0.883 | 3.934 | 3.936 | 120.467 |
| linux-arm64/T | blake3 | throttled | V1 | 8 | 10 | 0.165 | 0.263 | 0.629 | 4.815 | 4.816 | 185.865 |
| linux-arm64/T | blake3 | throttled | V2-W2 | 8 | 10 | 0.224 | 0.274 | 0.817 | 3.556 | 3.562 | 213.252 |
| linux-x64/S1 | blake3 | warm | V0 | 1 | 10 | 1.300 | 0.646 | 2.015 | 0.770 | 0.817 | 32.057 |
| linux-x64/S1 | blake3 | warm | V1 | 1 | 10 | 3.030 | 2.728 | 1.108 | 0.330 | 0.339 | 45.932 |
| linux-x64/S1 | blake3 | warm | V2-W2 | 1 | 10 | 5.740 | 2.545 | 2.251 | 0.174 | 0.177 | 50.402 |
| linux-x64/S1 | blake3 | warm | V2-W4 | 1 | 10 | 7.653 | 2.035 | 3.785 | 0.131 | 0.138 | 74.109 |
| linux-x64/S1 | blake3 | warm | V2-W8 | 1 | 10 | 7.390 | 1.959 | 3.788 | 0.135 | 0.148 | 95.758 |
| linux-x64/S1 | blake3 | cold | V0 | 1 | 10 | 0.400 | 0.541 | 0.744 | 2.500 | 5.402 | 30.316 |
| linux-x64/S1 | blake3 | cold | V1 | 1 | 10 | 0.316 | 2.083 | 0.151 | 3.167 | 5.229 | 43.660 |
| linux-x64/S1 | blake3 | cold | V2-W2 | 1 | 10 | 0.309 | 1.528 | 0.200 | 3.265 | 5.351 | 48.797 |
| linux-x64/S1 | blake3 | cold | V2-W4 | 1 | 10 | 0.420 | 1.292 | 0.316 | 2.381 | 3.716 | 72.717 |
| linux-x64/S1 | blake3 | cold | V2-W8 | 1 | 10 | 0.379 | 1.251 | 0.309 | 2.642 | 3.250 | 95.150 |
| linux-x64/S1 | blake3 | throttled | V0 | 1 | 10 | 0.081 | 0.781 | 0.104 | 12.306 | 12.307 | 34.289 |
| linux-x64/S1 | blake3 | throttled | V1 | 1 | 10 | 0.081 | 1.176 | 0.069 | 12.339 | 12.339 | 47.139 |
| linux-x64/S1 | blake3 | throttled | V2-W2 | 1 | 10 | 0.162 | 1.139 | 0.142 | 6.186 | 6.186 | 51.473 |
| linux-x64/S1 | blake3 | throttled | V2-W4 | 1 | 10 | 0.323 | 1.177 | 0.274 | 3.098 | 3.099 | 74.844 |
| linux-x64/S1 | blake3 | throttled | V2-W8 | 1 | 10 | 0.635 | 1.193 | 0.532 | 1.574 | 1.575 | 97.605 |
| linux-x64/S1 | sha256 | warm | V0 | 1 | 10 | 0.868 | 0.474 | 1.831 | 1.152 | 1.193 | 31.943 |
| linux-x64/S1 | sha256 | warm | V1 | 1 | 10 | 1.465 | 1.394 | 1.050 | 0.683 | 0.693 | 45.639 |
| linux-x64/S10 | blake3 | warm | V0 | 1 | 5 | 1.265 | 0.636 | 1.992 | 7.905 | 7.968 | 32.863 |
| linux-x64/S10 | blake3 | warm | V1 | 1 | 5 | 3.087 | 2.878 | 1.073 | 3.239 | 3.277 | 61.156 |
| linux-x64/S10 | blake3 | warm | V2-W2 | 1 | 5 | 6.011 | 2.651 | 2.267 | 1.664 | 1.664 | 65.805 |
| linux-x64/S10 | blake3 | warm | V2-W4 | 1 | 5 | 8.148 | 2.071 | 3.934 | 1.227 | 1.247 | 85.102 |
| linux-x64/S10 | blake3 | warm | V2-W8 | 1 | 5 | 7.501 | 1.911 | 3.930 | 1.333 | 1.357 | 112.203 |
| linux-x64/S10 | blake3 | cold | V0 | 1 | 5 | 0.315 | 0.543 | 0.560 | 31.749 | 49.949 | 30.281 |
| linux-x64/S10 | blake3 | cold | V1 | 1 | 5 | 0.310 | 2.135 | 0.143 | 32.214 | 45.542 | 54.277 |
| linux-x64/S10 | blake3 | cold | V2-W2 | 1 | 5 | 0.287 | 1.555 | 0.191 | 34.865 | 43.077 | 63.348 |
| linux-x64/S10 | blake3 | cold | V2-W4 | 1 | 5 | 0.314 | 1.323 | 0.236 | 31.851 | 41.174 | 82.738 |
| linux-x64/S10 | blake3 | cold | V2-W8 | 1 | 5 | 0.363 | 1.286 | 0.280 | 27.583 | 29.712 | 108.930 |
| linux-x64/T | blake3 | warm | V0 | 1 | 10 | 0.932 | 0.465 | 1.988 | 0.854 | 0.884 | 62.270 |
| linux-x64/T | blake3 | warm | V1 | 1 | 10 | 1.442 | 0.784 | 1.817 | 0.553 | 0.583 | 124.662 |
| linux-x64/T | blake3 | warm | V2-W2 | 1 | 10 | 1.645 | 0.772 | 2.132 | 0.485 | 0.535 | 140.822 |
| linux-x64/T | blake3 | warm | V0 | 2 | 10 | 1.666 | 0.609 | 2.752 | 0.478 | 0.516 | 63.271 |
| linux-x64/T | blake3 | warm | V1 | 2 | 10 | 2.450 | 0.944 | 2.616 | 0.325 | 0.338 | 138.018 |
| linux-x64/T | blake3 | warm | V2-W2 | 2 | 10 | 2.907 | 1.005 | 2.883 | 0.274 | 0.294 | 186.246 |
| linux-x64/T | blake3 | warm | V0 | 4 | 10 | 2.655 | 0.770 | 3.474 | 0.300 | 0.312 | 60.771 |
| linux-x64/T | blake3 | warm | V1 | 4 | 10 | 3.672 | 1.138 | 3.274 | 0.217 | 0.238 | 191.979 |
| linux-x64/T | blake3 | warm | V2-W2 | 4 | 10 | 3.649 | 1.065 | 3.468 | 0.218 | 0.241 | 241.395 |
| linux-x64/T | blake3 | warm | V0 | 8 | 10 | 2.730 | 0.771 | 3.600 | 0.292 | 0.313 | 87.721 |
| linux-x64/T | blake3 | warm | V1 | 8 | 10 | 3.572 | 1.044 | 3.466 | 0.223 | 0.232 | 233.412 |
| linux-x64/T | blake3 | warm | V2-W2 | 8 | 10 | 3.483 | 0.989 | 3.535 | 0.229 | 0.246 | 300.176 |
| linux-x64/T | blake3 | cold | V0 | 1 | 10 | 0.068 | 0.367 | 0.187 | 11.655 | 17.342 | 37.713 |
| linux-x64/T | blake3 | cold | V1 | 1 | 10 | 0.075 | 0.580 | 0.131 | 10.823 | 16.353 | 105.623 |
| linux-x64/T | blake3 | cold | V2-W2 | 1 | 10 | 0.086 | 0.555 | 0.156 | 9.245 | 12.207 | 154.107 |
| linux-x64/T | blake3 | cold | V0 | 2 | 10 | 0.103 | 0.349 | 0.292 | 7.766 | 15.546 | 39.412 |
| linux-x64/T | blake3 | cold | V1 | 2 | 10 | 0.148 | 0.550 | 0.269 | 5.491 | 15.153 | 141.855 |
| linux-x64/T | blake3 | cold | V2-W2 | 2 | 10 | 0.199 | 0.508 | 0.383 | 4.014 | 13.512 | 151.297 |
| linux-x64/T | blake3 | cold | V0 | 4 | 10 | 0.172 | 0.370 | 0.466 | 4.624 | 6.168 | 41.131 |
| linux-x64/T | blake3 | cold | V1 | 4 | 10 | 0.165 | 0.534 | 0.310 | 4.843 | 8.703 | 160.154 |
| linux-x64/T | blake3 | cold | V2-W2 | 4 | 10 | 0.201 | 0.508 | 0.399 | 3.975 | 6.337 | 234.074 |
| linux-x64/T | blake3 | cold | V0 | 8 | 10 | 0.195 | 0.379 | 0.502 | 4.104 | 6.654 | 47.221 |
| linux-x64/T | blake3 | cold | V1 | 8 | 10 | 0.231 | 0.540 | 0.424 | 3.452 | 5.118 | 241.279 |
| linux-x64/T | blake3 | cold | V2-W2 | 8 | 10 | 0.190 | 0.508 | 0.378 | 4.185 | 6.155 | 255.170 |
| linux-x64/T | blake3 | throttled | V0 | 1 | 10 | 0.031 | 0.257 | 0.120 | 25.848 | 25.849 | 60.596 |
| linux-x64/T | blake3 | throttled | V1 | 1 | 10 | 0.025 | 0.320 | 0.077 | 32.181 | 32.182 | 100.150 |
| linux-x64/T | blake3 | throttled | V2-W2 | 1 | 10 | 0.032 | 0.330 | 0.096 | 25.027 | 25.097 | 135.283 |
| linux-x64/T | blake3 | throttled | V0 | 2 | 10 | 0.060 | 0.261 | 0.230 | 13.315 | 13.316 | 58.801 |
| linux-x64/T | blake3 | throttled | V1 | 2 | 10 | 0.048 | 0.332 | 0.146 | 16.429 | 16.430 | 117.369 |
| linux-x64/T | blake3 | throttled | V2-W2 | 2 | 10 | 0.063 | 0.340 | 0.185 | 12.632 | 12.662 | 153.602 |
| linux-x64/T | blake3 | throttled | V0 | 4 | 10 | 0.111 | 0.304 | 0.365 | 7.175 | 7.176 | 80.148 |
| linux-x64/T | blake3 | throttled | V1 | 4 | 10 | 0.091 | 0.364 | 0.250 | 8.759 | 8.761 | 166.660 |
| linux-x64/T | blake3 | throttled | V2-W2 | 4 | 10 | 0.121 | 0.386 | 0.314 | 6.588 | 6.600 | 181.875 |
| linux-x64/T | blake3 | throttled | V0 | 8 | 10 | 0.203 | 0.331 | 0.612 | 3.934 | 3.935 | 114.092 |
| linux-x64/T | blake3 | throttled | V1 | 8 | 10 | 0.165 | 0.424 | 0.391 | 4.814 | 4.815 | 188.633 |
| linux-x64/T | blake3 | throttled | V2-W2 | 8 | 10 | 0.225 | 0.426 | 0.526 | 3.548 | 3.556 | 197.553 |
| win-x64/S1 | blake3 | warm | V0 | 1 | 10 | 0.658 | 0.545 | 1.203 | 1.519 | 1.574 | 18.984 |
| win-x64/S1 | blake3 | warm | V1 | 1 | 10 | 1.218 | 1.196 | 1.015 | 0.821 | 1.036 | 34.201 |
| win-x64/S1 | blake3 | warm | V2-W2 | 1 | 10 | 2.173 | 1.067 | 2.032 | 0.460 | 0.490 | 38.619 |
| win-x64/S1 | blake3 | warm | V2-W4 | 1 | 10 | 2.983 | 0.810 | 3.685 | 0.335 | 0.377 | 53.523 |
| win-x64/S1 | blake3 | warm | V2-W8 | 1 | 10 | 2.940 | 0.795 | 3.696 | 0.340 | 0.354 | 85.779 |
| win-x64/S1 | blake3 | throttled | V0 | 1 | 10 | 0.081 | 0.520 | 0.156 | 12.319 | 12.441 | 19.350 |
| win-x64/S1 | blake3 | throttled | V1 | 1 | 10 | 0.081 | 0.871 | 0.093 | 12.349 | 12.355 | 30.549 |
| win-x64/S1 | blake3 | throttled | V2-W2 | 1 | 10 | 0.161 | 0.866 | 0.186 | 6.198 | 6.208 | 38.693 |
| win-x64/S1 | blake3 | throttled | V2-W4 | 1 | 10 | 0.321 | 0.766 | 0.419 | 3.112 | 3.128 | 54.902 |
| win-x64/S1 | blake3 | throttled | V2-W8 | 1 | 10 | 0.626 | 0.685 | 0.912 | 1.597 | 1.619 | 87.020 |
| win-x64/S1 | sha256 | warm | V0 | 1 | 10 | 0.628 | 0.508 | 1.212 | 1.593 | 1.834 | 19.043 |
| win-x64/S1 | sha256 | warm | V1 | 1 | 10 | 1.186 | 1.164 | 1.016 | 0.843 | 0.864 | 32.422 |
| win-x64/S10 | blake3 | warm | V0 | 1 | 5 | 0.133 | 0.374 | 0.371 | 75.306 | 77.234 | 15.234 |
| win-x64/S10 | blake3 | warm | V1 | 1 | 5 | 0.380 | 0.986 | 0.378 | 26.321 | 28.785 | 44.625 |
| win-x64/S10 | blake3 | warm | V2-W2 | 1 | 5 | 0.380 | 0.888 | 0.429 | 26.323 | 26.330 | 49.168 |
| win-x64/S10 | blake3 | warm | V2-W4 | 1 | 5 | 0.380 | 0.793 | 0.477 | 26.301 | 26.931 | 63.816 |
| win-x64/S10 | blake3 | warm | V2-W8 | 1 | 5 | 0.380 | 0.779 | 0.469 | 26.298 | 32.775 | 92.430 |
| win-x64/T | blake3 | warm | V0 | 1 | 10 | 0.439 | 0.293 | 1.434 | 1.817 | 2.236 | 40.258 |
| win-x64/T | blake3 | warm | V1 | 1 | 10 | 0.617 | 0.464 | 1.378 | 1.293 | 1.466 | 99.855 |
| win-x64/T | blake3 | warm | V2-W2 | 1 | 10 | 0.674 | 0.378 | 1.792 | 1.183 | 1.276 | 107.059 |
| win-x64/T | blake3 | warm | V0 | 2 | 10 | 0.707 | 0.280 | 2.492 | 1.126 | 1.311 | 36.342 |
| win-x64/T | blake3 | warm | V1 | 2 | 10 | 0.990 | 0.385 | 2.535 | 0.805 | 0.936 | 114.613 |
| win-x64/T | blake3 | warm | V2-W2 | 2 | 10 | 1.226 | 0.421 | 2.927 | 0.650 | 0.707 | 129.574 |
| win-x64/T | blake3 | warm | V0 | 4 | 10 | 1.172 | 0.358 | 3.357 | 0.680 | 0.861 | 37.277 |
| win-x64/T | blake3 | warm | V1 | 4 | 10 | 1.657 | 0.515 | 3.044 | 0.481 | 0.768 | 137.350 |
| win-x64/T | blake3 | warm | V2-W2 | 4 | 10 | 1.735 | 0.502 | 3.449 | 0.459 | 0.605 | 158.375 |
| win-x64/T | blake3 | warm | V0 | 8 | 10 | 1.106 | 0.342 | 3.197 | 0.721 | 0.889 | 49.496 |
| win-x64/T | blake3 | warm | V1 | 8 | 10 | 1.655 | 0.502 | 3.197 | 0.481 | 0.601 | 191.654 |
| win-x64/T | blake3 | warm | V2-W2 | 8 | 10 | 1.697 | 0.488 | 3.514 | 0.470 | 0.579 | 222.164 |
| win-x64/T | blake3 | throttled | V0 | 1 | 10 | 0.031 | 0.198 | 0.156 | 25.859 | 25.871 | 42.889 |
| win-x64/T | blake3 | throttled | V1 | 1 | 10 | 0.025 | 0.292 | 0.085 | 32.197 | 32.203 | 100.209 |
| win-x64/T | blake3 | throttled | V2-W2 | 1 | 10 | 0.032 | 0.296 | 0.107 | 25.094 | 25.127 | 114.236 |
| win-x64/T | blake3 | throttled | V0 | 2 | 10 | 0.060 | 0.191 | 0.313 | 13.326 | 13.330 | 65.229 |
| win-x64/T | blake3 | throttled | V1 | 2 | 10 | 0.048 | 0.285 | 0.170 | 16.438 | 16.457 | 112.602 |
| win-x64/T | blake3 | throttled | V2-W2 | 2 | 10 | 0.063 | 0.296 | 0.213 | 12.676 | 12.702 | 156.525 |
| win-x64/T | blake3 | throttled | V0 | 4 | 10 | 0.111 | 0.210 | 0.525 | 7.190 | 7.233 | 91.342 |
| win-x64/T | blake3 | throttled | V1 | 4 | 10 | 0.091 | 0.308 | 0.295 | 8.774 | 8.779 | 134.373 |
| win-x64/T | blake3 | throttled | V2-W2 | 4 | 10 | 0.120 | 0.304 | 0.396 | 6.621 | 6.644 | 177.416 |
| win-x64/T | blake3 | throttled | V0 | 8 | 10 | 0.202 | 0.210 | 0.932 | 3.952 | 7.459 | 124.098 |
| win-x64/T | blake3 | throttled | V1 | 8 | 10 | 0.165 | 0.303 | 0.545 | 4.828 | 4.936 | 182.619 |
| win-x64/T | blake3 | throttled | V2-W2 | 8 | 10 | 0.222 | 0.318 | 0.698 | 3.583 | 3.605 | 223.867 |

Runs:
- CORE-VERIFY-001/RUN-20260929-4-5ff9db9-linux-arm64 (linux-arm64), plan 46e60cf3a6de6770011a9dbe8f05e53825481ad90ed307d7c0184afc42a9a4f7, commit 5ff9db9fad78f0dd72831e23eaaf26b63c2e88b8, 220 samples
- CORE-VERIFY-001/RUN-20260929-4-5ff9db9-linux-x64 (linux-x64), plan 46e60cf3a6de6770011a9dbe8f05e53825481ad90ed307d7c0184afc42a9a4f7, commit 5ff9db9fad78f0dd72831e23eaaf26b63c2e88b8, 220 samples
- CORE-VERIFY-001/RUN-20260929-4-5ff9db9-win-x64 (win-x64), plan 8edad6abcd2eb7877d5a435c5e6eb81f9029afc6e02d9744354cb1bf84e6d259, commit 5ff9db9fad78f0dd72831e23eaaf26b63c2e88b8, 145 samples
- CORE-VERIFY-001/RUN-20260929-4-5ff9db9-linux-arm64 (linux-arm64), plan b66ba331e4cd3ec0f8d947a505cbb1a3da6e81229c9e43b90a2823b9fd6b50a2, commit 5ff9db9fad78f0dd72831e23eaaf26b63c2e88b8, 360 samples
- CORE-VERIFY-001/RUN-20260929-4-5ff9db9-linux-x64 (linux-x64), plan b66ba331e4cd3ec0f8d947a505cbb1a3da6e81229c9e43b90a2823b9fd6b50a2, commit 5ff9db9fad78f0dd72831e23eaaf26b63c2e88b8, 360 samples
- CORE-VERIFY-001/RUN-20260929-4-5ff9db9-win-x64 (win-x64), plan 38c7f4acbdfd7430f7d1b2074ee4be02c0f7ee60409382ff8c604a973a3d9fcd, commit 5ff9db9fad78f0dd72831e23eaaf26b63c2e88b8, 240 samples
