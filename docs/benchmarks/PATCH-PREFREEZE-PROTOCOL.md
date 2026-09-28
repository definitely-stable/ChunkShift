# Patching pre-freeze protocol (#7): corpus, lanes and decision rules

Status: **frozen before measurement.** This note is merged before any run of the experiments below produces a decision dataset. Later edits may only add results links; a change to a rule, lane or corpus is a new ExperimentId.
Issue: [#7](https://github.com/definitely-stable/ChunkShift/issues/7) · Spec: [CSP-V1-CANDIDATE.md](../architecture/CSP-V1-CANDIDATE.md) §10.2 · Register: [PATCHING-DECISIONS.md](../architecture/PATCHING-DECISIONS.md) D13, D15, D17 · Registry: [EXPERIMENT-INDEX.md](../research/EXPERIMENT-INDEX.md)

| ExperimentId | Question | Register entry |
|---|---|---|
| `PATCH-PREFREEZE-001` | Does encoding 1 (zstd against base chunks) hold up on a frozen multi-product corpus? (§10.2) | CSP §12 decision (a) |
| `PATCH-ENC-002` | Which encoder policy (zstd level, dictionary chunks, candidates, radius) should be the default? | D15 |
| `PATCH-APPLY-001` | What do the re-chunk check and the `PAYL` block bound cost at apply and create? | D13, D17 |

## 1. Corpus

Manifest: [`patch-corpus/patch-corpus.json`](patch-corpus/patch-corpus.json). Source downloads: [`patch-corpus/source-assets.sha256`](patch-corpus/source-assets.sha256). Lock: [`patch-corpus/corpus-lock.json`](patch-corpus/corpus-lock.json) (`pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`). Reproduce with:

```text
python benchmarks/scripts/materialize_patch_corpus.py --root <corpus-root> --download --lock
```

The script downloads the release assets, verifies them against `source-assets.sha256`, extracts every version with the family's path rule and writes `<corpus-root>/pairs.json`, whose SHA-256 is the lock.

| family | split | versions (adjacent pairs) | category | source verification |
|---|---|---|---|---|
| `dotnet-aspnetcore-win-x64` | calibration | 10.0.10 → 10.0.11 → 10.0.12 | ReadyToRun managed + native PE, x64 | SHA-512 in the .NET `releases.json` |
| `dotnet-runtime-linux-arm64` | calibration | 10.0.10 → 10.0.11 → 10.0.12 | ReadyToRun managed + native ELF, ARM64 | SHA-512 in the .NET `releases.json` |
| `node-win-x64` | holdout | 24.19.0 → 24.20.0 → 24.21.0 | native PE (V8/C++) + JavaScript | `SHASUMS256.txt` |
| `node-linux-x64` | holdout | 24.19.0 → 24.20.0 → 24.21.0 | native ELF (V8/C++) + JavaScript | `SHASUMS256.txt` |
| `tzdata` | holdout | 2026b → 2026c → 2026d | text data | IANA publishes PGP signatures only; SHA-256 pinned at download |
| `chunkshift-source` | holdout | 9e7fd3a → 27aa319, 287a59d → 27aa319 | source archive (one tar per commit, fixed metadata) | built from this repository's commits |

- Twelve pairs, 1,893 changed files, 818 MiB of changed target bytes. Unchanged files are counted but not measured. Added files (for example the renamed `mscordaccore_amd64_amd64_<build>.dll`) are whole-file deliveries in every lane and are reported separately. Removed files cost nothing.
- The split is per product. Encoder settings are chosen on the .NET families (the product family of the original study) and checked on the holdout, which shares neither producer nor toolchain with them.
- The unit is a changed file: base = the file at the older version, target = the same normalized path at the newer version.
- Every manifest uses the stable profile `fastcdc.gear.chunkshift.v1.64k` and BLAKE3.

## 2. Lanes

All CSP lanes use the production encoder and applier of the commit under test (`ChunkShift.Patching`, internal `CspPatchBuilder` with an explicit `CspEncoderPolicy`, and `CspApplier`).

| lane | what is measured |
|---|---|
| `csp` | the default policy: level 19, K = 2 dictionary chunks, 8 candidates, 256 KiB radius |
| `csp-zstd` | the same without dictionaries (K = 0) |
| `csp-raw` | raw entries only (encoding 0); the "raw CSP" of §10.2 |
| `sweep-L{9,19}-K{1,2,4}-C{8,16}` | level × dictionary chunks × (8 candidates, 256 KiB) or (16 candidates, 1 MiB); 12 settings, one of them the default |
| `full` | target bytes (whole-file delivery) |
| `full-zstd` | `zstd -19` of the target |
| `zstd-patch-from` | `zstd -19 --long=31 --patch-from=<base>` |
| `xdelta3` | `xdelta3 -e -9 -s <base>` |
| `bsdiff` | `bsdiff4` (Python package) |

Recorded per file and lane: stored bytes (for CSP: physical length, embedded-TCSM bytes, payload entries by encoding, stored payload bytes, `PlanAsync` unique-missing bytes); create wall time; apply/decode wall time; for CSP also apply with the re-chunk check off. For files of at least 1 MiB in the `csp` lane, peak working set of create and of apply, each in a process of its own, and the same for an idle process as baseline.

Timing: `csp`, `csp-zstd` and `csp-raw` run one file at a time; create is timed once (the level-19 encoder makes one run of the corpus take tens of minutes), apply and decode are timed three times and the median is kept. Sweep lanes create only, files in parallel with the same worker count for every setting, one run; their time is only compared between sweep settings. External tools are timed the same way as the CSP lanes (encode once, decode three times).

Platforms: Windows 11 x64 (Intel Core i3-12100F, local), GitHub `ubuntu-24.04` (x64) and `ubuntu-24.04-arm` (ARM64). External tool versions are recorded per platform; they only feed informative rows.

End-to-end update time at bandwidth B: `bytes × 8 / B + apply (or decode) wall time`, for B = 50 Mbit/s and 1 Gbit/s.

## 3. Decision rules

Aggregates are sums over all changed files of the stated split (whole corpus when none is stated).

### PATCH-PREFREEZE-001 (CSP §10.2, thresholds fixed there)

- **R1:** `bytes(csp) ≤ 0.75 × bytes(csp-raw)` over the corpus.
- **R2:** at 1 Gbit/s, `end-to-end(csp) ≤ end-to-end(csp-raw)` over the corpus, on every platform lane.
- Both hold → **ADOPT**: decision (a) stands and the pre-freeze evidence is recorded. Either fails → **REJECT**: decision (a) reopens before the freeze (an owner decision); nothing is changed automatically.
- Informative, not gated: `full`, `full-zstd`, `zstd-patch-from`, `xdelta3`, `bsdiff`, the 50 Mbit/s times, the embedded-TCSM share, and the real CSP bytes against `UniqueMissingPayloadBytes` and against the study's framing estimate (payload bytes + the framing formula of `benchmarks/scripts/csp_encoding_study.py`).

### PATCH-ENC-002 (D15)

- Measured on the `ubuntu-24.04` lane. CSP bytes are expected to be identical on every platform; if they are not, that is reported and the x64 lane decides.
- A sweep setting is eligible when its total create time over the calibration split is at most 1.5 × the default's.
- The winner is the eligible setting with the smallest calibration bytes.
- **ADOPT the winner** as the new default when it saves at least 2 % of the default's calibration bytes and its holdout bytes are not larger than the default's. Otherwise **ADOPT the current default** (D15 becomes confirmed with this evidence). Settings outside the grid are not considered.

### PATCH-APPLY-001 (D13, D17)

- **A1 (D13):** re-chunk overhead = `apply(check on) / apply(check off) − 1` over the corpus. At most 25 % on every platform lane → **ADOPT**: the check stays on by default. Above 25 % on a lane → **DEFER**: the default stays, and an issue proposes an opt-out, which would be public API and needs the owner.
- **A2 (D17):** for every file of at least 1 MiB, `peak working set − idle baseline` stays at most 64 MiB for create and for apply, on every platform lane → **ADOPT**: the 4 MiB `PAYL` block bound and the bounded-memory claims are confirmed. Otherwise **DEFER** and investigate the file that exceeds it.

## 4. Records

Each run gets a RunId `<ExperimentId>/RUN-YYYYMMDD-NNN-<commit>-<platform>` and keeps its raw per-file JSON (CI artifact plus a compact committed dataset). Each experiment gets one result record under `docs/research/results/` from `RESULT-TEMPLATE.md`, and the index rows and register entries link it.

Results (2026-09-28, commit `853c89e`):

- `PATCH-PREFREEZE-001`: ADOPT — [PATCH-PREFREEZE-001-EVIDENCE-20260928-001](../research/results/PATCH-PREFREEZE-001-EVIDENCE-20260928-001.md);
- `PATCH-ENC-002`: ADOPT L19-K4-C8 — [PATCH-ENC-002-EVIDENCE-20260928-001](../research/results/PATCH-ENC-002-EVIDENCE-20260928-001.md);
- `PATCH-APPLY-001`: DEFER — [PATCH-APPLY-001-EVIDENCE-20260928-001](../research/results/PATCH-APPLY-001-EVIDENCE-20260928-001.md).
