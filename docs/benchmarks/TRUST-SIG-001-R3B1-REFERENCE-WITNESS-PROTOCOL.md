# TRUST-SIG-001 R3-B1 — authenticated reference readback and CAS

Status: **PREREGISTERED LAB ONLY / NOT PROTECTED STORAGE**.
Owner: #185, dependencies #184, #251. ExperimentId: TRUST-SIG-001.
Predecessor: [R3-A](TRUST-SIG-001-R3A-WITNESS-FIRST-PROTOCOL.md).

## Narrow hypothesis

Replace R3-A's abstract authenticated-readback oracle with **actual**
ECDSA P-256/SHA-256 signatures over a fresh 32-byte challenge and the
full canonical root+cursor checkpoint. Separately implement an in-process
lock-serialized compare-and-swap that accepts only a strictly newer
same-root checkpoint when the expected complete checkpoint matches.

The test server is *ephemeral*. It loses its key and state on restart.
It is not remote, trusted, durable, hardware-backed, or rollback-resistant.
CAS's independentlyAuthorizedUpdaterOracle Boolean is a privileged TEST
ORACLE ONLY; it is not authentication and must never be wired to clients.
A malicious actor with authority to invoke CAS could advance the floor
and cause denial of service. The test does not implement TUF target roles.

## Signed laboratory preimage (NOT wire format)

Canonical byte order:
- ASCII domain chunkshift.trust-sig-001.r3b1.readback.v0 plus NUL.
- 32 challenge bytes: the actual client must generate a fresh cryptographic
  nonce on every read, not the deterministic fixtures used by the tests.
- One-byte-length-prefixed canonical ASCII root channel; int64 LE root
  version; raw 32-byte root metadata SHA-256 digest.
- uint8 count of root keys; ordinal-sorted (ASCII id, SHA-256 digest)
  entries; int32 LE threshold; int64 LE expiry.
- One-byte-length-prefixed cursor channel; raw 32-byte tree SHA-256;
  int64 LE epoch and sequence.

R1 validates every root/cursor field before serialization. Signatures are
64-byte IEEE P1363 r||s with ECDSA P-256/SHA-256; caller pins independently
acquired SPKI public key. Verification rejects malformed signatures, swapped
keys, nonce replay, malformed digests, modified roots or cursor. Signing
private key is never exported. The actual signed preimage is deliberately
versioned as laboratory notation, without production compatibility promise.

## Adversary, assumptions and critical race

Attacker may replay an old signed response, substitute key/signature or
receipt fields, drop a CAS acknowledgment, race two writers, or alter an
untrusted local selector. Correct nonce binding prevents reuse of old
readback in response to a *different fresh challenge*.

CAS is atomic only inside this ephemeral process. Signed readback reflects
the server at read time, not a durable witness or the instant of future
filesystem publication. Another authorized CAS may occur between valid
readback and selector write, invalidating the earlier authorization.
A concrete test MUST demonstrate this readback-to-selector TOCTOU. No
cross-resource linearizability/fencing/serving-time guarantee is claimed.
Disabling/restarting the server discards state and changes the signing key,
which intentionally invalidates old independently pinned clients.

A truly protected witness must have independent initial trust/key bootstrap,
authenticated writer roles, strong conditional durable updates, trusted
readback and secure key custody, time/expiry/availability rules, durable
generation retention, backup/restore and recovery policy. No dual JSON
files or signatures next to mutable files meet that requirement.

## Frozen acceptance matrix

On GitHub-hosted Linux and Windows, build Release and run xUnit:
1. Real P-256 signing and pinned-SPKI verification, canonical key order.
2. Wrong challenge/key, replay, altered root/tree/version/threshold,
   truncated/corrupted signatures, malformed checkpoint.
3. Stale expected complete checkpoint, unauthorized/non-monotonic/root-
   rotating proposals and two competing writers (at most one CAS wins).
4. Dropped CAS acknowledgment -> fresh signed readback -> R3-A decision.
5. Demonstrate **counterexample**: previously signed current receipt is
   stale after a second CAS although it still verifies under old nonce.
6. Restart reference loses state/key; old verifier refuses new signer.

Acceptance also requires full benchmark lab, CI, Heavy validation,
CodeQL and Dependency review green on exact PR HEAD. No production src,
public API, CSM/CSP, package/global dependency or NuGet changes.

Allowed decision on pass: R3B1_REFERENCE_CAS_AND_SIGNED_READBACK_LAB_PASS.
Explicitly **NOT** a protected witness implementation, secure network
service, product anti-rollback, trusted publisher or ADOPT. The #185, #184,
#251 release/freeze gates remain open.

## Next step

R3-B2 must name a real out-of-process protected-witness provider with
a concrete adversary model, test authenticated conditional durable write
and read across restart/backup, denial, loss of ack and concurrency,
and resolve fencing before any local selector publication. If no provider
is available, explicitly DEFER and document the security limitation.
T4-C2/D separately owns file walking, atomic pointer behavior and real
durability fault tests.
