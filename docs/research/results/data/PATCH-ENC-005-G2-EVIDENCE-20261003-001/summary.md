# PATCH-ENC-005 G2 verdict

- run: `PATCH-ENC-005/RUN-20261003-002-489d3f2fe2582cc6251ee2fa87cd23bad1793b01-linux-x64`
- source: `489d3f2fe2582cc6251ee2fa87cd23bad1793b01`
- sample: `54061f4efe0969af0d772ddbc6d026fdf58bd4f2776514a9fb098753d062001d`
- status: **MISS**
- next action: **H6_O_GUARD_ONLY**
- H0 sample cost: 974,223
- G2 sample cost: 974,049
- ratio: 0.999821
- saved bytes: 174
- improved rows: 11 / 256
- pair passes: 0 / 4 (need >= 2)

| family | base | target | H0 | G2 | ratio | saved | pass <=0.97 |
| --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| dotnet-aspnetcore-win-x64 | 10.0.10 | 10.0.11 | 344,395 | 344,348 | 0.999864 | 47 | no |
| dotnet-aspnetcore-win-x64 | 10.0.11 | 10.0.12 | 201,266 | 201,262 | 0.999980 | 4 | no |
| dotnet-runtime-linux-arm64 | 10.0.10 | 10.0.11 | 261,382 | 261,259 | 0.999529 | 123 | no |
| dotnet-runtime-linux-arm64 | 10.0.11 | 10.0.12 | 167,180 | 167,180 | 1.000000 | 0 | no |

The main G2 gate passes only when the overall ratio is <=0.95 and at least two of four pair ratios are <=0.97.
