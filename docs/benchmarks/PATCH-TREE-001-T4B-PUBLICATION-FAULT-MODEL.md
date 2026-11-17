# PATCH-TREE-001 / T4-B: crash-boundary state model and generation pinning

Owner: issue #184; parent research freeze #251; trust boundary #185. This is
a continuation of [T4-A](PATCH-TREE-001-T4A-TREE-MANIFEST-PROTOCOL.md)
after [F2 NO_ADOPT](../research/results/PATCH-TREE-001-F2-ORIGINAL-EVIDENCE-20261016-001.md).

**Status: LAB-ONLY ABSTRACT MODEL — NO OS ATOMICITY OR DURABILITY CLAIM.**
No CSP/CSM v1 bytes, production package, public API or filesystem content
are modified. No network access, sealed corpus or OS-level publication occurs.

## Why this is the next slice

F2 locality reduced naive whole-tree T1 from 83,593,822 B to 47,002,363 B
on the frozen original four-pair .NET calibration but remained 3.185× larger
than same-path T0 at 14,758,982 B. Therefore the minimal update-set
architecture should retain same-path independent patches and solve
whole-tree *identity, authorization, reader consistency and publication*
in the distribution layer, with T0 the explicit baseline.

T4-A identified a conservative candidate tree digest but not a publisher.
T4-B formalizes the **logical state transition contract** without silently
assuming that platform file APIs supply atomic or power-durable semantics.

## Pure transition machine implemented

Located in `benchmarks/ChunkShift.Benchmarks/PatchLab/Tree/PatchTreePublicationModel.cs`.
State is a C# immutable record with immutable generation and reader-pin maps;
each transition returns a **new** state. No files or pointers are written.

| Transition | Preconditions and effects |
| --- | --- |
| `Initialize` | Installed, externally trusted tree identity, accepted epoch/sequence, one complete/approved active generation |
| `Prepare` | Exactly one writer (Idle only); current base identity match; target != active/not already retained; strictly newer epoch/sequence |
| `Stage` | T4-A manifest object with bounded canonical bytes, expected SHA256 and entry count; external all-content-materialized evidence; add **not yet verified** private candidate |
| `Verify` | Separate external positive oracles for actual content bytes, confined links and metadata; only then mark generation complete |
| `Authorize` | External policy signature oracle plus exact (base,target,epoch,sequence), not-before and expiry checks; only then mark policy-approved |
| `CommitPointer` | One **abstract atomic** transition changes active generation **and** accepted epoch/sequence together; old immutable generation retained |
| `Retire` | Postcommit remove only generations not referenced by any pinned reader; release exclusive writer |
| `Recover` | Precommit: discard private pending candidate and preserve old active pointer/policy cursor. Postcommit: preserve new active pointer/cursor and pinned old generations. Idempotent in the model |
| `Pin` / `ReadPinnedGeneration` / `Release` | Reader sees a single generation for its entire session; even after a commit, never re-resolves mutable active pointer. Retirement requires all pins released |

The stage-completion, byte-integrity, symlink-containment and
signature-verification flags are **injected laboratory oracle decisions**,
not filesystem verification or cryptography. They must never be accepted
from an untrusted client in a future production implementation. The
T4-A manifest is a research candidate; it is neither a CSM ManifestId
nor a production-compatible tree format.

No arbitrary direct field mutation or disk serialization of model state
is in scope. The accepted policy cursor and the active pointer move in
one pure transition **by assumption**, which is precisely the property
that a future OS-qualified real publisher must justify.

## Explicit invariants and rejected transitions

- All active generations must be complete and policy-approved.
  A prepared/staged/verified tree is **never** visible via Pin.
- Every pinned reader token resolves to the same retained generation
  across commits, crash/recovery and disposal of unpinned generations.
  Old snapshots must not disappear while a reader uses them.
- A writer holds the lock across preparation, verification, policy
  approval and commit/retirement. Concurrent Begin/Commit and out-of-order
  lifecycle calls reject. Commit is not permitted before approval.
- The active tree and accepted epoch/sequence change together only at
  CommitPointer. A rejected or interrupted precommit publication must
  not advance the cursor; recovery cannot invent a new active identity.
- Wrong base tree, reusing an existing generation, stale or equal
  epoch/sequence, negative epoch/sequence, unexpected manifest digest,
  incomplete materialization, byte mismatch, unsafe symlinks, missing
  metadata, false signature oracle, wrong target or expired/future
  policy must fail closed.
- A later epoch may begin a new monotonically scoped sequence; the
  signature oracle and external trust policy remain responsible for the
  validity of that epoch transition. This model is **not** an anti-rollback
  security mechanism by itself.

## Fault-injection coverage and completeness limit

The T4-B test suite enumerates publisher crash *before* the first step
and after each of six transition boundaries:
`PREPARE`, `STAGE`, `VERIFY`, `AUTHORIZE`,
`COMMIT_POINTER`, `RETIRE`. For every cutoff, it pins the old tree
before publication, recovers twice, takes a second pin and checks:
(a) the authoritative generation is old before commit, new at/after commit;
(b) both pins continue resolving to the exact generation they captured;
(c) after releasing pins, all unreachable generations retire and the
active generation remains. Tests additionally cover two consecutive
commits with pins spanning both boundaries; forced replay and base
mismatch; concurrent writer exclusion; illegal transition order; bad
materialization/content/symlink/metadata oracle outcomes; wrong signature,
scope, sequence, expiry and activation time; forged/stale reader tokens;
and fresh retries after a precommit crash.

**The fault matrix is exhaustive only for the enumerated logical
boundaries**, *not* all multithreaded interleavings, OS storage failure
modes, process kill points, reparse races or partial disk writes. The
record is a state-machine specification for a future operating-system
adapter and has no atomicity, crash consistency or security guarantees
outside the defined abstract assumptions.

## Next step: T4-C host filesystem feasibility gate

Write a separate, explicit OS adapter RFC/prototype only after #185's
authenticity and update-policy semantics are decided. On GitHub-hosted
Windows and Linux, preregister platform-specific atomic pointer selection
primitives, mount/volume constraints, handle-relative no-follow path
walk, ACL/mode propagation, directory flushing, crash+process-kill
matrix, forced file-write failure and old-generation retention. Validate
Unicode normalization, case collisions, link/reparse behavior, full
POSIX metadata as a separate compatibility concern; T4-A's ASCII
subset is not representative of arbitrary production trees.

A pair of `Directory.Move` operations or sequential file renames is
**not** a portable atomic tree swap. The abstract atomic commit is a
verification target, not a claim about `MoveFileEx` or `renameat2`.

## Acceptance

GitHub-hosted Linux and Windows targeted model tests, build and all
repo-required CI, CodeQL, Benchmark lab and Heavy validation. Failure
must stop at PR and no `Production ADOPT` or research-freeze closure.
