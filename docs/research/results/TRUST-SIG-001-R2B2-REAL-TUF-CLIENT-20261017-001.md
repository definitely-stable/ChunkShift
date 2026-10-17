# TRUST-SIG-001 / R2-B2 — real Python-TUF client original host evidence

Owner: #185; related #184 and #251. Research status: **R2B2_REAL_TUF_CLIENT_LAB_PASS** for specifically exercised reference-client behavior, **NOT** TUF integration or installed-state security ADOPT.

Experiment working HEAD `519f958a2e03b624a42722ad4353f5c76ac8d9c9`. Real GitHub-hosted specialized run: https://github.com/definitely-stable/ChunkShift/actions/runs/39069127198 . Protocol: `docs/benchmarks/TRUST-SIG-001-R2B2-REAL-TUF-CLIENT-PROTOCOL.md`.

## Original independent artifact audit

Downloaded the original ZIP archives for **this exact HEAD**, independently recomputed SHA-256 of the raw ZIP bytes and verified equality with GitHub's published artifact digests. All ZIPs contain exactly **r2b2-results.json, r2b2-python-deps.txt, r2b2-toolchain.txt**; no private signing key, signed metadata object, test target or update package was uploaded.

| Runner | Artifact ID | Original ZIP SHA-256 | Cases |
| --- | ---: | --- | ---: |
| Ubuntu 24.04 x64 | 12113934814 | 6d96d7f4ddd0c4e8856fea8099737a29468741ef71bcbcfb3159a8bcd7842a4d | 10/10 PASS |
| Windows 2025 x64 | 12113738772 | d3da554c32c98b2f5515f47fc06d6b06b57373a53e05c1a19d4e472d48718520 | 10/10 PASS |
| macOS 15 ARM64 | 12114517092 | 267b557ddf06bffb150bca71b21de6d456dcf1ecf434a59f2f02292ace801af6 | 10/10 PASS |

All runners used real `tuf.ngclient.Updater` **7.0.1**, `securesystemslib==1.5.1`, resolved `cryptography==50.0.2` and `urllib3==2.8.0`, Python 3.13 and upstream reference simulator source at **immutable commit 49eb1da48c58cf0cbb0f2e490f7ad39fbd8c1f15**. The exact complete pip transitive lists and host info are retained in the archived toolchain/dependency text files, not in production dependencies.

## Observed behavior in all three hosts

| Adversarial scenario | Actual reference-client result |
| --- | --- |
| Correct signed target + downloaded SHA-256 verification | Accepted and exact 50-byte inert target |
| Remote signed timestamp v1 after cached v2 | Rejected: `BadVersionNumberError`; local v2 unchanged |
| Newly signed but expired timestamp v2 | Rejected: `ExpiredMetadataError` |
| Target v2 lacking authorized signatures | Rejected: `UnsignedMetadataError` |
| Target metadata replaced under signed snapshot with old bound SHA-256 | Rejected: `LengthOrHashMismatchError` |
| Correctly signed target metadata but wrong same-length downloaded bytes | Rejected: `LengthOrHashMismatchError` |
| New root v2 signed only by newly proposed root keys | Rejected: `UnsignedMetadataError`; trusted root stayed v1 |
| Root v2 signed by both old and new root key thresholds | Accepted; persisted trusted root v2 with new key identity |
| Root advertised as next v2 but signed content says v3 | Rejected: `BadVersionNumberError` |
| **Local cached high-water and malicious mirror both roll back** | **Expected weakness reproduced**: after genuinely trusting signed timestamp v2, attacker restores old signed local timestamp v1 and serves valid remote v1; real Updater then accepts v1 because independent rollback-resistant high-water does not exist |

The tenth scenario is a **LIMITATION PROBE**, not a failed cryptographic test, and definitely not a TUF specification defect. TUF's ordinary version checks assume the previous trusted state has not been adversarially rewritten. A separately protected high-water/root-and-installed-tree binding is needed for the stronger ChunkShift local threat model.

## Interpretation and technical decision

1. There is **no engineering justification here for reinventing TUF's root/targets/snapshot/timestamp verification protocol** for ChunkShift. A real reference updater already rejects the chosen classes of malicious metadata and establishes root key continuity.
2. A standalone DSSE preimage/P-256 signature (R2-B1) is insufficient for transport/update trust. Use an independently maintained TUF-compliant client at the distribution boundary if supported by the host application's ecosystem.
3. Strong rollback resistance of locally installed trees is *not* provided simply by placing metadata next to CSP, mirroring a JSON counter, or storing two copies on the same rewritable disk. Specify the threat actor, external trust/witness (remote high-water/TPM/OS protected storage), rollback policy, recovery UI and trustworthy clock before T4-C2 OS publisher work can claim real protection.
4. The experiment had **in-memory simulated network fetches**, short-lived fresh keys, one-key-per-role initial threshold, CI wall clock and no real HTTP mirror or power interruption. Do **not** infer network integrity/availability, trusted clock, secure key custody, offline rotation, multi-key threshold recovery or crash-safe installed pointer behavior from these tests. The filename `update.csp` contains inert non-CSP bytes only.
5. Standalone **.NET Ed25519**, actual minisign interoperability, full DSSE JSON tooling and signature-package selection remain **unverified**. NSec.Cryptography 26.4.0 was identified as a possible pinned dependency with native libsodium assets; its JIT/NativeAOT/interop correctness must be measured in a separate optional lab, not smuggled into Core.
6. Research freeze #251 and #185 remain OPEN; #140 first public Patching API and production updater/release remain BLOCKED.

**Acceptance label:** `R2B2_REAL_TUF_CLIENT_LAB_PASS` for the ten described scenarios on three GitHub-hosted platforms, subject to final PR-head generic CI/CodeQL/Heavy/Benchmark completion and review. No algorithm-adoption, external updater-adoption, signing package or operational high-water security verdict yet.

**Next:** narrow R2-C isolated .NET standalone Ed25519 (version-locked NSec/libsodium) vs OpenSSL and optionally minisign with native assets/AOT, then RFC #185 distribution ownership decision. In parallel the independent trusted-state witness design and real-file publication adversarial tests must remain separate from cryptographic lab outcomes.
