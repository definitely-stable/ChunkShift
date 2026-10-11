# PATCH-DOTNET-001 D3-P — frozen physical candidate pilot (calibration only)

Status: **LAB-ONLY PHYSICAL CANDIDATE PROTOCOL / NO EVALUATION DECISION**.
Issue [#221](https://github.com/definitely-stable/ChunkShift/issues/221), prerequisite [real D3-O calibration evidence](../research/results/PATCH-DOTNET-001-D3-O-CALIBRATION-20261011-001.md), [D3 collectors](PATCH-DOTNET-001-D3-COLLECTORS.md) and unchanged D3 A0 prototype [PR #273](https://github.com/definitely-stable/ChunkShift/pull/273).

## Frozen hypotheses and exact source identity

D0 same-population `csp` default physical bytes are **16,029,352 B** for all **1,242** PowerShell 7.5.3→7.5.4 changed paths. D3-O scanner independently reconstructed **123** paired files (85 distinct content-pairs), with 46,059 TypeRef token slots. Its impossible best-case ceiling is **1,838,636 B** (11.4704%); on Windows self-contained, only 11,822 B maximum. This bound is *not* actual D3 patch-size evidence.

No files, candidate parameters, token rules or train/evaluation membership are changed after reading D3-O. Raw fixed D3 sidecar `CDN1` = 44 + 8×targetSlotCount; MaxImageBytes 64 MiB; MaxSlots 262,144. TypeRef symbol identity/hashing and normalized operand set exactly match the A0 implementation in PR #273. Do not adapt identities or hash widths to candidate results.

All 1,242 changed files and their original SHA-256, the original A1-v2 full-byte denominator and CSP H0 fallback cost remain fixed. Only PowerShell **calibration** is permitted here; SDK/Desktop evaluation remains sealed and is never downloaded, transformed, compressed, or measured.

Frozen input records:
- Original `v2-pairs.json` SHA-256 `5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9`.
- D0 baseline `d0-physical.json` SHA-256 `f0e4ff2e47905f22772f1642b105d6732b955070393eded3bf43c5b39ac82cee`.
- D3 verified scanner `d3-scanner.json` SHA-256 `9c2e6beb1f1086e9cd95e37d512950e355334d78e41be11718c415bccdf95b84`.
- Baseline/scanner source: GitHub-hosted [run #38123052019](https://github.com/definitely-stable/ChunkShift/actions/runs/38123052019), SHA `7c49d79b0eef1d7d14449254a52735cd9dbc28e0`, [evidence artifact #11696086817](https://github.com/definitely-stable/ChunkShift/actions/runs/38123052019/artifacts/11696086817), zip SHA `947aa86db7dddf84815a5ab66a2325ab6e025ba4f50fba2779beb88a3717275a`.

## Candidate construction — a real physical CSP patch, not an oracle

For each of the **123** preregistered `VERIFIED_BOTH` rows:
1. Read and SHA-256-verify both original source images. Re-run exactly A0 TypeRef scan; require identical slot counts and successful source restoration for both versions. A disagreement is an **experiment failure**, not a silent skip.
2. Normalize base and target with frozen A0 `PatchDotnet001.Encode`, creating new content-addressed CSM manifests with the same default ChunkShift profile as D0.
3. Create a real physical CSP file with `CspEncoderPolicy.Default` over normalized base/target. Use the same engine as H0, no normalized-stream synthetic byte estimate.
4. Persist a **research-only D3P1 envelope** containing exactly a **24-byte header**: ASCII `D3P1` (4 B), little-endian `uint32 version=1` (4 B), little-endian `uint64 CSP payload length` (8 B), little-endian `uint64 raw CDN1 inverse metadata length` (8 B); then the full physical CSP patch bytes; then the full, *uncompressed* target `CDN1` inverse metadata. No dictionary, external file, compression or invisible sidecar may be exempt from this size. The entire envelope size is `24 + CSP_file_length + 44 + 8×targetSlots`.
5. Independently parse the full research envelope; extract the CSP payload, apply it over the **newly re-normalized base** using existing production CSP apply; verify the normalized target SHA-256; then use a separately coded byte-fixup CDN1 inverse routine to recover original target SHA-256. Failure aborts rather than publishing success.
6. Record per-file candidate envelope bytes, CSP bytes, inverse metadata bytes, envelope SHA-256, target SHA-256 proof, create/apply runtime and fallback choice, and preserve every candidate envelope artifact for audit.

For all other 1,119 files, the candidate lane retains *exact* physical D0 bytes and emits no D3 envelope. Across the **entire 1,242-file population**, choose `min(D0, verified_D3P1)` per candidate. Report both **unconditional D3P1 total** (keeping even disadvantageous D3 candidates) and **min-selected fallback total**. The practical fallback decision is a *per-file size gate*, not measured calibration tuning; it must remain fixed for future independent cohorts. Do not subtract the raw sidecar a second time, or omit the 24-byte envelope header.

The prototype is **not** a ship-ready CSP extension: it requires the A0 type-reference normalization code available to the applying party, and has no accepted external grammar, parser hardening, compatibility or NativeAOT/R2R path. Measure apply CPU/RSS separately before claiming production benefit. The 24-byte D3P1 lab envelope must not be introduced into production CSP v1.

## Decision boundaries

- Report *actual calibration* physical savings (if any), aggregate per-family coverage, and exact independent inverse decoder success, with negative results retained.
- The frozen D3-O 11.4704% ceiling must never be exceeded by selected candidate savings under this raw sidecar design; if exceeded, reject result as an accounting/serialization error.
- The 5% full-corpus gate is useful only for **calibration early stop**, not ADOPT. The previously frozen evaluation gates still require median eligible >=15%, >=25% in an important family, corpus >=5%, bounded apply regression/RSS, independently held-out validation, and multiple genuinely independent managed-app/NuGet product families.
- **Do not unseal** SDK/Desktop evaluation, add unsupported token lanes, tune canonical identity, modify production CSP, public API or releases in this slice.
