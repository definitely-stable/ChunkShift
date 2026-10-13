# CORE011-CSM-001 / G1-J2H0 — immutable independent managed NuGet holdout prefreeze

**Stage:** PREREGISTERED CORPUS-IDENTITY ONLY / `NO_VERDICT_EXPLORATORY`. Owner [#154](https://github.com/definitely-stable/ChunkShift/issues/154); related [#221](https://github.com/definitely-stable/ChunkShift/issues/221). Follows J2E (no whole existing Roslyn chunks), J2F (lab FastCDC16k exactly 8 reused / 149568 B), J2G (with-base production stable64k CSP 1260098 B vs standalone 2100946 B, exact apply). Do not perform a physical patch or chunk-reuse measurement during H0.

## Preregistered independently released product family

Exactly one **independent** managed NuGet product family, selected before seeing any assembly data or outcome:

- NuGet package ID: `Newtonsoft.Json`; package author/product distinct from Roslyn.
- Base version: `13.0.2`; target version: `13.0.3`.
- Exact internal path: `lib/net6.0/Newtonsoft.Json.dll` in both immutable original nupkg archives.
- Transport: `https://api.nuget.org/v3-flatcontainer/newtonsoft.json/{version}/newtonsoft.json.{version}.nupkg`.
- Framework: .NET 6.0 managed assembly. Product-family independence is not independence from all .NET ecosystem shared build toolchains; this is only one holdout pair.
- Predeclared acceptance: both authentic public endpoints must respond; nupkg ZIP CRC good; exact case-sensitive payload path exists once; file starts valid PE/COFF with mapped CLR directory; base and target full SHA256 differ, all size limits hold. Any failure is **NO_GO**, do not swap versions or assets.

**This is not a random population draw.** The NuGet package ID / releases / exact assembly path are frozen *before* download/observation in this PR; H0 only establishes observed raw-byte digests and provenance, not independent publisher authentication or binary signature validation.

## H0 transport/digest-only controls

Run only on GitHub-hosted Ubuntu 24.04, exact checkout SHA, pinned `global.json` .NET 10 (later evaluation), Python standard library. Require HTTPS from precisely the two constructed official NuGet FlatContainer URLs; fail for non-2xx, missing/oversize packages (> 8 MiB), duplicate names, compression bombs (> 8 MiB extracted DLL), missing exact path, malformed ZIP, invalid ZIP CRC, absent PE signature or absent CLR descriptor. Compute and preserve: nupkg byte SHA-256 and SHA-512, DLL original byte SHA-256 and SHA-512, exact file sizes, version/path, observed PE/CLR traits, checkout SHA and RunId. Base and target DLL SHA-256 **MUST differ**, otherwise refuse as uninformative for changed-file patching.

Only archive `prefreeze.json` containing checksums and structural metadata, and non-sensitive `failure.json` on failure. Neither nupkg nor DLL bytes may be archived or committed. SHA-512 values in H0 are self-computed from downloaded bytes, not independently authenticated NuGet signatures. Future H1 must hard-code these observed values in a separately reviewed machine-readable frozen contract and re-fetch independently before any chunking, patch-create or patch-apply data are generated.

Do not inspect byte similarities, FastCDC chunks, Plan reuse, CSP physical bytes, normalized IL/metadata fields, patch timings or any performance data in H0. No threshold tuning. H0 merely proves the exact independent pair *exists* and can be reproduced bitwise.

## H1 (separate, after H0 digest freeze)

The next implementation requires a second reviewed protocol and fixed machine contract BEFORE running H1:

1. Re-fetch original packages and fail if package SHA256/SHA512, DLL SHA256/SHA512, internal path or content length deviate from the H0 freeze.
2. Use unchanged public stable FastCDC64k to create BLAKE3+BIDX CSM and verify byte-for-byte complete content/Plan; register/verify both physical CSM SHA.
3. Measure actual stable64k **with-base** and **self-contained** CSP physical bytes, payload, public Create/Apply correctness with exact target SHA, and descriptive wall/CPU/RSS. No claims based on missing logical bytes alone.
4. Reuse J2F's **same five predeclared** kernel profiles with streaming/scalar reference equality and byte-verified exact reused ChunkId counts. No extra profile candidates chosen after H0, and no unregistered profile is serialized as public CSM. Physical FastCDC16k patches are **not** claimed until a distinct lab-only encoding and complete reconstruction experiment exists.
5. For this holdout, a zero-reuse result remains valid negative evidence. One product version pair cannot qualify a universal .NET conclusion or a general change to the default.

`CORE011-CSM-001/G1J` remains `decisionRunsAllowed=false`, `budgetsLocked=false`, `NO_VERDICT_EXPLORATORY`. No source, wire, API, production policy or public ProfileId changes. No self-hosted runners.
