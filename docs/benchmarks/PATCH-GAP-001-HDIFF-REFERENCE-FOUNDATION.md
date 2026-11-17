# PATCH-GAP-001 — pinned HDiffPatch reference foundation

Date: 2026-11-01. Experiment #183; parent freeze #251. **FOUNDATION / POSITIVE_BINARY_SMOKE_ONLY / NO_CORPUS_REFERENCE_EVIDENCE / NO_RFC_DECISION**.

## Why this slice is next

G1, G2, G3, G4-BCJ and G5 have preserved original results and the independent F0 ledger is in `main`. Genuine Chromium Zucchini reference still requires a configured, actually available GitHub-hosted larger Linux runner with >=100 GiB free disk. We must not conflate missing Zucchini infrastructure with proof that Zucchini does not help. The separate reference comparison **HDiffPatch v5.1.3** can be qualified on an ordinary GitHub-hosted Linux x64 runner without changing the CSP representation.

This slice builds a source-locked **whole-file reference harness**, not a CSP encoder or one-factor counterfactual, and verifies that both frozen modes really execute on the SHA-pinned upstream binary. The full 1,893-file reference remains unexecuted until a separately authorized GitHub-hosted calibration/evaluation workflow and an independently audited durable per-file dataset exist.

## Frozen contract

Frozen source: `3b9dca715ca492873bf2c49e22e5d5b7d2a78620` (HDiffPatch v5.1.3).
Release asset: `hdiffpatch_v5.1.3_bin_linux64.zip`; exact SHA-256 `628963bf2ee9108a97260fa5eef44acd9ec94369b76090a957c9182b3abbb558`.
Protocol: `PATCH-GAP-001-PROTOCOL.md` at `5372678ae8451a71cc95eb24f30855cbbd7e0633`, §10.

Two independent, **mandatory** lanes:

| lane | create | apply |
|---|---|---|
| memory | `hdiffz -m-4 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH` | `hpatchz -s-8m -f OLD PATCH RECONSTRUCTED` |
| stream | `hdiffz -s-64 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH` | same fixed apply cache |

All commands are independent processes. Neither lane gets a free metadata cost or an automatic tool fallback. Exit != 0, missing outputs, invalid archive digest, wrong reconstructed length/hash or mismatched frozen corpus are fatal. Patch SHA-256 and physical bytes are recorded separately.

The fixed original corpus SHA is `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`. Calibrated file count **1,049** (ASP.NET Windows x64 + .NET Runtime Linux ARM64); evaluation file count **844** (Node Linux/Windows x64, tzdata, ChunkShift-source). Pair membership cannot depend on patch size, tool result or parser. Successful full reference must record each file, target/hash, two patch sizes/hash, exact reconstruction, create/apply wall, binary identity and parent run, then independently recompute split totals after final evaluation.

## Implemented and exact limitations

- `benchmarks/scripts/patch_gap_hdiff_reference.py`: SHA-pinned original archive validation; required dual lanes; executable identity hashes; exact target SHA-256 on reconstructed output; frozen split identities and count checks for corpus mode; structured per-file results (no zero-byte substitute on failure).
- `test_patch_gap_hdiff_reference.py`: simulated error/tamper controls. These tests **alone** cannot demonstrate HDiffPatch compression.
- `.github/workflows/patch-gap-001-hdiff-reference.yml`: default GitHub-hosted Linux x64, downloads official v5.1.3 release, independently verifies SHA-256 and runs **real** memory and stream mode on a deterministic edited binary. Only this step establishes real executable/CLI/round-trip feasibility. This is **not** full-corpus evidence, nor a valid 15% RFC gate.

Reference outcomes never enter the one-factor CSP size gate directly. Preserve CSP v1, D15, defaults and API unchanged. The calibration/evaluation source input must be materialized under the frozen lock, with an explicit RunId, source SHA, binary SHA, raw per-file evidence, independent H0 attribution and durable checked data. The pre-existing fixed evaluation partition is not statistically untouched; do not describe it as a new sealed generalization holdout.

## Next permitted step

After this PR's final-head hosted real-positive checks pass: prepare a **separate** frozen calibration execution on the 1,049 original files, including pre/post materialization SHA lock, per-file OOM/timeout/resource evidence, and independent reference H0 comparison. The 844-file evaluation is not run before its calibration/source provenance is validated. A tool-run failure is not a zero-byte patch nor a negative size verdict. Zucchini needs a distinct explicit resource-feasibility decision and must not be silently marked REJECT.
