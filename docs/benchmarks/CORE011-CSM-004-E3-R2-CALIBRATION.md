# CORE011-CSM-004 / E3-R2-A — baseline-only clean-process memory calibration

State: prospective calibration; PRODUCT_NOT_QUALIFIED; decision runs FORBIDDEN. Owner #154, parent #152. Stacked after E3-R1 Draft PR #344, which is stacked after E3-R0 #343.

## Objective and provenance

E3-R1 established correctness in 5 independent source-hashed Core variants across .NET 8/10 (2,730 PASS). The historical G1-H ArrayPool.Return(clearArray:true) was insufficient for a mandatory full-capacity-zeroing invariant when a returned array is dropped; E3-R1 hardens this by explicitly calling Array.Clear before pool return. Historical CPU results therefore do not qualify the hardened R1 variant.

R2-A measures **only the unchanged eager reader**. Reader Git blob: ff56ccddaa0efc9970fd0b019e20523d6c2933d6. SHA256: a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2. No source reader edits, no candidate A/B, no API/default changes and no invented decision thresholds.

Builder uses the existing G1-J1 source-free fixture generation harness as a trusted *separate executable process*. Its helper builderRunId intentionally starts CORE011-CSM-001/G1J1-E3R2- only for backwards-compatible invocation; it is NOT a G1-J product decision. The owning experiment RunId is CORE011-CSM-004/RUN-.... The builder creates genuine CSM, checks manifest/physical identity, deletes all source content and exits. The fresh consumer sees only physical CSM bytes and a digest-pinned descriptor. It never generates the original 1/384-MiB source, and it checks the builder PID differs from its own PID.

## Preregistered modest R2-A calibration matrix

Two independently allocated GitHub-hosted Ubuntu 24.04 runners, replicas 0 and 1. Each runner builds exactly two synthetic writer-produced CSM: 1-MiB source (single CBLK) and 384-MiB source (5,052 chunks, two CBLKs). Both use BLAKE3 and BIDX. Four separate consumer processes per host:

| Source MiB | Workers | Calls per worker per measured round | Rounds | Natural idle |
| ---: | ---: | ---: | ---: | --- |
| 1 | 1 | 64 | 8 | 0/30 s |
| 1 | 16 | 64 | 8 | 0/30 s |
| 384 | 1 | 8 | 8 | 0/30 s |
| 384 | 16 | 8 | 8 | 0/30/300 s |

Three public VerifyManifestAsync warmups outside measurement. Every measured operation checks valid CSM and exact chunk/block count, ManifestId and FileDigest. Tasks=16 are NOT assumed to execute on 16 hardware cores; actual max overlap and Environment.ProcessorCount are recorded.

Per batch record allocated bytes per operation (whole-process GC.GetTotalAllocatedBytes), CPU time, wall time, Gen2 delta, GC pause time, process working set, peak working set, and process private bytes. Per process record runtime, OS, architecture, server GC, original fixture SHA256, exact checkout SHA, distinct process IDs and samples at fixture-loaded, warm, natural idle 0/30/300 seconds and **separately labelled forced-GC diagnostic**. Last-GC LOH generation size is a snapshot, not LOH allocated bytes. Natural idle never invokes forced GC.

## Critical limitations

- This is a synthetic, Linux x64, BLAKE3/BIDX *eager-only* feasibility calibration. No real-versioned sealed holdout, Windows, NativeAOT, ARM64, SHA256, Plan or full Reader workload is evaluated here.
- Two hosted runner replicas provide at most two independent hardware/process blocks; eight rounds within a process are correlated, NOT eight independent host samples.
- Process.PrivateMemorySize64 on Linux is platform-dependent and may represent virtual address-space size, not private resident pages. Do not freeze a private-RSS budget until separately observing /proc/self/smaps_rollup or a trustworthy equivalent.
- PeakWorkingSet64 is process-lifetime high-water mark, including startup and JIT. Allocation delta includes harness task overhead.
- Numeric performance/memory gates in the original E3-R0 contract remain null. R2 JSON and guard reject candidate measurements, decision flags, source drift and incomplete idle/sample measurements. No ADOPT/REJECT or product qualification is permitted based on R2-A.
- R2-B must independently examine Windows, true private resident memory, more CPU/JIT noise blocks, source-free real-versioned multi-CBLK data, long-lived partial readers and resource budgets before a new prospective decision-ready protocol can be frozen.

## Exit

E3-R2-A GitHub-hosted CI success, per-host 2 external builder PIDs and 4 independent consumer PIDs, 32 measured rounds, verified 300-second natural idle and raw JSON/fixture digests archived. Product disposition: NO_ADOPT / NOT_QUALIFIED pending R2-B and locked R3 holdout.
