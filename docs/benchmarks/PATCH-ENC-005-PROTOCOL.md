# PATCH-ENC-005 protocol: better dictionary candidates for less work

Status: **DRAFT freeze candidate — parameters are fixed below; this PR must be reviewed, frozen and merged before implementation decision runs.**  
Issue: [#181](https://github.com/definitely-stable/ChunkShift/issues/181) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
ExperimentId: `PATCH-ENC-005`  
Protocol baseline: `e967aeb6d4d467e94c5ac20f85e70ba0035d998d` (PATCH-ENC-004 H2-W2 is already the production create default)  
Corpus lock: `pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`  
Prior evidence: [PATCH-ENC-002](../research/results/PATCH-ENC-002-EVIDENCE-20260928-001.md), [PATCH-ENC-003](../research/results/PATCH-ENC-003-EVIDENCE-20260929-001.md), [PATCH-ENC-004](../research/results/PATCH-ENC-004-EVIDENCE-20261001-001.md).

A change after freeze to a lane, numeric parameter, sampling rule, metric, memory bound, eligibility threshold, evaluation rule or fresh-confirmation rule requires a new ExperimentId. Exploratory runs before this protocol is merged are never decision data.

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

Phase A is always implemented and measured first. No resemblance implementation starts before the G2 oracle is measured. If the main G2 gate misses, §4.3 permits exactly one pre-frozen H6-O false-negative guard; H5-F/H6-P/H8 remain forbidden until either the main gate or that guard opens full Phase B.

### 3.1 H4 — L1 rank all current candidates, L19 re-encode top two

Lane: `H4-L1-R2`.

Candidate universe is exactly H0's C = 8 / ±256 KiB starts and K = 4 dictionary construction.

For each distinct missing target chunk:

1. Compute raw cost and one **L19 no-dictionary** frame exactly as H0.
2. Read every valid H0 dictionary candidate once and encode it at **zstd level 1** with raw-prefix dictionary semantics. H20/C20 are retained as policy caps; where level 1 already chooses smaller tables the caps do nothing.
3. The cheap ranking key is `(cheapCostBytes, selectorCandidateOrdinal)`, where
   `cheapCostBytes = cheapFrameBytes + 32 × dictionaryReferenceCount`.
   Dictionary-reference cost is therefore included before ranking. In H4, `selectorCandidateOrdinal` is exactly H0's candidate ordinal. In Phase B it is the deterministic union ordinal defined in §5.2.
4. Retain the two candidates with the smallest key **and their dictionary bytes**. The implementation may use one scratch dictionary buffer plus two retained-winner buffers per encode worker; it must not retain all candidate dictionaries. A winner must not be re-read merely because it reached L19.
5. Re-encode exactly those **R = 2** candidates at L19.
6. Choose the final stored form by H0's cost function and strict-decrease rule: raw → L19 no-dictionary → ranked L19 candidate 1 → ranked L19 candidate 2.

If fewer than two usable candidates exist, re-encode all usable candidates. Level 1 is a ranking proxy only; an L1 frame is never written to an H4 patch.

Rationale: L1 minimizes proxy cost; R = 2 gives the proxy one correction opportunity without retaining H0's eight expensive L19 dictionary trials. Ranking on frame bytes alone is invalid because the tail of a base may use fewer than four references and the CSP objective includes those bytes.

### 3.2 H7 — content-ranked early exit, never nearest-first early exit

Lane: `H7-L1-R2-E75`. H7 is H4 plus one rule.

All cheap trials run first and candidates are ordered by the H4 ranking key. With zero or one usable candidate H7 is exactly H4. With at least two, encode ranked candidate 1 at L19. Let

`baselineCost = min(targetLength, l19NoDictionaryFrameBytes)`

and

`firstCost = l19FrameBytes(candidate1) + 32 × dictionaryReferenceCount(candidate1)`.

Skip candidate 2 iff the integer relation

`4 × firstCost <= 3 × baselineCost`

holds; otherwise encode candidate 2 and finish exactly as H4.

There is **no** “two consecutive non-improvements” rule. Such a rule would be order-dependent and would systematically privilege offset-nearest candidates. H7 sees all cheap scores before it exits and therefore does not stop because a near-offset candidate happened to be visited first.

H7 is eligible only if it produces the **same patch SHA-256 as H4 on every dataset on which H7 is evaluated**; if H7 reaches fresh confirmation, equality is required there too. This is an additional no-byte-regression oracle for H7, not a general PATCH-ENC-005 oracle. It proves that the frozen 75% rule skipped no L19 trial that changes H4's result on the frozen corpus.

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

For each calibration pair independently, enumerate the same distinct missing target chunks that production create would emit. Define the sample key as SHA-256 over these six fields joined by one `00` byte and **no trailing separator**:

`familyId, baseVersion, targetVersion, normalizedPath, targetChunkIdHex, firstTargetIndex`.

The first four fields are their exact UTF-8 manifest/path strings; `targetChunkIdHex` is 64 lowercase ASCII hex characters; `firstTargetIndex` is invariant unsigned decimal ASCII with no leading zeros except `0`.

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

### 4.3 Prioritization gate and false-negative guard

Let `H0sample` and `G2sample` be the sums of final stored-form entry cost (frame/raw bytes plus dictionary references) over the frozen calibration sample. The **main G2 gate** passes when both hold:

1. `G2sample <= 0.95 × H0sample`; and
2. in at least **two of the four calibration pairs**, that pair's oracle cost is `<= 0.97 ×` its H0 sample cost.

A pass authorizes the full Phase-B grid in §5.

A miss does **not** prove that resemblance is useless. PATCH-ENC-002 already showed a distribution shift: widening offset search barely moved the .NET calibration bytes while materially improving the old Node/source partition. Therefore a .NET-only sampled oracle may false-negative the exact moved-content signal H5/H6 target.

When the main G2 gate misses, the implementation may build **only** the already-frozen `H6-O12-SF3-S128` lane as a false-negative guard; H5-F, H6-P and H8 remain forbidden. Run that guard first on linux-x64 over the **fixed evaluation split** in §8, with H0 bracketing and the same correctness/index/memory rules. It opens the full Phase-B grid only when all hold:

- `B_eval(H6-O) <= 0.97 × B_eval(H0)`;
- paired create wall `<= 1.50 × H0`;
- create peak `<= 160 MiB` over idle;
- exact reconstruction and cross-run deterministic bytes.

If the guard misses, record H5-F/H6-P/H8 as **STOPPED — sampled G2 miss confirmed by the pre-frozen H6-O guard**. H6-O's guard result remains research evidence but cannot be adopted from the one-platform screen.

This preserves the intended ordering: G2 is always measured before resemblance infrastructure, yet a known calibration-distribution weakness cannot silently suppress all resemblance work.

The legacy evaluation split is not an untouched holdout and may be used for this progression decision. The **fresh confirmation set** in §8 is never opened for G2 progression, selector pruning or parameter choice.

No oracle over the fresh confirmation set is inspected before the production-real finalist(s) are frozen. A later sampled oracle there is descriptive for #183 only and cannot retune PATCH-ENC-005.

## 5. Phase B — bounded resemblance retrieval

Full Phase B starts after the §4.3 main gate passes, or after its one permitted H6-O false-negative guard opens the grid. The H6-O guard itself is the sole exception to the full-grid gate. All selectors preserve the eight H0 offset candidates and add at most eight content candidates. The union therefore contains at most **16 unique starts**.

Every H5/H6 production-real lane feeds that union through **the H4 L1 ranking and L19 R = 2 final trial policy**. Thus a resemblance family changes where candidates come from; it does not silently reintroduce 16 L19 trials.

### 5.1 Normative feature byte semantics and families

Selector bytes must be reproducible independently of a particular .NET implementation. The following definitions are normative.

**Integer and hash helpers**

- `U32LE(x)` and `U64LE(x)` are exactly 4/8 bytes, least-significant byte first.
- `SHA256(x)` is the 32 raw digest bytes.
- `Key64(domain, payload) = UInt64LE(SHA256(ASCII(domain) || 0x00 || payload)[0..8])`.
- A displayed `Key64` is the unsigned integer written as 16 lowercase hexadecimal digits.
- ChunkId input to a selector hash is the raw 32 ChunkId bytes, not its hex text.

**H5 Rabin reference semantics**

H5 uses a non-reflected, MSB-first GF(2) remainder of width 64 with polynomial

`P(x) = x^64 + 0x003DA3358B4DC173`.

The lower constant's bit k is the coefficient of x^k; x^64 is implicit. The initial remainder is zero. Input bytes are consumed in increasing address order and each byte MSB-first. The normative `Rabin48` for one 48-byte window is intentionally specified from scratch so a rolling optimization cannot define semantics:

```text
r = 0
for byte in window[0..47]:
    for bit = 7 down to 0:
        carry = (r >> 63) & 1
        r = ((r << 1) | ((byte >> bit) & 1)) & 0xffffffffffffffff
        if carry != 0:
            r ^= 0x003DA3358B4DC173
return r
```

An implementation may use removal tables/rolling updates only when tests prove byte-for-byte equality with this reference for arbitrary overlapping windows.

**Normative vectors**

| vector | input / preimage | expected |
| --- | --- | --- |
| Rabin zero | 48 × `00` | `0000000000000000` |
| Rabin sequence | bytes `00 01 ... 2f` | `a37764d925385c09` |
| Rabin FF | 48 × `ff` | `f028667cb9141fe0` |
| transform i=0 | SHA-256 of ASCII `PATCH-ENC-005/NTRANSFORM/0` | digest `26d817cc25bd7deefff034120e80e42332fde80917014280d8273df9b807daaa`; `m=cc17d827`, `a=ee7dbd25` |
| H5 key | domain `PATCH-ENC-005/H5F/SF`; payload `00 || U64LE(1) || U64LE(2) || U64LE(3) || U64LE(4)` | `Key64=2653b8064dced72c` |
| H6-O key | domain `PATCH-ENC-005/H6O/SF3`; payload `02 || U32LE(1) || ... || U32LE(4)` | `Key64=fb14428b9f29aa0e` |
| H6-P key | domain `PATCH-ENC-005/H6P/TIER`; payload `01 00 || U32LE(1) || ... || U32LE(4)` | `Key64=4ebbf4b747872713` |
| stride key | domain `PATCH-ENC-005/INDEX-STRIDE`; payload 32 × `00` | `Key64=33ef21b43b1b962b` |

The implementation PR must add these vectors plus randomized reference-vs-optimized Rabin tests before any candidate result is accepted.

**N-transform SF is a literature/control family, not a lane in this ExperimentId.** Its repeated transforms over every rolling fingerprint are exactly the cost Finesse and Odess were designed to remove. G2 is the stronger CSP-specific upper bound; adding an N-transform implementation would add work without changing the production decision grid. If a later study needs N-transform recall as a standalone result, it gets its own recorded lane/ExperimentId rather than appearing post-freeze here.

**H5-F — Finesse-style fixed-subchunk locality.** Lane: `H5-F12-SF3`. This is a CSP adaptation of Finesse's feature/grouping construction, not a claim of byte-for-byte reproduction of the original prototype.

For a chunk of length L:

- subchunk i is `[floor(i × L / 12), floor((i + 1) × L / 12))`, i = 0..11;
- its feature is the maximum unsigned `Rabin48` of every 48-byte window wholly inside that subchunk;
- features are divided into the four consecutive triples `[0,1,2]`, `[3,4,5]`, `[6,7,8]`, `[9,10,11]`;
- within each triple sort by feature value descending; equal values put the **lower feature index first**;
- SF ordinal j = 0,1,2 takes rank j from the four triples, in triple order;
- its index key is `Key64("PATCH-ENC-005/H5F/SF", byte(j) || U64LE(v0) || U64LE(v1) || U64LE(v2) || U64LE(v3))`.

The stable 64 KiB profile has a 16 KiB minimum chunk, so every subchunk is larger than 48 bytes; no short-subchunk fallback exists.

**H6-O — Gear/content-defined sampling.** Lane: `H6-O12-SF3-S128`. This is a ChunkShift-specific Odess-style family:

- use the stable ChunkShift Gear table identified by `chunkshift.fastcdc.gear.v1` / SHA-256 `91a3061015ae351cd3701852712bcd6aa4a1ce26c8a231d3969432b00f028f88`;
- Patching computes its own deterministic pass; it does not expose or depend on Core's internal rolling state;
- start `h = 0`; for each byte in increasing offset order, `h = ((h << 1) + table[byte]) mod 2^64`;
- after updating h for a byte, keep that h when `(h & 0x7f) == 0`;
- if a chunk produces no sample, its terminal h is the sole fallback proxy;
- for transform i = 0..11, `d = SHA256(ASCII("PATCH-ENC-005/NTRANSFORM/" + decimal-i-with-no-leading-zero))`, `m = UInt32LE(d[0..4]) | 1`, `a = UInt32LE(d[4..8])`; feature i is the minimum unsigned `(m × Low32(h) + a) mod 2^32` over proxies;
- group features as `[0..3]`, `[4..7]`, `[8..11]`;
- group g's key is `Key64("PATCH-ENC-005/H6O/SF3", byte(g) || U32LE(f0) || U32LE(f1) || U32LE(f2) || U32LE(f3))`.

The min-wise selection follows the Odess/N-transform construction; SHA-derived transform pairs replace generated random pairs only to make the experiment reproducible.

**H6-P — Palantir-style hierarchy over the same 12 H6 features.** Lane: `H6-P12-T3-S128`. It performs no second byte scan and no second rolling hash.

- tier 1 id = 1: `(k,s)=(3,4)`, consecutive groups `[0..3]`, `[4..7]`, `[8..11]`;
- tier 2 id = 2: `(4,3)`, four consecutive groups of three;
- tier 3 id = 3: `(6,2)`, six consecutive groups of two;
- for tier t and zero-based group g, key = `Key64("PATCH-ENC-005/H6P/TIER", byte(t) || byte(g) || U32LE(features in group order))`.

There are 13 postings per indexed record. Retrieval processes tier 1, then 2, then 3. Inside one tier, aggregate the number of matching keys per base start, sort by matched-key count descending, then absolute target/base offset distance, then lower base start index; append unseen starts until eight sketch starts have been collected. A lower tier never displaces a start already admitted by a higher tier.

Palantir's generational backup-history machinery is not imported: CSP create has one exact base file and no persistent similarity database.

### 5.2 Bounded index and deterministic retrieval

The index is rebuilt for every base file/create. The build is a complete **single-threaded sequential prepass before the target payload producer starts**; its wall/CPU time and full base read are charged to create and are not overlapped with H2-W2 encoding. The prepass computes features/postings and discards base payload bytes; later L1/L19 dictionary trials reread candidate bytes through the ordinary H2 base source. Every base record selected for indexing is hashed with the patch HashSuite and must equal its manifest `ChunkId` **before any posting derived from it is published to the in-memory index**. Unindexed records are read/discarded but need no new eager hash. Target features are computed from the already target-hash-verified chunk bytes owned by the payload work item, never by a second target-stream pass. No cross-create cache, Repository service or hidden precomputation is allowed.

Index format for the experiment is sorted packed postings `(featureKey64, baseStartIndex32)`, sorted first by unsigned `featureKey64`, then ascending base start index. A posting is 12 logical bytes. A 64-bit key collision is deliberately a deterministic false-positive retrieval, never a correctness shortcut; exact L1/L19 trials and chosen-dictionary ChunkId verification remain authoritative.

Hard limits:

- maximum postings: **2,097,152**;
- maximum index-owned live managed + native bytes: **64 MiB**;
- a posting list with more than **256** starts for the same 64-bit key is non-discriminating and ignored at query time.

Before reading base content, use the already loaded base manifest to select indexed records. Define

`strideKey = Key64("PATCH-ENC-005/INDEX-STRIDE", rawBaseChunkId32)`.

For a family with P postings per indexed record (P = 3 for H5-F/H6-O; P = 13 for H6-P), choose the smallest power-of-two stride S such that

`count(records where (strideKey & (S - 1)) == 0) × P <= 2,097,152`.

S = 1 indexes every record. If no S representable as a positive 32-bit power of two satisfies the bound, the lane is invalid rather than silently changing the index policy.

For H5-F/H6-O target lookup, aggregate matched super-feature keys by base start and sort by matched-key count descending, then absolute target/base offset distance, then lower base start index. H6-P uses its tier procedure in §5.1.

Then:

1. take at most eight sketch starts;
2. union them with all H0 offset starts;
3. duplicate starts collapse to one row with source `both`; otherwise source is `offset` or `sketch`;
4. apply production candidate validity: K = 4 `TryMeasureCandidate` semantics, dictionary bytes <= 1 MiB and `CspDictionary.IsUsable`; an invalid sketch hit is not counted and consumes no L1/L19 trial;
5. assign `selectorCandidateOrdinal` after deduplication: all valid H0 offset starts first in H0 ordinal order, then valid sketch-only starts in their deterministic sketch order; a `both` start keeps its H0 position;
6. run H4's L1 ranking over the valid unique union and re-encode only its top two at L19.

The selected dictionary is still verified from its bytes against every named base `ChunkId` before writing the entry.

### 5.3 Index/create memory bound

Phase A lanes (H0/H4/H7/H9) must stay within the current H2-W2 D17 create bound: **96 MiB over idle**.

H5/H6/H8 may add the 64 MiB index budget, so their create bound is **160 MiB over idle**. Exceeding either the logical index bound or 160 MiB measured peak is a rejection, even if the lane is faster/smaller.

Apply has no index and keeps the existing **64 MiB over-idle** bound.

## 6. H8 — incompressibility gate only after resemblance exists

H8 is evaluated on exactly one deterministic parent after H5/H6 calibration. Among indexed H5-F/H6-O/H6-P lanes that are eligible under §10 on calibration, choose the parent with the smallest calibration bytes; ties go to smaller `max_p(w_p)`, then smaller `max_p(c_p)`, then lexicographically smaller lane id. If no indexed lane is eligible, H8 is NOT_RUN. H8 never precedes G2/H5/H6.

The parent selector still computes its sketch query and all L1 cheap scores. If the parent has no valid dictionary candidate, it already executes no dictionary L19 trial and H8 changes nothing. Otherwise H8 suppresses all L19 dictionary re-encodes for an entry only when all three conditions hold:

1. `l19NoDictionaryFrameBytes >= ceil(0.98 × targetLength)`;
2. the resemblance query produced **zero sketch candidates** (offset-only candidates do not count as resemblance evidence);
3. the best H4 cheap candidate total cost is `>= ceil(0.98 × targetLength)`.

Otherwise the parent selector runs unchanged.

This deliberately does **not** infer incompressibility from the no-dictionary frame alone. A small edit inside compressed/high-entropy-looking data can make no-dictionary zstd ineffective while the old version remains an excellent dictionary.

H8 is eligible only if it produces the **same patch SHA-256 as its parent H5/H6 lane on every dataset on which H8 is evaluated**; if H8 reaches fresh confirmation, equality is required there too. Its purpose is to remove provably unnecessary expensive trials on the frozen corpus, not to trade bytes for speed.

## 7. Shared candidate trace contract for #181/#183

Schema id: `chunkshift.patch-candidate-trace.v1`.

One JSON document is written per changed file. The header contains:

- schema, ExperimentId, RunId, protocol commit, source commit, platform and lane;
- dataset role (`calibration`, `evaluation` or `confirmation`), corpus/confirmation fingerprint, family/pair id and normalized path;
- base and target `ManifestId`.

The document contains one record per distinct missing target chunk, in production first-occurrence order:

| field | type / meaning |
| --- | --- |
| `targetIndex` | first target record index |
| `targetChunkId` | exact ChunkId hex |
| `targetOffset`, `targetLength` | bytes |
| `candidateCount` | unique candidates considered by the selector |
| `expensiveTrialCount` | dictionary trials at the lane's expensive/final level actually executed |
| `level19TrialCount` | all L19 frames actually encoded for the entry, including the no-dictionary L19 frame; zero for H9 L9/L12/L15 |
| `selectedEncoding` | raw / zstd / zstd-dictionary |
| `selectedCandidate` | candidate ordinal or null |
| `storedBytes` | selected raw/frame bytes, excluding dictionary refs |
| `dictionaryRefs` | selected reference count |
| `candidates[]` | compact candidate rows below |

Each candidate row contains:

- `ordinal`: the `selectorCandidateOrdinal` used by the cheap-stage tie break; Phase-A offset rows preserve H0 relative order (gaps from skipped invalid starts are allowed), while Phase-B rows use the union order in §5.2;
- `startIndex`, `startOffset`, `recordCount` and `firstChunkId`; together with the base ManifestId these identify the dictionary window;
- `source`: exactly `offset`, `sketch` or `both`;
- `cheapFrameBytes` and `cheapCostBytes`, nullable where the lane has no cheap stage;
- `l19FrameBytes` and `l19CostBytes`, nullable when no L19 dictionary trial ran;
- `selected` boolean.

The trace stores **no target bytes, dictionary bytes or frame payload bytes**. G2 oracle rows are separate and name their best base start; `source` is not extended with an `oracle` value. PATCH-GAP-001 consumes this schema and the G2 artifact rather than inventing selector semantics. SHA-256 of each completed trace/oracle document is recorded by the external evidence manifest; it is not embedded self-referentially in the document. GAP may record its own policy fingerprint beside that digest without forking this schema.

## 8. Calibration, fixed evaluation and fresh confirmation

The existing PATCH-PREFREEZE corpus remains the development corpus, but its former “holdout” partition is **not independent for PATCH-ENC-005**. PATCH-ENC-002 already published its behavior and H9 exists partly because that behavior was observed.

Development corpus lock:

`pairsSha256 = 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.

Roles:

- **calibration:** `dotnet-aspnetcore-win-x64`, `dotnet-runtime-linux-arm64`;
- **fixed evaluation:** `node-win-x64`, `node-linux-x64`, `tzdata`, `chunkshift-source`.

The fixed-evaluation split may reject/prioritize candidates and may run the §4.3 false-negative guard. It is useful development evidence but **must never be described as independent holdout/generalization confirmation**.

### 8.1 Fresh confirmation set

Final adoption uses a separate set whose products/version pairs were not used by PATCH-ENC-001/002/003/004 or to choose PATCH-ENC-005 parameters. These exact assets are frozen before any PATCH-ENC-005 selector is run on them:

| family | pair | asset / normalization | published SHA-256 |
| --- | --- | --- | --- |
| `go-linux-arm64` | Go 1.26.7 → 1.26.8 | `go1.26.{7,8}.linux-arm64.tar.gz`; strip top-level `go/` | 1.26.7 `5a4ec883379d51ee9ce1040d5e87f8d35e20387574dd8c947feb01eabc3c1b37`; 1.26.8 `211ffced9dcb9633a55eac6364816ec0ddd951389a740e88fa8b3337971bdda0` |
| `go-win-x64` | Go 1.26.7 → 1.26.8 | `go1.26.{7,8}.windows-amd64.zip`; strip top-level `go/` | 1.26.7 `f4f534a486e4bc3387fa18f08208f2f854b7aaea8a08f2a2d829a914a05abb11`; 1.26.8 `b92c3b2adae85a11ba71fe7216daf0d84e82af4c8ab6c5625807f28622043a59` |
| `cpython-source` | CPython 3.14.7 → 3.14.8 | `Python-3.14.{7,8}.tgz`; strip versioned top-level directory | 3.14.7 `62859805f6fdf25e2bcbf3fa3217801e1996887ca33e6a2af80674bdfa2dbe07`; 3.14.8 `a65b20a728f169f4e66ae143f40b1bd3d33c38d770251663f627c9767b79b210` |

Sources are frozen as `https://go.dev/dl/<asset>` for the Go rows and `https://www.python.org/ftp/python/<version>/<asset>` for CPython. The confirmation materializer uses the existing corpus rules unless overridden above: only regular files are content (symbolic/hard links are skipped), normalized relative paths are sorted ordinally, same-path files present in both versions form changed/identical comparisons, added files are reported as whole-file deliveries and removed files cost zero. It must reject a checksum mismatch, case-colliding normalized paths or archive entries escaping the destination.

The implementation/lab PR adds a separate canonical confirmation manifest/materializer and commits its materialized pair-list SHA-256 **before any selector lane is invoked on the confirmation content**. That derived lock is provenance, not a tunable input: asset identities, versions, checksums and normalization above cannot change without a new ExperimentId.

No PATCH-ENC-005 patch bytes, candidate traces, G2 samples or timing from these assets may be inspected before calibration finalists have survived fixed evaluation and the evaluator has committed the finalist lane ids plus qualification branch(es).

### 8.2 No-peeking rule

Parameter/lane design is frozen by this protocol. Selection proceeds:

1. calibration chooses eligible/Pareto candidates;
2. fixed evaluation is a transparent development stability check and may prune them;
3. fresh confirmation is opened once for the surviving finalist(s).

A failed fresh confirmation is a negative result, not permission to tune on Go/Python and retry. Any change after viewing confirmation results is a new ExperimentId with a new untouched confirmation set.

## 9. Decision metrics and paired timing

### 9.1 Metrics

Per file, lane, repetition and platform where applicable:

- physical patch bytes and SHA-256;
- create wall seconds and process CPU seconds;
- managed allocated bytes;
- base reads, bytes read and seeks;
- candidate count, expensive dictionary-trial count and total L19 frame count;
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

If both names resolve to one lane, it is the sole calibration finalist. If they differ, both are calibration finalists. All calibration finalists next go through the fixed-evaluation stability check in §11; none goes directly to fresh confirmation. This is not a tie: it is an explicit unresolved speed/size tradeoff.

## 11. Fixed-evaluation stability and fresh confirmation

### 11.1 Fixed-evaluation stability

The calibration evaluator records each finalist and the branch(es) it satisfied before the fixed-evaluation run is evaluated.

A calibration finalist survives development evaluation only when:

- all correctness and cross-platform determinism rules hold on the fixed-evaluation files;
- apply and memory hard gates hold;
- the **same qualification branch** that made it a calibration finalist also holds on fixed evaluation; a finalist recorded as satisfying both branches must satisfy both;
- H7/H8 retain their parent byte-equality oracle.

Because PATCH-ENC-002 results from these families were already known, this step is explicitly a development stability check, not evidence of independent generalization.

The evidence-plan commit then records the surviving lane id(s), their qualification branch(es), protocol/source commits and confirmation manifest lock. Only after that commit may the fresh confirmation content be processed by a selector.

### 11.2 Fresh confirmation

A survivor is confirmed only when, on the §8.1 Go/Python set:

- all independent decode/apply/correctness and cross-platform determinism rules hold;
- apply wall is <=1.10× H0 on all three runners;
- the lane's create/apply memory bounds hold on all three runners;
- the **same branch** holds with the same wall/byte thresholds used on calibration and evaluation;
- H7/H8 retain their parent byte-equality oracle.

The paired five-round timing/noise rules of §9 apply unchanged to fresh confirmation.

If one of two survivors fails, the other may be adopted if confirmed. If both confirm, recompute dominance on fresh confirmation over `(b, max_p(w_p), max_p(c_p))`: if one survivor is no worse in all three and strictly better in at least one, only that survivor remains; if the two distinct survivors remain non-dominated, the experiment result is **DEFER**, not an invented scalar preference. D15 stays unchanged until a separately frozen product tradeoff rule exists.

A fresh-confirmation failure cannot be repaired by changing thresholds, selector features, index fanout, lane parameters or complexity preference under PATCH-ENC-005. Missing platform/run data yields **INCOMPLETE**.

## 12. Correctness and failure oracles

PATCH-ENC-005 is allowed to change patch bytes, so H0 SHA equality is not a general correctness oracle.

Before any decision run, the implementation/lab PR must pass:

- the independent Python CSP decoder for every generated scenario/vector patch;
- production apply and exact target SHA-256 for every decision patch;
- cross-platform patch SHA equality for each lane/file;
- short reads on base manifest/content, target manifest/content and patch input;
- cancellation before and during create/apply;
- corrupt/truncated patch and wrong/corrupt base semantics;
- Phase A must preserve H0's existing create failure behavior for equivalent reads. Indexed Phase B has one explicitly broader dependency: its required full-base prepass means a short/throwing base anywhere in that prepass can fail create even where H0 would never read that region. A short base surfaces `InvalidDataException`; a source exception remains unwrapped; caller cancellation surfaces `OperationCanceledException`; no partial patch is accepted as successful;
- an indexed base record whose bytes do not hash to its manifest `ChunkId` fails before its postings become visible. This verification work is included in index build CPU/wall metrics; selected dictionary chunks are still verified again from the bytes actually encoded, as production does;
- existing P6 failure-point and P9 fuzz regressions;
- existing committed CSP vectors unchanged and still decodable;
- new committed creation vectors for any newly representable/default selector outcome where a vector adds coverage, without deleting old compatibility vectors.

A candidate that is never selected cannot affect correctness. A selected dictionary is still verified against its exact base ChunkIds before the patch is written. Final target verification on apply remains authoritative.

## 13. Evidence retention and run identity

Decision RunIds are `PATCH-ENC-005/RUN-YYYYMMDD-NNN-<commit>-<platform>`; evidence follows `RESULT-TEMPLATE.md`.

Every decision record must commit enough data to recompute the verdict without a GitHub artifact:

- protocol/evaluator version and exact commit;
- development corpus lock, fresh-confirmation pair-list lock and any oracle-sample/feature-definition fingerprints;
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

1. trace/evaluator seams, feature-reference vectors and Phase-A lab-only policies H4/H7/H9;
2. correctness/failure tests, paired-run support and the separate fresh-confirmation materializer/manifest;
3. commit the confirmation pair-list lock **without running any selector on confirmation content**;
4. Phase-A calibration runs and fixed-evaluation development checks;
5. G2 sample materialization and exact calibration oracle;
6. if §4.3 main gate passes, implement H5-F/H6-O/H6-P; if it misses, implement only the mandatory H6-O false-negative guard and stop or expand exactly as §4.3 dictates;
7. H8 only after a resemblance finalist exists;
8. freeze surviving finalist lane id(s)/branch(es) in the evidence-plan commit;
9. open the fresh confirmation set once and run only those survivor(s) plus H0;
10. only after an ADOPT evidence record: a separate production-default PR updates D15/default and runs JIT + NativeAOT package smoke and vector/fuzz checks.

No final adoption decision run is part of this protocol PR. In particular, the protocol PR performs no Phase-A measurement, G2 oracle, H6-O guard or fresh-confirmation selector run.

## 15. Research rationale

The resemblance families are intentionally narrower than the literature survey:

- Finesse (FAST'19) keeps the super-feature model but replaces N-transform's repeated transforms with fixed-subchunk feature locality; its paper uses 12 features and three 4-feature super-features and reports materially faster feature computation with comparable compression.
- Odess (ICDE'21) uses Gear hashing plus content-defined sampling before linear transforms and selects the minimum transformed value per feature; the paper evaluates 1/128 sampling and a 12-feature / 3-super-feature setup. H6 keeps those semantics where they matter, while pinning the random transform pairs deterministically for reproducibility.
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
- G2 does not pretend an all-target × all-base L19 Cartesian product is a practical decision run; it is exhaustive only over a deterministic calibration target sample and the entire corresponding base. A miss cannot hard-stop resemblance without the pre-frozen H6-O false-negative guard.
- H8 never uses no-dictionary incompressibility alone.
- H5/H6 build/read/index cost is charged to create; no hidden persistent index is assumed.
- H9 is labeled a level+search policy bundle, so its result cannot be misattributed to level alone.
- The old Node/tzdata/source “holdout” is renamed fixed evaluation because its results already influenced this experiment; final confirmation uses untouched Go/Python version pairs.
- Feature/index hashes have normative serialization, endianness, domain separators and reference vectors; optimized Rabin code cannot silently define selector semantics.

## 17. Freeze checklist

The protocol is ready for freeze review when this document and PR metadata agree:

- [x] H0 is the current production H2-W2 baseline;
- [x] H4 level, candidate count, re-encode count, reference-cost ranking and ties are fixed;
- [x] H7 exit rule is numeric, content-ranked and has a no-byte-regression oracle;
- [x] H9 levels/K/C/radius and execution are fixed;
- [x] G2 target sample, full-base candidate universe, prioritization gate and mandatory H6-O false-negative guard are exact;
- [x] H5/H6 families, Rabin/Gear/transform byte semantics, reference vectors, retrieval order, index/fanout/memory bounds and determinism are fixed;
- [x] H8 can only follow resemblance and protects compressed-data edits with three signals plus parent-byte equality;
- [x] the shared #181/#183 trace schema is fixed and carries no payload bytes;
- [x] calibration and fixed-evaluation roles are honest about prior exposure; the fresh Go/Python confirmation assets, checksums and normalization are predeclared;
- [x] wall/CPU roles, platforms, paired five-round timing and noise invalidation are fixed;
- [x] create/apply memory bounds and explicit memory runs are fixed;
- [x] Pareto, complexity preference, fixed-evaluation stability and fresh-confirmation rules are deterministic without a weighted score;
- [x] independent decoder, target SHA-256, apply, failure, short-read, cancellation, P6/P9 and vector obligations are explicit;
- [x] durable compact evidence and raw-artifact SHA-256 manifest requirements are fixed;
- [x] CSP/Core/Repository/GAP scope boundaries are explicit;
- [x] no production selector/default/D15 change or decision run is part of this protocol PR.

The PR remains Draft until review agrees that these frozen choices are implementable. Merging this protocol, not merely checking the boxes above, is the gate for implementation decision work.
