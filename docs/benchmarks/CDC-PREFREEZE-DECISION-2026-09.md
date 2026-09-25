# CDC pre-freeze decision note (#99) — 2026-09

Status: evidence and recommendation for [#8](https://github.com/definitely-stable/ChunkShift/issues/8); **not complete** until the real-corpus part in [§10](#10-exit-criteria-status) are done
Issue: [#99](https://github.com/definitely-stable/ChunkShift/issues/99)
Baseline: `main` at `9c6e172` (#98 local-state/split-loop scan and #101 single-buffer kernel are merged); persisted semantics unchanged
Local environment for the numbers below: one x64 VM (Intel Xeon @ 2.80 GHz, 4 vCPU), Ubuntu 24.04, .NET 10.0.12 runtime. Single-machine numbers are sanity checks, not release evidence. The CI jobs added here produce the x64/arm64 evidence.

## Recommendation

1. **Keep `fastcdc.gear.chunkshift.v1` semantics (zero Gear state at `Minimum`, prefix `[0, Minimum)` unhashed) as the stable semantic base.** Do not adopt the warmed-prefix variant.
2. The difference between the two semantics is limited to the first **47** tested positions after `Minimum` for every release-candidate preset (§2). On the synthetic plan, the two produce identical chunk sequences in 1,229 of 1,254 (corpus, target, mutation) cells. The 25 remaining cells come from one shifted boundary, and their reuse, missing-byte, resync and survival metrics are identical (§3).
3. The SIMD/parallel "64-byte locality" argument **partly applies** to the current profile. After a 47-position transient per chunk, every tested position is a pure function of a 48-byte window. A two-pass candidate/reducer design that replays only this transient with scalar code matches the scalar oracle bit for bit (tested). Keeping the current semantics therefore adds a 47-byte scalar replay per chunk to a future SIMD backend (about 0.06% of bytes at a 64 KiB target), which is not a reason to change semantics.
4. **SIMD stays post-freeze.** It is a same-profile backend and never needs a new ProfileId. The Amdahl ceiling is material on both CI architectures: boundary detection is 52–55% (BLAKE3) and 35% (SHA-256) of streaming time on x64, and 27% (BLAKE3) and 38–39% (SHA-256) on arm64 (§4). The work is handed to #14 with the gates in §7, which these numbers meet.
5. **fastcdc-rs `v2016` is the matching external oracle; `v2020` is not.** fastcdc-rs 5.0.0's two-byte `v2020` loop never tests the last position of an odd-length final window. We found one input where `v2020` differs from `v2016`, from the ChunkShift scalar reference and from the Python reference (§5). This does not change ChunkShift semantics. It does rule out "two-byte rolling" as a same-profile optimization unless the odd EOF window is handled exactly.
6. The default size, `min/target/max` and the stable-profile count are **not** decided here. They need the real multi-version corpus with a holdout (§9). Nominal targets measure about 1.1–1.45× their power-of-two value on high-entropy synthetic data and up to 4× on degenerate data (§6). #8 must compare **actual** means.

## 1. What this change adds (lab only; no Core, API or format change)

| Component | Purpose | Issue section |
|---|---|---|
| `Lab/Prefreeze/GearCandidate.cs`, `GearCutters.cs` | lab-only `lab.fastcdc.gear.warmed-prefix.v0` candidate with its own ProfileId and fingerprint; exact predicate-window/transient model; two-pass candidate/reducer oracle with explicit transient replay | B2, B4, F3 |
| `prefreeze` mode (`PrefreezeRunner`, `PrefreezeScorecard`, `benchmarks/experiments/prefreeze.v1.json`) | deterministic quality scorecard for the current candidate, the warmed-prefix candidate and the fixed-size control: fine lane at 64/128/256 KiB and exploratory coarse lane at 512 KiB/1/2 MiB; 25 mutations including boundary-anchored edits; E4 distribution projection | C, D, E, L3, L6 |
| real-corpus manifest (`RealCorpus`, `--real`) | local multi-version payloads with provenance, license, retrieval date, size and SHA-256; mandatory calibration/holdout split; adjacent and skip-2/3 transitions; pair-only/short-history labels | C |
| new corpus generators | `single-byte`, `short-pattern` (7-byte period), `alternating-entropy`, `low-entropy-runs` (64 KiB–1 MiB zero runs in random data) | C pathological |
| `amdahl` mode | boundary / chunk hash / read / full streaming decomposition and Amdahl bound | A |
| fastcdc-rs probe and Python reference | 16 shared fixtures × 3 presets; `v2016` = ChunkShift on all 48 digests; `v2020` odd-EOF divergence asserted | L1 |
| `cdc-prefreeze` CI job (x64 + arm64) and cross-architecture digest comparison; `boundary-scan-f08` also runs on `issue-99` branches | cross-architecture evidence for A/E1 | A, E1 |

The warmed-prefix candidate and the two-pass reducer live only in `benchmarks/`. `FastCdcScalar`, `ChunkBoundaryState`, `ChunkingKernel`, the CSM golden vectors and every ProfileId/ProfileFingerprint are unchanged. The only production-adjacent change is a new test vector (§5) for behaviour that already existed.

## 2. Exact locality statements (B4)

Let `h_i` be the Gear state after byte `i`: `h_i = (h_{i-1} << 1) + GEAR[x_i] mod 2^64`.

**Predicate window.** In `(h << 1) + g`, carries only move towards higher bits. Bits `[0, w)` of `h_i` are therefore `Σ_{j<w} GEAR[x_{i-j}] << j mod 2^w`: a function of the last `w` bytes only. The predicate `(h & mask) == 0` reads bits up to the mask's highest set bit, so it depends on exactly the last `W = 64 − lzcnt(mask)` bytes. It does not depend on 64 bytes.

For every release-calibration preset (64 KiB … 2 MiB targets), strict and relaxed masks both have their highest bit at 47, so **W = 48**. Smaller targets have smaller windows; for example, the 256-byte target's strict mask gives W = 41.

| statement | current (`fastcdc.gear.chunkshift.v1`) | warmed-prefix (lab `v0`) | Xet (as described in #99; not re-verified) |
|---|---|---|---|
| deterministic across platforms | yes: integer-only; x64/arm64 digests compared in CI | yes, same arithmetic | per its spec |
| read-segmentation invariant | yes (existing kernel tests; `prefreeze` bounded-buffer streaming = span, tested) | yes (tested) | per its spec |
| candidate predicate depends on bounded history | yes, **after the transient**: last 48 bytes | yes, last 48 bytes, at every tested position | mask of top 16 bits ⇒ W = 64 |
| transient after a cut | tested positions `[Minimum, Minimum + 47)` see only `i − Minimum + 1 < 48` bytes (state starts at zero) | none: `Minimum ≥ 64 ≥ W`, and the prefix supplies the history | none after the documented skip-ahead |
| positions evaluable independently | every position `≥ chunkStart + Minimum + 47` | every position `≥ chunkStart + Minimum` | every position after the minimum |
| two-pass candidate list + sequential reducer bit-identical | **yes, with a scalar replay of the 47 transient positions per chunk** (`GearCutters.ChunkTwoPass`, tested on 18 corpus/target cells and 4 non-preset profiles, 3 of them with a transient that crosses `Target`). Replaying 46 positions fails the tests, so 47 is tight. | yes, no replay (tested) | claimed by its spec |
| SIMD/parallel without replay | steady state only | whole admissible range | whole admissible range |

Behaviour at the edges (all unchanged, all covered by vectors):

- **`Minimum ± 64`:** the current candidate ignores the prefix. The warmed candidate needs only bytes `[Minimum − 47, Minimum)` of it (skip-ahead from `Minimum − 64` is tested to equal the chunk-start definition). The current candidate's first 47 tests see a short history.
- **`Target`:** both switch from the strict to the relaxed mask for positions `≥ Target`, with no state reset. The window proof holds for both masks, and a transient that crosses `Target` is tested.
- **`Maximum`:** a forced cut at `min(remaining, Maximum)`. Both semantics are identical.
- **EOF:** a final remainder `≤ Minimum` is emitted as-is. Otherwise every position up to `remaining − 1` is tested, including the last position of an odd final window (§5).

**Exhaustive single-byte result.** For all 256 byte values, a run of that value never satisfies the strict or relaxed predicate in the current candidate's transient (state `g·(2^{k+1} − 1)`) or in the shared steady state (`g·(2^{64} − 1)`), at 64 KiB through 2 MiB. Both semantics therefore cut every single-byte run at `Maximum`. The 64 KiB and 256 KiB cases are tested.

## 3. Measured difference: current vs warmed-prefix (B, D)

The `prefreeze` plan covers the fine lane (13 × 8 MiB synthetic corpora including the four new pathological generators, 3 targets) and the coarse lane (4 × 64 MiB, 3 targets), with 25 mutations each. It evaluates 1,254 cells per candidate.

| lane | unmutated boundaries (current) | shared with warmed | cells with identical chunk sequence | cells whose quality metrics differ |
|---|---:|---:|---:|---:|
| fine | 1,640 | 1,639 | 917 / 942 | 0 |
| coarse | 574 | 574 | 312 / 312 | 0 |

The one differing boundary (`low-entropy-runs-8m`, 128 KiB) is a warmed-candidate cut in the transient range next to a zero run. The shifted boundary moves bytes between two adjacent chunks. In every mutation of that cell, unique missing bytes, reuse, resync distance and survival are equal for both candidates.

**Model.** If the predicate bits behave as independent uniform bits, the probability that either candidate hits a cut in the 47 transient positions of a chunk is about `2 · 47 · 2^−popcount(strict)`: 7.2·10⁻⁴ per chunk at 64 KiB (17-bit strict mask), 3.6·10⁻⁴ at 128 KiB and 1.8·10⁻⁴ at 256 KiB. Only such a hit can make the chunk sequences differ. The measured rate, 1 in about 2,200 boundaries, matches this model.

**Residual risk.** Periodic low-entropy content could in principle make the current candidate's transient hit systematically, cutting at `Minimum + k` where the warmed candidate would force `Maximum`. No such case appears in the 256 single-byte runs, the 7-byte pattern, the 4 KiB repeated pattern, the 4-symbol and 16-symbol alphabets or the zero runs. The real-corpus run (§9) reports `currentCutsInTransient` and `warmedCutsInTransient` per file, so a real case would be visible.

**Decision rule for #8.** Revisit semantics only if the holdout real corpus shows warmed-prefix improving unique missing bytes or reuse by more than 0.5% on some family, with no family regressing. Without that evidence, the external `v2016` oracle (§5) and the existing vectors favour the current semantics.

## 4. Post-#98 baseline and Amdahl decomposition (A)

- **PR #98 is accepted as the baseline.** It is merged (`9e7fd3a`). The dedicated `boundary-scan-f08` x64/arm64 rerun on its code (workflow_dispatch run 36048105886, recorded in [BOUNDARY-SCAN-F08-EVIDENCE-2026-09-24.md](BOUNDARY-SCAN-F08-EVIDENCE-2026-09-24.md#follow-up-a1-f01--a1-f03-applied)) measured `Scan` at 0.418/0.378 ns/B on x64 and 0.342/0.344 ns/B on arm64 (64/256 KiB), with ScalarLocals/ChainFloor at 1.49–1.64 (x64) and 1.23 (arm64). The golden vectors, CSM identities and JIT/NativeAOT package-smoke identities did not change.
- `boundary-scan-f08` previously ran only on `perf/f08*` branches, which is why #98's own PR skipped it. It now also runs on `issue-99` branches, and the new `cdc-prefreeze` job runs `amdahl` on x64 and arm64.
- **#99 head rerun** (PR #103, benchmark-lab run 36087064749, merge commit `c958cc6` of `99e6a3d` into `9c6e172`, i.e. after #98 and #101). Harness medians, ns/B, 64/256 KiB: `Scan` 0.426/0.386 on x64 and 0.342/0.342 on arm64; ScalarLocals/ChainFloor (F07 gate) 1.50–1.51/1.64 on x64 and 1.23/1.24 on arm64. This reproduces run 36048105886 within a few percent. x64 BDN rounds are excluded again by the agreement rule. The arm64 counters give 6.24 instructions/B for `Scan`, the same as ScalarLocals. A1-F07 therefore stays unjustified.

CI `amdahl` from the same run (`cdc-prefreeze`, GitHub-hosted `ubuntu-24.04` / `ubuntu-24.04-arm`, .NET 10.0.12; 3 rounds, median), ns per input byte:

| arch | target | streaming+BLAKE3 | boundary | BLAKE3 | residual | **boundary share (BLAKE3)** | streaming+SHA-256 | SHA-256 | **boundary share (SHA-256)** |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| x64 | 64 KiB | 0.629 | 0.327 | 0.279 | 0.023 | **51.9%** (≤ 2.08×) | 0.931 | 0.558 | **35.1%** (≤ 1.54×) |
| x64 | 256 KiB | 0.598 | 0.327 | 0.233 | 0.038 | **54.6%** (≤ 2.20×) | 0.924 | 0.553 | **35.3%** (≤ 1.55×) |
| arm64 | 64 KiB | 1.265 | 0.343 | 0.847 | 0.075 | **27.1%** (≤ 1.37×) | 0.888 | 0.470 | **38.7%** (≤ 1.63×) |
| arm64 | 256 KiB | 1.255 | 0.341 | 0.825 | 0.089 | **27.2%** (≤ 1.37×) | 0.894 | 0.464 | **38.2%** (≤ 1.62×) |

On both CI architectures, SHA-256 is hardware-accelerated. The boundary scan is therefore a larger share of the SHA-256 path there than on the local VM below, whose SHA-256 ran at 2.5 ns/B. On arm64, BLAKE3 costs 0.83 ns/B against 0.23–0.28 on x64, so the boundary share of the BLAKE3 path is lower. On every row, eliminating further copies can gain at most the 4–7% residual.

Local `amdahl` (3 rounds, alternating order, ≥ 64 warmup calls, median; 16 MiB random data; production `ChunkBoundaryState`/`ChunkingKernel` at `9c6e172`), ns per input byte:

| target | streaming+BLAKE3 | boundary only | BLAKE3 only | read floor | residual (copy, buffering, dispatch) | boundary share = max gain | max speedup |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 64 KiB | 1.056 | 0.521 | 0.427 | 0.092 | 0.108 | **49.3%** | 1.97× |
| 256 KiB | 1.005 | 0.511 | 0.372 | 0.083 | 0.123 | **50.8%** | 2.03× |

| target | streaming+SHA-256 | boundary only | SHA-256 only | residual | boundary share = max gain | max speedup |
|---:|---:|---:|---:|---:|---:|---:|
| 64 KiB | 3.166 | 0.521 | 2.527 | 0.117 | **16.5%** | 1.20× |
| 256 KiB | 3.138 | 0.511 | 2.511 | 0.116 | **16.3%** | 1.19× |

"Even infinitely fast boundary detection can improve the full scan by at most": **52–55% of wall time (x64) and 27% (arm64) on the BLAKE3 path, and 35% (x64) and 38–39% (arm64) on the SHA-256 path** on the CI runners. On the local VM, without SHA extensions, the figures are about 50% for BLAKE3 and 16% for SHA-256. After #101 the copy/buffering residual is about 10% of the BLAKE3 path, so eliminating further copies has a smaller ceiling than boundary work does.

Caveats:

- Components are timed separately in one process. Their sum is not required to equal the streaming time, and the residual absorbs any overlap.
- Single f08 processes on this VM were noisy: `Scan` measured 0.75 ns/B in its own process against 0.52 ns/B inside `amdahl`, with ScalarLocals 0.48 and ChainFloor 0.51. Code placement effects were already observed in the F08 evidence. Do not quote these local numbers as architecture evidence. Use the CI artifacts.
- No PMU counters were available on this VM. Cycles/byte and instructions/byte remain CI/arm64-only (F08 caveats apply).

## 5. FastCDC lineage and the fastcdc-rs oracle (L1)

- **Pin:** `fastcdc = "=5.0.0"` in `tools/reference/fastcdc-rs-probe/Cargo.toml`. `Cargo.lock` fixes the crates.io checksum, and the toolchain is 1.98.1. Crates.io publishes no VCS commit for the package, so the lockfile checksum is the pin of record.
- **Fixtures** (identical byte-for-byte in `fastcdc_reference.py` and the probe): xorshift 1 MiB and 1 MiB − 1; zero; a 7-byte pattern; a 2-bit alphabet; alternating 64 KiB random/zero; an odd-EOF transient case; and `minimum/target/maximum ± 1` lengths. Each runs at 64/128/256 KiB presets, 48 digests in all.
- **Result:** fastcdc-rs `v2016` reproduces the independent Python ChunkShift reference on all 48 (FNV-1a over `(offset, length)`). `v2020` equals `v2016` on every even-length fixture and differs on one odd final window. For `16384 × 0x00 ‖ 02 FF 41`, ChunkShift and `v2016` return `[(0,16386),(16386,1)]`; `v2020` returns `[(0,16387)]`. The cause is that the two-byte loop runs to `remaining / 2` and never tests position `remaining − 1` when `remaining` is odd. The probe asserts this divergence, so a fastcdc-rs upgrade that changes it is noticed.
- **Consequences:**
  - The claim "v2020 produces the same cut points as v2016" is true only for even final windows. FASTCDC-V1-CANDIDATE §11 now says so.
  - A two-byte rolling backend (F2) is a same-profile optimization **only** if it tests the last odd position exactly as the scalar reference does. The new `FastCdc_OddFinalWindowTestsItsLastPosition` test pins this for the production streaming kernel at 1-, 2- and 64 KiB read segmentations.
  - Xet/Google warmed Gear is a **different semantic branch**, not "FastCDC 2020", and compatibility is never inferred from an algorithm name.

## 6. Boundary quality at actual means (C, D, E2, L3, L6)

Current candidate, unmutated inputs (warmed-prefix is identical to three significant digits in every cell). Each cell gives the actual mean with its ratio to the nominal target in parentheses, then the coefficient of variation, then the forced-`Maximum` rate:

| corpus | 64 KiB | 128 KiB | 256 KiB |
|---|---|---|---|
| game-pak-8m | 83.6 KiB (1.31), 0.51, 1.0% | 143.7 KiB (1.12), 0.43, 0% | 282.5 KiB (1.10), 0.50, 0% |
| executable-app-8m | 106.4 KiB (1.66), 0.52, 3.9% | 195.0 KiB (1.52), 0.51, 4.8% | 409.6 KiB (1.60), 0.62, 10.0% |
| installer-archive-8m | 75.2 KiB (1.17), 0.46, 0% | 148.9 KiB (1.16), 0.57, 0% | 282.5 KiB (1.10), 0.54, 0% |
| db-vm-8m | 195.0 KiB (3.05), 0.38, 47.6% | 409.6 KiB (3.20), 0.34, 50.0% | 744.7 KiB (2.91), 0.40, 36.4% |
| random-8m | 85.3 KiB (1.33), 0.48, 1.0% | 167.2 KiB (1.31), 0.44, 0% | 356.2 KiB (1.39), 0.54, 0% |
| low-entropy-8m | 78.0 KiB (1.22), 0.49, 0% | 154.6 KiB (1.21), 0.45, 0% | 282.5 KiB (1.10), 0.50, 0% |
| random-like-8m | 78.0 KiB (1.22), 0.46, 0% | 148.9 KiB (1.16), 0.47, 0% | 341.3 KiB (1.33), 0.60, 4.2% |
| alternating-entropy-8m | 81.1 KiB (1.27), 0.52, 0% | 167.2 KiB (1.31), 0.41, 0% | 356.2 KiB (1.39), 0.41, 0% |
| low-entropy-runs-8m | 93.1 KiB (1.45), 0.68, 8.0% | 186.2 KiB (1.45), 0.64, 6.8% | 372.4 KiB (1.45), 0.70, 4.5% |
| zero / repeated / single-byte / short-pattern | 256 KiB (4.00), 0, 100% | 512 KiB (4.00), 0, 100% | 1 MiB (4.00), 0, 100% |

Coarse exploratory lane (64 MiB inputs):

| corpus | 512 KiB | 1 MiB | 2 MiB |
|---|---|---|---|
| game-pak-64m | 662 KiB (1.29), 0.45, 0% | 1,338 KiB (1.31), 0.42, 0% | 2,427 KiB (1.19), 0.63, 0% |
| db-vm-64m | 1,638 KiB (3.20), 0.38, 57.5% | 3,277 KiB (3.20), 0.41, 60.0% | 5,958 KiB (2.91), 0.47, 54.5% |
| random-64m | 590 KiB (1.15), 0.46, 0% | 1,285 KiB (1.25), 0.43, 0% | 2,731 KiB (1.33), 0.58, 4.2% |
| low-entropy-runs-64m | 753 KiB (1.47), 0.57, 1.1% | 1,560 KiB (1.52), 0.51, 2.4% | 2,621 KiB (1.28), 0.50, 0% |

Median unique missing bytes for a 4 KiB insert/delete/overwrite on the current candidate, with fixed-size at the same nominal size in parentheses: game-pak 78 / 175 / 300 KiB at 64/128/256 KiB, then 624 / 1,531 / 2,772 KiB at 0.5/1/2 MiB (fixed: 1.6–10 MiB, because inserts shift every block). Random, db-vm and low-entropy-runs scale the same way. Full tables are in the job artifacts.

Findings:

- Nominal ≠ actual, again (#67/#84). On high-entropy content the 64 KiB preset measures 75–106 KiB, and structured page data (`db-vm`) measures 3× with 36–60% forced cuts. **L3's forced-maximum problem is real on page-structured data with long zero runs.** Whether regression chunking or a threshold predicate helps is a **new-profile** question that needs the real DB/VM family (handed to #14, §8).
- The **coarse lane** reduces chunks per GiB roughly in proportion to the mean, and patch bytes grow in the same proportion. On synthetic data it shows no Pareto point where large artifacts gain patch bytes. It stays exploratory, and real PAK/VM traces must decide it (§9).
- Every E4 projection on single-edit synthetic mutations coalesces to about 1.1–1.4 ranges. It gives no signal for pack or Range design; real multi-version traces are needed.
- The synthetic sample is small: 8 MiB at 256 KiB is about 25–30 chunks per corpus. These tables calibrate the tooling and are not profile evidence (M0-LAB rule).

## 7. Optimization ladder: same profile vs new profile (F, G, L5)

| optimization | class | status / gate |
|---|---|---|
| #98 local state + split loops | same profile | done; vectors identical |
| #101 single-buffer kernel | same profile | done |
| bounds-check/codegen review on x64/arm64 | same profile | F08 disassembly job exists; no change proposed |
| two-byte rolling (F07/v2020 style) | same profile **only with exact odd-EOF handling** | F08 gate still > 1.15 on CI runners, so not justified yet; §5 adds the correctness trap |
| multiple independent Gear heads / ILP | same profile | via the two-pass design below; not prototyped |
| two-pass SIMD candidate finder + scalar reducer | same profile, **with a 47-position transient replay** | oracle and tests in `GearCutters.ChunkTwoPass`. Gate: CI `amdahl` shows boundary ≥ 25% of the canonical path on both architectures (met in run 36087064749: 27–55%), and the complete streaming path, not the candidate scan alone, must improve. Note that a speculative pass-1 over all bytes also evaluates the 22% prefix bytes the scalar loop skips. |
| pipeline parallelism (ParaSync-style) | same profile | post-freeze; ordering, bounded memory and no public worker/SIMD knobs |
| warmed-prefix Gear | **new profile** | rejected for 0.1.0 (§3); stays as a lab candidate |
| threshold predicate, regression chunking, Google/Stadia Gear | **new profile** | #14, lab-only comparator first |
| VectorCDC, SeqCDC, WideCDC, Chonkers | **new profile** | #14, promotion rule of #99 G |
| approximate parallel CDC (independent segments) | **not a backend for any published ProfileId** | any boundary difference is a compatibility change |

## 8. Security and handoffs (H, I, J, #14)

- **Security (H, L9):** keep `gearSeed = 0` and public deterministic boundaries. ChunkShift makes no confidentiality claim for boundaries. Keyed CDC is a separate security profile with its own threat model (eprint 2025/558, arXiv 2504.02095) and is not a Gear-table tweak.
- **#66 / #7:** resemblance detection (Finesse, Odess, Argus) and delta/format transforms (Zucchini, Puffin, Epic BDO) run after exact CDC reuse. They are selected on the server or build side, stay out of `ProfileId` and CSP search state, use one-hop base→target deltas and always keep a full-chunk fallback. Nothing in this work requires CDC boundaries to change for them.
- **#10 / #13:** profile size is not chosen to reduce request count. Pack size and Range coalescing are transport policy, and per-chunk encoding never changes `ChunkId`. The E4 projection tooling (`missingChunks` as the per-object negative control, ranges at 0/64 KiB/1 MiB gaps) is ready for real traces.
- **#14 (deferred research):** warmed-prefix and threshold/regression Gear as lab profiles on real game/VM traces; the Google `cdc-file-transfer` comparator (L2: pin the archived commit, lab-only, no runtime dependency); DedupBench cross-checks of quality only (L4); exact parallel CDC through desync-style overlap (L5); a SIMD pass-1 prototype for x64 AVX2/AVX-512 and arm64 NEON/SVE (F3); the hashless/next-generation algorithms (G).

## 9. Real-corpus protocol for #8 (C, L7)

`prefreeze --plan benchmarks/experiments/prefreeze.v1.json --real <manifest.json> [--no-synthetic]` reads a local manifest (schema 1):

```json
{
  "schemaVersion": 1,
  "families": [
    {
      "id": "game-a",
      "category": "game-pak",
      "split": "holdout",
      "provenance": "internal build server, builds 1041-1046",
      "license": "internal evaluation only",
      "retrievedUtc": "2026-09-25",
      "versions": [
        { "version": "1041", "path": "game-a/1041.pak", "sizeBytes": 4294967296, "sha256": "…" }
      ]
    }
  ]
}
```

- Payloads never enter Git. Paths are relative to the manifest. Every size and SHA-256 is verified before chunking.
- `split` is mandatory, `calibration` or `holdout`, and is **assigned before any parameter is looked at**. A run without a holdout family is labelled as unable to select the stable profile.
- Every family yields adjacent `vN → vN+1` transitions and skipped `vN → vN+2/+3` transitions. Families with 2 versions are labelled pair-only, families with 3–4 versions short-history, and at least 5 versions is the target.
- Files are chunked through a bounded buffer of about 2 × Maximum. The result equals the in-memory chunking for any read segmentation (tested).
- Every candidate and target of the selected lanes is recorded in the plan file, so a candidate cannot silently disappear from the results.
- L7 layout variants (stable vs reordered assets, per-asset vs whole-pack compression, aligned vs unaligned) are expressed as separate families with a shared category and `provenance` describing the producer policy. #8 must keep "bad chunker response", "bad producer layout" and "compression destroying similarity" apart.

The families to obtain are listed in #99 C: game PAK/IoStore, Unity bundles, .NET app builds, installers/ZIP, DB/VM, container/tar/zstd, compressed media, plus a random negative control.

## 10. Exit-criteria status

| #99 exit criterion | status |
|---|---|
| PR #98 accepted or rejected with measured reasons | **accepted** (§4) |
| dedicated x64 + arm64 post-#98 boundary evidence | **done**: run 36048105886 (#98's code) and run 36087064749 (#99 head, after #101), §4 |
| current vs warmed compared at close actual means | **done on synthetic data** (identical means, §3); **open** on real holdout |
| exact statement of where Xet's 64-byte proof applies | **done** (§2): W = 48, 47-position transient, reducer replay tested. Xet's own spec was not re-fetched in this environment (egress blocked); the Xet column uses #99's description. |
| real multi-version corpus with holdout discipline | **tooling done** (§9); **corpus open**, owned by #8 |
| quality evaluated separately from throughput | **done** (`prefreeze` has no timing; `amdahl` has no quality) |
| low-entropy/pathological behaviour explicit | **done** for synthetic cases (§2, §3, §6) |
| maximum benefit of further boundary optimization quantified | **done** on CI x64 and arm64 (§4). Chunk sequences from both lanes are identical across architectures (`cdc-prefreeze-compare`). |
| same-profile vs new-profile optimizations distinguished | **done** (§7) |
| first stable profile independent of CSP/Repository | **yes**: nothing here depends on #7/#10/#13 |
| #8 has enough evidence to freeze without a semantic/performance blind spot | **semantics: yes** (current). **Size/min/max: no**; this waits on the real corpus. |

## Sources and evidence tiers (L10)

Tier A (primary: paper, specification or canonical source):

- FastCDC ATC '16 and TPDS 2020 papers.
- fastcdc-rs 5.0.0 source (`v2016/mod.rs`, `v2020/mod.rs`), read from the crates.io package pinned by `Cargo.lock` on 2026-09-25.
- USENIX papers (VectorCDC FAST '25, ParaSync FAST '26, Regression Chunking ATC '12, Finesse FAST '19), the ICDE '21 Odess paper, ACM TOS Argus, eprint 2025/558, arXiv 2504.02095 / 2505.21194 / 2509.11121 / 2409.06066.

Tier B (maintained implementation or vendor documentation):

- Hugging Face Xet chunking specification. It could not be retrieved from this environment on 2026-09-25, so statements about it follow #99's text and must be re-checked.
- Google `cdc-file-transfer`, an archived repository.
- Borg, restic, SteamPipe and MSIX documentation.
- DedupBench and desync.

Tier C (secondary): none are used for any decision here.

Every performance number in this note that comes from a paper is the authors' own report. Every ChunkShift number was measured by ChunkShift code, and the note names the machine.
