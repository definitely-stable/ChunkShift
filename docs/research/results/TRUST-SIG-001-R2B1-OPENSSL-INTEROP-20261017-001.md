# TRUST-SIG-001 / R2-B1 — independently verified external signature interop evidence

Date: 2026-10-17 (UTC). Owner: #185. Research lab only; **not** adoption of signing algorithm/envelope/TUF distribution.
Initial measured PR head: df7f1e5c2b4e79d62e255cafc7517583d93f14e5.
Specialized GitHub-hosted run: https://github.com/definitely-stable/ChunkShift/actions/runs/39067322538
Protocol: [TRUST-SIG-001-R2B1-EXTERNAL-INTEROP-PROTOCOL.md](../../benchmarks/TRUST-SIG-001-R2B1-EXTERNAL-INTEROP-PROTOCOL.md)

## Original evidence audit

Downloaded both original Actions artifacts and independently verified archive bytes and SHA-256 digest against the digest published by GitHub. ZIP files contain **only** `r2b-jit.json`, `r2b-aot.json`, `r2b-toolchain.txt`. No signing private key, PEM key, or serialized signature appears in either archive. Each uploaded JSON was independently inspected for required schema, successful check names and differences between JIT/AOT and hosts.

| GitHub-hosted architecture | Artifact ID | Original ZIP SHA-256 |
| --- | ---: | --- |
| Ubuntu 24.04 x64 | 12113886998 | c8eab907f888112db5253cf35cbb3ff3ffdb1457e0d6e409841cfbfb06300df1 |
| Ubuntu 24.04 ARM64 | 12113058898 | c93b8d8e4fca0c28d4aa7e991d7c8fc043859ccfa005bf9f0f9c9046f1af7fa1 |

Actual external signer/verifier OpenSSL: **3.0.13**, both platforms. T0 policy hash: **87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697**, 151 bytes. Independently encoded DSSE PAE SHA-256: **3ce2cec8f078c3e2ef586df6f557ec1bf78aad9257e26b635d8ee8a3feca9d4e** in all four modes.

## Observed acceptance

| Tested mechanism | Ubuntu x64 JIT | x64 AOT | ARM64 JIT | ARM64 AOT |
| --- | --- | --- | --- | --- |
| .NET P-256 DER verified by external OpenSSL (policy and DSSE PAE) | PASS | PASS | PASS | PASS |
| External OpenSSL P-256 DER verified by .NET public-only verifier | PASS | PASS | PASS | PASS |
| Independent Python DSSE PAE exact byte agreement | PASS | PASS | PASS | PASS |
| P-256 wrong key, modified payload, tampered type, malformed DER, P1363/DER confusion rejected | PASS | PASS | PASS | PASS |
| OpenSSL external Ed25519 signing/verification, wrong key/type/corruption rejected | PASS | PASS | PASS | PASS |

### Measured encoding sizes (bytes; **not** timing data)

| Encoding | x64 JIT | x64 AOT | ARM64 JIT | ARM64 AOT |
| --- | ---: | ---: | ---: | ---: |
| .NET P-256 DER policy signature | 72 | 70 | 72 | 70 |
| OpenSSL P-256 DER policy signature | 71 | 72 | 71 | 71 |
| .NET P-256 IEEE P1363 policy signature | 64 | 64 | 64 | 64 |
| OpenSSL external Ed25519 signature | 64 | 64 | 64 | 64 |
| OpenSSL external Ed25519 public SPKI | 44 | 44 | 44 | 44 |

The variable P-256 DER length depends on ASN.1 integer encoding of individual (r,s), not on JIT vs AOT as a causal factor; new ephemeral keys were generated separately for each mode. The table is descriptive, not a comparative performance experiment.

## Independent limitations and engineering disposition

**Verdict: R2B1_OPENSSL_INTEROP_PASS on the original experimental HEAD**, conditional on fresh final-head repository CI and code review when incorporated. The result proves actual P-256 signature interoperability across .NET and OpenSSL in both directions, both on the T0 policy and on correctly encoded DSSE PAE, at Linux x64 and ARM64 under JIT and NativeAOT. Adversarial acceptance checks passed in both implementations.

**NOT PROVEN:**
- No standalone .NET 10 Ed25519 support. OpenSSL Ed25519 is an external-only control; it is neither a .NET nor minisign/signify interop test.
- No DSSE JSON envelope parser, verifier policy, external DSSE library interop or accepted production content-type. Only spec-compatible PAE was measured, with an invented lab-only type.
- No TUF client, authenticated role metadata, expiry, trusted clock, offline delegation, root rotation, snapshot/timestamp, or rollback-resistant high-water. Neither a byte-for-byte DSSE verifier nor this P-256 check supplies those features.
- No Windows or macOS **external OpenSSL interop** result in this slice. R2A internal .NET matrix tested them, but that result cannot be substituted.
- No supply-chain/security audit of OpenSSL itself, no actual signing-key custody, no power-cut test, no CSP/CSM/public SDK or distribution changes.

**Decision:** Continue favoring an existing external TUF-style updater as baseline architecture. Keep S0 P-256 as least-dependency portable **candidate** rather than a production-signature choice. A full R2B verdict still requires dependency-pinned .NET Ed25519 ↔ external minisign or trusted DSSE tooling plus a real TUF client/trust boundary proof, and separate platform-portability evidence. #185, #184, #251 and production PublicAPI freeze remain OPEN.
