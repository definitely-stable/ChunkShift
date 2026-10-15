# PATCH-TREE-001 / F1 — matched-source physical CSP and candidate attribution

**Owner:** [#184](https://github.com/definitely-stable/ChunkShift/issues/184); Research Freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Protocol stage:** F1 implementation and calibration-only preregistration, **not a sealed decision freeze**.  
**Prior verified inputs:** [T1-B evidence](../research/results/PATCH-TREE-001-T1B-EVIDENCE-20261015-001.md); [F0 physical ledger](PATCH-TREE-001-T1-PHYSICAL-F0.md).  
**Known source run:** original metadata recovery #38136336025, pairs.json SHA-256 `8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.

## Question and precommitted distinctions

Original accepted T0/T1 calibration has T0 warm **14,758,982** vs T1 warm **83,593,822** physical bytes (+68,834,840). Across 1,049 changed files + 2 added paths in four .NET calibration pairs, 981 changed paths with **equal exact ChunkId availability** account for +62,003,683 bytes of net increase. Loss of exact matched chunks cannot be the sole sufficient mechanism, but does not identify the encoder-stage cause.

F1 describes, for **every complete source-locked file** (no selected-only subset), the actual physical CSP in the **same binary/runtime** versus its byte-identical traced re-encode:

1. **Exact reuse availability:** previously measured by T1-B, not reinterpreted as selected reuse.
2. **Absent payload entries:** target chunks served by base ChunkId or a prior target occurrence; there is no encoding trace for those chunks.
3. **Emitted payload entries:** PIDX first-occurrence target index, stored length, actual raw/zstd frame length, exact ordered dictionary ChunkIds and BASE ManifestId.
4. **Candidate trials:** deterministic exhaustive search order and each measured cost/offset, selected candidate, selected dictionary source occurrence (virtual base StartIndex/StartOffset; first source-file path for T1). This is different from a proof of complete segment-by-segment original dictionary provenance.
5. **Physical overhead:** exact PREAMBLE / TCSM / BASE / PAYL / PIDX / FOOT / TRAILER accounting. Complete patch bytes are counted from public output, not predicted from a model.

**Primary descriptive comparison:** partition all changed paths by T1-B equal/lower exact availability, and then decompose matched **T1 − T0 physical bytes**, raw/zstd stored bytes, emitted first-occurrence entry counts, per-file actual selected dictionary entries and candidate search costs. This can reject mechanistic stories but does not isolate causal contributions among co-varying encoder choices. Counterfactual changes in dictionary policy, if needed, require another preregistered experiment.

## F1 implementation semantics and strict gates

**Sourced only from original locked calibration families:** `dotnet-aspnetcore-win-x64` and `dotnet-runtime-linux-arm64`, **two version transitions each** in original frozen ordering. Source files, SHA-256, tree ordering, changed/added/removed/unchanged classification, profiles and pair definitions are unchanged. No holdout materialization in the measurement job. Symlinks, permissions and empty dirs are out of this previously frozen regular-file population.

Use `patch-lab tree-calibration` plus explicit `--f1-dir DIRECTORY --f1-output FILE`. Both flags must be supplied together. With neither flag, the existing original calibration behavior is unchanged. For **each changed path**, write a public default T0 CSP over its true same-path old file and a public default T1 CSP over the whole verified old virtual tree. For **each added path**, T0 is exact full raw bytes and has **no** synthetic T0 CSP/trace; T1 is normal public base-bound CSP. Build and verify target manifests with the unchanged SDK and apply every public CSP for exact reconstruction.

For each public CSP, run `PatchTreePhysicalAttribution.InspectAsync`, then separately encode the **same streams, hash suite, policy, manifest and target** via existing internal `CspPatchBuilder.CreateAsync` with `CspEncoderPolicy.Default`, sequential H0 and `ICspCandidateTraceSink`. The traced CSP **must be exactly SHA-256 identical** to the public default CSP. A same-size trace with different bytes is **FAIL**, not qualifying evidence. The F0 parse/CRC/digest must pass; every traced emitted entry must agree with validated PIDX/PAYL index, stored bytes, selected dictionary references, first target index and candidate ordinal. One failed file fails the entire run. No selected-only/sampled export.

**F1 lab limits (fail closed):** CSP at most 1 GiB, 65,536 entries and 65,536 physical sections. Source locks, run attempts, provenance and output checksum gates are additional restrictions, not changes to production CSP. Raw base/target content or raw dictionary bytes MUST NOT enter the output artifacts. Per-entry JSON includes ChunkIds/offsets/costs only. Persist source file identities (paths may be in source-locked public release), but no secrets and no raw content.

### Output and conservation

A single full `calibration.json` preserves the previously accepted schema `chunkshift.patch-tree-t0-t1-calibration.v1`. `f1-summary.json` uses `chunkshift.patch-tree-t1-physical-f1.v1` and status `CALIBRATION_ONLY_PHYSICAL_TRACE_NOT_DECISION`. Each T0/T1 CSP has an observation sidecar in `f1-observations/` named SHA-256 over UTF-8 `family\0baseVersion\0targetVersion\0path\0lane`, and status `SYNTHETIC_CONTRACT_OR_CALIBRATION_ONLY_NOT_DECISION`. Sidecar records public/traced CSP SHA-256 and physical totals, complete emitted candidate details and F0 physical components.

Expected exact populations:
- 4 calibration pairs, **1,049 changed / 2 added**.
- 1,051 full target path rows, **2,102** lane summary rows (T0 + T1).
- **2,100 actual CSP observations** (T0 for 1,049 changed, T1 for 1,051 changed/added).
- T0 added paths: 2 raw-transport rows, **no** CSP observations.
- Every sidecar's physical totals sum to exact public CSP bytes, with exact per-entry/raw/zstd/PAYL/PIDX conservation.
- `sum(T0 lane payload/raw bytes) + target-tree metadata = T0 warm` and the same for T1; the identical metadata charge cancels in each pair.

The independent Python verifier checks full row order, frozen pair identities, source SHA, duplicate/missing/unexpected sidecar rejection, every sidecar SHA-256, trace-parity assertions, physical byte sums and final warm totals. **Its replay validates evidence structure, not zstd frame bytes or selection independently**; the .NET evaluator holds that semantic assertion, while the original CSP input does not survive into the downloaded archive. This limitation must stay visible in the final evidence.

### Hosted run and one-success-only gate

A new workflow `.github/workflows/patch-tree-001-t1-physical-f1-calibration.yml` runs **only on GitHub-hosted ubuntu-24.04**, via `workflow_dispatch` from `main`, with `run-date=YYYYMMDD`, `pairs-run-id=38136336025` (or independently verified original eligible run), and the exact `source-sha`. Require owner `MrFr3di`, `GITHUB_RUN_ATTEMPT=1`, main SHA equality, zero prior successful/in-flight F1 workflow dispatches, source provenance, .NET from global.json and `materialize_patch_corpus.py --lock --calibration-only`. The owner-facing issue #184 command is:

```text
/tree-physical-f1 YYYYMMDD 38136336025
```

No workflow dispatch is permitted until the code/protocol/independent verifier/tests are merged on current `main`, with all final-head checks green. No rerun/retry to search better outcomes. The workflow archives original-pairs.json, layouts.json, calibration.json, f1-summary.json, all F1 sidecars, provenance.json and relative `SHA256SUMS`/ `F1-SHA256SUMS`. Failure artifacts are explicitly marked `UNVERIFIED_NOT_DECISION`.

Historical CSP SHA-256 in original accepted run #38142160679 may differ across builds and must **not** be enforced as same-build identity. The only mandatory physical parity is the *same-run* public default versus traced H0 CSP SHA, matched on the same data.

## Acceptance and next research decision

F1 **technical pass** requires complete source population, all verified public create+apply outputs, no CSP parity mismatch, 2,100 valid sidecars and both independently recomputed whole-update sums. The research conclusion may be only an observational decomposition of where bytes, dictionary decisions and frame costs differ on the pinned corpus; F1 alone cannot claim a causal fix or extrapolate to holdout.

Until the verified original hosted F1 artifact is separately downloaded, independently audited and integrated through evidence-only PR, state remains `F1_NOT_RUN / T1_NO_GO_CALIBRATION / NO_ADOPT`. **Do not** merge a new candidate selection, alter CSP v1 or public API, open sealed holdout, publish update-set semantics, implement T2/T3 or close #184/#251. The next possible step after F1 is a separately frozen matched counterfactual isolating dictionary-window/offset policies on the 981 equal-exact paths.
