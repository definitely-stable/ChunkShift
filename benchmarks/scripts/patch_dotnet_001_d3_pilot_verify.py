#!/usr/bin/env python3
"""Independently replay raw D3P1 envelope byte accounting, never holdout."""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import re
import struct
from pathlib import Path

D0_SHA = "f0e4ff2e47905f22772f1642b105d6732b955070393eded3bf43c5b39ac82cee"
SCAN_SHA = "9c2e6beb1f1086e9cd95e37d512950e355334d78e41be11718c415bccdf95b84"
PAIRS_SHA = "5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9"
SCHEMA = "chunkshift.patch-dotnet-d3-physical-calibration.v1"
CEILING = 1_838_636
H0_TOTAL = 16_029_352


class VerificationError(ValueError):
    pass


def require(ok: bool, code: str) -> None:
    if not ok:
        raise VerificationError(code)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def key(row: dict) -> tuple[str, str, str, str]:
    return tuple(row[k] for k in ("family", "base", "target", "path"))


def verify(results_path: Path, artifacts: Path, baseline_path: Path, scan_path: Path) -> dict:
    require(sha(baseline_path.read_bytes()) == D0_SHA and
            sha(scan_path.read_bytes()) == SCAN_SHA, "D3P1_FROZEN_UPSTREAM_EVIDENCE")
    baseline = json.loads(baseline_path.read_text())
    scanner = json.loads(scan_path.read_text())
    results = json.loads(results_path.read_text())
    require(results.get("schema") == SCHEMA and
            results.get("sourcePairsSha256") == PAIRS_SHA and
            results.get("baselineSha256") == D0_SHA and
            results.get("scannerSha256") == SCAN_SHA and
            results.get("role") == "calibration" and
            results.get("protocol") == "D3P1-fixed-raw-CDN1-24-byte-envelope",
            "D3P1_SCOPE_OR_PROVENANCE")
    original = {key(x): x for x in baseline["files"]}
    scanned = {key(x): x for x in scanner["rows"]}
    rows = {key(x): x for x in results["rows"]}
    require(len(original) == len(scanned) == len(rows) == 1242 and
            set(original) == set(scanned) == set(rows), "D3P1_ROW_MEMBERSHIP")
    h0sum = chosen = unconditional = 0
    verified = selected = 0
    seen_files = set()
    family = collections.defaultdict(lambda: collections.Counter())
    for k, r in rows.items():
        b = original[k]
        s = scanned[k]
        cost = b["patchBytes"]
        require(r["d0PhysicalBytes"] == cost and
                r["targetSha256"] == s["targetSha256"] and
                r["baseSha256"] == s["baseSha256"],
                "D3P1_ROW_SOURCE_DRIFT")
        if s["scannerStatus"] == "VERIFIED_BOTH":
            verified += 1
            name = r["candidateArtifact"]
            require(isinstance(name, str) and
                    re.fullmatch(r"ps-(?:linux|win)-(?:fdd|sc)-[0-9]{4}\.d3p", name) and
                    name not in seen_files, "D3P1_ARTIFACT_NAME")
            seen_files.add(name)
            blob = (artifacts / name).read_bytes()
            require(sha(blob) == r["candidateSha256"], "D3P1_CANDIDATE_SHA")
            require(len(blob) >= 24 and blob[:4] == b"D3P1" and
                    struct.unpack_from("<I", blob, 4)[0] == 1, "D3P1_HEADER")
            payload_len, sidecar_len = struct.unpack_from("<QQ", blob, 8)
            require(len(blob) == 24 + payload_len + sidecar_len and
                    len(blob) == r["candidatePhysicalBytes"] and
                    payload_len == r["candidateCspBytes"] and
                    sidecar_len == r["candidateInverseBytes"] and
                    r["candidateHeaderBytes"] == 24, "D3P1_ENVELOPE_LENGTH")
            raw = blob[24 + payload_len:]
            require(len(raw) >= 44 and raw[:4] == b"CDN1" and
                    len(raw) == 44 + 8 * s["targetSlotCount"] and
                    struct.unpack_from("<I", raw, 8)[0] == s["targetSlotCount"] and
                    raw[12:44].hex() == r["targetSha256"], "D3P1_TARGET_INVERSE_HEADER")
            n = struct.unpack_from("<I", raw, 4)[0]
            require(n == s["targetSize"], "D3P1_ORIGINAL_TARGET_LENGTH")
            prev = 0
            for i in range(s["targetSlotCount"]):
                offset = struct.unpack_from("<I", raw, 44 + i * 8)[0]
                require(prev <= offset <= n - 4, "D3P1_INVERSE_SLOT_BOUNDS")
                prev = offset + 4
            require(r["originalTargetReconstructedSha256"] == s["targetSha256"],
                    "D3P1_TARGET_DECODE_EVIDENCE")
            candidate = len(blob)
            choose = min(cost, candidate)
            unconditional += candidate
            if candidate < cost:
                selected += 1
        else:
            require(r["candidateArtifact"] is None and
                    r["candidatePhysicalBytes"] is None and
                    r["candidateInverseBytes"] is None and
                    r["originalTargetReconstructedSha256"] is None,
                    "D3P1_FALLBACK_FABRICATED")
            choose = cost
            unconditional += cost
        require(r["selectedPhysicalBytes"] == choose and
                r["selectedLane"] == ("D3P1" if choose < cost else "D0"),
                "D3P1_MIN_SELECTOR")
        h0sum += cost
        chosen += choose
        family[r["family"]]["h0"] += cost
        family[r["family"]]["selected"] += choose
        family[r["family"]]["changed"] += 1
    disk = {x.name for x in artifacts.iterdir() if x.is_file() and x.suffix == ".d3p"}
    require(disk == seen_files, "D3P1_EXTRA_ARTIFACTS")
    require(h0sum == H0_TOTAL and verified == 123 and h0sum - chosen <= CEILING,
            "D3P1_FROZEN_CEILING")
    require(results["d0PhysicalBytes"] == h0sum and
            results["selectedPhysicalBytes"] == chosen and
            results["unconditionalCandidatePhysicalBytes"] == unconditional and
            results["scannerVerifiedRows"] == verified and
            results["selectedCandidates"] == selected and
            results["measuredSavingBytes"] == h0sum - chosen and
            abs(results["measuredSavingFraction"] - (h0sum-chosen) / h0sum) < 1e-12,
            "D3P1_TOTAL_REPLAY")
    return {"status": "CALIBRATION_ONLY_PHYSICAL_D3P1_REPLAY_NOT_HOLDOUT_VERDICT",
            "changedRows": len(rows), "verifiedCandidates": verified,
            "selectedD3Candidates": selected, "measuredSavingBytes": h0sum-chosen,
            "measuredSavingFraction": (h0sum-chosen)/h0sum,
            "families": {name: dict(v) for name,v in sorted(family.items())}}


def main():
    p=argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--result",type=Path,required=True)
    p.add_argument("--artifact-dir",type=Path,required=True)
    p.add_argument("--baseline",type=Path,required=True)
    p.add_argument("--scanner",type=Path,required=True)
    args=p.parse_args()
    try:
        print(json.dumps(verify(args.result,args.artifact_dir,args.baseline,args.scanner),
                         sort_keys=True,indent=2))
    except (VerificationError,ValueError,KeyError,OSError,struct.error) as error:
        p.error(str(error))


if __name__ == "__main__":
    main()
