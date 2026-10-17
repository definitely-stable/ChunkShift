# PATCH-TREE-001 / T4-C1 — .NET 10 real filesystem commit probe

Owner: issue #184; dependencies T4-A (#377), T4-B (#378) and T4-C0 (#379).
Related trust and anti-rollback issue #185 remains **OPEN**. Research
freeze #251 remains **OPEN**.

**Status: research-only, host-observation, no production ADOPT.**
No Patching/Core SDK changes, persisted format, signed trust claims,
release/CLI gate or F2 sealed holdout.

## Decision: what the next experiment should actually measure

T4-B established only a *logical* single commit of tree identity +
(epoch, sequence) cursor. T4-C0 measured CPython's `os.replace`
on actual hosted Linux/Windows temporary files: 200 Linux publications with
no sharing denials; Windows default reader observed 125 access denials and
129 publishing retries, whereas Win32 `CreateFileW` with explicit
`FILE_SHARE_DELETE` observed no reader access denials but 55 publisher
retries on its separate 200-publication lane. No partial heads were observed.
These are not .NET API results.

T4-C1 isolates the **actual .NET 10 APIs** that ChunkShift could eventually
use: `FileStream` with explicitly specified `FileShare`, plus
`File.Move(staged, active, overwrite:true)`. It does not assume that
`File.Move` is a universal crash-durable or guaranteed atomic primitive
on every filesystem. Microsoft explicitly permits cross-volume moves,
which may behave like a copy plus delete. Our experiment avoids that case
by creating source and destination in the same owned directory.

Microsoft references:
[File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0),
[FileShare](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0),
[FileStream.Flush(Boolean)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0).

## Procedure — bounded private test scope

A standalone, zero-third-party-dependency .NET 10 console binary is kept
under `benchmarks/PatchTreeT4C1Probe/`. It operates only inside newly
created `Path.GetTempPath()/chunkshift-t4c1-<random>` directories, deletes
them at completion, and writes only aggregate JSON evidence to the
GitHub-hosted runner temporary folder. No existing installed tree,
developer workspace files, original CSP benchmark artifacts or trusted
policy are accessed.

It writes two different immutable generations containing two small files.
The test-only generation digest is domain-separated SHA256 over their exact
bytes and little-endian lengths, compatible with the previous T4-C0
fixture. The 88-byte `active` pointer packs magic, little-endian epoch,
sequence, raw generation SHA256 and SHA256 over its first 56 bytes.
The canonical previous T4-C0 fixture (epoch 0, sequence 1, 32 zero bytes)
must produce checksum
`0a33e8319dd69e441921385b256f5c8333e699f3d8b02af1d0becb3215c4ce67`.
The old and new generation ids must equal the independent Python-derived
T4-C0 vectors. **This encoding is not an accepted tree-manifest format
and its checksum is not an authenticity signature.**

Every staged 88-byte head is exclusively created and
`FileStream.Flush(flushToDisk:true)` is called before `File.Move`.
The parent-directory `fsync` is attempted via `libc.open(O_DIRECTORY)`,
`libc.fsync`, `libc.close` on Linux, recording the exact observed outcome.
Windows parent-directory fsync is explicitly `NOT_ATTEMPTED`; no
unjustified durable-disk claim is made.

Each of two **separate** lanes performs 200 replacements with four
concurrent reader threads, checking full-head checksums and that the
referenced immutable generation has exactly its expected digest.
Readers open using either (A) `FileShare.Read`, or (B)
`FileShare.Read | FileShare.Write | FileShare.Delete`.
Read permission/sharing denials are *not successful samples* and are
recorded separately for each reader. Unexpected I/O, torn heads,
missing generations, content mismatches, stuck readers or persistent
publication failure **fail the run**. Writer transient Windows
`ERROR_SHARING_VIOLATION`/`ERROR_ACCESS_DENIED` retries are counted
and bounded (500 retries of 2ms per publication); they are **not**
reported as uninterrupted publishing service.
A file descriptor from the original immutable generation remains open
across publication, and its content is re-checked afterward.

Two additional **separate .NET child process** fault barriers:
1. Child creates/flushes staged head, writes `before.ready` and blocks.
   Parent force-terminates child; pointer must remain old.
2. Child creates/flushes stage, performs `File.Move(overwrite:true)`,
   attempts parent sync on Linux, writes `after.ready` and blocks.
   Parent force-terminates child; pointer must be new.

For both barriers the old and new generation contents remain verifiable.
The barrier points are deterministic and exclude instruction-granularity
interruption, kernel faults or real power loss. Negative scenarios include
88 individual bit flips across the independent pointer vector,
truncation/extension, checksum tampering, partial staged file publication
**simulation**, and changed unpublished target generation.

## Acceptance, evidence and non-goals

PR-only workflow runs on GitHub-hosted `ubuntu-24.04` and `windows-2025`.
Pinned repository `global.json` SDK is `10.0.204` with latestFeature
roll-forward. Each lane must complete 200 successful published selectors,
retain an old open generation descriptor, observe 0 torn or mixed content,
count all denied reader opens and writer retry attempts, and capture
the two kill barriers and negative tests. The two lanes need **not**
report zero denials: a denial is itself a valid observed **platform
constraint**, not a sign that the probe should quietly retry it away.
A completed run with denials cannot be described as continuous reader
availability.

If directory sync is unavailable, record the actual errno. If it is
successful, state only that the syscall succeeded, not that a cloud
disk is physically durable. Do not generalize host results to NFS,
SMB, antivirus, ACL, network volumes or all thread interleavings.

Excluded: hostile reparse-point path races; genuine ENOSPC / I/O fault
injection; storage power-cut simulation; signed publication/anti-rollback;
real .NET updater API; process-level reader pin coordination; atomicity
theorem; cross-volume publication; Unicode, full ACL metadata and mount
semantics. Stage failure is simulated by writing a truncated staged file
**without** invoking `File.Move`; it is not evidence of low-level partial
writes in the selected filesystem.

T4-C1 is a prerequisite for a separate T4-C2 OS-adapter decision,
not a production publisher. A future implementation must require an
external trusted authorizer (#185), verify the activated generation,
prevent rollback of the accepted epoch/sequence, qualify actual
filesystem primitives, and demonstrate crash recovery with
platform-specific durability tests before defining persisted contracts.

Classification: `HOST_DOTNET_OBSERVATION_ONLY`,
`HOST_READER_SHARING_DENIAL` where any denied read is observed,
and `HOST_BLOCKER` if a single malformed/partial head or content mismatch
is observed. No successful experiment closes #184 or #251.
