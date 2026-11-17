# PATCH-TREE-001 / T4-C2B2A — real pinned-parent native create and rename

**EvidenceId:** PATCH-TREE-001/EVIDENCE-20261020-002  
**ExperimentId:** PATCH-TREE-001, owner #184; research freeze #251  
**Date:** 2026-10-20  
**Status:** SCOPED HOST OBSERVATION / **NO_OS_PUBLISHER_ADOPT**  
**Frozen protocol:** [T4-C2B2A](../../benchmarks/PATCH-TREE-001-T4C2B2A-PINNED-STAGING-PROTOCOL.md)

## What was actually implemented and observed

PR [#395](https://github.com/definitely-stable/ChunkShift/pull/395)
tests no-clobber stage creation/rename relative to a **previously
opened, fixture-owned directory handle**. It is not a production tree
publisher. Exact original specialized Linux/Windows evidence:

- [GitHub-hosted run #39459399366](https://github.com/definitely-stable/ChunkShift/actions/runs/39459399366),
  experiment implementation commit `7d246bdc126a41b1c2f5de42ceca891ac8a6ca46`;
  **SUCCESS** on both independent OS jobs.
- Ubuntu 24.04.5 / .NET 10.0.13, native `openat2` +
  `renameat2(RENAME_NOREPLACE)`, job #118754597820,
  original evidence artifact **#12281374122**.
- Windows 2025 runner (Windows 10.0.26100) / .NET 10.0.13,
  native `NtCreateFile` + `NtSetInformationFile(FileRenameInformation)`,
  job #118754597374, original evidence artifact **#12281743372**.
- The per-host JSON and independent test-side Python checks have been
  reviewed in original Actions job logs, and artifact IDs listed by the
  GitHub Actions API. The artifact ZIP bytes have **not** been
  independently downloaded/hash-audited.

| Fixed native trial | Linux | Windows |
| --- | --- | --- |
| CREATE_NEW stage in held parent | PASS | PASS |
| Second CREATE_NEW same name denied | PASS | PASS |
| NOREPLACE rename onto existing target denied | PASS | PASS |
| Original existing destination preserved | PASS | PASS |
| Successful unique-name stage rename; stage consumed | PASS | PASS |
| Parent name moved, outside symlink substituted | YES | YES |
| Rename through held parent after name substitution | Only pinned original | Only pinned original |
| Outside sentinel/secret unchanged | YES | YES |
| New symlink path refused when reopened from root | YES | YES |

Observed finite verifier flags for both hosts:
`validStage=true`, `existingCreateRejected=true`,
`existingRenameRejected=true`,
`stageRemovedOnRename=true`, `parentNameReplaced=true`,
`publishedIntoPinnedOldDirectory=true`,
`maliciousOutsideUnaffected=true`,
`maliciousSymlinkRefused=true`,
`testedRaceInterleavings=1`.

**Mandatory negative scope flags** (both):
`pretrustedRoot=true`;
`writerAuthorization=false`;
`rootStillAuthorizedAfterMove=false`;
`supportsProductionPublisher=false`;
`powerCutDurability=false`.

## Important rejected Windows Win32 hypothesis

The initial commit used
`SetFileInformationByHandle(FileRenameInfo)` with non-NULL
`FILE_RENAME_INFO.RootDirectory` and single-component relative
destination. It built on Windows but failed actual host execution
with **Win32 error 87 (ERROR_INVALID_PARAMETER)**. This is retained
as a **real negative** result, not quietly repainted green.

Research then changed the Windows rename to direct
`NtSetInformationFile(FileRenameInformation=10)`, preserving
the directory handle as `RootDirectory`, `ReplaceIfExists=FALSE`
and the single relative target. The latter **passed** the same
fixed trial on Windows.

This observation agrees with externally documented independent
experience: [Microsoft Q&A, September 2026]
(https://learn.microsoft.com/ja-jp/answers/questions/6015074/setfileinformationbyhandle-filerenameinfo-rootdire)
and [FILE_RENAME_INFORMATION contract]
(https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information).
Those sources explain or document the distinction; the original
Windows runner log and independent checker are the evidence for
**our** environment. Direct ntdll dependence is not adopted as
a production ABI support promise.

## Safety verdict: controlled rename is not authorized publish

The experiment succeeded when the visible `anchor` was moved to
`detached` and the old `anchor` name was replaced with a link to
an outside directory. The held parent still selected the **moved
original directory**, not the outside attacker directory.

This is useful isolation from *path-name replacement*, but the
successfully renamed file may now lie outside the **currently
authorized installation namespace**. A production publisher must
prove the committed directory is still the authorized root (or own
an independently protected namespace/capability) and must resolve
full root/reader/writer fencing before serving the result. The
negative flag `rootStillAuthorizedAfterMove=false` exists for
this precise reason.

Additional untested or unproven cases include: a concurrent
privileged attacker thread while a stage is reopened; substitution
of the **source** stage filename between close and re-open; hardlink
aliasing, Windows ACL and native ABI changes, filesystem identity
and mount manipulation, nested directory construction, snapshot
lease/GC, replace-existing target semantics, true EIO/ENOSPC,
filesystem metadata flush, OS power cut and trust-policy signature.
No authenticated source/target content validation is performed in
this microfixture.

**Allowed acceptance only:**
`T4C2B2A_SCOPED_CREATE_NOREPLACE_LAB_PASS / NO_OS_PUBLISHER_ADOPT`,
conditional on final exact-head CI, CodeQL, Heavy validation, Benchmark
lab and dependency review. Keep #184 and #251 OPEN. No `src/`,
public API, frozen CSM/CSP or NuGet change.

**Next:** T4-C2B2B — retain a continuous stage handle (no
reopening through mutable names), secure relative nested creation
and parent-authority proof including mount/file identities and
concurrent attacker swaps. T4-D then durability, reader pins,
generation retirement and restart/power-cut checks.
