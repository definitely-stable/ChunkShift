# PATCH-DICT-001 / D0 — bounded composite dictionary preregistration and structural oracle

Status: **D0 FOUNDATION ONLY / NO DECISION RUN / NO ADOPT**  
ExperimentId: `PATCH-DICT-001` · Owner: [#222](https://github.com/definitely-stable/ChunkShift/issues/222) · Program gate: [#251](https://github.com/definitely-stable/ChunkShift/issues/251)  
Parent baseline: `main@80dc2da08d9511bbac139299e9c232c7d7ae1d16` (2026-11-05), CSP v1 `CspEncoderPolicy.Default` / H2-W2.  
Dependencies: #183 G1/G2 closed for first release; #184 first-release tree/cross-file work DEFER.

## Question and release boundary

Under the **same fixed total raw-dictionary byte cap** and the **same verified single base file**, can concatenating several disjoint runs of complete base chunk *occurrences* yield enough end-to-end physical CSP benefit over the strongest same-budget contiguous run to justify additional apply-side references, seeks, memory and persisted representation complexity?

D0 only implements a deterministic, bounded **structural** selection oracle with an injected integer cost callback. The callback in tests is artificial. D0 produces no real zstd frames or CSP artifacts, **no byte-savings estimates**, no physical baseline, no formatter/decoder, and no real source-corpus decision. Do not cite a synthetic score as patch-size evidence. **CSP v1 compatibility correction:** CSP-V1-CANDIDATE §5.2 already allows up to four **ordered arbitrary base ChunkIds**, whose bytes are concatenated in listed order. Only the current **builder** requires contiguous base runs. Thus <=4 referenced occurrences across disjoint runs can already fit CSP v1 without run metadata or a decoder change. >4 named chunks would require a new persisted representation/RFC. Production code, builder default, CLI, public API, repository and trust format remain unchanged.

## D0 frozen structural semantics

1. Source = **one** immutable, contiguous, zero-origin logical base file, ordered by canonical CSM record ordinal. D0 deliberately models the supported SHA-256 ChunkId suite: require exact SHA-256 of every occurrence's immutable bytes and contiguous offsets before use, and recheck each selected occurrence before materialization. A BLAKE3 CSM requires a separately validated adapter in D1, **not** a SHA-256 substitution for a BLAKE3 ChunkId.
2. Run = `(start base-record ordinal, count)` of whole, consecutive verified occurrences. A candidate plan contains runs sorted by increasing ordinal. Selected runs must be non-overlapping **and non-adjacent**: adjacent runs are one canonical run. Equal bytes/ChunkIds occurring at distinct ordinals remain distinct occurrences.
3. Raw dictionary bytes = concatenate selected runs in canonical base-ordinal order, each chunk in its record order, without gaps or padding. Reject dictionaries starting with zstd trained-dictionary magic `37 a4 30 ec` as raw zstd prefixes. No arbitrary byte offsets, cross-file lookup, recursion or approximate matches.
4. For D0 proof-of-bounds: source <=64 chunks, <=8 MiB source bytes, each chunk <=256 KiB; dictionary `1 <= bytes <= 1 MiB`; <=32 referenced occurrences; <=16 selected runs; candidate pool <=8 ordered runs. The oracle checks at most `2^8-1=255` nonempty subsets, with each result materialized separately and no persistent multi-MiB cache. Out-of-bounds inputs fail closed. These are **D0 harness** bounds, not proposed public CSP limits.
5. Candidate pool is caller-supplied and sorted canonically; reject invalid, duplicate and out-of-range proposals. Enumerate all nonempty subsets of this *declared small pool*, skip impossible combinations, and record the best one-run (contiguous) and best-any plan under exactly the **same cap**. It is **not** an exhaustive oracle over every possible base-file occurrence. D1 must include the actual incumbent and a sufficiently strong same-budget contiguous candidate universe before it can claim meaningful headroom.
6. A selected plan is CSP v1-compatible if it references <=4 verified occurrences. V1 stores ordered ChunkIds, **not occurrence ordinals**, so duplicates may resolve to a different physical offset on apply. D1 must measure actual production base-locator read/seek costs; D0 source ordinal is not an apply locality guarantee.
7. Cost callback must return a deterministic, nonnegative, signed-64-bit integer. Select by `(cost, run count, dictionary byte count, referenced occurrence count, lexicographic run ordinals)`. Never use current wall-clock/CPU scheduling to choose artifact bytes. The D0 injected synthetic callback **does not define a persisted wire cost**.

## Follow-up D1: mandatory physical experiment design (not executed here)

**Input locking before any original measurements:** source assets and versions, full source SHA-256, manifest/profile/HashSuite, per-family cohort, file-level row identity, calibration versus unopened product-family evaluation, candidate pool generation and source/codec/BuildPolicy commit must be independently sealed. Prior 844-file G1/HDiff evaluation and previously opened corpus are historical controls, **not a fresh sealed holdout**. Do not tune budgets, shortlist or thresholds after opening evaluation.

- Start with actual shipping H0 per-file CSP and matched same-budget **contiguous** variant; no arbitrary increased G1 budget, changed zstd level/hash/chain, target chunking, or new source semantics. Record and reproduce H0 physical CSP SHA and apply SHA for every pair.
- Sample distinct real product/version families (managed .NET, native PE/ELF and diverse distribution/container populations); stratify source/data sizes. Include new independent families and unchanged/unsupported fallback in the denominator. Use original GitHub-hosted runner assets only; no workstation, self-hosted, purchased runner or holdout reuse as new.
- Candidate pool and exact permitted run-count ladder `1/2/4/8/16`, source-eligibility, same-byte budget and bounded search must be preregistered. With `MAX_CANDIDATES=8` D0 cannot explore 16 distinct runs; D1 must explicitly re-budget and prove resource safety before widening it.
- Generate **real zstd** frames using frozen backend and compare frame contents/length, independent bounded decoder, verified base identities, final original target SHA-256, corrupt/missing/wrong-base handling, cancellation, and explicit fallback. Synthetic concatenation alone is not an apply proof.
- Count complete physical bytes: retained CSP v1 sections/manifest/PAYL/PIDX, zstd frame and every 32-byte ChunkId reference. For <=4 references in CSP v1 there are **no new run descriptors**; the existing ordered references encode the dictionary. For >4 refs or occurrence-addressing semantics, a separate RFC and accounting for every new run/offset descriptor, versioning and inverse metadata are necessary. No invented 'free' framing or unattested compression savings. Until a real signed/versioned format is specified the framing estimate is explicitly **synthetic**, never a production artifact.
- Measure both create and apply process wall/CPU, peak private/RSS or accurately labelled substitute, logical and physical base reads, actual range/seek operations, cold/warm CSM, CPU amplification, and effective memory cap. Independently audit first-attempt original ZIP/digests and recompute each aggregate from retained per-case rows.

**Proposed D1 adoption gates from #222, requiring a separate freeze before execution:** at least 10% reduction in **complete** physical patch bytes versus strongest same-budget contiguous comparator on fresh holdout; apply wall/CPU no worse than +15% without a preregistered product tradeoff; apply base read bytes <=1.50x selected dictionary bytes; no >4x base read operations/ranges vs comparator without separately demonstrated end-to-end win; <=16 runs and hard memory/lookup budgets; exact reconstruct/verification on every eligible file and fail-closed fallback. Any relaxation after decision data is forbidden. No positive result directly changes CSP v1: only a new separately reviewed versioned RFC and independent apply/decoder vectors could authorize that.

**Two-path decision:** First test <=4-reference composite selections as a potential **builder-only CSP v1** optimization, without presuming a format revision. Consider >4 references/new CSP vNext only after a separately positive whole-patch cost gate and RFC.

**Stop rule:** If preregistered small-pool D1 calibration cannot clear 10% **even under a lower-bound wire-cost oracle** plus accounted references, stop without opening sealed evaluation; record `REJECT/DEFER` scoped to tested topology. No training, automatic PATCH-ML, PATCH-COMPILER or patch-format widening follows from D0.

## D0 reproducibility and acceptance

- [x] Bounded `benchmarks/scripts/patch_dict_001_d0.py`, stdlib-only, SHA-256-suite occurrence validation and canonical dictionary materialization.
- [x] Deterministic bounded one-run versus multi-run structural enumeration with explicit tie-break and injectable **synthetic** integer scorer.
- [x] Unit tests for exact cap, disjointness, adjacency, repeated occurrences, CSP v1 <=4-reference classification, malformed input/digest, trained magic, scoring errors, proposal reordering and independent random brute-force cross-check.
- [ ] GitHub-hosted Linux + Windows D0 test workflow green at exact PR HEAD (fill in only from real Actions result).
- [ ] Independent review and PR integration. **No D1 physical run or final research verdict yet.**

D0 tests: `python -m unittest discover -s benchmarks/scripts/tests -p 'test_patch_dict_001_d0.py' -v`.
