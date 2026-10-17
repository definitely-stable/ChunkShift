# PATCH-TREE-001 / T4-C0 — original real-host filesystem feasibility

**Research status:** `HOST_EXPLORATORY_OBSERVATION_ONLY`; not production ADOPT,
not signed/update policy, not power-crash durability.

Experiment: [PR #379](https://github.com/definitely-stable/ChunkShift/pull/379).
Original initial probe: [run #39025561214](https://github.com/definitely-stable/ChunkShift/actions/runs/39025561214)
— Ubuntu SUCCESS / Windows **FAILURE**, caused by `PermissionError`
on the default Windows reader attempting to open `active` during
`os.replace`. This failure is a real observed compatibility limitation,
not a corrupted or torn head, and has **not** been retroactively relabeled.

**Amended source-locked comparison:** [run #39025711377](https://github.com/definitely-stable/ChunkShift/actions/runs/39025711377)
— both host jobs SUCCESS. It adds a separately tagged Windows
`CreateFileW(FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE)` reader
and tracks genuine default reader access denials and publisher-side
permission retries rather than ignoring or "correcting" them. This is
a different, explicitly amended comparison and does not negate the
first Windows failure.

The original two independently downloaded workflow artifacts were checked
as valid ZIPs, each containing one JSON file under the frozen
`chunkshift.patch-tree-t4c0-fs-probe.v0` schema.

| Evidence | Hosted Ubuntu 24.04 | Hosted Windows 2025 |
| --- | --- | --- |
| Actions artifact | `12098945314` | `12098188474` |
| ZIP SHA256 | `aaf768a1e2abb0fe729d034477bca27f2d378b43dd1ddec4179dae5c0d19dbf4` | `5427e562529ec90de5fa1ed1fa1c0c4bb3161341c0938797c3c28c99845fda86` |
| Host / Python | Linux 6.17.0-1022-azure, CPython 3.11.17 | Windows 10.0.26100, CPython 3.11.9 |
| Default `os.replace` publications | 200 | 200 |
| Default successful reader snapshots | 2,186 (556+542+554+534) | 4,688 (1174+1184+1172+1158) |
| **Default reader access denials** | **0** | **125** (27+22+38+38) |
| Default publisher `PermissionError` retries | 0 | **129** |
| Bad/torn decoded pointer or mixed immutable data | 0 observed | 0 observed |
| Old generation open handle changed | No | No |
| Parent-directory fsync after replacement | 200 POSIX calls succeeded | 200 explicitly **not attempted** |

The second Windows-only lane, under `CreateFileW` with explicit
`FILE_SHARE_DELETE`, observed **200 publications**, 3,855 successful
reads (974+957+956+968), **zero reader access denials**, **55 publisher
permission retries**, zero bad pointer/content observations and the old
open generation handle intact. These retries remain an unresolved
publishing-continuity issue; no claim of wait-free/always-successful
replacement follows from this result.

Both OSes passed two real **separate-process terminate** barriers:
kill before the replace left `active` pointing to the old verified
generation; kill after the replace left `active` pointing to the new
verified generation. Exit codes: Linux `-9` (SIGKILL), Windows `1`
(TerminateProcess). Old and new immutable generations stayed present
and individually verifiable. Six negative cases (malformed/truncated
pointer, corrupted generation) were rejected on each OS.

## Interpretation: bounded hypothesis, not a release gate

1. On this Linux GitHub runner, temporary same-directory replacement
   under the controlled test produced no observed read gaps, no publisher
   retries and 200 successful parent `fsync` calls. It does **not**
   prove power-cut durability (storage may lie), general linearizability,
   hostile filesystem security or other filesystem support.
2. On this Windows GitHub runner, **default Python reader continuity
   failed** even though every successfully observed pointer was complete.
   Sharing flags are material to this design. Explicit `FILE_SHARE_DELETE`
   eliminated observed reader denials **only in this one host run**;
   publisher-side retries remained. Cross-process behavior and antivirus/
   filesystem sharing cannot be inferred from this limited control.
3. No test used .NET `File.Move(overwrite:true)`, `File.Replace` or a
   selected Win32/Linux production publication primitive. `os.replace`
   is the only actual publishing call. Source/destination are siblings
   on one filesystem; cross-volume, network filesystem, reparse/symlink
   race, ACL, hostile directory traversal, mutable generation safety,
   full installed tree integrity, enforced anti-rollback and signatures
   all remain **UNTESTED**.
4. No interruption deliberately cut power at arbitrary instructions,
   failed flush/write with injected errno, or simulated torn storage.
   Surviving process-kill probes are **not** power-fail recovery proof.
   The test head is checksum-bound, **not signed or authenticated**.

**Result:** `LAB_POSIX_FEASIBLE_SAMPLED` and
`WIN32_DEFAULT_READER_SHARING_BLOCKER` with
`WIN32_SHARE_DELETE_CONTROL_IMPROVED_READ_AVAILABILITY`.
This neither advances Patching Research Freeze #251 nor closes #184.

**Next preregistered slice:** T4-C1 should compare actual .NET
`FileStream` sharing modes and OS-native pointer publication primitives
on GitHub-hosted Windows/Linux, with a deliberately authenticated #185
policy boundary, separate safe-handle/parent-dir fsync adapters and
explicit per-platform failure diagnosis. Do **not** attempt production
atomic tree publication or publish a tree-update NuGet API from T4-C0.
