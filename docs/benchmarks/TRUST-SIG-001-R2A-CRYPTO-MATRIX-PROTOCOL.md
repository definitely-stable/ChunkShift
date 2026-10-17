# TRUST-SIG-001 / R2-A — .NET 10 cryptographic algorithm matrix

Owner: issue #185, research freeze #251; follows merged T0 #381 and R1 #382.
Status: RESEARCH-ONLY, NO TRUST-ALGORITHM ADOPT OR SHIPPING ENVELOPE.

## Preregistered design

Fixed 151-byte T0 policy preimage; SHA-256 87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697. Isolated zero-NuGet .NET 10 console consumer in benchmarks/TrustSigR2Matrix; no production source or persisted format.

Six unambiguous candidate rows: S0 ECDSA NIST P-256 SHA256/P1363 (mandatory baseline); S1 standalone Ed25519 (not in .NET 10 BCL; not measured or substituted by a composite implementation); S2 ML-DSA-44 and ML-DSA-65; S3 composite ML-DSA-44+ECDSA-P256 and ML-DSA-44+Ed25519. ML-DSA and composite algorithms execute only when the platform's BCL provider explicitly reports supported. Unsupported is a first-class provider_unavailable verdict, never a pass.

On each supported row: ephemeral signer, export SPKI, independently import public-only verifier, sign and verify exact domain preimage; reject independent wrong key, modified message, bit-flipped and truncated signature. Any false acceptance, key substitution, wrong vector, abnormal provider exception or S0 failure is a fatal error. Do not serialize private keys or signatures to evidence.

After eight warmups take 128 sequential single-operation Stopwatch samples per supported sign/verify lane, record medians in microseconds, DER SPKI and signature sizes, runtime, OS, architecture and NativeAOT status in schema chunkshift.trust-sig-001.r2.crypto-matrix.v0. These are runner-dependent screening samples, not stable performance claims or confidence bounds; use no fabricated results.

GitHub-hosted host matrix: ubuntu-24.04 x64, ubuntu-24.04-arm arm64, windows-2025 x64, macos-15 arm64. Require both Release JIT and self-contained NativeAOT, warnings as errors, executable verification and JSON validation. Failure to publish or run AOT is FAILURE, not JIT fallback. No self-hosted runner.

## Decision rules

R2A_MATRIX_EVIDENCE_PASS only if every host has JIT+NativeAOT audited JSON, mandatory S0, 6 rows with supported/unsupported distinctions, all project CI/CodeQL/Heavy/Benchmark gates successful, and final PR head reviewed. If an OS is unavailable, record incomplete evidence and do not claim cross-platform portability.

Never infer cryptographic interoperability with TUF, DSSE, minisign, Sigstore, X.509, or external tooling solely from an internal signature roundtrip. S1 requires separate pinned dependency/interoperability research. ML-DSA provider availability varies by OS and native libraries. NativeAOT success does not establish trust-anchor distribution, key custody, root/targets signature thresholds, timestamp freshness, or rollback-resistant persistent high-water; R1 remains a logical trust oracle. End-user packaging is not assessed.

Working choice remains documentation-only/external TUF-style framework as architectural baseline against which an optional separate signer package can be justified. No production signer/publisher, no format changes, no public SDK or release; #185, #184 and #251 remain OPEN.

Primary upstream sources: https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/libraries ; https://learn.microsoft.com/dotnet/standard/security/cross-platform-cryptography ; https://learn.microsoft.com/dotnet/api/system.security.cryptography.compositemldsa.isalgorithmsupported?view=net-10.0 ; https://theupdateframework.github.io/specification/latest/

Next R2-B: independent host-output audit, sample variability, pinned standalone Ed25519 dependency and interop against real external tooling, external TUF/DSSE adoption decision and threat-model boundary.
