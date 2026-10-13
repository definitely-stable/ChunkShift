# CORE011-CSM-001 G1-J2E — actual CSM chunk boundaries vs preserved original bytes

Owner: [issue #154](https://github.com/definitely-stable/ChunkShift/issues/154); cross-link [PATCH-DOTNET #221](https://github.com/definitely-stable/ChunkShift/issues/221). Frozen prior results: [G1-J2C](../research/results/CORE011-CSM-001-G1J2C-20261012-001.md), [G1-J2D](../research/results/CORE011-CSM-001-G1J2D-20261012-001.md). **Research-only**; no production algorithm, policy, CSM/CSP format, public API or defaults changed. J1 machine gate `decisionRunsAllowed=false`, `budgetsLocked=false`, `NO_VERDICT_EXPLORATORY` remains intact.

## Motivation

The original `Microsoft.CodeAnalysis.CSharp.dll` 4.13.0→4.14.0 pair gave public `ChunkPatch.PlanAsync` **0 reused / 81 missing** actual target CDC chunks, while separate SHA-checked J2D analysis found **2,069 byte-for-byte exact 4-KiB windows** on a base 256-byte grid, for at least **798,464 bytes** union coverage at potentially changed offsets. This establishes preserved shifted exact bytes, **not** that a whole canonical CDC chunk survives.

The next test is neither a patch-size contest nor a new CDC candidate. It asks: **do any entire existing source/target CDC chunks appear byte-for-byte in the opposite original DLL but fail to align with the opposite file's CDC chunk boundaries?**

## Frozen inputs and acceptance

Use **only** the existing frozen, immutable, independently checked original package+DLL byte hashes in `benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json` (NuGet original `Microsoft.CodeAnalysis.CSharp` **4.13.0** and **4.14.0**, `lib/net8.0/Microsoft.CodeAnalysis.CSharp.dll`). Re-download official package bytes from NuGet, recheck historical package SHA-256/SHA-512 and original DLL SHA-256. The frozen original physical CSM SHA-256 are:

- base: `da64f7ac2bed330730db249bd9b40614474da9b3d9bad648f5d93b18f58fd009` — 74 chunks, one CBLK;
- target: `83ab6375f6c50db29c89cbe48f3f5845dedd6f99e0f79fea471ec1bd152968ed` — 81 chunks, one CBLK.

Reconstruct physical CSMs from source DLLs with public `ChunkManifest.CreateAsync` and `VerifyManifestAsync` through the pre-existing isolated J2 builder; its `ChunkPatch.PlanAsync` must independently report exactly **0 reused / 81 missing**, otherwise FAIL. Both recreated **physical CSM SHA** must exactly equal the historical fixed hashes, not just logical identities. This is a CSM-byte stability guard on the same source, platform and encoder options. No post-build/recompression/byte modification is permitted.

A second production `ChunkScanner.ScanAsync` invocation (existing `chunks --list` benchmark CLI) independently emits `Offset,Length,ChunkId` TSV for original bytes. Independently parse physical CBLK records from both freshly recreated CSM files using frozen CSM v1 little-endian format (CBLK count 1..4096, reserved=0, first index/content offset conserved, 32-byte IDs, uint32 lengths, positive lengths, logical counts and totals). **Every single scanner tuple must match the independent CSM parser tuple**. The public .NET reader/verification already validates CSM CRC and manifest/footer and is the byte integrity authority; Python's independent parser verifies entry layout and conservation, not standalone CRC.

C# builder must delete original DLL source files after manifest construction, and the artifact **must not** include originals or NuGet source package bytes. Keep scanner TSV (74+81 rows), two physical CSMs, public-builder manifest and Plan descriptors, JSON forensic report, output SHA, negative failures and frozen run+checkout identifiers.

## Classification: every original CDC chunk, both directions

For each of the **74 base** and **81 target** chunks, take its *whole exact raw byte span*. Search for the identical full-byte string **anywhere in the opposite original DLL** (not just at source-grid anchors, not relying on J2D's 4KiB sampling). Separately compare **every chunk boundary in the opposite original CSM** with matching length and actual bytes, and require same `ChunkId`. A matching whole chunk at a canonical opposite boundary would contradict the original zero-reuse Plan and MUST fail closed.

A chunk receives **exactly one** classification:

- `EXACT_WHOLE_AT_CDC_BOUNDARY`: its complete original bytes form a canonical chunk of the opposite original CSM with same `ChunkId` — **contradiction to original J2C zero Plan reuse**; treat as FAIL, not performance success.
- `EXACT_WHOLE_ONLY_UNALIGNED`: complete original chunk bytes do occur somewhere in the other file, but **not** as a canonical full chunk of its opposite CSM. This is direct evidence of **boundary mismatch** for that whole source chunk, though it does not by itself distinguish shifted offsets, different chunk length policy, or neighboring content churn.
- `NO_WHOLE_EXACT_BYTES`: no occurrence of the entire chunk appears anywhere in the opposite original file; zero full-byte reuse is then due to **changed bytes within that chunk**. It may still contain shorter exact windows.

The diagnostic output records counts and full source bytes in each category, the first match offset when one exists, and exact 4-KiB matched-window coverage **inside each base chunk**. Target chunks use their own independently scanned target-anchored 4KiB windows; base anchors cannot be passed off as target anchors. Window evidence is a **lower bound**, not a proved whole-chunk match or hypothetical patch savings.

Recheck the unchanged original J2D 4/16/64-KiB base-window counts **2069 / 634 / 275** and non-overlap coverages **798464 / 291328 / 135680**; drift is FAIL and must not become cherry-picked data. Positive or zero exact-whole-unaligned outcomes are both valid findings. No attempts at reversible PE/CLR normalization, no encryption/compression modifications, no new CPU tests, no guessed improvement.

## Reproducibility, separate evidence and next research fork

Hosted-only workflow `.github/workflows/core011-csm-g1j2e-boundary.yml`, isolated `ubuntu-24.04`, .NET10 pinned SDK. Python unit tests cover malformed physical CBLK, invalid source hashes, scan gaps, deliberately shifted entire chunks, partial-only matching windows, mismatched IDs and union interval conservation. Artifact retained for 90 days; independent download, ZIP CRC and nested SHA verification required, with original raw scanner/CSM evidence archived in a separate docs PR.

**Research fork depends on actual observations:**

- **If `EXACT_WHOLE_ONLY_UNALIGNED` is positive**: investigate original CDC boundary shifts, chunk lengths, and how nearest canonical boundaries intersect exact preserved intervals; potential constrained boundary-aware candidate selector study, not automatic adoption.
- **If it is zero but many `NO_WHOLE_EXACT_BYTES` chunks have 4KiB interior matches**: content changes fragment chunks. Study whether reversible CLR metadata/IL/RVA transforms or a different chunking profile preserve more exact whole candidate spans, with separate frozen protocol.
- **If equality at canonical opposite boundary contradicts Plan**: pause new studies and audit hash suite, logical identity, source profiles and reconciliation implementation.

This G1-J2E is **not a representative multi-CBLK CPU workload** (Roslyn remains one CBLK per DLL); it does not authorize cap1/2/4 reader selection. Product verdict remains `NO_VERDICT_EXPLORATORY`.
