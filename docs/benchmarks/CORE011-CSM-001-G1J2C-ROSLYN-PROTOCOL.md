# CORE011-CSM-001 / G1-J2C — Roslyn NuGet binary corpus prequalification

Owner: [#154](https://github.com/definitely-stable/ChunkShift/issues/154). Parent preregistration: [G1-J protocol](CORE011-CSM-001-G1J-QUALIFICATION-PROTOCOL.md), previous [G1-J2B negative evidence](../research/results/CORE011-CSM-001-G1J2B-20261012-001.md).

**Stage: real-release corpus first observation only**. `NO_VERDICT_EXPLORATORY`, `decisionRunsAllowed=false`, `budgetsLocked=false`. No production `src/` changes, CSM/CSP bytes, default reader or public APIs. The experimental question is whether an independently released, sizeable .NET compiler DLL pair naturally contains both reused and missing chunks — **not** which buffer cap is fastest.

## Predeclared external source

Immutable NuGet package ID: `Microsoft.CodeAnalysis.CSharp`. Official package page for [4.13.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/4.13.0) and [4.14.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/4.14.0). Maintained by Microsoft/RoslynTeam and MIT licensed.

| Role | Package version | Exact payload entry |
|---|---|---|
| base | `4.13.0` | `lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll` |
| target | `4.14.0` | *same* |

Transport and package hash endpoints are **only** the official HTTPS NuGet FlatContainer, for example:

`https://api.nuget.org/v3-flatcontainer/microsoft.codeanalysis.csharp/4.13.0/microsoft.codeanalysis.csharp.4.13.0.nupkg`

~~`https://api.nuget.org/v3-flatcontainer/microsoft.codeanalysis.csharp/4.13.0/microsoft.codeanalysis.csharp.4.13.0.nupkg.sha512`~~ — **not available; first CI attempt returned HTTP 404**.

The NuGet FlatContainer does **not** guarantee a downloadable `.nupkg.sha512` sidecar; the first hosted attempt failed HTTP 404 on that endpoint before retrieving any packages. **Protocol amendment before obtaining data:** the script now computes SHA-512 and SHA-256 of downloaded `.nupkg` bytes and labels both as observational and not independently authenticated. It restricts package size to 32 MiB and extracted DLL size to 20 MiB, verifies ZIP integrity and exact case-sensitive DLL payload path, and independently checks PE/COFF, CLR metadata, the assembly name `Microsoft.CodeAnalysis.CSharp` and nonzero module MVID in C#. Original DLL bytes are not post-processed or artificially edited. The source file is deleted before any separate consumer is launched.

**Digest trust boundary:** the first package SHA-512 is computed **locally on the hosted runner** after obtaining NuGet bytes. No official sidecar is used, and hash self-consistency is NOT source authentication, package signature verification, or an independently frozen expected value. Package + DLL SHA-256 and package SHA-512 are preserved for independent future replay. A separate reviewed hash-freeze PR MUST pin them to immutable literals before a future experiment can qualify as a controlled versioned binary corpus. NuGet dependency resolution does not influence the payload selected from the original `.nupkg`.

## Tests and independent process oracle

Reuse existing GitHub-hosted experimental CSM builder / `ChunkManifest.CreateAsync` / `ChunkPatch.PlanAsync` and the same five source-hashed temporary Core reader builds: eager, lazy-only, hybrid cap1/2/4. A separate builder process outputs physical CSM for both genuine Roslyn assemblies and deletes original DLLs. Five new consumer OS processes refuse original source files; each compares exact manifest SHA, FileDigest, ManifestId, chunks, and Plan reused/missing counts. Ten diagnostic rounds are retained, but explicitly **not** treated as independent CPU samples.

The machine evidence gate checks the two version URLs, package ID, payload path, license marker, size bounds, observed locally computed canonical package SHA-512, physical CSM hashes, conservation of target bytes and target chunk counts, five distinct consumer PIDs and unique reader source hashes. A failed request or missing upstream NuGet package must result in **FAIL / no result** rather than silently swapping a version or falling back to arbitrary local content. Mutation tests cover wrong version, bad hash, wrong ZIP payload path, corrupted CSM, false ADOPT, duplicate PID and leaking source files.

## Three strictly separate gates

1. **Semantic / provenance PASS:** both genuine PE assemblies from frozen version IDs and official NuGet endpoints, valid hashes/metadata, correct public CSM Plan, separate builder/consumer PIDs, all 10 complete diagnostics.
2. **Mixed-reuse corpus gate:** `MIXED_REUSE_PASS` only if `ReusedChunks > 0` AND `MissingChunks > 0`, otherwise `NO_GO_NO_MIXED_REUSE` and retain negative outcome.
3. **Large-CBLK corpus gate:** `MULTICBLK_PASS` only if **both** manifests have at least two CBLK sections (thus cross the 4096-chunk boundary); otherwise `NO_GO_SINGLE_CBLK`. A large DLL byte size is not proof of many CDC chunks.

A pair may PASS gate 1 and fail 2 or 3. **Do not select only a favorable part of the pair, rewrite thresholds, or infer a production ADOPT from transport SUCCESS.** Even a pair passing both corpus gates would not constitute general representativeness: additional real versions, release-vs-release source controls, Windows/Linux, binary diversity and independent runner noise calibration are mandatory.

GitHub-hosted workflow: `.github/workflows/core011-csm-g1j2c-roslyn.yml`. The raw GitHub artifact (all per-run JSON, physical CSM, package SHA metadata and failures) should be independently retrieved and SHA-pinned in a separate evidence PR before this stage is archived. Source `.nupkg` and original DLL bytes are deliberately not included in archived artifact; third-party redistribution and bitwise replay require separate explicit verification and licensing review.

This is a corpus/measurement-methodology task under #154, not production optimization or a new default.
