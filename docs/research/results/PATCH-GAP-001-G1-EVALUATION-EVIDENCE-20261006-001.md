# PATCH-GAP-001 — G1 evaluation evidence

EvidenceId: `PATCH-GAP-001/G1-EVALUATION-EVIDENCE-20261006-001`

Status: **G1 REJECT — no frozen lane reaches the 15% RFC size threshold**

Owning issue: #183  
Parent: #7  
Research-freeze gate: #251  
Protocol: `docs/benchmarks/PATCH-GAP-001-PROTOCOL.md` at `5372678ae8451a71cc95eb24f30855cbbd7e0633`

## Provenance

- workflow: `PATCH-GAP-001 G1 evaluation`
- valid GitHub run: `37432365043`
- run number / attempt: `2 / 1`
- RunId: `PATCH-GAP-001/RUN-20261006-002-8983eb1-linux-x64-g1-evaluation`
- source commit: `8983eb11faa3e7c2df17b74d201249a19dd3dc13`
- platform: GitHub-hosted `ubuntu-24.04` / linux-x64
- dataset role: evaluation
- corpus digest: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`
- requested nested lane: `G1-B32-R2048` (produces B1/B4/B8/B32 together)
- sharding validation run: `37358264022` — **PASS**
- Actions result artifact: `11409716915`
- artifact ZIP SHA-256: `dc2a725f1612556f711d07b819f3de17d3b887bcd2e38a598eb557f9ae3ed2b0`
- artifact expiry: `2027-01-04T07:52:11Z`
- zstd backend: `ZstdSharp 0.8.8+2cd0c019693bc786a5fe5c3be94e107b24e7267e`
- zstd backend SHA-256: `b285bb9f59928becb8e048bf2a75e872f6555f8773d7b72386a9f77f447f425c`

The earlier run `37296111468` remains `INVALID_INFRA_TIMEOUT` and contributes no decision evidence. A later accidental redispatch, run `37485500579`, was rejected by the single-dispatch guard before any shard executed and likewise contributes no evidence.

## Sharding-equivalence gate

Before the replacement evaluation, run `37358264022` executed the same frozen G1 computation as eight deterministic GitHub-hosted shards over the already-revealed calibration population. The aggregate exactly reproduced the retained calibration result and passed `Require exact equivalence with frozen calibration`.

Therefore execution partitioning changed, but the frozen evaluator/codec, candidate starts/order, lane semantics, cost formula and decision threshold did not.

## Frozen H0 preflight

The valid evaluation regenerated the complete frozen H0 anchor before producing holdout bytes:

- files: 1,893
- calibration bytes: 11,860,274
- evaluation bytes: 26,363,364
- total bytes: 38,223,638
- patch-set SHA-256: `4a1f5c272da5cb12ba0d07c5d6b379701d7da154bdc1e92886bd27a8fc3781c6`
- frozen anchor: **PASS**

## Evaluation byte result

| lane | physical bytes | saved vs H0 | reduction vs H0 | frozen 15% threshold |
| --- | ---: | ---: | ---: | --- |
| `G1-B1-R64` | 26,020,421 | 342,943 | 1.300832% | **FAIL** |
| `G1-B4-R256` | 25,789,917 | 573,447 | 2.175166% | **FAIL** |
| `G1-B8-R512` | 25,763,908 | 599,456 | 2.273822% | **FAIL** |
| `G1-B32-R2048` | 25,763,908 | 599,456 | 2.273822% | **FAIL** |

H0 evaluation size is `26,363,364` bytes.

The best frozen result is a tie between B8/R512 and B32/R2048 at only `2.273822%` reduction. B32 adds no bytes advantage over B8, so the observed benefit saturates by the 8 MiB / 512-ref envelope on holdout.

## Base-read cost

- H0 base bytes read: `6,742,496,615`
- G1 factor base bytes read: `596,652,111,119`
- factor/H0 amplification: `88.491273365x`

The lane is therefore not merely far below the size threshold; its frozen search/history counterfactual also carries a very large base-read cost.

## Concentration

Only `7` of `844` evaluation file rows improve at B32.

The top two files account for `97.74%` of all B32 savings; the top four account for `99.89%`. Most savings come from the two large `node-linux-x64/bin/node` transitions.

| saved | family | path | versions |
| ---: | --- | --- | --- |
| 384,257 B | `node-linux-x64` | `bin/node` | `24.19.0 -> 24.20.0` |
| 201,643 B | `node-linux-x64` | `bin/node` | `24.20.0 -> 24.21.0` |
| 8,836 B | `node-win-x64` | `node.exe` | `24.20.0 -> 24.21.0` |
| 4,078 B | `node-win-x64` | `node.exe` | `24.19.0 -> 24.20.0` |
| 349 B | `chunkshift-source` | `source.tar` | `9e7fd3a -> 27aa319` |
| 227 B | `node-linux-x64` | `CHANGELOG.md` | `24.19.0 -> 24.20.0` |
| 66 B | `node-linux-x64` | `CHANGELOG.md` | `24.20.0 -> 24.21.0` |

## Decision

The protocol requires the frozen 15% gate independently on every predeclared G1 evaluation lane. All four lanes fail it by a wide margin.

Therefore:

- **G1 is formally REJECTED for PATCH-GAP-001**;
- no G1 three-platform timing/apply-RSS follow-up is authorized or required;
- no G1 dictionary/history envelope is promoted into CSP semantics;
- production `CspEncoderPolicy.Default`, CSP v1 and public API remain unchanged;
- the result does not prove that large dictionaries are universally bad; it rejects these frozen contiguous-prefix/H0-start envelopes under this corpus and accounting model;
- PATCH-GAP-001 itself remains **RUNNING** and advances to the next predeclared factor, **G3 frame/run granularity**.

Next action: **RUN_G3_CALIBRATION_AND_EVALUATION** exactly as frozen in §7 and §12 of the protocol.

## Durable evidence

`docs/research/results/data/PATCH-GAP-001-G1-EVALUATION-EVIDENCE-20261006-001/` contains:

- `evaluation.json` — compact formal G1 verdict and next action;
- `compact.json` — family aggregates, all 7 improved B32 rows, H0/lane totals and detail-manifest identity;
- `execution.json` — evaluation, sharding-validation and invalid-predecessor lineage plus aggregate environment provenance;
- `documents.json` — SHA-256/size identities for workflow-produced root documents and the raw-detail manifest;
- `artifacts.json` — evaluation and sharding-validation artifact identities/digests/expiry;
- `summary.md` and `h0-summary.md` — exact workflow-produced summaries;
- `recompute.py` — stdlib-only recomputation of lane totals, 15% gates, improved-file count and base-read amplification.

The raw detail JSON set is about 57.8 MiB unpacked and is not duplicated into Git. It remains bound by the compact detail-manifest identity and the retained Actions artifact.

Conclusion: **G1 REJECT; advance PATCH-GAP-001 to G3.**
