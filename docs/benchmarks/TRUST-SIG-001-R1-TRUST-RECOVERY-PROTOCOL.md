# TRUST-SIG-001 / R1 — root continuity, rollback checkpoint and crash-recovery model

Owner: [#185](https://github.com/definitely-stable/ChunkShift/issues/185). Adjacent: #184 (PATCH-TREE-001), #251 (release research freeze).
Status: **RESEARCH-ONLY / ABSTRACT LAB / NO PUBLIC FORMAT / NO OS DURABILITY CLAIM**.
Base: T0 detached P-256 policy proof lab (merged #381), RFC-0004 sections 4–5 and T4-B/T4-C1 update-set publication research.

## 1. Why R1 before a real publisher

T0 verifies a signed tuple only when a client separately supplies a trusted public key and an authenticated monotonic cursor. T4-B publishes complete immutable generations in a logical transition; T4-C1 exposed real Windows sharing and publisher contention. Neither makes the trust anchor and cursor crash-durable, nor answers what happens if one disk record advances without the other.

A single local JSON sequence number or duplicate filesystem pointer is not an anti-rollback primitive: an attacker with writable filesystem or backup restore capabilities may roll back *both*. The R1 recovery oracle therefore requires a **separately authenticated, rollback-resistant checkpoint** and rejects inconsistent observations. It cannot fabricate missing authorization by picking the highest sequence on disk.

## 2. Candidate trust architecture, not an adopted wire contract

Proposed separation:

- **Root trust** is provisioned out of band; trusted root metadata binds channel, monotonically increasing root version, key identities and signature threshold. R1 uses lowercase SHA-256 fingerprints of the supposed root metadata and pinned public keys supplied by its caller, not a real root parser or trust store.
- **Target policy** authorizes exact old/new tree identities plus epoch, sequence and validity window (T0). It is separate from both CSP and CSM and from the root delegation metadata.
- **Installed cursor** binds channel, active tree SHA-256, epoch and sequence. The current trusted root plus installed cursor are conceptually one authenticated checkpoint. Two separate filesystem files are NOT such a checkpoint.
- **Reader** pins the exact validated active generation (T4-B). Recovery compares a candidate active checkpoint against the independent trusted high-water, and separately requires content verification.
- **Delegation** from root to release/target keys, offline recovery roots, trusted time and clock rollback are open design questions. No claim of solving TUF freeze attacks is permitted.

### Root rotation

For a proposed version N+1, R1 requires:

1. Equal channel, unique changed root metadata identity and exactly N+1 (not N, not N+2; guard overflow).
2. Threshold of **distinct verified old-root signer IDs** found in the previously trusted root keys.
3. Separately, threshold of **distinct verified new-root signer IDs** found in the proposed new root keys.
4. Bounded canonical root shape (1–16 keys, threshold 1..key count, 64-lowercase-hex digests, valid names); the proposed new root must not be expired at a trusted update time.
5. Both signature-oracle lists are asserted to have been verified against the **same exact proposed root metadata bytes**. Merely listing signer IDs does not perform signature verification. The lab counts duplicates once and rejects unknown signers.

This models part of the root continuity requirement of TUF, but deliberately does **not** implement TUF roles, canonical metadata parsing, signature verification, consistent snapshots, timestamp/snapshot/targets freshness, delegation, key revocation, or threshold verification of real bytes. No production code may treat those lists as trusted user inputs.

Primary reference: https://github.com/theupdateframework/specification/blob/master/tuf-spec.md, Update the root role. A real TUF client requires substantially more than these abstract checks.

### Policy and cursor advancement

An externally verified T0 approval is insufficient by itself to advance the current installed cursor unless a separate **current trusted target-role verification** and exact root metadata binding are also satisfied. The current base tree must match, new cursor (epoch,sequence) must be lexicographically newer, the root and policy must be unexpired according to the trusted clock, and all identities must be valid. R1 returns a **candidate** immutable checkpoint. Persisting/authorizing it is NOT in scope.

Root version advancement is separate from target cursor advancement, and neither may be silently accepted from untrusted downloaded metadata. Epoch changes require delegated authorization outside R1.

## 3. Recovery decisions and fault matrix

R1 accepts an independently authenticated and rollback-resistant **trusted floor** and a separately observed active checkpoint, and returns:

| Case | Verdict | Safe operation |
| --- | --- | --- |
| Same root, same active tree, exact epoch/sequence, verified bytes | ACCEPT_PINNED | Permit reading a pinned immutable generation |
| Missing observed pointer/checkpoint | REJECT_MISSING | Stop; require trusted recovery |
| Wrong channel | REJECT_CHANNEL | Stop |
| Different root version/metadata/key set/threshold | REJECT_ROOT_MISMATCH | Stop; cannot re-pin roots from untrusted disk |
| Observed cursor lower than protected floor | REJECT_ROLLBACK | Stop; do not silently downgrade |
| Observed cursor higher than protected floor | REJECT_UNANCHORED_FUTURE | Stop; do not guess whether pointer publish completed |
| Equal cursor, different tree | REJECT_EQUIVOCATION | Stop; no tie-break by file modified time |
| Same checkpoint but content verification failed | REJECT_UNVERIFIED_CONTENT | Stop; do not serve corrupt generation |

Before an externally trusted checkpoint is promoted, only the exact old observed checkpoint can be selected; after promotion, only the exact new observed checkpoint can be selected. An interruption between a separately persisted cursor and pointer is **not** solved here; it is a detected inconsistent state. A future crash-safe protocol must either guarantee one durable commit witness for both or supply a separately proven recovery protocol tied to authenticated immutable generations.

The lab does not execute power loss, storage flush, root directory fsync, file sharing, reparse traversal, trusted monotonic hardware, rollback-resistant remote checkpointing, or process kill. It must not claim power durability, actual anti-rollback or atomic update publication.

## 4. Frozen R1 laboratory acceptance

Implement in benchmark-only namespace, with no production src/ changes and no new packages. Verify:

- Root N to N+1 succeeds only with both signer thresholds; duplicate IDs never count twice, wrong/incomplete/new-only/old-only signatures and unknown IDs reject.
- Skip/replay root versions, expired new root, missing/invalid key shape, wrong channel reject.
- Current-root/target-role bound approval, exact base matching, epoch/sequence monotonicity, expiration and malformed target reject; T0 P-256 signature proof interoperates with the R1 approval seam.
- Root/keyset equivocation, pre/post logical commit cutoffs, unanchored future, rollback, torn/mismatched checkpoints, missing and unverified content never return ACCEPT_PINNED.
- Linux and Windows hosted .NET 10 Release tests + repository CI, CodeQL, Heavy validation, Benchmark lab.
- T0 and T4-B source, CSP/CSM, public API, NuGet/release and baseline behavior unchanged.

Only a green final-head run may establish R1_LOGICAL_CONTRACT_PASS (research foundation, *not* ADOPT of trust/security semantics).

## 5. Decisions required before any shipping adapter

1. Choose whether ChunkShift integrates an existing TUF-style updater (preferred evaluation baseline) or owns an optional signing/distribution package. Document threshold roles, trustworthy initial root and secure root rotation.
2. Establish trusted time and offline grace rules (and known unavoidable freeze limitations without trusted freshness).
3. Prove a protected high-water channel: OS security model, trusted counter/TPM or remote state, local backup restore and attacker capabilities. Simple double-write on the same writable filesystem is NOT sufficient against adversarial rollback.
4. Couple durable policy cursor with real installed selector (T4-C2), specify fail-close and supported operator-assisted recovery for torn commit. Test platform-specific no-follow file walking, fsync/Flush, sharing and process failure on GitHub-hosted Linux/Windows. Real power-cut durability remains unproven until suitable fault instrumentation.
5. Preregister S0–S3 cryptographic sign/verify, key/signature sizes, .NET 10 NativeAOT, x64/arm64 and external interop comparison. Do not mistake the S0 T0 proof for a final algorithm selection.
6. Freeze only after #185 decision and the whole #251 research synthesis. #140 Patching PublicAPI, production updater and publication stay BLOCKED.

## 6. Research scope and disclosure

There is no standalone R1 serialization, signed policy file, public key distribution, actual signature verification of root metadata, protected on-disk store, authoritative runtime integration, persistent ABI or legal security audit. This model is **not suitable for production use**. The Boolean target-role approval and enumerated verified signer IDs are trusted laboratory oracles only; replacing them with untrusted inputs would be a critical security defect. A successful CI run proves the specified finite logical cases, not the safety of real update infrastructure.
