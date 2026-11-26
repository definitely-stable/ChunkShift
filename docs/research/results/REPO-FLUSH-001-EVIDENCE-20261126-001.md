# REPO-FLUSH-001 — independently reviewed platform capability evidence

EvidenceId: REPO-FLUSH-001/EVIDENCE-20261126-001. Refs #10 #133 #139 #144, PR #499.
Measured head: `c25a8a6b52dee85ee909ed11849152da66b0e5e0`. [Run 45817064617 attempt 1](https://github.com/definitely-stable/ChunkShift/actions/runs/45817064617).
Disposition: CAPABILITY_ONLY; eligible for R1-C.2a acceptance after integration gates.
Production default DEFER; crash ordering UNQUALIFIED. No durability adoption.
Prior [run 45800818127](https://github.com/definitely-stable/ChunkShift/actions/runs/45800818127) failed on both Linux lanes because CPython rejected negative fd before libc. Corrected native ctypes fsync(-1) with exact -1/EBADF check; no changed gate.

## Original artifacts and independent audit

| Lane | Artifact | Outer ZIP SHA256 |
|---|---|---|
| Windows x64 | [15115513665](https://github.com/definitely-stable/ChunkShift/actions/runs/45817064617/artifacts/15115513665) | 1955c310182eefda30e147a7ffdbf1c2832ed7e571afbe662cd4916b909ff826 |
| Linux x64 | [15115478704](https://github.com/definitely-stable/ChunkShift/actions/runs/45817064617/artifacts/15115478704) | 98c3b20c4ba0cf70d9bd9b5cd1cc3c6a9d2bdc0231c0f9c33713079eaf82a815 |
| Linux ARM64 | [15115188496](https://github.com/definitely-stable/ChunkShift/actions/runs/45817064617/artifacts/15115188496) | b7af61808b76ccdc82a95df767de7905f8688fbe562ce795a9365790e6d92e87 |

Downloaded all three original ZIPs independently. Outer SHA256 matches Actions artifact metadata; ZIP CRC clean, exactly seven unique files per ZIP, all six internal file hashes per lane (18/18), exact manifest head/run/attempt, managed.stdout equals summary.managed, empty managed.stderr, observations.jsonl equals summary.observations (19 observations total), expected stage order, results and native error values, fixture-empty and disposition assertions pass.
Independently regenerated 65,536-byte fixture (byte i mod 251), SHA256
`4b640d85ab3ba30fd02c9fc9db4a8928f416322ad27022ea58a65aaee68a4df2`.
Matches managed and manifest records on all lanes. Actual fixture bytes were intentionally deleted by the experiment; audit verified the retained hashes, not a second physical read of removed runner files.

## Observed capabilities (not ordering proof)

SDK 10.0.404 / runtime .NET 10.0.15 on all lanes.
Windows image 20261116.315.1, Linux x64 20261116.133.1, ARM64 20261116.188.1.
AUDIT-002 caveat: filesystem names below are the root DriveInfo labels, not
verified mount/volume identity of the fixture. Original native return codes
and hashes remain valid; filesystem-specific attribution is unqualified until
a handle-bound rerun is accepted. Do not retroactively relabel these results.

| Observation | Windows x64 (root: NTFS) | Linux x64 (root: ext4) | Linux ARM64 (root: ext4) |
|---|---|---|---|
| Managed Flush(true), byte identity | PASS | PASS | PASS |
| Exclusive CreateNew collision HResult | -2147024816 | 17 | 17 |
| Native regular file flush | success / 0 | success / 0 | success / 0 |
| Invalid native handle/fd | ERROR_INVALID_HANDLE / 6 | EBADF / 9 | EBADF / 9 |
| Read-only handle flush | ERROR_ACCESS_DENIED / 5 | not tested | not tested |
| Native ENOSPC | not tested | /dev/full / 28 | /dev/full / 28 |
| Directory flush after create | success / 0 | success / 0 | success / 0 |
| Directory flush after rename | success / 0 | success / 0 | success / 0 |
| Directory flush after delete | success / 0 | success / 0 | success / 0 |

Measured head workflow gates: CI 45817064354, Heavy 45817064486, CodeQL 45817064358, process-crash 45817064517, R0 model 45817064388, and other applicable non-skipped workflows all PASS. These are measured-head checks, not acceptance of a later integrated commit.

## Boundaries and next step

Directory flush success on NTFS is not a documented POSIX-equivalent crash-ordering guarantee. No native EIO, delayed writeback ENOSPC, host crash, power interruption, transaction publication, or storage backend qualification. Linux /dev/full is immediate ENOSPC negative control only. Do not promote production defaults or freeze Pack v1. R1-C.2b must separate platform capability from ordering contract, propagate storage errors and unknown outcomes, and require fail-closed fault-injection coverage before durable intent work. CSP corrective PR #500 accepted at `ce1c71a6cfc7b3d9ffefde02c1190c901d27fb55` (Heavy 45819767238; both architectures, seed 22058, zero differential mismatches). Registry reconciliation and exact integrated-head gates subsequently passed and #499 was merged. AUDIT-002 corrects provenance separately.
