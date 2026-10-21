# PATCH-TREE-001 / T4-C2B2B — held file identity and authority race

ExperimentId: `PATCH-TREE-001`; owner #184; research freeze #251.
**PREREGISTERED LAB / NEGATIVE SECURITY CONTROL / NO_OS_PUBLISHER_ADOPT**.
Previous T4-C2B2A: [pinned staging protocol](PATCH-TREE-001-T4C2B2A-PINNED-STAGING-PROTOCOL.md).
Trust is external to `ChunkShift.Patching` under RFC-0006.

## Research question and kill criterion

T4-C2B2A showed that an opened directory fd/HANDLE continues to target
the original directory after an adversary renames its visible name.
That is excellent protection against malicious symlink *redirection* but
**does not authorize installation in a detached tree**.

Test the strongest readily available cross-platform *snapshot
observation*, then explicitly falsify the tempting inference:

> Matching immutable-looking IDs before publication proves that an
> eventual rename is authorized.

A valid experiment MUST demonstrate all three:
(1) IDs are equal before namespace change;
(2) after an attacker replaces the authorized name by another object,
    a fresh trusted-root-relative identity comparison refuses it;
(3) despite the successful earlier comparison, renaming through the old
    held directory still succeeds in a detached path, proving the
    intervening authorization gap. Treat (3) as **NO_GO for
    preflight-only authorization**, not as safe publishing.

## Native identity and nested creation

Linux x64 GitHub-hosted ubuntu-24.04:
- `openat2` component-by-component relative to a pinned directory
  with `O_NOFOLLOW|O_DIRECTORY` and
  `RESOLVE_BENEATH|RESOLVE_NO_SYMLINKS|RESOLVE_NO_XDEV`.
- `mkdirat(parentfd, child, 0700)`, no overwrite, followed by a
  no-follow `openat2` of the new nested directory;
  reject duplicate creation.
- `statx(fd,"",AT_EMPTY_PATH,STATX_INO|STATX_MNT_ID)`, check the
  returned mask, record `stx_dev_major`, `stx_dev_minor`, mount ID
  and inode to compare opened root/parent/nested/file identities.
  Fail closed on unsupported `statx`/mount ID.
- `renameat2(RENAME_NOREPLACE)` relative to the held nested fd.

Windows x64 GitHub-hosted windows-2025:
- Use `NtCreateFile(FILE_CREATE|FILE_DIRECTORY_FILE)` relative
  to `RootDirectory` for a *new* nested directory, with
  `OBJ_DONT_REPARSE` and on-handle reparse check. Reject duplicate.
- `GetFileInformationByHandleEx(FileIdInfo)`, comparing
  `VolumeSerialNumber` and full 128-bit file ID of two open handles.
  Refuse capability errors; use `NtCreateFile` with held handles
  rather than unsafe full-path fallback.
- `NtSetInformationFile(FileRenameInformation)` to rename through
  held nested directory. `SetFileInformationByHandle` with non-NULL
  `RootDirectory` failed Win32 87 in T4-C2B2A and is NOT retried.
- The starting trusted root is fixture-owned, not cryptographically
  authenticated and not controlled by a different user/ACL.

**No portable security guarantee is inferred from equal serials,
inode numbers or file IDs**; it is an observation of object identity
for still-open handles. IDs do not lock namespace parentage, establish
authority, or eliminate mount/file-ID reuse and filesystem semantics.

## Frozen deterministic adversarial experiment

1. Create a trusted directory `trusted/anchor` from a disposable
   fixture. Open and retain `trusted` and `anchor` handles.
2. Verify identity equality between held `anchor` and an independent
   opened `trusted/anchor` by trusted-root-relative native open.
   Compare reported device/mount or volume and record result.
3. Create nested directory `nested` through a held `anchor` handle.
   Reject a second create. Reopen nested with no-follow and compare
   object ID and volume to retained root/parent.
4. Create and flush a stage under held `nested`, **keeping the same
   stage HANDLE open throughout** (no re-open by mutable source name).
5. Deliberately interleave a namespace change *after* the identity
   precheck: rename `anchor` to `detached` and create a **new**
   physical directory at the old `anchor` name.
6. A fresh trusted-root-relative ID observation MUST detect mismatch;
   the held `anchor` handle MUST retain its old ID.
7. In this experimental counterexample, deliberately perform a
   no-replace rename using the old held `nested` handle. The output
   MUST exist in `detached/nested` and MUST NOT exist in the
   newly installed `anchor` directory. Verify same file ID of
   stage/target and exact target contents.
8. Remove the fresh `anchor`, install an outside symlink to its old
   name, verify native trusted-root-relative open still rejects it,
   and verify outside sentinel bytes remain unchanged.
9. Export original finite JSON with all conditions, explicit
   `snapshotCheckIsAtomicWithRename=false`,
   `finalRootNameAuthorized=false`,
   `productionPublisherAdopted=false` and no hostile concurrency or
   power-cut claims. Independent Python validator MUST require all
   negative controls, not only the favorable OS observations.

This tests exactly one deliberate interleaving and tests no
privileged malicious process, suspended writer, hardlinks/ACLs,
Windows junction fallback, mount races, real device failures,
identity ABA/recycling, filesystem crash/power interruption, or
publication of an existing active selector. For safety, the code only
operates in a newly created temporary fixture. The test intentionally
creates a file in a detached directory: **it must never be installed
as a public updater**.

## Acceptance

Specialized GitHub-hosted Linux/Windows .NET 10 Release builds with
warnings-as-errors, Python stdlib independent assertions, original
per-host JSON artifacts and full final-head CI/CodeQL/Heavy/Benchmark
and dependency checks. No production `src/`, CSP/CSM, NuGet/API
modifications. #184, #251 stay OPEN.

Allowed combined verdict:
`T4C2B2B_NATIVE_IDENTITY_NESTED_CREATE_PASS /
PREFLIGHT_AUTHORITY_RACE_NO_GO / NO_OS_PUBLISHER_ADOPT`.

If Windows directory `NtCreateFile`/identity is unsupported, or
Linux statx mount identity unavailable, record HOST_BLOCKER_UNVERIFIED.
Never silently fall back to path-based APIs.

## Next gate

Do **not** patch the demonstrated authorization race with yet another
preflight compare immediately before rename; that retains a TOCTOU
window. Design an OS-specific namespace ownership/policy boundary
where the updater has exclusive authority over the committed selector,
and qualify reader handles/leases and crash recovery (T4-D). Strong
anti-rollback/remote trust is separate under RFC-0006.
