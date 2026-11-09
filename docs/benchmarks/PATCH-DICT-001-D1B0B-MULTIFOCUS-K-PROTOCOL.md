# PATCH-DICT-001 / D1-B0B — multi-focus variable-K and sparse headroom

Status: **FROZEN ORIGINAL CALIBRATION ONLY — NOT A FRESH HOLDOUT OR ADOPT**.
Owner [#222](https://github.com/definitely-stable/ChunkShift/issues/222), release research gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251).
Prior H0-aligned source-locked baseline: merged [PR #451](https://github.com/definitely-stable/ChunkShift/pull/451), `main@b49a02c5c20d5f921f4894d6bfddc7c0583eb350`. Existing 1-focus results: H0 11,086 B, contiguous 10,892 B, sparse extra saving 0 B over two original calibration files.

## Decision question

D1-B0A demonstrated 97 B per focused PAYL entry of H0 savings when a **single** verified contiguous base record replaced four consecutive ChunkId references (96 B reference bytes plus 1 B stored-frame size). But it did not look at every changed payload. Before spending a new held-out cohort or introducing a composite topology in production, measure:

1. How many complete **physical CSP bytes** are recoverable from the existing original calibration cases with a bounded variable-length consecutive-run ladder **K=1,2,3,4**, independently across **all** PAYL entries; and
2. Whether any 2..4-reference **non-adjacent** raw-content dictionary contributes *additional* savings after this stronger contiguous baseline is fully applied.

The final result is a **calibration necessary-gate / headroom** signal, never a decision-grade >=10% holdout ADOPT.

## Frozen dataset and scopes

- Reuse the existing original source-pinned patch corpus, sources in `docs/benchmarks/patch-corpus/source-assets.sha256`, `pairs.json` SHA-256 `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`. GitHub-hosted runner materializes with `--lock`.
- Exactly the first **two eligible** changed file cases by original manifest order from the **calibration** families `dotnet-aspnetcore-win-x64` and `dotnet-runtime-linux-arm64`, using D1-B0A's unchanged 1..8MiB old, 1..2MiB target, 5..32 base CSM records, 1..64 target CSM records, at least one target ChunkId not in base. No selecting files based on scores.
- Do **not** read prior evaluation cohort as an unopened holdout. The same two files were opened in D1-B0/D1-B0A; any new evaluation allocation must use independent families/versions and be preregistered later. No release ADOPT verdict from these files.
- Exact original H0 from shipping `CspPatchBuilder` sequential/default. Require byte-identical H0 replay through production `CspReader`/`CspWriter` before candidate trial. SHA-256 of base/target original bytes and manifest CSM plus full ChunkId HashSuite checks fail closed. Do not assume SHA-256 equals the CSM's BLAKE3 ChunkId.

## Selection, in two cumulative stages

- Stage A **variable-K**: visit *every* original H0 PAYL ordinal in ascending PIDX first-target order (<=64). Include the **already selected H0 candidate** for each entry, and every verified base-occurrence contiguous run starting at any ordinal and having **K=1,2,3,4** whole chunks if its concatenated dictionary fits <=1MiB and <=4 ChunkId references. Full original target bytes are verified by actual HashSuite. Candidate zstd uses fixed L19, Prefix dictionary loading, hash/chain log20. Verify every trial by the real `CspPayloadDecoder` and byte-exact target comparison; reject a stored frame longer than its target chunk and trained-zstd dictionary magic.
- Candidate reserializes the **complete physical CSP** with *only that PAYL entry* replaced and all other current PAYL entry metadata, ordered ChunkIds and stored bytes preserved. Choose minimum physical CSP length, then fewer references, then fewer runs, then invariant lexical topology. On any choice, validate all nonfocus entry parity against the immediately previous complete CSP. Never claim an unverified 40+32R+frame *estimate* is full physical cost. No candidate can make complete CSP longer than the incumbent.
- Stage B **sparse extra**: start from the **entire completed Stage A** CSP, then visit every PAYL ordinal once in the same order. For each focus, compare current incumbent against every 2..4-occurrence non-adjacent subset of <=8 deterministically equispaced base record ordinals `distinct(floor(i*(n-1)/7))`. Same <=4 references and <=1MiB cap. No other candidates are disabled: Stage B retains all Stage A winners as incumbents; can only reduce or preserve complete physical CSP bytes. A sparse run here contains **one complete verified base chunk occurrence**, concatenated in increasing base occurrence ordinal.
- This is a bounded **sequential coordinate-selection** comparison, not a joint global oracle and not an exhaustive composite-run search. Report every per-entry accepted-trial count, exact before/after whole CSP bytes, chosen topology, reference counts, frame sizes, and total stage savings. A zero-candidate case keeps H0/previous-stage bytes and is not excluded.

## Required physical and resource evidence

- Persist H0, multi-entry variable-K contiguous, and variable-K-plus-sparse full CSP files per original file; original verified base bytes, base/target CSM, target SHA, and source-commit/source-pairs locks. Require source H0 identical to D1-B0A calibration H0 and a **whole-file** byte-exact `CspApplier.ApplyAsync` with sequential re-chunk check on all three artifacts.
- An independent Python CSP decoder checks all three complete CSPs using the archived original base and target SHA; its verdict and the actual ZIP-file lengths/SHA must agree with the JSON summary.
- Record actual base stream read calls, bytes, seeks for each Apply; only a **single-pass smoke** for CPU/wall. Never interpret process stream-read bytes as disk-sector bytes or a peak RSS measurement.
- Linux Ubuntu 24.04 and Windows 2025 hosted tests: positive sparse mosaic case, multi-entry complete PAYL sweep, original source-hash tamper rejection, CLI missing arguments. Exact branch HEAD checkout/clean-tree and full mandatory CI, CodeQL, Benchmark and Heavy validation before merging.

## Predeclared gate, limitations, next action

Primary headroom: `(H0 bytes - variable-K bytes) / H0 bytes`, on the **complete physical patches**. Incremental hypothesis: `(variable-K bytes - variable-K+sparse bytes) / variable-K bytes`. Also track absolute byte delta, total references and total real Apply read counters.

Issue #222 requires **>=10% total physical CSP reduction over strongest matched same-budget contiguous comparator on a separately preregistered unopened holdout**, plus apply CPU/wall <=+15% and bounded receiver I/O/memory; none of those product ADOPT claims can be made from this calibration. If the composite incremental gain is zero or far below the qualifying gate, first-release **DEFER** is the appropriate research-program action absent independent positive evidence. Do not open a holdout merely to fish for gain, and do not convert variable-K calibration improvements into composite ADOPT.

No production `src/`, CSP v1 wire-format, ChunkId locator, public Patching API, defaults, publisher, CLI contract or release controls are changed here. Each first-release disposition must remain explicitly scoped to evidence, not a global impossibility claim.
