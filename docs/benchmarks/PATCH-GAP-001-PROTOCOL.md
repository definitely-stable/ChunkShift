# PATCH-GAP-001 protocol: decompose the remaining CSP size gap

Status: **BLOCKED DRAFT — #216 / PATCH-ENC-005 must be freeze-reviewed and merged first; then this protocol must pin/reconcile that merged producer contract before PATCH-GAP-001 can freeze.**  
Issue: [#183](https://github.com/definitely-stable/ChunkShift/issues/183) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
ExperimentId: PATCH-GAP-001  
Protocol baseline commit: e967aeb6d4d467e94c5ac20f85e70ba0035d998d  
Related candidate-policy protocol: [PR #216](https://github.com/definitely-stable/ChunkShift/pull/216) / PATCH-ENC-005. Current review snapshot: `70e9d906f7979200695597f6ead50d4f7efd88f5`; this SHA is **not** a frozen dependency and may move again before #216 merges.  
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

G1 is an upper-bound experiment. Any result beyond H0 cannot be emitted as CSP v1 because it may exceed the v1 4-reference, 1 MiB dictionary and/or 1 MiB window maxima.

CSP v1 also persists `DictionaryCount` as **UInt8** in both the 40-byte PAYL entry header and the 24-byte PIDX entry, so B4/R256 and larger are not representable by merely changing a semantic maximum. To make research bytes fully defined without granting a free compact encoding, G1 freezes this **synthetic revised-header accounting model only**:

- PAYL keeps its 40-byte fixed header; bytes 37..38 are treated as little-endian UInt16 DictionaryCount and byte 39 remains reserved zero;
- PIDX keeps its 24-byte fixed entry; bytes 21..22 are treated as little-endian UInt16 DictionaryCount and byte 23 remains reserved zero;
- dictionary ChunkIds still begin at PAYL offset 40 and still cost exactly 32 bytes each;
- every frozen G1 cap (maximum 2,048) fits UInt16.

This is not CSP v1 and is not a proposed wire revision; it is the minimal direct-list counterfactual needed to price the factor consistently. A real RFC would need to choose/version the layout and raise the dictionary/window limits explicitly. A future compact run descriptor, range encoding or other cheaper reference representation is a separate factor and receives no free credit here.

## 6. G2 — candidate choice, owned by PATCH-ENC-005

### 6.1 Blocking producer dependency

PATCH-GAP-001 cannot freeze G2 against a mutable producer. PR #216 is currently an unmerged Draft. Its current head `70e9d906f7979200695597f6ead50d4f7efd88f5` has incorporated the latest review corrections, but remains a **moving review snapshot only** until the PR is freeze-reviewed and merged.

The freeze order is mandatory:

1. freeze-review and merge #216;
2. record the merged PATCH-ENC-005 protocol commit and authoritative trace/oracle schema identity here;
3. reconcile this entire §6 and the G2 run-plan text against that merged contract;
4. only then mark G2 exact in §17 and freeze #217.

No PATCH-GAP G2 run is authorized before that reconciliation. If merged #216 changes its progression gate, evaluation population, H5/H6 availability, trace fields or oracle semantics, #217 follows the merged producer contract rather than this non-normative review snapshot.

### 6.2 Boundary

PATCH-GAP-001 does **not** implement Finesse, Odess, Gear-derived retrieval, a new similarity index, or another selector. #181 / PR #216 owns selector semantics, the shared trace, the exact whole-base oracle implementation and production-real H5/H6 work.

G2 answers: **how much size headroom is attributable to dictionary choice under today's K=4 / 1 MiB / one-frame-per-chunk CSP envelope?**

### 6.3 Post-merge consumer handshake

While #216 is open, **no selector/oracle/trace details below this boundary are normative in PATCH-GAP**. Duplicating a mutable producer specification here would create two sources of truth.

After #216 merges, the reconciliation commit on #217 must record all of the following before the G2 freeze-checklist item can be checked:

- the exact merged PATCH-ENC-005 protocol commit SHA and the implementation/evidence commit used by any consumed run;
- the authoritative candidate-trace schema id/version and exact dataset-role vocabulary;
- the exact calibration-oracle sample identity/lock and any progression/false-negative-guard semantics that determine which production-real resemblance lanes exist;
- the production-real G2 lane ids whose **full fixed-evaluation population** bytes may participate in the §11 whole-split gate;
- any fresh-confirmation dataset lock and verdict exported by PATCH-ENC-005, recorded as additional selector evidence but not substituted for #183's fixed-evaluation formula;
- SHA-256 of every consumed trace/oracle/result document and the upstream policy/lane fingerprint needed to prove it belongs to that merged contract.

PATCH-GAP then derives its per-entry selected cost only from the merged producer's authoritative fields. Under the current review snapshot this is conceptually `storedBytes + 32 × dictionaryRefs`, but even that field mapping is **non-normative here until the merged schema is pinned**.

The reconciliation must be a documentation-only change in #217 unless the merged producer contract exposes a genuine missing evidence field. In that case, fix/version the producer evidence contract first; #217 must not reconstruct selector decisions or invent a private compatibility shim.

### 6.4 Current snapshot note — informative only

At review snapshot `70e9d906f7979200695597f6ead50d4f7efd88f5`, #216 has already moved beyond the older assumptions that were previously copied here: its historical holdout is a fixed evaluation split, it defines a separate fresh confirmation set, its sampled G2 oracle is a prioritization gate with one pre-frozen H6-O false-negative guard, and its candidate-trace schema carries explicit protocol/source commit and dataset-role identity. These facts explain the required dependency ordering; **they are not frozen PATCH-GAP semantics**.

## 7. G3 — frame granularity

### 7.1 Why zstd --patch-from is not the G3 lane

Whole-file zstd --patch-from changes dictionary scope, candidate choice and frame granularity simultaneously. It remains an external reference, but cannot be counted as the isolated G3 result.

G3 instead uses a conservative counterfactual cost model that changes grouping of CSP payload bytes while holding candidate generation and dictionary budget fixed.

### 7.2 Frozen G3 lanes

- G3-H0 — exact production CSP: one independent stored form per distinct missing target chunk.
- G3-RUN — one zstd frame for each maximal run of payload-bearing first-occurrence missing target records that are consecutive in manifest index and physically adjacent in target content. A base-reused record, duplicate already-supplied ChunkId, or discontinuity ends the run.
- G3-FILE — one zstd frame for the ordered sequence of all payload-bearing first-occurrence missing chunks of a changed file. This is the strongest one-factor within-file framing extreme; it is **not** whole-target --patch-from.
- G3-REF-PATCHFROM — the pinned whole-file zstd --patch-from lane from §10. It is the requested whole-file/reference extreme, but is descriptive only because it simultaneously changes dictionary scope, candidate choice and framing. It is never substituted for B_G3 and cannot pass the G3 RFC gate.

For RUN/FILE, input bytes are the concatenation of the group's original target chunks in first-target-record order. The group uses only the dictionary that H0 selected for the group's first payload entry; if H0 selected raw or no-dictionary zstd there is no base dictionary. No group-level candidate search, larger dictionary or whole-base oracle is allowed. The zstd window remains capped at 1 MiB.

This anchor rule is intentionally conservative: later chunks lose their independent H0 dictionary changes. A single zstd frame cannot swap raw-prefix dictionaries between chunk boundaries, so G3 measures the **net coalescing envelope**: cross-chunk frame context plus the required loss of per-entry dictionary reselection. It must not be described as a pure context-carry gain, and the loss must not be repaired by silently importing G1/G2.

### 7.3 Exact synthetic physical-byte formula

G3 does not invent a parsable CSP file format. It is a **synthetic accounting counterfactual**: all non-payload CSP bytes and every existing 40-byte PAYL entry / PIDX cost are conservatively reserved, while only stored frame bytes and dictionary-reference bytes are replaced:

B_G3 = B_CSP - sum(entryStoredBytes + 32 × entryDictRefs) + sum(groupFrameBytes + 32 × anchorDictRefs)

This is conservative because it grants no hypothetical savings from deleting per-entry headers or PIDX entries. The compact dataset records every term so the total can be independently recomputed.

The reconstruction oracle decodes each group, splits decoded bytes by the known target chunk lengths, verifies every target ChunkId, and reconstructs the complete target file for SHA-256 equality.

### 7.4 Lost properties

| property | H0 | G3-RUN | G3-FILE | G3-REF-PATCHFROM |
| --- | --- | --- | --- | --- |
| independent target-chunk verification | yes | no, group scope | no, file-payload scope | no, whole-file scope |
| dictionary locality/change per chunk | yes | only group anchor | only file anchor | previous file is one large reference dictionary |
| random payload access | chunk | run | file payload | file |
| failure isolation | chunk | run | file payload | file |
| parallel decode/apply | many entries | fewer runs | at most one frame/file | per file |
| bounded decoder history | 1 MiB | 1 MiB | 1 MiB | reference-tool bound, not CSP v1 |
| CSP declarative codec model | current v1 | conceptually retainable, format revision required | conceptually retainable, format revision required | no; multi-factor external reference |

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
- A contiguous H0 dictionary is normalized from its own base-file start offset, never from the target offset. Target chunks use their target-file offset.
- The inverse uses the exact same architecture, untouched prefix, start offset and processed-length boundary. Evidence records all four values for every normalized target/dictionary span.

Ignoring the returned processed length, concatenating an unprocessed tail into the filter state, or deriving dictionary position from the target offset invalidates the lane.

For every eligible distinct missing chunk the research driver normalizes the target bytes and each of that entry's **same H0 candidate dictionaries**, then runs the same L19/raw-prefix/H20/C20 choice and 32-byte/reference cost. Candidate starts, K=4, C=8, 256 KiB radius, 1 MiB dictionary and 1 MiB zstd window do not change.

Apply is the exact inverse: verify/read the named base chunks, normalize dictionary bytes under the frozen base-offset rule, zstd-decode the normalized target chunk, inverse-BCJ under the frozen target-offset rule, then verify the original target ChunkId. A lane is invalid on any non-exact reconstruction.

This is a research codec counterfactual, not a production dependency or CSP v1 encoding. Normalization must be in-place within the already bounded dictionary/chunk buffers (or use an equivalently bounded scratch buffer); it does not receive a second whole-file/executable buffer. Because G4 keeps the v1 <=1 MiB dictionary/window and <=256 KiB output chunk, its hard RFC-eligibility apply bound remains **64 MiB over idle** under the D17 measurement convention on every required runtime platform. Exceeding it rejects G4 even if its byte reduction passes §11.

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
- **zlib** — the complete input is a valid RFC 1950 stream (CM=8, FCHECK valid, declared optional dictionary field structurally present when set), the deflate payload terminates exactly before the Adler-32 footer, and the footer verifies;
- **compressed-other** — already-compressed payload not in the three supported structural classes;
- **not-compressed/unknown**.

Raw DEFLATE is intentionally **not auto-detected** by trying a decoder against arbitrary bytes: it has no self-identifying wrapper and doing so would make population membership parser/false-positive dependent. Likewise this ExperimentId does not scan arbitrary binaries for embedded deflate signatures. Deflate extents enter G5 only as deterministic children of a recognized ZIP/gzip/zlib structure. A later raw/embedded-stream study requires its own predeclared locator and ExperimentId.

For every accepted ZIP/gzip/zlib file, the inventory also runs the pinned Puffin Android-17 structural locator for that declared type and stores the canonical sorted bit-extents plus their SHA-256. A pair is Puffin-supported only when both base and target pass the strict structural classifier and Puffin locator for the same type, every returned extent is in bounds, and at least one side contains a deflate extent. A disagreement between the strict classifier and Puffin is `UNSUPPORTED/PARSER_DISAGREEMENT`, never a silent skip or a size of zero.

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
| xdelta3 modern | xdelta v3.2.0, commit ff322e592383227b0d65ddfde7e0e5bbc504dc15 | create: xdelta3 -a -e -9 -f -s OLD NEW PATCH; apply: xdelta3 -d -f -s OLD PATCH NEW; `-a` disables 3.2 armor so this is legacy-compatible VCDIFF accounting |
| xdelta3 legacy continuity | xdelta3 v3.0.11, commit 81aebf78ae67c29f528088d65743643e5355e3d3 | create: xdelta3 -e -9 -f -s OLD NEW PATCH; apply: xdelta3 -d -f -s OLD PATCH NEW; descriptive continuity with PATCH-PREFREEZE only |
| HDiffPatch memory | v5.1.3, commit 3b9dca715ca492873bf2c49e22e5d5b7d2a78620 | create: hdiffz -m-4 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH; apply: hpatchz -s-8m -f OLD PATCH NEW |
| HDiffPatch stream | same v5.1.3 | create: hdiffz -s-64 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH; apply: hpatchz -s-8m -f OLD PATCH NEW |
| Zucchini | Chromium component commit 667ffb4e19970939936af2e7a169175ae4c1da5b | -gen / -apply, executable-aware mode, supported G4 subset only |
| XZ BCJ | XZ Utils v5.8.4, commit d3e650e63c110e830fd5391e7f8b45df0b91d3da | research-only low-level x86/ARM64 reversible transform; no .xz container |
| Puffin | Android 17.0.0_r1 / 343e23db1b4d81045e91a10244244893f5acd73b | puffdiff/puffpatch commands in §9.2, patch_algorithm=0, apply cache 5,242,880 bytes (5 MiB, matching update_engine) |

HDiffPatch has two deliberately separate lanes. The memory lane uses the documented all-in-memory matcher for ratio-oriented evidence. The -s-64 lane is the streaming/bounded-memory comparator. Both use the same single-compressed-diff format, one thread and identical zstd compressor settings, so the comparison does not silently change compressor or parallelism. Both are applied with the same explicit 8 MiB hpatchz stream cache, so create-side matcher memory is not confounded with a different apply configuration. HDiffPatch v5.1.3 release archives publish these SHA-256 values: Linux x64 628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558; Linux ARM64 03e404e16d06479deaba645a09ed5c06636778b083b82bc7fc932ba34425430b; Windows x64 77f141386e5d8f785c1c846e10fbbc19b6c05aa00e3f59cc44670fb3f0e2ae94.

The modern xdelta 3.2.0 lane replaces 3.0.11 as the current reference. Xdelta 3.2 enables BLAKE3 whole-file armor by default in the CLI; `-a` deliberately disables that 3.2-only application-header verification so the measured patch bytes remain comparable to the legacy VCDIFF-oriented reference rather than charging a new integrity/container feature to the delta algorithm. Decode of that unarmored patch uses the ordinary `-d` command. The 3.0.11 lane remains only to connect new results to PATCH-PREFREEZE evidence; it cannot be used to claim that the current xdelta implementation was measured. If armored 3.2 output is captured at all, it is a separately named descriptive lane and never replaces the frozen modern reference above.

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

A gate-eligible factor must additionally reconstruct every target exactly, have an explicit measured or analytically enforced apply-memory bound, report base-read amplification and lost properties, and be a one-factor CSP-costed counterfactual rather than merely a whole-file reference algorithm.

Passing does **not** adopt anything. It means the factor is large enough to justify a separate RFC/design issue. G5-Puffin, G4-Zucchini and the sampled G2 oracle are reference/attribution evidence and cannot pass this gate directly.

## 12. Run plan and timing

### 12.1 Stage A — deterministic inventory and byte decomposition

Run on **Linux x64 only** after this protocol is merged/frozen.

First run the inventory-only prepass and commit canonically sorted G4/G5 manifests plus their calibration/holdout SHA-256 values. No G4/G5 size tool runs before that inventory commit. Then:

- regenerate H0 and validate baseline totals/digest;
- consume the frozen G4/G5 subset manifests;
- G1 byte lanes;
- no G2 lane until #216 is merged and §6 is reconciled/pinned to that merged producer contract; after reconciliation, use only the shared #216 oracle/trace/production-real lanes authorized there;
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
- G2: **blocked/provisional until #216 merges**. After §6 reconciliation, use exactly the sampled-oracle and full-population production-real evaluation lanes authorized by the merged producer; GAP adds or drops none.
- G4: G4-BCJ receives one calibration/evaluation byte run over the frozen subset with unchanged H0 bytes outside it.
- G5: if the frozen subset is non-empty, Puffin plus its AOSP-bsdiff same-backend control receive one calibration/evaluation descriptive run; G5 has no direct RFC-gate lane until a later CSP-costed codec exists. If the subset is empty, report NOT_PRESENT.

**Calibration is a runtime-prioritization split, not a second RFC size gate and not a size-lane selector.** No threshold or parameter changes after any evaluation result is visible. The §11 gate is applied independently to each already-frozen gate-eligible lane; if multiple lanes pass, PATCH-GAP reports the full size/memory/read frontier and opens at most one factor-level RFC/design issue. This protocol does not choose a production winner.

Three-platform production-implication timing may be run before evaluation for any gate-eligible lane that already reaches 15% on calibration. A lane below 15% on calibration still gets its single evaluation byte run. If it unexpectedly reaches the §11 gate there, run the required three-platform timing/non-size checks **afterward on that unchanged lane without retuning or repeating its evaluation byte run** before treating it as RFC-qualified.

For CSP-counterfactual create/apply timing:

- use the same production H2-W2 topology where ≥2 CPUs;
- use 10 independent repetitions for short operations;
- for whole-corpus expensive create lanes use five paired whole-corpus repetitions, alternating H0/F and retaining every sample;
- report median, p50, p95 when at least 10 samples exist, min/max and sample count;
- use one process per peak-RSS measurement and the existing Patching idle-baseline convention;
- keep exact target verification outside the timed region where that does not change the measured operation; otherwise include it identically in both lanes and record that fact.

External reference tools are timed on pinned Linux x64 primarily. They are not production dependencies, so three-platform timing is not required merely for symmetry. A second platform is required only when the tool itself changes format/algorithm by platform or cannot reproduce Linux output; divergence is a tool-specific limitation, not CSP evidence.

### 12.3 Frozen evaluation (`holdout`) ordering

1. Run every frozen byte lane on calibration; calibration may prioritize expensive runtime work but may not remove a predeclared size lane.
2. Before any GAP evaluation-factor bytes are produced, commit the lane definitions, G4/G5 subset fingerprints, all thresholds and the complete list of evaluation lanes; for G2 this list is unavailable until §6's #216 dependency is reconciled.
3. Optionally complete three-platform runtime checks early for any lane already at >=15% on calibration.
4. Run the fixed evaluation partition exactly once for every committed evaluation lane and H0; run any G2 evaluation oracle/production-real lane only as authorized by the merged #216 contract.
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
- [ ] G2 producer dependency is not yet frozen: merge #216, pin its merged protocol commit/schema here, then reconcile §6/run-plan semantics;
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

This PR is **not freeze-ready while #216 is an unmerged/mutable producer**. The next allowed sequence is: resolve and merge #216, pin/reconcile its merged contract here, repeat freeze-review of #217, then merge #217. The protocol authorizes no PATCH-GAP decision run while that dependency/checklist item remains open or while this PR remains unmerged.

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
