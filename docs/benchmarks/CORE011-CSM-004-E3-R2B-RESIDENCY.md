# CORE011-CSM-004 / E3-R2-B1 — private-resident memory and parked-reader calibration

**Experiment:** CORE011-CSM-004, owner #154. E3-R0 protocol #343 → E3-R1 correctness #344 → E3-R2-A baseline #345 + audited evidence #346 → **E3-R2-B1**. Scope is prospective research, **NOT a production decision**.

The actual original CSM reader is unchanged: Git blob ff56ccddaa0efc9970fd0b019e20523d6c2933d6; SHA256 a62772638c715005cb8a1a6ca58222349755a7e21a341ff80655a825910cebf2. Readers are all eager, no lazy/hybrid source edits, no CSM/CSP/public API/default/writer changes.

## Motivation and registered metrics

The E3-R2-A two-Ubuntu-runner experiment established ~166 KiB allocation per eager VerifyManifestAsync across 11 and 5052 chunks, but Linux Process.PrivateMemorySize64 is not a valid private-resident measure, and 16 worker tasks overlapped only 4–5 actual reader operations. E3-R2-B1 therefore measures **actual private resident pages at strict 16 simultaneously live, partly enumerated readers**. Its separate no-reader control is essential to identify baseline CLR/GC/JIT/physical CSM metadata residency. Two independent OS hosts (Linux x64 and Windows x64) are registered; the experiment is not an unblinded candidate comparison.

Memory accounting:
- Linux, /proc/self/smaps_rollup (Linux kernel documented): sum of Private_Clean and Private_Dirty in KiB, multiplied by 1024, for physically resident private pages. Also independently record Rss and Pss and raw pair, enforce sum ≤ RSS. The source is current-process smaps, not Process.PrivateMemorySize64 virtual size.
- Windows, GetProcessMemoryInfo with PSAPI PROCESS_MEMORY_COUNTERS_EX2: PrivateWorkingSetSize (physically resident private pages) and WorkingSetSize (total resident pages). Record PrivateUsage as separate private committed virtual bytes, **never substitute it** for PrivateWorkingSetSize. Fail closed if unsupported; Windows EX2 requires a sufficiently recent operating system (Windows 10/11 and corresponding server patches). See https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2.
- Always also record process private bytes as *untrusted virtual/commit diagnostic*, CLR managed heap, last-GC LOH size (not LOH allocations), Gen2 count, total pause time, runtime, architecture, CPU count and OS.

## Frozen minimal matrix

| Runner | Input-derived CSM | Held readers | Natural idle clock |
| --- | --- | --- | --- |
| GitHub-hosted ubuntu-24.04 x64 | 1 MiB, 11 CSM entries, 1 CBLK | 0 and 16, each **fresh OS process** | 0/30 s |
| GitHub-hosted ubuntu-24.04 x64 | 384 MiB, 5052 entries, 2 CBLKs | 0 and 16, each **fresh OS process** | 0/30/300 s |
| GitHub-hosted windows-2025 x64 | same, built on target Windows | 0 and 16, each **fresh OS process** | 0/30 s |
| GitHub-hosted windows-2025 x64 | same, built on target Windows | 0 and 16, each **fresh OS process** | 0/30/300 s |

For each host: 2 external OS-process physical CSM fixture builders, then 4 independent CSM-only consumer processes. Source content is generated **only** in builder, physically hashed (fixture SHA256), deleted before consumer, and never archived. The pre-existing G1-J1 fixture builder is reused with auxiliary builderRunId CORE011-CSM-001/G1J1-E3R2B-... for compatibility; **this does not reopen G1-J decision experiments**.

The 16 held readers are *actually* distinct ManifestReader objects each with its own caller-owned stream. Every reader has performed exactly one ReadAsync(1 entry), so the first CBLK is validated/read but the enumeration is not complete. Their strong references and streams remain live over natural 0/30/300-second idle (no forced GC). Each uses the same immutable underlying physical CSM byte array; per-reader manifest buffer is distinct. Exit records count==16, all correctly partial, stream caller-owned; Dispose after natural idle, then separately record the post-dispose natural and **forced GC diagnostic**. No CPU tasks=16 claimed to equal 16 hardware cores.

In the hold=0 control, exactly the same fixture and three public VerifyManifestAsync warmups plus one warm partial ManifestReader lifecycle are executed, then closed, before the baseline snapshots. Control and parked are **separate processes** because shared process baselines would carry JIT/pool/GC memory from previous cells; process-to-process comparisons are noisy and do not prove causal resident-byte attribution without repetitions.

No numeric budget or acceptance threshold is registered. The E3-R0 protocol and historical 0.1.1 DEFER remain unchanged; no ADOPT/REJECT or hybrid/eager CPU comparison. The next R2-B2 step would need independent host replicates per OS, real-versioned unblinded calibration (NOT the sealed holdout), SHA256/BIDX variants, Windows/portable AOT probes, and lifecycle/Plan characterization before a prospective frozen numerical-budget PR can be reviewed.

## Evidence and safety

A dedicated GitHub-hosted workflow compiles the real unchanged .NET10 Core and new benchmark harness on both OSes. Strict validators require:
- original Git reader SHA256/blob, exact checkout/run ID, protocol parent, historical R0 decision gate still closed, unchanged src/tests/package paths;
- same-OS external builder, source file absence, physically identical CSM and builder manifest/chunk/CBLK count, separate builder and consumer PIDs and **four unique consumers** per OS;
- valid Linux smaps or Windows EX2 source and numerically coherent resident/working set, no zero/negative/NaN; mandatory 30s and 300s *actual elapsed time*, mandatory snapshot stage ordering, explicit no-product-verdict;
- raw JSON, original fixture SHA256, build logs and OS kernel metadata retained as GitHub Actions artifact, including on failure.

**Known limitations:** It observes point-in-time private resident bytes, *not resident high-water peak*; cross-process JIT/GC noise is uncontrolled; physical source-derived corpus is synthetic, not a real-versioned sealed holdout; neither candidates nor full-product Plan are measured. Until these are resolved, E3 production verdict remains NOT_QUALIFIED.
