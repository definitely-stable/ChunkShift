# PATCH-EXEC-001 / G2-A — unsealed real .NET update-set execution calibration

Issue: [#225](https://github.com/definitely-stable/ChunkShift/issues/225). Research freeze: [#251](https://github.com/definitely-stable/ChunkShift/issues/251). Dependencies: G1 [#354](https://github.com/definitely-stable/ChunkShift/pull/354), frozen PATCH-TREE-001 original pairs recovery [#38136336025](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025).

**Status: pre-registered exploratory implementation. No decision runs occurred before this protocol is merged. G2-A is not a final PATCH-EXEC-001 adoption experiment.**

## Scope, version and separation

Only the original `dotnet-aspnetcore-win-x64` and `dotnet-runtime-linux-arm64` **calibration** families are eligible. Frozen *full* `pairs.json` SHA-256 is exactly:

`8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd`.

Independently computed, frozen selected-pairs SHA-256 for G2-A training: `a7f1e607820f6a637600953ddb634b63cfd17e7edc55a67bae86f74f54b0c6e2`; reserved G2-B validation: `79077cbd2e1e6dc2335f98eacd7adb5f1375d497eba66298cff8cbb4d7e0c827`. These digests are assertions in the selector and evaluator, not values learned from a timing run.

The original complete file is recovered from successful first-attempt [PATCH-TREE-001 pairs recovery #38136336025](https://github.com/definitely-stable/ChunkShift/actions/runs/38136336025), artifact `patch-tree-001-original-pairs`; GitHub Actions recovery run must match original workflow, repository, branch main, SUCCESS and `run_attempt=1`. Existing `materialize_patch_corpus.py --calibration-only --pairs-reference --lock` physically materializes **only** the two .NET calibration families and independently matches reconstructed pair records to the original full reference. Nothing may materialize node/tzdata/source holdout bytes.

G2-A training: version `10.0.10 -> 10.0.11` for each of the two families, 8 changed files per family, **16 total**. `10.0.11 -> 10.0.12` is a reserved **unsealed calibration validation** set for a future separately frozen G2-B; it is not measured or tuned by G2-A.

Each family contributes 3 changed paths with **both** base and target sizes in [64 KiB, 256 KiB), 3 in [256 KiB, 1 MiB), 2 in [1 MiB, 4 MiB]. The deterministic per-bin priority is ascending SHA-256 of UTF-8 `family + NUL + base + NUL + target + NUL + path`, then path. This rule is pre-registered, independent of measured patch time/size. `prepare_patch_exec_001_g2.py` validates every selected physical source length and SHA-256, rejects unsafe paths, creates only verified hardlinks on the **same runner filesystem**, and writes separately SHA-locked subset `pairs.json` and `selection.json`. Refuse stale output, a changed frozen source, missing pair, duplicate path and insufficient population. Do not tune bins/quotas after seeing times; any such change needs new protocol version and ExperimentId slice.

## Allocation and modes

Actual PatchLab production CSP writer/reader, unchanged policy/default and no new scheduler. Six fixed configurations per mode, as frozen by G1: F1/H0, F1/H2-W2, F2/H0, F2/H2-W2, F4/H0, F4/H2-W2. Inner worker choice is explicitly lab-only; production already defaults H2-W2 on CPUs with >=2 logical processors. Each case runs in its **own fresh dotnet process**, process-level CPU/allocations and lifetime high-water are recorded.

- **warm-csm**: first run a separately apply-verified H0/F1 oracle to populate the CSM manifest cache. Case processes share the unchanged CSM files. The operating-system filesystem page cache is *not* controlled.
- **cold-csm**: the same H0 oracle is verified first, then the CSM cache directory alone is deleted **before each case**. This does not clear file system caches or other storage caches.
- Five rounds per mode. Round order follows the deterministic G1 forward/reverse and rotation schedule. A within-mode same-host ratio is allowed; **cross-mode ratios across independent runners are not**.

The batch includes manifest/content digesting, patch planning, CSP create, patch readback and digest — not just the encoder. Report total wall seconds, process CPU seconds, managed allocation deltas and process lifetime high-water. `PeakWorkingSet64` is not incremental per-create isolated RSS. Disk bytes/seeks, quota/affinity, idle-subtracted per-create peak RSS and the adaptive scheduler are explicitly not measured in G2-A. This is why it cannot yield production ADOPT.

## Correctness, validation and failure

Each hosted job rebuilds from its exact commit and validates the original pair and all selected file hashes before constructing patches. One H0/F1 oracle *applies all 16 patches and verifies each reconstructed target digest*. Every one of 5 x 6 = 30 independently created case batches must match that apply-verified H0 across all 16 CSP SHA-256 digests, policy, target chunks, payload counts and corpus membership using independent `compare_patch_batch_create.py`. Any error suppresses the final success `summary.json` and artifact. The workflow prints a SHA-256 manifest for all final result JSONs. A manual run is accepted only from GitHub-hosted ubuntu-24.04 x64, main, `workflow_dispatch`, first attempt and the exact frozen data; reruns and PR branch runs are exploratory/invalid.

Artifacts keep **all** raw samples and provenance; unarchived or failed runs are not evidence. Never claim rerun/stale output as new success. A flow with no source acquisition must fail rather than fall back to synthetic data.

## Decision semantics and next step

A G2-A `PASS` means **real fixed-lane calibration mechanics and five repeated within-mode measurements passed**, not algorithm adoption. Report per-lane median, min/max, CPU and lifetime peak memory. Report the best *observed* fixed configuration within this calibration **descriptively**. This best-in-training selection is not a pre-registered adaptive policy and cannot be compared to itself as a 25% improvement.

For a real adoption decision, freeze G2-B separately **before** touching its reserved calibration validation pair. Pre-register a deterministic cheap-cost hardware/work-class adaptation rule learned only from G2-A; compare on G2-B against the **best fixed safe baseline chosen on training** with >=25% end-to-end wall gain, CPU amplification budget, isolated measured per-create peak RSS, cancellation/failure semantics, and unambiguous repeated-build test scope where relevant. Block any production/surface change on #251 and dependencies #183/#184/#220/#221/#222/#224. Do not invent a task-result cache until tasks/BuildPolicy stabilize.
