# CORE-VERIFY-002 — confirmatory run of CORE-VERIFY-001 without pool spinning and with a cache-sized warm file

EvidenceId: `CORE-VERIFY-002/EVIDENCE-20260929-001`  
Status: `DEFER` (CORE-VERIFY-001 becomes `SUPERSEDED`; no public shape is discussed on this evidence)  
Owning issue(s): #186, #152  
Implementation PR(s): #197 (protocol), #198 (lab), #199 (this record)  
Date: 2026-09-29

## Hypothesis

The ADOPT of [CORE-VERIFY-001](CORE-VERIFY-001-EVIDENCE-20260929-001.md) still holds when:

- no lane is charged for thread-pool spinning (every gated sample runs with `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`);
- the warm large file stays in the runner's file cache: `SL` is sized to 0.4 × the runner's memory, and every pre-read sample is checked for residency.

## Frozen decision rule

From [CORE-VERIFY-002-PROTOCOL.md](../../benchmarks/CORE-VERIFY-002-PROTOCOL.md) §6 and §7, merged at `cc639d2` (#197) before any run. The rules are those of CORE-VERIFY-001, with `SL` in the place of `S10`. They are evaluated on the BLAKE3 lanes of the `spin-0` matrix, over the `resident` samples only (§4.2):

- **R1 (per core):** on `S1` warm and `SL` warm, `GiB per CPU-second (V1) / GiB per CPU-second (V0) ≥ 1.6`.
- **R2:** in warm and throttled mode, the best V2 on the largest single file of the mode (`SL` warm, `S1` throttled) is strictly faster than the best V1 × K on the tree `T`.
- **R3 (#152 guard):** in warm and throttled mode, the best V2-W2 × K on `T` keeps at least 95 % of the best V1 × K.
- **Residency (§4.2):** an aggregate with fewer than half of its planned samples `resident` is `unverified-warm`. Every rule that needs it is `missing` on that platform, and a `missing` rule does not hold.
- **Outcome:**
  - ADOPT when R1, R2 and R3 hold on at least two of three platforms;
  - DEFER when R1 holds on at least two platforms but the ADOPT condition fails;
  - REJECT when R1 fails on at least two.
  - Any oracle failure means no decision.
- **Effect on CORE-VERIFY-001 (§7, fixed in advance):** ADOPT confirms it; DEFER or REJECT weaken it, and CORE-VERIFY-001 becomes `SUPERSEDED` by this record.

## Compared lanes

| Lane | Construction (CORE-VERIFY-001 protocol §1–2, unchanged) |
| --- | --- |
| V0 | `ChunkManifest.VerifyAsync` over `File.OpenRead` |
| V1 | manifest verified first, then its records read again and hashed from an unbuffered `FileStream`, ranges of ≤ 4 MiB |
| V2-W{2,4,8} | V1's ranges on `min(W, ⌈length / 4 MiB⌉)` pool workers with `RandomAccess` reads; lowest mismatch wins |
| V0/V1/V2-W2 × K | the tree `T`, K = 1/2/4/8 files at a time |

Pool settings: `spin-0` (gated, `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`) and `default` (informative, variable absent; V0 and V1 on `S1` and `SL` warm only). Every one of the 1,635 sample processes reported the pool setting it was planned with.

## Included runs

All samples come from workflow run 36528577444 (`verify-lab.yml`, `verify-lab` label on #199). The measured commit is `56e49e2ba2886207e8158d949602128303056bf4`; its lab and protocol are identical to `main` at `95361af` (#198). The jobs checked out the pull-request merge commit `cd9755f`, whose tree equals `56e49e2`; the run documents record `56e49e2`.

The runners are GitHub-hosted with 4 vCPUs each, running .NET 10.0.12 (JIT, Release). They reported 16,766,414,848 bytes of memory on linux-x64, 16,722,010,112 (single) and 16,722,046,976 (tree) on linux-arm64, and 17,174,360,064 on win-x64. All three memory sizes give `SL` = 6 GiB (6,442,450,944 bytes), as protocol §4.1 expected.

**Each RunId covers two executions.** The workflow runs the single-file matrix (`S1`, `SL`) and the tree matrix (`T`) of a platform as two separate hosted jobs. Protocol §9 and the workflow give one RunId per platform, so both jobs emit the same RunId string. In this run, a RunId string is therefore not unique per execution: an execution is identified by RunId + job kind (`single`/`tree`) + job id. The run documents are kept as emitted and are not relabelled. The next experiment fixes this (see Consequences).

| RunId | Job kind | Job id | Runner | Image | OS (run document) | Samples | Plan fingerprint |
| --- | --- | --- | --- | --- | --- | ---: | --- |
| `CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-x64` | single | 109276915986 | 1000019691 | `ubuntu-24.04` | Ubuntu 24.04.5 LTS, X64 | 250 | `f7fdb5a7…` |
| `CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-x64` | tree | 109276915960 | 1000019690 | `ubuntu-24.04` | Ubuntu 24.04.5 LTS, X64 | 360 | `b5f7201b…` |
| `CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-arm64` | single | 109276916050 | 1000019692 | `ubuntu-24.04-arm` | Ubuntu 24.04.5 LTS, Arm64 | 250 | `f7fdb5a7…` |
| `CORE-VERIFY-002/RUN-20260929-14-56e49e2-linux-arm64` | tree | 109276915983 | 1000019686 | `ubuntu-24.04-arm` | Ubuntu 24.04.5 LTS, Arm64 | 360 | `b5f7201b…` |
| `CORE-VERIFY-002/RUN-20260929-14-56e49e2-win-x64` | single | 109276916043 | 1000019687 | `windows-2025` | Windows 10.0.26100, X64 | 175 (no cold mode) | `7cee96c9…` |
| `CORE-VERIFY-002/RUN-20260929-14-56e49e2-win-x64` | tree | 109276915947 | 1000019688 | `windows-2025` | Windows 10.0.26100, X64 | 240 (no cold mode) | `dc3471df…` |

The decision was evaluated by job `verify-lab-decide` (id 109292395366, runner 1000019712). The Windows fingerprints differ only because its plans have no cold groups.

Dataset: [data/CORE-VERIFY-002-20260929/](data/CORE-VERIFY-002-20260929/). It holds:
- `samples.csv` with all 1,635 samples, including the pool setting each process saw, the residency verdict, the probe throughput and, on Linux, the `mincore` resident fraction;
- `runs.json` with the metadata of the six run documents (`file` names the job kind), environment, memory, plan, workload digests and skipped modes;
- `decision.json` and `decision.md` with the aggregates and rule evaluation, byte-identical to what `verify-lab decide` wrote in CI;
- the six oracle reports.

The raw CI artifacts of the run expire on 2026-12-28:

| artifact | id |
| --- | --- |
| single linux-x64 / linux-arm64 / win-x64 | 11015944847 / 11015929984 / 11016991008 |
| tree linux-x64 / linux-arm64 / win-x64 | 11016892758 / 11017152142 / 11017856018 |
| decision | 11017772606 |

**Workloads:** identical bytes on every platform (the same content digests in `runs.json`):
- `S1`: 1 GiB;
- `SL`: 6 GiB (seed `0xC0FE0020`);
- `T`: 1,892 distinct changed-target files of the patch corpus, 855,440,301 bytes.

## Reproduction

```text
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab oracle --fixtures tests/ChunkShift.Tests/Fixtures/CsmV1 --output oracle.json
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab prepare --dir <dir> --workloads S1,SL
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab run --dir <dir> --workloads S1,SL --modes warm,cold,throttled --platform <p> --output run-single.json
python3 benchmarks/scripts/materialize_patch_corpus.py --root <corpus> --download --lock
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab prepare --dir <dir> --workloads T --corpus <corpus>
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab run --dir <dir> --workloads T --modes warm,cold,throttled --platform <p> --output run-tree.json
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab decide --runs <runs> --oracles <oracles> --output decision.json --markdown decision.md
```

Or add the `verify-lab` label to a pull request, or dispatch `verify-lab.yml`.

## Semantic / compatibility checks

- [x] **Oracle (CORE-VERIFY-001 §3) on every platform job (6 × 1,208 cases, 62 CSM vectors): 0 failures.** Every report is a full run (not quick) and passed.
- [x] Every timed sample returned a valid verdict (O1): 0 invalid samples out of 1,635.
- [x] Every gated sample process saw `spin-0`, and every informative one saw `default`.
- [x] CSM golden vectors only read; no fixture or vector changed.
- [x] Core, ProfileId/ProfileFingerprint, HashSuite, ChunkId, ManifestId and FileDigest unchanged (no `src/` or `tests/` change in #197–#199).
- [x] x64 and ARM64 run.
- [ ] NativeAOT not run; that belongs to the API gates of #186.
- [x] Public API unchanged.

**Provenance.** `verify-lab decide` does not itself check that its inputs belong together. Before the dataset was built, a fail-closed import check ran over the six raw run documents and six oracle reports. It passed **71 of 71 checks**:

| # | check | count |
| --- | --- | ---: |
| 1 | exactly six run documents; one `single` and one `tree` document per platform in {linux-x64, linux-arm64, win-x64} | 3 |
| 2 | per document: `experimentId = CORE-VERIFY-002`, schema `chunkshift.verify-lab-run.v2`, `smoke = false`, commit `56e49e2…`, `platform` matching the file, RunId matching commit and platform, every sample measured | 6 × 7 = 42 |
| 3 | per document: one RunId string per platform across its `single` and `tree` documents | 6 |
| 4 | per job kind: Linux plan fingerprints equal, Linux plans equal, Windows plan equal to the Linux plan without its cold groups, only Windows skipped cold mode | 2 × 4 = 8 |
| 5 | `S1` and `T` bytes and content digest equal on all platforms; `SL` digest equal where its size is equal (one size, 6,442,450,944 bytes) | 3 |
| 6 | exactly six oracle reports, each full and passed with 1,208 cases and 62 vectors | 1 + 6 = 7 |
| 7 | `verify-lab decide`, rerun locally at `56e49e2` on the six raw documents, reproduces the CI `decision.json`; its `experimentId` is `CORE-VERIFY-002` | 2 |

The local rerun of `decide`, given the documents in the CI order, also reproduces `decision.md` byte for byte. Check 3 confirms the RunId collision described above.

## Results

p50 over the admitted samples; "cores" = CPU / wall. Full tables: `decision.md`.

### R1 — per core, warm (BLAKE3, `spin-0`)

| platform | S1 V0 / V1 GiB per CPU-s | ratio | SL V0 / V1 GiB per CPU-s | ratio | R1 |
| --- | --- | ---: | --- | ---: | --- |
| linux-x64 | 0.656 / 1.274 | **1.943** | 0.656 / 1.311 | **1.997** | holds |
| linux-arm64 | 0.608 / 0.973 | **1.600** | 0.613 / 0.986 | **1.607** | holds |
| win-x64 | 0.582 / 1.143 | **1.964** | `unverified-warm` (0 of 5 resident) | — | **missing** |

**R1 holds on two platforms; on win-x64 it is `missing` because `SL` warm is `unverified-warm` (see caveat 2).**

**linux-arm64 R1 in full precision.** The rendered 1.600 is too close to the bound to read from three decimals. The evaluator's values, from `decision.json`:

| | V0 p50 GiB per CPU-s | V1 p50 GiB per CPU-s | ratio | margin over 1.6 |
| --- | --- | --- | --- | --- |
| S1 warm | 0.6082393975057099 | 0.9732244627248959 | 1.600068109227929 | +0.0043 % |
| SL warm | 0.6132983317160997 | 0.9858683980706555 | 1.60748586305795 | +0.47 % |

The per-sample values (GiB per CPU-second = bytes / 2³⁰ / CPU seconds, from `samples.csv`), sorted:

- S1 V0 (10): 0.599272, 0.600946, 0.606292, 0.607108, 0.607762, 0.608716, 0.609562, 0.612000, 0.614595, 0.616223;
- S1 V1 (10): 0.962608, 0.966335, 0.970626, 0.971659, 0.972691, 0.973758, 0.974261, 0.974866, 0.975359, 0.976298;
- SL V0 (5): 0.611572, 0.613262, 0.613298, 0.617383, 0.621719;
- SL V1 (5): 0.980968, 0.985490, 0.985868, 0.986252, 0.987754.

The S1 ratio is above 1.6 by 6.8 × 10⁻⁵; a 0.0043 % change in either median would flip it. R1 holds on linux-arm64 as frozen, but only just: on this platform, without pool spinning, V1 saves 1.6× per core, not the 2.2× of CORE-VERIFY-001.

### Cost of pool spinning (informative, §3)

V0 and V1 on warm files with the runtime's default spinning, next to the gated samples:

| platform | workload | V0 cores spin-0 / default | V0 GiB per CPU-s spin-0 / default | V1 cores spin-0 / default | V1 GiB per CPU-s spin-0 / default | R1 ratio spin-0 / default |
| --- | --- | --- | --- | --- | --- | --- |
| linux-x64 | S1 | 1.145 / 1.752 | 0.656 / 0.421 | 1.018 / 1.046 | 1.274 / 1.227 | 1.943 / 2.913 |
| linux-x64 | SL | 1.125 / 1.774 | 0.656 / 0.444 | 1.012 / 1.039 | 1.311 / 1.268 | 1.997 / 2.856 |
| linux-arm64 | S1 | 1.069 / 1.493 | 0.608 / 0.433 | 1.008 / 1.020 | 0.973 / 0.939 | 1.600 / 2.168 |
| linux-arm64 | SL | 1.058 / 1.487 | 0.613 / 0.444 | 1.006 / 1.019 | 0.986 / 0.957 | 1.607 / 2.157 |
| win-x64 | S1 | 1.028 / 1.170 | 0.582 / 0.489 | 1.001 / 0.998 | 1.143 / 1.068 | 1.964 / 2.186 |

- Spinning costs V0 0.4–0.65 cores on Linux and 0.14 on Windows at about the same wall time; V1 is almost unchanged (≤ 0.04 cores).
- With default spinning, linux-arm64's R1 ratio would be ≈ 2.17, the value CORE-VERIFY-001 measured. The difference between 001's 2.18 and this run's 1.600 is the spinning, as caveat 1 of 001 predicted.
- win-x64 `SL` default is `unverified-warm` like its `spin-0` group and is left out.

### R2 — one large file vs file-level concurrency (cross-runner)

Throughput in GiB/s. The first number in each cell is the best V2 on the single file (from the `single` job); the second is the best V1 × K on the tree (from the `tree` job). **The two operands of every R2 comparison come from different hosted runners** (for example, on linux-x64: job 109276915986 on runner 1000019691 against job 109276915960 on runner 1000019690). R2 therefore compares absolute throughputs across two VMs of the same image, not within one machine.

| platform | warm: best V2 on SL vs best V1 × K on T | ratio | throttled: best V2 on S1 vs best V1 × K on T | ratio | R2 |
| --- | --- | ---: | --- | ---: | --- |
| linux-x64 | **3.338** (W4) vs 2.011 (K8) | 1.66 | **0.634** (W8) vs 0.165 (K8) | 3.84 | holds |
| linux-arm64 | **3.875** (W4) vs 2.172 (K8) | 1.78 | **0.634** (W8) vs 0.165 (K8) | 3.84 | holds |
| win-x64 | `unverified-warm` (0.418 W8 as measured, not admitted) vs 1.780 (K8) | — | **0.627** (W8) vs 0.165 (K8) | 3.80 | **missing** |

The throttled comparison is set by the throttle model (83 MiB/s per request stream), not by the runner, and its margin is 3.8×. The warm margins are 1.66× (linux-x64) and 1.78× (linux-arm64). On linux-arm64 the same lanes moved by less than 2 % between runner sets (below), well inside the margin. On linux-x64 they moved by more than 2×, which is more than the margin. Neither margin proves an intra-file advantage independent of runner hardware, and the linux-x64 one least of all.

**How much the same lanes moved between runner sets.** CORE-VERIFY-001 measured the same lanes at `5ff9db9` on another set of hosted runners ([001 `decision.md`](data/CORE-VERIFY-001-20260929/decision.md)). The lab is the same except for the pool setting and the size of the large warm file:

| platform | lane | 001 (default spinning) | 002 (`spin-0`) |
| --- | --- | ---: | ---: |
| linux-x64 | S1 warm V1 | 3.030 | 1.297 |
| linux-x64 | large file warm V1 (S10 / SL) | 3.087 | 1.326 |
| linux-x64 | large file warm best V2 (S10 / SL, W4) | 8.148 | 3.338 |
| linux-x64 | T warm best V1 × K | 3.672 (K4) | 2.011 (K8) |
| linux-x64 | S1 throttled best V2 (W8) | 0.635 | 0.634 |
| linux-x64 | T throttled best V1 × K (K8) | 0.165 | 0.165 |
| linux-arm64 | S1 warm V1 | 0.983 | 0.981 |
| linux-arm64 | large file warm best V2 (W4) | 3.930 | 3.875 |
| linux-arm64 | T warm best V1 × K | 2.205 (K4) | 2.172 (K8) |
| win-x64 | S1 warm V1 | 1.218 | 1.135 |
| win-x64 | T warm best V1 × K | 1.657 (K4) | 1.780 (K8) |

- On linux-arm64 and win-x64 the warm lanes moved by less than 8 % between runner sets.
- On linux-x64 the runners of this run hashed at less than half the speed of 001's: V1 on S1 fell from 3.03 to 1.30 GiB/s, and the tree's best V1 × K from 3.67 to 2.01. Spinning does not explain this: V1 runs at about one core with either pool setting. It points to a different, slower class of x64 host, but the run documents do not record the CPU model, so this cannot be confirmed (see Limitations).
- The throttled operands are identical across both runs, as the model makes them.

### R3 — V2 does not cost aggregate throughput on the tree

Both operands come from the same `tree` job.

| platform | warm: best V2-W2 × K / best V1 × K | throttled | R3 |
| --- | ---: | ---: | --- |
| linux-x64 | **0.916** (1.843 K4 / 2.011 K8) | 1.354 (0.224 / 0.165) | **fails** |
| linux-arm64 | 0.973 (2.114 K4 / 2.172 K8) | 1.354 (0.224 / 0.165) | holds |
| win-x64 | 1.003 (1.785 K4 / 1.780 K8) | 1.349 (0.222 / 0.165) | holds |

On linux-x64, V2-W2 × K leads V1 × K at K = 1 and 2 (0.893 vs 0.812, 1.505 vs 1.299 GiB/s) and falls behind at K = 4 and 8 (1.843 vs 1.937, 1.781 vs 2.011), where the four vCPUs are busy (3.2–3.7 cores). There V2-W2 gets 13 % fewer GiB per CPU-second than V1 (0.493 vs 0.567 at K8). In CORE-VERIFY-001 the same comparison was 0.994 on faster x64 runners with default spinning. This run cannot say whether the failure comes from `spin-0` (workers that block at once pay a wake-up on every hand-off) or from the slower hardware: the tree has no `default` pool lane. See Consequences.

### Residency (§4.2)

**Linux.** Every one of the 830 pre-read samples is `resident`, with a `mincore` resident fraction of 1.000 in every sample. The probes of these resident files ran at:
- linux-x64: `SL` warm 12.9–16.5 GiB/s, `S1` warm 10.9–16.3, `T` warm 2.5–14.3 (p50 13.3);
- linux-arm64: `SL` warm 10.7–15.1, `S1` warm 8.5–14.9, `T` warm 11.6–13.0.

The lowest Linux probe, 2.51 GiB/s (linux-x64, `T` warm, V0 × 8, repetition 9), came from a sample that `mincore` reports fully resident. A resident probe can therefore dip well below its usual band. Cold reads on the same runners ran at 0.39–0.43 GiB/s (p50 of `S1` and `SL` cold).

**Windows.** The residency verdict comes from the probe alone (≥ 1.5 GiB/s), and it is inferred, not measured.

| workload, mode, pool | samples | probe min / p50 / max GiB/s | margin to 1.5 | verdict |
| --- | ---: | --- | --- | --- |
| `SL` warm, spin-0 | 25 | 0.39 / 0.54 / 0.81 | max 0.54× the threshold | 25 `not-resident` |
| `SL` warm, default | 10 | 0.48 / 0.55 / 0.65 | max 0.43× | 10 `not-resident` |
| `S1` warm, spin-0 | 70 | 6.42 / 7.74 / 8.08 | min 4.3× | 70 `resident` |
| `S1` warm, default | 20 | 6.40 / 7.64 / 8.01 | min 4.3× | 20 `resident` |
| `S1` throttled, spin-0 | 50 | 6.82 / 7.87 / 8.16 | min 4.5× | 50 `resident` |
| `T` warm, spin-0 | 120 | 4.05 / 4.69 / 5.32 | min 2.7× | 120 `resident` |
| `T` throttled, spin-0 | 120 | 1.99 / 4.64 / 5.55 | min 1.3× | 120 `resident` |

- **The check separated the two populations.** The highest `SL` probe (0.81) is 5× below the lowest `T` warm probe (4.05) and 7.9× below the lowest `S1` probe (6.42); the p50s differ by 8.7× (`T`) and 14× (`S1`). Only one resident Windows probe came near the threshold: 1.99 GiB/s (`T` throttled, V2-W2 × 8, repetition 1); the next lowest is 3.87.
- **The `SL` probes sit at the disk's rate.** They lie near the 0.38 GiB/s at which CORE-VERIFY-001's `S10` ran on `windows-2025`, and the timed `SL` "warm" lanes of this run themselves ran at 0.38–0.46 GiB/s for V1 and V2 (V0 0.20–0.23). That is the behaviour of a disk-bound file, not a cached one.
- **This run has no uncached Windows measurement of its own.** The protocol has no cold mode on Windows, and the storage step only lists the disks. The 0.38 GiB/s of CORE-VERIFY-001 comes from other runners and is context, not an in-run reference.
- **Conclusion:** the 6 GiB `SL` file did not stay in the `windows-2025` runner's file cache after its pre-read, although the runner reported 16 GiB of memory. Sizing `SL` to 0.4 × memory was not enough on Windows.

**All 35 `not-resident` samples** are win-x64 `SL` warm, i.e. every pre-read `SL` sample on Windows. None is `unverified`. In `samples.csv`, filter `platform = win-x64`, `workload = SL`, `mode = warm`:

| pool | lane | repetitions (probe GiB/s) |
| --- | --- | --- |
| spin-0 | V0 | 0 (0.398), 1 (0.738), 2 (0.812), 3 (0.465), 4 (0.688) |
| spin-0 | V1 | 0 (0.388), 1 (0.604), 2 (0.484), 3 (0.632), 4 (0.541) |
| spin-0 | V2-W2 | 0 (0.389), 1 (0.472), 2 (0.479), 3 (0.622), 4 (0.543) |
| spin-0 | V2-W4 | 0 (0.673), 1 (0.510), 2 (0.617), 3 (0.662), 4 (0.530) |
| spin-0 | V2-W8 | 0 (0.532), 1 (0.601), 2 (0.458), 3 (0.613), 4 (0.489) |
| default | V0 | 0 (0.646), 1 (0.550), 2 (0.551), 3 (0.504), 4 (0.628) |
| default | V1 | 0 (0.477), 1 (0.597), 2 (0.552), 3 (0.506), 4 (0.567) |

All 35 are valid samples (O1) and stay in the dataset; §4.2 keeps them out of the rules.

### Other observations (not gated)

- **Scaling on one file.** Warm V2 scales to the 4 vCPUs as in CORE-VERIFY-001:
  - linux-x64 SL: 1.33 → 2.57 → 3.34 GiB/s for V1 → W2 → W4, at 1.01 → 2.06 → 3.95 cores;
  - linux-arm64 SL: 0.99 → 1.99 → 3.88 GiB/s.
  - W8 adds nothing on 4 vCPUs. On x64, W4 costs 36 % of V1's GiB per CPU-second (0.845 vs 1.311); on arm64 the change stays within 1 %.
- **Cold (Linux, informative).** Every lane runs at the disk's speed (`S1` 0.43, `SL` 0.39 GiB/s); V1 does it at 0.33–0.46 cores against V0's 0.66–0.78.
- **Throttled.** As in CORE-VERIFY-001: V0 and V1 at 0.081 GiB/s on `S1`, V2-W8 at 0.63 GiB/s (7.8×). On the tree, V1 × K stays slower than V0 × K (0.165 vs 0.202–0.203 GiB/s at K8) for the reason 001 gives (the second manifest pass and the trailing-byte read).
- **SHA-256 (informative).** S1 warm V1/V0 GiB per CPU-second: linux-x64 1.93, linux-arm64 1.91, win-x64 1.97.

## Exclusions / invalid runs

None. Every sample of the run is included in the dataset, and no outliers were removed. The 35 `not-resident` samples are kept out of the rules by the frozen §4.2, not excluded from the record. The earlier smoke runs of the lab on #198 are not decision data.

## Limitations

1. **RunId per execution.** Each RunId string covers two hosted jobs (above). The record tells them apart by job kind and job id; the run documents themselves cannot.
2. **R2 is cross-runner.** Its warm operands are absolute throughputs from two different VMs. The run documents record OS, architecture, runtime and processor count, but not the CPU model or the storage device, so the two hosts cannot be shown to be equivalent. The x64 runners of this run were more than 2× slower than 001's on the same lanes.
3. **Windows residency is inferred.** Windows has no public call for cache residency, and the run has no in-run uncached read to compare the probe against. The fixed 1.5 GiB/s threshold is absolute; faster runner storage could make an uncached probe pass it.
4. **`verify-lab decide` does not check provenance.** The checks above were run at import, outside the lab.
5. GitHub-hosted runners share hardware, and the "NVMe" lane is the runner's managed disk. The throttled source is a model.
6. No 100 GiB file, real network storage or NativeAOT was measured (CORE-VERIFY-001 protocol §8).

## Decision

```text
DEFER
```

| platform | oracle | R1 | R2 | R3 |
| --- | --- | --- | --- | --- |
| linux-arm64 | passed | holds (1.600068 / 1.607) | holds | holds |
| linux-x64 | passed | holds | holds | **fails** (warm 0.916) |
| win-x64 | passed | **missing** (`SL` warm `unverified-warm`) | **missing** (warm) | holds |

R1 holds on two platforms (linux-x64, linux-arm64), so the result is not REJECT. R1, R2 and R3 together hold on one platform only (linux-arm64): R3 fails on linux-x64, and R1 and R2 are `missing` on win-x64. The ADOPT condition fails, and the frozen outcome is **DEFER**. This is the value of `decision.json` from CI, reproduced locally.

**Caveats of CORE-VERIFY-001 (§7):**

1. **Caveat 1 (pool spinning): settled on Linux, not on all three platforms.** R1 was evaluated from `spin-0` samples on linux-x64 and linux-arm64. On linux-arm64 it holds at 1.600068 on S1 and 1.607 on SL, a margin of 0.0043 % on S1; V0 used 1.07 cores with `spin-0` and 1.49 with default spinning. On win-x64, R1 is `missing` because of `SL`, so the §7 condition "R1 evaluated on all three platforms" is met only in part. The win-x64 `S1` half of R1 (1.964) was evaluated.
2. **Caveat 2 (Windows large file not warm): not settled.** win-x64 `SL` warm is `unverified-warm` (0 of 5 samples resident in every lane). The probes show that the 6 GiB file was read from disk. win-x64 R2 warm counts as `missing`, not as failed.

## Consequences

- **CORE-VERIFY-001 becomes `SUPERSEDED` by this record** (protocol §7, DEFER row). Its ADOPT no longer governs; this DEFER does. The 001 protocol and record stay as they are.
- **The public-shape discussion of #186 (plan step 4) does not continue on this evidence.** No public shape is proposed; the lanes V0–V2 stay lab code in `benchmarks/ChunkShift.Benchmarks/VerifyLab/`, and #186 records the numbers.
- **No code change follows from this record.**
- **Follow-up (a new ExperimentId, with its protocol frozen before runs):**
  - one RunId per execution: distinct `single`/`tree` RunIds with both environments kept, or both matrices in one job;
  - the operands of R2 measured on the same runner;
  - a fail-closed provenance check inside `verify-lab decide` (the checks above: experiment, schema, commit, platform set, plan and workload fingerprints, full oracles);
  - an in-run uncached read on Windows (for example with `FILE_FLAG_NO_BUFFERING`) as the reference for the residency probe, and the CPU model and storage device in the run documents;
  - a warm large file that stays cached on Windows: a smaller `SL` or another mechanism to keep it resident, decided in the protocol;
  - an analysis of the linux-x64 R3 failure: run the tree with default spinning as well as `spin-0`, to separate the pool setting from the runner hardware.
- Index and registry: `CORE-VERIFY-002` → DEFER; `CORE-VERIFY-001` → SUPERSEDED.

## References

- #186 (CORE-VERIFY-001 result comment of 2026-09-29, "Follow-up"), #152 (multi-file lane);
- [CORE-VERIFY-002-PROTOCOL.md](../../benchmarks/CORE-VERIFY-002-PROTOCOL.md), [CORE-VERIFY-001-PROTOCOL.md](../../benchmarks/CORE-VERIFY-001-PROTOCOL.md);
- superseded: `CORE-VERIFY-001/EVIDENCE-20260929-001`, [CORE-VERIFY-001-EVIDENCE-20260929-001](CORE-VERIFY-001-EVIDENCE-20260929-001.md);
- lab: `benchmarks/ChunkShift.Benchmarks/VerifyLab/`, workflow `.github/workflows/verify-lab.yml`.
