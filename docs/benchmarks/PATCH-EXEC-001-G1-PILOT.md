# PATCH-EXEC-001 / G1: hosted multi-file scheduling pilot

Experiment: [#225](https://github.com/definitely-stable/ChunkShift/issues/225), research gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251). Depends on merged [#351](https://github.com/definitely-stable/ChunkShift/pull/351).
Status: **exploratory synthetic calibration only**. Never an adoption/production benchmark, a PATCH-ENC-004 decision rerun, or a real PATCH-TREE-001 lane.

## Question and competing allocations

Existing PatchLab already parallelizes independent files. The new opt-in --batch-create exposes both levels of concurrency and aggregate metrics. Does a fixed CPU budget work better with parallel files than with multiple workers per CSP?

| Case | Outer files | Inner CSP encode | Intended isolation |
| --- | ---: | --- | --- |
| f1-h0 | 1 | sequential H0 | reference workload baseline |
| f1-h2w2 | 1 | H2-W2 | current per-create adopted execution |
| f2-h0 | 2 | sequential H0 | inter-file parallelism |
| f2-h2w2 | 2 | H2-W2 | nested 2×2 workers |
| f4-h0 | 4 | sequential H0 | higher inter-file parallelism |
| f4-h2w2 | 4 | H2-W2 | nested 4×2 workers (oversubscription test) |

These are research allocations, *not* changes to public ChunkPatch.CreateAsync. Never assume 4×2 means eight usable cores. Scheduler and I/O overhead, quota and page cache may dominate.

## Frozen parameters for G1 calibration source

The script creates 12 changed independent regular files, 393216 bytes each, in a standard patch-lab pairs.json. SHAKE-256 derives unique deterministic source and replacement bytes from versioned exact ASCII seeds. Three strata rotate by file index: 128-byte periodic, random-like, and 4096-byte periodic base. In every target the central half is replaced while first/last quarters are retained. No source download, sealed holdout, change of corpus role or parameter tuning.

One apply-verified H0/F1 execution creates and checks all target SHA-256 files before any batch measurements and pre-populates a shared CSM cache. Each batch is a fresh dotnet process and creates **real** CSP with policy csp/default; it reads and hashes each patch. Two rounds traverse the 6 allocations in forward then reverse order. Each round is validated by the independent exact-patch SHA comparison against the H0 oracle (including all file rows, corpus digest, same host/runtime/commit). Any failure suppresses the success summary. No performance assertions or 25% shortcut.

Host environments: GitHub-hosted Linux and Windows x64, distinct jobs; compare only within one host/job. Inputs + full raw per-case JSON, apply oracle and summary are archived with short retention if and only if successful. CPU model, SDK/OS/runtime and checkout SHA are recorded in raw results. Medians and extrema across **two rounds** describe exploratory behavior only; two rounds cannot establish stable noise, isolated memory budgets, a reliable speedup, or a representative large update-set.

Measurements include complete batch end-to-end SHA-256 validation, CSM cache lookup, planning, CSP encoding, readback and patch SHA-256. Process CPU and allocations are batch-wide; reported process peak is lifetime high-water, **not an independent isolated RSS bound**. CPU quota/affinity, disk cache, file-system variance, rebuildability and physical read amplification remain uncontrolled in G1.

## Acceptance / next gate

G1 can be accepted as **CALIBRATION_MECHANICS_PASS** only when both GitHub-hosted jobs compile, generate deterministic inputs, produce all 12 matching reconstructed H0 targets and all 12 CSP hashes match for each of 12 batch cases (6 allocations × 2 rounds), and archive provenance. No speedup is assumed.

Only after G1 can a distinct **G2 unsealed real update-set** protocol pre-register a real corpus subset, warm/cold CSM and base storage modalities, independent fresh-process OS peak memory, CPU quotas/affinity, raw per-file read amplification, minimum 5 round alternating designs, and >=25% wall-time reduction versus *best safe fixed baseline* under #225. Production adoption needs #251/research freeze and resource boundaries. Do not add cache, a public executor or a new scheduler in G1.
