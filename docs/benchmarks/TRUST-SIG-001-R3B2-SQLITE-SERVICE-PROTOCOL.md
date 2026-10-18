# TRUST-SIG-001 R3-B2 / S1 — separate-process SQLite witness pilot

Status: **PREREGISTERED, RESEARCH-ONLY, NO PROTECTED-STORE ADOPT**.
ExperimentId: TRUST-SIG-001; issues #185, #184, #251. R3-B1:
[reference in-process CAS](TRUST-SIG-001-R3B1-REFERENCE-WITNESS-PROTOCOL.md).

## Research question and provider choice

Replace a purely in-memory reference CAS with a **real independent process**
backed by Python stdlib `sqlite3` using a one-row SQLite transactional
checkpoint. It is a named, simple ACID provider on GitHub-hosted Linux/Windows,
not a production updater or a secured remote monotonic service.

This is a limited **S1 feasibility and negative-control test**:
- Can two independent HTTP clients safely contend for one SQLite state?
- Do committed transactions persist after service restart/process kill?
- Does a dropped confirmation after COMMIT recover by authenticated fresh read?
- Can a corrupted/missing installation selector alter the separate DB? (no)
- Can an adversary who also controls the service DB roll it back and still
  produce an authenticated old checkpoint? (**yes, expected counterexample**)

## Exact deployment and threat assumptions

Two distinct paths on the hosted runner: attacker-writable installation tree
and service-owned SQLite file. The pilot does **not** configure separate OS
accounts, ACL confinement, disk encryption or an independently managed trusted
cloud operator. The service runs on 127.0.0.1 plain HTTP; no TLS, network
segmentation, public access, real cross-host trust or mutual authentication.
**Do not deploy this listener outside an isolated disposable lab.**

The server gets two unrelated ephemeral environment secrets: a 32-byte
read-response HMAC key and a write bearer credential (minimum 32 bytes).
Client pins the MAC key out of band, verifies a 32-byte fresh nonce and the
complete response. This is a symmetric, closed-domain experiment. A holder of
the HMAC key can forge readbacks; the MAC key is **not** a public key, and
compromise of that key destroys authentication. The write bearer permits CAS
without independently checking TUF target metadata; it is a *surrogate role
credential*, not a production update authorization mechanism.

No secret is checked into the repo, stored in test artifacts or included in
the JSON payload. Actual CI secrets are generated ephemerally on each test.
Only the server owns the independent SQLite file; this separation is
**assumed, not enforced** by CI OS permissions.

## DB transaction and crash semantics

One table holds `(revision:int64, checkpoint:canonical JSON BLOB)`. The
server refuses implicit bootstrap or reset: only an explicitly supplied
initial checkpoint file may create a new database, and startup rejects a
missing DB. A transaction uses `BEGIN IMMEDIATE`, reads the full current
checkpoint, compares it with the complete expected checkpoint, validates
strict monotonic epoch+sequence with unchanged root and changed target tree,
updates both checkpoint and revision, then COMMIT. `journal_mode=DELETE`
and `synchronous=FULL` are chosen explicitly. SQLite rollback journal
and transaction semantics are used, not reimplemented. This does **not**
prove storage-controller flush honesty, independent hardware monotonicity,
host power-cut durability, multi-host consistency or rollback protection
against server-storage replacement.

Lab-only failure headers trigger abrupt process exit immediately before
or after the SQLite COMMIT boundary. These headers are enabled solely by
`--allow-lab-faults` and MUST NOT exist in production.

## Readback MAC / canonical shape

JSON requests are bounded (16 KiB), use reject-duplicate-key parsing,
strict root/cursor member sets and R1-compatible canonical lowercase ASCII
names and SHA256 hex. Current checkpoint and a monotonically increasing
SQLite revision are returned along with an exact 32-byte caller challenge.

HMAC-SHA256 is computed with the pinned ephemeral key over:

```text
ASCII("chunkshift.trust-sig-001.r3b2.read.v0") || NUL
nonce[32] || revision(uint64,LE) ||
canonical_checkpoint_byte_length(uint32,LE) ||
JSON canonical checkpoint bytes (sorted keys, compact separators, ASCII)
```

A completely independent test-side verifier reconstructs the preimage and
requires constant-time MAC equality and exact requested nonce equality.
This is a **research-only lab protocol**, not a public wire format or
TUF/DSSE compatibility target. Unlike R3-B1's asymmetric signed response,
this protocol is symmetric and cannot be represented as public attestation.

## Frozen adversarial and failure-injection matrix

GitHub-hosted Ubuntu 24.04 and Windows 2025, Python 3.13, subprocess
server + real SQLite + independent HTTP requests and test-side verifier.

1. Correct CAS commits, increments revision and survives terminate/restart.
2. Incorrect/absent writer authorization fails; no state change.
3. Wrong challenge, substituted HMAC key, edited tree and version reject.
4. Root swap, invalid shape, non-monotonic cursor and stale expected
   checkpoint reject. Two concurrent CAS requests: exactly one wins.
5. Kill during open uncommitted SQLite transaction -> old checkpoint
   recoverable; kill after COMMIT before acknowledgment -> new checkpoint
   recovered through a fresh MAC-authenticated read.
6. Adversarial rollback of **only installation files** does not roll back
   separate service DB. Missing DB without explicit bootstrap fails closed.
7. **Required negative counterexample:** snapshot/copy of witness SQLite
   file BEFORE accepted update, process shutdown, replacement with old copy
   and service restart with unchanged HMAC key -> prior checkpoint is accepted
   under a VALID MAC and reduced revision. This MUST be recorded as proof
   that SQLite+HMAC is NOT independently rollback-resistant.

Specialized workflow must succeed on both hosts; general CI, CodeQL,
Heavy validation and Benchmark lab must also pass for a merged LAB result.

## Exit and next step

Possible positive decision:
`R3B2_S1_SQLITE_TRANSACTION_AND_PROCESS_RESTART_PASS` **only for the
observed OS processes and SQLite journal behavior**. Mandatory security
verdict: `ANTI_ROLLBACK_WITH_SERVICE_DB_CONTROL = FAIL / NO_ADOPT`.

A viable R3-B3 must select a truly independent trust provider/service
outside the attacker's modification authority (with bootstrap/key custody,
TLS/mTLS or comparable transport, authenticated write roles, trusted time,
independent monotonic anchoring, outage policy, backup/restore and
*fencing* across witness-readback and local selector publication).
Alternatively decide an explicit weaker offline attacker model, or DEFER
installed-tree anti-rollback functionality. None of R3-B2 S1's passing
tests authorize T4-C2/D, release #140, or closing #185/#184/#251.
