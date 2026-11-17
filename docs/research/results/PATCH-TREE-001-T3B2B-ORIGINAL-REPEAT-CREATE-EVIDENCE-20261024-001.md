# PATCH-TREE-001 / T3-B2B — original repeated create CPU and cost admission

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261024-T3B2B-001`  
**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184), parent [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Date:** 2026-10-24.  
**Verdict:** **T3B2B_ORIGINAL_REPEAT_CREATE_PASS / FALLBACK_T0_OR_T0S__CREATE_BUDGET (both original pairs) / NO_PRODUCTION_ADOPT.**

This is **a rejection of automatic cross-file T3 under the pre-registered 3× create CPU/wall budget on the original 2-DLL calibration**, not a rejection of its substantial physical patch-size advantage. This run did not implement a production fallback, signed update-set, host-owned base selector, or any new CSP/CSM format.

## Frozen sources, provenance and independent checks

Protocol: [T3-B2B repeat-create/CPU/cost admission](../../benchmarks/PATCH-TREE-001-T3B2B-REPEAT-CREATE-ADMISSION-PROTOCOL.md). Implementation [PR #405](https://github.com/definitely-stable/ChunkShift/pull/405), main **`6c32a74f29ef179e4efbe8831c0618c9dd4bfd8a`**. Original first-attempt [GitHub-hosted run #40078373627](https://github.com/definitely-stable/ChunkShift/actions/runs/40078373627) **SUCCESS**, Ubuntu 24.04 Linux/X64 (one hosted runner process), run attempt 1. Only four source-locked .NET **calibration** trees were materialized for original SHA verification; both actual measured pairs are the preselected ASP.NET Core win-x64 mscordaccore version-renames. Sealed Node/tzdata/ChunkShift-source holdout remained closed.

- Original independent pairs run #38136336025, `pairs.json` SHA256 **`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`**.
- Prior original B1 real source/CSP [run #39967460136](https://github.com/definitely-stable/ChunkShift/actions/runs/39967460136) and independent B2A source-preselection [run #39977481690](https://github.com/definitely-stable/ChunkShift/actions/runs/39977481690) were separately SHA-locked and their **old/new paths, complete source and target SHA-256** cross-checked before creating a patch.
- Original artifact **#12553937250**, `patch-tree-t3b2b-original-repeated-NO-ADOPT`, physical ZIP **301,954 B**, ZIP SHA256 **`9b0562b888952958b1e90ab1d2808f2c12e40d722628095b0127a54870375568`** (matches GitHub artifact digest). All **8** ZIP members pass CRC; `SHA256SUMS` checks all 7 retained data files:

| Archived file | SHA-256 |
| --- | --- |
| `original-pairs.json` | `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd` |
| `original-b1-selection.json` | `2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad` |
| `original-b2a-selected.json` | `1798536b998c9078fda8dca5fb2af457909753c7b7f811e89402f63e9ad8242b` |
| `rehash-selection.json` | `2627067691b3fb2cd6515d8eaf5107b331080c4603599b5ae276e522d08dfdad` |
| `layouts.json` | `6deb37fd4cb778fab1ad67a1da8d187ed0205ac2c89c4fa391e5507169da389a` |
| `samples.json` | `306944646d0b3a7fb60ed0b44296adb2d93869ee151ed2159f043589d9a4ccb5` |
| `provenance.json` | `0a5883bbc423eb0cdfee82d966160a7799b6380d2beec3b0e27f793fa194ae52` |

[Independent pure-stdlib replay](data/PATCH-TREE-001-T3B2B-EVIDENCE-20261024-001/recompute.py) verifies source locks, all eight canonical old/target layout SHA-256 digests, old/new source identities vs original B1/B2A, exact 12-entry per-file lane chronology, real CSP physical SHA/byte invariants, recomputes per-lane medians and applies **unchanged pre-registered** physical/wall/CPU budget. A source-locked hosted CI replay must pass independently before accepting this durable evidence.

**Evidence limit:** Original raw DLL/CSM/CSP bytes are *not* preserved in the ZIP; independent off-line replay checks the provenance/locked ledger, not an independent decode of those raw original CSPs. The producer did run actual public SDK create/apply for **every** warmup and measured round, SHA-256-verify full staged targets, and inspect F0 CSP sections in the first measured round.

## Per-pair repeated physical and performance result

Each pair: **one T0S then T3 warmup**, five measured **AB/BA alternating** pairs; each patch independently applies/validates. CSM creation/verification is separately costed. CPU is **whole-process accumulated CPU**; wall is wall-clock. Medians exclude warmups. Processes were not restarted for each lane.

| Original .NET source→target | T0S physical | T3 CSP | Map | T3 warm | T3 cold (+old CSM) |
| --- | ---: | ---: | ---: | ---: | ---: |
| 10.0.10 → 10.0.11 | 548,714 B | 86,432 B | 295 B | **86,727 B** | **87,841 B** |
| 10.0.11 → 10.0.12 | 548,863 B | 5,649 B | 295 B | **5,944 B** | **7,058 B** |
| **Total** | **1,097,577 B** | **92,081 B** | **590 B** | **92,671 B** | **94,899 B** |

These physical bytes match the independently audited prior B1 real CSP. Moreover, all **24 CSP instances** (12 per pair, six T0S and six T3 including warmups) have CSP SHA-256 identical to the corresponding original B1 CSP file SHA. Thus no patch-size or binary nondeterminism was observed under these original inputs; this is not a format guarantee across all machines/versions.

| Metric (median measured) | 10.0.10→10.0.11 T0S | T3 | 10.0.11→10.0.12 T0S | T3 |
| --- | ---: | ---: | ---: | ---: |
| Process CPU time | **308.941 ms** | **3,614.386 ms** | **259.708 ms** | **1,088.596 ms** |
| Wall-clock | **133.935 ms** | **1,740.003 ms** | **128.814 ms** | **549.607 ms** |
| CPU T3/T0S ratio | — | **11.699×** | — | **4.192×** |
| Wall T3/T0S ratio | — | **12.991×** | — | **4.267×** |
| GC allocated bytes (median, process-global) | 7,239,040 | 15,375,464 | 7,388,416 | 12,413,056 |
| Logical old-file Stream API bytes per create | 0 | **36,133,305** | 0 | **14,522,936** |

Old-file size was **1,356,632 B**, so reported T3 old-file read amplification is approximately **26.6×** and **10.7×**. Across **five measured rounds**, old Stream API reads total **180,666,525 B** and **72,614,680 B**, respectively. This counter sums Stream bytes returned and repeated/positional reads, *not* true physical disk traffic, cache misses, SSD reads or a new network dependency.

**CSM setup, measured once separately (not included in steady create samples):**

| Pair | Old CSM build (wall / process CPU) | Old source bytes read | Old serialized CSM bytes | Target CSM build (wall / CPU) |
| --- | --- | ---: | ---: | --- |
| 10.0.10 → 10.0.11 | 57.288 / 62.112 ms | 1,356,632 | 1,114 | 5.243 / 6.879 ms |
| 10.0.11 → 10.0.12 | 1.231 / 2.430 ms | 1,356,632 | 1,114 | 1.112 / 2.179 ms |

Single setup samples are diagnostic, not stable estimates of CSM build cost. The significant first CSM warmup discrepancy also demonstrates why B1 single timings could not serve as a controlled performance baseline.

**Memory interpretation:** A 10-ms sampler recorded whole-process `WorkingSet64` and `PrivateMemorySize64`, not private RSS or isolated operation high-water. Median *observed process WorkingSet peaks* were approximately **156.7 MB vs 157.8 MB** for first T0S/T3 pair and **204.5 MB vs 209.0 MB** for second (decimal megabytes from archived byte counts, not binary MiB). Prior rounds and GC can change process baseline; no causal peak-memory gain/penalty or safe production memory bound follows. GC allocated-byte observations above likewise are process-global.

## Frozen cost-admission result and safety boundary

Pre-registered size gate: `T3 warm < 0.90 × min(raw T0, measured T0S)`, with cold `T3+old CSM ≤ min(raw T0,T0S)`. **Both source pairs pass**: T3 warm is 91.56% smaller than T0S in aggregate.

Pre-registered create budget: both **CPU** and **wall** T3/T0S median ratios must be at most **3×**. **Both pairs fail by wide margins**, therefore independently selected verdict for each is:

**`FALLBACK_T0_OR_T0S__CREATE_BUDGET`**.

This is a cost-admission lab recommendation, not an actual new production fallback implementation. Exact old base remains useful for bandwidth-limited/offline patch builds where higher CPU is acceptable; whether different SLA, base CSM caching or encoder internals can improve the tradeoff needs an **independently preregistered** follow-up study with real multi-file workloads.

## Critical audit and next step

1. The five full measured samples per lane retain direction, all bytes, process CPU/wall, sampled process residency, GC, and exact original CSP SHAs. No sample was removed as an outlier and no admission threshold was tuned after original measurement.
2. **The high repeated old-file Stream reads and creation CPU are now the strongest potential bottleneck.** Next allowed T3-B2C is a tightly scoped **create-read amplification/stage attribution** experiment: independently distinguish BASE CSM reads, source file random/nonmonotonic reads and patch encoder probes; evaluate already-present content cache/positioning without altering frozen output on original two files. If changing an internal encoder is proposed, gate on CSP exact apply/format vectors and independently metered source reads, CPU, allocation and memory on hosted CI.
3. A production opt-in decision additionally requires broader rename population without hand selection, complete update-set physical denominator, host-owned authenticated base source availability/namespace pinning, target metadata, publication/GC and actual receiver memory/trust limits. The B2A selector is not an authorization mechanism.
4. Keep `#184` and `#251` **OPEN**, CSP v1 and all shipped APIs unchanged. No sealed holdout was opened.

**Final disposition: `T3B2B_ORIGINAL_REPEAT_CREATE_VALIDATED / BOTH_PAIRS_CREATE_BUDGET_REJECT / CALIBRATION_ONLY / NO_PRODUCTION_ADOPT`.**
