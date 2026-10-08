# PATCH-RESIDUAL-001 — Phase A reversible residual foundation

Status: **PHASE A FOUNDATION / NO DECISION RUN / NO ADOPTION**  
Date: 2026-10-08  
ExperimentId: `PATCH-RESIDUAL-001`  
Owner: [#262](https://github.com/definitely-stable/ChunkShift/issues/262)  
Research-freeze authority: [#251](https://github.com/definitely-stable/ChunkShift/issues/251)  
Depends on completed G4 evidence in [#183](https://github.com/definitely-stable/ChunkShift/issues/183) **before Phase B decision measurements**.

## 1. Correction to the research thesis

PATCH-ENC-005's measured miss concerns candidate selection inside the current dictionary representation universe. It does **not** prove that better residual *representations* do not exist. Conversely, the prefreeze gap to bsdiff and zstd --patch-from is not proof that a local XOR scheme can close that gap.

Run a cheap, falsifiable exact-residual screen before proposing CSP vNext, ML ranking or a general patch compiler.

This experiment is isolated from PATCH-GAP-001 G4. Never add its candidates, corpora, transforms or encodings to the frozen G4 implementation or evaluator.

## 2. Phase A — fixed scope / semantics

Code: `benchmarks/ChunkShift.Benchmarks/PatchLab/Residual/PatchResidualFoundation.cs`.

Two intentionally elementary representations are permitted:

- **R1 — equal-length XOR + one standard zstd frame**: for each index i, residual[i] = base[i] XOR target[i]. Reconstruct by exact-length frame decode and XOR. No alignment search, no trained dictionary, no change to zstd parameter identity in CSP.
- **R2 — canonical maximal changed runs (raw)**: store strictly separated runs of changed bytes with zero-based start and positive length. Adjacent changed positions MUST belong to one run. Base bytes are copied first; specified runs overwrite them. No floating point or probabilistic similarity.

Phase A is **not** a search algorithm; its caller supplies one exact base byte span and a corresponding target span. Inputs have the **same nonzero length, at most 1 MiB**, with a nonnegative base offset. Inserts/deletes and whole-file alignment are out of scope. Source corruption must reject, never continue by guessing.

The SHA-256 base/target bindings are synthetic Phase-A identities to test exact decode failures. It is **not a ChunkId**, does not replace the CSM HashSuite, and cannot be carried into an actual CSP vNext design without a separate persisted-format RFC.

## 3. Synthetic RS01 envelope and accounting

RS01 is an explicitly **non-production research envelope**, not a CSP section/encoding. All bytes of this envelope are included in reported synthetic representation costs. This is a lower-scope *codec mechanics* experiment, not a whole-patch measurement.

| Field | Bytes | Semantics |
| --- | ---: | --- |
| Magic | 4 | `RS01` |
| Kind | 1 | `1` XOR+zstd, `2` raw changed runs |
| Target length | 4 | positive LE int32, <= 1 MiB |
| Base offset | 8 | nonnegative LE int64, caller binding |
| Stored length | 4 | bounded LE int32 |
| Base SHA-256 | 32 | exact supplied base content bytes |
| Target SHA-256 | 32 | exact decoded target content bytes |
| Stored body | variable | one frame or canonical sparse runs |

**Header = 85 bytes.** Total = 85 + exactly `StoredLength`, using checked arithmetic. Decoder rejects missing/extra bytes, unsupported mode, invalid base offset/digest, invalid sizes, noncanonical/overlapping/adjacent/unchanged sparse runs and corrupt zstd. After reconstruction it independently checks SHA-256 of the exact decoded target so a structurally legal payload alteration cannot silently succeed. No allocation based on an untrusted length before validating the 1 MiB output bound. Sparse body starts with LE int32 count, then `(start:i32,len:i32,bytes[len])` for each maximal run.

Important: RS01 base/target SHA-256/offset/header costs are **not CSP vNext overhead estimates**. A production representation could use a different verified-source model. Neither naked zstd-frame size nor RS01 size may be compared as if it were full CSP physical size.

## 4. Phase A acceptance

- Exact round trips for both modes, even when input is low-entropy, periodic or random.
- Repeatable encoding for equal bytes and the same backend/toolchain.
- Wrong base and mismatched offset fail closed; malformed headers, sparse payloads and target-content tampering are rejected.
- Every synthetic header/stored byte is accounted; no claimed compressed-byte gain yet.
- No changes under `src/`, CSM/CSP, `CspEncoderPolicy.Default`, PublicAPI or publication repository.
- Existing `benchmark-lab.yml` on standard GitHub-hosted runners must build and test the benchmark solution.

Phase A cannot close #262. A green PR check is a *mechanics result only*, not the frozen final decision or proof of headroom.

## 5. Phase B protocol to freeze separately (NOT RUN)

Pre-register eligibility and oracle before examining decision data:

1. Exact target-first-occurrence mapping and verified base occurrences; define *which* old occurrence is legal, including repeated ChunkIds and source read/seek accounting.
2. Equal candidate-retrieval budget and comparable byte-alignment/control work for H0 and residual lanes; no unbounded search.
3. R0 = production H0 CSP on the **same pinned build**; R1/R2 = independently decoded synthetic representations plus all required source/ref/target/container overhead projected into an explicit comparable physical model. Any true CSP vNext lane needs separate RFC/vectors.
4. Retain the complete *physical* CSP baseline, eligible subset, corpus-weighted total, unique missing physical bytes, metadata, base reads/seeks, create/apply CPU/wall/RSS, and all exact reconstruction digests.
5. Include .NET IL/R2R/NativeAOT, binaries, precompressed containers, periodic, random and adversarial edits. Use calibration and sealed held-out evaluation sets.
6. Use an independent decoder that does not simply invoke the encoder helper. A second implementation or separately pinned oracle is required for persisted-format adoption.
7. Choose and freeze numerical ADOPT/DEFER/REJECT thresholds **before** decision-run byte observations. Phase A does not choose them; no post-hoc tuning or substitution of eligible-only results for full-corpus gates.

If the bounded oracle reveals no meaningful residual advantage or overhead erases it, record **REJECT**. If it reveals headroom, still require an isolated vNext-format feasibility RFC, decoder and application-cost gates. No direct production promotion.

## 6. Cross-project lessons and non-goals

- DELSK's best simple candidate layer selects **whole base objects**, not a residual's per-byte alignment. Do not confuse them.
- Mathlab's exact-locality results encourage explicit proof/counterexample work, not invented general composition of cryptographic digests.
- DeltaMeter's NO-GO on system reconciliation warns against counting only payload/sketch bytes while hiding verification and required full-transfer overhead.
- The Core incremental manifest study (#150) reuses chunk records, but `ManifestIdAccumulator` hashes the full ordered record stream. No free digest concatenation is assumed.
- Cross-file reuse (#184) requires bounded exact locator work; current CSP apply materializes up to 4,194,304 distinct base IDs. Do not raise the limit silently.
- No new public hash suite, neural codec, network protocol, self-hosted runner, public API or CSP v1 encoding.

## 7. Evidence

Research index and machine registry record `PATCH-RESIDUAL-001` as **PLANNED** until a concrete, retained decision RunId exists. A Phase-A green unit test does not change it to RUNNING or ADOPT.
