# REPO-INGEST-001 — hosted harness smoke qualification

Not performance evidence; do not use smoke timings for decisions.

Accepted #488 exact head f3d28e8783d7ed8b7405a81876dc80555b03e2a4;
main merge 2ef1c745ff02e3b205c7ab82b00971ac66596152. CI 45634883876,
Heavy 45634883261, CodeQL 45634883798, smoke 45634883496 PASS.
Repository 233/233 across Windows, Ubuntu, Linux x64/ARM64.
Seven driver tests per smoke lane; 28 samples per lane, 56 total.

Independent download audit:
- Windows artifact [15030604437](https://github.com/definitely-stable/ChunkShift/actions/runs/45634883496/artifacts/15030604437), ZIP SHA256 ac8cbcad0c5f0fb9b24acd8d6b2ea383ff6888ff8f351c7b4297b08781ff56a2
- Ubuntu artifact [15033075379](https://github.com/definitely-stable/ChunkShift/actions/runs/45634883496/artifacts/15033075379), ZIP SHA256 ed443ecc65302213be20fbfdda1789cc1306a16a27eb957058992cdd2be45e81

ZIP CRC, GitHub digest and internal inventory (59 files per lane) independently
verified; raw sample verdict/commit/RunId/restore digest and physical geometry
match. SDK 10.0.404, dirty=false. Both suites and platforms agree:

| Family | Occurrences | Unique frames | Unique payload bytes | Packs | Active | Closed |
|---|---:|---:|---:|---:|---:|---:|
| Unique | 101 | 101 | 8388608 | 9 | 0 | 0 |
| Recent | 68 | 53 | 4456448 | 5 | 15 | 0 |
| Cross | 65 | 51 | 4718592 | 5 | 7 | 7 |

Layout fingerprints equal across candidates and platforms for each family/suite.
Smoke acceptance only. Full screening uses a distinct locked definition.
