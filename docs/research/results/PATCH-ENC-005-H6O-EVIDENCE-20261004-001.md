# PATCH-ENC-005 — H6-O false-negative guard final evidence

EvidenceId: `PATCH-ENC-005/H6O-EVIDENCE-20261004-001`

Status: **REJECT — H6-O MISS / STOP_RESEMBLANCE**

Owning issue: #181  
Parent: #7  
Research-freeze gate: #251  
Protocol: `docs/benchmarks/PATCH-ENC-005-PROTOCOL.md` at `96fd9b296d6998cac397e61041f22df51e6dd43c`

## Provenance

- workflow: `PATCH-ENC-005 H6-O guard`
- GitHub run: `37187615407`
- run number / attempt: `2 / 1`
- source commit: `148ee8adc6df47cce0d866ea2e6eb25d684713d7`
- platform: `linux-x64` / GitHub-hosted `ubuntu-24.04`
- dataset role: fixed evaluation
- corpus digest: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`
- lane: `H6-O12-SF3-S128`
- decision artifact: `11301043088`
- decision artifact ZIP SHA-256: `114288db25329d32122dbfcc75e4cb24d4030b34da9616da3e44357817edad6f`
- artifact expiry: `2027-01-02T08:02:07Z`

The preceding run `37187324079` is **INVALID / non-result**: shallow checkout prevented materialization of historical corpus commit `9e7fd3a` before PatchLab measurement. PR #250 fixed the guard checkout to full history. No data from that invalid run contributes to this decision.

## Frozen guard result

| metric | H0 | H6-O | frozen gate | result |
| --- | ---: | ---: | --- | --- |
| physical patch bytes | 26,363,364 | 30,248,709 | H6-O <= 0.97 x H0 | **FAIL** |
| byte ratio | 1.000000 | 1.147376678 | <= 0.97 | **FAIL** |
| paired create wall median | — | 0.423377806 x H0 | <= 1.50 x | PASS |
| wall rounds <= 1.50 x | — | 5 / 5 | >= 4 / 5 | PASS |
| create peak over idle | 75,395,072 B | 80,658,432 B | <= 167,772,160 B | PASS |
| exact reconstruction | — | 844 / 844 checked files | all | PASS |
| accepted/correctness patch SHA agreement | — | 844 / 844 | all | PASS |

The five paired wall ratios were:

`0.443112984, 0.432691795, 0.410999551, 0.423377806, 0.407901660`.

H6-O is therefore much faster than H0 in this frozen execution shape, but its physical patch is **3,885,345 bytes larger (+14.74%)**. The speed result does not rescue the lane because the pre-frozen false-negative guard requires every gate to pass.

## Independent compact recomputation

The retained compact record independently recomputes the numeric gate from:

- the five accepted paired wall ratios;
- H0/H6 physical patch bytes;
- memory idle/peak measurements;
- retained correctness/determinism counts.

Run:

```text
python docs/research/results/data/PATCH-ENC-005-H6O-EVIDENCE-20261004-001/recompute.py \
  --compact docs/research/results/data/PATCH-ENC-005-H6O-EVIDENCE-20261004-001/compact.json \
  --verdict docs/research/results/data/PATCH-ENC-005-H6O-EVIDENCE-20261004-001/verdict.json
```

The raw `paired.json` is 3,578,319 bytes, so it is not duplicated into Git. Its exact SHA-256 and the decision artifact ZIP SHA-256 are retained in the bundle. The compact record was independently derived from the downloaded artifact: each lane had 844 valid decoder/target-digest checks and zero accepted-vs-correctness patch-SHA mismatches.

## Protocol consequence

The frozen §4.3 progression is exhausted:

- G2 exact whole-base oracle: **MISS**.
- H6-O fixed-evaluation false-negative guard: **MISS**.
- H5-F: **STOPPED**.
- H6-P: **STOPPED**.
- H8: **STOPPED**.
- full Phase B: **forbidden**.
- D15/default remains unchanged.

No selector threshold, H6 transform, grid, corpus or decision rule is retuned after observing this result.

This is a **REJECT** result for PATCH-ENC-005's frozen candidate-selection/resemblance continuation. It is not a claim that resemblance search is universally useless; a future materially different source/representation universe requires a new ExperimentId and new pre-frozen protocol.

## Architectural consequence

Combined with G2 (974,223 B H0 vs 974,049 B exact whole-base oracle, only 174 B saved), the evidence says that choosing a better **single contiguous dictionary candidate** is not the main remaining CSP size headroom on the frozen corpus.

The next size research therefore remains owned by PATCH-GAP-001 (#183), especially:

- G1 dictionary envelope;
- G3 frame/run granularity;
- G4 executable normalization.

PATCH-DICT-001 (#222) remains a separate hypothesis because multiple disjoint verified runs are a different source representation, not another search heuristic over one contiguous dictionary.

PATCH-ML-001 (#223) must not train a learned ranker over the exhausted old single-contiguous-candidate universe. Its useful entry gate is a richer source/representation universe with independently demonstrated exact-oracle headroom.

## Durable evidence

`docs/research/results/data/PATCH-ENC-005-H6O-EVIDENCE-20261004-001/` contains:

- `verdict.json` — workflow-produced frozen guard verdict;
- `summary.md` — workflow-produced summary;
- `execution.json` — RunId/GitHub/source identity;
- `documents.json` — workflow document SHA-256 manifest, including the omitted raw `paired.json`;
- `artifacts.json` — Actions artifact identity/expiry/ZIP digest;
- `compact.json` — retained numeric/correctness/determinism decision inputs;
- `recompute.py` — stdlib-only gate recomputation.

Conclusion: **REJECT / STOP_RESEMBLANCE; D15 unchanged.**
