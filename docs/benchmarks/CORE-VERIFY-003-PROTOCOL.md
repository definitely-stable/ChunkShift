# CORE-VERIFY-003 protocol (#186): CORE-VERIFY-002 with one execution per platform, fail-closed provenance and a Windows warm file checked against the runner's own disk

Status: **frozen before measurement.** This note is merged before any run of the lanes below produces a decision dataset. Later edits may only add results links; a change to a lane, workload, calibration rung, oracle case, residency check, provenance check or rule is a new ExperimentId.
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
- the metrics and aggregates, and the form of R1–R3 and of the decision (CORE-VERIFY-002 §6).

## 3. Execution and provenance

### 3.1 One execution per platform

Each platform runs **one hosted job**, and that job is the execution. In order, it:

1. checks out the measured commit (full history, which the corpus needs), sets up .NET and Python, frees disk space (Linux), and records the environment (§3.2);
2. builds the lab and runs the full oracle;
3. materializes the corpus and prepares `S1`, `SL` at rung 0 (§4.3), `T` and the warm-up file;
4. on Windows, takes the first uncached reference block (§4.1);
5. calibrates the size of `SL` (§4.3);
6. runs the single-file matrix (`S1`, `SL`), then the tree matrix (`T`), in the group order of §5;
7. on Windows, takes the second uncached reference block (§4.1);
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
- on Windows, six uncached reads and the calibration of at most four rungs (§4.1, §4.3): at most 10 min with the 0.4–0.5 GiB/s disk of CORE-VERIFY-002.

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
| P7 | workloads: `S1` and `T` bytes, file counts and content digests equal the CORE-VERIFY-002 values of §2 in every document; `SL` is seed `0xC0FE0020` at a rung of §4.3 equal to the document's calibration result, and its digest equals the digest the lab pins for that rung |
| P8 | oracles: each report is full (not quick), carries the RunId, execution id, commit and platform of the document of its platform, and the three reports have equal case and vector counts |
| P9 | calibration and reference: every document holds its calibration trials (§4.3); the win-x64 document holds both uncached reference blocks (or the recorded reason a read failed), and its Windows threshold equals the one `decide` recomputes from them (§4.2); the Linux documents hold no reference block |

An invalid sample (CORE-VERIFY-001 §6) and a failed but well-formed oracle report are not provenance failures: the first is reported as before, the second gives `NO-DECISION` for its own reason (§6). `decision.json` and `decision.md` carry the check table in every case.

Smoke runs are checked against their own expectations (`smoke = true`, one platform) and produce `SMOKE`, never a decision.

## 4. Measurement

### 4.1 An uncached reference read on Windows

On win-x64 the execution measures its own disk. An **uncached read** opens the `SL` file with `FILE_FLAG_NO_BUFFERING` and reads it sequentially from start to end with synchronous 1 MiB reads into a sector-aligned buffer. Its throughput is defined like the probe's: bytes read over the time spent inside the read calls. A read with `FILE_FLAG_NO_BUFFERING` bypasses the file cache whether or not the file is resident, so it measures the storage path of the same file on the same runner.

- **Block 1:** three uncached reads of `SL` at rung 0, after `prepare` and before the calibration.
- **Block 2:** three uncached reads of the calibrated `SL`, after the last sample of the execution.

`U` is the **maximum** throughput over the six reads. The reads are reported (each read, and the ratio of every Windows probe to `U`); they are not samples of the matrix and enter no rule except through §4.2.

Linux takes no uncached reference: `mincore` checks residency directly there, and the `cold` mode already measures the disk.

### 4.2 Residency

The Windows row of CORE-VERIFY-002 §4.2 becomes:

| platform | `resident` | `not-resident` | `unverified` |
|---|---|---|---|
| Linux | `mincore` resident fraction ≥ 0.99 (unchanged) | fraction < 0.99 | `mincore` could not be called |
| Windows | probe ≥ **max(1.5 GiB/s, 3 × `U`)** | probe below that | the probe failed, or no uncached read succeeded |
| other | — | — | always |

The sample process records its probe; the Windows verdicts are computed from the document's probes and `U` once block 2 is taken. `decide` recomputes them and uses its own values (P9).

**Why k = 3.** In CORE-VERIFY-002 on `windows-2025`, the disk-bound timed `SL` lanes ran at 0.38–0.46 GiB/s and the `not-resident` probes at 0.39–0.81 GiB/s; resident probes ran at 4.05–5.32 GiB/s on `T` warm and 6.40–8.16 GiB/s on `S1`, with one outlier at 1.99 (`T` throttled). With a disk of that class, 3 × `U` stays at or below 1.5 GiB/s and the rule is the frozen threshold of CORE-VERIFY-002. On faster storage it rises with the disk: a probe must be three times faster than the fastest uncached read of the same file on the same runner, which is more than the 0.81/0.38 ≈ 2.1× that the fastest partly cached probe of CORE-VERIFY-002 reached. The 1.5 GiB/s floor is kept so that a failed or unusually slow reference cannot lower the bar below CORE-VERIFY-002's.

The aggregate rule is unchanged: in a pre-read mode, the rules read the `resident` samples, and only if at least half of the planned samples are `resident` (5 of 10, 3 of 5); otherwise the aggregate is `unverified-warm` and every rule that needs it is `missing`.

### 4.3 `SL` sized by calibration

A fixed smaller `SL` would be a guess: CORE-VERIFY-002 shows that 6 GiB did not stay cached on a 16 GiB `windows-2025` runner, not how much does. So each execution chooses the size of `SL`, **before its first sample**, by a procedure frozen here.

**Rungs.** Rung 0 is CORE-VERIFY-002's size, `min(10 GiB, round(0.4 × M / 1 GiB) GiB)`, at least 1 GiB (6 GiB on the 16 GiB runners). The further rungs are 4, 3 and 2 GiB, those below rung 0, in that order. At rung `r` the `SL` content is the first `r` bytes of the SplitMix64 stream of seed `0xC0FE0020` (a prefix of rung 0's content), with its BLAKE3 manifest written for that length. The lab pins the content digest of every rung (P7). The smallest rung keeps `SL` at twice `S1` or more.

**Procedure.** For each rung, from rung 0 down: three fresh processes each pre-read the rung's file and manifest exactly as a sample does and run the §4.2 residency check (on Windows with `U` from block 1, the only block taken so far). The first rung at which all three are `resident` becomes `SL` for the whole execution: warm, cold and `default` groups alike. If no rung passes, `SL` is 2 GiB and the record says so; the per-sample checks then decide as always, and the rules that need `SL` warm are most likely `missing`.

**Every platform runs the same procedure.** On Linux, `mincore` found every pre-read `SL` sample of CORE-VERIFY-002 fully resident at 6 GiB, so rung 0 is expected to pass there and keep `SL` at 6 GiB, as in CORE-VERIFY-002. Where platforms end at different rungs, their `SL` digests differ. Every rule compares lanes within one platform, so the rules stay meaningful, as CORE-VERIFY-002 §4.1 already allowed.

The calibration trials are not samples of the matrix. The run document records every trial: rung, bytes, residency verdict, probe and, on Linux, resident fraction.

**No calibration smoke before the freeze.** A smoke run to pick a size would need the CORE-VERIFY-003 lab, which comes after this note, and a size chosen from it would be one more thing to freeze. The in-run procedure above is frozen instead: nothing about the size is decided after this note is merged, and no size is chosen after seeing a decision sample.

### 4.4 The tree with the default pool (informative)

V1 × K and V2-W2 × K also run on `T` warm with the `default` pool, for K = 1, 2, 4 and 8, 10 samples each, on every platform (the plan stays the same everywhere, which P5 relies on). They are reported with GiB/s, effective cores and GiB per CPU-second next to the gated `spin-0` samples. They do not enter R1–R3: the gated setting stays `spin-0`, as in CORE-VERIFY-002, so that no lane is charged for spinning.

The reading is fixed in advance. For each platform, R3′ is the R3 warm ratio computed on the `default` group (best V2-W2 × K / best V1 × K). On a platform where R3 warm fails:

- R3′ ≥ 0.95: the record states that the failure goes with the `spin-0` setting on that runner, because both groups ran on the same VM in the same execution;
- R3′ < 0.95: the record states that the pool setting does not explain the failure.

Either statement is informative and changes no rule.

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

**Per-sample fields:** those of CORE-VERIFY-002. **Per-run fields added:** the environment of §3.2, the execution id, the start and end time of every group, the calibration trials and the chosen `SL` size (§4.3), and on Windows the six uncached reads, `U` and the threshold (§4.1–§4.2).

## 6. Decision rules

Evaluated per platform on the BLAKE3 lanes of the `spin-0` matrix, over the samples §4.2 lets in, and only after P1–P9 pass; ratios use p50 values. The rules are CORE-VERIFY-002 §6, with `SL` of the size §4.3 chose:

- **R1 (per core):** on `S1` warm and on `SL` warm, `GiB per CPU-second (V1) / GiB per CPU-second (V0) ≥ 1.6`.
- **R2 (intra-file beats file-level parallelism):** in `warm` mode and in `throttled` mode, the best V2 (maximum GiB/s over W) on the largest single file of the mode (`SL` warm, `S1` throttled) is strictly faster than the best V1 × K (maximum aggregate GiB/s over K) on `T` in the same mode. Both operands come from the platform's one run document.
- **R3 (multi-file guard, #152):** in `warm` mode and in `throttled` mode, the maximum aggregate GiB/s of V2-W2 × K on `T` is at least 0.95 × that of V1 × K.

Decision, as in CORE-VERIFY-001 and CORE-VERIFY-002:

- **ADOPT**: R1, R2 and R3 hold on at least two of the three platforms.
- **DEFER**: R1 holds on at least two platforms, but the ADOPT condition fails.
- **REJECT**: R1 fails on at least two platforms.

`NO-DECISION` when a provenance check fails (§3.3), or when an oracle fails on any platform, until the cause is fixed and rerun under a new RunId. A rule that is `missing` does not hold. The `cold` mode, the SHA-256 lane, the `default` pool lanes (including §4.4), V0 × K, the uncached reads, the calibration trials, and the CPU, memory, allocation and residency figures are reported, not gated.

## 7. Relation to the DEFER of CORE-VERIFY-002 (fixed in advance)

| CORE-VERIFY-003 decision | effect on CORE-VERIFY-002 | what follows |
|---|---|---|
| ADOPT | **overturns** it | CORE-VERIFY-002 becomes `SUPERSEDED` by this record. The public-shape discussion of #186 (plan step 4) opens on this record's evidence: named consumer, `PublicAPI.Unshipped.txt`, package validation, NativeAOT smoke, #137 oracle. No API is added by this experiment. |
| DEFER | **confirms** it | CORE-VERIFY-002 stays DEFER, and this record is added as its confirmation. No public shape is discussed; the lanes stay lab code, and #186 records which rules carry the DEFER now. |
| REJECT | **overturns** it (the other way) | CORE-VERIFY-002 becomes `SUPERSEDED` by this record; integrity-only verification is not worth a public shape on this evidence. The lanes stay lab code. |
| NO-DECISION | neither | CORE-VERIFY-002's DEFER stands until a valid run. |

CORE-VERIFY-001 stays `SUPERSEDED` whatever the outcome. **Only ADOPT** opens the #186 public-shape discussion (plan step 4); an ADOPT is ADOPT by §6 even if one platform is `missing`, and the record says which.

Independently of the decision, the record states for each limitation of CORE-VERIFY-002 (§1) whether it is settled:

1. **RunId per execution:** settled when P1–P4 pass.
2. **R2 on one runner:** settled on each platform where R2 is evaluated from its one document; the record names the job and runner behind it.
3. **Provenance in the evaluator:** settled when `decide` evaluated the rules after P1–P9 passed.
4. **Windows warm large file:** settled when win-x64 has at least one successful uncached read, its `SL` warm aggregates are `resident` under §4.2, and R1 and R2 have been evaluated there, holding or failing. The record gives the calibrated size, `U`, the threshold and the probe distribution with its margin. If the aggregates are `unverified-warm`, the limitation stays open and win-x64 R1/R2 count as `missing`, not as failed.
5. **x64 R3:** the record gives R3 and R3′ per platform and the statement of §4.4 wherever R3 fails. If R3 holds on linux-x64, the record says so and the §4.4 statement is not needed.

## 8. Not decided here

Everything CORE-VERIFY-002 §8 leaves open stays open: the public shape, the manifest gates of an integrity-only mode, a file-handle entry point, 100 GiB files, real network storage, NativeAOT and V3. Also open:

- whether a production path should run with or without pool spinning, read in larger pieces, or read the manifest once;
- the thresholds 1.6 and 0.95 and the two-of-three rule, which this experiment does not revisit;
- the hardware class of the hosted runners, which the lab cannot choose; §3.2 records it, and the record compares it with what CORE-VERIFY-001 and CORE-VERIFY-002 could state.

## 9. Records

Each platform execution gets one RunId `CORE-VERIFY-003/RUN-YYYYMMDD-NNN-<commit>-<platform>`: the UTC date the execution starts, `NNN` the workflow run number, `<commit>` the first seven characters of the measured commit, and platform `linux-x64`, `linux-arm64` or `win-x64`. It keeps its raw run document and oracle report as a CI artifact.

The compact committed dataset goes under `docs/research/results/data/CORE-VERIFY-003-YYYYMMDD/`: `samples.csv` with every sample, `runs.json` with the metadata of the three run documents (including §3.2, the calibration and the uncached reads), `decision.json` and `decision.md` as `decide` wrote them in CI (with the provenance table), and the three oracle reports. Before it is committed, every column of `samples.csv` is compared with the raw documents, row by row.

The result record `docs/research/results/CORE-VERIFY-003-EVIDENCE-YYYYMMDD-NNN.md` follows `RESULT-TEMPLATE.md`. It evaluates §6 against this note, applies §7, gives the provenance table, the environment of each execution, the calibration and the uncached reads, the §4.4 reading, and every `not-resident` and `unverified` sample. The index row, the registry entry and #186 link it.
