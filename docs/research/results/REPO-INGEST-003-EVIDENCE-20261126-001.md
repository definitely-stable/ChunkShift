# REPO-INGEST-003 — reviewed sealed real-artifact holdout

EvidenceId: REPO-INGEST-003/EVIDENCE-20261126-001.
Refs #133 #144 #139 #10. Decision: **PERFORMANCE_ELIGIBLE** within frozen scope;
production default **DEFER** pending R1-C and remaining production gates.
Measured head 6c86fda1e842d2b350c0358302e409c78ec0463a, merge 9b7a502013f2a832d370e0a335ab1a321e674182.
[Run 45791900188 attempt 1](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188).
Six measure jobs and aggregate audit PASS; CI 45791900165, Heavy 45791900208,
CodeQL 45791899797 and applicable gates PASS. No retries or exclusions other
than fixed warmups. 672 raw/576 measured/96 warmup, 288 paired comparisons.
SDK 10.0.404, .NET 10.0.15, JIT, 4 logical processors per lane.
Lock SHA256 e1bf44f06a90d7c20dfa149b78601a7c300a2f836547130bdb03746b95fea9f9;
geometry a34a647908b76574d1670ea65c2410fa819fb752ba52a816ce74f963772d8f31;
qualification bf34253c751469e3d6f3084e8e9ce23a0d255d3c109618ed96bf66a7fcba4bff.
Exact command, source selection, schedule and rule: three committed locks,
holdout_run.py and holdout_evaluate.py at measured head. RunId prefix
REPO-INGEST-003/RUN-45791900188-1-6c86fda1e842-, suffix <lane>-<replica>.

## Original artifacts

All expire 2027-02-24T09:25:56Z. Outer ZIP SHA256 independently verified.
| Lane | Artifact | ZIP SHA256 | Image version | Start UTC |
|---|---|---|---|---|
| windows-2025-1 | [15103436720](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188/artifacts/15103436720) | 10dc771b16f11733e64f2eb39d561cf20566dec37b0bd5c9c57ce3a32d83ca03 | 20261116.315.1 | 09:26:24.578877 |
| windows-2025-2 | [15102649374](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188/artifacts/15102649374) | 612ea8037c14f353ef31fc0c090cbf83ac26983fc352cc8e0f642ba332d48a38 | 20261116.315.1 | 09:26:16.267498 |
| ubuntu-24.04-1 | [15103731399](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188/artifacts/15103731399) | f370d13118822a37dbd2c6ad6ecce1f669ccda0ccf820a9dc2b25b9aa3058516 | 20261116.133.1 | 09:26:19.151590 |
| ubuntu-24.04-2 | [15103556317](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188/artifacts/15103556317) | e93669f2930e880715c5ddcf326f3c9474adf2c8c325a441eb9d5347cea5b3f8 | 20261116.133.1 | 09:26:16.287261 |
| ubuntu-24.04-arm-1 | [15103855937](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188/artifacts/15103855937) | 6de3f6277334feb9bf86902badc397108267b82fd8e4aca13d9d03a0e8021152 | 20261116.188.1 | 09:26:12.346645 |
| ubuntu-24.04-arm-2 | [15103297359](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188/artifacts/15103297359) | a17acfd5192a72d8450d06dab629dc2e9f80d70072bd018cbd3390cc7c93ddb9 | 20261116.188.1 | 09:26:09.660421 |
| aggregate | [15102917487](https://github.com/definitely-stable/ChunkShift/actions/runs/45791900188/artifacts/15102917487) | 045ebfe2ebf55c23dc91ce961e6d9b5c93f815ff5e4c867606074ba3929c7883 | — | — |

Independent review: ZIP CRC, exact 230 files/lane (229 internal checksums,
1374 total), all sample identities, stdout/JSONL equality, source/restore
digests, final completion seals, arithmetic, 8 layout fingerprints, stage/range
and duplicate accounting, all metric medians and pair ratios and exact primary
products against aggregate. Replay manifests identical; four asset SHA512,
lengths and canonical member fingerprints match source lock. This audit did
not redownload actual source binaries locally; six hosted replays did.
All 4620 active/66780 closed comparisons completed (warmups included).
All 288 measured pairs favor C. No changed rule, no pooled runner pairs.

## Primary criteria

Geometric mean of paired medians across three real corpora, threshold 1.10.
| Lane | SHA256 | BLAKE3 | Result |
|---|---:|---:|---|
| Windows 1 | 1.616726 | 1.611338 | PASS |
| Windows 2 | 1.624619 | 1.604848 | PASS |
| Linux x64 1 | 1.218845 | 1.317806 | PASS |
| Linux x64 2 | 1.217214 | 1.342270 | PASS |
| Linux ARM64 1 | 1.387388 | 1.428828 | PASS |
| Linux ARM64 2 | 1.367250 | 1.411204 | PASS |

All 288 per-cell guardrail tests pass. C/D paired median ranges:
Ready 0.583772–0.880630, CPU 0.534413–0.712493, allocated 0.081773–0.119097,
peak 0.620559–0.781453, restore 0.892512–1.071402, rebuild
0.859968–1.035338, cleanup 0.080443–0.434764 (report only).
Worst restore ARM64 replica1 compressed BLAKE3; worst rebuild Linux x64
replica2 win-versioned SHA256. No threshold tuned. D/C per-cell speedup
1.135553–1.713024. Linux SHA256 adverse slowest, but remains clearly positive.

## Per-cell reviewed results (rounded, never used for decisions)

D/C Ready median, six within-round D/C ratios, and D/C absolute median Ready
ms. W=win-versioned, L=linux-versioned, Z=compressed, A=adverse-repeat.
S=sha256, B=blake3. Full exact ratios and all other metrics in original
archive/summary; compact record retains every Ready pair and decision ranges.
| Lane | Cell | D/C median | Six paired ratios | Ready D/C ms |
|---|---|---:|---|---|
| Win1 | W S | 1.627127 | 1.524783 1.846909 1.608367 1.624983 1.629271 1.632114 | 2511.563/1545.847 |
| Win1 | W B | 1.692405 | 1.729535 1.695632 1.608646 1.689178 1.704144 1.578626 | 2849.139/1676.081 |
| Win1 | L S | 1.606340 | 1.636172 1.620384 1.560021 1.702852 1.592296 1.379914 | 2516.594/1578.565 |
| Win1 | L B | 1.592768 | 1.594930 1.607156 1.590606 1.612913 1.562514 1.461780 | 2853.624/1782.371 |
| Win1 | Z S | 1.616777 | 1.579250 1.596614 1.624353 1.652419 1.628548 1.609201 | 1241.631/767.493 |
| Win1 | Z B | 1.552042 | 1.555853 1.577609 1.536853 1.548231 1.565849 1.504351 | 1546.139/993.066 |
| Win1 | A S | 1.622425 | 1.643016 1.631819 1.612094 1.613030 1.659759 1.606574 | 1355.612/837.244 |
| Win1 | A B | 1.608385 | 1.486326 1.602233 1.614536 1.569222 1.743639 1.699236 | 1726.054/1094.373 |
| Win2 | W S | 1.616497 | 1.815276 1.414788 1.617848 1.615147 1.608280 1.647566 | 2537.883/1568.939 |
| Win2 | W B | 1.713024 | 1.563097 1.706370 1.719679 1.745258 1.706166 1.833396 | 2883.315/1678.218 |
| Win2 | L S | 1.637308 | 1.633434 1.657847 1.617171 1.668350 1.633585 1.641030 | 2537.155/1545.690 |
| Win2 | L B | 1.573639 | 1.550204 1.771397 1.289190 1.614243 1.507641 1.597074 | 2892.641/1828.769 |
| Win2 | Z S | 1.620128 | 1.629226 1.633447 1.627793 1.601809 1.578366 1.612463 | 1244.951/767.856 |
| Win2 | Z B | 1.533322 | 1.514115 1.523868 1.493607 1.550761 1.542776 1.830097 | 1543.912/1009.558 |
| Win2 | A S | 1.618793 | 1.648687 1.621664 1.611096 1.592102 1.616074 1.621512 | 1343.742/830.532 |
| Win2 | A B | 1.590827 | 1.585200 1.658254 1.596453 1.572431 1.584423 1.745218 | 1672.456/1040.575 |
| Linux1 | W S | 1.213997 | 1.207288 1.209734 1.212143 1.220084 1.216236 1.215851 | 1565.289/1290.410 |
| Linux1 | W B | 1.243039 | 1.259677 1.238467 1.104723 1.247611 1.102169 1.251478 | 1745.391/1404.157 |
| Linux1 | L S | 1.229652 | 1.225811 1.231819 1.200178 1.247427 1.227485 1.232098 | 1572.478/1277.844 |
| Linux1 | L B | 1.290052 | 1.299736 1.313458 1.280369 1.265223 1.371070 1.264586 | 1723.947/1342.390 |
| Linux1 | Z S | 1.212956 | 1.206870 1.213740 1.220918 1.212173 1.217256 1.205245 | 771.513/635.917 |
| Linux1 | Z B | 1.427127 | 1.610352 1.622272 1.254456 1.288771 1.493598 1.360656 | 1070.217/741.971 |
| Linux1 | A S | 1.135553 | 1.138225 1.128596 1.130592 1.137066 1.145707 1.134040 | 743.202/655.104 |
| Linux1 | A B | 1.639496 | 1.738744 1.657612 1.609276 1.588533 1.678482 1.621381 | 1228.999/747.256 |
| Linux2 | W S | 1.218369 | 1.212538 1.222196 1.211500 1.225688 1.214542 1.228072 | 1575.283/1291.756 |
| Linux2 | W B | 1.242921 | 1.235319 1.257812 1.230777 1.218688 1.250522 1.255198 | 1725.849/1388.791 |
| Linux2 | L S | 1.220818 | 1.219993 1.233727 1.221642 1.202966 1.237470 1.216452 | 1586.618/1301.522 |
| Linux2 | L B | 1.294114 | 1.276387 1.345359 1.298396 1.268305 1.289832 1.324331 | 1773.526/1369.151 |
| Linux2 | Z S | 1.212471 | 1.216357 1.204113 1.227487 1.217650 1.208585 1.206590 | 784.385/647.737 |
| Linux2 | Z B | 1.503501 | 1.524788 1.607476 1.065801 1.429245 1.482214 1.607027 | 1123.425/739.508 |
| Linux2 | A S | 1.136721 | 1.137670 1.131602 1.137375 1.127143 1.153991 1.136067 | 756.815/666.324 |
| Linux2 | A B | 1.662889 | 1.702166 1.708186 1.601834 1.292024 1.685032 1.640746 | 1311.554/770.957 |
| ARM1 | W S | 1.390899 | 1.399647 1.382225 1.384789 1.392965 1.388833 1.407085 | 2129.310/1529.961 |
| ARM1 | W B | 1.388043 | 1.372740 1.393707 1.388519 1.395751 1.387566 1.373615 | 2532.349/1827.906 |
| ARM1 | L S | 1.377158 | 1.374720 1.376292 1.383496 1.378023 1.375820 1.392145 | 2116.036/1535.713 |
| ARM1 | L B | 1.400869 | 1.399829 1.393697 1.401908 1.414115 1.405052 1.375940 | 2526.540/1800.425 |
| ARM1 | Z S | 1.394168 | 1.406592 1.396657 1.365941 1.391678 1.390428 1.408206 | 1076.407/773.437 |
| ARM1 | Z B | 1.500168 | 1.509714 1.501988 1.559190 1.498348 1.485145 1.498289 | 1497.674/998.359 |
| ARM1 | A S | 1.419302 | 1.428077 1.397526 1.453111 1.371008 1.410527 1.463640 | 1169.262/826.984 |
| ARM1 | A B | 1.434452 | 1.426629 1.438474 1.416513 1.447197 1.430430 1.547623 | 1515.907/1055.471 |
| ARM2 | W S | 1.369096 | 1.361842 1.391984 1.385691 1.376349 1.350918 1.352150 | 2086.207/1523.785 |
| ARM2 | W B | 1.378767 | 1.376559 1.332988 1.380975 1.416154 1.397237 1.366855 | 2467.697/1780.017 |
| ARM2 | L S | 1.361790 | 1.353964 1.373773 1.358644 1.369500 1.364937 1.333418 | 1989.732/1461.057 |
| ARM2 | L B | 1.365042 | 1.416252 1.351643 1.360711 1.369374 1.355816 1.421502 | 2500.643/1807.195 |
| ARM2 | Z S | 1.370880 | 1.386344 1.404808 1.404855 1.355416 1.353572 1.349543 | 1049.453/760.490 |
| ARM2 | Z B | 1.493249 | 1.484582 1.484063 1.489580 1.526112 1.496917 1.525898 | 1467.390/978.222 |
| ARM2 | A S | 1.383082 | 1.435729 1.373704 1.326082 1.423053 1.392460 1.351421 | 1097.014/788.487 |
| ARM2 | A B | 1.388730 | 1.303209 1.401442 1.405864 1.382364 1.387370 1.390090 | 1428.253/1028.395 |

## Decision and limitations

All frozen requirements pass on both replicas per platform. Adopt no new
production default yet. No evidence requiring closed handle cache, parallel
workers or hash suite change. Narrow .NET versioned/ZIP corpus, one constructed
adverse workload, source <=256 MiB, uncontrolled OS cache, hosted local
filesystem, serial JIT ingestion. No device IO, physical durability, multi-file
semantics, power-loss or production publication claim. No significance/p95
claims. Proceed to [R1-C process-crash baseline](../../architecture/REPOSITORY-R1C-PROCESS-CRASH-20261126.md).
