# PATCH-ENC-005 — G2 exact whole-base oracle evidence

EvidenceId: `PATCH-ENC-005/G2-EVIDENCE-20261003-001`

Status: **G2 MISS — H6-O guard required**

Owning issue: #181  
Parent: #7  
Protocol: `docs/benchmarks/PATCH-ENC-005-PROTOCOL.md` at `96fd9b296d6998cac397e61041f22df51e6dd43c`

## Provenance

- workflow: `PATCH-ENC-005 G2 oracle`
- GitHub run: `37150436770`
- run number / attempt: `2 / 1`
- source commit: `489d3f2fe2582cc6251ee2fa87cd23bad1793b01`
- platform: `linux-x64` / `ubuntu-24.04`
- locked sample SHA: `54061f4efe0969af0d772ddbc6d026fdf58bd4f2776514a9fb098753d062001d`
- artifact id: `11284200988`
- artifact expiry: `2027-01-01T20:08:06Z`

The earlier run `37148261047` is not evidence: it failed in the first workflow preflight before checkout because the literal date placeholder was supplied. No corpus or oracle code ran in that attempt.

## Frozen §4.3 result

| metric | result | gate |
| --- | ---: | --- |
| H0 sample cost | 974,223 B | — |
| exact G2 sample cost | 974,049 B | <= 925,511.85 B required |
| G2/H0 | 0.9998213961 | <= 0.95 required |
| saved | 174 B | — |
| improved sampled entries | 11 / 256 | descriptive |
| pair passes | 0 / 4 | >= 2 required |
| outside-radius improved winners | 11 / 11 | descriptive |

Per pair:

| family | base → target | H0 | G2 | ratio | saved | <= 0.97 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| dotnet-aspnetcore-win-x64 | 10.0.10 → 10.0.11 | 344,395 | 344,348 | 0.999864 | 47 B | no |
| dotnet-aspnetcore-win-x64 | 10.0.11 → 10.0.12 | 201,266 | 201,262 | 0.999980 | 4 B | no |
| dotnet-runtime-linux-arm64 | 10.0.10 → 10.0.11 | 261,382 | 261,259 | 0.999529 | 123 B | no |
| dotnet-runtime-linux-arm64 | 10.0.11 → 10.0.12 | 167,180 | 167,180 | 1.000000 | 0 B | no |

The main G2 gate therefore **MISSES** both required conditions.

The exact whole-base oracle confirms that farther-than-±256 KiB starts occasionally improve an entry, but the entire 256-entry sample gains only 174 bytes (~0.01786%). This is far below the frozen 5% aggregate headroom threshold.

## Protocol consequence

Full Phase B is **not authorized** by G2.

- H5-F: forbidden.
- H6-P: forbidden.
- H8: forbidden.
- the only authorized continuation is the already-frozen fixed-evaluation false-negative guard `H6-O12-SF3-S128`.

The guard may open the full grid only if every §4.3 guard condition passes on linux-x64 fixed evaluation:

- physical patch bytes <= 0.97 × H0;
- five-round paired create wall median <= 1.50 × H0 and at least 4/5 rounds <= 1.50;
- create peak <= 160 MiB over idle;
- exact reconstruction and deterministic bytes.

If the guard misses, H5-F/H6-P/H8 become **STOPPED — sampled G2 miss confirmed by the pre-frozen H6-O guard**. D15/default remains unchanged.

## Durable evidence

`docs/research/results/data/PATCH-ENC-005-G2-EVIDENCE-20261003-001/` contains:

- `oracle.json` — all 256 locked target rows with H0 and exhaustive whole-base winning costs/metadata;
- `verdict.json` — frozen §4.3 decision;
- `summary.md` — workflow-produced human summary;
- `execution.json` — RunId/GitHub/platform/source identity;
- `documents.json` — raw document size/SHA-256 manifest produced in the run;
- `artifacts.json` — artifact id/name/expiry/ZIP SHA-256;
- `recompute.py` — independent stdlib-only recomputation of totals, pair gates and next action.

No payload or dictionary bytes are retained.
