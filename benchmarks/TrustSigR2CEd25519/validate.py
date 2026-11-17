#!/usr/bin/env python3
"""Independent static contract audit for R2-C JIT and actual NativeAOT JSONs."""
import json
import sys

assert len(sys.argv) == 3, "Expected JIT and NativeAOT report paths"
jit, aot = [json.load(open(path, encoding="utf8")) for path in sys.argv[1:]]
checks = [
    "rfc8032-vector-and-independent-dsse-pae",
    "nsec-pure-ed25519-to-openssl-policy-and-dsse",
    "openssl-pure-ed25519-to-nsec-policy-and-dsse",
    "openssl-rejects-wrong-key-type-policy-and-malformed-signatures",
    "nsec-validates-openssl-signatures-and-rejects-negative-cases",
]
for expected_mode, report in (("jit", jit), ("nativeaot", aot)):
    assert report["schema"] == "chunkshift.trust-sig-001.r2c.nsec-ed25519.v0"
    assert report["claim"] == "EXPERIMENT_ONLY_NO_SIGNING_PACKAGE_ADOPT"
    assert report["nsecVersion"] == "26.4.0"
    assert report["nsecAlgorithm"] == "pure-Ed25519-not-Ed25519ph"
    assert report["dotnetMode"] == expected_mode
    assert report["t0PreimageBytes"] == 151
    assert report["t0PreimageSha256"] == "87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697"
    assert report["checks"] == checks
    assert report["rawPublicKeyBytes"] == 32
    assert report["publicPkixSpkiBytes"] == 44
    assert report["signatureBytes"] == 64
    assert report["privateKeysInEvidence"] is False
    assert report["minisignFormatVerified"] is False
    assert report["trustedDistributionVerified"] is False
    assert report["nsecKeyImportOpenSsl"] is True
    assert report["openssl"].startswith("OpenSSL 3")
assert jit["dssePaeSha256"] == aot["dssePaeSha256"]
assert jit["arch"] == aot["arch"]
print("R2C_REAL_NSEC_OPENSSL_INTEROP_PASS", jit["os"], jit["arch"])
