# CORE-VERIFY-002 protocol (#186): confirmatory run of CORE-VERIFY-001 without pool spinning and with a cache-sized warm file

Status: **frozen before measurement.** This note is merged before any run of the lanes below produces a decision dataset. Later edits may only add results links; a change to a lane, workload, oracle case, residency check or rule is a new ExperimentId.
Issue: [#186](https://github.com/definitely-stable/ChunkShift/issues/186) (result comment of 2026-09-29, "Follow-up") · Parent: [#152](https://github.com/definitely-stable/ChunkShift/issues/152) · Predecessor: [CORE-VERIFY-001-PROTOCOL.md](CORE-VERIFY-001-PROTOCOL.md) and [CORE-VERIFY-001-EVIDENCE-20260929-001](../research/results/CORE-VERIFY-001-EVIDENCE-20260929-001.md) · Registry: [EXPERIMENT-INDEX.md](../research/EXPERIMENT-INDEX.md)

| ExperimentId | Question |
|---|---|
| `CORE-VERIFY-002` | Does the ADOPT of CORE-VERIFY-001 still hold when no lane is charged for thread-pool spinning and the warm large file stays in the runner's file cache? |

This is lab research only. It adds no public API, changes nothing in `src/`, and ships nothing.

## 1. Why a new ExperimentId

CORE-VERIFY-001 reached ADOPT with two caveats (its evidence record, "Caveats that bear on the decision"):

1. **V0 is charged for thread-pool spinning.** V0 showed 2.0 effective cores on linux-x64 and 1.5 on linux-arm64 although it reads sequentially. The pool's workers spin between V0's 64 KiB asynchronous reads. A local control with `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0` brought V0 to 1.09 cores at the same wall time. Without the spinning, R1 on linux-arm64 is about 1.6, at the threshold.
2. **`S10` warm was not warm on Windows.** The 10 GiB file did not stay in the `windows-2025` runner's file cache, so every "warm" S10 lane ran at the disk's 0.38 GiB/s and R2 failed there for that reason.

Both caveats are about how the measurement is taken, not about the lanes. Settling them changes the sample environment (§3) and a workload with its pre-sample check (§4), so the question gets a new ExperimentId. CORE-VERIFY-001, its protocol and its record stay as they are.

## 2. What is unchanged

Everything not named in §3–§6 is CORE-VERIFY-001 as frozen and amended before its runs:

- the lanes V0, V1 and V2-W{2,4,8} and their construction (CORE-VERIFY-001 §1);
- the semantics of V1 and V2, including the manifest gate, `cdc-fallback`, early exit, cancellation and bounded memory (§2);
- the equivalence oracle O1–O6, the declared difference D1 and the oracle report (§3), run in every platform job before any timed lane;
- the workloads `S1` and `T`, the warm-up file and the shipped profile (§4);
- the storage modes `warm`, `cold` (Linux) and `throttled`, and the throttle model (§5);
- the platforms (GitHub-hosted `ubuntu-24.04`, `ubuntu-24.04-arm`, `windows-2025`, four vCPUs each, .NET 10 JIT, Release), one fresh process per sample, a thread-pool floor of 64 threads, the warm-up (the warm-up file verified with the sample's lane for at least five rounds and three seconds, 200 ms apart), sample counts, lane rotation, and the treatment of invalid samples and outliers (§6);
- the metrics and aggregates (§6), and the form of the rules R1–R3 and of the decision (§7).

## 3. Change A: no thread-pool spinning in the gated samples

Every sample process of the gated matrix (§5) starts with the environment variable `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`. The runtime reads it at start-up, before the warm-up. Workers that find the pool's queue empty then block at once instead of spinning. That applies to every lane, not just V0: V2's workers and the many-file slots run on the same pool.

The sample process reports the value it saw. A gated sample whose process did not see `0` is invalid.

**Informative comparison.** V0 and V1 also run on `S1` warm and `SL` warm with the runtime's default spinning (the variable is absent, as in CORE-VERIFY-001). These samples are reported next to the gated ones, with effective cores and GiB per CPU-second, so that the record can state how much CPU the spinning cost each lane. They do not enter R1–R3.

Pool setting names in the run documents: `spin-0` (gated) and `default` (informative).

## 4. Change B: a warm large file sized to the runner's memory

### 4.1 Workload `SL`

`SL` replaces `S10` as the largest single file:

| id | content | manifests |
|---|---|---|
| `SL` | one file of `min(10 GiB, round(0.4 × M / 1 GiB) GiB)` bytes, at least 1 GiB, SplitMix64 bytes, seed `0xC0FE0020` | BLAKE3 |

`M` is the runner's total physical memory as .NET reports it (`GCMemoryInfo.TotalAvailableMemoryBytes`, which is the cgroup limit where one applies). The runner records `M` and the size it chose.

The three platforms of §2 have 16 GiB each, so **`SL` is 6 GiB (6,442,450,944 bytes) on every platform of this experiment.** If a runner reports a memory that gives another size, the run keeps that size and the record says so: every rule compares lanes within one platform, so the rules stay meaningful, but that platform's `SL` digest then differs from the others'.

`SL` is used in warm and cold mode, like `S10` before it; cold stays Linux-only and informative. `S10` is not run. It is not needed for cold mode, and it would add 10 GiB to the runner's disk for an informative lane.

### 4.2 Residency check

Pre-read modes (`warm`, `throttled`) read the sample's files once, untimed, before the timed section, as in CORE-VERIFY-001 §5. Each such sample then checks that the files are actually in the file cache, after the pre-read and before the timed section. The check covers every content file and every manifest of the sample. It has two parts:

- **Probe (all platforms).** One more untimed pass reads every file with synchronous 1 MiB reads. The probe's throughput is the bytes read divided by the time spent inside the read calls; opening and closing files are not counted, so the many small files of `T` are not penalized for per-file open costs. The probe is reported on every platform.
- **Direct check (Linux).** Every file is mapped read-only and `mincore` reports which of its pages are resident. The resident fraction is the resident pages over all pages of all the sample's files.

A sample's **residency** is:

| platform | `resident` | `not-resident` | `unverified` |
|---|---|---|---|
| Linux | `mincore` resident fraction ≥ 0.99 | fraction < 0.99 | `mincore` could not be called |
| Windows | probe ≥ 1.5 GiB/s | probe < 1.5 GiB/s | the probe failed |
| other | — | — | always |

On Windows the check is indirect: Windows has no public call that reports the cache residency of a file. The 1.5 GiB/s threshold sits about 4× above the disk-bound rate of CORE-VERIFY-001 (0.38 GiB/s on `windows-2025`) and below the cached rate the same runner reached on `S1` warm. A pass whose reads were all fast was served from the cache, so the file was resident when the pass ended, immediately before the timed section. On Linux the probe is reported beside `mincore`, which calibrates the Windows threshold.

Samples in `cold` mode are not checked (`n/a`).

A sample that is `not-resident` or `unverified` stays a **valid** sample if its verdict is valid (O1). It is reported, but it does not enter the rules. For each (platform, workload, mode, pool, lane, K) of the gated matrix, the rules read aggregates over the `resident` samples only, and only if at least half of the planned samples are `resident` (5 of 10 for `S1` and `T`, 3 of 5 for `SL`). Otherwise the aggregate is marked `unverified-warm`, and every rule that needs it reports `missing` on that platform. A rule that is `missing` does not hold.

## 5. Matrix

| workload | modes | pool | lanes | samples |
|---|---|---|---|---|
| `S1` | warm, cold, throttled | spin-0 | V0, V1, V2-W2, V2-W4, V2-W8 | 10 |
| `S1` SHA-256 | warm | spin-0 | V0, V1 (informative) | 10 |
| `S1` | warm | default | V0, V1 (informative, §3) | 10 |
| `SL` | warm, cold | spin-0 | V0, V1, V2-W2, V2-W4, V2-W8 | 5 |
| `SL` | warm | default | V0, V1 (informative, §3) | 5 |
| `T` | warm, cold, throttled | spin-0 | for K = 1, 2, 4, 8: V0 × K, V1 × K, V2-W2 × K | 10 |

`SL` is not run throttled (as `S10` before it). Windows has no cold mode. Within a (workload, suite, mode, pool) group, repetition r runs the lanes in matrix order rotated by r, as before.

**Per-sample fields added to CORE-VERIFY-001's metrics:** the pool setting the process saw; for pre-read modes, the residency verdict, the probe throughput and, on Linux, the `mincore` resident fraction. **Per-run fields added:** `M` and the `SL` size.

## 6. Decision rules

Evaluated per platform on the BLAKE3 lanes of the `spin-0` matrix, over the samples §4.2 lets in; ratios use p50 values. The rules are CORE-VERIFY-001 §7, with `SL` in the place of `S10`:

- **R1 (per core):** on `S1` warm and on `SL` warm, `GiB per CPU-second (V1) / GiB per CPU-second (V0) ≥ 1.6`.
- **R2 (intra-file beats file-level parallelism):** in `warm` mode and in `throttled` mode, the best V2 (maximum GiB/s over W) on the largest single file of the mode (`SL` warm, `S1` throttled) is strictly faster than the best V1 × K (maximum aggregate GiB/s over K) on `T` in the same mode.
- **R3 (multi-file guard, #152):** in `warm` mode and in `throttled` mode, the maximum aggregate GiB/s of V2-W2 × K on `T` is at least 0.95 × that of V1 × K.

Decision, as in CORE-VERIFY-001:

- **ADOPT**: R1, R2 and R3 hold on at least two of the three platforms.
- **DEFER**: R1 holds on at least two platforms, but the ADOPT condition fails.
- **REJECT**: R1 fails on at least two platforms.

An oracle failure on any platform means no decision (`NO-DECISION`) until it is fixed and rerun under a new RunId. The `cold` mode, the SHA-256 lane, the `default` pool lanes, V0 × K, and the CPU, memory, allocation and residency figures are reported, not gated.

## 7. Relation to the ADOPT of CORE-VERIFY-001 (fixed in advance)

| CORE-VERIFY-002 decision | effect on CORE-VERIFY-001 | what follows |
|---|---|---|
| ADOPT | **confirms** it | CORE-VERIFY-001 stays ADOPT; the #186 public-shape discussion (plan step 4) continues. This record's numbers replace caveats 1 and 2 as the per-core and large-file evidence for that discussion. |
| DEFER | **weakens** it | CORE-VERIFY-001 becomes `SUPERSEDED` by this record, whose DEFER governs: no public shape is discussed on this evidence; the lanes stay lab code, and #186 records the numbers. |
| REJECT | **weakens** it (overturns it) | CORE-VERIFY-001 becomes `SUPERSEDED`; integrity-only verification is not worth a public shape on this evidence. |
| NO-DECISION | neither | CORE-VERIFY-001 stands with its caveats until a valid run. |

Independently of the decision, the record states for each caveat whether it is settled:

- **Caveat 1** is settled when R1 has been evaluated from `spin-0` samples on all three platforms, whatever its outcome. The record gives R1 on linux-arm64 explicitly, and V0's effective cores for `spin-0` and `default`.
- **Caveat 2** is settled when win-x64's `SL` warm aggregates are `resident` (§4.2) and R2 has been evaluated there, holding or failing. If they are `unverified-warm`, caveat 2 stays open. The record says so, and win-x64 R2 counts as `missing`, not as failed.

## 8. Not decided here

Everything CORE-VERIFY-001 §8 leaves open stays open: the public shape, the manifest gates of an integrity-only mode, a file-handle entry point, 100 GiB files, real network storage, NativeAOT and V3. Whether a production V0 should read in larger pieces, or with a synchronous handle, is an input to the shape discussion, not a question for this experiment.

## 9. Records

Each platform run gets a RunId `CORE-VERIFY-002/RUN-YYYYMMDD-NNN-<commit>-<platform>`, with platform `linux-x64`, `linux-arm64` or `win-x64`. It keeps its raw per-sample JSON and the oracle report as a CI artifact, plus a compact committed dataset under `docs/research/results/data/`. The result record `docs/research/results/CORE-VERIFY-002-EVIDENCE-YYYYMMDD-NNN.md` follows `RESULT-TEMPLATE.md`. It evaluates §6 against this note, applies §7, and lists every `not-resident` and `unverified` sample. The index row, the registry entry and #186 link it.

Results (2026-09-29, commit `56e49e2`): DEFER — [CORE-VERIFY-002-EVIDENCE-20260929-001](../research/results/CORE-VERIFY-002-EVIDENCE-20260929-001.md).
