#!/usr/bin/env python3
"""TRUST-SIG-001 R2-A host-level contract checks. No crypto simulation."""
import json
import pathlib
import sys

expected = [
    "S0-ECDSA-P256", "S1-Ed25519-standalone",
    "S2-ML-DSA-44", "S2-ML-DSA-65",
    "S3-ML-DSA-44+ECDSA-P256", "S3-ML-DSA-44+Ed25519",
]
reports = [json.loads(pathlib.Path(p).read_text(encoding="utf-8")) for p in sys.argv[1:]]
assert len(reports) == 2
for want_aot, report in zip((False, True), reports):
    assert report["schema"] == "chunkshift.trust-sig-001.r2.crypto-matrix.v0"
    assert report["claim"] == "LAB_ONLY_NOT_ALGORITHM_ADOPT"
    assert report["preimageBytes"] == 151
    assert report["preimageSha256"] == "87483bae4112cd14f9641400ff195f9d2052679213186a8025ea0cb960c78697"
    assert report["nativeAot"] is want_aot, "JIT/AOT misclassified"
    assert report["warmup"] == 8 and report["samples"] == 128
    rows = report["candidates"]
    assert [x["candidate"] for x in rows] == expected
    assert rows[0]["status"] == "verified"
    assert rows[1]["status"] == "not_in_net10_bcl"
    assert all(x["status"] in ("verified", "provider_unavailable") for x in rows[2:])
    for row in rows:
        if row["status"] == "verified":
            assert row["publicSpkiBytes"] > 0 and row["signatureBytes"] > 0
            assert row["signMedianMicroseconds"] >= 0
            assert row["verifyMedianMicroseconds"] >= 0
        else:
            assert all(row[key] is None for key in (
                "publicSpkiBytes", "signatureBytes",
                "signMedianMicroseconds", "verifyMedianMicroseconds"))
assert reports[0]["architecture"] == reports[1]["architecture"]
assert [x["status"] for x in reports[0]["candidates"]] == [
    x["status"] for x in reports[1]["candidates"]], "JIT/AOT provider mismatch"
for before, after in zip(reports[0]["candidates"], reports[1]["candidates"]):
    assert before["publicSpkiBytes"] == after["publicSpkiBytes"]
    assert before["signatureBytes"] == after["signatureBytes"]
print("R2A host contract PASS:", reports[0]["os"],
      reports[0]["architecture"],
      [f'{row["candidate"]}:{row["status"]}' for row in reports[0]["candidates"]])
