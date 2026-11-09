# PATCH-DICT-001 / D1-B0 — original calibration, complete physical CSP

Status: **ORIGINAL CALIBRATION PREFLIGHT / NOT DECISION / NO_ADOPT**.
Issue: [#222](https://github.com/definitely-stable/ChunkShift/issues/222), gate [#251](https://github.com/definitely-stable/ChunkShift/issues/251). Previous D0: #448 (`main@96edd25c`); D1-A #449 (`main@5503f964`).

## Strict objective

Verify a real **complete CSP v1** can be built and physically applied from verified original source/target CSM data with either (a) the best same-budget contiguous dictionary or (b) a bounded multi-run composition, without extending the CSP format, changing the existing production builder or selecting arbitrarily read byte ranges. Compare the resulting *whole physical CSP lengths* and actual original target hashes, not an inferred `64+32R+frameLength` approximation. Production H0 is a separately generated read-only anchor, **not assumed to be an otherwise identical counterfactual builder**.

**This is D1-B0 feasibility, not the D1-B decision.** The already-opened PATCH-GAP calibration dataset is used only for a bounded preflight. We do not query the historical evaluation families for candidate scores; we do not describe old G1 evaluation rows as an unopened holdout. No 10%-gate / first-release product decision can be derived from two selected pilot files.

## Frozen preflight input and eligibility (before original runs)

1. Use existing `docs/benchmarks/patch-corpus/patch-corpus.json`, original source-archive SHA-256 `source-assets.sha256`, and `pairs.json` SHA-256 `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`, enforced at startup. Materialize using the **original pinned source versions** and `--lock`; reject any changed bytes, CSM identities, HashSuite mismatch or corrupt target.
2. **Calibration only:** `dotnet-aspnetcore-win-x64`, `dotnet-runtime-linux-arm64`. Traverse original `pairs.json` pair order, with changed file path ordered ordinally inside a pair. Select the **first two eligible files**, never based on the observed compression win. Predeclared resource screen: base size 1..8 MiB, target 1..2 MiB, 5..32 verified whole base CSM records, 1..64 verified target CSM records, at least one distinct target ChunkId absent in base; all records digest-verified against original source under CSM's actual HashSuite.
3. Only the **first missing target ChunkId** in manifest order is subjected to dictionary selection (all other missing entries are identically encoded by raw/no-dictionary zstd under both research comparators). This makes a sharply limited, causal *one-entry* screen on whole files. It is **not** a complete-file optimal dictionary selector and never establishes full-corpus potential.
4. Dictionary cap = 1 MiB; <=4 ordered named `ChunkId` references per CSP v1 payload entry. Encoder fixed: production `CspPayloadEncoder` level19/Prefix with hash/chain caps20, target max chunk 256 KiB. Real decoder checks every candidate exact byte-for-byte. Raw/trained dictionary magic and CSP StoredLength > target chunk length excluded.

## Comparator universe and accounting

- **Contiguous:** exhaustively test all start ordinals and lengths 1..4 within the verified 5..32-base-record file and the same 1 MiB budget. Keep raw/no-dictionary alternatives.
- **Composite:** sample <=8 base occurrence indices by equispaced deterministic floor quantiles `floor(i*(n-1)/7)`, unique ascending, then test all 2..4-reference combinations of non-adjacent indices. Keep raw/no-dictionary alternatives. This is a deliberately bounded **pool**, not a global optimum for multi-run dictionaries.
- Rank candidate by exact **per-entry** PAYL+PIDX reference/actual frame bytes, then refs, run count, topology. Output three independently serialized **complete** patches: shipping H0, matched lab contiguous, matched lab composite. Both research lanes encode every other missing entry in exactly the same manner and embed the same byte-identical target CSM, BASE, PAYL, PIDX, FOOT and TRAILER physical structure through the production `CspWriter`.
- The resulting complete CSPs are re-applied by **production `CspApplier` with the Sequential re-chunk check**. Require zero Apply failures, verified complete output byte equality with the original target and target SHA-256 in `pairs.json`. Count observed `PatchLabCountingStream` base reads, bytes, seeks for **the actual apply**, not synthetic ordinals. Single apply process CPU/wall are only smoke observations, not stable performance evidence.
- Preserve physical patch bytes, SHA-256, exact target/base CSM and frozen base source as an audit artifact; independently check each artifact with the Python 3.14 CSP decoder on the same runner. Recompute total physical lengths from actual CSP files. No upfront claim on improvement.
- No measured process peak RSS here, no repeated ABBA/counterbalancing of apply timing, no full pair-family coverage, no fresh unopened evaluation. Adoption requires a **separately sealed and preregistered D1-B1 evaluation** with explicit independent host/corpus verification and whole-patch/CPU/RSS/resource gates from #222.

## Required checks

- xUnit: real synthetic-source full-CSP Create/Apply round-trip via H0, contiguous and composite lanes; source-digest tampering fail-closed; CLI bounded-argument rejection.
- GitHub-hosted Linux/Windows: compile/tests and same SHA source checkout; original calibration ZIP on GitHub Ubuntu only, pinned source assets with corpus lock, all three persisted CSP bytes, independent decoder and output SHA. Failure (including no eligible original files) blocks preflight rather than fabricating data.
- All repo-required CI/Benchmark/Heavy/CodeQL checks on final PR HEAD. No changes under `src/`, CSP normative spec, PublicAPI, publication repo or trust boundary.

## Execution

```bash
python3 benchmarks/scripts/materialize_patch_corpus.py --root "$CORPUS" --sources "$HOME/patch-corpus/sources" --download --lock
dotnet benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0/ChunkShift.Benchmarks.dll \
  patch-lab dict-d1b0 --corpus "$CORPUS" --output "$OUT/summary.json" \
  --evidence-dir "$OUT/evidence" --source-commit "$GITHUB_SHA"
```

**Decision status remains PLANNED.** A positive D1-B0 result can authorize designing D1-B1 only; a negative result can motivate an explicit scoped STOP/DEFER, never a format break.
