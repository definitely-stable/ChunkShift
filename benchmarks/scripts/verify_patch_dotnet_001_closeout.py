#!/usr/bin/env python3
"""Source-free mechanical verification of the PATCH-DOTNET-001 D3P1 DEFER ledger.

Checks only durable original artifact digests and byte/conservation invariants.
It cannot independently re-run the hosted D3P1 Apply without source archives.
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import unittest

_DEFAULT = (
    Path(__file__).resolve().parents[1]
    / "experiments"
    / "patch-dotnet-001-d3p1-first-release-closeout-20261110.json"
)

SOURCE_ZIP_SHA = "f27a98829dc0abc3c7dac03e32cd9b383bae8e7219b78d38bb62dbffea8ce729"
PAIRS_SHA = "5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9"
SOURCE_SHA = "0067f7c37eda3db2e0c6f58a001f3c99ddae09a5"
D3P1_JSON_SHA = "56ef0c2639d010aff943943446b8a1b59dec66cb1fa363be80323a303d0ec4fa"
REPLAY_JSON_SHA = "aef549ec5838369fb19250e53f0a94eb7ce55eaa9e90b7c0f4f81c566108fcc5"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def verify(data: dict) -> dict:
    """Fail closed when original cohort, provenance or physical byte accounting drifts."""
    require(data["schema"] == "chunkshift.patch-dotnet-first-release-closeout.v1", "schema")
    require(data["evidenceId"] == "PATCH-DOTNET-001/D3P1-FIRST-RELEASE-DEFER-20261110-001", "evidenceId")
    require(data["experimentId"] == "PATCH-DOTNET-001", "experimentId")
    require(data["firstReleaseDecision"] == "DEFER_DOTNET_SEMANTIC_TRANSFORMS__NO_PRODUCTION_ADOPT", "decision")
    for key in ("defaultBuilderChanged", "cspV1WireChanged", "publicApiChanged", "evaluationUnsealed"):
        require(data[key] is False, f"forbidden release change: {key}")
    p = data["provenance"]
    expected = {
        "sourceRepo": "definitely-stable/ChunkShift",
        "issue": 221, "experimentalPr": 281,
        "sourceCommit": SOURCE_SHA, "hostedRun": 38123812844,
        "artifactId": 11695974097,
        "zipSha256": SOURCE_ZIP_SHA,
        "pairsSha256": PAIRS_SHA,
        "originalD3PilotJsonSha256": D3P1_JSON_SHA,
        "originalIndependentReplayJsonSha256": REPLAY_JSON_SHA,
        "datasetRole": "CALIBRATION_ONLY_NOT_FRESH_HOLDOUT",
        "softwareFamilyCount": 1,
        "upstreamProduct": "PowerShell",
        "versionPair": "7.5.3 -> 7.5.4",
        "zipMemberCount": 126,
        "verifiedSha256SumEntries": 125,
        "validatedEnvelopes": 123,
    }
    for key, value in expected.items():
        require(p[key] == value, f"provenance drift: {key}")
    for key in (
        "all125MembersShaVerified", "all123EnvelopeShaVerified",
        "all123EnvelopeCspAndCdn1MagicVerified",
    ):
        require(p[key] is True, f"archive audit missing: {key}")
    for key in ("baselineEvidenceSha256", "scannerEvidenceSha256"):
        require(len(p[key]) == 64 and all(c in "0123456789abcdef" for c in p[key]), key)
    require(p["hostedReconstructionClaim"].startswith("SUCCESS in original GitHub-hosted"),
            "hosted Apply must be distinguished from source-free audit")

    families = data["families"]
    require([x["id"] for x in families] ==
            ["ps-linux-fdd", "ps-win-fdd", "ps-win-sc"], "frozen deployment identities")
    for f, expected_rows, expected_h0, expected_candidates in zip(
        families, (263, 282, 697), (1922166, 2045059, 12062127), (57, 60, 6), strict=True
    ):
        require(f["changedRows"] == expected_rows, "family row count changed")
        require(f["h0Bytes"] == expected_h0, "family baseline bytes changed")
        require(f["selectedBytes"] == f["h0Bytes"], "family unexpected selected savings")
        require(f["verifiedCandidatePairs"] == expected_candidates, "family candidate count")
    a = data["aggregate"]
    require(a["changedRows"] == sum(f["changedRows"] for f in families) == 1242, "changed row conservation")
    require(a["strictScannerVerifiedPairs"] == sum(f["verifiedCandidatePairs"] for f in families) == 123,
            "verified candidate conservation")
    require(a["baselinePhysicalBytes"] == sum(f["h0Bytes"] for f in families) == 16029352,
            "H0 physical bytes")
    require(a["selectedPhysicalBytes"] == sum(f["selectedBytes"] for f in families), "selected bytes")
    require(a["selectedD3P1"] == 0 and a["measuredSavingsBytes"] == 0 and
            a["selectedPhysicalBytes"] == a["baselinePhysicalBytes"], "false D3 adoption")
    require(a["envelopeHeaderLengthBytes"] == 24, "D3P1 header length")
    require(a["candidateHeadersBytes"] == a["envelopeHeaderLengthBytes"] * 123 == 2952,
            "header accounting")
    require(a["candidateRawInverseBytes"] == 373884, "CDN1 map accounting")
    require(a["candidateNormalizedCspBytes"] == 2162832, "observed D3 CSP total")
    require(a["candidateSubsetH0Bytes"] == 2172190, "D3 paired H0 total")
    candidate_sum = (
        a["candidateNormalizedCspBytes"]
        + a["candidateRawInverseBytes"]
        + a["candidateHeadersBytes"]
    )
    require(a["candidatePhysicalEnvelopeBytes"] == candidate_sum == 2539668,
            "full D3P1 physical envelope conservation")
    unconditional = (
        a["baselinePhysicalBytes"] - a["candidateSubsetH0Bytes"]
        + a["candidatePhysicalEnvelopeBytes"]
    )
    require(a["unconditionalPhysicalBytes"] == unconditional == 16396830,
            "full population unconditional D3 accounting")
    require(a["candidateRegressBytes"] == unconditional - a["baselinePhysicalBytes"] == 367478,
            "real candidate regression accounting")
    require(a["positiveNormalizedCspCandidates"] == 4, "positive-candidate count")
    require(a["observedGrossPositiveOnlyBytes"] == 9358, "conditioned gross saving")
    require(
        a["observedGrossPositiveOnlyBytes"] ==
        a["candidateSubsetH0Bytes"] - a["candidateNormalizedCspBytes"],
        "conditional saving not conserved",
    )
    expected_pct = 100 * a["observedGrossPositiveOnlyBytes"] / a["baselinePhysicalBytes"]
    require(abs(a["observedPayloadConditionedMaximumSavingPercent"] - expected_pct) < 1e-10,
            "conditioned bound denominator or unit")
    require(expected_pct < a["adoptionPopulationPhysicalSavingsThresholdPercent"] == 5,
            "unexpected 5 percent gate")
    require(len(data["caveats"]) >= 5, "scientific limitations omitted")
    return {
        "decision": data["firstReleaseDecision"],
        "changedRows": a["changedRows"],
        "candidatePairs": a["strictScannerVerifiedPairs"],
        "h0Bytes": a["baselinePhysicalBytes"],
        "unconditionalBytes": a["unconditionalPhysicalBytes"],
        "selectedBytes": a["selectedPhysicalBytes"],
        "regressBytes": a["candidateRegressBytes"],
        "observedBoundPercent": round(expected_pct, 8),
        "sourceZipSha256": p["zipSha256"],
    }


class CloseoutContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = json.loads(_DEFAULT.read_text(encoding="utf-8"))

    def test_original_ledger_passes(self) -> None:
        self.assertEqual(verify(self.source)["selectedBytes"], 16029352)

    def test_original_cohort_cannot_be_changed(self) -> None:
        a = copy.deepcopy(self.source)
        a["families"][0]["changedRows"] = 262
        with self.assertRaises(ValueError):
            verify(a)

    def test_missing_inverse_metadata_must_not_be_omitted(self) -> None:
        a = copy.deepcopy(self.source)
        a["aggregate"]["candidateRawInverseBytes"] = 0
        with self.assertRaises(ValueError):
            verify(a)

    def test_holdout_or_adopt_cannot_be_claimed(self) -> None:
        for key, value in (
            ("firstReleaseDecision", "ADOPT"),
            ("evaluationUnsealed", True),
        ):
            a = copy.deepcopy(self.source)
            a[key] = value
            with self.assertRaises(ValueError):
                verify(a)

    def test_source_zip_digest_is_immutable(self) -> None:
        a = copy.deepcopy(self.source)
        a["provenance"]["zipSha256"] = "0" * 64
        with self.assertRaises(ValueError):
            verify(a)

    def test_false_5_percent_improvement_is_rejected(self) -> None:
        a = copy.deepcopy(self.source)
        a["aggregate"]["observedGrossPositiveOnlyBytes"] = 801468
        with self.assertRaises(ValueError):
            verify(a)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ledger", type=Path, default=_DEFAULT)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        result = unittest.main(argv=["closeout-contracts"], exit=False, verbosity=2)
        raise SystemExit(0 if result.result.wasSuccessful() else 1)
    summary = verify(json.loads(args.ledger.read_text(encoding="utf-8")))
    print(json.dumps(summary, sort_keys=True))


if __name__ == "__main__":
    main()
