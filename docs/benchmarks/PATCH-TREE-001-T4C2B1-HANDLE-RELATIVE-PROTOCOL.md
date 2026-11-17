# PATCH-TREE-001 / T4-C2B1 — OS handle-relative no-follow reader study

Status: **PREREGISTERED / RESEARCH-ONLY / NO_OS_PUBLISHER_ADOPT**.
Owner: #184. Parent research freeze: #251. Previous:
[T4-C2A](PATCH-TREE-001-T4C2A-FAULT-BOUNDARIES-PROTOCOL.md).
ExperimentId: `PATCH-TREE-001`. Host-owned signed policy is governed by
[RFC-0006](../architecture/RFC-0006-patching-distribution-trust-boundary.md).

## Why narrow the next slice

T4-C2A tested crash boundaries of a path-based `File.Move`, but its
preflight directory checks were explicitly vulnerable to a path
replacement between validation and use. T4-C2B1 changes only a
**bounded READ** from an already trusted starting directory. It DOES
NOT yet provide a secure relative CREATE, rename, staged-writer,
snapshot/retirement policy or a production publisher.

## OS mechanisms tested

- **Linux x64 (Ubuntu 24.04):** use actual `syscall(__NR_openat2=437)`
  with one component per call, root directory fd retained, `O_RDONLY |
  O_NOFOLLOW | O_CLOEXEC`, `O_DIRECTORY` on intermediate entries,
  and `RESOLVE_BENEATH | RESOLVE_NO_SYMLINKS | RESOLVE_NO_XDEV`.
  Fail closed on `ENOSYS`, `EINVAL`, `EXDEV`, `ELOOP` and other
  error statuses. Never fall back to path-based `File.Open`.
- **Windows (windows-2025):** a trusted root is opened with
  `CreateFileW(FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT)`
  and checked for `FILE_ATTRIBUTE_REPARSE_POINT` on the acquired
  handle. Each child is opened **by relative name** using
  `NtCreateFile`, `OBJECT_ATTRIBUTES.RootDirectory` set to the
  retained parent handle and `OBJ_DONT_REPARSE | OBJ_CASE_INSENSITIVE`.
  Use `FILE_OPEN_REPARSE_POINT`, assert directory/non-directory open
  options, reject any returned handle whose
  `FileAttributeTagInfo` has the reparse flag, and keep the parent
  handle alive through the child open. Fail closed on NTSTATUS or
  unavailable API; no full-path post-check as a purported protection.
- The starting root is established by a **trusted fixture creator**.
  The experiment does **not** prove the root's parent path/volume cannot
  be manipulated before that first open. No OS security descriptor,
  tamper-resistant root, trusted writer or signed update verification.

### Input/path prevalidation

An intentionally restricted portable ASCII subset, inspired by T4-A,
rejects drive/absolute roots, `..` and `.` components, empty
components, `:` (NTFS ADS), backslashes and control characters. The
kernel-level handle-relative/no-follow check remains necessary
**independent of this validation**, because an attacker can swap an
otherwise safe component for a symlink/junction after the check.

A hard link to a file outside the installation tree is not excluded by
the `no-follow` flags; caller-supplied file integrity and content hash
verification and a separate trusted staging/creation boundary remain
required. Windows device-name filtering in the prototype is incomplete
and **does not constitute production path-policy acceptance**.

## Frozen test matrix

On GitHub-hosted Ubuntu 24.04 and Windows 2025, zero third-party
NuGet dependencies and warnings-as-errors:

1. Build and read `safe/inner/payload.txt` under a trusted fresh root
   through actual held native directory/file handles.
2. Reject each of nine malformed logical paths before OS traversal,
   including parent traversal, empty component, absolute, backslash,
   NTFS stream syntax and embedded NUL.
3. Create a real symlink to a separately created outside directory
   (Windows junction fallback if symlinks require privilege); deny both
   file traversal and direct opening of the reparse directory.
4. Open a legitimate directory handle, attempt rename of its original
   name, replace that name by a symlink/junction to an outside directory,
   then read through the **previously opened** directory handle:
   only the original safe file is acceptable. On Windows a
   `Directory.Move` sharing denial is recorded as an explicit
   `RENAME_OF_OPEN_DIRECTORY_DENIED` capability observation, and
   **does not establish concurrent swap resistance**. Linux lane
   should demonstrate a successful pinned-handle after-swap read.
5. After the adversarial checks, the untouched safe path remains
   readable. No process may receive the outside secret through any
   requested logical path.
6. Export finite JSON with OS/runtime, link kind, pin outcome, negative
   count and explicit trust/no-publisher limitations. A Python stdlib
   checker must reject missing or unexpected result fields and prevent
   a negative/blocked Windows swap from being reclassified as PASS
   for the swap proof.

GitHub-hosted specialized matrix plus main CI/CodeQL/Heavy/Benchmark,
dependency and PR checks on the **final PR SHA** must finish without
failure. Archive original per-host JSON artifacts. Incomplete link
creation, unsupported API and Linux inability to pin a moved directory
are HOST_BLOCKER, not success.

## Adversarial limits / acceptance

Allowed limited result:
`T4C2B1_HANDLE_RELATIVE_READ_NOFOLLOW_OBSERVED`, with explicit
per-host pinned-handle capabilities. **NO_OS_PUBLISHER_ADOPT**.

No signature/authenticated root, same-volume enforcement over a
production tree, complete object-type/mode policy, ACL/xattr/hardlink
confinement, Windows exact filesystem equivalence, malicious
privileged kernel attack, automatic device-name handling, cache
tamper resistance or untrusted writable tree is implemented.
`openat2` / `NtCreateFile` are opening primitives, **not** atomic
publication, safe file creation or power-cut durability proofs.

## Next T4-C2B2 and T4-D

T4-C2B2 must characterize secure relative **stage creation**,
directory construction and rename against adversarial swaps, including
Windows NT handle-relative create/reparse rules, file IDs, volume
confinement and an explicit supported filesystem/ACL policy. T4-D
then handles flush/metadata durability, genuine I/O failures, reader
pins/GC and OS crash fault injection under an externally authorized
update-policy. #184 and #251 stay OPEN.
