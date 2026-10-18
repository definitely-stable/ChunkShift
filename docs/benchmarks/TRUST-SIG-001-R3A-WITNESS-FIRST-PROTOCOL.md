# TRUST-SIG-001 / R3-A: protected witness-first publication and recovery

Owner: #185; dependencies #184, #251; previous research T0, R1, R2A/R2B1/R2B2/R2C1.
Status: **PRE-REGISTERED LAB LOGICAL CONTRACT / NO SECURITY OR PRODUCTION ADOPT**.
ExperimentId: `TRUST-SIG-001`. This slice is not a new format, algorithm or
versioned on-disk witness implementation.

## Research question and observed prerequisite

R2-B2 used a real TUF Updater and demonstrated that replaying authentic old
metadata succeeds if the attacker first rolls back the updater's locally cached
trusted version. TUF is not at fault: the attack assumes control of trusted
local state that normally should not be rewritable by the attacker.

R1 already models exact root/cursor equality and rejects unanchored future
states, but does not resolve the **crash gap** between an externally protected
cursor write and a mutable filesystem selector. T4-B assumes the cursor and
pointer change in one abstract atomic step; T4-C1 did not establish such an
OS primitive. R3-A tests the narrower construction **witness-first**, so
the local pointer can lag the protected floor but never legitimately lead it.

## Trust and attacker boundaries

The model takes a checkpoint that is independently authenticated and
rollback-resistant **outside the mutable installation tree**. This is an
explicit unimplemented assumption; no same-disk JSON, dual-write or hash
can supply it against a local rollback-capable attacker.

- External trusted authority: accepts a signed update policy and current
  root/target role with correct expiry and trusted time (R1/T0 or a real TUF
  client); creates a candidate next checkpoint bound to channel, root identity,
  target tree SHA256, epoch and sequence.
- Protected witness backend: compare-and-set against **the entire previous
  authenticated checkpoint**, durably advance to an approved future checkpoint,
  then permit an independently authenticated readback. The model does not
  implement, choose or evaluate TPM, OS security isolation or remote witness.
- Mutable installed selector/generations: attacker can replace, delete,
  replay or corrupt local selector and generation bytes, but cannot forge the
  protected readback or break cryptographic collision resistance.
- Generation verifier: independently verifies the exact target bytes, file
  metadata, path confinement and content identity **before publication and
  again before recovery/serving**. R3's Boolean is an injected trusted
  laboratory oracle, NEVER a production caller-provided security credential.
- Client must refuse read/repair if the protected witness is unavailable.
  This deliberately sacrifices availability under witness outage.

If the attacker can also roll back the external witness, subvert the trust
root/verifier, forge approved policy or control trusted time, R3-A offers
**no anti-rollback protection**. No real network, cryptographic signing,
hostile symlink walk, disk corruption or power failure is exercised here.

## Proposed logical protocol, before any OS adaptation

1. Establish authenticated protected `W_old`. A local selector is never
   allowed to supply this root or monotonic version.
2. Obtain independently verified target-policy approval using R1. It must
   bind exact base identity, new target identity, current trusted root,
   channel, fresh epoch/sequence and time window.
3. Materialize candidate immutable generation and verify all its bytes,
   paths and metadata against the target identity.
4. Prepare `W_new` without changing `W_old`. An uncommitted candidate
   is not visible to new readers.
5. Ask the external protected store to conditionally commit `W_new` only
   when the durable floor is still exactly `W_old`; never overwrite
   a concurrently advanced witness.
6. Acknowledge commit for pointer publication ONLY after an independently
   authenticated readback equals **the complete** `W_new`. If it is still
   `W_old`, do not switch; if missing, malformed or unexpected, halt and
   re-read/seek operator assistance. A blind network/storage success code
   does not authorize publication.
7. Write the new local selector using an independently qualified OS
   primitive. Serve only the **verified generation named by the protected
   witness**; a writer failure or crash after step 5 may leave the old
   selector on disk.
8. On recovery, read and authenticate external witness **first**; independently
   verify the exact generation named by it. If selector matches, it may be
   pinned. If selector is absent, older or points to another tree under the
   same root/cursor, plan a *repair to W*, never a floor decrease. If local
   selector reports a newer cursor or another root/channel, halt rather than
   trust it. The model returns plans, not disk changes.
9. Root rotation and protected witness migration are independent policy
   protocols. This slice requires root identity continuity and does not
   authorize local root upgrades or new epoch grants.

## Invariants and stop conditions

- `selector write => protected readback == W_new` is required; neither
  `R1.AcceptPolicy` nor a transient write acknowledgement is sufficient.
- Before witness commit, only `W_old` may be selected. After witness commit,
  only `W_new` may be selected. A stale pointer is at most a recoverable
  hint and never authorization for a rollback.
- No generation is served unless its actual bytes and tree identity match
  the protected witness; absent/corrupt generation halts even with a
  valid signature or pointer.
- An unknown compare-and-set result requires re-reading protected state.
  Observed witness advancement by another writer is a conflict, not success.
- Recovery repairs a missing/old/equivocating same-root selector only when
  the exact witness generation independently validates. Local future
  selectors, different roots, invalid metadata and missing trusted witness
  halt; no heuristic "highest local version wins".
- R3-A intentionally does not claim availability if the witness is advanced
  while the required generation disappears. Durable publication needs
  separate retention/GC rules to prevent such loss.

## Fixed acceptance matrix

xUnit logical cases under `PatchTreeTrustR3WitnessModelTests`:
R1 signed-policy failures and exact-base/expiration gate; before/after
witness commit; missing/unknown/conflicting protected readback; crashes
at every logical pre/post witness/selector boundary; old/missing/equivocating
selector; malicious newer local selector; mismatch of root/channel;
malformed witness or pointer; independently unverified target; idempotent
recovery. GitHub-hosted Linux and Windows Release tests, baseline CI,
CodeQL, Heavy validation and benchmark lab must all pass before a lab
result can be accepted.

No source `src/` changes, Core API, CSP/CSM bytes, format, publication
repository, secret keys, new dependency or production signing/updater.
Results can be recorded as `R3A_LOGICAL_MODEL_PASS` only after final-head
CI; **never as protected-storage implementation, actual crash durability,
TUF adoption or the completion of #185/#184/#251**.

## Next gate

R3-B must name an actual trusted witness backend, attacker capabilities,
trusted time and out-of-band root bootstrap, plus authenticated conditional
write/read semantics, recovery availability and offline policy. T4-C2/D
must separately prove OS-qualified no-follow file walking, handle lifetime,
Flush/fsync/failure injection and durable generation retention. Without
those gates, no real installed-tree publisher/security claim is authorized.
