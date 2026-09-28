# PATCH-APPLY-001 A2 diagnosis: which memory makes the Linux create peak

Status: **frozen before the decision runs.** Merged before the runs this note defines; later edits may only add result links. The A2 rule itself stays as frozen in [PATCH-PREFREEZE-PROTOCOL.md](PATCH-PREFREEZE-PROTOCOL.md) §3; this note only attributes the excess.  
Issue: [#7](https://github.com/definitely-stable/ChunkShift/issues/7) · Register: [PATCHING-DECISIONS.md](../architecture/PATCHING-DECISIONS.md) D17 · Prior records: [EVIDENCE-20260928-001](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-001.md), [EVIDENCE-20260928-002](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-002.md)  
Result record: `PATCH-APPLY-001/EVIDENCE-20260928-003`

## 1. Question

At `2f15c67` create exceeds 64 MiB over idle on both Linux lanes (worst +219.6 MiB x64, +251.3 MiB ARM64), while apply and Windows stay within it. `EVIDENCE-20260928-002` names three candidate causes and measures none of them separately:

- **(a) glibc retention:** freed native blocks stay resident in the `malloc` arenas;
- **(b) zstd workspace:** ZstdSharp's level-19 context and dictionary tables, allocated with `NativeMemory.Alloc`, grow with the dictionary (K = 4 dictionaries reach 1 MiB);
- **(c) managed heap:** live or late-collected GC memory.

Exploratory runs before this note (local Linux x64 container, `source.tar` and `bin/node` of `node-linux-x64` 24.20.0 → 24.21.0, not decision data) showed the `bin/node` create peak at 289.8 MiB by default, 150.0 MiB with `MALLOC_ARENA_MAX=1` and 134.8 MiB with `MALLOC_TRIM_THRESHOLD_=131072`, with identical patch bytes. The configurations below were chosen from them.

## 2. Configurations

Each configuration is one `patch-lab memory` run: every changed file of at least 1 MiB, create and apply each in a child process, and the idle baseline measured under the same environment (children inherit it). The run records its lane, policy and every `MALLOC_*` and `DOTNET_GC*` variable.

| id | `--lane` | environment | isolates |
|---|---|---|---|
| D0 | `csp` (L19, K = 4) | — | reproduction of `EVIDENCE-20260928-002` |
| D1 | `csp` | `MALLOC_ARENA_MAX=2` | per-thread arenas |
| D2 | `csp` | `MALLOC_TRIM_THRESHOLD_=131072` | glibc retention off |
| D3 | `sweep-L19-K2-C8` | — | K = 2 with retention |
| D4 | `sweep-L19-K2-C8` | `MALLOC_TRIM_THRESHOLD_=131072` | K = 2 without retention |
| D5 | `csp-zstd` (K = 0) | `MALLOC_TRIM_THRESHOLD_=131072` | zstd without a dictionary |
| D6 | `csp-raw` | `MALLOC_TRIM_THRESHOLD_=131072` | pipeline without zstd |
| D7 | `csp` | `MALLOC_TRIM_THRESHOLD_=131072`, `DOTNET_GCHeapHardLimit=0x3000000` | managed heap capped at 48 MiB |

Setting `MALLOC_TRIM_THRESHOLD_` turns off glibc's dynamic mmap and trim thresholds (`mallopt(3)`): a block of 128 KiB or more is then mapped on allocation and unmapped on free, and the heap top is trimmed above 128 KiB. Its value equals glibc's initial default, so only the dynamic adjustment changes. D1 is the variable #171 named; D2 is the one that removes retention.

Platforms: GitHub `ubuntu-24.04` (x64) and `ubuntu-24.04-arm` (ARM64) over the whole frozen corpus (`patch-lab.yml`, `lanes=memory`, inputs `memory-lane` and `memory-env`); in addition the local Linux x64 container over the families it can download (`node-linux-x64`, `node-win-x64`, `chunkshift-source`), reported separately.

## 3. Metrics and attribution

`E(Dn)` = peak working set of a file's create child − that run's idle baseline, per file; the tables report the worst file and the median over files. Per file:

- (a) retention = `E(D0) − E(D2)`;
- (b) dictionary workspace at K = 4 = `E(D2) − E(D5)`, at K = 2 = `E(D4) − E(D5)`;
- (c) managed share = `E(D2) − E(D7)`;
- floor without zstd = `E(D6)`.

## 4. Decision rule

Evaluated on the worst file of each Linux lane, in order:

1. `E(D2) ≤ 64 MiB` on every lane → **(a) suffices**: the excess is allocator retention. The fix removes the encoder's native allocation churn without changing patch bytes, and A2 is rerun.
2. Otherwise, `E(D4) ≤ 64 MiB` on every lane → **(b) at K = 4**: the dictionary-sized workspace exceeds the bound. The fix changes how or with which parameters the encoder uses zstd: an encoder-policy change (D14) with its own ExperimentId, patch-size and create-time comparison, or a documented create-memory bound that depends on K.
3. Otherwise, `E(D6) > 64 MiB` on a lane → the pipeline without zstd exceeds the bound; investigate the managed heap with D7.
4. Otherwise → (b) at every K ≥ 2; as rule 2.

Independently: (c) is excluded when `E(D2) − E(D7) ≤ 8 MiB` on the worst file of every lane and D7 completes; a D7 create that fails for lack of heap is a finding for (c).

The A2 verdict of PATCH-APPLY-001 stays DEFER until a fix passes the frozen A2 rule; this diagnosis alone never turns it into ADOPT.
