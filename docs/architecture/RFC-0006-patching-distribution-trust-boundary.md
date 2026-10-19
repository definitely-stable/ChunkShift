# RFC-0006 — Patching distribution trust and update-policy boundary

Status: **Accepted architecture boundary** (2026-10-19).
Owner: [#185](https://github.com/definitely-stable/ChunkShift/issues/185);
related [#184](https://github.com/definitely-stable/ChunkShift/issues/184),
[#251](https://github.com/definitely-stable/ChunkShift/issues/251), #7, #13 and #140.
Refines [RFC-0004](RFC-0004-core-0.1-deferred-product-concerns.md)
§4–5. It does **not** amend CSM v1, CSP v1, any content identity,
the public API, or the release gate.

## 1. Decision

**ChunkShift Core and ChunkShift.Patching are deterministic content
transformation and verification libraries, NOT update-trust authorities.**
For the first Patching release:

1. Retain **integrity without claimed publisher authenticity** in Core and
   Patching. CSM and CSP contain no signature, update policy, channel,
   sequence, epoch, root key or anti-rollback record.
2. **ADOPT the external application-owned distribution boundary** for trust
   decisions. An updater/launcher that requires authenticated delivery SHOULD
   integrate a maintained **TUF-compatible updater** for signed target
   metadata and key/role continuity rather than reimplementing TUF inside
   ChunkShift. The actual client, trust bootstrap and POUF are host-specific.
3. **DEFER a first-party signing package** and standalone DSSE, minisign,
   Sigstore, P-256, Ed25519 and post-quantum envelope selection. These are
   valid tooling candidates, not an automatically secure distribution scheme.
   The host's selected trust protocol chooses signature algorithms and keys.
4. **DEFER any first-party protected monotonic witness**, root-provisioning
   service, TUF role service, remote updater, publisher daemon or filesystem
   update-set API. The consuming application must implement and qualify these
   if its threat model requires them.
5. **Keep the Patching research freeze #251 OPEN.** This RFC resolves #185's
   *architecture ownership* question only. It does not resolve #184's
   tree/publisher semantics, #251's other research dispositions, or authorize
   #140/PublicAPI publication.

An application may use ChunkShift on local trusted files without TUF; in
that mode the caller owns every input's provenance. It MUST NOT market
ChunkShift's internal digest checks as distribution authorization.

## 2. Alternatives evaluated and disposition

| Candidate | Verdict for initial release | Why |
| --- | --- | --- |
| Put signatures or sequence into CSM/CSP | **REJECT** | Violates RFC-0004, changes frozen content identity/format and does not manage root/freshness |
| Invent proprietary signing/updater roles in Patching | **REJECT** | Repeats established trust framework; R2/R3 show complexity of root continuity, rollback and installed state |
| Host-owned TUF-compatible distribution client | **ADOPT as architecture guidance** | Maintained role separation, signed targets and root rotation; host supplies bootstrap, trusted state and policy |
| Host-owned detached DSSE/minisign/Sigstore tooling | **DEFER as alternatives** | Proves selected bytes only unless host supplies adequate authorization, replay/freeze and key management |
| New ChunkShift signing NuGet | **DEFER** | No demonstrated consumer need or secure product boundary; NSec requires RID libsodium support on tested hosts |
| SQLite/HMAC as locally protected high-water | **REJECT for adversarial rollback** | R3-B2 recovers a *validly authenticated old checkpoint* after service-DB restore |
| Host-specific independently protected witness | **DEFER implementation / ALLOW integration** | Actual OS/hardware/remote trust domain and rollback adversary not selected for any shipping host |
| Pure per-file CSP v1 plus external update-set identity | **RETAIN boundary** | Frozen CSP v1 remains per-file; #184 decides canonical tree format, staging and publication |

Here **ADOPT** means the package boundary, not adoption of any TUF
implementation, key algorithm, protected witness, or shipped updater.

## 3. Authority and trust decisions

| Authority | Owns / MUST establish | MUST NOT infer |
| --- | --- | --- |
| Core CSM | Canonical chunk/manifest structure, `ManifestId`, physical `FileDigest` and `PhysicalLength` | Publisher or target authorization |
| Patching CSP | Valid CSP and base binding, exact target reconstruction and verifiable output | Trusted release, version, channel or filesystem install permission |
| External trust client | Trusted root/bootstrap, root rotation, authorized targets, authenticated length/hash and freshness checks | Integrity of a yet-unverified reconstructed tree or atomic local installation |
| Application release policy | Channel, version/epoch/sequence, expiry, permitted base-to-target graph, downgrade/recovery and offline rules | That local mutable state is rollback-resistant |
| Optional protected witness | Independently anchored, authenticated installed root/tree + cursor under an explicit attacker model | Installed file integrity, merely from state signature |
| Application publisher | Safe staging, exact path metadata, generation pinning, constrained handles and OS-specific publish/recovery | That renaming a directory proves power durability |

No Boolean `SignatureVerified`, enumerated signer IDs, or test-only
`verifiedByCurrentTargetRoleOracle` from the R1/R3 labs may be exposed
to untrusted users as if it were cryptographic evidence.

### 3.1 TUF claims and prerequisites

TUF version **1.0.37**, dated 2026-10-08, defines roles, signed target
metadata and update-client freshness/consistency behavior:
https://theupdateframework.github.io/specification/latest/ .
Its scope excludes actual application-file installation and initial
root bootstrapping; a host must provision trusted roots independently and
select an interoperable implementation/POUF. TUF checks are conditional on
retaining prior trusted state and on a meaningful update start time.
A compromised or rolled-back local cache may nullify protections based
on prior versions; R2-B2 demonstrates this for the stronger local adversary.

**No production TUF integration has been implemented or adopted by this RFC.**

## 4. Artifact and release binding contract

These are **host policy requirements**, not a ChunkShift wire schema and
not a requirement to add any field to CSM or CSP. A host must use
authenticated metadata from its selected trust framework; values parsed
only from an untrusted CSP/CSM never authorize themselves.

### 4.1 File and manifest target

For each authorized file target the host must bind and verify:

- Unambiguous artifact kind/version and target name within the trust
  repository and channel; selected HashSuite/profile semantics.
- Exact downloaded artifact **byte length** and cryptographic physical
  digest from authenticated target metadata (such as TUF targets), before
  interpreting its contents.
- Expected manifest `ManifestId`, manifest physical `FileDigest` and
  `PhysicalLength` if the manifest is a separately distributed artifact.
- Expected reconstructed file identity, length and target manifest identity,
  compared against actual reconstructed content/verified CSM; physical CSM
  `FileDigest` hashes the **manifest**, not the reconstructed file bytes.

A host must not substitute `ManifestId` for a physical artifact hash,
nor call a manifest's `FileDigest` a whole-content digest. HashSuite IDs
and digest scopes must be unambiguous at the trust boundary.

### 4.2 Patch target

For every selected CSP, the signed release policy or signed target set
must bind the **exact CSP file length and physical cryptographic digest**
as well as the intended `base ManifestId` (if required) and
`target ManifestId`. For a self-contained CSP, the host may authorize
the empty-base case; it must not let an untrusted CSP select an unexpected
source. The host checks observed CSP/CSM identities against authenticated
expectations, not merely against each other.

If a host uses one signed, immutable release descriptor referencing multiple
targets, that descriptor must itself be authenticated, versioned,
bounded and verified through the trust client. Its exact serialization
and any custom TUF target metadata field names are **host-specific**
and are not standardized by this RFC.

### 4.3 Whole-tree update set

Once [#184](https://github.com/definitely-stable/ChunkShift/issues/184)
accepts a canonical tree identity and metadata representation, the host
must bind it in the authorized release target along with the set of
required patch/source artifacts. Path identity, file kind, executable
modes, symlinks/reparse-point handling, directory metadata, empty
directories and collision policy are #184's responsibility, not CSP v1.

The T0 same-path independent CSP representation remains the safe cost
baseline. The preliminary T1/F2 cross-file attempts were not adopted;
this RFC does not choose a new tree byte format or universal base.

### 4.4 Update-policy semantics

An updater claiming rollback/freeze protection must specify and enforce:
channel binding, an authorized monotonically ordered epoch/sequence
(or trust-framework equivalent), permitted base/target transitions,
expiry and trusted-time/offline policy, authenticated root continuity,
and recovery/downgrade procedure. Distinguish a cryptographically valid
old target from a release currently authorized for installation.

No globally monotonically increasing counter, magic epoch encoding or
timestamp is inserted into `ChunkId`, `ManifestId` or CSP v1.

## 5. Threat model, responsibility and release claims

| Attacker/scenario | Responsible boundary | ChunkShift-alone claim |
| --- | --- | --- |
| Malicious mirror, modified CSP/CSM | Trust client checks signed target digests; CSP/CSM check internal integrity | Detect corruption relative to trusted expectation; **not** authenticate mirror |
| Arbitrary installation / unauthorized publisher | Application trust root and target roles | **No authenticity claim** |
| Mix-and-match of signed metadata / unrelated releases | TUF snapshot/targets and application release binding | **No release-policy claim** |
| Replayed metadata, frozen releases, stale timestamps | TUF refresh/version/expiry and host's trusted-time policy | **No freshness claim** |
| Stolen/rotated publisher keys | Host TUF root/role thresholds, provisioning and recovery | **No key-management claim** |
| Long/infinite/slow network response | Host transport timeout/quotas and signed length; CSP local structural limits | No CDN availability claim |
| Locally replaced selector or restored install backup | Host protected state (if required), pin/verify and publisher recovery | **No protected installed-state claim** |
| Same-disk DB or signer-backed cache rollback | Independent trust domain required for stronger anti-rollback | **NO_GO** for local-store-only solution |
| Concurrent writers, lost CAS acknowledgment | Host conditional witness write, fencing, verified readback | R3 tests are laboratory only |
| Symlink/reparse escape, power cut or torn tree | #184 OS publisher and verified generation identity | **No portable tree atomicity claim** |

A remote malicious actor who can only manipulate the download mirror
is materially different from one who can rewrite local trusted metadata,
a service database or the OS trust anchor. The host must state which actor
it protects against; stronger guarantees are **opt-in host commitments**.

## 6. Required host integration sequence (non-API)

The following is an **acceptance checklist**, not a proposed public
method name or an already implemented publisher:

1. Load a *previously established* trusted root and applicable trusted
   release state from the host's trust domain. If unavailable or suspect,
   fail closed under the host's documented recovery policy.
2. Ask the maintained trust client to refresh/check roles, metadata
   signatures, lengths, hashes and permitted target names; enforce trusted
   time, version/channel and locally authorized transition policy.
3. Download selected, bounded artifacts. Compare exact bytes against
   authenticated physical digests/lengths BEFORE using them as authority.
4. Inspect verified CSP/CSM base and target identities and compare them
   with authenticated expectations; validate present base content.
5. Reconstruct into a private, bounded staging generation; verify full
   target bytes, paths, metadata, permissions and tree identity. Do not
   publish partially written files.
6. Apply **host-qualified** installation policy and OS publication
   contract. If the host claims local rollback resistance, require
   independently protected monotonic state, a proven transition/recovery
   protocol and single-writer fencing. R3-A/B1/B2 do not supply these.
7. Before serving a new generation, pin and verify the actually authorized
   generation under the host's trust and concurrency rules. Never infer
   trusted state from a mutable local selector or choose the highest local
   version during recovery.
8. On error, do not silently downgrade or install a different target.
   Expose an actionable failure to the host; retain the previously
   authorized generation when supported by the OS/publisher contract.

All cryptographic and installed-state authorization inputs are obtained
**outside** any untrusted patch argument. This checklist must not be
translated into public signing/updater methods until a concrete consumer
and additional accepted RFC justify that change.

## 7. Adversarial acceptance examples for an integrating host

| Scenario | Required host outcome |
| --- | --- |
| Valid CSP with maliciously substituted target identity | Reject against signed expected target identity |
| Correct CSP digest but wrong authenticated channel/base | Reject release/policy; do not publish |
| Wrong or stale signed release policy | Reject/downgrade only via authorized recovery procedure |
| Properly signed old TUF metadata after independent trusted-state rollback | Host may **not** claim strong local anti-rollback unless independent floor exists |
| Validly MACed old SQLite checkpoint after same-service DB restore | Reject security claim; R3-B2 proves this counterexample |
| Crash after protected floor advance, before selector switch | If claiming this mode, recover only independently verified floor target or halt |
| Competing witness writer after signed readback | Require fencing/revalidation; a signed historical read alone is insufficient |
| Missing protected witness or corrupted target generation | Fail closed for hosts promising protected installed state |
| Windows file lock, Linux mount move or power interruption | Test OS-specific publisher separately under #184; don't infer durability from model |
| Offline client past trusted metadata expiry | Follow declared host offline/freshness policy; do not silently treat old timestamp as fresh |

## 8. Evidence supporting this boundary

All of these are **research** results; none individually implements the
production trust boundary.

- T0 P-256 detached policy + R1 logical root/cursor model: signer and
  continuity mechanics, with external trusted proofs only.
- R2-A algorithm matrix: P-256 JIT/NativeAOT broadly measured;
  platform-dependent ML-DSA support. No signature algorithm adopted.
- R2-B1 OpenSSL/DSSE and R2-B2 real Python-TUF client: role and
  metadata rejection behavior tested; local high-water rollback
  weakness demonstrated under stronger attacker.
- R2-C1 standalone NSec Ed25519 / OpenSSL: interoperability on measured
  JIT/NativeAOT hosts, with separate libsodium runtime assets.
- R3-A witness-first logical recovery contract:
  [PR #387](https://github.com/definitely-stable/ChunkShift/pull/387).
- R3-B1 real signed readback + ephemeral CAS, readback-to-selector race
  counterexample: [PR #388](https://github.com/definitely-stable/ChunkShift/pull/388).
- R3-B2 out-of-process SQLite transaction/restart, process-kill tests and
  **valid-MAC service DB rollback NO-GO**:
  [PR #389](https://github.com/definitely-stable/ChunkShift/pull/389),
  [recorded in #185](https://github.com/definitely-stable/ChunkShift/issues/185).

The original hosted runs and their limits remain under
`docs/research/results/` and `docs/benchmarks/`. Neither signing
interop, successful TUF validation nor SQLite ACID guarantees a secure
installed tree.

## 9. Compatibility, release and reopening criteria

- **No new public APIs.** No `ITrustProvider`, `ISigner`,
  `IUpdatePolicy`, `IWitness`, `IUpdater` or `IRepository`
  abstraction is introduced as part of the initial NuGet surface.
- **No CSM/CSP changes.** No new authenticated slots or digest/manifest
  identity semantics; independent third-party tools can use existing
  immutable artifact bytes.
- **No transitive crypto/trust dependency.** NSec, TUF tooling, DSSE,
  Sigstore, TPM/cloud client and SQLite pilot are NOT dependencies
  of `ChunkShift.Patching`.
- **No permission to release yet.** #251 remains the Patching Research
  Freeze gate; #184 must still decide update-set/publisher contracts.
  #140 and publication await #251's separate closure.
- **#185 disposition:** architecture ownership **ADOPT** (external
  host-managed trust); first-party signer/updater/witness **DEFER**,
  local SQLite as strong rollback anchor **REJECT**. Closing #185
  means only these decisions are resolved.
- Reopen this boundary only for a concrete consumer needing an integrated
  verified adapter, an incompatibility in authenticated artifact
  binding, changed adversary/rollback requirements, or a formally
  adopted update-set format that cannot be represented by detached
  signed targets. Require a new RFC, separately versioned contracts,
  attack-oriented tests and key/transport/persistence review before
  promotion.

## 10. Reference specifications

- [The Update Framework specification 1.0.37 (2026-10-08)](https://theupdateframework.github.io/specification/latest/):
  root/targets/snapshot/timestamp roles, trusted client workflow,
  explicit out-of-scope software installation and initial bootstrap.
- [in-toto attestation/DSSE envelope](https://github.com/in-toto/attestation/blob/main/spec/v1/envelope.md):
  useful detached signing envelope, not a replacement for release
  policy, root continuity or protected installed state.
- [RFC-0004](RFC-0004-core-0.1-deferred-product-concerns.md):
  original detached-authenticity and anti-rollback package boundaries.
- [CSP-V1-CANDIDATE](CSP-V1-CANDIDATE.md):
  immutable per-file transform format and exact base/target binding.
