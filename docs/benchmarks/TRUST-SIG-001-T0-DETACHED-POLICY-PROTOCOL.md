# TRUST-SIG-001 / T0 — detached update-policy verification lab

**Owners:** #185, #184; release freeze #251.
**Status:** RESEARCH FOUNDATION / PROPOSED / NO PRODUCTION ADOPT.
**ExperimentId:** TRUST-SIG-001. Scope: signature binding and policy rejection mechanics only, NOT the final S0–S3 algorithm matrix.

## Decision context and why T0

RFC-0004 sections 4–5 already accepted a detached trust boundary: hashes of CSM/CSP supply integrity, not origin authenticity, while rollback and freeze belong to signed update policy. PATCH-TREE-001 T4-A/B describe a candidate tree identity and abstract publication model. T4-C0/C1 on real hosted Windows/Linux show why a filesystem probe is insufficient to authorize an update: file rename success is neither a signed trust decision nor a durable monotonic cursor.

T0 evaluates one **illustrative**, disposable ECDSA P-256 (S0) detached policy verifier in benchmark-only code, using BCL APIs without new dependencies. It deliberately does not decide Ed25519, ML-DSA, signature package, TUF interoperability, root rotation, persistence, or a versioned production envelope. S0 is a control mechanism, not a preferred final algorithm.

## Threat boundaries

| Threat | T0 guard exercised | Remains unresolved |
| --- | --- | --- |
| Malicious mirror / substituted tree | Pinned external P-256 key verifies signature over exact base/target identities | Trusted key delivery, independent file hash verification |
| Replay / rollback | Trusted caller passes per-channel installed identity and monotonic epoch/sequence high-water | Durable authenticated high-water, restore/backup rollback, epoch delegation |
| Mix-and-match | Signature covers channel, key ID, old and new tree IDs, cursor and time window | TUF roles, snapshot/timestamp consistency, CSP physical digest binding |
| Freeze | not-before <= now < expiry | Trusted clock, refresh/online freshness, offline policy |
| Malicious local path | No filesystem access in lab | Handle-relative no-follow walking, Windows reparse/ACL, POSIX modes |
| Endless/slow delivery | Constant-size policy encoding, strict names/identity length | Network and disk quotas, untrusted artifact decompression |
| Key compromise | Wrong trusted-key pin rejected | Rotation, revocation, threshold signatures, out-of-band recovery |

**Important:** SignatureVerified produced by this lab is only meaningful after the caller pins a trusted public key. The Cursor and Anchor are arguments supplied from a trusted external source, not read from untrusted update metadata. This model does not make rollback protection real if an attacker can roll back that source. It does **not** implement TUF.

## Exact candidate signature preimage (unstable lab notation)

A fixed ASCII domain and NUL separator:

    ASCII("chunkshift.trust-sig-001.t0.policy.v0") || 00

Then one-byte length-prefixed canonical lowercase ASCII channel and keyId (each 1..63 bytes, first char a-z, rest a-z0-9._-); raw 32-byte lowercase-hex-decoded old/new SHA-256 tree identities; then signed **little-endian 64-bit integers**: epoch, sequence, not-before Unix seconds, expiry Unix seconds. No extra fields, implicit defaults, Unicode, JSON aliases, unsigned cursors, or trailing ignored input. Both tree digests must differ; negative epoch/sequence and inverted validity windows are invalid.

The candidate preimage is **not a wire format**, has no extension scheme, and is not usable as a persisted compatibility contract. The signature is ECDSA P-256 + SHA-256 with 64-byte IEEE P1363 (r concatenated with s) output for this isolated lab; public keys are trusted SPKI bytes, not supplied by the payload.

Independent reference encoded with Python stdlib little-endian struct packing of (0, 2, 1800000000, 1800003600), channel stable, key root-p256, old identity = 32 zero bytes, target identity = 32 bytes 0x11:

- Preimage length: **151 B**.
- SHA256: **87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697**.

The lab validates the preimage, signature, anchor channel+key match, trusted active base, monotonicity and time, then emits an existing T4-B Approval with SignatureVerified=true; it **does not** call T4-B CommitPointer, inspect real tree bytes, nor couple cursor persistence to a filesystem swap.

## Test scope and acceptance

The isolated xUnit suite covers deterministic independent byte vector, good pinned P-256 signature, every signed field tampered individually, other key, invalid key data, wrong channel/key ID, malformed/truncated signature, invalid names and digests, expired/future release, equal/older cursor replay, wrong installed base and later epoch. Build/test on **GitHub-hosted Ubuntu 24.04 and Windows 2025** via a PR-only specialized workflow; preserve general CI/CodeQL/Heavy/Benchmark requirements.

Pass means T0_MECHANICS_PASS only, not algorithm/package-matrix ADOPT, not a signed on-disk envelope or trust RFC acceptance.

## Next separate gates

1. #185 R1: full threat/delegation decision: offline channels, trusted time, root/key rotation and compromised issuer, crash-safe trusted high-water persistence, recovery and downgrade authorization. Compare TUF/DSSE vs documentation-only integration; avoid inventing a standalone update framework.
2. #185 R2 (TRUST-SIG-001): preregister S0–S3 size/sign/verify/NativeAOT portability matrix on actual GitHub-hosted x64/arm64, without unsupported platform coverage claims.
3. #184 T4-C2: only after the trust boundary is frozen, compare Linux parent fsync/secure open and Windows handle/reparse/sharing failure modes; no shipping publisher or tree-format commitment until both contracts meet their exit gates.
4. #251 remains OPEN. Do not freeze #140 Patching API or publish as NuGet until all required research dispositions exist.

**No changes to** production src/, CSM/CSP semantics or formats, public SDK, update distribution endpoints, or any release policy.
