# PATCH-DOTNET-001 D3-O: real PowerShell calibration optimistic ceiling (2026-10-11)

EvidenceId: `PATCH-DOTNET-001/D3-O-CALIBRATION-20261011-001`
Verdict: **D0_PHYSICAL_PASS / D3_SCANNER_ROUNDTRIP_PASS / D3_O_CALIBRATION_NONVETO / NO_MEASURED_D3_PATCH_SAVINGS**.
Owner: [issue #221](https://github.com/definitely-stable/ChunkShift/issues/221).
Protocol: [D3-O preregistration](../../../benchmarks/PATCH-DOTNET-001-D3-UPPER-BOUND-PROTOCOL.md); [collectors](../../../benchmarks/PATCH-DOTNET-001-D3-COLLECTORS.md). Upstream immutable data: [A1-v2 calibration](PATCH-DOTNET-001-A1V2-CALIBRATION-20261011-001.md).

## Provenance and independent replay

- [GitHub-hosted workflow #38123052019](https://github.com/definitely-stable/ChunkShift/actions/runs/38123052019), **SUCCESS** on source `7c49d79b0eef1d7d14449254a52735cd9dbc28e0`, runs production H0 CSP patch creation/apply and an entirely separate A0 TypeRef research scanner+byte-only inverse decoder on the six SHA-256-verified PowerShell 7.5.3/7.5.4 release binaries.
- [Artifact #11696086817](https://github.com/definitely-stable/ChunkShift/actions/runs/38123052019/artifacts/11696086817), full ZIP SHA-256 `947aa86db7dddf84815a5ab66a2325ab6e025ba4f50fba2779beb88a3717275a` (406,078 compressed bytes). **Downloaded and independently verified** whole ZIP SHA-256 and the embedded `SHA256SUMS` over all four evidence documents. `v2-pairs.json` was independently extracted from A1-v2 [artifact #11694182924](https://github.com/definitely-stable/ChunkShift/actions/runs/38119011975/artifacts/11694182924) and its SHA-256 `5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9` rechecked.
- Individual evidence SHA-256: `d0-physical.json` = `f0e4ff2e47905f22772f1642b105d6732b955070393eded3bf43c5b39ac82cee`; `d3-scanner.json` = `9c2e6beb1f1086e9cd95e37d512950e355334d78e41be11718c415bccdf95b84`; `d3-oracle-input.json` = `b761915ea8f1d7ed9a4983885a1c0b3e1c0a160eb218ded2532a41ac9fa04e4a`; `d3-ceiling.json` = `08fe2c3cdc356aaeb3cf893cb0e292540284b5b59d9c5ba111088a3473f35939`.
- Independently joined every `(family,base,target,path)` in the immutable A1-v2 changed file set against D0, D3 and oracle input (all **1,242/1,242**); cross-checked original SHA-256, sizes, decoder SHA evidence, per-file D0 patch sizes, strict `csp` baseline and `seq` target apply, complete totals and the oracle equation.
- Baseline environment: GitHub-hosted Ubuntu 24.04.5, x64, .NET 10.0.12, 4 logical CPUs (AMD EPYC 7763). Encoder policy is default CSP: zstd level 19, 4 dictionary chunks, 8 candidates, radius 262,144 B, prefix dictionary load. `applyRepeats=1`, `applyCheck=seq`, D0 create/apply for every changed file. Whole lab run elapsed ~151.6 s. No publication or production contract changed.

## Empirical H0 sizes versus **impossibly optimistic** D3 ceiling

| Correlated PowerShell deployment | Changed files | Verified D3 scanner rows | Exact physical D0 B | Ideal D3 lower-cost B | Ideal max saving B | Ideal max saving, *not measured* |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| linux-x64 framework-dependent | 263 | 57 | 1,922,166 | 1,011,989 | 910,177 | 47.3516% |
| win-x64 framework-dependent | 282 | 60 | 2,045,059 | 1,128,422 | 916,637 | 44.8220% |
| win-x64 self-contained | 697 | 6 | 12,062,127 | 12,050,305 | 11,822 | 0.0980% |
| **Full PowerShell calibration** | **1,242** | **123** | **16,029,352** | **14,190,716** | **1,838,636** | **11.4704%** |

**Interpretation:** D3 scanner confirms exact independent source roundtrip for only **123/960** A1-v2 structurally IL-only potential rows, representing **85 distinct (base,target) SHA-256 pairs**, not 123 independent observations. It rejects/falls back on **837** structurally possible rows because both sides contained no supported assembly-scoped InlineType/InlineTok TypeRef slots; **282** rows were structurally ineligible. Verified 123 target images total 43,766,808 bytes. Those rows contain **46,059 target TypeRef slots**; the uncompressed raw CDN1 target-sidecar bytes sum to **373,884 bytes**. A sidecar equal to or larger than original D0 is allowed to fall back; 8 scanner-verified rows have no positive ideal cost opportunity.

The optimistic oracle sets **the normalized physical CSP patch payload and all other framing to zero** and charges `min(D0, 44+8×targetSlots)` for verified rows. It does *not* encode a normalized CSP patch or include the cost of base normalization at apply, future decoder distribution, or other metadata. Therefore the `11.4704%` number is **not** achieved savings, an estimate, nor a guarantee. It is solely a ceiling for the frozen **raw uncompressed CDN1** transform. A compressed or revised sidecar requires a new separately registered model.

## Engineering decision

For this calibration scope the 5%-of-full-H0 physical baseline requirement represents `ceil(0.05 × 16,029,352) = 801,468` bytes. The impossible upper saving is 1,838,636 bytes, leaving at most **1,037,168 bytes** for *all* real candidate costs above the theoretical floor if a future physical D3 patch were to meet that particular 5% gate. This is **not sufficient** to assert real savings; it only means the fixed-sidecar variant is *not falsified by this necessary condition*.

**Strong segmentation:** the self-contained family accounts for **12,062,127/16,029,352 (~75.25%)** of the H0 physical baseline, but its absolute maximum saving under the fixed design is **11,822 bytes**, i.e. only 0.0980% of that family's H0. Stop treating this current TypeRef-only normalization as a credible self-contained improvement absent a materially different mechanism. Framework-dependent segments retain theoretical opportunity but still require empirical normalized CSP create/apply, exact inverse target verification, and physical sidecar/framing cost.

## Next gate and forbidden inferences

**Next permitted slice:** preregister a **calibration-only D3 physical candidate pilot**, unchanged H0 comparison population (all 1,242 changed files), with the copied A0 normalizer and exact raw CDN1 metadata. Construct a real CSP patch over normalized base/target for each `VERIFIED_BOTH` row, independently apply it and verify decoded original target SHA-256. Charge CSP physical file length + raw CDN1 target metadata + *every additional candidate framing/base-normalization burden*; fallback must remain D0. Performance measurements and AOT/R2R remain separate. The physical candidate must not use SDK/Desktop evaluation for tuning or measurements.

For **general adoption**, first freeze a full decision protocol with independent managed-app/NuGet, R2R-composite and provenance-labelled NativeAOT strata; do not call three correlated PowerShell deployments independent sample families. No new persisted format, production CSP v1, public API, default encoder or release change is authorized by the upper-bound result.
