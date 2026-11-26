# R1-C.2a — REPO-FLUSH-001 platform persistence capability baseline

Status: ACCEPTED in #499 — [reviewed capability evidence](../research/results/REPO-FLUSH-001-EVIDENCE-20261126-001.md); CAPABILITY_ONLY, default DEFER, crash ordering UNQUALIFIED. Exact integration head `28786d99484b8fb3f54ab30698f63a8ae2666d56`, [flush](https://github.com/definitely-stable/ChunkShift/actions/runs/45828255120), [CI](https://github.com/definitely-stable/ChunkShift/actions/runs/45828255205), [Heavy](https://github.com/definitely-stable/ChunkShift/actions/runs/45828254975), all applicable workflows PASS; merged `4170dc2f709eabaf2bc9400a75de9c612b9c94a6`. Refs #10 #133 #139 #144.
Predecessor REPO-CRASH-001 accepted. REPO-INGEST-003 performance eligible,
production default DEFER. Scope: observed return codes on test-owned local
runner files, not a durable storage contract or crash simulation.

## API research and contract

- [.NET FileStream.Flush(Boolean)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0):
  use flushToDisk:true to request intermediate-buffer persistence; current
  pack writer's FlushAsync is not this operation.
- [Linux fsync(2)](https://man7.org/linux/man-pages/man2/fsync.2.html):
  file sync does not necessarily persist its directory entry; directory
  descriptor fsync is a separate operation. Record errors, never assume success.
- [Windows directory handles](https://learn.microsoft.com/en-us/windows/win32/fileio/obtaining-a-handle-to-a-directory)
  and [FlushFileBuffers](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers):
  directory open uses FILE_FLAG_BACKUP_SEMANTICS; flush requires GENERIC_WRITE.
  Probe this precise ordinary-user directory handle, record open/flush/close
  return codes. Even success is not documented POSIX-equivalent crash ordering.
  No volume handle, privilege escalation, NT kernel API or global disk flush.

## Fixed probe

Separate research-only mode in existing CrashLab, unchanged pack writer and
coordinator. Fresh external directory, 65536 deterministic bytes, managed
FileStream CreateNew/write/Flush(true)/close, second CreateNew must fail and
original bytes stay intact. Independently hash resulting file. Native regular
file sync through os.fsync (Linux) / FlushFileBuffers on write-capable handle
(Windows). Verify after native call. In same directory sync after initial
create, after same-directory rename to previously absent name, and after
delete of only that known fixture. Each directory attempt records native
operation/phase/error. Linux uses os.open O_DIRECTORY|O_RDONLY and fsync;
Windows CreateFileW GENERIC_WRITE, FILE_SHARE_READ|WRITE|DELETE, OPEN_EXISTING,
FILE_FLAG_BACKUP_SEMANTICS and FlushFileBuffers. Directory failures are recorded
capability gaps, not silently converted into durable success. Regular file
flush must succeed on hosted supported lanes. No native EIO simulation.

Negative controls: managed exclusive create collision; native invalid descriptor
fsync(-1) => EBADF on Linux, invalid handle FlushFileBuffers => ERROR_INVALID_HANDLE
on Windows. Linux /dev/full one-byte write => ENOSPC (safe device, no disk filling;
not delayed writeback failure, not actual disk exhaustion); Windows ENOSPC
untested, explicit NOT_TESTED. No actual power interruption or destructive test.
Windows native read-only handle flush should fail, recorded as independent
negative control; if unexpected success STOP and review. Linux read-only
fsync semantics not assumed identical. Capture SDK/runtime/OS/arch, filesystem,
runner image and exact head. Hash all evidence; no timings.

All three lanes Windows x64/Linux x64/Linux ARM64. Structural probe success
requires managed and native regular file flush, exact byte identity,
collision/invalid handle negative controls, Linux ENOSPC, rename/delete
state and no fixture residue. Directory success and documented semantics
are separate capability outcomes; on any directory failure report DEFER.
Windows crash-ordering contract remains UNQUALIFIED even on observed directory
success. No production adoption. Next R1-C.2b explicit persistence backend
contract with fault propagation and fail-closed ordering, native EIO/late
ENOSPC research where safe. R1-C.3 durable intent design only after capability
review. No format freeze or public API changes.
