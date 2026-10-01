# PATCH-GAP-001 protocol: decompose the remaining CSP size gap

Status: **DRAFT — informative study; freeze before decision-bearing runs.**  
Issue: [#183](https://github.com/definitely-stable/ChunkShift/issues/183) · Parent: [#7](https://github.com/definitely-stable/ChunkShift/issues/7)  
ExperimentId: `PATCH-GAP-001`  
Baseline commit for protocol work: `e967aeb6d4d467e94c5ac20f85e70ba0035d998d`  
Related candidate-policy protocol: PR #216 / `PATCH-ENC-005`.

## 1. Question

Where does the remaining patch-size gap between CSP v1 and byte-level/reference delta tools come from, and which factor — if any — is large enough to justify a future CSP revision?

This experiment is **informative**. It does not change CSP v1 or production defaults.

The current measured reference point from PATCH-PREFREEZE-001 is approximately:

- CSP encoding 1: 38.56 MiB;
- zstd `--patch-from`: 28.90 MiB;
- bsdiff: 20.64 MiB;

over the frozen changed-file corpus. This protocol must verify the exact baseline dataset/fingerprint it uses rather than relying on rounded historical numbers.

## 2. Independence and shared ownership

PATCH-GAP-001 owns decomposition of the size gap.

It does **not** own production candidate-selection semantics.

`PATCH-ENC-005` (#181, protocol PR #216) owns candidate ranking, resemblance/sketch retrieval and any production-real candidate trace.

For factor G2, this study must consume:

- the shared candidate trace;
- a clearly defined oracle upper bound;
- H5/H6 results when available;

rather than implementing a second independent resemblance selector.

This lets G2 answer "how many bytes are lost because of candidate choice?" while #181 answers "which selector should production use?"

## 3. Factors

Each factor is relaxed **one at a time** against the same baseline unless a later explicitly named interaction study is added.

### G1 — dictionary budget

Relax the CSP-v1-sized dictionary budget in the research harness:

- current 1 MiB / K=4 baseline;
- candidate budgets such as 4, 8 and 32 MiB;
- up to a pre-frozen chunk-count limit.

Record both byte recovery and the apply-side memory/base-read cost implied by the larger dictionary.

This is an upper-bound study, not a production-format proposal.

### G2 — candidate choice

Measure:

1. current offset-radius selection;
2. the PATCH-ENC-005 oracle upper bound;
3. production-real resemblance candidates from H5/H6 when available.

Do not fork candidate semantics in this experiment.

### G3 — frame granularity

Compare:

- one independent frame per missing chunk (CSP v1);
- one frame per run of adjacent missing chunks;
- a whole-file/reference-frame extreme comparable to `zstd --patch-from`.

The study must keep target reconstruction verifiable and record what independence/random-access properties are lost by each relaxation.

### G4 — executable normalization

On a clearly identified PE/ELF subset, measure BCJ-style branch normalization and Zucchini-style reference normalization.

Report:

- affected corpus bytes/files;
- patch-byte recovery;
- tool/filter coverage by architecture/format;
- create/apply cost where measurable.

Do not generalize PE/ELF results to the full corpus.

### G5 — compressed containers

Classify target bytes/files that live inside already-compressed containers/streams, including relevant zip/nupkg/deflate cases.

Measure a deflate-aware upper/reference lane where reproducible tooling is available (e.g. Puffin-style reasoning), and report the fraction of the corpus to which it applies.

## 4. Reference tools

Pin exact versions/build identities and documented modes before final runs.

At minimum evaluate or justify exclusion of:

- zstd `--patch-from`;
- bsdiff;
- xdelta3;
- HDiffPatch / `hdiffz` stream and memory modes;
- Zucchini on the supported executable subset.

Reference tools are comparison evidence, not implementation dependencies.

## 5. Corpus and split

Use the frozen patch corpus and its calibration/holdout split.

Record:

- corpus lock digest;
- materialization/source provenance;
- per-tool input fingerprint;
- subset fingerprints for G4/G5.

Factors are explored/tuned on calibration. Holdout is used to confirm whether a recovered-byte effect generalizes.

## 6. Metrics

For every factor/lane where applicable:

- patch/delta bytes;
- bytes recovered vs CSP baseline;
- percentage of the CSP-to-reference gap recovered;
- create wall/CPU time;
- apply/decode wall/CPU time;
- peak RSS / declared memory requirement;
- base bytes read / amplification;
- affected bytes/files for subset-only factors;
- exact target reconstruction.

For G1/G2/G3, explicitly state the apply-side cost and which CSP-v1 property is being relaxed.

## 7. Draft interpretation rule

This experiment does not directly ADOPT a production change.

A factor is **RFC-worthy** only when, on holdout:

- it recovers at least 15% of CSP bytes relative to the current production baseline (or the protocol freezes an equivalent exact definition before runs);
- its apply-side memory/cost remains explicitly bounded;
- the result is not explained only by an unrepresentative subset;
- the semantic/format property being relaxed is clearly identified.

Otherwise record the negative result and keep CSP v1 unchanged.

The final protocol must define exactly whether "15%" means reduction from CSP bytes or fraction of the gap to a named reference; do not leave both interpretations possible.

## 8. Run strategy

Before freeze, define:

- which factors require only one Linux byte-count lane;
- which require timing on linux-x64/linux-arm64/win-x64;
- tool pinning and artifact retention;
- sample/repetition strategy for timing;
- how external-tool failures/unsupported files are classified;
- compact per-file result format and raw-artifact digest manifest.

Do not use expiring CI artifacts as the only durable decision record.

## 9. Interaction policy

The primary experiment changes one factor at a time.

Only after individual-factor results exist may a named interaction run combine winners (for example candidate choice + larger dictionary). Such a run must have a separate recorded identity and cannot silently replace the one-factor decomposition.

## 10. Non-goals

- no CSP v1 change in this PR/experiment;
- no production encoder change;
- no second resemblance/sketch implementation;
- no instruction VM;
- no Repository dependency;
- no tree/update-set semantics (#184 owns that dimension).

## 11. Freeze checklist

Before marking the protocol frozen:

- [ ] exact CSP baseline and corpus fingerprint are recorded;
- [ ] each factor has exact parameters;
- [ ] the G2 dependency on PATCH-ENC-005 trace/oracle is explicit and implementable;
- [ ] reference tool versions/modes are pinned;
- [ ] calibration/holdout and subset fingerprints are fixed;
- [ ] "15% recovered" has one unambiguous formula;
- [ ] apply-side bounds are defined;
- [ ] timing/noise rules are defined where timing matters;
- [ ] raw/compact evidence retention and SHA-256 manifests are defined;
- [ ] interaction runs cannot contaminate one-factor conclusions.
