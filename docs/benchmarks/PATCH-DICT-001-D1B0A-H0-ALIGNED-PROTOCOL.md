# PATCH-DICT-001 / D1-B0A — H0-aligned one-entry full-CSP calibration

Status: **REGISTERED CALIBRATION PRE-FLIGHT / NO HOLDOUT / NO_ADOPT**.
Owner: [#222](https://github.com/definitely-stable/ChunkShift/issues/222); research freeze: [#251](https://github.com/definitely-stable/ChunkShift/issues/251).
Prerequisite: original full-CSP D1-B0, PR #450 merged in `main@56df5ab0a9713e181482e3f7a396eb9d022117e0`.

## Question, frozen before first source test

Can a verified, bounded <=4-reference disjoint-run dictionary for one target payload entry materially reduce the **complete physical CSP** relative to a strong **same-budget contiguous** selector, when all other PAYL decisions are held byte-identical to the shipping H0 patch? D1-B0's previous 2-file physical comparator left all other entries encoded with plain/raw and was 21x larger than H0 for that reason; a comparison against its H0 artifact was confounded. D1-B0A explicitly repairs the confound.

The experimental population stays **exactly the first <=2 eligible original calibration cases by source order** from D1-B0, not reselected by result or renamed, with existing frozen `pairs.json` SHA-256 `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`. Original release assets are pinned by `docs/benchmarks/patch-corpus/source-assets.sha256` and materialized with `--lock`. Evaluation/holdout families are **not consulted**. Synthetic positive and incompressible test fixtures are separate and are never counted as original evidence.

## Strong baseline and exact replay admission

1. Create **real production H0** using the default `CspPatchBuilder` sequential execution and source-verified CSM. Open the complete H0 via production `CspReader`; read its embedded target manifest, optional BASE binding, PIDX first-target ordinals and every PAYL metadata/reference/stored frame byte, keeping entry order.
2. Re-emit the same sections and every H0 entry via the unchanged production `CspWriter` default block settings. Before admitting any candidate, require complete **physical byte equality** with the original H0 CSP (not only size or target equivalence). Reject the entire case if any bit differs; do not "correct" H0 post hoc.
3. Focus on the same first missing target ChunkId that D1-B0 recorded. Require equality with H0 PIDX FirstTargetIndex/ChunkId, with the verified original target CSM/HashSuite, with the original target raw bytes and with the expected BASE ManifestId.
4. For A and B, preserve **all other H0 entries** exactly: original encoding, ordered dictionary ChunkIds, stored frame bytes, first-target index and ChunkId. Re-parse chosen physical CSP and check per-entry equivalence for every entry except focus. Never re-encode any nonfocus entry. Only the focus entry may change; physical offsets, PIDX index offsets/CRC/trailer necessarily update as a result.
5. CSM ChunkIds and source occurrences are verified with the actual CSM HashSuite (normally BLAKE3) and a 1 MiB dictionary cap, <=4 reference chunks. The locator works by ChunkId, not ordinal. Per-source bytes and full original content SHA-256 are enforced before selecting a target.

## Frozen candidate universes

- H0 **incumbent** itself belongs to both lanes, including its original frame and references: an incompressible/no-composite case cannot regress production H0.
- Contiguous lane: incumbent H0 plus **all** consecutive 1/2/3/4 base-chunk occurrence runs in files with 5..32 base CSM records, within 1 MiB dictionary budget. No arbitrary partial records, no cross-file lookup, no new source semantics.
- Composite-enabled lane: the complete contiguous lane plus all subsets of 2..4 non-adjacent source occurrences from the deterministic equispaced <=8-index pool `distinct(sorted(floor(i*(n-1)/7)))`, each with the same cap and <=4 ChunkId references. A "run" here is one selected whole occurrence. No candidate fitting the same compressed bytes is assumed to save metadata; count every 32-byte reference.
- Encoder fixed: `CspPayloadEncoder(19, Prefix, dictionaryHashLog:20, dictionaryChainLog:20)`. Reject raw trained-zstd dictionary magic and encoded frames with StoredLength greater than the target ChunkId's real uncompressed length. Every candidate is decoded by `CspPayloadDecoder` and compared with the original target chunk bytes. Byte-identical duplicate ChunkIds are legitimate but actual source Apply lookup must be measured, not inferred from occurrence ordinals.
- Compare candidate **physical whole CSP length**, reserialized through `CspWriter` with all other H0 entries unchanged. Choose by smallest whole length, then fewer refs, fewer runs, then canonical invariant-culture ordinal topology string. Never rank on estimated `64+32R+frame` in place of the full artifact. Run selection is deterministic for a frozen backend and same source.
- Evaluate real `CspApplier` + `ChunkingCheck.Sequential` and require exact final bytes and pinned SHA-256 on H0, aligned contiguous, and aligned composite. Count physical base read calls, bytes, seeks from actual Apply. Single-run process wall/CPU is *smoke-only*: not a performance verdict, no meaningful RSS/CPU normalization.

## Controls, evidence, stop gate

- Resource envelope remains D1-B0: base file 1..8MiB, target file 1..2MiB, 5..32 base CSM records, 1..64 target records, <=2 files chosen by manifest order and SHA-locked original corpus; only first missing target ChunkId is replaced. Evaluate target ChunkId exactly and never exclude no-gain outcomes.
- Must retain H0 bytes, resynthesized aligned contiguous/composite CSPs, original verified base, base/target CSMs, target SHA and JSON with source commit/candidate counts, whole lengths and actual Apply read counters. An independent Python CSP decoder must validate every original CSP, even unchanged H0, against the pinned target SHA.
- **No adoption inference from two files**, even if the synthetic mosaic control has a large win. If these two originals show 0% additional composite benefit, record that as a scoped negative and decide if a larger calibration stage or STOP/DEFER is justified. No sealed holdout opening, format RFC, production `src/` modification, CLI public exposure, or release gating change follows from D1-B0A.
- D1-B1 sealed evaluation (if ever authorized) requires an independent, preallocated evaluation corpus, fully matched builder, repeated create/apply CPU + wall, independent process peak memory, actual ChunkId locator read-amplification and absolute first-release adoption gates of #222 (>=10% complete physical bytes versus best same-cap contiguous on holdout, apply costs within predeclared bounds).
- This research PR remains Draft until exact-head hosted Linux/Windows contract tests, frozen original calibration, independent ZIP audit and all general CI, Benchmark/CodeQL/Heavy checks complete.

## Command

```bash
dotnet benchmarks/ChunkShift.Benchmarks/bin/Release/net10.0/ChunkShift.Benchmarks.dll \
  patch-lab dict-d1b0a --corpus "$CORPUS" --output "$OUT/summary.json" \
  --evidence-dir "$OUT/evidence" --source-commit "$GITHUB_SHA"
```
