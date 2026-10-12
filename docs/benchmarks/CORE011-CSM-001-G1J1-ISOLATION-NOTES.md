# CORE011-CSM-001 / G1-J1 — separate OS process fixture isolation pilot

Status: **NON-DECISION IMPLEMENTATION / NO_VERDICT_EXPLORATORY**; owning issue [#154](https://github.com/definitely-stable/ChunkShift/issues/154). Parent frozen [G1-J protocol](CORE011-CSM-001-G1J-QUALIFICATION-PROTOCOL.md). PR implementation is an isolated **feasibility smoke**, not a production performance qualification.

## Implemented scope

- `core011-g1j1-build`: **one separate .NET OS process** generates deterministic content in 1-MiB bounded blocks, creates a physical CSM via public `ChunkManifest.CreateAsync`, cross-checks public `VerifyManifestAsync`, hashes source and CSM, deletes `source.bin`, and writes JSON descriptor with physical bytes, manifest SHA, builder PID, run ID, checkout SHA and expected ManifestId/FileDigest/chunks.
- `core011-g1j1-consume`: a **new independent OS process**, with no raw content-generation path. Requires absence of `source.bin`; verifies builder PID differs, checkout SHA, physical CSM SHA, CSM length, expected ManifestId and FileDigest, total bytes, chunk/CBLK counts. Only the small physical CSM is loaded into memory; executes public `VerifyManifestAsync`, not `VerifyAsync(content,manifest)`. Reports 2 warm-batch rounds (non-decision), process PID and GC/CPU/working-set/allocations.
- `core011_csm_g1j1_isolation.py`: builds **five actual source-hashed Core variants** (eager, lazy, hybrid1/2/4) in separate disposable detached Git worktrees, runs targeted Core CSM tests on net8/net10, invokes builder ONCE and five independent consumers, then rejects mismatched SHA, wrong builder/consumer PID, duplicate consumer PID, wrong source/checkout, absent rounds and oracle drift.
- `test_core011_csm_g1j1_isolation.py`: no-.NET synthetic corrupted evidence/source/PID/round/CSM negative tests. These tests use a synthetic non-CSM byte sentinel **only** to unit-test digest gating; real hosted acceptance requires a genuine CSM created by the public API.
- Hosted `core011-csm-g1j1-isolation.yml`: physically creates a 384 MiB source in builder ONLY (in 1 MiB blocks), verifies an actual multi-CBLK CSM, executes 5 fresh consumer processes (workers 4), stores original raw JSON, 5 build SHA records, summary and physical CSM in 90-day GitHub artifact. No self-hosted runner.

## Strict interpretation

The fixture builder creates 384 MiB of **disk content**, not a 384 MiB managed byte array, and exits *before* any consumer starts. Consumers see the CSM file, never the source file; source digest is descriptor metadata only. Managed per-call allocation can now be observed without source memory history contaminating the *consumer* process. Operating-system disk/page cache is still shared, and a single hosted-runner smoke does not provide representative CPU comparison, RSS production bounds, idle trimming or true physical host separation.

Five candidates are present for **correctness and isolation**, not ranked by the 10 raw experimental rounds. Negative/cancelled CI attempts must be archived. The G1-J machine contract remains `stage=preregistration`, `budgetsLocked=false`, `decisionRunsAllowed=false`. Do not use J1 smoke samples as G1-J2 decision evidence.

## What remains for G1-J2/J3

Add real versioned and independently licensed corpus descriptors, physical base/target manifest pairs and isolated public `ChunkPatch.PlanAsync`; threshold-neighbour blocks; CSM reader cancellation/invalid inputs/Dispose stress; more runner/OS/JIT/AOT lanes; cold vs warm separate samples, post-idle vs forced GC, genuinely attributable pool misses, batch duration/noise calibration and accepted memory/CPU budgets. Final decision remains ADOPT / DEFER / REJECT only after a separate protocol freeze and decision samples. Existing #154 writer/hash/decode investigations remain independent.

## Reproduction

The resource-consuming implementation refuses to run outside GitHub-hosted Actions.

```sh
python3 -m unittest -v benchmarks/scripts/test_core011_csm_g1j1_isolation.py
python3 benchmarks/scripts/core011_csm_g1j1_isolation.py \
  --output-dir "$RUNNER_TEMP/g1j1-data" \
  --worktree-root "$RUNNER_TEMP/g1j1-worktrees" \
  --run-id "CORE011-CSM-001/G1J1-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-${GITHUB_SHA:0:8}-linux-x64" \
  --size-mib 384 --workers 4
```

Preserve the actual workflow SHA separately from the GitHub PR branch HEAD. Full raw artifact, independent RunId and ZIP SHA must be archived in an **evidence PR** after successful execution.
