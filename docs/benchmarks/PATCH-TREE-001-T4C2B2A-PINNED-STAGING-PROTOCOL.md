# PATCH-TREE-001 / T4-C2B2A — held-parent relative staging and no-clobber rename

**PREREGISTERED LAB / NO PRODUCTION PUBLISHER**. ExperimentId: `PATCH-TREE-001`.
Owner #184; final research freeze #251. Predecessors: T4-C2A fault-cut
and T4-C2B1 real Linux/Windows pinned directory reads. RFC-0006 trust
boundary remains application-owned.

## Narrow research question

Can a process that already owns a directory HANDLE/fd create a new
regular stage file and rename it **to a previously absent name in the
same pinned directory**, even if an attacker changes the *path naming*
that directory after its handle was opened, without following an
outside symlink?

This is **not** the same as proving that the detached directory still
belongs to an authorized installation tree. A holder of an untrusted
root can still write in the wrong place; that remains an unsolved
product security/publisher condition.

## OS contracts

- Linux GitHub ubuntu-24.04 x64: `openat2` using
  `O_CREAT|O_EXCL|O_RDWR|O_NOFOLLOW|O_CLOEXEC`, mode `0600`,
  `RESOLVE_BENEATH|RESOLVE_NO_SYMLINKS|RESOLVE_NO_XDEV` against
  retained directory fd; `Flush(true)` on the created stage.
  `renameat2(parentfd,stage,parentfd,target,RENAME_NOREPLACE)`.
  No fallback to plain rename or full-path Create/Open on failure.
- Windows GitHub windows-2025 x64: `NtCreateFile(FILE_CREATE)`
  relative to `OBJECT_ATTRIBUTES.RootDirectory`, with
  `OBJ_DONT_REPARSE|OBJ_CASE_INSENSITIVE`,
  `FILE_OPEN_REPARSE_POINT` and on-handle reparse attribute refusal.
  Open stage with DELETE permission for rename. Then
  `SetFileInformationByHandle(FileRenameInfo)` with
  `ReplaceIfExists=false` and `FILE_RENAME_INFO.RootDirectory`
  containing the retained parent handle, and only a relative target
  single component. No `File.Move` full-path fallback.
- The **starting trusted root is fixture-owned and pretrusted**.
  Directory names are restricted to 63-byte portable ASCII components;
  this is not accepted Windows path/ACL policy. No nested directory
  creation is provided by this slice.

Native API references:
[Linux openat2](https://man7.org/linux/man-pages/man2/openat2.2.html),
[renameat2](https://man7.org/linux/man-pages/man2/renameat2.2.html),
[FILE_RENAME_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info).

## Frozen experiments

Both hosts must have real, passing .NET 10 Release builds and the
following independent Python assertions on original JSON:

1. Create stage via held-directory handle and flush real bytes;
   reject second `CREATE_NEW` for the same name without truncating it.
2. Reject NOREPLACE rename while target exists; target old bytes stay
   intact; staged bytes stay intact.
3. Rename stage to a fresh target name in the held directory;
   old stage name is absent and target bytes match original.
4. Create second stage, **rename the held parent directory's visible
   name** and replace it by a malicious link to a separate outside
   directory. Native handle-relative rename must put target under the
   old **held directory** and leave outside sentinel/contents unchanged.
5. Deny following the new symlink from the original trusted root.
6. Explicit finite evidence includes `pretrustedRoot=true`,
   `rootStillAuthorizedAfterMove=false`, `writerAuthorization=false`,
   `supportsProductionPublisher=false`,
   `testedRaceInterleavings=1` and `powerCutDurability=false`.
   None may be promoted to ADOPT after a successful experiment.

All tests run in one disposable process and owned temporary directory,
not a hostile-principal namespace or controlled power-loss VM.
No repeatable concurrent swap loop, user privilege/ACL or hardlink attack.
Even successful native syscall results do **not** prove durability,
safe update authorization, atomic replacement of already existing
active pointer, protected witness, or safe publication.

An unsupported native API, no-clobber implementation, or incompatible
filesystem result is **HOST_BLOCKER_UNVERIFIED**, not a successful
fallback. Windows directory rename may be refused due to sharing;
such a result is recorded as a blocker for this experiment, not
misrepresented as a passed swap.

## Exit gate

Accept only `T4C2B2A_SCOPED_CREATE_NOREPLACE_LAB_PASS /
NO_OS_PUBLISHER_ADOPT` after both original host results, independent
checker, and general CI/CodeQL/Heavy/Benchmark/dependency review
succeed on the **final PR HEAD**. No `src/`, CSM/CSP, public API,
persistent tree-format or publication changes. #184/#251 remain OPEN.

**Next:** T4-C2B2B must validate full root authorization after
adversarial rename, relative nested-directory creation, file/volume
identities, Windows reparse/junction/hardlink/ACL cases and native
failure-injection. T4-D separately covers replacement of the committed
selector, reader pinning/retirement, writeback ordering,
power-cut/ENOSPC/EIO and authenticated host policy.
