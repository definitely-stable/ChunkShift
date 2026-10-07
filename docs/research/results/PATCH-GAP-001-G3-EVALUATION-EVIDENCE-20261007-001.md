# PATCH-GAP-001 — G3 evaluation evidence

EvidenceId: `PATCH-GAP-001/G3-EVALUATION-EVIDENCE-20261007-001`

Status: **G3 REJECT — neither frozen frame-granularity lane reaches the 15% RFC size threshold**

Owning issue: #183  
Parent: #7  
Research-freeze gate: #251  
Protocol: `docs/benchmarks/PATCH-GAP-001-PROTOCOL.md` at `5372678ae8451a71cc95eb24f30855cbbd7e0633`

## Provenance

Calibration:

- workflow: `PATCH-GAP-001 G3 calibration`
- GitHub run: `37566276539` (`1 / 1`)
- RunId: `PATCH-GAP-001/RUN-20261007-001-4975b0d-linux-x64-g3-calibration`
- source commit: `4975b0d4121b0bee426b9a7e3fce140918cce151`
- artifact: `11458943321`
- artifact ZIP SHA-256: `061c02256b53dc2d15c1f5a64ccef0af062087b6dd40e1f547443be5f6a5464a`

Evaluation:

- workflow: `PATCH-GAP-001 G3 evaluation`
- GitHub run: `37590709260` (`1 / 1`)
- RunId: `PATCH-GAP-001/RUN-20261007-001-4975b0d-linux-x64-g3-evaluation`
- source commit: `4975b0d4121b0bee426b9a7e3fce140918cce151`
- artifact: `11469621374`
- artifact ZIP SHA-256: `0a8d5766c19c6520ccf5a18378720de253c1d698164f0c687d144213ff5805bb`

Common identity:

- GitHub-hosted `ubuntu-24.04` / linux-x64
- frozen corpus digest: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`
- zstd backend: `ZstdSharp 0.8.8+2cd0c019693bc786a5fe5c3be94e107b24e7267e`
- backend SHA-256: `b285bb9f59928becb8e048bf2a75e872f6555f8773d7b72386a9f77f447f425c`

Both workflows completed successfully and their independent `verify_patch_gap_g3.py` recomputation passed.

## Frozen H0 preflight

Both runs regenerated the same complete frozen H0 anchor before consuming their split:

- files: 1,893
- calibration bytes: **11,860,274**
- evaluation bytes: **26,363,364**
- total bytes: **38,223,638**
- patch-set SHA-256: `4a1f5c272da5cb12ba0d07c5d6b379701d7da154bdc1e92886bd27a8fc3781c6`
- frozen anchor: **PASS**

## Calibration

| lane | physical bytes | saved vs H0 | reduction vs H0 | groups | members | base-read amplification | 15% gate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `G3-RUN` | 31,473,592 | -19,613,318 | -165.369856% | 433 | 1,923 | 1.028897x | **FAIL** |
| `G3-FILE` | 44,856,434 | -32,996,160 | -278.207401% | 446 | 2,321 | 1.033003x | **FAIL** |

Calibration is not a lane selector under the frozen protocol. Both lanes therefore proceeded to the fixed evaluation split exactly once.

## Evaluation byte result

| lane | physical bytes | saved vs H0 | reduction vs H0 | groups | members | base-read amplification | frozen 15% threshold |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `G3-RUN` | **75,922,335** | **-49,558,971** | **-187.984246%** | 109 | 3,277 | 1.004881x | **FAIL** |
| `G3-FILE` | **79,825,697** | **-53,462,333** | **-202.790255%** | 25 | 3,314 | 1.000724x | **FAIL** |

H0 evaluation size is `26,363,364` bytes. `G3-RUN` is about **2.880x** H0 and `G3-FILE` about **3.028x** H0.

The failure is therefore not marginal. Both frozen frame-granularity counterfactuals increase physical patch bytes by roughly 50–53 MiB on the evaluation split.

## Concentration and interpretation

For `G3-RUN`, only 20 of 844 evaluation file rows improve, 820 are unchanged and 4 regress. The four regressions are the four large Node executables and account for the entire gross regression.

| added bytes | family | path | versions |
| ---: | --- | --- | --- |
| 15,744,945 | `node-linux-x64` | `bin/node` | `24.20.0 -> 24.21.0` |
| 11,823,991 | `node-linux-x64` | `bin/node` | `24.19.0 -> 24.20.0` |
| 11,461,934 | `node-win-x64` | `node.exe` | `24.20.0 -> 24.21.0` |
| 10,595,230 | `node-win-x64` | `node.exe` | `24.19.0 -> 24.20.0` |

For `G3-FILE`, 18 rows improve, 819 are unchanged and 7 regress. The same four Node executables contribute about 99.7% of gross added bytes.

This supports a narrow conclusion: under the frozen G3 semantics, replacing independently selected H0 payload frames with one grouped frame whose only base prefix is the first payload member's H0 dictionary destroys too much local dictionary adaptation on the large executable deltas. It does **not** prove that all multi-chunk framing is bad, nor does it authorize retuning G3 after observing evaluation.

Base-read amplification is close to 1x in both evaluation lanes, so I/O amplification is not the cause of rejection.

## Decision

The protocol requires at least 15% whole-evaluation physical-byte reduction before a G3 lane is eligible for three-platform runtime/apply-RSS characterization or later RFC promotion. Neither lane is close to the gate.

Therefore:

- **G3 is formally REJECTED for PATCH-GAP-001**;
- no G3 three-platform timing/apply-RSS follow-up is authorized or required;
- no grouped-frame encoding or continuation semantics are promoted into CSP;
- production `CspEncoderPolicy.Default`, CSP v1 and public API remain unchanged;
- the result rejects only the frozen `G3-RUN` and `G3-FILE` constructions on this corpus and accounting model;
- PATCH-GAP-001 itself remains **RUNNING** and advances to **G4 executable normalization**.

Next action: **RUN_G4_EXECUTABLE_NORMALIZATION** using the already frozen Stage-A G4 subset manifest. G5 remains `NOT_PRESENT` on the materialized corpus.

## Durable evidence

`docs/research/results/data/PATCH-GAP-001-G3-EVALUATION-EVIDENCE-20261007-001/` contains:

- `evaluation.json` — formal G3 verdict and next action;
- `compact.json` — split/family aggregates, file outcome counts and largest evaluation regressions;
- `documents.json` — SHA-256/size identities for workflow-produced root documents plus raw-detail manifest identities;
- `artifacts.json` — calibration/evaluation workflow and artifact identities/digests/expiry;
- workflow-produced summaries and H0 summaries;
- `recompute.py` — stdlib-only recomputation of split lane totals and frozen 15% gates from family aggregates.

The raw per-file detail JSON sets are intentionally not duplicated into Git; their count/byte/digest identities are retained in `documents.json` and the complete Actions artifacts remain available until their recorded expiry.

Conclusion: **G3 REJECT; advance PATCH-GAP-001 to G4.**
