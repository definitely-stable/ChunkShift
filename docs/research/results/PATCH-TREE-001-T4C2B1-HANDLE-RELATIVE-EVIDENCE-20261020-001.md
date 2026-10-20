# PATCH-TREE-001 / T4-C2B1 — real .NET handle-relative path observations

EvidenceId: `PATCH-TREE-001/EVIDENCE-20261020-001`  
ExperimentId: `PATCH-TREE-001`; owner [#184](https://github.com/definitely-stable/ChunkShift/issues/184);
research freeze [#251](https://github.com/definitely-stable/ChunkShift/issues/251).
Date: 2026-10-20.  
Protocol: [T4-C2B1](../../benchmarks/PATCH-TREE-001-T4C2B1-HANDLE-RELATIVE-PROTOCOL.md).
Classification: **BOUNDED HOST LAB OBSERVATION / NO_OS_PUBLISHER_ADOPT**.

## Exact measured experiment and provenance

[PR #394](https://github.com/definitely-stable/ChunkShift/pull/394)
implements a standalone zero-external-package .NET 10 executable using
real Linux `openat2` and Windows `NtCreateFile` handle-relative file
opening. The original, preregistered [Linux/Windows GitHub-hosted run
#39440872958](https://github.com/definitely-stable/ChunkShift/actions/runs/39440872958)
ran on implementation `main`-descended PR HEAD
`1ca20dba350497925df0f4e8b6471ad4f3982a57` and returned
**SUCCESS** on both hosts. .NET runtime was **10.0.13**.

- Ubuntu **24.04.5 LTS**, dedicated job **#118690035409**,
  original JSON artifact **#12274770689**
  (`patch-tree-t4c2b1-ubuntu-24.04`).
- Windows **10.0.26100** (GitHub `windows-2025` label),
  dedicated job **#118690035112**,
  original JSON artifact **#12274630777**
  (`patch-tree-t4c2b1-windows-2025`).

Verified original job logs include per-host runtime JSON and successful
independent Python JSON assertions. Artifacts are listed by the live
GitHub Actions API but the ZIP bytes have **not** been independently
downloaded, SHA-checked or audited in a second runtime. The initial
failed runs were lab compilation analyzer errors (`CA1305`,
`CA2020`, `CA1869)) and one independent Python checker error
assuming `OSDescription` must contain `Linux`; the actual Ubuntu
run returned `Ubuntu 24.04.5 LTS`. Those early failed runs are
preserved in GitHub history and are not reclassified as success.

## Independent fixed matrix (both OSes)

| Case | Ubuntu/openat2 | Windows/NtCreateFile |
| --- | --- | --- |
| Legitimate `safe/inner/payload.txt` via held dir handles | PASS | PASS |
| Nine malformed logical paths | 9 rejected | 9 rejected |
| Hostile intermediate symlink to outside `secret.txt` | Rejected | Rejected |
| Open symlink itself as directory via handle | Rejected | Rejected |
| Rename already opened directory; replace name by outside link | Performed | Performed |
| Reopen `payload.txt` through **old held directory handle** | Original `PINNED_SAFE` | Original `PINNED_SAFE` |
| Reopen hostile replacement by path | Refused | Refused |
| Read external secret through scoped paths | Not observed | Not observed |

Both runners reported `linkKind=symlink`,
`handlePinnedAfterSwap=true`, `renameBlocked=false`,
`pinObservation=POST_OPEN_SWAP_HANDLE_STAYS_PINNED`,
`bypassToOutsideObserved=false`. The Windows junction fallback was
*available in code* but not exercised: symlink creation succeeded.
No repeatedly randomized race schedule, foreign attacker process,
hardlink alias, privilege change, live file modification or remote
filesystem was tested. The swap in this lab is deterministic and
sequential after parent handle acquisition.

Linux opened a trusted starting directory with `O_NOFOLLOW` and each
nested component with `openat2(RESOLVE_BENEATH |
RESOLVE_NO_SYMLINKS | RESOLVE_NO_XDEV)`; Windows opened children
relative to retained directory HANDLEs using `NtCreateFile`,
`OBJ_DONT_REPARSE`, `FILE_OPEN_REPARSE_POINT` and an on-handle
reparse flag check. An untrusted name cannot simply be checked by
`GetFullPath` and then safely reopened by full path; this pilot
actually exercises handle-based resolution.

## Decision boundaries and negative claims

**Accept:** `T4C2B1_HANDLE_RELATIVE_READ_NOFOLLOW_OBSERVED` under
the explicit pretrusted root and the tested Linux/Windows host/fs
conditions. This demonstrates a viable direction for root-confined
reads, not a production security module or universal Windows NT
contract. **NO_OS_PUBLISHER_ADOPT.**

**Not tested / not guaranteed:**
- The starting root's own parent path/volume tamper resistance,
  root ACL/key authority, concurrent attacker with arbitrary write
  privilege, or cross-identity process access.
- Hardlinks, Windows reserved-name coverage beyond basic examples,
  device paths, junction fallback, casefold collisions, Unicode,
  mount namespace/volume changes, ADS outside the ASCII reject set,
  directory creation/renaming, symlink installation semantics or
  practical attacker capabilities against the staging root.
- Safe *write* via a relative handle, durable generation construction,
  metadata fsync, reader pins, GC, power interruption or actual EIO/ENOSPC.
- External signed release authorization, independently protected
  update-policy/witness and writer fencing (RFC-0006 host-owned).

This PR does not change source `src/`, public API, CSP/CSM, canonical
tree identity, default patch encoder or NuGet packages. #184/#251
remain OPEN.

## Next gate

T4-C2B2 should implement an independently reviewed **relative writer**
and creation/rename protocol on Linux and Windows under a *pretrusted
root handle*, with actual no-follow component traversal, safe temporary
file creation, file IDs, volume capability checks and hostile directory
replacement tested **during** creation. T4-D must then address
durability and crash recovery with externally approved release
identity, reader pins/retirement and real power-cut/IO error research.

Full PR CI/Heavy/Benchmark/CodeQL merge gate remains independent of
this successful specialized lab run and must be confirmed on final
commit; do not merge or label security ADOPT before that gate.
