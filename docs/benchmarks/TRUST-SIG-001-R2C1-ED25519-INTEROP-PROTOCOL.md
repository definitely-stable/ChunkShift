# TRUST-SIG-001 / R2-C1 — pinned standalone NSec Ed25519 JIT/NativeAOT interoperability lab

Owner: issue #185; cross-links #184, #251. Predecessors: T0 #381; R1 #382; R2A #383; R2B1 #384; R2B2 #385. Status: **RESEARCH ONLY; NOT SIGNATURE ALGORITHM, PACKAGE OR UPDATE DISTRIBUTION ADOPT**.

## Decision problem

R2A demonstrated cross-platform built-in P-256 but .NET 10 offers no equivalent standalone built-in pure Ed25519 API. R2B1 showed only an **external OpenSSL Ed25519 control**. R2B2 independently exercised an actual TUF client, but neither resolved .NET standalone Ed25519 dependency costs. This lab supplies empirical answers without importing native dependencies into ChunkShift production.

Proposed competitor: [NSec.Cryptography 26.4.0](https://www.nuget.org/packages/NSec.Cryptography/26.4.0), managed .NET wrapper backed by libsodium (transitive libsodium >=1.0.22, <1.0.23). Pin NSec only via the **nearest Directory.Packages.props inside benchmarks/TrustSigR2CEd25519**: root central-package versions are untouched. The runner records the *resolved* libsodium and native library inventory; compatibility must not be inferred solely from package metadata.

External crypto implementation: independently installed OpenSSL 3. RFC 8032 §7.1 vector 1 (seed `9d61b19d...`, signature `e5564300...`) is verified against NSec **pure Ed25519** with empty message (NOT Ed25519ph). The ordinary 151-byte T0 policy preimage SHA-256 is pinned to `87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697`. Lab-only DSSE type is `application/vnd.chunkshift.trust-sig-001.t0.policy`. DSSE PAE byte length and concatenation follow the upstream `DSSEv1` specification and are independently reproduced by Python.

## Experimental matrix / hostile cases

| Test | Expected outcome |
| --- | --- |
| RFC 8032 §7.1 Ed25519 vector 1 | Exact 32-byte key and 64-byte signature plus verified empty message; rejects altered message |
| NSec 26.4.0 → OpenSSL 3 | Independently exported RFC 8410 44-byte public SPKI; external verification of NSec Ed25519 signatures for both T0 policy and DSSE PAE |
| OpenSSL 3 → NSec 26.4.0 | Independent OpenSSL signer emits plain Ed25519 over policy and DSSE PAE; NSec imports public SPKI and verifies both |
| Negative controls in OpenSSL | Different public key, changed policy byte, changed DSSE payload type, corrupted and truncated signature always reject |
| Negative controls in NSec | Wrong imported public key, modified content, changed DSSE type, changed DSSE content, corrupt and truncated signatures reject |
| JIT / NativeAOT | A separately compiled native executable (warnings as errors) must re-run full cross-implementation signing/verification; no silent JIT fallback |
| Dependencies / native assets | Record exact resolved NSec/libsodium versions, OpenSSL version, operating system, RID and NativeAOT output manifest |
| Evidence/data safety | Store only summary JSON and dependency/toolchain text. No signatures, seed, PKCS8 private key or ephemeral keyring in GitHub uploads |

Runner targets: `ubuntu-24.04` (x64), `ubuntu-24.04-arm` (ARM64), `windows-2025` (x64), `macos-15` (ARM64), all **GitHub-hosted**. No self-hosted runners. Require an actual OpenSSL 3 executable on each host; on macOS use the preinstalled Homebrew openssl@3. If a host or NativeAOT is incompatible, **report failure/unsupported, do not claim coverage**. Do not add a production NuGet/package dependency.

No performance ranking is attempted in R2-C1. Correctness, native asset/runtime support and dependency footprint must be demonstrated before investing in reliable sign/verify measurements. Plain 32 B raw public key, 44 B DER SPKI and 64 B signature are different encodings and must not be conflated.

## Threat boundary and unresolved choices

Ed25519 interoperability at a raw signature and DSSE **PAE** level does **not** implement minisign/signify envelope parsing, DSSE JSON envelope verification, TUF root/targets/snapshot/timestamp role trust, Sigstore transparency, secure bootstrap trust, encrypted key custody, trusted time, or the rollback-resistant installed high-water demonstrated necessary in R2B2. Do not describe this as complete end-to-end signed updates. TUF and NSec answer different questions.

Exporting a plaintext private key to a temporary test directory is strictly a **laboratory-only interop necessity** to drive OpenSSL from the exact same NSec key; the source comments and tool deliberately prevent uploading this directory, automatically remove it, and never print private bytes. This should never be copied to production signers.

References:
- https://www.rfc-editor.org/rfc/rfc8032#section-7.1
- https://www.rfc-editor.org/rfc/rfc8410
- https://nsec.rocks/docs/api/nsec.cryptography.signaturealgorithm
- https://nsec.rocks/docs/api/nsec.cryptography.key
- https://nsec.rocks/docs/api/nsec.cryptography.publickey
- https://github.com/secure-systems-lab/dsse/blob/master/protocol.md
- https://theupdateframework.github.io/specification/latest/

## Acceptance and next decision

`R2C1_NSEC_INTEROP_PASS` requires all four Github-hosted configurations to pass JIT, NativeAOT, the RFC vector, bidirectional external OpenSSL signing, hostile negatives, independent JSON validator and actual resolved native dependency inventory, plus project-wide CI, CodeQL, Heavy and Benchmark gates on the final PR SHA. If a host fails, preserve negative evidence, do not silently change the security claim.

Possible next R2-C2: genuine **minisign CLI** interoperability with pinned version and its trusted/untrusted comments + signature/file-hash framing (not just Ed25519); and a decision RFC comparing P-256 BCL versus NSec/libsodium native complexity. If no high-value consumer needs a first-party signer, the preferred outcome for #185 remains documentation-only external TUF-style distribution, **not** expanding Core 0.1.0 or adopting a new authentication protocol. #185/#184/#251 remain OPEN, production PublicAPI/release BLOCKED.
