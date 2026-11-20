# CLI140-B2C — preregistered resource and bounded-output gate

Status: **PREREGISTERED, NOT ACCEPTED**. This document is committed before the
first GitHub-hosted measurement. The exact numeric gates below are
hypotheses/acceptance thresholds, not measured results.

## Target and non-goals

Run the **unmodified product CLI assembly** as real child processes on
`ubuntu-24.04` and `windows-2025` using the repository-pinned .NET 10 SDK.
No CLI environment fault hooks, changes to Core/Patching SDK, persisted
CSM/CSP formats, encoder selection, public APIs or package publication.

Existing Heavy validation checks **library-level** `4 GiB > 128 MiB` cgroup
streaming; it is not evidence of the CLI's process RSS or bounded JSON report.
B2C adds process-level evidence. Measurements are not performance benchmarks
or stable cross-platform absolute baselines.

## Inputs and limits — frozen before the first measurement

| Item | Gate |
|---|---|
| Baseline source | 8 MiB, generated using fixed xorshift32, streamed in 64 KiB buffers |
| Large manifest source | **384 MiB** (48× baseline), streamed to disk (not allocated as one array) |
| Self-contained CSP target | **64 MiB** (8× baseline), streamed to disk |
| Managed GC heap budget per CLI child | `DOTNET_GCHeapHardLimit=10000000` (hex **256 MiB**) |
| Max observed process peak resident set | **640 MiB** per CLI invocation (including runtime/native allocations) |
| Large vs small `manifest verify` peak RSS growth | **≤128 MiB** after subtracting the low-input peak on the same OS |
| Large vs small `manifest inspect` peak RSS growth | **≤128 MiB** on the same OS |
| Maximum stdout JSON per successful operation | **4,096 UTF-8 bytes**, exactly one newline-terminated JSON object |
| stderr on successful operation | **0 bytes** |
| Child process timeout | **240 seconds** per command |
| Maximum captured output | **4,096 bytes** each for stdout and stderr; overflow fails closed |
| Checkpoint | Must produce a machine-readable JSON evidence artifact per OS with input sizes, output sizes, chunk counts, process peak RSS and exact SHA-256 comparison |

No test may silently shrink the declared inputs, skip an OS, permit unknown
exit status, turn a timeout into a success, or change these numeric gates
after observing a failure. If the gate fails, record actual evidence and
either repair code or start a **separate, explicitly reviewed recalibration**
with a new rationale and new preregistration; do not silently waive failure.

## Operations and observations

1. `manifest create --json --sha256` for baseline and large sources; assert
   `manifest.contentBytes` matches file lengths; `manifest.chunks` for large
   source exceeds 256 and is at least eight times baseline chunk count.
2. `manifest inspect --json` for each CSM; validate `verification.valid`,
   exact manifest ID and chunk count. Compare peak RSS across sizes.
3. `manifest verify --content ... --json` for each source/CSM pair;
   compare peak RSS across sizes; large input is bigger than GC heap cap.
4. `patch plan --base-manifest large --target-manifest large --json` for
   high-chunk-count read-only operation: all chunks reused, zero missing bytes,
   bounded stdout and resident memory. This is not proof of constant-memory
   arbitrary plan matching against distinct target manifests.
5. `manifest create` for 64 MiB target, `patch create` self-contained CSP,
   `patch apply` to a new destination. Assert `apply.applied`, output byte
   count, published artifact existence, and byte-exact target equality via
   streaming SHA-256 (not `ReadAllBytes`).
6. At each stage, sample `Process.WorkingSet64` during execution and capture
   `Process.PeakWorkingSet64` for high-water evidence; do not confuse
   a sampled value with GC committed heap or cgroup peak. If the OS does not
   expose a positive peak value, the gate fails instead of reporting zero.
7. Emit a compact JSON report with a per-command `peakResidentBytes`, exact
   `stdoutBytes`, `exitCode`, command/operation, counts and limits.
   Upload it as an Actions artifact; CI failure must retain runner logs.

## Applicability and limitations

* The GC budget **only limits managed allocations**, not unmanaged/native
  memory. The RSS gate independently observes whole-process resident memory.
  See [Microsoft GC configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)
  and [Process.PeakWorkingSet64](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.peakworkingset64?view=net-10.0).
* Existing B1R pure tests classify `PatchApplyFailure.ResourceLimit` as exit
  `1`. The product SDK default of **1,048,576** CSP entries cannot be
  faithfully forced using a short invalid CSP without violating the format.
  B2C does **not** claim an end-to-end CSP-over-default-limit test or
  arbitrarily inject a result flag into production.
* The workload samples small, large, same-manifest plan, and 64 MiB self-
  contained CSP flows; it does not prove memory bounds for every valid
  feature of CSM/CSP, unlimited patch histories, or concurrent hostile input.
* This is a **B2C** focused gate, not B2D path/lock completeness, CLI140-C
  release acceptance or NuGet publication. `IsPackable=false` remains.

## Acceptance

- Both OS resource probes pass **all** frozen assertions and upload evidence.
- Full Linux/Windows solution CI and Heavy Linux x64/ARM64 / cross-arch
  validation remain green on the exact reviewed PR head.
- No newly added runtime dependencies or public SDK API/format changes.
- PR merge only after all required jobs succeed, with exact-head/tree
  verification; issue #140 remains OPEN for B2D/C.
