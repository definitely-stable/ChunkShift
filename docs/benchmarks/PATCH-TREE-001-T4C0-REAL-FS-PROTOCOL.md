# PATCH-TREE-001 / T4-C0 — real host filesystem pointer feasibility

**Owner:** #184, after merged T4-A (#377) and T4-B (#378).
**Related boundary:** #185 detached trust/update policy (OPEN), #251 research freeze.
**Status:** preregistered, **research-only**; NO_ADOPT; no production filesystem
publisher, no trusted update manifest, no accepted wire format.

## Motivation, prerequisite and exact question

F2 original physical warm sizes on the four frozen .NET transitions:
T0 14,758,982 B; T1 83,593,822 B; F2-local 47,002,363 B.
T0 remains the size baseline. T4-A established a restricted candidate tree
identity; T4-B's immutable publication model assumed its active generation and
epoch/sequence cursor change in one atomic operation, but never touched a disk.

The full T4-C platform publication adapter cannot be accepted before #185
defines who may sign a tree identity and how anti-rollback/recovery policy is
bound. T4-C0 therefore answers a **narrower independent feasibility question**:
on GitHub-hosted Windows and Ubuntu, what is *observed* when a short sibling
file `active` is replaced with `os.replace` while other threads continuously
open and check that file, and when a **separate process** is forcibly killed
just before and after that replacement?

This is **Python CPython 3.11's `os.replace` probe**. It is not a direct
measurement of .NET `File.Move(overwrite:true)`, `File.Replace`, Windows
`ReplaceFileW`, Linux `renameat2`, or an SDK API. Platform/FS-specific
behavior, kernel capabilities and permissions can differ. The next study
must test the selected .NET/OS primitive itself.

## Test setup — no destructive operations outside the harness

In each run, `tempfile.TemporaryDirectory` creates an owned work area
on the runner's filesystem. Two immutable generation directories, each with
two small different files, are written using an exclusive file-create,
flush and `os.fsync(file_fd)`. Their synthetic test identity is a
domain-separated SHA256 over both complete test files, independently re-read
before every promotion. **This is not T4-A TreeCandidateId or CSM ManifestId.**

An `active` regular file holds `T4C0v0!\n` (8 bytes),
little-endian UInt64 epoch, little-endian UInt64 sequence, raw 32-byte
generation hash and SHA256 over the preceding 56 bytes (88 bytes total).
This is **temporary test framing only**, not a persistent/releasable
signed format. No signature keys or policy verification are simulated;
a checksum is not authenticity. The cursor+generation tuple fits in one
file replacement, not an independently coordinated pair of files.

Each prepared selector `.next-N` is an exclusively created, flushed
**sibling file on the same filesystem**. `os.replace(stage, active)` is
the only selector publication mutation. There is **no copy-overwrite**,
file-tree rename, mutable generation rewrite or multi-file publish sequence.
On Windows, transient access-denied during concurrent read/replacement may
cause bounded retries; all retries are counted and **not represented as one
uncontended atomic success**. Persistent failure aborts the workflow.

Linux: after each observed successful replace, attempt
`fsync(open(parent_directory,O_DIRECTORY))` and log success or errno.
Windows: record directory fsync as **NOT_ATTEMPTED**, since this Python
probe has no portable directory-handle durability primitive. A successful
directory fsync system call is **not evidence of power-cut durability**
or stable cloud-hosted block-device write ordering.

## Preregistered experiment matrix

| Probe | Setup | PASS condition | What is *not* proven |
| --- | --- | --- | --- |
| Negative pointer | alter magic, size, epoch/seq/hash/checksum | 5 malformed pointers + 1 tampered generation rejected; active stays old | Signature, anti-rollback, hostile path checks |
| Concurrent readers | 4 threads, 200 alternating publish attempts; keep open handle from old generation | every reader samples nonzero; each observed head is complete and refers to correct immutable files; all 200 commits succeed; pinned old open descriptor still reads the same bytes; no torn/gap/mixed observations | Linearizability for all schedules, same semantics for other filesystems |
| Kill **before** | separate child creates synced staged selector, writes barrier marker **before** replace, parent terminates child | active head remains old, staged file remains non-published, old and new generations retained | Kill at instruction-granularity race, kernel failure, power interruption |
| Kill **after** | child creates stage, `os.replace`, attempts parent fsync on POSIX, writes barrier marker **after** replacement, parent terminates child | new full head is visible, old+new data intact | Durability across unexpected power loss |
| Host capability record | independent per-host JSON artifact | record Python, OS, 200 per-commit directory-sync results, Windows replace permission retries and reader samples | Generalization to production install permissions or remote/storage-backed volumes |

The barriers deliberately select only **two deterministic process-kill
points**, not an instruction-level crash sweep. There is no physically
simulated sudden power removal, ENOSPC/fsync error injection, sharing-denial
fuzzer, symlink/junction race, hard link, ACL, directory replacement, NFS/SMB
or volume-boundary transfer. A killed worker after the marker may already
have made data visible; a killed worker before the marker has not been
authorized to call replace by program order.

## Evidence and acceptance classification

A dedicated PR-only `patch-tree-001-t4c0-real-fs-probe.yml` runs on
GitHub-hosted `ubuntu-24.04` and `windows-2025`. CPython 3.11 is pinned
in setup-python. Output is one immutable workflow artifact per OS with
the original machine-observed counts and capabilities, no file contents
or secrets. All negative cases and boundaries are fail-closed.
No holdout/patched original data is involved.

Interpretation:
- `OBSERVED_FEASIBLE_HOSTED_FS_ONLY`: 200 replacements with zero observed
  bad pointers, both kill barriers, negative tests and pinned old handle
  pass on that exact host; not a platform-wide atomicity theorem.
- `HOST_BLOCKER`: any observed missing/torn head, inconsistent generation,
  persistent replace denial, failed barrier or wrong selector state.
  Diagnose the exact job and kernel/OS facts, no production fallback.
- Unsupported directory fsync on Windows and lack of power-cut tests remain
  **UNRESOLVED** even when functional tests pass.

Only after host evidence passes may a separately preregistered T4-C1 probe
use a real .NET/OS adapter with durable-writer protocol and deliberate
forced failures, per-OS capability gates, same-volume enforceability,
trusted #185 detached policy and full path/link metadata rules. Its
publication contract must keep both generation and anti-rollback cursor
authenticated and durable as one checkable recovery unit; `active` without
a protected trust layer is susceptible to malicious replacement/rollback.

**No production API/default, tree-format freeze, package release, issue
closure or ADOPT is authorized by this T4-C0 experiment.**
