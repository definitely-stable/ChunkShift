# PATCH-EXEC-001 — multi-file create calibration foundation (experimental)

Owner: [#225](https://github.com/definitely-stable/ChunkShift/issues/225); related [#184](https://github.com/definitely-stable/ChunkShift/issues/184), [#251](https://github.com/definitely-stable/ChunkShift/issues/251), PATCH-ENC-004. Status: **LAB ONLY / NOT A DECISION / NOT A PRODUCT ADOPTION**.

## Why this slice exists

PATCH-ENC-004 adopted H2-W2 inside one CSP build, but a batch of independent files may benefit more from distributing the same cores between separate creates. `patch-lab run --workers` already schedules different changed files via `Parallel.ForEachAsync`; a second scheduler is unnecessary. However `--execution` previously required `--workers 1`, because the process CPU and GC allocation counters were reported per file. This prevented a controlled nested-concurrency comparison.

This slice explicitly enables the existing outer worker loop **only for a new research lane**:

```text
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- \
  patch-lab run --corpus /corpus --lane csp --output /results/batch-h2w2-f2.json \
  --execution h2-w2 --workers 2 --batch-create --no-apply --work /corpus/work
```

Compare a matrix: `(outer=1, execution=h0)`, `(outer=1, execution=h2-w2)`, `(outer=2, execution=h0)`, `(outer=2, execution=h2-w2)`, then outer 4/8 only if the host has enough effective CPU and memory. The old production/default execution remains unchanged. No cache, task identity, BuildPolicy, public scheduler or update-set protocol is introduced.

## Measurement contract

* New `chunkshift.patch-batch-create.v1` schema; existing `chunkshift.patch-lab.v1` and frozen PATCH-ENC-004 measurements are not reinterpreted.
* The **elapsed** time is batch-wide end-to-end wall time for SHA-256 input validation, cached-or-created CSM manifests, plan, actual CSP creation, CSP readback and patch SHA-256. It is **not** isolated encoder time. All runs use the same already-populated manifest cache for warm comparisons; run a cold-cache study separately.
* `batchCreate.processCpuSeconds` and `processAllocatedBytes` are process-wide deltas measured over the same batch interval. Per-file `createMetrics` must be null. Individual `createSeconds` are observed wall times and must **not** be summed into batch CPU or throughput.
* `processPeakWorkingSetBytes` is the OS-reported **process-lifetime** high-water at completion. It is not an isolated batch peak, a hard memory cap, or a per-worker attribution. For memory acceptance, use dedicated fresh child processes and independent OS-level sampling.
* Runs use `--no-apply`; every patch is still structurally decoded and hashed. Compare each patch's SHA-256 against a separately **apply-verified** H0 reference for the same corpus and compiler/backend. For performance evidence, always run an independent full apply/target SHA verification on the selected candidates.
* A batch failure aborts the run without writing a success result document; temporary patch paths use per-file directories. An existing result output path is rejected, rather than overwritten. Use a fresh unique filename per run; a failed run writes no successful result. No cancellation or atomic whole-tree publication guarantee is claimed here.
* Keep I/O amplification, raw bytes, CPU quota/cgroup, GC mode, process architecture, .NET SDK, base-storage type, warm/cold cache and host identity alongside raw results; never pool heterogeneous hosts as if identical.

## Qualification before a product decision

First run synthetic 2+ file equivalence, invalid flag cases, regression tests and Linux/Windows hosted build. A future separately preregistered **unsealed** real-tree pilot must compare warm and cold modes in repeated, alternating lane order on fixed hosts, report median/dispersion and make all CSPs exactly apply. Rank by whole-batch wall time, not sum of per-file timers; account for memory as approximately `outerWorkers × perCreateBound(innerWorkers)` plus manifests/I/O buffers. On scarce cores, oversubscription may make H2-W2 × outer-4 worse than H0 × outer-4.

No performance gain is claimed by this code slice. The >=25% improvement and memory/correctness gates of #225 remain untested. Full task-result cache work and any production update-set API remain blocked by the representation/BuildPolicy research gates and #251.
