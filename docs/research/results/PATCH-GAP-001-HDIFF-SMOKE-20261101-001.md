# PATCH-GAP-001 — HDiffPatch v5.1.3 real-binary dual-lane smoke

**Date:** 2026-11-01. **EvidenceId:** `PATCH-GAP-001/HDIFF-SMOKE-20261101-001`. Owner #183, gate #251.

**Status:** `REAL_PINNED_BINARY_DUAL_LANE_SMOKE_PASS / NO_ORIGINAL_CORPUS_REFERENCE / NO_RFC_DECISION`.

## Original GitHub-hosted execution

- [Run #41303301679](https://github.com/definitely-stable/ChunkShift/actions/runs/41303301679), job ID `124838529313`, GitHub-hosted `ubuntu-24.04`, **SUCCESS**, commit `26d64efff0c2019a657a115d3364707f07a4a8a6`.
- Raw JSON artifact: `patch-gap-hdiff-pinned-smoke`, GitHub artifact ID `13081794658`, SHA-256 `dab2b939a94c7a12a5ce8017f86c8ef61eaccd9607afd6eb6eeda14cd6d663c5`, 972-byte ZIP. Raw artifacts are retention-limited.
- Downloaded exact official [HDiffPatch v5.1.3 Linux64 release](https://github.com/sisong/HDiffPatch/releases/tag/v5.1.3) ZIP, verified SHA-256 `628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558`.
- Independently extracted and SHA-bound **both** `hdiffz` and `hpatchz` executable bytes to their exact ZIP members. Substitution or duplicate entries are rejected.
- **8/8** Python negative/contract tests PASS. Both actual native CLI commands `-m-4` and `-s-64` with the same frozen `-SD -d -f -p-1 -c-zstd-21-24` arguments executed. Each used `hpatchz -s-8m -f` and the complete reconstructed target matched target SHA-256.
- Deterministic synthetic fixture: 262,144 old bytes plus a 50-byte overwrite and a 6-byte append in target. Both modes happened to emit a 64-byte patch with SHA-256 `e088d828114747b7dc2fa5a4fb70790df9f907eba5c6543a7bdd1a1abe02764f`. These fixture numbers are **never** extrapolated to the real corpus and **never** used as an RFC size gate.

## Scientific scope

This tests whether the exact frozen HDiffPatch binaries and flags are executable and reconstruct correctly on the hosted Linux lane. It does **not** measure the frozen 1,049-file calibration or 844-file evaluation, nor Linux memory limits, cache sensitivity, real patch size, Zucchini or CSP vNext benefit. The script's original corpus mode requires frozen `pairs.json` and checks both base and target file sizes and SHA-256. An independent H0 same-population comparison and per-file source-locked durable decision evidence are required before closing #183.

**Next:** implement and execute separately predeclared original calibration and evaluation jobs with a valid GitHub-hosted source/corpus lock and no duplicate decision runs; independently verify reference totals. For Zucchini, first verify eligible configured GitHub-hosted >=100GiB-free runner or formally review feasibility deferral. #183/#251 remain OPEN; CSP v1/default/API unchanged.
