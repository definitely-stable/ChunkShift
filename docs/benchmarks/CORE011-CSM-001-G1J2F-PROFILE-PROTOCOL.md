# CORE011-CSM-001 — G1-J2F frozen original Roslyn profile sensitivity (lab only)

Owner: [issue #154](https://github.com/definitely-stable/ChunkShift/issues/154); related [#221](https://github.com/definitely-stable/ChunkShift/issues/221). Inputs: original NuGet `Microsoft.CodeAnalysis.CSharp.dll` versions **4.13.0** → **4.14.0**, exact package and DLL SHA-256/SHA-512 frozen in `benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json` and physically confirmed in G1-J2E (both original physical CSM SHA frozen). This is a strict `NO_VERDICT_EXPLORATORY` laboratory experiment, `decisionRunsAllowed=false`, budgets not locked, no production changes.

## Critical distinction: current production does NOT register the proposed extra profiles

`ChunkScanConfiguration` currently registers only `fastcdc.gear.chunkshift.v1.64k` (minimum/target/maximum **16/64/256 KiB**). The proposed `fastcdc.v1.patch64k`, `fastcdc.v1.general256k` and `fixed.v1.*` strings in the research roadmap must not be represented as currently supported public profile IDs. Attempting to select them with `ChunkScanner`/`ChunkManifest` would throw `NotSupportedException`. This experiment therefore uses existing **internal** `ChunkingKernel`, the Core scalar `ChunkingReference` oracle and raw original bytes exclusively inside `ChunkShift.Benchmarks`. The only real CSM manifests persisted are for the existing stable64k registered profile.

Predeclared five lanes (order fixed before any new run):

| Lane label | Algorithm | min/target/max (KiB) | Production status |
|---|---|---|---|
| `stable-fastcdc64k` | current production FastCDC gear | 16 / 64 / 256 | **registered** |
| `lab-fastcdc16k` | existing Core FastCDC gear | 4 / 16 / 64 | research-only |
| `lab-fastcdc256k` | existing Core FastCDC gear | 64 / 256 / 1024 | research-only |
| `lab-fixed16k` | existing internal fixed chunk kernel | 16 / 16 / 16 | research-only |
| `lab-fixed64k` | existing internal fixed chunk kernel | 64 / 64 / 64 | research-only |

All five use the same `chunkshift.blake3-256.v1` ChunkId hashing suite. The extra FastCDC16k gives a smaller-size contrast relevant to J2E's lost whole large chunks. No further lanes may be added after observing the results; a different candidate requires a separate protocol amendment + fresh run.

## Measurement, correctness and failure rules

A new GitHub-hosted runner independently downloads original NuGet files and requires byte-for-byte SHA256/SHA512 agreement with the earlier frozen values, without silently replacing a version, file or package. The benchmark harness then scans **each source DLL** through the exact internal production `ChunkingKernel.ScanAsync` using each predeclared lab profile. An independent Core scalar `ChunkingReference.Chunk` must match **every tuple of offset, length and hash**. Existing public `ChunkScanner.ScanAsync` must additionally match the stable64k lane. Only the stable64k lane may use public `ChunkManifest.CreateAsync` + `VerifyManifestAsync` + `ChunkPatch.PlanAsync`; its physical CSM SHA256 must match accepted J2C and J2E physical hashes, and Plan must report **0 reused / 81 missing**.

For each profile independently, count target canonical `ChunkId`s also present in the base source. Verify each hash hit by comparing *all original bytes* for the matched source and target slices, failing closed on any hash-length or byte inconsistency. Report exact `ReusedChunks`, `ReusedBytes`, `MissingChunks`, `MissingBytes`, two chunk counts and fraction of target bytes reused. Exact conservation is required.

For each hypothetical lab lane, report only the **CBLK entry/envelope arithmetic**: `36*chunk_count + 44*ceil(chunk_count/4096)` bytes for chunk hashes, lengths, CBLK section header/prefix and CRC. This **excludes CORE, CEND, FOOT, trailer, BIDX** and any CSP payload, compression, source fetching or patch overhead. It is NOT a complete physical CSM size; unsupported profiles are never serialized to CSM as if officially registered.

Do **not** infer any end-to-end patch-size, transfer bytes, reconstruction proof, CPU/GC, storage pack index, or production adoption from this chunk-count/reuse comparison. Binary package files and DLL inputs are removed from the output artifact in a `finally` block even on failure; scanner TSV, the two genuine baseline CSMs, lab control and summary JSON plus their immutable source SHA references are archived. Errors create `failure.json`, never a replacement corpus.

## Acceptance and interpretation

The study succeeds as methodology if all five pairs of profiles are present, original package/DLL and stable CSM SHA lock and public zero reuse oracle pass, 10 scalar-vs-streaming/source-coverage comparisons pass, and all profile hash matches pass independent byte equality. **Zero reuse in every lane is an admissible negative finding** and must not prompt post-hoc selection of extra variants within J2F. If some lane has positive reuse, it is a bounded research signal only, requiring multi-version holdout and actual patch reconstruction/size/CPU evaluation with separately frozen protocol before adoption.

All resourceful tests and measurements run on GitHub-hosted ubuntu-24.04, using pinned .NET 10 SDK and no self-hosted runners. The owner issue remains `RUNNING`, `NO_VERDICT_EXPLORATORY`. Full original evidence and independent artifact ZIP SHA are archived via separate PR after hosted run and review. No production `src/` files, CSP/CSM v1 wire bytes, public API, shipped defaults or decision budgets are changed.
