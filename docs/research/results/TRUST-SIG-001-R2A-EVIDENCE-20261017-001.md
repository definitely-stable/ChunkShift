# TRUST-SIG-001 R2-A — independent evidence audit, 2026-10-17

Owner: #185 / TRUST-SIG-001. Research scope only, not algorithm/package ADOPT.
Exact measured PR head: 32f9c860f1f39810e86ac15a44568c5dc0aee0b9.
Workflow: https://github.com/definitely-stable/ChunkShift/actions/runs/39063347605
Lab input: T0 preimage 151 bytes, SHA-256 87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697.
All four host jobs SUCCESS (Release JIT and NativeAOT, warnings as errors, hostile-signature tests and independent 2-JSON validator). R2A protocol: docs/benchmarks/TRUST-SIG-001-R2A-CRYPTO-MATRIX-PROTOCOL.md.

## Original evidence preservation

Downloaded each GitHub-hosted upload-artifact ZIP and independently SHA256-hashed the exact archive bytes; obtained hashes agree with GitHub artifact digests. Each contains r2-jit.json, r2-aot.json, r2-toolchain.txt. The JSON files contain measurements, algorithm support states, OS/architecture/runtime and JIT/AOT evidence but **no private signing material or emitted signature contents**.

| Host | Artifact ID | ZIP SHA256 |
| --- | ---: | --- |
| Ubuntu 24.04 x64 | 12111354328 | 37529de7580ac5baa146df4d8d22dd1213614229cbc2462511b60a955035aee2 |
| Ubuntu 24.04 ARM64 | 12111449100 | 7750c09f4b436882e638a9976b825bf5d8946b1f8def1c17ee1298c361b210f7 |
| Windows 2025 x64 | 12112412119 | 28bcdbaa18d6effcbcbd54271551fd278ac4068b1aef8f235164fe3c15d648f0 |
| macOS 15 ARM64 | 12112605701 | 7365834bb337e7bbeeae5c83618f9627157630fde6118fdc0ffb6ff489c19f34 |

All measured runtime versions: .NET 10.0.13; Windows reports OS 10.0.26100; Ubuntu 24.04.5; macOS 15.7.9. One hosted sample per OS/mode. The .NET SDK in the Windows toolchain was 10.0.402 (global.json requested 10.0.204 with latestFeature roll-forward). The hosted toolchain, not a frozen custom image, controls provider availability.

## Capability results

| Candidate | Linux x64 | Linux ARM64 | Windows x64 | macOS ARM64 |
| --- | --- | --- | --- | --- |
| S0 P-256 | verified JIT+AOT | verified JIT+AOT | verified JIT+AOT | verified JIT+AOT |
| S1 standalone Ed25519 | not_in_net10_bcl | not_in_net10_bcl | not_in_net10_bcl | not_in_net10_bcl |
| S2 ML-DSA-44 | provider_unavailable | provider_unavailable | verified JIT+AOT | provider_unavailable |
| S2 ML-DSA-65 | provider_unavailable | provider_unavailable | verified JIT+AOT | provider_unavailable |
| S3 ML-DSA-44+P256 | provider_unavailable | provider_unavailable | verified JIT+AOT | provider_unavailable |
| S3 ML-DSA-44+Ed25519 | provider_unavailable | provider_unavailable | provider_unavailable | provider_unavailable |

The provider verdict is conditional on THIS host/runtime/OS/cryptographic provider; it does not prove that a different Linux distribution with OpenSSL >=3.5, an updated Windows or an alternate library cannot support these algorithms. S1 was not measured and must not be characterized as tested or cryptographically unavailable in general.

## S0 host timing samples (median microseconds, sign / verify)

| Host | JIT | NativeAOT |
| --- | ---: | ---: |
| Linux x64 | 27.21 / 75.27 | 20.54 / 59.02 |
| Linux ARM64 | 24.83 / 66.88 | 24.10 / 66.07 |
| Windows x64 | 114.60 / 115.90 | 114.75 / 115.80 |
| macOS ARM64 | 149.90 / 118.02 | 166.75 / 136.27 |

The S0 public SPKI length was 91 bytes, and P1363 signature length exactly 64 bytes on all hosts/modes.

## Windows x64 crypto sizes and timing (one-host screening)

| Algorithm | Public SPKI, bytes | Signature bytes | JIT sign/verify us | AOT sign/verify us |
| --- | ---: | --- | ---: | ---: |
| S2 ML-DSA-44 | 1334 | 2420 | 316.85 / 54.05 | 339.05 / 54.00 |
| S2 ML-DSA-65 | 1974 | 3309 | 506.75 / 76.50 | 484.25 / 76.30 |
| S3 ML-DSA-44+P256 | 1398 | JIT 2490..2492; AOT 2489..2492 | 449.80 / 173.40 | 456.50 / 172.10 |

The S3 signature is NOT constant-size on Windows: initial measurement failed because it assumed exact length equality. The experiment corrected this false premise, retained mandatory signature verification and collected actual minimum/maximum length across 128 measured signs. Do not publish an unqualified fixed size for this composite scheme or treat S3's ECDSA portion as a 64-byte P1363 field.

## Audit/verdict and engineering consequences

This is screening evidence, not a security audit or statistically controlled benchmark. The measurements are microbenchmark medians on shared hosted virtual machines: runtime and clock noise, provider implementations and hardware differ; no error intervals or controlled rerun. No claims of speedup. Supported signature paths reject wrong imported SPKI, modified preimage, corrupted/truncated signature; unsupported rows are explicit and never counted as passes. NativeAOT compiled and ran all checked host programs without new crypto NuGet dependencies.

**Research verdict: R2A_MATRIX_EVIDENCE_PASS (mechanics & host support only)** provided final generic CI/CodeQL/Heavy/Benchmark checks for the updated PR head remain green. **ALGORITHM ADOPT: NO_VERDICT**, **DISTRIBUTION ADOPT: NO_VERDICT**. S0 has the only demonstrated complete host footprint in this lab. The most economical baseline for subsequent evaluation is a documented external TUF-style updater and S0 BCL verification, not a new first-party signed-patch format. Recheck this after R2-B interoperable signing tooling, standalone Ed25519 dependency audit and trusted distribution framework evaluation.

The R1 authenticated root/key roles, trusted clock, crash-safe high-water, T4-C2 OS publication and #251 research synthesis all remain unsolved production dependencies. #185/#184/#251 stay OPEN; #140/PublicAPI/publication BLOCKED.
