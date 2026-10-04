#!/usr/bin/env python3
import argparse
import json
from collections import defaultdict
from pathlib import Path

PAIRS = (
    ("dotnet-aspnetcore-win-x64", "10.0.10", "10.0.11"),
    ("dotnet-aspnetcore-win-x64", "10.0.11", "10.0.12"),
    ("dotnet-runtime-linux-arm64", "10.0.10", "10.0.11"),
    ("dotnet-runtime-linux-arm64", "10.0.11", "10.0.12"),
)

def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))

def recompute(oracle):
    rows = oracle["rows"]
    assert len(rows) == 256
    totals = {pair: [0, 0, 0, 0] for pair in PAIRS}
    h0_total = 0
    g2_total = 0
    improved = 0
    outside = 0

    for row in rows:
        pair = (row["family"], row["baseVersion"], row["targetVersion"])
        assert pair in totals
        h0 = int(row["h0CostBytes"])
        g2 = int(row["oracleCostBytes"])
        assert g2 <= h0
        saved = h0 - g2
        assert int(row["savedBytes"]) == saved
        is_improved = int(saved > 0)
        is_outside = int(
            row["oracleEncoding"] == "zstd-dictionary"
            and int(row["oracleStartDistanceBytes"]) > 256 * 1024
        )
        h0_total += h0
        g2_total += g2
        improved += is_improved
        outside += is_outside
        t = totals[pair]
        t[0] += h0
        t[1] += g2
        t[2] += saved
        t[3] += is_improved

    pair_passes = 0
    pairs = []
    for pair in PAIRS:
        h0, g2, saved, count = totals[pair]
        passed = g2 * 100 <= h0 * 97
        pair_passes += int(passed)
        pairs.append((pair, h0, g2, saved, count, passed))

    overall = g2_total * 100 <= h0_total * 95
    passed = overall and pair_passes >= 2
    return {
        "h0SampleCostBytes": h0_total,
        "g2SampleCostBytes": g2_total,
        "savedBytes": h0_total - g2_total,
        "rowCount": len(rows),
        "improvedRows": improved,
        "outsideRadiusWinners": outside,
        "overallFivePercentGate": overall,
        "pairPassCount": pair_passes,
        "status": "PASS" if passed else "MISS",
        "nextAction": "FULL_PHASE_B" if passed else "H6_O_GUARD_ONLY",
        "pairs": pairs,
    }

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--oracle", required=True)
    parser.add_argument("--verdict", required=True)
    args = parser.parse_args()
    oracle = load(args.oracle)
    verdict = load(args.verdict)
    actual = recompute(oracle)

    for field in (
        "h0SampleCostBytes",
        "g2SampleCostBytes",
        "savedBytes",
        "rowCount",
        "improvedRows",
        "outsideRadiusWinners",
        "overallFivePercentGate",
        "pairPassCount",
        "status",
        "nextAction",
    ):
        assert actual[field] == verdict[field], (field, actual[field], verdict[field])

    expected_pairs = {
        (p["family"], p["baseVersion"], p["targetVersion"]): p
        for p in verdict["pairs"]
    }
    for pair, h0, g2, saved, improved, passed in actual["pairs"]:
        row = expected_pairs[pair]
        assert h0 == row["h0CostBytes"]
        assert g2 == row["oracleCostBytes"]
        assert saved == row["savedBytes"]
        assert improved == row["improvedRows"]
        assert passed == row["passesThreePercentGate"]

    print(json.dumps(
        {k: v for k, v in actual.items() if k != "pairs"},
        sort_keys=True,
        separators=(",", ":"),
    ))

if __name__ == "__main__":
    main()
