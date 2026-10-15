# CORE011-CSM-004 E3-R2-C1 — independently source-pinned reuse/multiblock Plan calibration

Owner #154, parent #152. Based on E3-R2-C0 evidence PR #358. **No source changes under src/, no production reader, CSM encoding, API or default changes. This is an open unsealed corpus protocol only; there is no ADOPT, candidate AB, numerical resource budget, sealed R3 or holdout.**

## Hypotheses, source classes, limits

The previously independently source-SHA-frozen Microsoft.CodeAnalysis.CSharp 4.13.0/4.14.0 and Newtonsoft.Json 13.0.2/13.0.3 original NuGet pairs were shown to have zero exact stable64k chunk reuse on genuine original release updates. These **cannot** provide a representative mixed-reuse or multi-CBLK reader stress case. We therefore preregister six classes explicitly; only the first two are genuine *different published DLL versions*:

| Frozen case | Origin | Expected Plan class |
|---|---|---|
| roslyn-releases | Original Microsoft.CodeAnalysis.CSharp 4.13.0 → 4.14.0 DLL | 0 reused, 1 CBLK |
| newtonsoft-releases | Original Newtonsoft.Json 13.0.2 → 13.0.3 DLL | 0 reused, 1 CBLK |
| roslyn-identical | Unmodified original Roslyn 4.13.0 → **same** original version | Full reuse, 1 CBLK; no version change |
| roslyn-tail-insert | Original Roslyn 4.13.0 → derived bytes: 4096-byte insert from real 4.14.0 DLL, offset base length − 131072 | Mixed reuse, 1 CBLK; **not original PE** |
| roslyn-tail-delete | Original Roslyn 4.13.0 → derived byte array with final 8192 bytes removed | Mixed reuse, 1 CBLK; **not original PE** |
| roslyn-composite64 | 64 copies of unchanged original Roslyn 4.13.0 concatenated → 63 copies and one original 4.14.0 appended | Mixed reuse and >4096 target chunks, at least 2 CBLKs; **not a single real-world DLL/file** |

Frozen source packages are independently hash-checked against older repo contracts: original Roslyn `benchmarks/experiments/core011-csm-g1j2d-frozen-corpus.v1.json`, independently preregistered Newtonsoft holdout `benchmarks/experiments/core011-csm-g1j2h1-frozen-corpus.v1.json`. These hashes originated in earlier first-party GitHub-hosted NuGet downloads, not publisher cryptographic signatures. Copies/edit recipes are preregistered and never attributed to real published versions. Full SHA256 of every original/rederived input is recorded in private builder `case.json`, never confused with the original DLL hash.

## Process and metric separation

Source-bearing preparation is a dedicated Python subprocess, which calls a dedicated .NET builder OS process for each case. After physical CSM `ChunkManifest.CreateAsync` and public `ChunkPatch.PlanAsync` oracle verification, the builder forcibly deletes all `base.input`/`target.input`. Source preparer terminates **before any target/observer process** starts. The only files preserved are pair/case metadata, exact physical `base.csm`/`target.csm`, logs and measurements. Every case validates SHA256, file size, chunk counts, CBLK counts, provenance/phase, and both chunk and byte conservation. Forbids symlink physical CSM and source leakage, validates OS process PID unique between six builders, six independent target consumers and six separate external observer processes per runner.

Each target performs four public Plan warmups, then loops public `ChunkPatch.PlanAsync` until an **active window ≥4.0s**, checking the frozen independent builder's Plan oracle on every call and collecting wall time, process CPU, allocated bytes and GC counters in the target *only during the active window*. It holds ≥30s natural idle without forced GC. There is no in-process native memory sampler. The third process observes native target PID on Linux `/proc/<pid>/smaps_rollup` Private_Clean+Private_Dirty or on Windows PSAPI EX2 PrivateWorkingSetSize every 100ms. Files marking active-start and active-end allow the observer to report and strictly validate **≥25 native samples during active Plan** and ≥320 total original samples per case, unlike C0's 28–105ms Plan phase. All point-by-point original traces are archived and maxima independently recomputed.

Two different GitHub-hosted runner OS families, Ubuntu 24.04 and Windows Server 2025, both net10 Release x64, one replica each. **This is only technical calibration**, not product/noninferiority qualification. Longer active windows can confound JIT/GC/thermal; no genuine originally released managed PE mixed-reuse or natural real-file multiblock pair is proven by derived controls. 100ms native sampled high-water is not a certified instantaneous upper bound, and external observer still perturbs the OS. No CPU comparisons between OS, release budget or sealed holdout claims are warranted. If any frozen expectation is violated, fail closed and classify unsupported; never tune the case after observing results.

**Exit:** exact physical CSM/provenance recorded; both hosted jobs validate all six cases with active external native sampling; independent original ZIP/CRC/SHA and raw trace audit before archived evidence; E3-R0 remains `decisionRunsAllowed=false`, `budgetsLocked=false`. **Product NOT_QUALIFIED / NO_ADOPT.**
