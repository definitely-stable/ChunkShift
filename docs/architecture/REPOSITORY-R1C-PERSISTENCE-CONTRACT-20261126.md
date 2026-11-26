# R1-C.2b.1 — private persistence preparation contract

Status: ACCEPTED_MODEL_ONLY in #501 (80 cases; exact head `7e087d8fe7f3fdbaf83c1f7ea038d360f7e5978f`, CI 45882185987, Heavy 45882185904; merged `a388faf0191c1549129c6f3e091b10617f3f7e29`). Refs #10 #133 #139 #144.
Predecessor [R1-C.2a](REPOSITORY-R1C-FLUSH-CAPABILITIES-20261126.md) accepted by #499
(merged `4170dc2f709eabaf2bc9400a75de9c612b9c94a6`, integration CI 45828255205, Heavy 45828254975,
flush 45828255120, 3/3 hosted lanes). See [RFC-0001 §12.6](RFC-0001-target-architecture-2026.md):
a published ref must not point at incomplete/non-durable objects.

## Boundary

PrivatePackCoordinator.Ready is a correctness baseline, not a persistence
receipt. Its writer calls ordinary FlushAsync and the coordinator closes,
independently reads and verifies the pack; existing cleanup can delete its
private objects. Do not silently change those semantics or reuse that cleanup
for ambiguous durable operations. No modifications to that coordinator or
writer in this slice.

The new internal one-shot PrivatePersistenceAttempt exercises an operation
seam against a deterministic model only, not a native backend. It rejects
Unqualified and unknown capabilities before any operation. ModelOnly is not
a qualified filesystem, platform, or durable receipt. No API for publication,
rename, delete, automatic retry, or catalog update is provided. No additional
dependencies, public API, persisted format, or default changes.

## Ordering and error semantics

1. CreateExclusive: establish ownership only on positive confirmation; an
   exception may leave an unknown path. Never infer ownership from existence.
2. WriteVerifiedObject: bounded writes, verify expected length and identity.
3. FlushFile: explicit file barrier, not just async buffer drain.
4. CloseWriter: successful close; error is not ignored.
5. VerifyClosedObject: independent full readback tied to expected descriptor;
   include closure of the read handle before return.
6. FlushParentDirectory: metadata barrier for the same trusted parent.
7. Final cancellation check; ModelPrepared only for completed modeled steps.

Each operation has an attempted and a completed marker. Throw the original
exception unchanged, including HRESULT or cancellation token. Failure or
cancellation before attempting create is RejectedBeforeCreate; after any
attempt it is conservatively ReconciliationRequired, even if the operation
may actually have completed. A fresh instance is necessary for any future
retry, but not sufficient authorization to reuse a path. No automatic delete
or adoption. The operation provider owns handle lifetime; concrete resource
disposal and its failures require explicit integration qualification. The
caller provides an exclusively owned, stable operations instance. A second
call (including while running) is rejected without another operation. Inspect
diagnostics only after the task settles. These in-memory states are not a
durable intent ledger.

## Fixed tests / next gate

36 cases: ordered model success and reuse rejection; unqualified/unknown
capability; pre-cancellation; 24 injected IOExceptions at six boundaries
(before/after effect, codes 5/28); cancellation after each of six effects;
invalid readback and concurrent duplicate call. Record calls and effects separately; assert exact
prefix, attempted/completed markers, terminal state, exception identity and
no continued operations. Codes are modeled EIO/ENOSPC, not native errors.
No timing or performance claim.

AUDIT-010 adds 44 cases (80 total): 24 suspended faults before/after effects,
12 suspended cancellations (cooperative vs ignored until completion), six
suspended successes with duplicate-call rejection, a backend aggregate
primary/cleanup exception, and a failing capability query. Task completion
gates force an actual incomplete await; no sleep or timing-based scheduling.
A cooperative cancellation watchdog bounds test failure only. A noncooperative
operation must settle before the attempt returns; completed markers describe
acknowledged operations, not effects or durability. A cancellation request
racing after the final cancellation check may lose to successful completion.
Aggregate exception tests verify pass-through only; actual cleanup and native
error propagation remain concrete-backend gates.

Original PR #501 head `3f411b00fdb16f2c7c681f2fb72015c55cc7fb23` passed CI
45878923721 and Heavy 45878923747. The expanded matrix requires a fresh
exact-head qualification; no claim of passing added tests before that run.

Before R1-C.2b.2, qualify AUDIT-002 handle-bound filesystem provenance, AUDIT-003 crash oracle and AUDIT-001 staging failure handling. Next concrete trusted-private-directory file backend, explicit
handle ownership and cleanup error reporting, actual file and directory
barriers with platform-specific capability policy. Linux and Windows native
observations do not alone qualify power-loss behavior. Model contract remains
separate from a real receipt; introduce qualified guarantees only against
documented filesystem scope and verified ordering evidence. Then actual
fault-injection and recovery boundaries, before R1-C.3 intent protocol.
Production default DEFER; no format freeze.
