# PATCH-INCR-001 A3-S0 — early go/no-go screening protocol

**Date/preregistration:** 2026-11-01, before first A3 measurement. Owner #150; release gate #251. Status: **SCREEN_ONLY / NO_PRODUCTION_ADOPT**. Depends on A2c-S2a #432 and A2c-S2b #433. No profile/CSM/public API/publisher modification.

## Hypothesis and asymmetric stopping rule

The public full `ChunkManifest.CreateAsync` visits the target stream once. Existing A2b-S3, invoked by S2a/S2b, calls `ChunkManifest.VerifyAsync(oldSource, oldCsm)` first; Core's `CsmContentVerifier.VerifyAsync` rescans the **entire old source** through `ChunkingKernel.ScanAsync` before any exact convergence reuse. This adds at least N bytes of re-scanning for an unchanged-sized N-byte base, irrespective of kernel reflink savings. That makes the issue #150 primary `SourceBytesRead/NewContentLength` criterion unlikely to succeed.

**Early necessary gate** (predeclared, both BIDX settings):
- measured `(OldSourceVerificationBytesRead + NewContentBytesRead) / NewContentLength <= 0.50`;
- incremental end-to-end median wall time / full-create end-to-end median wall time `<= 0.80`.

If either condition fails, **DEFER_CURRENT_IMPLEMENTATION / STOP**, without creating a 10/100-GiB large run or sealed holdout. This is a conservative *screen*, not evidence of general behavior of all possible authenticated incremental engines. A positive screen would require a separate frozen original/holdout protocol and independently approved security story before adoption. A failed screen establishes no need to build yet more snapshot backends or public dirty-range APIs.

## Measured execution and controls

- GitHub-hosted Ubuntu 24.04 runner, a freshly formatted mounted 640-MiB Btrfs loopback volume. Test validates `stat -f` and an independent positive `ioctl(FICLONE)` probe; unsupported-clone skips are forbidden.
- Frozen fixture **96 MiB** deterministic high-entropy bytes seeded with .NET `Random(150)`; one 4-byte overwrite at old offset 49,152. A complete target file and independent canonical CSM-v1 oracles with and without BIDX are generated in setup **before** any timed measurement. Old CSM generated from old source, no production authentication claim.
- `full` measures public `ChunkManifest.CreateAsync` on the fully materialized target, writes a private staged CSM, flushes it to disk, independently calls `VerifyManifestAsync`, checks BIDX, and performs a no-overwrite local rename.
- `s2b` measures the complete `A2c-S2b.BuildLocalAsync` sequence: FICLONE source clone (not full copy), bounded CSM copy, S2a complete independent old CSM verification plus **S3 full old-content CDC verification**, real convergence, staging, staged CSM verification, lease checks and no-overwrite rename.
- An old CSM ManifestId is fetched in setup, outside the measured interval, to give S2b the benefit of a preexisting host-authoritative identity. This is **not** a signature or provenance proof.
- Both lanes require exact **physical byte-for-byte** equality against the preprepared fresh full-create CSM, BIDX on and off. The oracle file read and check are *after* the timed interval.
- Three isolated **fresh processes per lane per BIDX**; 12 processes total. Execution order alternates `full/s2b`, `s2b/full`, `full/s2b`. No intra-process warmup; both paths include their startup/JIT costs. No claim of randomization or CPU-frequency control.
- Each process records own wall time, total CPU time delta, process-lifetime peak resident working-set bytes, Linux `/proc/self/io` rchar/wchar/read_bytes/write_bytes deltas, S3 verified-old bytes and virtual-target scan bytes, CSM-copy and stage-verification bytes. `read_bytes` may report zero due to page cache: **not cold physical I/O**.
- Aggregate stores all 12 raw JSON samples and a summary/decision JSON as immutable CI artifact. A failed benchmark or oracle mismatch fails CI; a scientifically negative **DEFER** decision remains a successful, valid measurement job.

## External validity and known biases

This is an intentional **small original screening gate** only, not the 10/100-GiB / real multiversion game/VM traces required for an ADOPT verdict. Baseline full create has a previously materialized target file whose construction is excluded; S2b has a frozen old snapshot source and trusted edit receipt supplied by the lab. The asymmetric setup is explicit, not necessarily a model of every product workflow. Metadata authority lookup is excluded from S2b's measured time, which favors incremental. Disk page cache, compressed/reflink extents, CPU dynamics and startup/JIT introduce noise; a decisive **read-ratio** failure is structural even if wall times are noisy.

A failed early necessary gate does not disprove the mathematical reset-cut proof, exact CSM equivalence, Btrfs clone isolation or possible benefit under a *different* independently trusted CAS/journal with no full-old CDC pass. It rules out first-release **adoption of the current fully-verified S2b pipeline**. Future work may reopen only on a named consumer with stable old-content identity and independently verified edit provenance.

## Acceptance and decision recording

- Require pinned GitHub-hosted run ID, exact tested commit SHA, raw samples, environment and derived pass/fail table.
- If screen fails, record `PATCH-INCR-001 = DEFER / NO_PRODUCTION_ADOPT` for first-release use, no further snapshot infrastructure; preserve proof/tests as research evidence.
- Decide separately how to archive Draft PR #432/#433 and avoid merging lab-only workflow scaffolding by default.
- Research freeze #251 may mark #150 disposition complete **only after** evidence record and issue/index statuses are consistent. This screen does not authorize CSP release, #140 API or publication.
