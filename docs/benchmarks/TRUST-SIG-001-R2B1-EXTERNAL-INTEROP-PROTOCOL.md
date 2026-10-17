# TRUST-SIG-001 / R2-B1 — external DER/DSSE/Ed25519 interoperability protocol

Experiment owner: #185; research freeze #251; follows R2A (#383). Status: LAB-ONLY, NOT ALGORITHM / ENVELOPE / DISTRIBUTION ADOPT.

## Problem

R2A showed S0 .NET P-256 correctness and portability for four GitHub-hosted host types, but a same-library signer/verifier roundtrip does not prove interoperability. DER ECDSA is not IEEE P1363; an external signature envelope also needs to authenticate payload *type*, not only payload bytes. Standalone Ed25519 is not a public .NET 10 BCL API and is not equivalent to CompositeMLDsa with Ed25519.

This phase uses zero added NuGet packages. All private keys are ephemeral, exist only under isolated temporary runner directories and are deleted before evidence upload. The emitted evidence contains **no private keys or signatures**. R2B1 chooses independent OpenSSL as the first counterparty; it does not claim minisign, TUF or Sigstore compatibility.

## Locked vectors and format boundaries

Fixed T0 151-byte policy preimage; independent SHA-256 87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697.
DSSE payloadType candidate: `application/vnd.chunkshift.trust-sig-001.t0.policy`. This is a LAB label, **not registered or accepted as a production MIME type**.
Follow DSSE PAE exactly: ASCII `DSSEv1 ` + ASCII decimal UTF-8 byte length of type + space + UTF-8 type bytes + space + decimal payload-byte length + space + exact payload. Independent Python stdlib reconstructs the PAE and compares it byte-for-byte with .NET output.

ECDSA algorithm: NIST P-256, SHA-256. .NET emits P1363 (64 B) and RFC3279 DER ECDSA signatures. OpenSSL `dgst -sha256 -verify` must accept DER for both raw policy and DSSE PAE and reject P1363 when presented as DER. OpenSSL signs the *same* bytes using the .NET-generated ephemeral PKCS8 private key; .NET must validate OpenSSL signatures using independently imported public-only SPKI PEM. That proves cross-implementation signature format and algorithm agreement, not independent key lifecycle/custody.

The external Ed25519 control is generated/signed/verified using OpenSSL `genpkey -algorithm ED25519` and `pkeyutl -rawin`. It proves the OpenSSL baseline (including wrong-key and wrong-payload-type rejection) but **does not prove Ed25519 interoperability with a .NET signer or verifier**; that requires a separate pinned library or consumer boundary. Do not report this as S1 .NET supported.

## Mandatory negative coverage

- Wrong trusted P-256 public key, policy-byte tamper, DSSE type mismatch, DER truncation, and P1363 passed to DER verifier must all fail in OpenSSL.
- .NET must reject externally produced signature bytes on a modified policy, changed DSSE type, truncated/bit-flipped DER, and different public key. It must accept both OpenSSL signed policy and OpenSSL signed DSSE PAE.
- Pure OpenSSL Ed25519 valid signature must pass. Wrong public key, changed DSSE PAE type, corrupted Ed25519 signature must fail.
- Fail the job on an unavailable OpenSSL implementation or an unsupported flag; do not silently skip rows.
- The source tree, keys or signature contents must **not** appear in uploaded artifacts. Keep only the JSON capability/interop verdict and host OpenSSL/.NET toolchain. Output schema `chunkshift.trust-sig-001.r2b.external-interop.v0` and `nativeAot` mode are asserted.

## Measurement and host gates

Run on GitHub-hosted Ubuntu 24.04 x64 and ARM64 with pinned .NET 10 via global.json and OpenSSL preinstalled by runner. Build standalone console in Release warnings-as-errors and publish NativeAOT warnings-as-errors. Exercise the independent Python/OpenSSL driver for JIT and NativeAOT *separately*, with fresh ephemeral keys each time. Validate both evidence files, archive only those and a toolchain text dump (including `openssl version -a`). Results are an interop pass/fail **not a throughput benchmark**. No timing ranking from this phase.

Given process/container/OpenSSL build variance, do not infer Windows or macOS OpenSSL command portability until their own runs are completed. Host labels and exact external tool versions are evidence, not architectural guarantees.

## External trust framework decision record (not adoption)

Compare distinct layers and resist creating a bespoke combination:

| Option | Capability this experiment exercises | Capability still absent |
| --- | --- | --- |
| Raw detached P-256 signature | OpenSSL/.NET sign/verify, DER/P1363 agreement when format is explicit | Trust anchor, secure root rotation, high-water, TUF snapshot/timestamp, update authorization |
| DSSE PAE around a typed artifact declaration | Exact preimage/type-binding (with S0 P-256 across two tools) | Key management, trust roles, threshold verification, consistent snapshot and expiry; DSSE does not fix signature algorithm or envelope trust |
| TUF-based distributor / verified existing updater | None in this first OpenSSL-only step | Must later evaluate a real TUF client, root/targets/snapshot/timestamp role authorization, rollback/freeze behavior, offline update policy, local trusted cursor persistence |
| minisign / standalone Ed25519 consumer | OpenSSL pure Ed25519 only, *not* minisign format interop | Pin NSec/libsodium or another library and actual minisign verification, dependency/AOT/native-asset matrix |
| First-party ChunkShift optional signing NuGet | No shipping implementation | Security ownership, interoperability, keys and root trust, stable security API, maintenance burden, formal RFC and user demand |

**Interim architecture recommendation:** prefer a documented external TUF-style updater as the primary distribution/trust boundary, with ChunkShift producing/verifying only manifest/patch content identities. Only introduce optional signing integration if independent real consumers require it. DSSE may be a portable signed statement *inside a separately trusted framework*, but is not itself a root-policy/anti-rollback solution.

## Exit verdict and next experiment

R2B1_OPENSSL_INTEROP_PASS requires cross-tool P-256 DER and DSSE proof + external Ed25519 control on x64 and ARM64 in JIT and NativeAOT, with negative test oracle and final PR-head green CI/CodeQL/Heavy/Benchmark. This is *not* full R2B acceptance or TRUST-SIG-001 ADOPT. If missing, remain DRAFT and record partial evidence.

Next R2B2: actual dependency-pinned standalone Ed25519 interop against a second .NET consumer and minisign, with JIT/NativeAOT and OS asset review; separately execute an existing real TUF client workflow against compromised metadata and root rotation, then propose RFC #185 integration/ship-none outcome.

Primary references:
- https://github.com/secure-systems-lab/dsse/blob/master/protocol.md
- https://github.com/secure-systems-lab/dsse/blob/master/governance/02-scope.md
- https://docs.openssl.org/3.3/man1/openssl-pkeyutl/
- https://theupdateframework.github.io/specification/latest/
- docs/architecture/RFC-0004-core-0.1-deferred-product-concerns.md
