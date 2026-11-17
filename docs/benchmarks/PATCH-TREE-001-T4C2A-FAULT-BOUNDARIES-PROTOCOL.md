# PATCH-TREE-001 / T4-C2A — real .NET filesystem failure-cut protocol

Status: **PREREGISTERED LAB / NO_ADOPT**. Owner #184; depends on
T4-A (#377), T4-B (#378), T4-C0 (#379), T4-C1 (#380) and detached
trust boundary RFC-0006 (#185 closed). Research freeze #251 remains OPEN.

## Scope and decision

T4-C1 measured real `File.Move(overwrite:true)` and Windows sharing
behavior but lacked interrupted write/sync boundaries. T4-C2A selects
**bounded deliberate I/O failure injection** in a disposable .NET 10
publisher. Do not promote an OS-specific filesystem API, persisted
pointer format, tree schema, trust/witness or public `ChunkShift.Patching`
API from this experiment.

The original T4-C1 observations (400 promotions/OS; 200 per sharing lane):
Ubuntu 0 sampled denied reads / 0 retries; Windows `FileShare.Read`
327 denied reads / 29 writer retries; explicit `Read|Write|Delete`
0 sampled denied reads / 23 retries. There were zero observed mixed or
torn heads, but neither power loss nor real ENOSPC was tested.
See [T4-C1 evidence](../research/results/PATCH-TREE-001-T4C1-DOTNET-FS-20261017-001.md).

## Exact experimental implementation

One standalone zero-NuGet-dependency console binary under
`benchmarks/PatchTreeT4C2Probe` executes only in a freshly allocated
`Path.GetTempPath()/chunkshift-t4c2-<guid>` directory on each host. It
creates two small immutable generations, with one SHA256-named content
file each. These **are not T4-A tree-manifest generations** or a real
update-set. The 88-byte `active` pointer reuses the exact T4-C0/C1
checksum vector and ephemeral encoding; its SHA256 is not an
authentication signature. Target bytes are rehashed before each
publication.

The writer creates the stage in the *same owned directory* as
`active` via `FileMode.CreateNew`; flushes staged bytes with
`FileStream.Flush(flushToDisk:true)`; re-parses full 88-byte stage and
revalidates old head; then calls exactly one
`File.Move(stage, active, overwrite:true)`. The Linux host afterwards
attempts `open(O_DIRECTORY)` + `fsync(dirfd)`; Windows records
`NOT_ATTEMPTED`, since no validated Windows directory durability
primitive is implemented. Failure of directory sync **after** the
replace cannot be reported as an uncommitted rename or rolled back
blindly: a new generation may be visible, but durability remains
unqualified.

A separate `publisher.lock` `FileStream(FileShare.None)` is held
over the entire operation. A second independent .NET process attempts
the same lock and must fail, then re-acquire after the first process
is killed. This validates **this host's** cooperative file-sharing
behavior only; it is not writer authorization, hardened fencing or an
independently protected monotonic witness.

## Frozen deterministic fault matrix

| Cut | Injection / actual operations | Expected verified visible state |
| --- | --- | --- |
| `BeforeStage` | fail before `CreateNew` | OLD, no stage |
| `PartialStage` | write first 40/88 bytes and throw before flush | OLD, incomplete stage |
| `AfterStageFlush` | complete stage/Flush(true), then throw | OLD, complete stage |
| `CorruptStage` | corrupt stage, re-read and reject before rename | OLD, corrupt stage |
| `BeforeMove` | exact valid stage but throw before rename | OLD, complete stage |
| `AfterMoveBeforeSync` | successful rename, then throw | NEW, sync not reached |
| `ParentSyncFailure` | successful rename; simulate unavailable/error before parent fsync | NEW, sync error |
| `AfterSyncBeforeAck` | rename + directory sync attempt; lose return/ack | NEW, ambiguous caller outcome |
| `Complete` | rename + directory sync attempt + return | NEW, capability-specific recorded result |

For **each cut** verify active by opening with
`FileShare.Read|Write|Delete`, exact length/format/checksum,
then hash-check the named generation from actual file bytes.
Check both old and new generations still exist and remain intact.
Check presence of orphan stage precisely. No corrupted pointer or
wrong generation may be reported as accepted. All cut positions and
expected visible identities are chosen *before the experiment*.

Negative controls: attempted cross-parent/out-of-root stage/active
path rejection before any mutation, tampered target-generation bytes
rejected, no unexpected active update. These are **textual
preflights only**, and do **not** defeat malicious path substitution
after validation, junction traversal, ACL changes or symlink races.
A future hardened handle-relative file walker must be proved
separately, including Unix `openat2`/safe component walk and a
Windows reparse-safe handle model.

## Acceptance and limits

GitHub-hosted Ubuntu 24.04 and Windows 2025, pinned `global.json`
.NET 10 build warnings-as-errors; always upload per-host evidence JSON
from `runner.temp`, including diagnostic failures. A Python stdlib
independent checker asserts nine cuts, precise OLD/NEW results,
stage retention, both generations retained, four negative controls,
competing process lock and explicit `NO_SECURITY_OR_DURABILITY_ADOPT`.
General CI, CodeQL, Heavy validation and Benchmark lab must pass on
exact final PR HEAD before merge.

Allowed final verdict after successful audit:
`T4C2A_SCOPED_DOTNET_FAILURE_CUTS_PASS / NO_OS_PUBLISHER_ADOPT`.
Failures, malformed heads, denied lock or missing generations are
`HOST_BLOCKER_UNVERIFIED`; never silently convert a failure into
successful recovery. A passing test makes **no** claims about:
- true ENOSPC, EIO or disk-controller cache flush behavior;
- sudden OS power removal, filesystem/journal ordering or Windows
  directory fsync (explicitly not attempted);
- symlink/reparse races, escape from hostile installation roots,
  Unicode/ACL/hardlinks/mounts/cross-volume or network filesystems;
- hostile writer identity, signed tree authenticity, protected installed
  rollback state, reader snapshot leases, GC or production availability;
- OS-wide atomicity of .NET `File.Move` beyond bounded runner samples.

## Next gate

T4-C2B must replace preflight string validation with a real
handle-relative no-follow traversal and staged-writer API on *each*
target OS, prove privilege/policy requirements and record unsupported
volumes/FS behavior fail-closed. T4-D must then resolve real OS metadata
durability, ENOSPC/IO failures, reader leases/GC, authenticated host-owned
publisher/fencing and power-cut testing where a suitable controlled
platform exists. #184 and #251 remain OPEN.
