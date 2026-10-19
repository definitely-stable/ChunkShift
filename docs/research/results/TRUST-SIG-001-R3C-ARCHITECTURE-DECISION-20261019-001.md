# TRUST-SIG-001 / R3-C — architecture ownership decision record

EvidenceId: `TRUST-SIG-001/EVIDENCE-20261019-001`  
ExperimentId: `TRUST-SIG-001`  
Owner: #185; dependencies #184 and #251.  
Date: 2026-10-19.  
Kind: architecture synthesis over independently archived prior experiments;
**no new performance benchmark or production trust-store validation**.

## Frozen decision question

After T0, R1, R2-A/B1/B2/C1 and R3-A/B1/B2, does the first
`ChunkShift.Patching` release need a **first-party signing or updater/
protected-state API**, or is detached host-owned trust sufficient for a
safe package compatibility boundary?

Decision rule: an in-package security component requires independently
verified roles/root bootstrap, key custody, authenticated writer authority,
freshness/time, protected rollback-resistant installed state, writer
fencing and actual publisher semantics under a named consumer threat model.
Partial signature interoperability, finite logical proofs and SQLite
transaction integrity are **not** sufficient. If such a complete
production need/evidence is absent, retain a detached external-host
boundary and explicitly defer in-package security components.

## Evidence reviewed

| Source | Concrete result | Implication |
| --- | --- | --- |
| RFC-0004 §4–5 | Detached integrity/authentication and release-policy separation accepted | No signature/sequence fields in CSM/CSP |
| R2-A, #185 | P-256 host JIT/NativeAOT; ML-DSA provider matrix host-dependent | A library choice is not a secure updater |
| R2-B1, R2-B2 | OpenSSL/DSSE mechanics, reference Python TUF client rejection cases; signed old metadata can be replayed after trusted local cache rollback | Prefer maintained external role verifier; independent local high-water requires stronger trust |
| R2-C1, PR #386 | Pure Ed25519 interoperable via NSec; separate native libsodium RID deployment | Do not force native cryptographic dependency on generic Patching |
| R3-A, [#387](https://github.com/definitely-stable/ChunkShift/pull/387) | Pure witness-first/reader recovery model, all host gates green | Logical preconditions, not durable cross-resource transaction |
| R3-B1, [#388](https://github.com/definitely-stable/ChunkShift/pull/388) | Real signed challenge readback/in-memory CAS; 11/11 tests per Linux/Windows host and all PR gates; accepted `main@3b56b8586a27` | Signature freshness at read time does not fence a later writer |
| R3-B2, [#389](https://github.com/definitely-stable/ChunkShift/pull/389) | Separate-process SQLite transactional CAS; 10/10 tests on each Linux/Windows host and all final-head PR gates, `main@abd19d76bfc2` | Process restart passes; attacker restoring service DB obtains valid HMAC for older state — **security NO_GO** |

R3-B2 final-head GitHub-hosted runs:
[specialized #39145494673](https://github.com/definitely-stable/ChunkShift/actions/runs/39145494673),
[CI #39145494738](https://github.com/definitely-stable/ChunkShift/actions/runs/39145494738),
[CodeQL #39145494675](https://github.com/definitely-stable/ChunkShift/actions/runs/39145494675),
[Heavy #39145494640](https://github.com/definitely-stable/ChunkShift/actions/runs/39145494640),
[Benchmark lab #39145494699](https://github.com/definitely-stable/ChunkShift/actions/runs/39145494699).
All reported SUCCESS for exact final PR head
`063fd3ab5c0bbf7d00bee417e7681a525e912be4`;
specialized 10/10 PASS per OS. Historical artifacts and limitations
remain in corresponding experiment protocols/issue comments. No ZIP
content was newly independently re-audited for R3-C.

## Decision and limitations

**ADOPT (architecture ownership only):** signed distribution policy
and freshness belong to the consuming application with a maintained
TUF-compatible trust client where authentication is required;
`ChunkShift.Patching` remains an integrity/reconstruction library.

**DEFER:** first-party signer, TUF client/role server, trusted time,
root/key provisioning, protected witness backend, SDK trust-interface
abstractions and tree OS publisher.

**REJECT:** local JSON/SQLite plus MAC/signature as a claimed
rollback-resistant high-water **when attacker controls the same store**.

Accepted architecture: [RFC-0006](../../architecture/RFC-0006-patching-distribution-trust-boundary.md).
This is a package boundary and explicit host integration checklist,
**not a deployed trusted updater** or a security audit.

### Exit gates and cross-experiment dependencies

- #185 architecture decision may close after RFC-0006, this index and
  registry merge without waiting for a protected-witness product.
- #184 remains open for canonical tree metadata, real OS publication
  semantics, handle-relative paths, durability/recovery and reader pins.
- #251 remains open for PATCH-GAP/META/INCR/DICT/DOTNET/ML/COMPILER/EXEC,
  their final dispositions and the CSP v1/vNext synthesis. #140,
  Patching API freeze, NuGet publication remain blocked by #251.
- Reopen #185's boundary only with a specific host application whose
  threat model and interoperable security requirements cannot be met
  outside Patching; a new RFC and evidence are mandatory.

No additional CSM/CSP fixture regeneration, performance score,
cryptographic oracle or new signature-validation implementation was
introduced by R3-C; these are deliberate non-goals.
