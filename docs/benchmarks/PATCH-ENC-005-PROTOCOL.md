# PATCH-ENC-005 protocol: better dictionary candidates for less work

Status: **DRAFT freeze candidate — parameters are fixed below; this PR must be reviewed, frozen and merged before implementation decision runs.**  
Issue: [#181](https://github.com/definitely-stable/ChunkShift/issues/181) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
ExperimentId: `PATCH-ENC-005`  
Protocol baseline: `e967aeb6d4d467e94c5ac20f85e70ba0035d998d` (PATCH-ENC-004 H2-W2 is already the production create default)  
Corpus lock: `pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`  
Prior evidence: [PATCH-ENC-002](../research/results/PATCH-ENC-002-EVIDENCE-20260928-001.md), [PATCH-ENC-003](../research/results/PATCH-ENC-003-EVIDENCE-20260929-001.md), [PATCH-ENC-004](../research/results/PATCH-ENC-004-EVIDENCE-20261001-001.md).

A change after freeze to a lane, numeric parameter, sampling rule, metric, memory bound, eligibility threshold or holdout rule requires a new ExperimentId. Exploratory runs before this protocol is merged are never decision data.

## 1. Question and baseline

Can ChunkShift reduce patch-create work/time and/or physical patch bytes by choosing dictionary candidates more intelligently, without changing CSP v1, weakening exact reconstruction, changing Core, or adding a public tuning surface?

**H0 is the production create at the commit under test, not the old sequential PATCH-ENC-004 control.** On every decision runner, `Environment.ProcessorCount` must be at least 2 and every production-real lane is invoked explicitly with H2-W2: two encode workers, no base cache, the existing ordered writer, `4 × W = 8` entries and `W × 4 MiB = 8 MiB` in flight. zstd itself has no worker threads.

H0 encoder policy is:

- zstd level 19;
- K = 4 contiguous base records per dictionary, truncated only at end of the base;
- at most C = 8 candidate starts within ±256 KiB of the target record offset;
- starts nearest-first, lower base index first on an equal offset distance;
- raw-prefix dictionary handling and dictionary hash/chain logs capped at 20;
- cost = frame bytes + 32 bytes per dictionary reference;
- final tie order is raw, then no-dictionary zstd, then the first candidate in candidate order, because the production chooser replaces a winner only on a strict cost decrease.

Every H4/H5/H6/H7/H8/H9 production-real lane uses exactly the same H2-W2 execution topology. Parallel encoding gain is therefore not counted as candidate-policy gain.

## 2. Scope and ownership

PATCH-ENC-005 owns candidate-selection semantics for Patching. [PATCH-GAP-001 protocol PR #217](https://github.com/definitely-stable/ChunkShift/pull/217) / #183 may consume the trace and G2 oracle defined here, but must not implement a second offset/sketch selector.

This experiment may change physical patch bytes (D14/D15). It does **not**:

- change CSP v1, K's format maximum, dictionary-reference encoding or the 1 MiB dictionary limit;
- add a public option, overload or environment-variable tuning knob (D9);
- change the Core API, stable FastCDC profile, HashSuite or identities;
- require Repository, a persistent similarity database or update-set/tree semantics;
- implement PATCH-GAP G3/G4/G5.

## 3. Phase A — cheap changes before resemblance infrastructure

Phase A is always implemented and measured first. H5/H6/H8 are forbidden until the G2 progression gate in §4 passes.

### 3.1 H4 — L1 rank all current candidates, L19 re-encode top two

Lane: `H4-L1-R2`.

Candidate universe is exactly H0's C = 8 / ±256 KiB starts and K = 4 dictionary construction.

For each distinct missing target chunk:

1. Compute raw cost and one **L19 no-dictionary** frame exactly as H0.
2. Read every valid H0 dictionary candidate once and encode it at **zstd level 1** with raw-prefix dictionary semantics. H20/C20 are retained as policy caps; where level 1 already chooses smaller tables the caps do nothing.
3. The cheap ranking key is `(cheapCostBytes, h0CandidateOrdinal)`, where
   `cheapCostBytes = cheapFrameBytes + 32 × dictionaryReferenceCount`.
   Dictionary-reference cost is therefore included before ranking.
4. Retain the two candidates with the smallest key. Candidate bytes may be retained for the L19 re-encode; they must not be re-read merely because they won ranking.
5. Re-encode exactly those **R = 2** candidates at L19.
6. Choose the final stored form by H0's cost function and strict-decrease rule: raw → L19 no-dictionary → ranked L19 candidate 1 → ranked L19 candidate 2.

If fewer than two usable candidates exist, re-encode all usable candidates. Level 1 is a ranking proxy only; an L1 frame is never written to an H4 patch.

Rationale: L1 minimizes proxy cost; R = 2 gives the proxy one correction opportunity without retaining H0's eight expensive L19 dictionary trials. Ranking on frame bytes alone is invalid because the tail of a base may use fewer than four references and the CSP objective includes those bytes.

### 3.2 H7 — content-ranked early exit, never nearest-first early exit

Lane: `H7-L1-R2-E75`. H7 is H4 plus one rule.

All cheap trials run first and candidates are ordered by the H4 ranking key. Encode ranked candidate 1 at L19. Let

`baselineCost = min(targetLength, l19NoDictionaryFrameBytes)`

and

`firstCost = l19FrameBytes(candidate1) + 32 × dictionaryReferenceCount(candidate1)`.

Skip candidate 2 iff the integer relation

`4 × firstCost <= 3 × baselineCost`

holds; otherwise encode candidate 2 and finish exactly as H4.

There is **no** “two consecutive non-improvements” rule. Such a rule would be order-dependent and would systematically privilege offset-nearest candidates. H7 sees all cheap scores before it exits and therefore does not stop because a near-offset candidate happened to be visited first.

H7 is eligible only if it produces the **same patch SHA-256 as H4 for every file on calibration and holdout**. This is an additional no-byte-regression oracle for H7, not a general PATCH-ENC-005 oracle. It proves that the frozen 75% rule skipped no L19 trial that changes H4's result on the frozen corpus.

### 3.3 H9 — fixed level ladder, wider offset search

Separate lanes:

- `H9-L9-K4-C16-R1M`;
- `H9-L12-K4-C16-R1M`;
- `H9-L15-K4-C16-R1M`.

Each uses K = 4, C = 16, radius = 1 MiB, raw-prefix dictionary handling with H20/C20 caps and H2-W2. It performs one no-dictionary frame and up to 16 dictionary frames **at that lane's level**; there is no H4 cheap-ranking stage.

These are deliberately policy-bundle lanes, not a causal “level only” experiment: PATCH-ENC-002 already identified `L9-K4-C16-R1M` as a materially faster point with a different calibration/holdout byte tradeoff. L12 and L15 interpolate compression effort while keeping that candidate search fixed. Results must not be described as the isolated effect of zstd level.

## 4. G2 — exact sampled whole-base oracle before H5/H6

G2 is a research-only byte upper bound. It is **not timed as a production selector** and cannot be adopted.

### 4.1 Frozen target sample

Use calibration only: the two .NET families and their four adjacent-version pairs from `patch-corpus.json`.

For each calibration pair independently, enumerate the same distinct missing target chunks that production create would emit. Define the sample key as SHA-256 over UTF-8 fields separated by a zero byte:

`familyId, baseVersion, targetVersion, normalizedPath, targetChunkIdHex, firstTargetIndex`.

Sort by the 32-byte digest, then by normalized path, then target index. Take the first **64 entries per pair**, or every entry if the pair contains fewer than 64. The maximum sample is therefore 256 target entries.

The implementation PR must materialize this list before the first oracle run and record its SHA-256 as `oracleSampleSha256`. The selection algorithm above, not a measured result, fixes membership.

### 4.2 Candidate universe and exact cost

For each sampled target entry, enumerate **every base record index in the corresponding base file** as a possible K = 4 dictionary start. There is no radius and no candidate-count sample.

A start is valid iff the current `TryMeasureCandidate` semantics produce 1–4 contiguous records totaling at most 1 MiB and the concatenated dictionary passes `CspDictionary.IsUsable`. Encode every valid start at L19 with raw-prefix H20/C20, add exactly 32 bytes per named base chunk, and compare it with raw and L19 no-dictionary using the same strict cost rule.

This is an exact whole-base oracle **for the sampled target entries**. The oracle may shard sampled entries across processes because its timing is not evidence; its output cost must be independent of scheduling.

It does **not** prove:

- that a bounded index can retrieve the oracle start;
- that the same headroom exists for all corpus entries;
- that an implementable selector is fast enough;
- that H5/H6 should be adopted.

It exists only to answer whether non-offset starts have enough byte headroom to justify building Phase B.

### 4.3 Progression gate

Let `H0sample` and `G2sample` be the sums of final stored-form entry cost (frame/raw bytes plus dictionary references) over the frozen sample. Phase B may start only when both hold:

1. `G2sample <= 0.95 × H0sample`; and
2. in at least **two of the four calibration pairs**, that pair's oracle cost is `<= 0.97 ×` its H0 sample cost.

If either condition fails, H5/H6/H8 are recorded as **STOPPED — insufficient oracle headroom** and PATCH-ENC-005 proceeds only with Phase A.

No holdout oracle is inspected before H5/H6 parameters and code are frozen. A later holdout G2 run is informative for #183 only and cannot retune PATCH-ENC-005.

## 5. Phase B — bounded resemblance retrieval

Phase B starts only after §4.3 passes. All selectors preserve the eight H0 offset candidates and add at most eight content candidates. The union therefore contains at most **16 unique starts**.

Every H5/H6 production-real lane feeds that union through **the H4 L1 ranking and L19 R = 2 final trial policy**. Thus a resemblance family changes where candidates come from; it does not silently reintroduce 16 L19 trials.

### 5.1 Families to measure

**N-transform SF is a research control, not an adoption lane.** It is useful to validate feature recall/CPU against the literature, but its repeated transforms over every rolling fingerprint are exactly the cost Finesse and Odess were designed to remove.

**H5-F — Finesse-style fixed-subchunk locality.** Lane: `H5-F12-SF3`.

The feature definition is frozen rather than left to an implementation-specific Rabin library:

- divide a chunk of length L into 12 contiguous subchunks with boundaries `floor(i × L / 12)`, i = 0..12;
- Rabin fingerprint polynomial: `0x3DA3358B4DC173` over GF(2); sliding window: exactly 48 bytes;
- a subchunk feature is the maximum Rabin fingerprint over every 48-byte window wholly inside that subchunk;
- partition the 12 features into four consecutive sets of three, sort each set by unsigned feature value descending (feature index breaks equal-value ties), and form three 4-feature super-features: SF j takes rank j from each of the four sets;
- identify a super-feature by BLAKE3-256 over ASCII `"PATCH-ENC-005/H5F"`, its SF ordinal byte and the four little-endian 64-bit feature values, truncated to the first 64 bits for indexing.

The stable 64 KiB profile has a 16 KiB minimum chunk, so every subchunk is larger than the 48-byte window; no short-subchunk fallback exists in this experiment. This fixes the Finesse-style family completely while keeping it independent of Core's CDC implementation.

**H6-O — Gear/content-defined sampling.** This is a ChunkShift-specific Odess-style family rather than a claim of bit-for-bit Odess reproduction:

- use the stable ChunkShift Gear table identified by `chunkshift.fastcdc.gear.v1` / SHA-256 `91a3061015ae351cd3701852712bcd6aa4a1ce26c8a231d3969432b00f028f88`;
- Patching computes its own deterministic pass; it does not expose or depend on Core's internal rolling state;
- Gear state is `h = (h << 1) + table[byte]` modulo 2^64;
- after the first 32 bytes, a proxy sample is kept when `(h & 0x7f) == 0` (1/128 sampling);
- derive 12 features from the proxy hashes. For transform i in 0..11, let `d = SHA256(ASCII("PATCH-ENC-005/NTRANSFORM/" + invariant(i)))`, `m = UInt32LE(d[0..4]) | 1`, `a = UInt32LE(d[4..8])`; the feature is the maximum of `(m × UInt32(h) + a) mod 2^32` over sampled hashes. An empty proxy set produces feature zero;
- group features in order into **3 groups of 4**, and identify each group by BLAKE3-256 of the tier id plus its four little-endian feature values, truncated to the first 64 bits for the in-memory index.

Lane: `H6-O12-SF3-S128`.

This uses Gear-derived features without changing the Core contract. The EuroSys'26 rolling-hash-reuse result motivates avoiding a second expensive fingerprint family, but ChunkShift does not cross D2 merely to reuse Core-private state.

**H6-P — Palantir-style hierarchy on the same 12 H6 features.** It performs no second byte scan and no second rolling hash. It adds the ASPLOS'24 hierarchy:

- tier 1: `(k,s) = (3,4)`;
- tier 2: `(4,3)`;
- tier 3: `(6,2)`.

Within each tier, partition the 12 feature values in feature-index order into k consecutive groups of s and hash each group as above with the tier id. There are 3 + 4 + 6 = **13 indexed super-features per indexed base record**. Query tier 1, then 2, then 3 until eight unique sketch starts are collected; lower tiers never outrank an already found higher-tier match solely because of offset.

Lane: `H6-P12-T3-S128`.

Palantir's generational backup-history machinery is not imported: CSP create has one exact base file and no persistent similarity database. The experiment borrows only hierarchical sensitivity over the same per-chunk features.

### 5.2 Bounded index and deterministic retrieval

The index is rebuilt for every base file/create; build time and the full sequential base read are charged to create. No cross-create cache, Repository service or hidden precomputation is allowed.

Index format for the experiment is sorted packed postings `(feature64, baseStartIndex32)`. A posting is 12 logical bytes. Hard limits:

- maximum postings: **2,097,152**;
- maximum index-owned live managed + native bytes: **64 MiB**;
- feature posting lists with more than **256** starts are non-discriminating and ignored at query time.

Before reading base content, use the already loaded base manifest to select indexed records. For a family with P postings per indexed record (P = 3 for H5-F/H6-O; P = 13 for H6-P), choose the smallest power-of-two stride S such that the actual count of base records satisfying

`Low64(BLAKE3(baseChunkId bytes)) & (S - 1) == 0`

times P is at most 2,097,152. S = 1 indexes every record. This decision uses ChunkIds only, so the entire base content still needs just one sequential feature-build read.

For a target:

1. look up its super-features, ignoring hot lists >256;
2. score each base start by matched super-feature count; H6-P first compares tier (1 before 2 before 3), then matched count descending;
3. remaining ties use absolute target/base offset distance, then lower base start index;
4. take at most eight sketch starts;
5. union them with all eight H0 offset starts, preserving source as `offset`, `sketch` or `both`;
6. H4 ranks the unique union at L1 and re-encodes top two at L19.

The selected dictionary is still verified from its bytes against every named base `ChunkId` before writing the entry.

### 5.3 Index/create memory bound

Phase A lanes (H0/H4/H7/H9) must stay within the current H2-W2 D17 create bound: **96 MiB over idle**.

H5/H6/H8 may add the 64 MiB index budget, so their create bound is **160 MiB over idle**. Exceeding either the logical index bound or 160 MiB measured peak is a rejection, even if the lane is faster/smaller.

Apply has no index and keeps the existing **64 MiB over-idle** bound.

## 6. H8 — incompressibility gate only after resemblance exists

H8 is evaluated only on the best still-eligible H5/H6 retrieval family after its parameters have been frozen. It never precedes G2/H5/H6.

The parent selector still computes its sketch query and all L1 cheap scores. H8 suppresses all L19 dictionary re-encodes for an entry only when all three conditions hold:

1. `l19NoDictionaryFrameBytes >= ceil(0.98 × targetLength)`;
2. the resemblance query produced **zero sketch candidates** (offset-only candidates do not count as resemblance evidence);
3. the best H4 cheap candidate total cost is `>= ceil(0.98 × targetLength)`.

Otherwise the parent selector runs unchanged.

This deliberately does **not** infer incompressibility from the no-dictionary frame alone. A small edit inside compressed/high-entropy-looking data can make no-dictionary zstd ineffective while the old version remains an excellent dictionary.

H8 is eligible only if it produces the **same patch SHA-256 as its parent H5/H6 lane for every calibration and holdout file**. Its purpose is to remove provably unnecessary expensive trials on the frozen corpus, not to trade bytes for speed.

## 7. Shared candidate trace contract for #181/#183

Schema id: `chunkshift.patch-candidate-trace.v1`.

One JSON document is written per changed file. The header contains:

- schema, ExperimentId, RunId, commit, platform and lane;
- corpus `pairsSha256`, family/pair id and normalized path;
- base and target `ManifestId`.

The document contains one record per distinct missing target chunk, in production first-occurrence order:

| field | type / meaning |
| --- | --- |
| `targetIndex` | first target record index |
| `targetChunkId` | exact ChunkId hex |
| `targetOffset`, `targetLength` | bytes |
| `candidateCount` | unique candidates considered by the selector |
| `expensiveTrialCount` | dictionary L19 trials actually executed |
| `selectedEncoding` | raw / zstd / zstd-dictionary |
| `selectedCandidate` | candidate ordinal or null |
| `storedBytes` | selected raw/frame bytes, excluding dictionary refs |
| `dictionaryRefs` | selected reference count |
| `candidates[]` | compact candidate rows below |

Each candidate row contains:

- `ordinal`;
- `startIndex`, `startOffset`, `recordCount` and `firstChunkId`; together with the base ManifestId these identify the dictionary window;
- `source`: exactly `offset`, `sketch` or `both`;
- `cheapFrameBytes` and `cheapCostBytes`, nullable where the lane has no cheap stage;
- `l19FrameBytes` and `l19CostBytes`, nullable when no L19 dictionary trial ran;
- `selected` boolean.

The trace stores **no target bytes, dictionary bytes or frame payload bytes**. G2 oracle rows are separate and name their best base start; `source` is not extended with an `oracle` value. PATCH-GAP-001 consumes this schema and the G2 artifact rather than inventing selector semantics.

## 8. Corpus, calibration and holdout discipline

The frozen corpus is the one in [PATCH-PREFREEZE-PROTOCOL.md](PATCH-PREFREEZE-PROTOCOL.md):

- calibration: `dotnet-aspnetcore-win-x64`, `dotnet-runtime-linux-arm64`;
- holdout: `node-win-x64`, `node-linux-x64`, `tzdata`, `chunkshift-source`;
- lock: `pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.

No threshold, feature family, index bound, fanout limit, lane parameter or tie rule may change after any holdout result is viewed. Calibration may choose among the lanes already frozen here; holdout only confirms or rejects those calibration finalists.

## 9. Decision metrics and paired timing

### 9.1 Metrics

Per file, lane, repetition and platform where applicable:

- physical patch bytes and SHA-256;
- create wall seconds and process CPU seconds;
- managed allocated bytes;
- base reads, bytes read and seeks;
- candidate count and L19 dictionary-trial count;
- dictionary entries and references;
- apply wall/CPU, base reads and target SHA-256;
- H5/H6/H8 index build wall/CPU, bytes scanned, posting count, ignored-hot-feature count, index peak bytes and candidate source statistics;
- peak working set over idle in explicit memory runs.

Aggregates include total patch bytes B, total create wall/CPU, candidate/L19 trials per payload entry, apply totals, maximum RSS over idle, and for H5/H6 the share of selected dictionaries sourced from sketch/both and the share whose start lies outside ±256 KiB.

CPU is a first-class reported/Pareto metric, but the user-visible create eligibility thresholds in #181 are wall-time thresholds. Wall improvement is never inferred from CPU alone.

### 9.2 Paired timing and noise rule

Decision timing runs on:

- `ubuntu-24.04` linux-x64;
- `ubuntu-24.04-arm` linux-arm64;
- `windows-2025` win-x64;
- .NET 10 JIT, same commit, same corpus materialization and explicit H2-W2 execution.

A runner with fewer than two available processors is invalid.

For one phase/split/platform:

1. run one H0 warm-up over that split; exclude it;
2. run **five measured rounds**;
3. every round starts with H0, runs every candidate lane once, and ends with H0;
4. rotate candidate order cyclically by one position each round;
5. normalize a candidate in a round to the arithmetic mean of that round's two bracketing H0 totals;
6. use the median of the five ratios as the platform ratio.

A round is invalid if its two H0 wall totals differ by more than **25% of their mean**. Any invalid round invalidates that platform dispatch. Retry the dispatch once; a second invalid dispatch makes timing evidence **INCOMPLETE**, never an automatic pass/fail.

For a threshold to hold on a platform, the median ratio must satisfy it and at least **4 of 5** round ratios must satisfy it.

Apply is run **five times per patch** with fresh output and the per-file median is aggregated. The first apply verifies the target SHA-256. Create is not hidden behind file-level parallelism; H2-W2 is the only intra-create parallelism.

### 9.3 Memory runs

Memory is a separate group in the same decision dispatch/RunId. Every changed file with target size >=1 MiB is created/applied in a child process, with the same allocator/GC environment and idle-baseline method as PATCH-ENC-004/PATCH-APPLY-001. The evaluator rejects memory evidence from another commit, RunId, lane definition or file set.

## 10. Eligibility, Pareto selection and complexity preference

Define for a split:

- `b = B(candidate) / B(H0)`;
- `w_p` = paired median create-wall ratio on platform p;
- `c_p` = paired median create-CPU ratio on platform p;
- `a_p` = aggregate apply-wall ratio on platform p.

A production-real lane is eligible only if:

1. independent decode/apply/correctness rules in §12 pass;
2. its patch SHA-256 for a given file is identical across the three architectures (it may differ from H0);
3. `a_p <= 1.10` on **all three** platforms;
4. its §5.3 create and apply memory bounds hold on **all three** platforms;
5. one #181 branch holds on **all three** platforms:
   - **speed branch:** `w_p <= 0.50` for every p and `b <= 1.02`; or
   - **size branch:** `b <= 0.97` and `w_p <= 1.50` for every p.

H7 additionally needs H4 byte equality (§3.2); H8 additionally needs parent byte equality (§6).

On calibration, form the Pareto set over:

- b;
- `max_p(w_p)`;
- `max_p(c_p)`.

Lower is better. A lane is removed only when another eligible lane is no worse in all three and strictly better in at least one. Memory/apply are hard gates, not weighted objectives.

### 10.1 Explicit complexity preference

H0/Phase-A lanes require no full-base resemblance index. H5/H6/H8 do.

An indexed lane is **near-equivalent** to an eligible Phase-A lane when the Phase-A lane has:

- patch bytes no more than **0.5% larger**;
- max wall ratio no more than **10% larger**; and
- max CPU ratio no more than **10% larger**.

When such a Phase-A lane exists, the indexed lane is removed from the finalist set. A full-base scan/index must buy a material measured gain; “almost equal” does not justify the extra subsystem.

There is no weighted score. From the remaining Pareto set, name:

- **speed finalist**: smallest `max_p(w_p)`, then smaller b, then smaller `max_p(c_p)`, then lexicographically smaller lane id;
- **size finalist**: smallest b, then smaller `max_p(w_p)`, then smaller `max_p(c_p)`, then lexicographically smaller lane id.

If both names resolve to one lane, it is the sole calibration finalist. If they differ, both go to holdout. This is not a tie: it is an explicit unresolved speed/size tradeoff.

## 11. Holdout confirmation

The calibration evaluator records each finalist and the branch(es) it satisfied before holdout is run. Holdout uses the same commit and frozen parameters.

A finalist is confirmed only when:

- all correctness, cross-platform determinism, apply and memory gates hold;
- the **same calibration qualification branch** holds on holdout. A finalist recorded as satisfying both branches must satisfy both on holdout;
- H7/H8 retain their parent byte-equality oracle.

If one of two calibration finalists fails, the other may be adopted if confirmed. If both are confirmed and remain non-dominated with materially different speed/size tradeoffs, the experiment result is **DEFER**, not an invented scalar preference; D15 stays unchanged until a separately frozen product tradeoff rule exists.

Missing platform/run data yields **INCOMPLETE**.

## 12. Correctness and failure oracles

PATCH-ENC-005 is allowed to change patch bytes, so H0 SHA equality is not a general correctness oracle.

Before any decision run, the implementation/lab PR must pass:

- the independent Python CSP decoder for every generated scenario/vector patch;
- production apply and exact target SHA-256 for every decision patch;
- cross-platform patch SHA equality for each lane/file;
- short reads on base manifest/content, target manifest/content and patch input;
- cancellation before and during create/apply;
- corrupt/truncated patch and wrong/corrupt base semantics;
- existing P6 failure-point and P9 fuzz regressions;
- existing committed CSP vectors unchanged and still decodable;
- new committed creation vectors for any newly representable/default selector outcome where a vector adds coverage, without deleting old compatibility vectors.

A candidate that is never selected cannot affect correctness. A selected dictionary is still verified against its exact base ChunkIds before the patch is written. Final target verification on apply remains authoritative.

## 13. Evidence retention and run identity

Decision RunIds are `PATCH-ENC-005/RUN-YYYYMMDD-NNN-<commit>-<platform>`; evidence follows `RESULT-TEMPLATE.md`.

Every decision record must commit enough data to recompute the verdict without a GitHub artifact:

- protocol/evaluator version and exact commit;
- corpus lock and any oracle-sample/feature-definition fingerprints;
- per-file patch bytes/SHA/correctness verdict;
- per-round aggregate timing and CPU values with both H0 brackets;
- per-file memory peaks for the memory set;
- candidate/source/trial aggregates and the compact trace projection needed by G2/#183;
- `verdict.json`, a human-readable summary and a small independent recomputation script;
- `artifacts.json` containing artifact ids/names, size, expiry and SHA-256 of each ZIP and every raw document inside it.

Large raw workflow artifacts may expire, but **no adoption/rejection fact may exist only in them**. The committed compact evidence is the durable decision input; `artifacts.json` is the durable SHA-256 manifest for the full raw capture.

No payload/dictionary bytes are written to trace/evidence unless a minimized regression fixture is separately reviewed and committed.

## 14. Implementation staging after protocol merge

The protocol merge does not alter D15 or `CspEncoderPolicy.Default`.

Implementation order is fixed:

1. trace/evaluator seams and Phase-A lab-only policies H4/H7/H9;
2. correctness/failure tests and paired-run support;
3. Phase-A calibration decision run;
4. G2 sample materialization and exact calibration oracle;
5. only if §4.3 passes: H5-F/H6-O/H6-P lab implementation and bounded index;
6. H8 only after a resemblance finalist exists;
7. holdout confirmation of calibration finalist(s);
8. only after an ADOPT evidence record: a separate production-default PR updates D15/default and runs JIT + NativeAOT package smoke and vector/fuzz checks.

The final decision runs in steps 3/4/7 are forbidden on this protocol PR.

## 15. Research rationale

The resemblance families are intentionally narrower than the literature survey:

- Finesse (FAST'19) keeps the super-feature model but replaces N-transform's repeated transforms with fixed-subchunk feature locality; its paper uses 12 features and three 4-feature super-features and reports materially faster feature computation with comparable compression.
- Odess (ICDE'21) uses Gear hashing plus content-defined sampling before transforms; the paper's evaluated setup includes 1/128 sampling and 12 features / 3 super-features. H6 adopts that direction, not an opaque external index.
- Palantir (ASPLOS'24) shows why one fixed super-feature threshold can miss candidates and uses the three `(3,4)/(4,3)/(6,2)` tiers. H6-P evaluates only that hierarchy over H6's existing features; it does not import backup-history state.
- Argus (ACM TOS'26) is directly relevant: its bin-wise partitioning plus fine-grained Gear/plain-feature design addresses duplicate/useless features in earlier super-feature schemes. It is **not** a fourth selector lane in PATCH-ENC-005: H6-O first establishes whether a bounded Gear-derived signal recovers enough of G2 on CSP's 64 KiB chunks. If H6-O/H6-P leave material G2 headroom that can plausibly be feature-recall loss, an Argus-style bin-wise lane requires a new ExperimentId rather than post-freeze expansion of this grid.
- *Once Rolling Hashing is Enough* (EuroSys'26) is relevant to avoiding duplicate rolling work. In ChunkShift the reusable Core Gear state is internal by design, so H6 recomputes a small Patching-side pass rather than changing the Core contract.
- ML/embedding selectors are not in this experiment: they add model/runtime/training and NativeAOT deployment complexity before G2 has shown that CSP has enough candidate-choice headroom to justify any large selector.

Primary references:

- [Finesse, FAST'19](https://www.usenix.org/conference/fast19/presentation/zhang)
- [Odess, ICDE'21, DOI 10.1109/ICDE51399.2021.00048](https://doi.org/10.1109/ICDE51399.2021.00048)
- [Palantir, ASPLOS'24, DOI 10.1145/3620665.3640353](https://doi.org/10.1145/3620665.3640353)
- [Argus, ACM TOS'26, DOI 10.1145/3747839](https://doi.org/10.1145/3747839)
- [Once Rolling Hashing is Enough, EuroSys'26, DOI 10.1145/3767295.3803596](https://doi.org/10.1145/3767295.3803596)

## 16. Known protocol traps fixed by this freeze candidate

- The legacy exploratory `benchmarks/scripts/csp_encoding_study.py` is **not** a production candidate oracle: its historical start window extends the upper search side by target length, whereas production `FindCandidateStarts` is centered on target offset. PATCH-ENC-005 decision code must call/shared-test production candidate semantics rather than silently inheriting the script's approximation.
- H7 does not use consecutive-nearest misses.
- G2 does not pretend an all-target × all-base L19 Cartesian product is a practical decision run; it is exhaustive only over a deterministic calibration target sample and the entire corresponding base.
- H8 never uses no-dictionary incompressibility alone.
- H5/H6 build/read/index cost is charged to create; no hidden persistent index is assumed.
- H9 is labeled a level+search policy bundle, so its result cannot be misattributed to level alone.

## 17. Freeze checklist

The protocol is ready for freeze review when this document and PR metadata agree:

- [x] H0 is the current production H2-W2 baseline;
- [x] H4 level, candidate count, re-encode count, reference-cost ranking and ties are fixed;
- [x] H7 exit rule is numeric, content-ranked and has a no-byte-regression oracle;
- [x] H9 levels/K/C/radius and execution are fixed;
- [x] G2 target sample, full-base candidate universe, cost and progression gate are exact;
- [x] H5/H6 families, retrieval union, index/fanout/memory bounds and determinism are fixed;
- [x] H8 can only follow resemblance and protects compressed-data edits with three signals plus parent-byte equality;
- [x] the shared #181/#183 trace schema is fixed and carries no payload bytes;
- [x] calibration/holdout membership and corpus lock are fixed;
- [x] wall/CPU roles, platforms, paired five-round timing and noise invalidation are fixed;
- [x] create/apply memory bounds and explicit memory runs are fixed;
- [x] Pareto, complexity preference, finalists and holdout confirmation are deterministic without a weighted score;
- [x] independent decoder, target SHA-256, apply, failure, short-read, cancellation, P6/P9 and vector obligations are explicit;
- [x] durable compact evidence and raw-artifact SHA-256 manifest requirements are fixed;
- [x] CSP/Core/Repository/GAP scope boundaries are explicit;
- [x] no production selector/default/D15 change or decision run is part of this protocol PR.

The PR remains Draft until review agrees that these frozen choices are implementable. Merging this protocol, not merely checking the boxes above, is the gate for implementation decision work.
