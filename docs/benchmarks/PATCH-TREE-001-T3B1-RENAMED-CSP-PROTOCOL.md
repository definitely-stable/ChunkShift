# PATCH-TREE-001 / T3-B1 — actual changed-rename DLL CSP comparison

Status: PREREGISTERED ORIGINAL CALIBRATION ONLY / NO_ADOPT. Owner #184; release gate #251. T3-B0 found 0 exact reuse among the only 2 added paths on original .NET calibration: [T3-B0 negative evidence](../research/results/PATCH-TREE-001-T3B0-ORIGINAL-CENSUS-EVIDENCE-20261023-001.md).

## Fixed 2-pair population / no candidate search

Only ASP.NET Core win-x64 source-locked original transitions: 10.0.10→10.0.11 (mscordaccore_amd64_amd64_10.0.1026.32716.dll→mscordaccore_amd64_amd64_10.0.1126.37416.dll, new raw 1,356,632 B) and 10.0.11→10.0.12 (mscordaccore_amd64_amd64_10.0.1126.37416.dll→mscordaccore_amd64_amd64_10.0.1226.42308.dll, new raw 1,356,640 B). Prefix: shared/Microsoft.NETCore.App/{version}/. No H5 sketch-ranking, tuning, extra candidates, changing path normalization or sealed holdout.

Read unchanged pairs recovery run #38136336025; full original pairs SHA-256 8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd. The Python selector reuses B0 original source identity checks, checks all source archive SHA and independent full content rehash, then chooses the *only* fixed two old/new rename pairs. The .NET producer independently verifies the bytes and digest of each source and target.

## Real public SDK lanes, all independently applied

1. T0 raw: verify complete target SHA-256; transport physical bytes equal raw new content length; no same-path old-file CSP exists.
2. T0S: self-contained production CSP v1 created from new file CSM and new file bytes; real production apply without base; exact staged SHA-256 verification. Physical CSP bytes reconciled with the existing F0 forensic physical inspector.
3. T3-B1: real old-file CSM plus new CSM; production CSP v1 created using old DLL at different path as BASE; BASE/target CSM ManifestIds fixed to real manifests. Production apply using real base and old file bytes, stage then independently verify complete new file SHA. F0 independently verifies PAYL/PIDX/TCSM/BASE/FOOT byte accounting.
4. Charged diagnostic map: one binary entry with T3B1 NUL magic, LE uint32 count, two (LE uint32+UTF8 path) strings, LE int64 source and target size, 32-byte old/new SHA-256 and 32-byte base CSM ManifestId. Full length is included in warm. This is a research byte ledger, NOT an adopted update-set format.
5. Warm=real base-bound CSP physical size + full map bytes. Cold=warm+actual serialized old CSM bytes. Note base CSM may be generated locally, but cold scenario charges its transfer. No free cross-file locator.
6. For scale context only, four-pair *modelled* T3 warm size = prior original T0 warm 14,758,982 B, including unchanged target-tree metadata 185,436 B, minus original raw added control 2,713,272 B, plus the measured two B1 warm costs. This is **not** new whole-update CSP experiment. The original T0S four-pair total 13,143,287 B is not an independent per-pair denominator; compare fresh measured per-file T0S instead. Timings are one-shot wall diagnostics, not stable CPU/RSS.
7. Delete all raw CSP/CSM/staged new-file bytes after successful verification. Retain only JSON results/provenance/checksums in workflow artifact.

## Frozen stop/go interpretation

For an eligible T3-B1 adoption investigation, **both** original pairs must save at least 10% warm physical bytes compared with min(raw T0, real compressed T0S), **including full map bytes**, and not exceed this same baseline in the conservative cold model including the old CSM bytes. If this is false, REJECT_T3B1_AS_DEFAULT_FOR_ORIGINAL_CALIBRATION. If true, B1_PROMISING_CALIBRATION_ONLY_NO_ADOPT: do not ship; full update-set, other rename populations, locator indexing, apply CPU/RSS, hostile namespace and holdout still require separately frozen protocols. No candidate retuning after original runs.

## Safety and workflow

GitHub-hosted PR CI on Windows/Linux runs synthetic real SDK create/apply and negative tests. The single main-only .NET source-calibration workflow validates first-attempt original run provenance, checksums original materialized sources, runs the *unchanged* candidate selector and real CSP evaluation on GitHub-hosted Ubuntu 24.04. It uploads JSON and SHA-locked provenance and marks failed diagnostics UNVERIFIED_NOT_DECISION. Real data must be independently audited in a separate permanent evidence PR. No production source, CSP/CSM format, API, policy or publisher change. Keep #184 and #251 open.
