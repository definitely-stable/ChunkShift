# PATCH-GAP-001 protocol: decompose the remaining CSP size gap

Status: **FREEZE-READY DRAFT — PATCH-ENC-005 is merged and pinned below; repeat freeze review/CI before merging this protocol, and run no PATCH-GAP decision lane before merge.**  
Issue: [#183](https://github.com/definitely-stable/ChunkShift/issues/183) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
ExperimentId: PATCH-GAP-001  
Protocol baseline commit: e967aeb6d4d467e94c5ac20f85e70ba0035d998d  
Related candidate-policy protocol: [PR #216](https://github.com/definitely-stable/ChunkShift/pull/216) / PATCH-ENC-005, merged to `main` at `96fd9b296d6998cac397e61041f22df51e6dd43c` (reviewed PR head `73d7af7e6f30e24922f34bb9da9be86820fcc7f8`).  
Tree/update-set research is owned by [#184](https://github.com/definitely-stable/ChunkShift/issues/184), not this experiment.

## 1. Question and decision boundary

PATCH-GAP-001 asks one question: **which independently testable cause explains the remaining size gap of the current per-file CSP encoder, and by how much?**

It is an informative decomposition study. It does not change CSP v1, CspEncoderPolicy.Default, the production applier, a public API, or D15. A positive result can justify a later RFC or a more focused experiment; it does not adopt a format or encoder change here.

The five named factors are:

- G1 — dictionary/history budget;
- G2 — dictionary candidate choice;
- G3 — frame granularity;
- G4 — executable normalization;
- G5 — already-compressed containers/streams.

A primary PATCH-GAP-001 lane changes **one** factor. Any combined lane is a later interaction study with a different ExperimentId and evidence identity. No interaction result may be substituted for an individual-factor result.

Two corrections to the original #183 sketch are part of this freeze:

1. A fixed reference cap can silently prevent the nominal G1 byte budget from being exercised. G1 therefore derives each research cap from the stable profile's 16 KiB minimum chunk: B1/R64, B4/R256, B8/R512 and B32/R2048. This guarantees that the byte budget, not an arbitrary reference ceiling, is the limiting envelope for any legal stable-profile chunk sequence.
2. Whole-file Zucchini and Puffin/PUFFDIFF are reference/attribution lanes, not automatically one-factor CSP counterfactuals. Their patch algorithms change more than one CSP property. They cannot directly satisfy the RFC gate unless a separate, CSP-costed reversible normalization lane is defined.

## 2. Frozen production baseline

### 2.1 Production policy on the protocol baseline

The source of truth is main at e967aeb6d4d467e94c5ac20f85e70ba0035d998d, not rounded PATCH-PREFREEZE numbers.

CspEncoderPolicy.Default is:

| property | value |
| --- | ---: |
| zstd level | 19 |
| dictionary chunks K | 4 contiguous base chunks |
| maximum candidate starts C | 8 |
| search radius | 262,144 bytes (256 KiB) |
| dictionary load | raw prefix |
| dictionary hash-log cap | 20 |
| dictionary chain-log cap | 20 |

Candidate starts are the nearest base-record offsets inside ±262,144 bytes of the target offset, lower base index first on an equal-distance tie, capped at eight. A production dictionary is the K contiguous records beginning at that start, truncated only by end-of-base; the candidate is skipped if their total exceeds 1,048,576 bytes.

The stored-form choice is the strict minimum of:

1. raw target chunk bytes;
2. one zstd frame without a dictionary;
3. one zstd frame for each legal dictionary candidate plus 32 × DictionaryCount bytes of dictionary-reference cost.

Strict less-than comparisons mean ties stay with the earlier form: raw first, then no-dictionary zstd, then the first equal-cost dictionary candidate.

### 2.2 Production create topology

PATCH-ENC-004 is already adopted on this baseline. CspCreateExecution.Default is H2-W2 when at least two processors are available and sequential on a one-processor machine:

- two encode workers;
- shared base stream, serialized seek/read critical section;
- no base-candidate cache;
- four in-flight entries and 4 MiB window bytes per worker.

Changing worker topology is not a GAP factor. **Only H0 is required to reproduce the production H2-W2 patch bytes/digest.** A GAP factor lane is expected to change its synthetic/physical bytes when the factor has an effect; it must instead be deterministic under its own frozen semantics, reconstruct the exact target, and use the same H2-W2 create execution topology for production-real CSP comparisons unless the lane is explicitly an external reference tool.

### 2.3 CSP framing and normative costs

CSP v1 encoding 1 is one independent RFC 8878 zstd frame per distinct missing target ChunkId. Its decoded content is exactly one target chunk. Normative limits are:

- DictionaryCount 0..4;
- total dictionary bytes ≤ 1,048,576;
- zstd window ≤ 1,048,576;
- output exactly target chunk length;
- PAYL entry header = 40 bytes;
- dictionary reference = 32 bytes each;
- PIDX entry = 24 bytes.

The stable chunking profile is fastcdc.gear.chunkshift.v1.64k: minimum 16 KiB, target 64 KiB, maximum 256 KiB.

### 2.4 Exact baseline evidence anchor

The current policy's frozen-corpus byte anchor is not 38.56 MiB. PATCH-ENC-003 measured:

- calibration: **11,860,274 bytes**;
- holdout: **26,363,364 bytes**;
- total: **38,223,638 bytes**.

PATCH-ENC-004 subsequently proved every execution topology, including H2-W2, byte-identical for all 1,893 files. Its whole-corpus patch-set SHA-256 is:

4a1f5c272da5cb12ba0d07c5d6b379701d7da154bdc1e92886bd27a8fc3781c6

Every GAP decision dataset must regenerate an H0 control from the production builder and record exact per-file bytes. For this protocol baseline, a different total or patch-set digest is an invalid baseline unless the evidence record names and justifies a later production-code baseline under a new protocol revision. Historical rounded numbers are descriptive only.

## 3. Corpus, fixed splits and no-retuning rule

Use the existing frozen PATCH corpus and no other inputs for the primary decision:

- pairsSha256: 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd;
- corpus manifest SHA-256: 0e0ba2f1d0a0f7a27690f88a55cd045008a1ea71277b04680775e4c6ee3c028f;
- source-assets SHA-256: 3ae2e55108051700a07425896e01b1703683cd896ed4f54e4a6c652fe55bda3e;
- 12 version pairs, 1,893 changed files, 857,713,581 changed target bytes.

Calibration is the frozen .NET ASP.NET Core Windows x64 plus .NET Runtime Linux ARM64 families. The corpus field historically named `holdout` is the frozen Node Windows x64, Node Linux x64, tzdata and ChunkShift-source families. Family membership cannot move after results are known.

**The historical `holdout` label does not mean an untouched statistical holdout.** PATCH-PREFREEZE and PATCH-ENC-002/003/004 have already published behavior on this partition, and those results informed the present research program. PATCH-GAP therefore treats it as a **fixed evaluation split** for the #183 corpus-specific RFC rule, not as independent generalization evidence. Below, `holdout`/H refers to that frozen evaluation split solely to stay compatible with the corpus schema and #183 wording.

All frozen size-lane definitions and parameters are fixed by this protocol before GAP evaluation begins. Calibration may prioritize expensive runtime characterization, but it cannot prune any predeclared gate-eligible byte lane from the fixed evaluation split. Each committed evaluation lane runs exactly once and is never retuned from evaluation results. A claim of out-of-corpus generalization would require a separately predeclared fresh confirmation corpus under a new protocol/evidence identity.

G4 and G5 use deterministic subset classifiers frozen below. The first post-merge GAP action is an **inventory-only** pass: it materializes canonically sorted G4/G5 subset manifests, records calibration/holdout SHA-256 values and commits those manifests before any G4/G5 size codec is invoked. Subsequent factor/reference runs consume those exact manifests; they do not re-decide membership. A classifier change after the inventory commit requires a protocol revision/new evidence identity. Subset membership may depend on file structure or parser support, but never on resulting patch size.

## 4. Common cost model and metrics

Let B_CSP(S) be the sum of exact production CSP bytes over split S and B_F(S) the whole-split bytes of factor lane F. For a subset factor, files outside its predeclared eligible subset contribute their production CSP bytes unchanged. Therefore subset results are never extrapolated to the rest of the corpus.

Every lane records, where meaningful:

- per-file bytes and aggregate bytes;
- reduction_vs_csp;
- gap_recovered(reference) for each explicitly named reference;
- create wall time and process CPU time;
- decode/apply wall time and process CPU time;
- peak RSS over the same idle/process convention used by Patching evidence;
- base bytes read and base_read_amplification = factor_base_bytes_read / CSP_base_bytes_read;
- eligible/affected file count, target bytes and unique-missing bytes;
- exact reconstruction result and target SHA-256;
- the exact property/cost relaxed by the lane.

The whole-split byte **sum** is authoritative for the §11 gate, so large files intentionally carry their byte weight. Per-file rows and concentration statistics are mandatory to make dominance visible; family means or an unweighted mean of per-file ratios may not replace the byte-sum decision metric.

## 5. G1 — dictionary/history budget

### 5.1 Question

How much size is lost because the current entry can name only four base chunks / 1 MiB of dictionary history, before changing candidate-start selection?

G1 keeps the production candidate-start set: at most eight starts inside ±256 KiB, in the exact production order. It does not use the G2 oracle, H5 or H6.

### 5.2 Frozen G1 lanes

| lane | max refs | byte budget | zstd window cap | purpose |
| --- | ---: | ---: | ---: | --- |
| G1-H0 | 4 | 1,048,576 | 1,048,576 | exact production control |
| G1-B1-R64 | 64 | 1,048,576 | 1,048,576 | isolate the 4-reference ceiling at the current byte/window budget |
| G1-B4-R256 | 256 | 4,194,304 | 8,388,608 (windowLog=23) | 4 MiB history envelope |
| G1-B8-R512 | 512 | 8,388,608 | 16,777,216 (windowLog=24) | 8 MiB history envelope |
| G1-B32-R2048 | 2,048 | 33,554,432 | 67,108,864 (windowLog=26) | 32 MiB history envelope |

G1 is a **nested relaxation**, not a replacement dictionary policy. For each distinct missing target entry:

1. Start with the exact H0 winning stored form and exact H0 entry cost. It remains a legal choice in every G1 lane without re-encoding.
2. Reuse exactly the H0 candidate-start set/order; G1 never adds a start.
3. For one start and envelope E, build the **maximal contiguous prefix** beginning at that start: append base chunks in order until end-of-base, E's reference cap, or adding the next chunk would exceed E's byte budget. Never skip a chunk.
4. Deduplicate trials whose ordered ChunkId sequence is identical to an already-tried dictionary for that start.
5. A lane includes every frozen envelope up to itself:
   - B1 tries B1;
   - B4 tries B1 + B4;
   - B8 tries B1 + B4 + B8;
   - B32 tries B1 + B4 + B8 + B32.
6. Each envelope candidate is encoded with **that envelope's own frozen window cap**, so the exact B4 trial embedded in B32 is byte-identical to the B4-lane trial. The final winner is the strict minimum against the preserved H0 winner; equal cost keeps the earlier/smaller-envelope choice.

The reference cap is derived from the stable profile's **16 KiB minimum chunk**, not its 256 KiB maximum: `maxRefs = byteBudget / 16 KiB`. Therefore a reference cap cannot fire before the byte budget except where end-of-base itself is the limiter. This is important for B8/B32: a common fixed cap such as 128 references would typically expose only about 8 MiB at a 64 KiB average chunk and could turn a nominal 32 MiB experiment into a hidden reference-cap experiment. Reference metadata remains fully charged, so the larger cap is not free.

These nesting rules create mandatory byte oracles, aggregate and per entry:

`cost(H0) >= cost(B1) >= cost(B4) >= cost(B8) >= cost(B32)`.

Any violation is an implementation/evidence error, not measurement noise.

The G1 driver is **lab-only** and must not widen `CspDictionary.MaximumBytes`, `CspEncoderPolicy.Default` or any public API. Production `CspPayloadEncoder` rejects dictionaries above 1 MiB through `CspDictionary.IsUsable`, so B4/B8/B32 use a dedicated research codec path over the same pinned low-level zstd backend. The only dictionary-eligibility changes are the frozen G1 reference/byte/window limits. All other H0 semantics stay fixed:

- level 19;
- raw-prefix dictionary handling;
- dictionary hash/chain logs capped at H20/C20;
- the candidate must not begin with the zstd dictionary magic (the current raw-content semantic guard is preserved);
- raw and no-dictionary zstd remain competing stored forms under the same strict-decrease/tie order;
- the decoder uses the corresponding lab-only raw-prefix path with the lane's window bound and must reproduce the exact target length, ChunkId and file SHA-256.

The evidence records zstd version/build/binary SHA-256 and the exact research-policy fingerprint. A result obtained by patching the production format limits or by falling back to trained-dictionary auto-detection is invalid.

For B4/B8/B32, the window is the smallest frozen power-of-two envelope chosen to keep the larger raw-prefix history usable with a maximum-size 256 KiB target chunk. The larger window is therefore an explicit part of the G1 **dictionary/history envelope** relaxation, not a hidden second factor. G1-B1-R64 deliberately keeps the v1 1 MiB window. Because smaller-envelope trials retain their own window caps, a larger lane never rewrites earlier evidence merely by raising its maximum supported window.

### 5.3 Costing and representability

For each distinct missing entry define `entryCost = storedBytes + 32 × dictionaryRefs`. Since G1 conservatively keeps every existing fixed PAYL/PIDX/non-payload byte and the synthetic count field does not enlarge either fixed header, whole-split physical accounting is exact:

`B_G1(E) = B_CSP - sum(H0EntryCost) + sum(G1WinnerEntryCost(E))`

over the distinct missing payload entries. The compact dataset records H0 cost, every attempted envelope/start cost, selected envelope/start and final winner so this equation and the nesting oracle can be recomputed independently.

Each dictionary reference still costs exactly 32 bytes in the research score. No oracle gets free references. Maximum reference metadata is therefore 2,048 bytes for B1/R64, 8,192 for B4/R256, 16,384 for B8/R512 and 65,536 for B32/R2048.

The protocol also freezes an analytical decoder-data envelope, separate from observed process RSS:

decoder_data_envelope = dictionary_budget + zstd_window_cap + 262,144 target-output bytes + maximum reference bytes

| lane | decoder-data envelope |
| --- | ---: |
| G1-H0 | 2,359,424 bytes (about 2.2501 MiB) |
| G1-B1-R64 | 2,361,344 bytes (about 2.2520 MiB) |
| G1-B4-R256 | 12,853,248 bytes (about 12.2578 MiB) |
| G1-B8-R512 | 25,444,352 bytes (about 24.2656 MiB) |
| G1-B32-R2048 | 100,990,976 bytes (about 96.3125 MiB) |

For RFC eligibility, G1 also has a **hard measured apply-RSS ceiling** derived from the current D17 64 MiB-over-idle apply budget. A research lane receives only the incremental codec-data envelope above H0:

apply_rss_limit(F) = 64 MiB + decoder_data_envelope(F) - decoder_data_envelope(H0)

| lane | apply RSS limit over idle |
| --- | ---: |
| G1-H0 | 67,108,864 bytes (64.0000 MiB) |
| G1-B1-R64 | 67,110,784 bytes (about 64.0018 MiB) |
| G1-B4-R256 | 77,602,688 bytes (about 74.0077 MiB) |
| G1-B8-R512 | 90,193,792 bytes (about 86.0155 MiB) |
| G1-B32-R2048 | 165,740,416 bytes (about 158.0624 MiB) |

A G1 lane that crosses the §11 size threshold but exceeds its lane-specific apply-RSS ceiling on **any** required runtime platform is not RFC-qualified. The measurement uses the same >=1 MiB per-file child-process / idle-baseline convention as D17. This rule preserves the existing 64 MiB implementation allowance and grants only memory directly implied by the larger declared dictionary/window/reference envelope; it does not hide arbitrary lab implementation overhead.

This is a codec-data upper envelope, not a prediction of RSS; allocator, native zstd and implementation overhead are measured separately. The G1 driver must read at most the **largest requested dictionary once per H0 start** and reuse prefix slices for the smaller nested-envelope trials; it may not reread the same start independently for B1/B4/B8 inside a B32 entry. Thus the per-entry base-read bound remains at most `8 × laneMaximumDictionaryBudget`; B32 performs at most 32 non-deduplicated extended dictionary encodes (8 starts × 4 envelopes), not thousands of prefix-length trials. Apply reads only the selected named dictionary and is bounded by the selected envelope's dictionary budget. Actual bytes/read calls/seeks and amplification are still recorded.

For every attempted envelope/start, record envelope id, ordered dictionary ChunkIds, dictionary bytes/reference count, compressed-frame bytes, reference-cost bytes, zstd window, base bytes/read calls and whether the trial was deduplicated. Per entry also record H0 winner cost, G1 winner envelope/start/stored form, trial count and create/apply peak RSS.

G1 is a **bounded envelope counterfactual**, not a mathematical best-dictionary oracle. For each H0 start it tests the frozen maximal prefix of each envelope, plus the exact H0 winner; it does not search every intermediate prefix length. Therefore its result answers the value of these concrete larger-history envelopes. Searching arbitrary prefix endpoints would mix dictionary-budget relaxation with a new candidate/length-selection policy and belongs in a separately identified G1×G2 interaction study. Any result beyond H0 cannot be emitted as CSP v1 because it may exceed the v1 4-reference, 1 MiB dictionary and/or 1 MiB window maxima.

CSP v1 also persists `DictionaryCount` as **UInt8** in both the 40-byte PAYL entry header and the 24-byte PIDX entry, so B4/R256 and larger are not representable by merely changing a semantic maximum. To make research bytes fully defined without granting a free compact encoding, G1 freezes this **synthetic revised-header accounting model only**:

- PAYL keeps its 40-byte fixed header; bytes 37..38 are treated as little-endian UInt16 DictionaryCount and byte 39 remains reserved zero;
- PIDX keeps its 24-byte fixed entry; bytes 21..22 are treated as little-endian UInt16 DictionaryCount and byte 23 remains reserved zero;
- dictionary ChunkIds still begin at PAYL offset 40 and still cost exactly 32 bytes each;
- every frozen G1 cap (maximum 2,048) fits UInt16.

This is not CSP v1 and is not a proposed wire revision; it is the minimal direct-list counterfactual needed to price the factor consistently. A real RFC would need to choose/version the layout and raise the dictionary/window limits explicitly. A future compact run descriptor, range encoding or other cheaper reference representation is a separate factor and receives no free credit here.

## 6. G2 — candidate choice, owned by PATCH-ENC-005

### 6.1 Pinned producer contract

PATCH-ENC-005 is no longer mutable. PR #216 merged to `main` at commit `96fd9b296d6998cac397e61041f22df51e6dd43c`; its reviewed PR head was `73d7af7e6f30e24922f34bb9da9be86820fcc7f8`. PATCH-GAP consumes that merged protocol as the sole authority for selector/oracle semantics.

Pinned shared evidence contracts:

- candidate trace schema: exactly `chunkshift.patch-candidate-trace.v1`;
- G2 oracle schema: exactly `chunkshift.patch-g2-oracle.v1`;
- trace `datasetRole`: exactly `calibration`, `evaluation` or `confirmation`;
- oracle policy: exactly `L19-K4-ALL-PREFIX-H20C20-REF32`;
- oracle candidate order: exactly `abs-offset-then-lower-index`;
- production-real resemblance lane ids: `H5-F12-SF3`, `H6-O12-SF3-S128`, `H6-P12-T3-S128`;
- H8 is not an independent G2 size lane because the merged producer requires H8 patch SHA-256 to equal its chosen H5/H6 parent on every dataset where H8 is evaluated.

Every consumed document must name `protocolCommit = 96fd9b296d6998cac397e61041f22df51e6dd43c` and its actual implementation/evidence `sourceCommit`. A document from an earlier Draft protocol is invalid GAP evidence even if its JSON shape happens to match.

### 6.2 Boundary

PATCH-GAP-001 does **not** implement Finesse, Odess, Gear-derived retrieval, a new similarity index, or another selector. #181 / merged #216 owns selector semantics, the shared trace, the exact whole-base oracle implementation and production-real H5/H6 work.

G2 answers: **how much size headroom is attributable to choosing non-offset dictionaries under today's K=4 / <=1 MiB / one-frame-per-chunk CSP envelope, and how much of that headroom is recovered by the bounded production-real selectors?**

Phase-A H4/H7 results may be carried as selector-policy context, but they do not constitute the primary G2 resemblance result because they add no non-offset candidate. H8 contributes its parent's bytes only.

### 6.3 Sampled exact oracle populations

The **calibration oracle** is exactly the merged #216 §4 sample. For each of the four calibration pairs, enumerate distinct missing target entries, compute the #216 six-field zero-separated SHA-256 sample key, sort under the merged tie rules, and take the first 64 or all if fewer. Maximum population: 256 entries. GAP consumes the resulting `chunkshift.patch-g2-oracle.v1` artifact and its `oracleSampleSha256`; it does not implement another exhaustive search.

For #183's calibration/evaluation output, GAP additionally freezes one **descriptive evaluation oracle sample** using the same merged sample-key serialization and ordering independently on each of the eight fixed-evaluation pairs. Take the first 64 entries per pair or all if fewer, maximum 512 entries. Materialize and hash that membership list **before** any evaluation-oracle encoding. Produce it through the merged PATCH-ENC-005 oracle implementation/contract with `datasetRole = evaluation`, the same policy/candidate order above, and record its own `oracleSampleSha256`.

The sampled oracle is exact only for its named sampled population. Neither calibration nor evaluation oracle bytes are projected to unsampled entries, and neither can directly satisfy the §11 whole-split 15% gate.

For oracle rows, GAP consumes the producer's explicit `h0CostBytes`, `oracleCostBytes`, location fields and `savedBytes`; it does not reconstruct costs from candidate traces.

### 6.4 Production-real whole-population G2 evidence

Merged #216 makes G2 a prioritization gate with one pre-frozen H6-O false-negative guard, then may run the full Phase-B resemblance grid. PATCH-GAP does not alter that progression.

For #183:

- any of `H5-F12-SF3`, `H6-O12-SF3-S128` or `H6-P12-T3-S128` that has full fixed-evaluation per-file patch bytes under the merged producer contract is a production-real G2 result;
- a lane that #216 never executes on the full fixed-evaluation population is reported `NOT_EVALUATED`, not inferred from calibration or the sampled oracle;
- the one-platform H6-O false-negative guard may supply deterministic fixed-evaluation **byte** evidence. If that frozen lane alone crosses the §11 15% threshold, GAP may request the additional unchanged-lane cross-platform correctness/runtime/memory characterization required by §11. That is measurement of the existing #216 lane, not a new selector or retuning step;
- no GAP run may change features, fanout, ranking, L1/L19 trial policy, thresholds or candidate union. Missing selector semantics require a new PATCH-ENC experiment, not a private GAP compatibility shim.

For a production-real trace row, selected entry cost is `storedBytes + 32 * dictionaryRefs`. This is diagnostic/attribution data only. **Whole-file and whole-split G2 physical bytes come from the actual per-file patch-size records/artifacts produced by the lane and H0**, because trace entry sums do not include every fixed CSP byte. H0 and factor rows must match the same dataset lock, family/pair/path, source/protocol contract and target identity before comparison.

The primary whole-split reporting metric is therefore the §11 `reduction_vs_csp` over actual patch bytes. Also report, per lane, incremental resemblance value against its H4-based policy context when that parent/control exists, so an H5/H6 result is not misdescribed as pure retrieval gain if H4's cheap-rank/R=2 policy also changed bytes relative to H0.

### 6.5 Fresh confirmation and evidence lineage

PATCH-ENC-005's separately predeclared Go/Python fresh-confirmation set is useful external selector evidence, but it does **not** replace #183's frozen fixed-evaluation denominator or §11 gate. If available, GAP records the producer's confirmation dataset lock and verdict beside the fixed-evaluation result.

For every consumed trace/oracle/result document GAP records:

- SHA-256 of the complete document;
- merged `protocolCommit` and actual `sourceCommit`;
- schema id, datasetRole/datasetSha256 and lane/policy identity;
- `oracleSampleSha256` where applicable;
- per-file patch SHA-256/size artifact lineage for production-real lanes.

If valid #216 evidence already exists, GAP consumes it by digest. If a required descriptive evaluation oracle or unchanged-lane characterization is still missing, it is executed using the merged producer implementation/contract and then consumed here; selector logic is never duplicated inside #217.

## 7. G3 — frame granularity

### 7.1 Why zstd --patch-from is not the G3 lane

Whole-file zstd --patch-from changes dictionary scope, candidate choice and frame granularity simultaneously. It remains an external reference, but cannot be counted as the isolated G3 result.

G3 instead uses a conservative counterfactual cost model that changes grouping of CSP payload bytes while holding candidate generation and dictionary budget fixed.

### 7.2 Frozen G3 lanes

- G3-H0 — exact production CSP: one independent stored form per distinct missing target chunk.
- G3-RUN — one zstd frame for each maximal run of payload-bearing first-occurrence missing target records that are consecutive in manifest index and physically adjacent in target content. A base-reused record, duplicate already-supplied ChunkId, or discontinuity ends the run.
- G3-FILE — one zstd frame for the ordered sequence of all payload-bearing first-occurrence missing chunks of a changed file. This is the strongest one-factor within-file framing extreme; it is **not** whole-target --patch-from.
- G3-REF-PATCHFROM — the pinned whole-file zstd --patch-from lane from §10. It is the requested whole-file/reference extreme, but is descriptive only because it simultaneously changes dictionary scope, candidate choice and framing. It is never substituted for B_G3 and cannot pass the G3 RFC gate.

For RUN/FILE, input bytes are the concatenation of the group's original target chunks in first-target-record order. **Only groups with at least two payload members are coalesced.** A singleton run/file-payload group has no cross-chunk framing opportunity and remains the exact H0 entry byte-for-byte; forcing it through GroupZstd would change codec choice rather than frame granularity.

A coalesced group uses only the dictionary that H0 selected for the group's first payload entry; if H0 selected raw or no-dictionary zstd there is no base dictionary. No group-level candidate search, larger dictionary or whole-base oracle is allowed. The zstd window remains capped at 1 MiB.

This anchor rule is intentionally conservative: later chunks lose their independent H0 dictionary changes. A single zstd frame cannot swap raw-prefix dictionaries between chunk boundaries, so G3 measures the **net coalescing envelope**: cross-chunk frame context plus the required loss of per-entry dictionary reselection. It must not be described as a pure context-carry gain, and the loss must not be repaired by silently importing G1/G2.

For **G3-FILE**, “file” means one frame over that file's ordered **payload-entry stream**, not over every target byte. Base-reused records and later duplicate occurrences are not inserted into the zstd input and receive no second copy in the patch. If such a target record lies between two FILE group members, apply may suspend the group's streaming zstd decoder after finishing the earlier member, emit the intervening record through the ordinary base/replay path, then resume decoding the next group member. It must not materialize the full group merely to bridge those target positions. Thus FILE tests frame/context scope over stored payload bytes without silently turning reused target bytes into new payload.

### 7.3 Exact revised-layout accounting

G3 freezes a minimal **research-only parsable layout counterfactual** so group signaling receives no free bytes. It consumes two currently reserved one-byte encoding values while keeping every existing PAYL/PIDX fixed-size record:

- **Encoding = 4 — GroupZstdStart.** This PAYL entry carries the group's single zstd frame and the anchor dictionary references. Its `ChunkId` / `FirstTargetIndex` identify the first group member. `StoredLength` is the group-frame byte length.
- **Encoding = 5 — GroupContinuation.** This entry identifies one following group member in target-first-occurrence order, has `StoredLength = 0`, `DictionaryCount = 0`, and stores no payload bytes.
- A group ends immediately before the next non-continuation payload entry or at the end of the payload-entry sequence. RUN/FILE construction determines which entries receive continuation markers; the decoder does not need an external lane id.
- The corresponding PIDX entries carry the same Encoding/StoredLength/DictionaryCount values as PAYL. Continuation `PayloadOffset` still points to its ordinary 40-byte PAYL entry header, so no new pointer or group table is added.
- A group frame is invalid if `groupFrameBytes > UInt32.MaxValue`, because the existing `StoredLength` field is retained rather than silently widened.

This deliberately relaxes v1 rules that Encoding 2..255 are unsupported, every entry has `StoredLength > 0`, and one frame produces exactly one chunk; those are the G3 format revision. It does **not** remove or shrink any header/index record.

Therefore the physical byte equation is exact for this frozen counterfactual:

`B_G3 = B_CSP - sum(entryStoredBytes + 32 × entryDictRefs) + sum(groupFrameBytes + 32 × anchorDictRefs)`

where the subtraction covers only H0 entries in coalesced groups of two or more members and the addition carries one frame plus one anchor-reference list per such group. Singleton entries are unchanged and cancel completely. Fixed PAYL/PIDX/non-payload bytes cancel because their sizes are unchanged. The compact dataset records group id/type, member FirstTargetIndex/ChunkId/length, H0 stored/ref costs, anchor refs and group frame bytes so every term and continuation sequence can be independently reconstructed.

The reconstruction oracle reads GroupZstdStart followed by its GroupContinuation records, decodes the frame incrementally, splits output by the members' known target chunk lengths, verifies every target ChunkId, rejects short/extra decoded output, and reconstructs the complete target file for SHA-256 equality.

### 7.4 Lost properties

| property | H0 | G3-RUN | G3-FILE | G3-REF-PATCHFROM |
| --- | --- | --- | --- | --- |
| independent target-chunk verification | yes | no, group scope | no, file-payload scope | no, whole-file scope |
| dictionary locality/change per chunk | yes | only group anchor | only file anchor | previous file is one large reference dictionary |
| random payload access | chunk | run | file payload | file |
| failure isolation | chunk | run | file payload | file |
| parallel decode/apply | many entries | fewer runs | at most one frame/file | per file |
| bounded decoder history | 1 MiB | 1 MiB | 1 MiB | reference-tool bound, not CSP v1 |
| CSP declarative codec model | current v1 | research Encoding 4/5 demonstrates a declarative revision shape | same | no; multi-factor external reference |

A group decoder must be streamable and hash chunks as they emerge; it may not allocate the complete group merely because the research frame is larger than one chunk. Repeated target ChunkIds follow the existing applier model: after the first verified occurrence has been written, later occurrences replay those bytes from the already-written output rather than retaining the group in memory or re-decoding it.

G3 keeps the v1 1 MiB zstd window and <=1 MiB anchor dictionary, so its hard RFC-eligibility apply bound remains **64 MiB over idle** under the existing D17 per-file child-process convention. RUN/FILE must satisfy that bound on every required runtime platform. A research implementation that materializes a complete group/file payload or otherwise exceeds 64 MiB is not evidence for a bounded G3 revision.

## 8. G4 — executable normalization

### 8.1 Predeclared subset

G4 membership is based on bytes, not filename extensions. At the frozen-family level, only these corpus families can contribute executable rows:

- calibration: dotnet-aspnetcore-win-x64 and dotnet-runtime-linux-arm64;
- holdout: node-win-x64 and node-linux-x64.

tzdata and chunkshift-source are structurally outside the executable family set and contribute unchanged H0 bytes to whole-split G4 metrics.

For an individual changed-file pair, both base and target must parse as the same supported executable family/architecture before any patch result is produced:

- PE/COFF x86 (Machine=0x014c) or x86-64 (0x8664);
- ELF x86 (EM_386=3), x86-64 (EM_X86_64=62) or AArch64 (EM_AARCH64=183).

PE is further classified so managed IL is not mislabeled as x86/x64 machine-code normalization:

- **PE-native** — no CLR/COM descriptor: gate-eligible for x86 BCJ;
- **PE-R2R/mixed** — CLR header present and either ManagedNativeHeader is non-empty or ILONLY is not set: gate-eligible, but reported separately from PE-native;
- **PE-managed-IL-only** — CLR header present, ILONLY set and ManagedNativeHeader empty: inventory/reference-only; it is not included in G4-BCJ because the PE Machine field does not imply that its method bodies are x86/x64 branch code.

This matters for dotnet-aspnetcore-win-x64, whose frozen corpus category explicitly contains ReadyToRun managed and native PE. The inventory manifest records PE subtype, architecture, parser result, CLR flags/ManagedNativeHeader presence, target bytes and unique-missing bytes.

Mach-O is excluded because the frozen corpus has no macOS family. Adding Mach-O requires a new corpus/protocol revision; it cannot be added after seeing G4 results.

The committed inventory is the denominator. Unsupported/malformed inputs are explicit rows, never silent skips. Zucchini may report results for a broader parser-supported PE/ELF subset, including managed-only PE if its own parser accepts it, but those rows remain reference-only and are reported by subtype; they do not enlarge the gate-eligible BCJ subset.

### 8.2 G4-BCJ counterfactual lane

The gate-eligible normalization lane uses the public raw BCJ APIs from XZ Utils 5.8.4 / liblzma, pinned to commit d3e650e63c110e830fd5391e7f8b45df0b91d3da:

- lzma_bcj_x86_encode/decode for PE/ELF x86 and x86-64;
- lzma_bcj_arm64_encode/decode for ELF AArch64.

The position contract is frozen because BCJ conversion depends on stream position and the raw functions return the number of bytes actually processed.

- **x86/x64:** call the x86 raw filter with `start_offset = UInt32(originalFileOffset mod 2^32)`. All start offsets are valid. If the API reports `processed < inputLength`, leave the unprocessed suffix unchanged; the x86 filter may leave up to four trailing bytes.
- **ARM64:** the filter start offset must be 4-byte aligned. Leave the 0..3-byte prefix before the first aligned original-file offset unchanged, call the raw ARM64 filter on the remaining span with `start_offset = UInt32(alignedOriginalFileOffset mod 2^32)`, and leave `inputLength - processed` trailing bytes unchanged (at most three).
- **Target position:** one payload entry exists per distinct missing ChunkId, so normalize it at the offset of that ChunkId's **first target-record occurrence** (the same first occurrence that owns the payload entry). Later duplicate occurrences replay the already inverse-normalized raw bytes exactly as the production applier does; they do not re-run BCJ at their later offsets.
- **Dictionary position:** CSP names dictionary bytes by an ordered ChunkId sequence, not by occurrence offsets. For the selected ordered dictionary sequence `ids[0..n)`, define `canonicalBaseSequenceStart(ids)` as the **lowest base-manifest record index whose next n records have exactly that ordered ChunkId sequence**. Such an occurrence always exists because every admitted G4 dictionary comes from an H0 contiguous candidate. Normalize the concatenated dictionary bytes as one contiguous span beginning at that real occurrence's file offset. Create and apply independently derive the same lowest matching sequence from the base manifest; no base-offset metadata is granted for free.
- Choosing the earliest occurrence of the **whole sequence**, rather than merely the earliest occurrence of `ids[0]`, prevents later dictionary chunks from being assigned virtual file positions that never occurred together in the base. If the H0 candidate itself is not the canonical occurrence because an identical sequence exists earlier, both create and apply use the earlier full-sequence occurrence only for BCJ position; the dictionary bytes/ChunkIds remain unchanged.
- The inverse uses the exact same architecture, untouched prefix, canonical-sequence/first-target-occurrence start offset and processed-length boundary. Evidence records H0 candidate start, canonical sequence start, whether they differ because of duplicate content, processed length and untouched prefix/tail for every normalized target/dictionary span.

Ignoring the returned processed length, using only the create-side candidate occurrence, canonicalizing only the first dictionary ChunkId instead of the full sequence, re-normalizing duplicate target occurrences at later positions, or deriving dictionary position from the target offset invalidates the lane.

G4 is an **additive encoding relaxation**, not a forced replacement of ordinary CSP encoding. For every eligible distinct missing chunk:

1. Preserve the exact H0 winning stored form and exact H0 entry cost as the initial winner. It is never re-encoded and remains legal.
2. Normalize the target bytes once under the frozen first-target-occurrence position rule.
3. Add one BCJ+zstd L19 no-dictionary trial and one BCJ+zstd L19 trial for each of that entry's **same H0 candidate dictionary sequences**. Candidate starts/order, K=4, C=8, ±256 KiB radius, <=1 MiB original dictionary and 1 MiB zstd window do not change.
4. Candidate eligibility is decided on the original H0 dictionary bytes using the production K/size/`CspDictionary.IsUsable` rules **before** normalization. An admitted dictionary is then normalized and supplied explicitly as a raw prefix; do not reinterpret transformed bytes as a trained dictionary.
5. BCJ trials cost `frameBytes + 32 × dictionaryRefs`. Replace H0 only on a strict cost decrease; a tie keeps H0, then the earlier H0 candidate order.

For exact synthetic physical accounting, the BCJ-coded winner consumes two of CSP v1's currently reserved one-byte encoding values in both PAYL/PIDX:

- **Encoding = 2:** x86/x64 BCJ + zstd frame, optional normalized raw-prefix dictionary;
- **Encoding = 3:** ARM64 BCJ + zstd frame, optional normalized raw-prefix dictionary.

The architecture is therefore self-described by the entry without adding bytes or depending on an external G4 inventory at apply time. PE-vs-ELF does not need to be persisted because both x86 families use the same BCJ transform and ARM64 is a separate encoding. All normalization positions are derived deterministically from the embedded target/base manifests as frozen above; no free offset field is granted. Encoding 0/1 entries remain exact H0 bytes/semantics.

For eligible distinct missing entries define `H0EntryCost = storedBytes + 32 × dictionaryRefs` and `G4EntryCost = min(H0EntryCost, all BCJ trial costs)`. Then:

`B_G4 = B_CSP - sum(H0EntryCost_eligible) + sum(G4EntryCost_eligible)`

with every ineligible entry/file unchanged. The mandatory oracle is therefore `B_G4 <= B_CSP` both per eligible entry and in aggregate. The compact evidence records H0 winner, every BCJ trial, selected encoding/start/refs, normalization positions and every term of the equation.

Apply dispatches Encoding 0/1 exactly as production. For synthetic Encoding 2/3 it selects x86-or-ARM64 BCJ from the encoding value itself, verifies/reads the named base chunks, derives the lowest real contiguous occurrence of the full ordered dictionary ChunkId sequence, normalizes the concatenated dictionary under that canonical-sequence start with the same transform, zstd-decodes the normalized target chunk, inverse-BCJ under the frozen first-target-occurrence rule, then verifies original target length/ChunkId. A lane is invalid on any non-exact reconstruction.

This is a research codec counterfactual, not a production dependency or CSP v1 encoding; assigning Encoding 2/3 for real would require the normal format-revision/vector process. Normalization must be in-place within the already bounded dictionary/chunk buffers (or use an equivalently bounded scratch buffer); it does not receive a second whole-file/executable buffer. Because G4 keeps the v1 <=1 MiB dictionary/window and <=256 KiB output chunk, its hard RFC-eligibility apply bound remains **64 MiB over idle** under the D17 measurement convention on every required runtime platform. Exceeding it rejects G4 even if its byte reduction passes §11.

### 8.3 Zucchini reference lane

Zucchini is a whole-file executable-aware reference, not the G4 RFC gate. Pin Chromium Zucchini source commit 667ffb4e19970939936af2e7a169175ae4c1da5b and record the built executable SHA-256 plus full Chromium/build provenance. Use:

    zucchini -gen OLD NEW PATCH
    zucchini -apply OLD PATCH RECONSTRUCTED

Do not use -raw: the purpose is executable reference normalization. Apply must reproduce target SHA-256. Before the size run, parser support is probed on both base and target; a parser rejection is unsupported, not zero bytes and not a failed experiment.

Zucchini's documented model recognizes PE, ELF and DEX and executable architectures including x86/x64/ARM/AArch64; only the predeclared PE/ELF corpus subset above contributes here. Report Zucchini bytes next to generic references on the **same subset**. Do not extrapolate its subset ratio to the whole corpus.

## 9. G5 — already-compressed containers and deflate streams

### 9.1 Classification before measurement

G5 has a structure inventory and a reference attribution lane. Its input population is the **materialized changed files** used by PATCH-PREFREEZE/Patching, not the source archives used to obtain those trees.

Therefore the outer source packages named by patch-corpus.json — for example aspnetcore-runtime-*.zip, node-*.zip, node-*.tar.xz, dotnet-runtime-*.tar.gz and tzdata*.tar.gz — are provenance only and are **not G5 target files**. Counting them would measure the distribution container instead of CSP's per-file workload. Only compressed containers/streams that actually remain inside a materialized changed file are eligible. chunkshift-source's materialized source.tar is an uncompressed tar and is not treated as a deflate/ZIP container merely because it is an archive.

The inventory classifier runs before any Puffin/bsdiff output and classifies by structure, never by extension alone. The frozen primary classes are deliberately limited to self-describing containers/streams:

- **ZIP-compatible** — a valid ZIP central directory/end record is present, every referenced local header/range is in bounds, and the parser can enumerate members deterministically; .zip/.nupkg names receive no special credit without this structure;
- **gzip** — the complete input is a valid RFC 1952 member chain with deflate method 8 and no trailing unparsed bytes;
- **zlib** — the complete input is a valid RFC 1950 stream with CM=8 and valid FCHECK; `FDICT` MUST be 0 for this experiment because the frozen corpus supplies no external preset-dictionary bytes. `FDICT=1` is explicit `UNSUPPORTED/PRESET_DICTIONARY`, not a parse failure. The deflate payload must terminate exactly before the Adler-32 footer and the footer must verify;
- **compressed-other** — already-compressed payload not in the three supported structural classes;
- **not-compressed/unknown**.

Raw DEFLATE is intentionally **not auto-detected** by trying a decoder against arbitrary bytes: it has no self-identifying wrapper and doing so would make population membership parser/false-positive dependent. Likewise this ExperimentId does not scan arbitrary binaries for embedded deflate signatures. Deflate extents enter G5 only as deterministic children of a recognized ZIP/gzip/zlib structure. A later raw/embedded-stream study requires its own predeclared locator and ExperimentId.

For every accepted ZIP/gzip/zlib file, the inventory also runs the pinned Puffin Android-17 structural locator for that declared type and stores canonical sorted bit-extents plus their SHA-256. A pair is Puffin-supported only when both base and target pass the strict structural classifier and Puffin locator for the same type, every returned extent is in bounds/non-overlapping, and at least one side contains a deflate extent.

For ZIP specifically, the Puffin deflate extents must have a one-to-one correspondence with the structurally enumerated members whose compression method is deflate (method 8), in canonical member order; stored/other-method members generate no Puffin deflate extent. For gzip/zlib, the locator must identify exactly the structurally parsed deflate payload(s). Any count/order/range disagreement is `UNSUPPORTED/PARSER_DISAGREEMENT`, never a silent skip or a size of zero.

For ZIP-compatible pairs, match members by raw member name plus duplicate-name ordinal and record separately:

1. metadata-only/container-layout change — uncompressed bytes and compressed payload match while surrounding metadata/layout differs;
2. recompressed member — uncompressed member SHA-256 matches but deflate bytes differ;
3. changed compressed member — both versions exist but uncompressed bytes differ;
4. genuinely new compressed payload/member — no matched base member.

These categories are reported as target bytes and, where the CSP trace maps them exactly, unique-missing bytes. Ambiguous or unsupported members remain explicit.

If the committed G5 inventory contains no supported changed-file pair, G5 is recorded **NOT_PRESENT on the frozen corpus** and no Puffin size run is executed. A tiny subset is still measured/described if non-empty, but the whole-split §11 formula prevents it from being generalized. The protocol does not add synthetic nupkg/zip cases merely to preserve the original #183 hypothesis.

### 9.2 Puffin / PUFFDIFF reference lane

Pin AOSP Puffin at Android 17.0.0_r1, commit 343e23db1b4d81045e91a10244244893f5acd73b. Record the built puffin binary SHA-256, compiler/build identity and help output in the tool manifest.

Puffin is a deterministic deflate recompressor: it transforms deflate streams to a puff representation, uses a binary diff, then deterministically reconstructs the original deflate stream. Therefore its whole-file patch is useful evidence for deflate-instability headroom, but it is not a CSP-v1-compatible one-factor encoding. Android update_engine's PUFFDIFF apply path uses PuffPatch with a 5 MiB maximum cache; the reference lane pins that same cache rather than Puffin's larger standalone-tool default.

To isolate the deflate transform from the raw diff algorithm, freeze Puffin's patch_algorithm to 0 (bsdiff), not Zucchini. For a pair admitted by §9.1, TYPE is exactly one of {zip,gzip,zlib}; a structurally valid .nupkg is TYPE=zip. The exact reference operations are:

    puffin --operation=puffdiff --src_file=OLD --dst_file=NEW --patch_file=PATCH --src_file_type=TYPE --dst_file_type=TYPE --patch_algorithm=0
    puffin --operation=puffpatch --src_file=OLD --dst_file=RECON --patch_file=PATCH --cache_size=5242880

Run Puffin only on this predeclared supported subset and verify RECON by target SHA-256. The verbose Puffin-discovered extent lists must exactly match the inventory fingerprints; a mismatch invalidates the run. No file may become eligible because Puffin happened to produce a small patch.

To isolate the puff/huff transform from the raw binary-diff backend, build AOSP bsdiff from the **same Android 17.0.0_r1 release**, commit 6bbcf65f3b25bd09fc39d8166070f9adea325089. Use its BSDF2 writer with the same compressor set Puffin passes to libbsdiff:

    bsdiff --format bsdf2 --type bz2:brotli --brotli_quality 11 OLD NEW RAWPATCH
    bspatch OLD RAWRECON RAWPATCH

Verify RAWRECON by target SHA-256. The primary descriptive transform-attribution metric on the identical frozen subset is then:

puff_transform_gain = B_aosp_bsdiff_bsdf2_same_subset - B_puffin_same_subset

This is materially cleaner than subtracting Python bsdiff4 because Puffin itself invokes AOSP libbsdiff with BZ2+Brotli compressors. Brotli quality is pinned explicitly to 11 (the AOSP CLI maximum/default for this mode) so the control does not depend on an implicit tool default. Python bsdiff4 remains a continuity/generic reference and is still reported on the identical subset, but **puffin vs bsdiff4 is not labeled a causal deflate-transform gain**.

Also report CSP, zstd-patch-from and other references on exactly that subset. No Puffin result directly passes the 15% RFC gate. A large G5 result opens a separately identified follow-up experiment that must define a CSP-costed, bounded reversible container/stream codec before an RFC can be considered.

## 10. Frozen reference tools and modes

Every executable/package is pinned by immutable version/tag/commit and is also hashed at execution time. latest is forbidden. The compact evidence keeps both upstream identity and exact local binary/package SHA-256.

| reference | frozen identity | frozen mode |
| --- | --- | --- |
| CSP H0 | ChunkShift e967aeb6d4d467e94c5ac20f85e70ba0035d998d; production policy in §2 | production builder, H2-W2 when ≥2 CPUs |
| zstd patch-from | zstd v1.5.7, commit f8745da6ff1ad1e7bab384bd1f9d742439278e99 | zstd -19 --long=31 -q --patch-from=OLD NEW -o PATCH; decode zstd -d --long=31 -q --patch-from=OLD PATCH -o NEW |
| bsdiff | Python bsdiff4==1.2.6; source distribution SHA-256 2ab57d01a78b39e29e5accc9cfead4130982ded9dccbc4261bd0e9c51d6b751d | bsdiff4.diff(base,target) / bsdiff4.patch(base,patch); installed artifact SHA-256 recorded |
| AOSP bsdiff control | Android 17.0.0_r1, commit 6bbcf65f3b25bd09fc39d8166070f9adea325089 | bsdiff --format bsdf2 --type bz2:brotli --brotli_quality 11 OLD NEW PATCH; apply bspatch OLD NEW PATCH; G5 identical-subset same-backend control |
| xdelta3 modern | xdelta v3.2.0, commit ff322e592383227b0d65ddfde7e0e5bbc504dc15 | raw-byte VCDIFF: create `xdelta3 -a -A= -D -S none -e -9 -f -s OLD NEW PATCH`; apply `xdelta3 -D -R -d -f -s OLD PATCH NEW` |
| xdelta3 legacy continuity | xdelta3 v3.0.11, commit 81aebf78ae67c29f528088d65743643e5355e3d3 | raw-byte VCDIFF: create `xdelta3 -A= -D -S none -e -9 -f -s OLD NEW PATCH`; apply `xdelta3 -D -R -d -f -s OLD PATCH NEW`; descriptive continuity only |
| HDiffPatch memory | v5.1.3, commit 3b9dca715ca492873bf2c49e22e5d5b7d2a78620 | create: hdiffz -m-4 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH; apply: hpatchz -s-8m -f OLD PATCH NEW |
| HDiffPatch stream | same v5.1.3 | create: hdiffz -s-64 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH; apply: hpatchz -s-8m -f OLD PATCH NEW |
| Zucchini | Chromium component commit 667ffb4e19970939936af2e7a169175ae4c1da5b | -gen / -apply, executable-aware mode, supported G4 subset only |
| XZ BCJ | XZ Utils v5.8.4, commit d3e650e63c110e830fd5391e7f8b45df0b91d3da | research-only low-level x86/ARM64 reversible transform; no .xz container |
| Puffin | Android 17.0.0_r1 / 343e23db1b4d81045e91a10244244893f5acd73b | puffdiff/puffpatch commands in §9.2, patch_algorithm=0, apply cache 5,242,880 bytes (5 MiB, matching update_engine) |

HDiffPatch has two deliberately separate lanes. The memory lane uses the documented all-in-memory matcher for ratio-oriented evidence. The -s-64 lane is the streaming/bounded-memory comparator. Both use the same single-compressed-diff format, one thread and identical zstd compressor settings, so the comparison does not silently change compressor or parallelism. Both are applied with the same explicit 8 MiB hpatchz stream cache, so create-side matcher memory is not confounded with a different apply configuration. HDiffPatch v5.1.3 release archives publish these SHA-256 values: Linux x64 628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558; Linux ARM64 03e404e16d06479deaba645a09ed5c06636778b083b82bc7fc932ba34425430b; Windows x64 77f141386e5d8f785c1c846e10fbbc19b6c05aa00e3f59cc44670fb3f0e2ae94.

The modern xdelta 3.2.0 lane replaces 3.0.11 as the current reference. Xdelta 3.2 enables BLAKE3 whole-file armor by default, so `-a` disables that 3.2-only feature. Both reference lanes also disable the application header with `-A=` so filenames/build paths do not enter patch bytes, disable automatic external decompression with `-D`, and pin `-S none` so an optional/build-dependent secondary compressor cannot change the ratio. Apply supplies source/output explicitly and passes `-D -R`; VCDIFF window checksums remain enabled. The 3.0.11 lane is descriptive continuity only and cannot be used to claim that the current implementation was measured. If armored or secondary-compressed 3.2 output is captured at all, it is a separately named descriptive lane and never replaces this frozen raw-byte reference.

Tool failures are tool-error; structurally unsupported inputs are unsupported. Neither becomes zero bytes. A reference aggregate over a subset names its exact denominator and subset SHA-256.

## 11. The only RFC decision formula

The issue wording "recovers >=15% of CSP bytes on holdout" is frozen literally as reduction from current production CSP bytes on the corpus partition historically named `holdout` (the fixed evaluation split described in §3), not as a fraction of an external-tool gap or as an independent-generalization claim.

For factor F on holdout H:

**reduction_vs_csp(F,H) = (B_CSP(H) - B_F(H)) / B_CSP(H)**

The RFC size gate is:

**reduction_vs_csp(F,H) >= 0.15**

For a subset factor, B_F(H) includes factor bytes on the frozen eligible subset and unchanged H0 CSP bytes on every ineligible holdout file. Therefore a 30% win on 10% of holdout does not become a 30% corpus claim.

The following is **descriptive only**, never a gate:

gap_recovered(F,R,H) = (B_CSP(H) - B_F(H)) / (B_CSP(H) - B_R(H))

It is reported only when named reference R is evaluated on the same population and the denominator is positive. Always name R. Values above 100% are possible and reported as-is; that is one reason this metric is unsuitable for the RFC threshold.

A gate-eligible factor must additionally reconstruct every target exactly, have an explicit measured or analytically enforced apply-memory bound, report base-read amplification and lost properties, and be a one-factor CSP-costed counterfactual rather than merely a whole-file reference algorithm. When the lane reaches three-platform characterization, its per-file selected encoding/cost decisions and total physical bytes must be identical on linux-x64, linux-arm64 and win-x64; where the lab materializes a concrete revised-layout patch, its patch SHA-256 must also match across those platforms. Cross-platform divergence is an invalid/incomplete factor result, not timing noise.

Passing does **not** adopt anything. It means the factor is large enough to justify a separate RFC/design issue. G5-Puffin, G4-Zucchini and the sampled G2 oracle are reference/attribution evidence and cannot pass this gate directly.

## 12. Run plan and timing

### 12.1 Stage A — deterministic inventory and byte decomposition

Run on **Linux x64 only** after this protocol is merged/frozen.

First run the inventory-only prepass and commit canonically sorted G4/G5 manifests plus their calibration/holdout SHA-256 values. No G4/G5 size tool runs before that inventory commit. Then:

- regenerate H0 and validate baseline totals/digest;
- consume the frozen G4/G5 subset manifests;
- G1 byte lanes;
- G2: consume the merged #216 calibration oracle; materialize/run the §6.3 descriptive fixed-evaluation oracle sample through the shared oracle implementation; consume any full-population H5-F/H6-O/H6-P patch/trace evidence produced under the merged contract;
- G3 byte lanes;
- G4-BCJ byte lane plus Zucchini subset reference;
- G5 classification plus Puffin subset reference;
- whole-file reference tools.

One platform is sufficient for deterministic byte counts when the exact tool binary/build is pinned. This avoids spending three-platform CI time to rediscover the same integer byte count.

### 12.2 Stage B — runtime-sensitive implications

Every frozen byte lane is measured on calibration. The fixed evaluation split then receives **exactly one deterministic byte run for every predeclared gate-eligible lane**, not one calibration-selected winner. This matches #183's existential rule ("if one relaxation ... recovers >=15%") and prevents calibration-specific ranking from hiding a valid factor result.

The evaluation set is therefore fixed before any GAP factor bytes are produced:

- G1: G1-B1-R64, G1-B4-R256, G1-B8-R512 and G1-B32-R2048 all receive one calibration byte run and one evaluation byte run.
- G3: G3-RUN and G3-FILE both receive one calibration byte run and one evaluation byte run.
- G2: run no new selector family. Report both sampled-oracle populations descriptively; for the §11 gate, evaluate every merged-#216 H5-F/H6-O/H6-P lane for which full fixed-evaluation patch bytes exist. Missing full-population lanes remain NOT_EVALUATED. H8 inherits its parent bytes.
- G4: G4-BCJ receives one calibration/evaluation byte run over the frozen subset with unchanged H0 bytes outside it.
- G5: if the frozen subset is non-empty, Puffin plus its AOSP-bsdiff same-backend control receive one calibration/evaluation descriptive run; G5 has no direct RFC-gate lane until a later CSP-costed codec exists. If the subset is empty, report NOT_PRESENT.

**Calibration is a runtime-prioritization split, not a second RFC size gate and not a size-lane selector.** No threshold or parameter changes after any evaluation result is visible. The §11 gate is applied independently to each already-frozen gate-eligible lane; if multiple lanes pass, PATCH-GAP reports the full size/memory/read frontier and opens at most one factor-level RFC/design issue. This protocol does not choose a production winner.

Three-platform production-implication timing may be run before evaluation for any gate-eligible lane that already reaches 15% on calibration. A lane below 15% on calibration still gets its single evaluation byte run. If it unexpectedly reaches the §11 gate there, run the required three-platform timing/non-size checks **afterward on that unchanged lane without retuning or repeating its evaluation byte run** before treating it as RFC-qualified.

For CSP-counterfactual create/apply timing:

- use the same production H2-W2 payload topology where >=2 CPUs; do not add file-level parallelism;
- each measured whole-corpus create invocation runs in a fresh child process against the same already-materialized corpus/input lock; environment variables, processor affinity policy, runtime and tool build are identical within the pair;
- before measurement, run one untimed H0 warm-up and one untimed factor warm-up on that platform;
- expensive whole-corpus create uses exactly five measured paired rounds: rounds 1/3/5 run `H0 -> F`, rounds 2/4 run `F -> H0`; no other measured lane is interleaved inside a pair;
- compute `createWallRatio_r = wall(F_r) / wall(H0_r)` and the analogous CPU ratio per round; the platform summary is the median of the five paired ratios plus all five raw pairs. An interrupted/failed member invalidates the pair; do not silently replace only that member;
- use 10 independent repetitions for short operations; report median/p50, p95 only where at least 10 observations exist, plus min/max and sample count;
- peak-RSS evidence is separate from timing: one operation per fresh child process using the existing Patching idle-baseline convention, with no earlier zstd/GC state in that process;
- keep exact target verification outside the timed region where that does not change the measured operation; otherwise include it identically in H0/F and record that fact;
- record per-file selected-form/cost digests during the three-platform dispatch and enforce the cross-platform byte oracle from §11 before accepting timing/memory evidence.

External reference tools are timed on pinned Linux x64 primarily. They are not production dependencies, so three-platform timing is not required merely for symmetry. A second platform is required only when the tool itself changes format/algorithm by platform or cannot reproduce Linux output; divergence is a tool-specific limitation, not CSP evidence.

### 12.3 Frozen evaluation (`holdout`) ordering

1. Run every frozen byte lane on calibration; calibration may prioritize expensive runtime work but may not remove a predeclared size lane.
2. Before any GAP evaluation-factor bytes are produced, commit the lane definitions, G4/G5 subset fingerprints, all thresholds, the §6.3 G2 evaluation-oracle sample lock and the complete list of production-real G2 lanes whose full-population bytes already exist or are scheduled unchanged under the merged producer contract.
3. Optionally complete three-platform runtime checks early for any lane already at >=15% on calibration.
4. Run the fixed evaluation partition exactly once for every committed GAP-owned factor lane and H0; consume or execute only the §6-pinned G2 evidence under merged #216 semantics, never a GAP-private selector implementation.
5. Apply the §11 size formula independently to every gate-eligible lane, with no calibration veto and no post-evaluation lane creation.
6. If a lane first crosses 15% on evaluation, complete its required non-size/runtime checks afterward on the unchanged lane. Never retune its parameters or repeat its evaluation byte run to improve the result.

Exploratory smoke runs before protocol merge are not decision evidence and must carry exploratory in their evidence identity.

## 13. Evidence durability and provenance

A decision dataset is incomplete unless it leaves durable, recomputable evidence in the repository.

Commit under docs/research/results/data/PATCH-GAP-001-<date>-<n>/ at minimum:

- files.jsonl or equivalent compact per-file rows containing H0 and all included factor/reference byte metrics;
- tools.json with tool name, exact version/tag/commit, build command, version output, source/archive SHA-256 and executable/package SHA-256;
- inputs.json with repo commit, pairsSha256, corpus manifest/source-assets SHA-256 and materialized input SHA-256 values;
- subsets/g4.json and subsets/g5.json, canonically sorted, plus calibration/holdout SHA-256 values;
- artifacts.json with workflow run ID, artifact ID/name, raw artifact SHA-256, contained-file SHA-256/size and retention information;
- reproduction commands/scripts;
- a small evaluator that recomputes §11 and all published aggregates from the committed compact dataset.

The compact per-file dataset must be sufficient to recompute the decision after CI raw artifacts expire. A 90-day Actions artifact may hold verbose logs/samples, but it may never be the only link between the evidence record and quoted numbers.

Every raw result names its RunId, exact source commit, clean/dirty state, environment, framework/runtime, OS/architecture, processor count, command line, timestamps and sample count as required by docs/research/README.md.

## 14. Prior-art interpretation

The reference tools answer different questions and must not be collapsed into one leaderboard:

- zstd --patch-from uses the previous file as a large dictionary and long-range matching; it bounds G1+G2+G3 together and is an external gap marker, not an isolated factor.
- bsdiff and xdelta are generic byte-delta references. They show attainable whole-file delta size under different instruction/match models, not a drop-in CSP codec.
- HDiffPatch's memory and stream modes explicitly trade matching precision, memory and speed. Comparing both with one compressor setting makes that trade visible instead of treating hdiffz as one number.
- Zucchini models executable references/elements and is the executable-aware reference for G4; G4-BCJ is the cleaner per-chunk counterfactual.
- Puffin neutralizes deflate bitstream instability before binary diff and reconstructs the exact original deflate stream. Android update_engine carries PUFFDIFF as a patch operation and delegates reconstruction to puffpatch; that validates it as update-pipeline prior art, not as CSP semantics. It is the G5 attribution reference; its whole-file diff is not evidence that CSP should embed Puffin.
- OSTree static deltas are useful architectural prior art for content-aware delta generation. PATCH-GAP keeps that lesson at the measurement layer and does not import tree/update-set semantics from #184.

The purpose of prior art here is causal attribution: identify which CSP constraint is expensive and whether headroom survives CSP's desired properties. It is not a catalog of delta algorithms.

## 15. Interaction policy

No primary lane combines G1..G5 changes. After every participating factor has an individual result, a combined study may be proposed under a new identity such as PATCH-GAP-INT-001 with its own protocol, because interactions are not additive: a larger dictionary can change candidate-choice value; broader frames can change dictionary value; normalization can change both.

No combined result may retroactively change an individual factor's verdict.

## 16. Non-goals

- no CSP v1 production-format change;
- no CspEncoderPolicy.Default change;
- no production candidate selector in #217;
- no new ChunkShift instruction VM;
- no public tuning/API surface;
- no Repository dependency;
- no tree/update-set implementation or cross-file reference semantics (#184 owns that space);
- no silent combination of factors;
- no publication/release/default decision;
- no decision-bearing run before this protocol is reviewed, frozen and merged.

## 17. Freeze checklist

- [x] production baseline policy, K/C/radius, execution topology and exact byte anchor are recorded;
- [x] corpus and split hashes are recorded;
- [x] G1 byte/ref/window lanes are budget-saturating under the stable 16 KiB minimum and charge all reference metadata;
- [x] G2 is pinned to merged PATCH-ENC-005 commit `96fd9b296d6998cac397e61041f22df51e6dd43c`, shared trace/oracle schemas, exact sample semantics and production-real lane ids;
- [x] G3 groups, cost formula and lost properties are exact;
- [x] G4 supported formats/architectures, BCJ counterfactual and Zucchini reference are exact;
- [x] G5 classifier and Puffin attribution semantics are exact;
- [x] reference tools have immutable identities and fixed modes;
- [x] the 15% RFC formula has one interpretation;
- [x] subset membership is determined before size results and fingerprinted;
- [x] deterministic-byte and runtime-sensitive platform requirements are separated;
- [x] compact/raw evidence retention and SHA-256 lineage are mandatory;
- [x] interaction studies require a separate identity;
- [x] production/API/tree/update-set changes remain non-goals.

PATCH-ENC-005 is now an immutable merged dependency and §6 is reconciled to it. The remaining action is a final freeze review/CI pass of #217 itself, followed by merge if approved. No PATCH-GAP decision run is authorized while this PR remains unmerged.

## 18. Primary sources

- CSP and prior ChunkShift evidence: docs/architecture/CSP-V1-CANDIDATE.md, docs/architecture/PATCHING-DECISIONS.md, docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md, PATCH-ENC-002/003/004 evidence and docs/research/README.md.
- Zstandard v1.5.7: <https://github.com/facebook/zstd/releases/tag/v1.5.7> and <https://github.com/facebook/zstd/wiki/Zstandard-as-a-patching-engine>.
- Xdelta: <https://github.com/jmacd/xdelta/releases/tag/v3.2.0>; legacy continuity <https://github.com/jmacd/xdelta-gpl/releases/tag/v3.0.11>.
- bsdiff4 1.2.6: <https://pypi.org/project/bsdiff4/1.2.6/>.
- AOSP bsdiff Android 17.0.0_r1: <https://android.googlesource.com/platform/external/bsdiff/+/refs/tags/android-17.0.0_r1>.
- HDiffPatch v5.1.3: <https://github.com/sisong/HDiffPatch/releases/tag/v5.1.3> and project CLI documentation.
- XZ Utils BCJ: <https://github.com/tukaani-project/xz/releases/tag/v5.8.4> and liblzma BCJ API documentation.
- Chromium Zucchini: <https://chromium.googlesource.com/chromium/src/components/zucchini/>; pinned component commit 667ffb4e19970939936af2e7a169175ae4c1da5b.
- AOSP Puffin: <https://android.googlesource.com/platform/external/puffin/+/refs/tags/android-17.0.0_r1>; Android update_engine PUFFDIFF apply path: <https://android.googlesource.com/platform/system/update_engine/+/refs/heads/main/payload_consumer/>.
- OSTree static deltas: <https://ostreedev.github.io/ostree/man/ostree-static-delta.html>.
