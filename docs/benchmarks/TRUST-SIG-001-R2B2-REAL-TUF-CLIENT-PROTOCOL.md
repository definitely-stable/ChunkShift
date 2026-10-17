# TRUST-SIG-001 / R2-B2 — real external TUF client attack pilot

Owner: [issue #185](https://github.com/definitely-stable/ChunkShift/issues/185); related #184 and release freeze #251.
Status: **PREREGISTERED RESEARCH LAB**. No first-party signature package, updater, CSM/CSP/PublicAPI/persisted format or release modification.

## Decision question

After R2A demonstrated .NET P-256 host support and R2B1 demonstrated .NET/OpenSSL DER and byte-exact DSSE preimage compatibility, should ChunkShift implement its own trusted publisher/update-policy format, or leave distribution trust to an already developed TUF client? This pilot experimentally exercises a **real reference TUF client's cryptographic and version-verification code** rather than another custom Boolean signature oracle.

### Independent implementation and exact supply-chain selection

- Actual client: `tuf.ngclient.Updater` from Python-TUF **7.0.1** (PyPI, released 2026-09-02), not a fake or wrapper with disabled verification.
- Reference repository simulator used only as the source of **signed metadata/target bytes and FetcherInterface**, copied from upstream via a second read-only GitHub checkout at immutable commit `49eb1da48c58cf0cbb0f2e490f7ad39fbd8c1f15` (annotated tag v7.0.1). The simulator is a test fixture and can sign authorized roles to create adversarial cases; actual `Updater.refresh`, role verification, signatures, version checks, metadata cache and `Updater.download_target` remain real.
- Pip install exact `tuf==7.0.1`, `securesystemslib[crypto]==1.5.1`; copy the precise resolved dependency list and interpreter information to host evidence. This is a **version-pinned laboratory dependency**, not a reproducible hash-locked production supply-chain proof.
- No signing key, actual signed metadata, target test bytes, private-key PEM or downloaded update archive is uploaded. Each case uses its own temporary client cache with a newly generated signed reference repository; output is JSON verdicts only. Mock transport makes CI deterministic and avoids dependency on a public internet mirror *during the test*, but cannot prove network/TLS/latency availability.

Primary upstream docs: https://theupdateframework.readthedocs.io/en/latest/api/tuf.ngclient.updater.html ; https://github.com/theupdateframework/python-tuf/tree/v7.0.1/tests ; https://theupdateframework.github.io/specification/latest/ .

## Frozen adversarial scenarios

| ID | Repository/client condition | Required observation |
| --- | --- | --- |
| 1 happy_verified_target | Correct root, timestamp, snapshot, targets, signed SHA-256 target and matching downloaded bytes | `Updater.refresh`, `get_targetinfo` and `download_target` succeed, target digest agrees |
| 2 timestamp_rollback | Client has previously cached genuinely signed timestamp v2; mirror presents genuinely signed lower v1 | `BadVersionNumberError`; cached v2 remains trusted |
| 3 expired_timestamp | Previously valid client, but newly served signed timestamp expired under runner wall-clock | `ExpiredMetadataError` |
| 4 forged_targets | New advertised targets version without signatures from authorized targets keys | `UnsignedMetadataError` |
| 5 snapshot_mix_and_match | Signed snapshot binds exact targets hash/length; signed targets changed, advertised version manually matches but digest does not | `LengthOrHashMismatchError` |
| 6 corrupted_target_download | Valid authorized target metadata but mirror returns different **same-length** target bytes | `LengthOrHashMismatchError` or downloader rejection; no untrusted content returned as accepted |
| 7 root_new_keys_without_old_threshold | Root v2 signed only by newly designated keys, not the previously trusted root keys | `UnsignedMetadataError`; cached trusted root stays v1 |
| 8 valid_dual_signed_root_rotation | Same root v2 metadata signed by **both old and new authorized root key thresholds** | Refresh succeeds and trusted cached root advances to v2/new root key set |
| 9 nonconsecutive_root_version | Root served in versioned v2 path internally claims v3 | `BadVersionNumberError` |

All cases must PASS or the CI job fails with full traceback. A negative test passes only when the **expected exception class** is raised, not on any arbitrary error; no skipped unavailable scenarios. The signed target is an inert illustrative byte string, **not** a real CSP stream. Runner clocks are not simulated or trusted: an expiration failure relative to the CI wall clock does not establish adversarial clock-tampering resistance.

## Host gates and evidence

GitHub-hosted Ubuntu 24.04, Windows 2025 and macOS 15, Python 3.13 setup pinned to action commit; read-only exact upstream checkout; fresh pip install. Retain JSON results and runtime/dependency versions only. Output schema `chunkshift.trust-sig-001.r2b2.real-tuf-updater.v0`, exactly 9 stable named cases, explicit capability/limitation flags. The PR must also pass full general CI, CodeQL, Heavy and Benchmark gates on the **same final HEAD**.

### Safety interpretation

Passing this lab demonstrates that the existing TUF client already provides essential **root/targets/timestamp/snapshot verification** and rejects selected real attacks. It does **not** prove complete TUF adoption for ChunkShift, consistent snapshot publish semantics, secure bootstrap root distribution, trusted wall-clock/freeze or HTTP transport robustness, signed sequence or channel/epoch semantics unique to ChunkShift, or rollback-resistant local installed-tree/high-water state. A privileged local attacker who rolls back the *client's cached metadata and installed bytes together* is outside this test; TUF alone should not be claimed to solve that without stronger trust assumptions.

A malicious mirror blocking new metadata can cause a denial of service before metadata expiry, and a trusted clock is necessary to enforce expiry. Compromise of the full threshold of authorized signing keys is an unsolved trust compromise, not something the client can magically detect. No production tree pointer, disk fsync/atomic rename, power-cut nor secure key custody are tested.

### Preliminary decision policy

Prefer documentation and interoperability with existing independently maintained updater implementations as **baseline**. Only design an optional ChunkShift signing package after an explicit requirement forces ownership of signing and a review confirms that TUF/DSSE tooling cannot satisfy it. RFC #185 remains OPEN, and all T4-C2/shipping/Research Freeze #251 gates remain intact even after a real TUF client laboratory PASS.

Next after R2-B2: a separately isolated, dependency-pinned **standalone .NET Ed25519** interoperability/NativeAOT pilot against OpenSSL (then minisign if relevant), and a full end-to-end TUF target-to-ChunkShift-identity mapping, protected installed-cursor/OS publication threat model. No premature production ADOPT.
