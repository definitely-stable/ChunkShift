# PATCH-TREE-001 / T4-C1: original .NET 10 host filesystem pilot

**Research verdict:** `HOST_DOTNET_OBSERVATION_ONLY`. Not production
ADOPT, not an atomicity theorem and **not power-failure durability**.

Original GitHub-hosted Release probe: [run #39048221682](https://github.com/definitely-stable/ChunkShift/actions/runs/39048221682);
experiment source HEAD `f343d078087604496f277efde22175274d2be67f`;
platforms Ubuntu 24.04 and Windows 2025; .NET runtime **10.0.13**.
All two specialized OS jobs **SUCCESS**. Build warnings are errors.

**Audit trail:** the first two iterations failed build on .NET
analyzer gates (CA2101 P/Invoke string marshaling, CA1869 cached JSON
options, CA2201 typed exception). All were corrected before evidence
run #39048221682. Those earlier compilation failures are not claims
about host filesystem publication. Real evidence uses a direct .NET
`File.Move(..., overwrite:true)` call and `FileStream` with explicit
sharing modes; it is **not** CPython `os.replace`.

## Independent original artifact replay

Both original GitHub-hosted artifact ZIPs were downloaded separately
and examined with Python `zipfile.testzip()` + `json.loads()`,
including their archive SHA256, schema, success marker and numeric
per-lane constraints. This verifies retained **diagnostic JSON**
integrity and internal consistency, not complete byte-for-byte disk
traces for every commit.

| Evidence | Ubuntu 24.04 | Windows 2025 |
| --- | --- | --- |
| GitHub artifact ID | `12106905843` | `12106299046` |
| Retained artifact ZIP SHA256 | `692fa23b22ac1d737e21741b2307d1f5652987aed25e4b445c2ef1884046b20d` | `95dbec22f17076a67e7daaff4bb8627023a2dbac2fbd07cc950dcb7dc5c9c1de` |
| .NET actual publication primitive | `File.Move(staged, active, overwrite:true)` | same |
| Initial staged file flush | `FileStream.Flush(true)` | same |
| Directory sync after each observed replacement | POSIX `fsync` syscall succeeded 200 per lane | explicitly NOT_ATTEMPTED |

### Measured original comparison (400 publications / OS)

| Metric | Ubuntu `FileShare.Read` | Ubuntu `ReadWrite\|Delete` | Windows `FileShare.Read` | Windows `ReadWrite\|Delete` |
| --- | ---: | ---: | ---: | ---: |
| Actual head replacements | 200 | 200 | 200 | 200 |
| Successful reader snapshots (4 threads) | 11,540 | 6,719 | 23,216 | 23,381 |
| Reader access/sharing denials | 0 | 0 | **327** | **0** |
| Writer `File.Move` sharing retries | 0 | 0 | **29** | **23** |
| Sampled torn head/mixed content | 0 | 0 | 0 | 0 |
| Old pinned generation handle preserved | yes | yes | yes | yes |
| Parent directory fsync syscall successes | 200 | 200 | 0 (not attempted) | 0 (not attempted) |

Windows default reader denials were distributed **75 + 88 + 83 + 81**;
explicit `FileShare.Read|Write|Delete` observed **0 + 0 + 0 + 0**.
The latter reduced *observed read denials*, but **did not eliminate
publisher retries**. It must not be called wait-free, failure-free or
universally portable. The two lanes are sequential and sampled on one
host each: counts vary by scheduler, filesystem and instrumentation.

Both OSes also passed the two **separate .NET child-process termination**
barriers. Child killed before replacement: old complete pointer and
policy cursor (epoch 0, sequence 1) remained; child killed after
replacement: new complete pointer (epoch 0, sequence 2) remained.
Both generations were readable in both cases. Linux exit codes `137`
and Windows `-1` were recorded. Synthetic negative controls
rejected six malformed/tampered cases on each host; the checksum
vector was additionally tested with 88 independent single-bit flips.
A truncated staged-file write was *simulated* and correctly not
published, but **ENOSPC/fsync kernel failure was not injected**.

The `active` data structure and old/new content digest reproduce the
independent T4-C0 fixture; these checksum/digests are laboratory
integrity evidence only and are **not a detached signature**. No raw
per-publish trace is persisted, so the aggregate counts are not a
proof about every possible concurrent interleaving.

## Interpretation and next decision gate

1. Actual .NET 10 `FileStream` readers on this Windows runner exhibit
   sharing-mode sensitivity, matching the qualitative discovery from
   the previous Python/Win32 experiment T4-C0. Default
   `FileShare.Read` is a **real observed read-continuity blocker**.
   Explicit `FileShare.Delete` is a promising requirement for any
   candidate reader adapter but not a sufficient publishing contract.
2. Actual .NET `File.Move(overwrite:true)` on these **same-directory**
   samples made 200 successful replacements in each lane; no reader
   observed a torn head or different-generation content. This is a
   bounded observation of actual Linux/Windows operations, *not*
   a universal atomicity or power-fail crash-consistency proof.
3. On this Linux runner POSIX `fsync(directory)` syscall returned
   success after each of 400 replacements. The Windows prototype
   records parent-directory synchronization as unavailable from the
   selected managed API and **does not claim durable metadata**.
   FileStream.Flush(true) is not equivalent to whole-tree durable
   publication across every filesystem or OS configuration.
4. OS differences, hostile reparse/symlink traversal, path aliases,
   ACL/ownership, mount and cross-volume behavior, replica consistency,
   true power interruption, antivirus sharing and signed update policy
   remain unresolved. The #185 trust RFC is OPEN; a writable unsigned
   `active` file is susceptible to malicious rollback/tampering.

**Conclusion:** `LAB_SAMPLED_DOTNET_FEASIBLE` on GitHub-hosted Linux
for two open modes; Windows shows `DEFAULT_FILESHARE_READ_BLOCKER`
with `FILESHARE_DELETE_CONTROL_ZERO_READER_DENIALS_IN_SAMPLE`,
**23 writer retries remain**. This is evidence to inform T4-C2 OS
adapter selection, not a decision to ship a production tree publisher.

**Proposed T4-C2:** first freeze authenticated active-pointer and
monotonic policy cursor semantics together with #185. Then
preregister OS-specific handles/publish primitives, failure injection
(ENOSPC, access-denied, pre/post-sync failure), cross-volume rejection,
a deterministic per-host kill matrix, secure handle-relative path
walking, directory durability and independent reader-process
observations. No changes to CSP v1, CSM v1, package public API or the
sealed F2 corpus, and no research freeze #251 closure.
