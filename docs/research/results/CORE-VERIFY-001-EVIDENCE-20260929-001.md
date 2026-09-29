# CORE-VERIFY-001 — integrity-only verification guided by the manifest

EvidenceId: `CORE-VERIFY-001/EVIDENCE-20260929-001`  
Status: `ADOPT` (the public-shape discussion of #186 opens; no API is added here)  
Owning issue(s): #186, #152  
Implementation PR(s): #188 and #192 (protocol), #191 (lab), #193 (this record)  
Date: 2026-09-29

## Hypothesis

For content paired with a verified manifest, slicing the content at the manifest's record lengths and hashing each slice (V1) is much cheaper per core than re-running CDC (V0). Because records are independent, several workers over record ranges of one large file (V2) beat what a many-file workload already gets from verifying files concurrently.

## Frozen decision rule

From [CORE-VERIFY-001-PROTOCOL.md](../../benchmarks/CORE-VERIFY-001-PROTOCOL.md) §7. The protocol was merged at `5ab5257` (#188) and amended at `6a5020d` (#192), both before any run:

- **R1 (per core):** on `S1` warm and `S10` warm, `GiB per CPU-second (V1) / GiB per CPU-second (V0) ≥ 1.6`.
- **R2:** in warm and throttled mode, the best V2 on the largest single file of the mode (`S10` warm, `S1` throttled) is strictly faster than the best V1 × K on the tree `T`.
- **R3 (#152 guard):** in warm and throttled mode, the best V2-W2 × K on `T` keeps at least 95 % of the best V1 × K.
- **Outcome:**
  - ADOPT when R1, R2 and R3 hold on at least two of three platforms;
  - DEFER when only R1 holds on two;
  - REJECT when R1 fails on two.
  - Any oracle failure means no decision.

## Compared lanes

| Lane | Construction (protocol §1–2) |
| --- | --- |
| V0 | `ChunkManifest.VerifyAsync` over `File.OpenRead` |
| V1 | manifest verified first, then its records read again and hashed from an unbuffered `FileStream`, ranges of ≤ 4 MiB |
| V2-W{2,4,8} | V1's ranges on `min(W, ⌈length / 4 MiB⌉)` pool workers with `RandomAccess` reads; lowest mismatch wins |
| V0/V1/V2-W2 × K | the tree `T`, K = 1/2/4/8 files at a time |

## Included runs

All samples come from workflow run 36515572552 (`verify-lab.yml`, `verify-lab` label on #193). The measured commit is `5ff9db9fad78f0dd72831e23eaaf26b63c2e88b8`, whose lab and protocol are the content that #191 and #192 merged. The runners are GitHub-hosted with 4 vCPUs each, running .NET 10 (JIT, Release).

| RunId | Platform | Samples (single + tree) | Plan fingerprints |
| --- | --- | ---: | --- |
| `CORE-VERIFY-001/RUN-20260929-4-5ff9db9-linux-x64` | `ubuntu-24.04` | 220 + 360 | `46e60cf3…`, `b66ba331…` |
| `CORE-VERIFY-001/RUN-20260929-4-5ff9db9-linux-arm64` | `ubuntu-24.04-arm` | 220 + 360 | `46e60cf3…`, `b66ba331…` |
| `CORE-VERIFY-001/RUN-20260929-4-5ff9db9-win-x64` | `windows-2025` | 145 + 240 (no cold mode) | `8edad6ab…`, `38c7f4ac…` |

The Windows fingerprints differ only because its plan has no cold groups.

Dataset: [data/CORE-VERIFY-001-20260929/](data/CORE-VERIFY-001-20260929/). It holds:
- `samples.csv` with all 1,545 samples;
- `runs.json` with run metadata, environment, workload digests, skipped modes and idle peaks;
- `decision.json` and `decision.md` with the aggregates and rule evaluation, as `verify-lab decide` wrote them;
- the six oracle reports.

The raw CI artifacts of the run (IDs 11012640813, 11011279329, 11012630142, 11013486610, 11013010327, 11012830527, 11012877573) expire on 2026-12-28.

**Workloads:** identical bytes on every platform (the same content digests in `runs.json`):
- `S1`: 1 GiB;
- `S10`: 10 GiB;
- `T`: 1,892 distinct changed-target files of the patch corpus, 855,440,301 bytes.

## Reproduction

```text
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab oracle --fixtures tests/ChunkShift.Tests/Fixtures/CsmV1 --output oracle.json
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab prepare --dir <dir> --workloads S1,S10
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab run --dir <dir> --workloads S1,S10 --modes warm,cold,throttled --platform <p> --output run-single.json
python3 benchmarks/scripts/materialize_patch_corpus.py --root <corpus> --download --lock
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab prepare --dir <dir> --workloads T --corpus <corpus>
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab run --dir <dir> --workloads T --modes warm,cold,throttled --platform <p> --output run-tree.json
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- verify-lab decide --runs <runs> --oracles <oracles> --output decision.json --markdown decision.md
```

Or add the `verify-lab` label to a pull request, or dispatch `verify-lab.yml`.

## Semantic / compatibility checks

- [x] **Oracle (§3) on every platform job (6 × 1,208 cases, 62 CSM vectors): 0 failures.**
  - The only difference is the declared one: 32 D1 verdicts per job, all in the declared form.
  - Every O3/O4 case in every V1/V2 lane went through `slices`; every O5 case went through `cdc-fallback`.
- [x] Every timed sample returned a valid verdict (O1): 0 invalid samples out of 1,545.
- [x] CSM golden vectors only read; no fixture or vector changed.
- [x] Core, ProfileId/ProfileFingerprint, HashSuite, ChunkId, ManifestId and FileDigest unchanged (no `src/` change in #188–#193).
- [x] x64 and ARM64 run.
- [ ] NativeAOT not run; that belongs to the API gates of #186.
- [x] Public API unchanged.

## Results

p50 over the samples; "cores" = CPU / wall. Full tables: `decision.md`.

### R1 — per core, warm (BLAKE3)

| platform | S1 V0 / V1 GiB per CPU-s | ratio | S10 V0 / V1 GiB per CPU-s | ratio | wall-clock ratio S1 / S10 | SHA-256 S1 ratio (informative) |
| --- | --- | ---: | --- | ---: | --- | ---: |
| linux-x64 | 0.646 / 2.728 | **4.22** | 0.636 / 2.878 | **4.53** | 2.33 / 2.44 | 2.94 |
| linux-arm64 | 0.443 / 0.963 | **2.18** | 0.449 / 0.976 | **2.17** | 1.49 / 1.49 | 2.83 |
| win-x64 | 0.545 / 1.196 | **2.20** | 0.374 / 0.986 | **2.64** | 1.85 / (I/O-bound) | 2.29 |

**R1 holds on all three platforms as measured.** See the thread-pool caveat below: V0 is charged about 0.5–1 core of pool spinning, so the per-core ratios above overstate the pure CDC saving.

### R2 — one large file vs file-level concurrency

Throughput in GiB/s. The first number in each cell is the best V2 on the single file; the second is the best V1 × K on the tree.

| platform | warm: best V2 on S10 vs best V1 × K on T | throttled: best V2 on S1 vs best V1 × K on T | R2 |
| --- | --- | --- | --- |
| linux-x64 | **8.15** (W4) vs 3.67 (K4) | **0.635** (W8) vs 0.165 (K8) | holds |
| linux-arm64 | **3.93** (W4) vs 2.21 (K4) | **0.634** (W8) vs 0.165 (K8) | holds |
| win-x64 | 0.380 (every lane) vs 1.66 (K4) | **0.626** (W8) vs 0.165 (K8) | **fails** |

### R3 — V2 does not cost aggregate throughput on the tree

| platform | warm: best V2-W2 × K / best V1 × K | throttled | R3 |
| --- | ---: | ---: | --- |
| linux-x64 | 0.994 (3.649 / 3.672) | 1.357 (0.225 / 0.165) | holds |
| linux-arm64 | 0.981 (2.164 / 2.205) | 1.354 (0.224 / 0.165) | holds |
| win-x64 | 1.047 (1.735 / 1.657) | 1.347 (0.222 / 0.165) | holds |

### Other observations (not gated)

- **Scaling on one file.** Warm V2 scales to the 4 vCPUs:
  - linux-x64 S10: 3.09 → 6.01 → 8.15 GiB/s for V1 → W2 → W4, at 1.07 → 2.27 → 3.93 cores;
  - linux-arm64 S10: 0.99 → 2.00 → 3.93 GiB/s.
  - W8 adds nothing on 4 vCPUs.
  - V2 costs some of V1's GiB per CPU-second on x64 (−7 to −8 % at W2, −25 to −28 % at W4), where BLAKE3 is fast enough for the worker overhead and memory traffic to show; on arm64 the change stays within 1 %.
- **Cold (Linux, informative).** Every lane runs at the runner disk's speed:
  - S10 0.29–0.36 GiB/s on x64 and 0.39 GiB/s on arm64; S1 0.31–0.43 GiB/s;
  - V2 cannot help on one throttled device;
  - V1 does it at 0.14–0.46 cores against V0's 0.56–1.08.
- **Throttled.** One sequential reader is at the model's 83 MiB/s (0.081 GiB/s) in V0 and V1; V2-W8 reaches 0.63 GiB/s (7.8×).
  - On the tree, V1 × K is slower than V0 × K (0.165 vs 0.203 GiB/s at K8). V1 makes more requests per file than V0: its second manifest pass (protocol §2 rule 4) and the one-byte read that checks for trailing bytes. Most tree files fit in one request, so latency dominates. V2-W2 × K (0.225 GiB/s) checks the file length instead of reading past the end.
- **Memory (peak working set over idle).**
  - Single file: V0 15–33 MiB, V1 31–64 MiB, V2-W8 86–117 MiB. This is bounded as designed: W buffers of 4 MiB plus pool retention.
  - Tree, warm, K8: V0 50–88 MiB, V1 192–233 MiB, V2-W2 222–300 MiB. `ArrayPool` keeps a range buffer per size class and per core; this stays bounded, but it is the largest cost of the lab construction.
- **Dispersion.** Warm S1 p95/p50 is at most 1.06 on Linux; on Windows it is 1.26 for V1 (0.821 / 1.036 s).

## Caveats that bear on the decision

1. **V0 is charged for thread-pool spinning.**
   - On linux-x64, V0 shows 2.0 effective cores although it is sequential (arm64 1.5, Windows 1.2); V1 shows 1.0–1.1.
   - A local control on a 4-vCPU x64 VM (S1 warm) points to thread-pool worker spin-waiting between V0's 64 KiB asynchronous reads: with `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`, V0 fell from 1.6 to 1.09 cores at the same wall time (1.19 vs 1.23 s), and V1 was unchanged.
   - The spinning is real CPU that a consumer calling `ChunkManifest.VerifyAsync` on a `FileStream` pays today, and the frozen rule measures process CPU, so R1 stands as evaluated.
   - Without it, the per-core ratio is closer to the wall-clock ratio adjusted by V0's ≈ 1.09 cores:
     - linux-x64 ≈ 2.3 (holds);
     - win-x64 S1 ≈ 2.0 (holds);
     - linux-arm64 ≈ 1.6 (at the threshold).
2. **`S10` "warm" was not warm on Windows.**
   - Every lane ran at 0.38 GiB/s with 0.37–0.48 cores: the 10 GiB file did not stay in the `windows-2025` runner's file cache after the pre-read, so the lanes were disk-bound.
   - V0 was slower (0.133 GiB/s) because its 64 KiB reads are small for an uncached file on that disk.
   - R2 fails on Windows for this reason, not because file-level concurrency beat V2. On S1 warm, which did stay cached, V2-W4 reached 2.98 GiB/s against the tree's best 1.66.
   - The rule is applied as frozen; the Windows row counts as failing.
3. GitHub-hosted runners share hardware, and the "NVMe" lane is the runner's managed disk.
4. The throttled source is a model (protocol §5).
5. No 100 GiB file, real network storage or NativeAOT was measured (protocol §8).

## Exclusions / invalid runs

None. Every sample of the run is included, and no outliers were removed. The earlier workflow runs on #191 and #193 were smoke runs on 8–48 MiB stand-ins; they are not decision data.

## Decision

```text
ADOPT
```

R1, R2 and R3 hold on linux-x64 and linux-arm64; on win-x64, R1 and R3 hold and R2 fails (caveat 2). Two of three platforms satisfy the rule, so the protocol's outcome is ADOPT: the public-shape discussion of #186 (plan step 4) opens.

The strength of the evidence differs by platform:
- linux-x64 passes every rule with a wide margin, even on wall clock;
- linux-arm64 passes R1 by a margin that depends on how V0's pool spinning is counted (caveat 1);
- win-x64's R2 failure is an environment effect (caveat 2).

## Consequences

- **No code change follows from this record.** ADOPT only opens the discussion; a public shape still needs a named consumer (#186 plan step 3):
  - the engineering CLI `verify --integrity-only`;
  - update-set verification (#184).
- **Inputs for the shape discussion:**
  - the profile question (protocol §2 rule 6 and D1): an integrity-only verdict must say the profile was not checked;
  - a single manifest pass. The two-pass manifest read costs one request per file on high-latency sources, the one place V1 lost to V0; a production V1 should hash while it reads the records and fall back only when the manifest turns out invalid;
  - read size. V0's 64 KiB reads cost CPU in pool scheduling and disk throughput when uncached; V1's 4 MiB ranges did not;
  - V2's value is confined to large files on fast storage or high-latency sources, and to machines with spare cores. R3 shows it does not cost the many-file case.
- **Follow-up (new ExperimentId if it is run):**
  - V0 and V1 with thread-pool spinning disabled, to settle caveat 1 on arm64;
  - an S10 warm lane sized to the Windows runner's cache.
- Index and registry: `CORE-VERIFY-001` → ADOPT.

## References

- #186 (CORE-VERIFY-001, decision of 2026-09-28), #152 (multi-file lane), #127 (hash share of the scan), #153;
- [CORE-VERIFY-001-PROTOCOL.md](../../benchmarks/CORE-VERIFY-001-PROTOCOL.md);
- lab: `benchmarks/ChunkShift.Benchmarks/VerifyLab/`, workflow `.github/workflows/verify-lab.yml`.
