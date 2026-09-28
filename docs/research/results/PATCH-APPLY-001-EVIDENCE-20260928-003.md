# PATCH-APPLY-001 — Linux create peak: allocator retention and the dictionary-sized zstd state

EvidenceId: `PATCH-APPLY-001/EVIDENCE-20260928-003`  
Status: `DEFER`  
Owning issue(s): #7  
Implementation PR(s): #172 (lab lane and allocator selection, frozen diagnosis protocol)  
Date: 2026-09-28

## Hypothesis

`EVIDENCE-20260928-002` left three candidate causes for the Linux create peak over the 64 MiB allowance, none measured on its own:

- (a) glibc keeps freed native blocks resident;
- (b) ZstdSharp's level-19 state grows with the dictionary (up to 1 MiB at K = 4);
- (c) managed heap.

## Frozen decision rule

From [PATCH-APPLY-001-A2-DIAGNOSIS.md](../../benchmarks/PATCH-APPLY-001-A2-DIAGNOSIS.md), merged in #172 before these runs. On the worst file of each Linux lane, in order:

1. `E(D2) ≤ 64 MiB` → (a) suffices;
2. else `E(D4) ≤ 64 MiB` → (b) at K = 4;
3. else `E(D6) > 64 MiB` → pipeline without zstd;
4. else → (b) at every K ≥ 2.

(c) is excluded when `E(D2) − E(D7) ≤ 8 MiB` and D7 completes. The A2 rule of `PATCH-PREFREEZE-PROTOCOL.md` §3 is unchanged and still decides PATCH-APPLY-001.

## Compared lanes

`E` = create peak working set − the same run's idle baseline, per file, 75 files of at least 1 MiB.

| id | lane | environment |
| --- | --- | --- |
| D0 | `csp` (L19, K = 4, C8) | — |
| D1 | `csp` | `MALLOC_ARENA_MAX=2` |
| D2 | `csp` | `MALLOC_TRIM_THRESHOLD_=131072` (dynamic mmap/trim thresholds off) |
| D3 | `sweep-L19-K2-C8` | — |
| D4 | `sweep-L19-K2-C8` | `MALLOC_TRIM_THRESHOLD_=131072` |
| D5 | `csp-zstd` (K = 0) | `MALLOC_TRIM_THRESHOLD_=131072` |
| D6 | `csp-raw` | `MALLOC_TRIM_THRESHOLD_=131072` |
| D7 | `csp` | `MALLOC_TRIM_THRESHOLD_=131072`, `DOTNET_GCHeapHardLimit=0x3000000` |

## Included runs

All at commit `0e5f93b` (main after #172), .NET 10.0.12, GitHub `ubuntu-24.04` (x64) and `ubuntu-24.04-arm`, whole frozen corpus (`pairsSha256 8b3b92a9…22dd`).

| config | workflow run | RunIds |
| --- | --- | --- |
| D0 | 36406579180 | `PATCH-APPLY-001/RUN-20260928-3-0e5f93b-linux-{x64,arm64}` |
| D1 | 36406587343 | `…/RUN-20260928-4-0e5f93b-linux-{x64,arm64}` |
| D2 | 36406590363 | `…/RUN-20260928-5-0e5f93b-linux-{x64,arm64}` |
| D3 | 36406592723 | `…/RUN-20260928-6-0e5f93b-linux-{x64,arm64}` |
| D4 | 36406595167 | `…/RUN-20260928-7-0e5f93b-linux-{x64,arm64}` |
| D5 | 36406597730 | `…/RUN-20260928-8-0e5f93b-linux-{x64,arm64}` |
| D6 | 36406600683 | `…/RUN-20260928-9-0e5f93b-linux-{x64,arm64}` |
| D7 | 36406603527 | `…/RUN-20260928-10-0e5f93b-linux-{x64,arm64}` |

Raw evidence: [data/PATCH-APPLY-001-20260928-003/](data/PATCH-APPLY-001-20260928-003/). `memory-diagnosis.json` holds, per run, the per-file create and apply excess as the job printed it (0.1 MiB), the idle baseline, the environment, and the artifact id and SHA-256 of the full `chunkshift.patch-lab-memory.v1` JSON; `attribute.py` recomputes every table below.

## Reproduction

```text
gh workflow run patch-lab.yml --ref main -f lanes=memory -f memory-lane=<lane> -f memory-env="<NAME=VALUE ...>"
python3 docs/research/results/data/PATCH-APPLY-001-20260928-003/attribute.py
```

## Semantic / compatibility checks

- [x] every create and apply child exited successfully; apply verifies the target's SHA-256;
- [x] no format, identity, API or encoder change (lab and workflow only);
- [x] x64 and ARM64 measured.

## Results

### Create excess over idle

| config | x64 worst | x64 median | x64 files > 64 | ARM64 worst | ARM64 median | ARM64 files > 64 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| D0 default | 239.5 | 73.7 | 62 | 248.6 | 69.6 | 51 |
| D1 arenas 2 | 144.2 | 57.8 | 29 | 159.0 | 58.3 | 27 |
| D2 no retention | 103.8 | 43.1 | 4 | 99.4 | 41.2 | 4 |
| D3 K = 2 | 146.8 | 50.8 | 19 | 153.4 | 53.0 | 19 |
| D4 K = 2, no retention | 69.7 | 40.4 | 3 | 66.2 | 39.1 | 2 |
| D5 K = 0, no retention | 39.1 | 26.2 | 0 | 38.4 | 25.8 | 0 |
| D6 raw, no retention | 26.9 | 19.1 | 0 | 24.4 | 18.0 | 0 |
| D7 D2 + 48 MiB GC heap | 101.2 | 41.3 | 4 | 99.7 | 41.6 | 4 |

MiB. D0 reproduces `EVIDENCE-20260928-002` (+219.6 / +251.3 MiB) within the run-to-run spread of the retained share. In every configuration the worst file is a Node.js binary (`bin/node` or `node.exe`); every file over 64 MiB in D2 and D4 is one of the four Node.js files. Apply stays within the allowance in every run (worst +40.6 MiB; +54.3 MiB for the K = 0 patches on ARM64 in D5).

### Attribution per file

| share | x64 median | x64 max | ARM64 median | ARM64 max |
| --- | ---: | ---: | ---: | ---: |
| (a) retention, D0 − D2 | 29.1 | 151.2 | 26.0 | 151.1 |
| (b) dictionary at K = 4, D2 − D5 | 17.9 | 64.7 | 16.7 | 61.4 |
| (b) dictionary at K = 2, D4 − D5 | 13.5 | 30.6 | 12.7 | 30.1 |
| zstd without dictionary, D5 − D6 | 7.1 | 14.2 | 8.0 | 15.5 |
| (c) managed, D2 − D7 | 0.9 | 4.8 | −0.2 | 3.2 |
| floor without zstd, D6 | 19.1 | 26.9 | 18.0 | 24.4 |

`MALLOC_ARENA_MAX=2` (D1) removes about half of the retained share (worst file 144–159 MiB), the fixed thresholds (D2) all of it.

### Rule evaluation

| rule | x64 | ARM64 | holds |
| --- | ---: | ---: | --- |
| 1: worst `E(D2)` ≤ 64 | 103.8 | 99.4 | no |
| 2: worst `E(D4)` ≤ 64 | 69.7 | 66.2 | no |
| 3: worst `E(D6)` > 64 | 26.9 | 24.4 | no |
| 4 | | | **yes** |
| (c) excluded: worst-file `E(D2) − E(D7)` ≤ 8, D7 complete | 3.7 | 0.1 | yes |

## Analysis

- **Rule 4 holds: the dictionary-sized zstd state exceeds the bound at K = 4 and, narrowly, at K = 2.** Without retention, create of the Node.js binaries still peaks at +66 to +104 MiB at K = 4 and at up to +70 MiB at K = 2; the pipeline without zstd needs at most +27 MiB and zstd without a dictionary at most +39 MiB.
- **The size of that state fits the zstd context estimates.** The largest dictionary share is 61–65 MiB at K = 4 and 30–31 MiB at K = 2. `EVIDENCE-20260928-002` gives the level-19 context as 33.3 MiB for a 1 MiB dictionary and 17.3 MiB for 512 KiB (the K = 2 maximum with 256 KiB chunks). Twice those, less the 1.7–5.3 MiB context without a dictionary that D5 already contains, gives 61–65 and 29–33 MiB. Two dictionary-sized table sets are live at once. That is consistent with how `LoadDictionary` works: it builds a CDict per candidate with the dictionary's parameters, and at level 19 the CDict tables are copied into a context sized the same way. This mechanism is inferred from the estimates and not profiled.
- **Allocator retention is the larger share for most files.** It takes the count of files over 64 MiB from 62/51 to 4/4 and adds up to 151 MiB on the Node.js files. The per-candidate CDict is allocated and freed at varying sizes up to about 33 MiB. glibc raises its dynamic mmap threshold after such frees and keeps later blocks in the arenas, which is what the fixed thresholds of D2 remove. A library cannot set `MALLOC_*` for its caller, so the fix has to remove the allocation churn itself.
- **(c) is excluded.** The 48 MiB GC heap limit changes the peak by at most 4.8 MiB and no create fails under it.
- **Windows** stayed within the bound at K = 2 (`EVIDENCE-20260928-001`); these Linux runs do not re-measure it at K = 4.

## Exclusions / invalid runs

- The local Linux x64 series of the same configurations (protocol §2, reported separately) stopped after its first file when the container restarted, and was not repeated. The CI runs cover a superset of its files on both architectures. Its exploratory runs before the freeze are listed in the protocol and are not decision data.

## Limitations

- Values are the job-printed excess at 0.1 MiB, not the raw byte peaks. The raw JSON is in the listed artifacts, which expire after 90 days; the printed tables are committed.
- The CDict-plus-context explanation of the dictionary share rests on the zstd estimates and on the K = 2 / K = 4 scaling, not on an allocation profile.
- One run per configuration and platform; the retained share varies between runs (worst file of D0 vs `EVIDENCE-20260928-002`: +19.9 MiB on x64, −2.7 MiB on ARM64).

## Decision

```text
DEFER
```

The diagnosis resolves to rule 4 of the frozen protocol. The dictionary-sized zstd state exceeds 64 MiB at every K ≥ 2 on the Node.js binaries, and glibc retention of the per-candidate allocations adds the larger part of the excess on most other files. The managed heap is not a cause. A2 of PATCH-APPLY-001 stays DEFER, and D17 stays provisional.

## Consequences

By rule 4, the fix changes how, or with which parameters, the encoder uses zstd. That is encoder policy (D14): a new experiment with its own ExperimentId, frozen before its runs, comparing patch bytes, create time and the create peak on this memory lane. Alternatively, the owner accepts a documented create-memory bound that depends on K. Candidates, each of which changes patch bytes:

- a raw-content prefix (`ZSTD_CCtx_refPrefix`) instead of a CDict per candidate. It keeps one dictionary-sized table set in a reused context and removes the per-candidate native allocation. It needs ZstdSharp's low-level API and therefore unsafe code in Patching (D8);
- bounded compression parameters for dictionary entries (window, hash and chain logs);
- a smaller dictionary bound or K = 2. This gives back part of the 6.5 % that `PATCH-ENC-002` measured, and alone it still leaves the worst K = 2 file at +66 to +70 MiB.

## References

- #7, #172; `docs/benchmarks/PATCH-APPLY-001-A2-DIAGNOSIS.md`; `PATCH-APPLY-001/EVIDENCE-20260928-001`, `/EVIDENCE-20260928-002`; `PATCH-ENC-002/EVIDENCE-20260928-001`; PATCHING-DECISIONS D8, D14, D15, D17.
