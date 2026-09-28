# PATCH-APPLY-001 — rerun after buffer reuse: create peak grows with the dictionary size

EvidenceId: `PATCH-APPLY-001/EVIDENCE-20260928-002`  
Status: `DEFER`  
Owning issue(s): #7, #168  
Implementation PR(s): #170 (buffer reuse, K = 4 default)  
Date: 2026-09-28  
Supersedes for A2: `PATCH-APPLY-001/EVIDENCE-20260928-001` (its A2 investigation)

## Hypothesis

`EVIDENCE-20260928-001` traced the Linux create peaks to managed garbage. The likely source was the per-candidate dictionary and compression buffers in `CspPatchBuilder.ChooseEntryAsync`. #170 reuses those buffers. If the hypothesis holds, the create peak on the Linux lanes falls to at most 64 MiB over idle.

## Frozen decision rule

Unchanged, from `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md` §3 (A1 and A2 as in `EVIDENCE-20260928-001`).

## Compared lanes

| Lane | Configuration |
| --- | --- |
| before | commit `853c89e`: new buffers per candidate, default K = 2 (`EVIDENCE-20260928-001`) |
| after | commit `2f15c67`: reused buffers (#170), default K = 4 (`PATCH-ENC-002`) |

The two differ in two ways, and this run cannot separate them; see the analysis.

## Included runs

| RunId | Platform/runtime | Raw evidence |
| --- | --- | --- |
| `PATCH-PREFREEZE-001/RUN-20260928-2-2f15c67-linux-x64` | GitHub `ubuntu-24.04` x64, .NET 10 | workflow run 36392403116 (`lanes=csp-lanes`); [data](data/PATCH-APPLY-001-20260928-002/) `x64--*` |
| `PATCH-PREFREEZE-001/RUN-20260928-2-2f15c67-linux-arm64` | GitHub `ubuntu-24.04-arm`, .NET 10 | workflow run 36392403116; `arm64--*` |

The `csp-lanes` job names its RunIds after `PATCH-PREFREEZE-001`. It measures the lanes and the memory records that this experiment uses. There is no Windows lane in this run.

## Reproduction

```text
gh workflow run patch-lab.yml --ref main -f lanes=csp-lanes
```

The dataset holds the minified, gzipped per-file JSON of both lanes, `verdict.json` and `summary.md` from the workflow's summary job, and `SHA256SUMS` of the original files.

## Semantic / compatibility checks

- [x] every apply reproduced the target's SHA-256;
- [x] per-file patch sizes of `csp` at `2f15c67` equal those of `sweep-L19-K4-C8` at `853c89e` for all 1,893 files, on both architectures;
- [x] `csp-zstd` and `csp-raw` sizes are unchanged from `853c89e` for every file;
- [x] no format, identity or API change.

## Results

### A2: peak working set over idle (create)

| platform | before (K = 2) | after (reuse, K = 4) | files over 64 MiB, before → after |
| --- | ---: | ---: | ---: |
| linux-x64 | +151.2 MiB | +219.6 MiB | 23 → 47 of 75 |
| linux-arm64 | +185.2 MiB | +251.3 MiB | 25 → 52 of 75 |

Apply stays within the allowance on both lanes (worst +38.0 and +40.6 MiB), as before. The worst create is `bin/node` of `node-linux-x64` 24.20.0 → 24.21.0. On both lanes the smallest create excess is now +48.5 and +53.9 MiB.

Per file, the create peak rose on 70 of 75 files (x64) and 71 of 75 (ARM64).

- Files other than Node.js: median +13.6 / +14.5 MiB, 90th percentile +27.8 / +29.9 MiB (x64 / ARM64).
- The four Node.js binaries: +62 to +79 MiB on x64 and +28 to +115 MiB on ARM64.

### A1: re-chunk check (at K = 4)

| platform | apply, check on | apply, check off | overhead |
| --- | ---: | ---: | ---: |
| linux-x64 | 6.86 s | 6.48 s | 5.93 % |
| linux-arm64 | 8.04 s | 6.52 s | 23.41 % |

Both lanes are below 25 % in this run. In `EVIDENCE-20260928-001` they were 34.91 % and 27.06 %. On x64 the apply time without the check rose from 3.81 s to 6.48 s between runs on the same runner image, with an unchanged apply path. The overhead ratio therefore moves with runner noise by more than the 25 % bound allows to settle.

### PATCH-PREFREEZE-001 at K = 4 (informative)

`csp` is 36.45 MiB, 7.68 % of `csp-raw`, against 8.12 % at K = 2. End-to-end at 1 Gbit/s is 7.17 s vs 9.45 s (x64) and 8.35 s vs 10.19 s (ARM64) for `csp` vs `csp-raw`, so R1 and R2 still hold.

## Analysis

The hypothesis is refuted: reusing the managed buffers did not lower the Linux create peak. The peak rose with the change from K = 2 to K = 4.

- **The dictionary size drives it.** The zstd context workspace at level 19 grows with the dictionary. ZstdSharp 0.8.8's own `ZSTD_getCParams` and `ZSTD_estimateCCtxSize_usingCParams` give, for a 64–256 KiB chunk:

  | dictionary | windowLog / chainLog / hashLog | context |
  | ---: | --- | ---: |
  | none | 16–18 / 17–19 / 17–19 | 1.7–5.3 MiB |
  | 128 KiB | 18–19 / 19–20 / 19–20 | 5.3–9.3 MiB |
  | 256 KiB | 19 / 20 / 20 | 9.3 MiB |
  | 512 KiB | 20 / 21 / 21 | 17.3 MiB |
  | 1 MiB | 21 / 22 / 22 | 33.3 MiB |

  With the 64 KiB average chunk, K = 2 dictionaries are about 128 KiB and K = 4 about 256 KiB, up to the 1 MiB bound.
- **Native memory.** ZstdSharp allocates this workspace with `NativeMemory.Alloc` (C `malloc` on Linux), not on the GC heap. That fits the observations:
  - a GC heap limit did not stop create (`EVIDENCE-20260928-001`);
  - apply, which only decodes, is unaffected.
- **Linux only.** The same code stays within the bound on Windows. Linux glibc `malloc` keeps freed large blocks in its arenas, and raises its mmap threshold up to 32 MiB after a free. So a workspace that is freed and reallocated at varying sizes, possibly from different thread-pool threads (arenas), can stay resident. This part is a hypothesis and is not measured.
- The Windows peaks fell by 1.6–16.4 MiB with #170 at K = 2 (#170 validation), so the managed garbage was real, but it is not what exceeds the bound on Linux.

## Exclusions / invalid runs

None.

## Limitations

- The run changes two factors (buffer reuse, K) at once. The Windows comparison at K = 2 isolates buffer reuse; nothing yet isolates K on Linux.
- The native-allocation explanation rests on the zstd estimates and the platform difference, not on a Linux allocation profile.

## Decision

```text
DEFER
```

A2: create exceeds 64 MiB over idle on both Linux lanes, so D17 stays provisional. A1: below 25 % on both Linux lanes in this run, but the two runs disagree by more than the bound. #168 stays with the owner, with both runs as evidence.

## Consequences

Next measurement (not yet claimed): run the Linux memory lane with the allocator and the zstd context held fixed, to separate the three candidate causes:

- `MALLOC_ARENA_MAX=2` and `MALLOC_TRIM_THRESHOLD_` for glibc retention;
- the policy at K = 2 vs K = 4 for the zstd workspace;
- `DOTNET_GCHeapHardLimit` for the managed share.

If the zstd workspace is the cause, the fix options are:

- bound the context parameters for dictionary entries;
- keep one context per dictionary size;
- accept a documented create-memory bound that depends on K. That is an encoder policy change (D14) and needs its own evidence.

## References

- #7, #168, #170; `PATCH-APPLY-001/EVIDENCE-20260928-001`; `PATCH-ENC-002/EVIDENCE-20260928-001`; `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md`.
