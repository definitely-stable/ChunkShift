# PATCH-TREE-001 / T4-C2B2B — held identity and authorization-race evidence

**EvidenceId:** `PATCH-TREE-001/EVIDENCE-20261021-001`  
**ExperimentId:** `PATCH-TREE-001`; owner [#184](https://github.com/definitely-stable/ChunkShift/issues/184); freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).  
**Date:** 2026-10-21.  
**Classification:** **HOST NATIVE OBSERVATION PASS / PREFLIGHT AUTHORITY NO-GO / NO_OS_PUBLISHER_ADOPT**.

## Frozen question and test design

The previous T4-C2B2A research showed that a retained directory HANDLE
remains usable after its name has been replaced. T4-C2B2B asks whether
Linux inode/device/mount identity or Windows 128-bit FileId plus volume
serial can observe replacement, and whether such a preflight check
**fences a later stage create or rename**. The answer to the second
question is **no** in a deliberately constructed adversarial schedule.

[Frozen protocol](../../benchmarks/PATCH-TREE-001-T4C2B2B-IDENTITY-AUTHORITY-PROTOCOL.md).

## Tested native capabilities

On both hosts a fresh trusted fixture-owned root and a pinned parent
are opened. Nested directory creation is performed **relative to that
open parent** and duplicate creation is rejected. Object IDs are
captured from native open handles and compared against an independently
opened path relative to that trusted root:

- Linux: `mkdirat`, `openat2` no-follow/beneath/no-xdev,
  `statx(AT_EMPTY_PATH, STATX_INO|STATX_MNT_ID)`, observing
  `stx_dev_major`, `stx_dev_minor`, mount ID and inode.
- Windows: `NtCreateFile(FILE_CREATE|FILE_DIRECTORY_FILE)`,
  `GetFileInformationByHandleEx(FileIdInfo)`, comparing volume
  serial and full `FILE_ID_128`, with no-full-path fallback for
  relative opens; `NtSetInformationFile` for scoped rename.

The experiment first verifies that a root-relative reopen of
`anchor` matches the retained parent ID. All child handles are then
closed for the next Windows directory-move test, but the parent handle
**remains pinned**. The attack schedule renames `anchor` to
`detached` and creates a new directory object named `anchor`.
A fresh identity check detects the mismatch; the old pinned parent
ID is still equal to itself. **Nevertheless**, nested is reopened
relative to the original held parent, a stage is created, flushed,
and renamed through the *same continuously held stage HANDLE*,
placing target bytes into `detached/nested`, not into the currently
named `anchor` installation. That is the intentionally accepted
counterexample. An outside symlink subsequent to the rename is also
rejected.

## Original GitHub-hosted evidence

[Specialized run #39643367368](https://github.com/definitely-stable/ChunkShift/actions/runs/39643367368)
has original independent GitHub-hosted jobs **SUCCESS** on both OSes,
at implementation/protocol HEAD
`c00ad4d427e88c1a9a8e60f928cd7bcb01847591`.
Reported runtime is **.NET 10.0.13**.

| Host | Original job | Archived JSON artifact | Outcome |
| --- | --- | --- | --- |
| Ubuntu 24.04.5 LTS | #119372450006 | #12366052146 | SUCCESS; independent assertions PASS |
| Windows 10.0.26100 (`windows-2025`) | #119372450516 | #12364524633 | SUCCESS; independent assertions PASS |

The original job logs were inspected for emitted per-host JSON and
the independent Python PASS message. The artifact ZIP files were
listed through the GitHub API but **not independently downloaded or
cryptographically hashed**. No additional OS/FS kernel trace, stress
loop or real concurrent attacker is claimed.

The first Linux schedule PASSed. The **first Windows schedule failed**
with `Access denied` on moving the parent directory **while opened
descendant directory/stage handles still existed**. This is a genuine
Windows sharing/capability difference and was not a pass. A revised,
documented schedule moves the parent after the preflight but **before
opening the stage file**, with descendant handles released. That
isolates the authorization race without claiming Windows supports
directory moves with open descendants. The final successful host
runs above are for this revised schedule.

## Exact reported checks and conclusions

Both OSes reported true for:
`initialIdentityMatched`, `rootAndParentVolumeMatched`,
`nestedVolumeMatched`, `duplicateCreateRejected`,
`pinnedFileIdentityRetained`, `preflightBeforeSwapPassed`,
`replacementIdentityMismatchDetected`,
`oldPinnedHandleIdentityRetained`,
`renameAfterPrecheckAllowedInDetachedDirectory`,
`maliciousOutsideUnaffected`, and `outsideSymlinkRejected`.

Both reported false for:
`finalRootNameAuthorized`,
`snapshotCheckIsAtomicWithRename`,
`hostileConcurrencyCovered`,
`productionPublisherAdopted`, and `powerCutTested`.

The experiment demonstrates:

1. Kernel/OS object identity on held handles **does** allow a bounded
   fresh comparison against a current trusted root-relative name.
2. Such a comparison is not an atomic authorization primitive.
   The subsequent rename can be valid relative to an old retained
   directory even when the installation has moved/replaced its name.
3. Fixing this by repeating the same preflight check immediately
   before rename does **not** eliminate the check-to-use race.
4. A publisher requires an explicit exclusive namespace ownership
   / fencing contract, or an OS-specific atomically constrained
   publication primitive with acceptable recovery. None are proven.

**Verdict:**
`T4C2B2B_NATIVE_IDENTITY_NESTED_CREATE_PASS /
PREFLIGHT_AUTHORITY_RACE_NO_GO / NO_OS_PUBLISHER_ADOPT`.

### Unproven/non-goals

No separate attacker account or parallel fuzzer; no root ACL/volume
security policy, Windows junction fallback, hardlink aliasing, mount
moves, inode/file-ID reuse, Windows API support guarantees on other
builds, target content authentication, full tree representation,
actual release installation, power-cut/IO durability, read leases,
retirement/GC or trusted monotonic update state. Full CI/Heavy/CodeQL
gate on the *final PR SHA* is separately required before merging.
No product `src/`, package, persisted CSM/CSP or public Patching API
changes are made; #184 and #251 remain OPEN.

## Next engineering decision

Stop adding preflight checks as an alleged way to certify authorization.
Move to an **exclusive publisher namespace/lock + atomically validated
commit contract** under a host-owned trust model, and independently
test reader snapshot pinning, real OS durability and failure ordering
(T4-D). If hostile privileged actors can rewrite namespace despite
the exclusive publisher boundary, classify protection as UNATTAINABLE
under that threat model until separate OS isolation is introduced.
