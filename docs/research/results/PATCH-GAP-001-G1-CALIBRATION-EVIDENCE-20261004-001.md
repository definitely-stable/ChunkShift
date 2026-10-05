# PATCH-GAP-001 — G1 calibration evidence

EvidenceId: `PATCH-GAP-001/G1-CALIBRATION-EVIDENCE-20261004-001`

Status: **CALIBRATION ONLY — no lane reaches the frozen 15% size threshold**

Owning issue: #183  
Parent: #7  
Research-freeze gate: #251  
Protocol: `docs/benchmarks/PATCH-GAP-001-PROTOCOL.md` at `5372678ae8451a71cc95eb24f30855cbbd7e0633`

## Provenance

- workflow: `PATCH-GAP-001 G1 calibration`
- GitHub run: `37231286406`
- run number / attempt: `1 / 1`
- source commit: `f5f1b62be06f0fd2e62f36d07b6d2df56f745903`
- platform: `linux-x64` / GitHub-hosted `ubuntu-24.04`
- dataset role: calibration
- corpus digest: `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`
- requested nested lane: `G1-B32-R2048` (produces B1/B4/B8/B32 together)
- Actions artifact: `11316573406`
- artifact ZIP SHA-256: `1380b35ad6f2d5adf6e78710088ca281622dad96564bafb23fbdfc951ac792bf`
- artifact expiry: `2027-01-02T20:13:42Z`
- zstd backend: `ZstdSharp 0.8.8+2cd0c019693bc786a5fe5c3be94e107b24e7267e`
- zstd backend SHA-256: `b285bb9f59928becb8e048bf2a75e872f6555f8773d7b72386a9f77f447f425c`

## Frozen H0 preflight

The workflow regenerated the complete frozen H0 before producing G1 bytes:

- files: 1,893
- calibration bytes: 11,860,274
- evaluation bytes: 26,363,364
- total bytes: 38,223,638
- patch-set SHA-256: `4a1f5c272da5cb12ba0d07c5d6b379701d7da154bdc1e92886bd27a8fc3781c6`
- frozen anchor: **PASS**

## Calibration byte result

| lane | physical bytes | saved vs H0 | reduction vs H0 | frozen 15% threshold |
| --- | ---: | ---: | ---: | --- |
| `G1-B1-R64` | 11,773,832 | 86,442 | 0.728836% | **FAIL** |
| `G1-B4-R256` | 11,769,971 | 90,303 | 0.761391% | **FAIL** |
| `G1-B8-R512` | 11,769,971 | 90,303 | 0.761391% | **FAIL** |
| `G1-B32-R2048` | 11,769,971 | 90,303 | 0.761391% | **FAIL** |

H0 calibration size is `11,860,274` bytes.

The best frozen lane is B4/R256 at `0.761391%` reduction. B8/R512 and B32/R2048 produce exactly the same aggregate bytes, so the observed calibration benefit saturates at B4 under the frozen H0-start/maximal-prefix counterfactual.

## Base-read cost

- H0 base bytes read: `3,113,635,436`
- G1 factor base bytes read: `31,121,707,793`
- factor/H0 amplification: `9.995295992x`

This is diagnostic calibration evidence, not an RFC gate by itself.

## Concentration

Only `10` of `1,049` calibration file rows improve at B32.

The top three improved files account for `93.27%` of all B32 savings; the top five account for `99.37%`. The largest gains are concentrated in CLR/native runtime binaries rather than broadly distributed over the corpus.

| saved | family | path | versions |
| ---: | --- | --- | --- |
| 34,710 B | `dotnet-aspnetcore-win-x64` | `shared/Microsoft.NETCore.App/{version}/coreclr.dll` | `10.0.10 -> 10.0.11` |
| 27,983 B | `dotnet-runtime-linux-arm64` | `shared/Microsoft.NETCore.App/{version}/libclrjit.so` | `10.0.10 -> 10.0.11` |
| 21,532 B | `dotnet-aspnetcore-win-x64` | `shared/Microsoft.NETCore.App/{version}/clrjit.dll` | `10.0.10 -> 10.0.11` |
| 4,069 B | `dotnet-runtime-linux-arm64` | `shared/Microsoft.NETCore.App/{version}/libcoreclr.so` | `10.0.10 -> 10.0.11` |
| 1,436 B | `dotnet-aspnetcore-win-x64` | `shared/Microsoft.NETCore.App/{version}/System.Private.CoreLib.dll` | `10.0.10 -> 10.0.11` |
| 378 B | `dotnet-aspnetcore-win-x64` | `shared/Microsoft.NETCore.App/{version}/coreclr.dll` | `10.0.11 -> 10.0.12` |
| 173 B | `dotnet-runtime-linux-arm64` | `shared/Microsoft.NETCore.App/{version}/libclrjit.so` | `10.0.11 -> 10.0.12` |
| 12 B | `dotnet-runtime-linux-arm64` | `shared/Microsoft.NETCore.App/{version}/libmscordbi.so` | `10.0.10 -> 10.0.11` |
| 8 B | `dotnet-aspnetcore-win-x64` | `shared/Microsoft.NETCore.App/{version}/msquic.dll` | `10.0.10 -> 10.0.11` |
| 2 B | `dotnet-runtime-linux-arm64` | `shared/Microsoft.NETCore.App/{version}/System.Private.CoreLib.dll` | `10.0.10 -> 10.0.11` |

## Protocol consequence

Calibration is explicitly **not** a second RFC size gate and must not select or remove a predeclared G1 lane. Therefore this evidence does **not** mark G1 REJECT.

Because no frozen G1 lane reaches 15% on calibration:

- no early three-platform G1 runtime/apply-RSS characterization is required;
- no lane, threshold, dictionary budget, candidate start, prefix rule or codec setting may be changed;
- all four frozen G1 lanes still receive exactly one fixed-evaluation byte run;
- if an evaluation lane unexpectedly reaches >=15%, run the required unchanged three-platform non-size checks afterward before any RFC qualification.

Next action: **RUN_FROZEN_EVALUATION_ALL_G1_LANES**.

## Durable evidence

`docs/research/results/data/PATCH-GAP-001-G1-CALIBRATION-EVIDENCE-20261004-001/` contains:

- `calibration.json` — compact calibration outcome and next action;
- `compact.json` — family aggregates, all 10 improved file rows, frozen H0/lane totals and the deterministic detail-manifest digest;
- `execution.json` — source/run/environment identity;
- `documents.json` — SHA-256/size manifest for workflow-produced root documents plus a deterministic manifest hash over all 1,049 detail records;
- `artifacts.json` — Actions artifact identity, expiry and ZIP digest;
- `summary.md` and `h0-summary.md` — exact workflow-produced summaries;
- `recompute.py` — stdlib-only recomputation of aggregate lane bytes, 15% gates, improved-file count and base-read amplification.

The raw detail JSON set is about 36 MiB unpacked and is not duplicated into Git. Every raw detail document remains bound by the retained detail-manifest digest and the Actions artifact.

Conclusion: **strong negative calibration signal, but formal G1 verdict remains pending the mandatory fixed evaluation run.**
