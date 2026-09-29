# CORE-VERIFY-003 protocol (#186): CORE-VERIFY-002 with one execution per platform, fail-closed provenance and a Windows warm file checked against the runner's own disk

Status: **frozen before measurement.** This note is merged before any run of the lanes below produces a decision dataset. Later edits may only add results links; a change to a lane, workload, calibration rung, oracle case, residency check, provenance check or rule is a new ExperimentId. Amended once before any measurement, on 2026-09-29, after the review of [#202](https://github.com/definitely-stable/ChunkShift/pull/202); no lab or run of CORE-VERIFY-003 existed. The amendment changes §2 (the decision bullet), §3.1 (steps 4 and 7, and the time estimate), §3.3 (P7 and P9), §4.1 (the uncached-read contract), §4.2 (the positive control), §4.3 (the rung digests, frozen here), §4.4 (the reading of R3′), §5 (per-run fields), §6 (the rule at the threshold), §7 (the gate of plan step 4) and §9 (what the record gives); the lanes, the workloads and the thresholds 1.6 and 0.95 did not change. Any further change is a new ExperimentId.
Issue: [#186](https://github.com/definitely-stable/ChunkShift/issues/186) (CORE-VERIFY-002 result comment of 2026-09-29, "What follows") · Parent: [#152](https://github.com/definitely-stable/ChunkShift/issues/152) · Predecessor: [CORE-VERIFY-002-PROTOCOL.md](CORE-VERIFY-002-PROTOCOL.md) and [CORE-VERIFY-002-EVIDENCE-20260929-001](../research/results/CORE-VERIFY-002-EVIDENCE-20260929-001.md) · Registry: [EXPERIMENT-INDEX.md](../research/EXPERIMENT-INDEX.md)

| ExperimentId | Question |
|---|---|
| `CORE-VERIFY-003` | Does the DEFER of CORE-VERIFY-002 still hold when each platform's rules come from one execution on one runner, the evaluator refuses inputs that do not belong together, and the Windows warm large file is shown to be cached against that runner's own uncached read? |

This is lab research only. It adds no public API, changes nothing in `src/`, and ships nothing.

## 1. Why a new ExperimentId

CORE-VERIFY-002 reached DEFER (R1 holds on linux-x64 and linux-arm64; R3 warm fails on linux-x64 at 0.916; R1 and R2 warm are `missing` on win-x64). Its record names limits in how that result was measured (its "Limitations" and "Consequences"):

1. **A RunId was not one execution.** The single-file matrix and the tree matrix of a platform ran as two hosted jobs that emitted the same RunId. [docs/research/README.md](../research/README.md) defines a RunId as one concrete execution.
2. **R2 compared two runners.** Its warm operands came from different VMs. The x64 runners of CORE-VERIFY-002 hashed at less than half the speed of CORE-VERIFY-001's (V1 on `S1` warm: 1.30 against 3.03 GiB/s), a spread larger than the x64 warm R2 margin (1.66×). The run documents recorded neither the CPU model nor the storage device.
3. **`verify-lab decide` did not check provenance.** The 71 import checks of the record ran outside the lab.
4. **The Windows large file was not warm, and its residency was inferred.** All 35 pre-read `SL` samples on win-x64 were `not-resident` (probes 0.39–0.81 GiB/s), and the run had no uncached Windows read of its own to compare the probe against. The fixed 1.5 GiB/s threshold is absolute: faster runner storage could make an uncached probe pass it.
5. **The x64 R3 failure is not attributed.** V2-W2 × K fell behind V1 × K at K = 4 and 8. The tree had no `default` pool lane, so the run cannot separate the `spin-0` setting from the slower hardware.

Fixing them changes the job topology, the evaluator, the residency rule on Windows, how `SL` is sized and the matrix. The question is the same, but the measurement is not, so it gets a new ExperimentId. CORE-VERIFY-001 and CORE-VERIFY-002, their protocols and their records stay as they are.

## 2. What is unchanged

Everything not named in §3–§5 is CORE-VERIFY-002 as frozen (which is CORE-VERIFY-001 as frozen and amended, with CORE-VERIFY-002's §3–§4):

- the lanes V0, V1 and V2-W{2,4,8}, their construction and semantics (CORE-VERIFY-001 §1–§2);
- the equivalence oracle O1–O6, the declared difference D1 and the oracle report (CORE-VERIFY-001 §3), run in full in every platform execution before any timed lane;
- the workloads `S1` and `T` and the warm-up file, byte for byte (the content digests CORE-VERIFY-002 recorded: `S1` `57528a0b…`, `T` `7ad7bae6…` over 1,892 files and 855,440,301 bytes), and `SL`'s content (SplitMix64, seed `0xC0FE0020`) with its manifests;
- the storage modes `warm`, `cold` (Linux) and `throttled`, and the throttle model (CORE-VERIFY-001 §5);
- the platforms (GitHub-hosted `ubuntu-24.04`, `ubuntu-24.04-arm`, `windows-2025`, four vCPUs each, .NET 10 JIT, Release), one fresh process per sample, the thread-pool floor of 64 threads, the warm-up, sample counts, lane rotation and the treatment of invalid samples and outliers (CORE-VERIFY-001 §6);
- the pool settings: every gated sample process runs with `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0` (`spin-0`) and reports what it saw, and V0/V1 on `S1` and `SL` warm also run with the default pool (`default`, informative) (CORE-VERIFY-002 §3);
- the pre-read, the probe and the Linux `mincore` check, and the rule that an aggregate needs at least half of its planned samples `resident` (CORE-VERIFY-002 §4.2);
- the measured commit: the pull request's head commit, which the run documents record;
- the metrics and aggregates, the comparisons and thresholds of R1–R3, and the two-of-three form of the decision (CORE-VERIFY-002 §6). When a rule holds or fails at its threshold is new (§6.1).

## 3. Execution and provenance

### 3.1 One execution per platform

Each platform runs **one hosted job**, and that job is the execution. In order, it:

1. checks out the measured commit (full history, which the corpus needs), sets up .NET and Python, frees disk space (Linux), and records the environment (§3.2);
2. builds the lab and runs the full oracle;
3. materializes the corpus and prepares `S1`, `SL` at rung 0 (§4.3), `T` and the warm-up file;
4. on Windows, takes the first uncached reference block (§4.1) and the first positive control (§4.2);
5. calibrates the size of `SL` (§4.3);
6. runs the single-file matrix (`S1`, `SL`), then the tree matrix (`T`), in the group order of §5;
7. on Windows, takes the second uncached reference block (§4.1) and the second positive control (§4.2);
8. writes **one run document** that holds both matrices, and uploads it with the oracle report.

`verify-lab decide` runs in a separate job after all three executions. It measures nothing.

**It fits the job limit.** The jobs of CORE-VERIFY-002 (workflow run 36528577444) took:

| platform | `single` job | `tree` job | sum |
|---|---:|---:|---:|
| linux-x64 | 36.1 min | 56.8 min | 92.9 min |
| linux-arm64 | 35.3 min | 56.2 min | 91.5 min |
| win-x64 | 44.9 min | 50.6 min | 95.5 min |

The sum counts set-up, build and oracle twice; one job runs them once (about 3 minutes less). What CORE-VERIFY-003 adds, estimated from the same run's per-sample overhead (3.8–4.9 s per tree sample outside the timed section):

- the `default` tree group of §4.4: 80 samples, about 6 min on Linux and 7.5 min on Windows;
- on Windows, six uncached reads and the calibration of at most four rungs (§4.1, §4.3): at most 10 min with the 0.4–0.5 GiB/s disk of CORE-VERIFY-002;
- on Windows, six positive-control trials on `S1` (§4.2): under 1 min.

That is at most about 115 minutes per execution, against a job timeout of 350 minutes (the hosted limit is 360). A job that times out leaves an incomplete document, which the provenance check refuses (P6).

**RunId.** `CORE-VERIFY-003/RUN-YYYYMMDD-NNN-<commit>-<platform>` (§9), one per execution. With one job per platform, no two executions share one. A job re-run inside the same workflow run would repeat the RunId, so decision data come only from a first attempt; a failed decision run is replaced by a new workflow run.

**R2 on one runner.** Both operands of R2 on a platform now come from the same run document, measured by the same VM within about two hours. The document records the start and end time of every group.

### 3.2 Environment in the run document

Every run document records, in addition to CORE-VERIFY-002's fields (OS, architecture, runtime, logical processor count, memory `M`):

| field | Linux | Windows |
|---|---|---|
| CPU model | `model name` of `/proc/cpuinfo`; where it is absent (ARM64), `CPU implementer`, `CPU part`, `CPU variant` and `CPU revision` of the first processor | `ProcessorNameString` under `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0`, and `PROCESSOR_IDENTIFIER` |
| cores | logical processors, and physical cores where the OS reports them (distinct `physical id`/`core id` pairs) | logical processors, and physical cores where the OS reports them |
| storage | the block device under the work directory: name, model, rotational, transport, size, and the file system | the volume of the work directory and its physical disk: friendly name, media type, bus type, size, and the file system |
| Actions identity | `GITHUB_RUN_ID`, `GITHUB_RUN_NUMBER`, `GITHUB_RUN_ATTEMPT`, `GITHUB_JOB`, the job's check-run id where the workflow can pass it, `RUNNER_NAME` (on hosted runners it carries the runner id), `RUNNER_OS`, `RUNNER_ARCH`, `ImageOS`, `ImageVersion` | the same |

A field that cannot be read is recorded as `unavailable` with the reason. These fields are reported, not gated; only the workflow identity enters the provenance check (P3). The run document also records an **execution id**, a random identifier drawn once when `verify-lab run` starts, and the oracle report of the same job records the same RunId and execution id.

### 3.3 Fail-closed provenance in `verify-lab decide`

`verify-lab decide` checks that its inputs belong together **before** it computes any aggregate. It is given the measured commit. If any check fails, it writes a decision document with `NO-DECISION`, the reason `provenance`, and the list of checks with their outcomes, evaluates no rule, and exits non-zero. The checks:

| # | check |
|---|---|
| P1 | exactly three run documents and three oracle reports, one of each per platform in {linux-x64, linux-arm64, win-x64}; no other input |
| P2 | per document: `experimentId = CORE-VERIFY-003`, the CORE-VERIFY-003 run schema, `smoke = false`, a full commit SHA; the same commit in every document and equal to the commit `decide` was given |
| P3 | per document: the RunId matches §9 with the document's commit and platform; the three RunIds and the three execution ids are distinct; `GITHUB_RUN_ID` and `GITHUB_RUN_NUMBER` are recorded and equal in all three, `NNN` equals the run number, and `GITHUB_RUN_ATTEMPT` is 1 |
| P4 | per document: one execution, holding both matrices of its platform (single-file and tree groups) |
| P5 | per document: the plan equals the plan `decide` rebuilds from §5 for that platform (win-x64: without the cold groups, and `cold` is the only skipped mode), and the plan fingerprint that `decide` recomputes from the document's plan, workloads and calibration result and its own lab constants equals the recorded one |
| P6 | per document: every planned (group, lane, K, repetition) sample appears exactly once, in the rotation order of §5, with a measurement or an error |
| P7 | workloads: `S1` and `T` bytes, file counts and content digests equal the CORE-VERIFY-002 values of §2 in every document (for `S1`, the full values of the §4.3 table); `SL` is seed `0xC0FE0020` at a rung of §4.3 equal to the document's calibration result, and its SHA-256 and content digest equal the values this note freezes for that size in §4.3, not a constant of the lab; an `SL` size without a row there fails |
| P8 | oracles: each report is full (not quick), carries the RunId, execution id, commit and platform of the document of its platform, and the three reports have equal case and vector counts |
| P9 | calibration and reference: every document holds its calibration trials (§4.3); the win-x64 document holds both uncached reference blocks (or the recorded reason a read failed), and its Windows threshold equals the one `decide` recomputes from them (§4.2); the win-x64 document holds both positive controls, three trials each with probe, threshold and outcome, and the outcome `decide` recomputes for each trial equals the recorded one (§4.2); the Linux documents hold no reference block and no positive control |

A positive control that fails is not a provenance failure: `decide` then treats every pre-read verdict of win-x64 as `unverified` and records the reason (§4.2). An invalid sample (CORE-VERIFY-001 §6) and a failed but well-formed oracle report are not provenance failures: the first is reported as before, the second gives `NO-DECISION` for its own reason (§6). `decision.json` and `decision.md` carry the check table in every case.

Smoke runs are checked against their own expectations (`smoke = true`, one platform) and produce `SMOKE`, never a decision.

## 4. Measurement

### 4.1 An uncached reference read on Windows

On win-x64 the execution measures its own disk. An **uncached read** reads the `SL` file once, sequentially from start to end, under this contract:

1. **Open.** The file is opened for synchronous reading (`GENERIC_READ`, `FILE_SHARE_READ`, `OPEN_EXISTING`) with `FILE_FLAG_NO_BUFFERING` as the only `FILE_FLAG_*` value: no `FILE_FLAG_SEQUENTIAL_SCAN`, `FILE_FLAG_WRITE_THROUGH`, `FILE_FLAG_RANDOM_ACCESS` or `FILE_FLAG_OVERLAPPED`.
2. **Alignment.** `A = max(LogicalBytesPerSector, PhysicalBytesPerSectorForPerformance)`, both from `GetFileInformationByHandleEx` with `FileStorageInfo` (`FILE_STORAGE_INFO`) on the same handle.
3. **Buffer.** One 1 MiB buffer from `NativeMemory.AlignedAlloc` with alignment `max(A, 4096)`, used for every read of the pass and freed after it. A managed `byte[]` is not used.
4. **Checks before the first read.** `1 MiB % A = 0` (so `A` is a power of two no larger than 1 MiB) and `file length % A = 0`. Every rung of §4.3 is a whole number of GiB, so both hold for any such `A`, and the last read ends exactly at the end of the file: there is no tail to handle.
5. **Reads.** Synchronous reads of 1 MiB at offsets 0, 1 MiB, 2 MiB, … (`RandomAccess.Read` on the handle) until the offset equals the file length. Every read must return exactly 1 MiB; a read that returns fewer bytes (including 0) before that offset is an error. Throughput is defined like the probe's: bytes read over the time spent inside the read calls; the open, the alignment query and the allocation are not timed.
6. **Failure.** If `A` cannot be obtained, a check of step 4 fails, or the open or any read fails, the uncached read is **unsuccessful**: the run document records it with the reason and it gives no throughput. §4.2 decides what follows (no successful read: `unverified`).

`FILE_FLAG_NO_BUFFERING` bypasses the operating system's file cache whether or not the file is resident in it, so the read measures the storage path of the same file on the same runner. It does not bypass caching below the operating system: the device, its controller, or the host behind a virtual disk ([File buffering](https://learn.microsoft.com/windows/win32/fileio/file-buffering)). Such a cache can only make an uncached read faster; that raises `U` and the threshold of §4.2, the conservative direction for a `resident` verdict, and the positive control of §4.2 checks that the threshold stays reachable. The run document records `A`, both sector sizes and the outcome of every read.

- **Block 1:** three uncached reads of `SL` at rung 0, after `prepare` and before the calibration.
- **Block 2:** three uncached reads of the calibrated `SL`, after the last sample of the execution.

`U` is the **maximum** throughput over the successful reads of the six. The reads are reported (each read, and the ratio of every Windows probe to `U`); they are not samples of the matrix and enter no rule except through §4.2.

Linux takes no uncached reference: `mincore` checks residency directly there, and the `cold` mode already measures the disk.

### 4.2 Residency

The Windows row of CORE-VERIFY-002 §4.2 becomes:

| platform | `resident` | `not-resident` | `unverified` |
|---|---|---|---|
| Linux | `mincore` resident fraction ≥ 0.99 (unchanged) | fraction < 0.99 | `mincore` could not be called |
| Windows | probe ≥ **max(1.5 GiB/s, 3 × `U`)** | probe below that | the probe failed, no uncached read succeeded, or a positive control failed (below) |
| other | — | — | always |

The sample process records its probe; the Windows verdicts are computed from the document's probes and `U` once block 2 is taken. `decide` recomputes them and uses its own values (P9).

**Why k = 3.** In CORE-VERIFY-002 on `windows-2025`, the disk-bound timed `SL` lanes ran at 0.38–0.46 GiB/s and the `not-resident` probes at 0.39–0.81 GiB/s; resident probes ran at 4.05–5.32 GiB/s on `T` warm and 6.40–8.16 GiB/s on `S1`, with one outlier at 1.99 (`T` throttled). With a disk of that class, 3 × `U` stays at or below 1.5 GiB/s and the rule is the frozen threshold of CORE-VERIFY-002. On faster storage it rises with the disk: a probe must be three times faster than the fastest uncached read of the same file on the same runner, which is more than the 0.81/0.38 ≈ 2.1× that the fastest partly cached probe of CORE-VERIFY-002 reached. The 1.5 GiB/s floor is kept so that a failed or unusually slow reference cannot lower the bar below CORE-VERIFY-002's.

The aggregate rule is unchanged: in a pre-read mode, the rules read the `resident` samples, and only if at least half of the planned samples are `resident` (5 of 10, 3 of 5); otherwise the aggregate is `unverified-warm` and every rule that needs it is `missing`.

**Positive control (win-x64).** A threshold that rises with `U` must still be reachable by a file that is certainly cached; otherwise every rung and every `SL` sample would read as `not-resident` because the classifier does not separate, not because the file did not fit. Three fresh processes each pre-read `S1` and its BLAKE3 manifest exactly as a sample of `S1` warm does, and run the probe on them:

- **control 1**, right after block 1 and before the calibration, judged against the threshold from block 1, `max(1.5 GiB/s, 3 × U₁)` with `U₁` the maximum of block 1, which is the threshold the calibration uses;
- **control 2**, right after block 2, judged against the final threshold (`U` over both blocks).

A trial passes when its probe is at or above its threshold; a failed probe, or a threshold that cannot be computed because no read of the blocks taken so far succeeded, fails it. If any of the six trials fails, every pre-read verdict of win-x64 (calibration trials and samples) becomes `unverified`, and the run document and the record give the reason and the six probes with their thresholds. The execution still runs its whole plan: the control changes no group, rung or order. `S1` is the control because it was the certainly cached class of CORE-VERIFY-002 on `windows-2025` (all 140 pre-read `S1` probes `resident`, 6.40–8.16 GiB/s): with a disk like that run's the control passes by a wide margin, and it fails only where `3 × U` approaches a cached read, which is the case it exists to catch. Linux takes no positive control: `mincore` reads residency directly.

### 4.3 `SL` sized by calibration

A fixed smaller `SL` would be a guess: CORE-VERIFY-002 shows that 6 GiB did not stay cached on a 16 GiB `windows-2025` runner, not how much does. So each execution chooses the size of `SL`, **before its first sample**, by a procedure frozen here.

**Rungs.** Rung 0 is CORE-VERIFY-002's size, `min(10 GiB, round(0.4 × M / 1 GiB) GiB)`, at least 1 GiB (6 GiB on the 16 GiB runners). The further rungs are 4, 3 and 2 GiB, those below rung 0, in that order. At rung `r` the `SL` content is the first `r` bytes of the SplitMix64 stream of seed `0xC0FE0020` (a prefix of rung 0's content), with its BLAKE3 manifest written for that length. The smallest rung keeps `SL` at twice `S1` or more.

**Rung digests (frozen).** P7 compares every document with this table:

| content | seed | bytes | SHA-256 of the file | content digest |
|---|---|---:|---|---|
| `S1` (CORE-VERIFY-002, check) | `0xC0FE0001` | 1,073,741,824 | `520974e9a85c4c750a84cfd334198364abe8e0824ab2a07f12e653dee66f62bc` | `57528a0b33b276292e435462e50f50ee8b6f5273edbb876678e6b4fe97eb7f38` |
| `SL` 6 GiB (rung 0 at 16 GiB; CORE-VERIFY-002, check) | `0xC0FE0020` | 6,442,450,944 | `7092431e212dc83737fe47f3884ce42dd2340721ebbc9d692b57830790ca6a81` | `31840063cc1ddc9c57f52db15b4d7958a3dca506b0bec1ae1cd0a79fa8727829` |
| `SL` 4 GiB | `0xC0FE0020` | 4,294,967,296 | `19499664f610176860a4d9a1549f39326c0fce6b4f153e2c5ad8928a0b5086a0` | `64215c570be3270760cfeb9db775bd339336014f59e02e297db80bf22967e950` |
| `SL` 3 GiB | `0xC0FE0020` | 3,221,225,472 | `ef82401f27709a2b0d8f266327f06da59294196510f68a160b5fea0a1eb6cfe4` | `5ae878aa7a9101f10e589d84992254241dcccd24af043349ba2ff0f8ee3ef6f9` |
| `SL` 2 GiB | `0xC0FE0020` | 2,147,483,648 | `7e632a36ea61fe9239ebb243fae34bcd9762a7e56a5650a1269b68ef879a7bab` | `44e7c60929cdd70a415816f891e93e077b915dd8013e1fbb009bb86e1916a8bf` |

The content digest is the workload digest of the run document: SHA-256 of the UTF-8 text `<SHA-256 hex> <bytes>\n` of the one file (`VerifyLabRun.Summarize`). The values were computed before the amendment by two independent paths that agree on every row: the lab's own generator (`verify-lab prepare`, which calls `VerifyLabWorkloads.GenerateAsync`, run once per size), and a separate SplitMix64 implementation in Python with numpy, written from the algorithm of `DeterministicPrng`. Both paths first reproduced the two digests CORE-VERIFY-002 recorded (the check rows). P7 checks against this table, not against a constant the lab produced. The table covers the hosted runners' rung 0 of 6 GiB; an `SL` of any other size has no row and fails P7.

**Procedure.** For each rung, from rung 0 down: three fresh processes each pre-read the rung's file and manifest exactly as a sample does and run the §4.2 residency check (on Windows with `U` from block 1, the only block taken so far). The first rung at which all three are `resident` becomes `SL` for the whole execution: warm, cold and `default` groups alike. If no rung passes, `SL` is 2 GiB and the record says so; the per-sample checks then decide as always, and the rules that need `SL` warm are most likely `missing`.

**Every platform runs the same procedure.** On Linux, `mincore` found every pre-read `SL` sample of CORE-VERIFY-002 fully resident at 6 GiB, so rung 0 is expected to pass there and keep `SL` at 6 GiB, as in CORE-VERIFY-002. Where platforms end at different rungs, their `SL` digests differ. Every rule compares lanes within one platform, so the rules stay meaningful, as CORE-VERIFY-002 §4.1 already allowed.

The calibration trials are not samples of the matrix. The run document records every trial: rung, bytes, residency verdict, probe and, on Linux, resident fraction.

**No calibration smoke before the freeze.** A smoke run to pick a size would need the CORE-VERIFY-003 lab, which comes after this note, and a size chosen from it would be one more thing to freeze. The in-run procedure above is frozen instead: nothing about the size is decided after this note is merged, and no size is chosen after seeing a decision sample.

### 4.4 The tree with the default pool (informative)

V1 × K and V2-W2 × K also run on `T` warm with the `default` pool, for K = 1, 2, 4 and 8, 10 samples each, on every platform (the plan stays the same everywhere, which P5 relies on). They are reported with GiB/s, effective cores and GiB per CPU-second next to the gated `spin-0` samples. They do not enter R1–R3: the gated setting stays `spin-0`, as in CORE-VERIFY-002, so that no lane is charged for spinning.

The reading is fixed in advance. For each platform, R3′ is the R3 warm ratio computed on the `default` group (best V2-W2 × K / best V1 × K). On a platform where R3 warm fails:

- R3′ ≥ 0.95: the record states that the failure does not reproduce with the `default` pool in this execution. That is consistent with sensitivity to the pool setting, not proof of it: the two groups run one after the other in a job of about two hours, so frequency, thermal state or host contention can differ between them;
- R3′ < 0.95: the record states that the pool setting alone does not explain the failure.

"Fails" is R3 warm failing under §6.1. R3′ is read from its point estimate, and the record gives its interval (computed as in §6.1) next to it. Where R3 warm is indeterminate, the record gives R3 and R3′ with their intervals and makes neither statement. The pool settings are not interleaved within a repetition: that would change the tree matrix and its order for an informative lane, and the non-causal reading above does not need it. Either statement is informative and changes no rule.

## 5. Matrix

| workload | modes | pool | lanes | samples |
|---|---|---|---|---|
| `S1` | warm, cold, throttled | spin-0 | V0, V1, V2-W2, V2-W4, V2-W8 | 10 |
| `S1` SHA-256 | warm | spin-0 | V0, V1 (informative) | 10 |
| `S1` | warm | default | V0, V1 (informative) | 10 |
| `SL` (§4.3) | warm, cold | spin-0 | V0, V1, V2-W2, V2-W4, V2-W8 | 5 |
| `SL` (§4.3) | warm | default | V0, V1 (informative) | 5 |
| `T` | warm, cold, throttled | spin-0 | for K = 1, 2, 4, 8: V0 × K, V1 × K, V2-W2 × K | 10 |
| `T` | warm | default | for K = 1, 2, 4, 8: V1 × K, V2-W2 × K (informative, §4.4) | 10 |

The groups run in the order of the table, mode by mode as listed. `SL` is not run throttled; Windows has no cold mode. Within a (workload, suite, mode, pool) group, repetition r runs the lanes in table order rotated by r, as before.

**Per-sample fields:** those of CORE-VERIFY-002. **Per-run fields added:** the environment of §3.2, the execution id, the start and end time of every group, the calibration trials and the chosen `SL` size (§4.3), and on Windows the six uncached reads with `A` and their outcomes, `U`, the threshold and the six positive-control trials (§4.1–§4.2).

## 6. Decision rules

Evaluated per platform on the BLAKE3 lanes of the `spin-0` matrix, over the samples §4.2 admits, and only after P1–P9 pass. The comparisons and thresholds are CORE-VERIFY-002 §6, with `SL` of the size §4.3 chose; whether a rule holds or fails is decided by the interval rule of §6.1:

- **R1 (per core):** on `S1` warm and on `SL` warm, `GiB per CPU-second (V1) / GiB per CPU-second (V0) ≥ 1.6`, on p50 values.
- **R2 (intra-file beats file-level parallelism):** in `warm` mode and in `throttled` mode, the best V2 (maximum p50 GiB/s over W) on the largest single file of the mode (`SL` warm, `S1` throttled) is strictly faster than the best V1 × K (maximum p50 aggregate GiB/s over K) on `T` in the same mode. Both operands come from the platform's one run document.
- **R3 (multi-file guard, #152):** in `warm` mode and in `throttled` mode, the maximum p50 aggregate GiB/s of V2-W2 × K on `T` is at least 0.95 × that of V1 × K.

### 6.1 The rule at the threshold

CORE-VERIFY-002 evaluated each rule once, on p50 point estimates. Its linux-arm64 R1 on `S1` was 1.600068 against 1.6, a margin of 0.0043 %, far inside the variation between hosted runners: the same performance could have landed on either side, and one such bit can decide between ADOPT and DEFER. So each comparison gets an interval from a paired block bootstrap, and only an interval clear of the threshold decides.

- **Blocks.** A block is one repetition index `r` of a (workload, suite, mode, pool) group: the samples of every lane, and every K, that ran in repetition `r` (§5). A replicate draws `n` repetition indices with replacement from the group's `n` (10 for `S1` and `T`, 5 for `SL`), so the lanes of one repetition stay together and a ratio keeps its pairing.
- **Admitted samples.** Only the samples §4.2 admits enter, as for the point estimate: in a pre-read mode the `resident` samples of an aggregate that is not `unverified-warm` (an `unverified-warm` aggregate makes its rule `missing`, as before), and never an invalid sample (CORE-VERIFY-001 §6). A block drawn twice contributes its admitted samples twice. A replicate computes its statistic exactly like the point estimate: p50 of each lane over the drawn admitted samples (the mean of the two middle values for an even count), then the best over W or K, then the ratio. A replicate in which a lane has no admitted sample is discarded; the record gives the count, and a comparison with more than 5 % of its replicates discarded is indeterminate.
- **Replicates.** B = 10,000 per comparison. The indices come from the lab's SplitMix64 generator (`DeterministicPrng`) seeded with `0xC0FE0B03`, a fresh generator for every comparison; each index is `NextInt32(n)`, and within a replicate the single-file group's indices are drawn before the tree group's. The same documents therefore give the same intervals on every evaluation, and `decide` recomputes them.
- **Interval.** Two-sided 95 % percentile interval: of the `B′` retained replicate values in ascending order, the lower bound is the ⌈0.025 · `B′`⌉-th and the upper bound the ⌈0.975 · `B′`⌉-th.

| rule | comparison (per platform) | resampling | holds | fails |
|---|---|---|---|---|
| R1 | `S1` warm and `SL` warm, each: V1 / V0, p50 GiB per CPU-second | the group of the workload; V0 and V1 from the same blocks | lower bound ≥ 1.6 | upper bound < 1.6 |
| R2 | warm (`SL` against `T`) and throttled (`S1` against `T`), each: best V2 / best V1 × K | the single-file group and the tree group independently, each by its own blocks (they are different groups with different repetition counts) | lower bound > 1 | upper bound < 1 |
| R3 | warm and throttled, each: best V2-W2 × K / best V1 × K on `T` | the `T` group of the mode; both lanes from the same blocks | lower bound ≥ 0.95 | upper bound < 0.95 |

Otherwise a comparison is **indeterminate**. A rule is `missing` on a platform if any of its comparisons is `missing` (as before); otherwise it fails if any comparison fails, holds if every comparison holds, and is **indeterminate** in the remaining cases. An indeterminate rule neither holds nor fails.

Decision:

- **ADOPT**: R1, R2 and R3 hold on at least two of the three platforms.
- **REJECT**: R1 fails on at least two platforms.
- **DEFER**: otherwise.

ADOPT needs R1 to hold on two platforms and REJECT needs it to fail on two, so at most one of them applies. CORE-VERIFY-002 defined DEFER as "R1 holds on at least two platforms, but the ADOPT condition fails"; with an indeterminate outcome that would leave cases with no decision, so DEFER is now the remainder: an R1 that cannot be told from its threshold defers, in either direction.

**Point-estimate verdict (reported, not gating).** `decide` also evaluates R1–R3 and the decision exactly as CORE-VERIFY-001 and CORE-VERIFY-002 did, on the p50 point estimates of the admitted samples, and reports it next to the gated result so that the three experiments can be compared. It does not gate; where the two differ, the record gives both and the interval rule decides.

**Five repetitions.** On `SL` the interval is coarse. A resample of five repetitions has only 126 distinct compositions; with five admitted samples a lane's replicate p50 is always one of its five observed values, and it is its smallest observed value in about 5.8 % of the replicates (and its largest in as many), so the ends of the interval sit at or near the extreme repetitions. With so few blocks the percentile interval is also likely to cover less than its nominal 95 %. It is therefore a frozen stability rule, not a calibrated confidence statement: it keeps a sub-percent margin such as 1.600068 from deciding, and on `SL` it in effect asks that the comparison stay on one side of its threshold across nearly all resamples of the five repetitions. The sample counts stay as they are (§2); a rule that stays indeterminate because of them gives DEFER, and the record says so.

`NO-DECISION` when a provenance check fails (§3.3), or when an oracle fails on any platform, until the cause is fixed and rerun under a new RunId. A rule that is `missing` or indeterminate does not hold. The point-estimate verdict, the `cold` mode, the SHA-256 lane, the `default` pool lanes (including §4.4), V0 × K, the uncached reads, the positive controls, the calibration trials, and the CPU, memory, allocation and residency figures are reported, not gated.

## 7. Relation to the DEFER of CORE-VERIFY-002 (fixed in advance)

| CORE-VERIFY-003 decision | effect on CORE-VERIFY-002 | what follows |
|---|---|---|
| ADOPT, limitation 4 settled (below) | **overturns** it | CORE-VERIFY-002 becomes `SUPERSEDED` by this record. The public-shape discussion of #186 (plan step 4) opens on this record's evidence: named consumer, `PublicAPI.Unshipped.txt`, package validation, NativeAOT smoke, #137 oracle. No API is added by this experiment. |
| ADOPT, limitation 4 not settled | **overturns** it | CORE-VERIFY-002 becomes `SUPERSEDED` by this record, but the public-shape discussion does not open: #186 records that it waits for Windows evidence of the warm large file, which needs a separate experiment under a new ExperimentId. No API is added. |
| DEFER | **confirms** it | CORE-VERIFY-002 stays DEFER, and this record is added as its confirmation. No public shape is discussed; the lanes stay lab code, and #186 records which rules carry the DEFER now. |
| REJECT | **overturns** it (the other way) | CORE-VERIFY-002 becomes `SUPERSEDED` by this record; integrity-only verification is not worth a public shape on this evidence. The lanes stay lab code. |
| NO-DECISION | neither | CORE-VERIFY-002's DEFER stands until a valid run. |

CORE-VERIFY-001 stays `SUPERSEDED` whatever the outcome. The performance decision stays two of three (§6), for continuity with CORE-VERIFY-001 and CORE-VERIFY-002, and the record says which platforms carry it. Permission to open the #186 public-shape discussion (plan step 4) is separate: it needs **ADOPT and limitation 4 settled**. Without that gate, an ADOPT carried by the two Linux platforms could open the discussion while the Windows gap that motivated this experiment stayed open. The gate sits here rather than in §6 so that the decision rule stays the one CORE-VERIFY-001 and CORE-VERIFY-002 used.

Independently of the decision, the record states for each limitation of CORE-VERIFY-002 (§1) whether it is settled:

1. **RunId per execution:** settled when P1–P4 pass.
2. **R2 on one runner:** settled on each platform where R2 is evaluated from its one document; the record names the job and runner behind it.
3. **Provenance in the evaluator:** settled when `decide` evaluated the rules after P1–P9 passed.
4. **Windows warm large file:** settled when win-x64 has at least one successful uncached read (§4.1), all six positive-control trials pass, its `SL` warm aggregates are `resident` under §4.2, and R1 and R2 have been evaluated there under §6.1: holding, failing or indeterminate, not `missing`. The record gives the calibrated size, `U`, the threshold and the probe distribution with its margin. If the aggregates are `unverified-warm`, the limitation stays open and win-x64 R1/R2 count as `missing`, not as failed.
5. **x64 R3:** the record gives R3 and R3′ per platform, each with its point estimate and interval, and the statement of §4.4 wherever R3 fails. If R3 holds on linux-x64, the record says so and the §4.4 statement is not needed.

## 8. Not decided here

Everything CORE-VERIFY-002 §8 leaves open stays open: the public shape, the manifest gates of an integrity-only mode, a file-handle entry point, 100 GiB files, real network storage, NativeAOT and V3. Also open:

- whether a production path should run with or without pool spinning, read in larger pieces, or read the manifest once;
- the thresholds 1.6 and 0.95 and the two-of-three rule, which this experiment does not revisit;
- the hardware class of the hosted runners, which the lab cannot choose; §3.2 records it, and the record compares it with what CORE-VERIFY-001 and CORE-VERIFY-002 could state.

## 9. Records

Each platform execution gets one RunId `CORE-VERIFY-003/RUN-YYYYMMDD-NNN-<commit>-<platform>`: the UTC date the execution starts, `NNN` the workflow run number, `<commit>` the first seven characters of the measured commit, and platform `linux-x64`, `linux-arm64` or `win-x64`. It keeps its raw run document and oracle report as a CI artifact.

The compact committed dataset goes under `docs/research/results/data/CORE-VERIFY-003-YYYYMMDD/`: `samples.csv` with every sample, `runs.json` with the metadata of the three run documents (including §3.2, the calibration, the uncached reads and the positive controls), `decision.json` and `decision.md` as `decide` wrote them in CI (with the provenance table, every comparison's point estimate and interval, and the point-estimate verdict), and the three oracle reports. Before it is committed, every column of `samples.csv` is compared with the raw documents, row by row.

The result record `docs/research/results/CORE-VERIFY-003-EVIDENCE-YYYYMMDD-NNN.md` follows `RESULT-TEMPLATE.md`. It evaluates §6 against this note, gives the point-estimate verdict next to it (§6.1), applies §7, gives the provenance table, the environment of each execution, the calibration, the uncached reads and the positive controls, the §4.4 reading, and every `not-resident` and `unverified` sample. The index row, the registry entry and #186 link it.
