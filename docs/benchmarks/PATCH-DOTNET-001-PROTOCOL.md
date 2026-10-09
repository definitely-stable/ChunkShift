# PATCH-DOTNET-001 — preregistration and Phase A foundation

Status: **RESEARCH FOUNDATION / NOT A FROZEN DECISION PROTOCOL**. Issue [#221](https://github.com/definitely-stable/ChunkShift/issues/221); parent #7; generic G4 owner #183. No decision-bearing measurements are authorized until the holdout artifact locks and review have been merged independently of the measurement code.

## Question and dependency

Does .NET-specific reference normalization provide meaningful **physical CSP patch-byte** savings against an unchanged generic baseline? G4-BCJ was REJECT (frozen evaluation 251,840 / 26,363,364 = 0.955265%, apply base-read amplification 1.987989825×). Its evaluation split has no managed .NET cohort; this is **not** an independent .NET negative result. Generic G4 completion, whatever verdict, is the entry prerequisite, not positive G4 evidence. The Zucchini reference is an attribution/control stream, not a .NET normalization result.

## Boundary and staged lanes

- **D0**: existing production CSP H0 per-file, with frozen encoder policy and physical bytes.
- **D1**: completed generic G4 BCJ result, *only* on the identical comparison population; where none exists report N/A and obtain a new same-population generic control, never transplant Node-only scores.
- **D2**: metadata table rows, coded indices and heap references, in an independently reviewed module. Preserve table sorting, index-width transitions, heap boundaries and every original byte for inverse. Not implemented in Phase A.
- **D3**: IL operand references. Phase A implements strict PE/CLR classification, scans **top-level assembly-scoped TypeRef tokens used by InlineType/InlineTok in IL-only methods** and tests an explicit-slot, reversible token-reference codec. It does not yet construct CSP patches. Other token kinds, nested/module references, strings and EH are unchanged; unparseable method/unknown opcodes fail closed.
- **D4**: RVA and relocation targets, separately enabled and costed. Do not normalize arbitrary integer constants or move section bytes. Not implemented.
- **D5**: ReadyToRun header/section and fixup-aware pilot. Separate ABI, architecture and runtime major-version cohort; no raw native BCJ reuse and no decoding if format version unsupported. Not implemented.

NativeAOT may look like an ordinary native PE, ELF or Mach-O: classify it as a **provenance-labelled control**, never positively infer NativeAOT from the absence of the CLR header.

## Phase A safety contract

Input cap = 64 MiB/file; transform slot cap = 262,144. Recognition: PEReader and MetadataReader must parse the CLR header and metadata; ReadyToRun classification requires the native header signature `RTR\0` at its mapped RVA. Mixed-mode, R2R, metadata-unavailable and malformed PE do not enter Phase A D3. Opcode widths come from platform OpCodes metadata; reject truncated switch, invalid RVA, out-of-bounds/overlapping fixups and ambiguous identities. Only original binary's four-byte IL token operand locations are changed in the canonical stream.

Symbol identity (research-only) = length-prefixed assembly simple name, namespace and TypeRef name; duplicate symbols mapping to different token values reject. Canonical value = first LE u32 of SHA-256 of the domain-separated identity; any observed collision rejects. This is **not** a persisted token grammar; named-key identity can have false semantic matches, but exact inverse does not rely on semantic equivalence.

Inverse metadata `CDN1`: 4-byte magic, LE u32 original length, LE u32 fixup count, 32-byte original SHA-256 and ascending pairs of LE u32 file offsets / LE u32 original values. Physical metadata bytes **exactly 44 + 8 × N**, including header, hash, and offsets. Independent decode does not require PE parsing; it writes declared original values at bounded offsets and verifies the final SHA-256. Payload bytes, patch container bytes, dictionary/reference headers, fallback indicators and framing must additionally be charged by a future D3 patch runner. No improvement claim from comparing only the normalized streams.

Malformations: fail closed to D0 before applying a candidate; never publish an unverified output. No modifications to production encoder, CSP v1, identity/hash/profile contracts, shipped packages or public APIs.

## Fresh population and holdout lock — MUST precede parameter tuning

Before reading any transform-size results:
1. Freeze a versioned, checksummed source asset lock (official release manifests + exact immutable asset SHA-256) and `pairs.json` membership with normalized relative paths. Record source URLs, family, base/target versions, RID, toolchain, architecture, artifact hashes and materialization script SHA. Fail on missing assets.
2. Pre-assign entire **family × version-pair groups** rather than individual files to calibration, fixed evaluation and negative/control subsets; no product/version leakage. Include .NET Runtime, ASP.NET Runtime, SDK, NuGet packages, framework-dependent and self-contained apps, ReadyToRun (including composite where available), plus NativeAOT-labelled negative controls. Windows/Linux and x64/ARM64 are distinct strata; R2R/ILONLY/mixed/unsupported are classified separately before any size results.
3. Freeze parser-version identifier, mutually exclusive eligible classes, file/byte counts, data provenance, SHA of sorted inventory, exact result schema, and D0/D1 same-population anchors before examining canonicalized patch bytes.
4. Freeze lane configuration, CPU/RSS/wall measurement settings, apply read amplification and exact same-target decoder contract; run only on GitHub-hosted runners. Do not use the generic G4 Node holdout as a .NET holdout.
5. Obtain protocol review and merge this measurement contract **before** a decision run. Phase A unit-test assembly and hand-authored vectors are *not* calibration or holdout and must not be used to set thresholds.

## Evaluation and stopping rules (predeclared)

Record for every row: eligibility/fallback reason, SHA-256 of exact target, normalized/candidate size, full physical patch bytes **including all transform map/header/index/fallback/padding**, create/apply CPU/wall/peak RSS, base bytes read, final apply verification, family/version role. D0 is unchanged for every ineligible row; sidecar cost is always nonnegative.

Before building a general table or R2R parser, compute a **same-population upper-bound oracle** for potential benefit under zero-cost ideal normalization, and stop if even that bound cannot meet the below gates. The oracle must be explicitly defined and frozen with its own reproducible implementation; no optimistic estimate is decision evidence.

Open a persisted-format RFC only if **all**:
- median eligible holdout physical patch saving >= 15% versus *same-population* completed generic/control baseline;
- >= 25% saving in one predeclared important .NET family;
- >= 5% reduction in total **corpus-weighted physical bytes** versus best applicable generic baseline (or explicitly restrict product scope with separate acceptance);
- apply wall/CPU regression <= 15% or documented product-level benefit; bounded RSS and read amplification;
- byte-exact target recovery and SHA-256 verification in **every** eligible case, independent decoder and malformed/fallback tests pass.

Otherwise mark DEFER/REJECT, retain evidence, and leave CSP unchanged. Negative result does not justify tuning thresholds after holdout.

## Primary sources

- ECMA-335 Partition II metadata tables and Partition III CIL.
- Microsoft System.Reflection.Metadata / PortableExecutable: https://learn.microsoft.com/dotnet/api/system.reflection.portableexecutable.pereader
- ReadyToRun format: https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/readytorun-format.md
- Runtime versioned section/fixup grammar: https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/readytorun.h
- Chromium Zucchini: https://chromium.googlesource.com/chromium/src/+/HEAD/components/zucchini/
- Predecessor evidence: [PATCH-GAP-001 G4](../research/results/PATCH-GAP-001-G4-EVIDENCE-20261008-001.md)

The lab implementation in `benchmarks/ChunkShift.Benchmarks/PatchLab/PatchDotnet/` is intentionally an **experimental partial D3 mechanism**, not the end-to-end D0–D5 comparison or an adoption verdict.
