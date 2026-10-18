# TRUST-SIG-001 R3 — distribution/trust boundary: provisional architecture decision

Status: **DRAFT / NOT ACCEPTED**. Owner: #185; depends on #184 and #251.
Research evidence: T0, R1, R2A/B1/B2/C1. R3-A is a pure witness-first
logical contract only; see [protocol](../benchmarks/TRUST-SIG-001-R3A-WITNESS-FIRST-PROTOCOL.md).

## Decision this draft proposes

Keep the ChunkShift Core and Patching content verification boundary unchanged.
CSM/CSP remain integrity artifacts and **do not** transport trusted keys,
signature roles or anti-rollback state. Authentication, channels, freshness,
update graphs and root key continuity are responsibilities of an application
distribution/trust layer.

For a first launcher/updater integration, **use a maintained TUF-compatible
framework where the consuming host has one**; require signed target metadata
to bind the canonical tree identity and, as needed, exact patch/base/target
metadata. Do not implement new TUF root/targets/snapshot/timestamp semantics
or a general first-party signing package on the strength of laboratory
cryptographic interop alone.

A standalone detached signature, including DSSE or pure Ed25519, can attest
selected bytes but does not alone solve role delegation, root rotation,
rollback, freeze, key compromise or installed-tree publication. An optional
signing adapter would require a concrete consuming application, threat
model, independent interoperability and packaging evidence; presently
**DEFER**, not permanently rejected.

## Trust ownership and exact authority

| Layer | Authoritative for | Not authoritative for |
| --- | --- | --- |
| Core CSM / CSP | Byte integrity, chunks, manifests and exact target reconstruction | Publisher identity, trusted root, freshness or rollback |
| External signed update metadata / framework | Authorized target identity, channel, key roles, root continuity, signed version policy | Durable local selector state without a trusted witness |
| Protected high-water witness | Independently anchored accepted root + active tree identity + epoch/sequence floor | File bytes; local directory content is verified separately |
| Local immutable generation and selector | Storage of exact installed bytes and an efficient current pointer | New trusted version, keys or rollback authorization |
| Publisher/recovery component | OS-qualified stage, flush, commit, pin and recovery steps under approved policy | Inferring trusted state from a corrupt/rolled-back local selector |

**Bootstrap** must pin the initial trusted root through an independent secure
distribution mechanism. Freshness requires a defined trusted time model and
online/offline expiry policy; the repository does not choose these here.

A protected witness cannot be simulated by two signed files on the same disk.
What counts as "protected" depends on attacker capability: remote trusted
checkpoint, hardware-backed monotonic mechanism or separate OS trust
domain require separate security analysis, availability, backup/restore and
provisioning evidence. A remote service can itself be unavailable or
compromised; no unconditional anti-rollback guarantee is claimed.

## Witness-first proposed crash policy

The local publisher MUST NOT write a selector for a candidate before (a)
complete generation verification, (b) current authorized signed target
policy, (c) protected conditional write of the exact new witness, and
(d) **independently authenticated protected witness readback** of that
exact checkpoint. A transient success response is insufficient.

On restart, a reader obtains the protected witness first. It may serve an
exactly verified pinned generation or plan a repair from an absent/old
selector **toward** the protected witness. It may never downgrade the
protected cursor to fit local disk. With a missing/unverified generation
or unreadable witness, it fails closed; the system may be unavailable.

Race limitation: a witness readback and subsequent local pointer write are
not a single atomic transaction. If another writer advances the witness in
between, a stale pointer may still be written. Any actual implementation
requires a single-writer lease/fence or revalidation before serving, and
reader snapshot/pinning semantics. The pure R3-A model does NOT prove
cross-resource linearizability, protected-store CAS atomicity, or OS crash
durability. T4-C2/D must explicitly address this race and real fault tests.

## Forbidden promotion shortcuts

- Do not call the R2-C1 Ed25519 interop PASS a secure signer product.
- Do not call R2-B2 signed TUF network checks local rollback protection.
- Do not promote R1/R3 injected Boolean signature/verifier oracles to
  a public API callable by untrusted update inputs.
- Do not use signed CSP hashes as permission to activate arbitrary
  paths, symlinks, hardlinks or execution metadata.
- Do not claim portable directory swap, power-cut safety or authenticated
  active-tree recovery from the Linux/Windows `File.Move` pilot.
- Do not close #185/#184/#251 or freeze #140 on this document alone.

## Required evidence before acceptance

1. Select the actual trust framework/host integration and bootstrap method;
   specify signed policy bindings (base, target, tree, patch and root).
2. Choose a protected witness provider and audit attacker capabilities,
   acknowledgement, monotonic compare-and-set and authenticated readback,
   unavailable-service policy, clock and rollback backup/restore.
3. Independently verify installed bytes and hostile path metadata under
   the exact root; prove bounded memory and failure policy.
4. Reconcile witness and pointer commit with T4-C2/D OS-specific fencing,
   staged generation retention, reader pins and fault/kill tests.
5. Produce decision-grade evidence and an accepted RFC with public
   compatibility/versioning policy only if shipping an in-product
   distribution layer is justified.

This is architecture analysis and a lab protocol, not a production security
claim, accepted RFC or authorization to port/release Patching.
