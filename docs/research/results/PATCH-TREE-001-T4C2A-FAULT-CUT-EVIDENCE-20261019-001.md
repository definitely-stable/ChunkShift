# PATCH-TREE-001 / T4-C2A — .NET scoped publication-fault evidence

EvidenceId: `PATCH-TREE-001/EVIDENCE-20261019-001`  
ExperimentId: `PATCH-TREE-001` · Issue: #184 (freeze #251).  
Date: 2026-10-19.  
Protocol: [T4-C2A](../../benchmarks/PATCH-TREE-001-T4C2A-FAULT-BOUNDARIES-PROTOCOL.md).  
Classification: **HOST-OBSERVATION LAB PASS; NO PRODUCTION ADOPT**.
Final PR acceptance depends on all final-head project gates.

## Provenance and limitations

Pre-registered implementation source HEAD:
`a4a8b35219fc0f20f6cabd011dab659ec4c988cf` in
[PR #392](https://github.com/definitely-stable/ChunkShift/pull/392).
Specialized [GitHub-hosted run #39210048048](https://github.com/definitely-stable/ChunkShift/actions/runs/39210048048)
completed **SUCCESS** for independent Ubuntu 24.04 and Windows 2025
jobs. Original job logs include the emitted per-host JSON and
the separate Python stdlib assertion script. C# runtime reported
**.NET 10.0.13** on both; Linux OS Ubuntu 24.04.5; Windows OS
Microsoft Windows 10.0.26100.

Retained per-host diagnostic CI artifacts:
- Ubuntu `patch-tree-t4c2a-ubuntu-24.04`, artifact **#12166887298**.
- Windows `patch-tree-t4c2a-windows-2025`, artifact **#12167257528**.

No independent ZIP download/byte SHA audit has yet been performed for
these artifacts; the verified fact is the original job log success,
the JSON emitted within each job and its fail-closed independent
assertions. No raw file-by-file disk trace or original corpus data
is included. The two initial failed CI iterations were **source
syntax errors** (a literal escaped newline in C#), not filesystem
failures; they remain historically unsuccessful and are not relabelled.

## Fixed failure-cut outcomes (both hosts)

| Injected cut | Verified visible head | Stage after cut |
| --- | --- | --- |
| BeforeStage | OLD | absent |
| PartialStage (40/88 bytes) | OLD | incomplete stage |
| AfterStageFlush | OLD | complete stage |
| CorruptStage (16 bytes) | OLD | corrupt stage rejected |
| BeforeMove | OLD | complete stage |
| AfterMoveBeforeSync | NEW | consumed by rename |
| ParentSyncFailure (injected) | NEW | consumed by rename |
| AfterSyncBeforeAck | NEW | consumed by rename |
| Complete | NEW | consumed by rename |

The independent check verifies all nine cut labels and old/new
outcomes, re-opens and validates the pointer SHA256/length/epoch/sequence,
re-hashes the referred generation, verifies old/new generations
retained, and checks stages. All assertions passed on both hosts.
Four preflight negatives (three external/cross-parent path attempts
and one altered target generation) were rejected before promotion;
these are NOT hardened path-race controls.

A separate child process holding a `FileShare.None` lock prevented a
second cooperative process from taking the same lock, then permitted
reacquisition after termination, on both tested hosts. This is NOT a
hostile-writer security fence or substitute for a protected witness.

Linux attempts a directory `fsync` syscall after completed rename;
Windows explicitly does not attempt directory sync. A returned
`fsync` result cannot certify hardware power-cut durability. The
fixture never tests true ENOSPC, EIO, losing kernel writes or
hostile symlink/reparse races.

**Most important observable fact:** when the rename has succeeded,
post-rename failures (including failed parent sync or lost
acknowledgment) leave **NEW** as the observed active generation,
with durability pending. Recovery must verify the actual active
generation and external host policy; silently treating such a
failure as an uncommitted OLD state is unsafe.

## Decision and next prerequisites

`T4C2A_SCOPED_DOTNET_FAILURE_CUTS_PASS` for bounded Linux/Windows
host observations only; `NO_OS_PUBLISHER_ADOPT`.
No changes to product SDK, Core, CSP/CSM v1, public API, tree
manifest or signer. #184 and #251 remain OPEN.

Next T4-C2B: real no-follow handle-relative file opens and path
traversal with explicit Linux/Windows capability gates, including
adversarial symlink/junction swaps. T4-D then tests OS-specific
staged generation durable construction, real IO error/power-cut
protocols, reader pins/retirement and host-owned trust/fencing.
No release gate is unblocked by this evidence.
