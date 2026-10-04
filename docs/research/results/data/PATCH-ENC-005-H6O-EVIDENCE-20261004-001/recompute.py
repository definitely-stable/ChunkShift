#!/usr/bin/env python3
import argparse
import json
import statistics
from pathlib import Path

def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))

def recompute(compact):
    timing = compact["timing"]
    memory = compact["memory"]
    correctness = compact["correctness"]
    determinism = compact["determinism"]

    h0 = int(timing["h0PatchBytes"])
    h6 = int(timing["h6PatchBytes"])
    wall = [float(x) for x in timing["wallRatios"]]
    byte_ratio = h6 / h0
    wall_median = statistics.median(wall)
    wall_pass_count = sum(x <= 1.50 for x in wall)

    h0_over_idle = int(memory["h0MaxCreatePeakBytes"]) - int(memory["h0IdleBaselineBytes"])
    h6_over_idle = int(memory["h6MaxCreatePeakBytes"]) - int(memory["h6IdleBaselineBytes"])
    memory_limit = int(memory["limitOverIdleBytes"])

    correctness_ok = (
        int(correctness["filesCheckedPerLane"]) == 844
        and int(correctness["invalidDecoderVerdicts"]) == 0
        and int(correctness["decoderTargetDigestMismatches"]) == 0
    )
    deterministic_ok = (
        int(determinism["filesCheckedPerLane"]) == 844
        and int(determinism["acceptedVsCorrectnessPatchShaMismatches"]) == 0
    )

    bytes_ok = h6 * 100 <= h0 * 97
    wall_ok = wall_median <= 1.50 and wall_pass_count >= 4
    memory_ok = h6_over_idle <= memory_limit
    passed = bytes_ok and wall_ok and memory_ok and correctness_ok and deterministic_ok

    return {
        "h0PatchBytes": h0,
        "h6PatchBytes": h6,
        "byteRatio": byte_ratio,
        "bytesOk": bytes_ok,
        "wallRatioMedian": wall_median,
        "wallPassCount": wall_pass_count,
        "wallOk": wall_ok,
        "h0CreatePeakOverIdleBytes": h0_over_idle,
        "h6CreatePeakOverIdleBytes": h6_over_idle,
        "createMemoryLimitBytes": memory_limit,
        "memoryOk": memory_ok,
        "correctnessOk": correctness_ok,
        "deterministicBytesOk": deterministic_ok,
        "status": "PASS" if passed else "MISS",
        "nextAction": "FULL_PHASE_B" if passed else "STOP_RESEMBLANCE",
    }

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--compact", required=True)
    parser.add_argument("--verdict", required=True)
    args = parser.parse_args()
    actual = recompute(load(args.compact))
    expected = load(args.verdict)

    fields = (
        "h0PatchBytes", "h6PatchBytes", "byteRatio", "bytesOk",
        "wallRatioMedian", "wallPassCount", "wallOk",
        "h0CreatePeakOverIdleBytes", "h6CreatePeakOverIdleBytes",
        "createMemoryLimitBytes", "memoryOk", "correctnessOk",
        "deterministicBytesOk", "status", "nextAction"
    )
    for field in fields:
        assert actual[field] == expected[field], (field, actual[field], expected[field])

    print(json.dumps(actual, sort_keys=True, separators=(",", ":")))

if __name__ == "__main__":
    main()
