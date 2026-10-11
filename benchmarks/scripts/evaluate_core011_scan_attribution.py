#!/usr/bin/env python3
"""Fail-closed CORE011-SCAN-001 attribution matrix checker; not an A/B verdict."""

import argparse
import json
import math
import statistics
from pathlib import Path

MODES = ("memory", "fragmented-memory", "file-sequential")
SUITES = ("chunkshift.blake3-256.v1", "chunkshift.sha256.v1")
CALLBACKS = ("completed", "yield-once")


def evaluate(data):
    if data.get("schema") != "chunkshift.core011.scan-attribution.v1":
        raise ValueError("unsupported attribution schema")
    if data.get("experimentId") != "CORE011-SCAN-001":
        raise ValueError("unexpected ExperimentId")
    if not data.get("runId") or not data.get("checkoutSha"):
        raise ValueError("missing run/check-out provenance")

    conditions = data.get("conditions", {})
    rounds = conditions.get("rounds")
    if type(rounds) is not int or not 1 <= rounds <= 12:
        raise ValueError("invalid round count")
    total_bytes = data.get("corpus", {}).get("contentBytes")
    if type(total_bytes) is not int or total_bytes <= 0:
        raise ValueError("invalid input byte count")
    source_hash = data.get("corpus", {}).get("sha256", "")
    if len(source_hash) != 64 or any(c not in "0123456789abcdef" for c in source_hash):
        raise ValueError("missing deterministic source hash")

    samples = data.get("samples")
    if not isinstance(samples, list) or len(samples) != 12 * rounds:
        raise ValueError("incomplete factorial sample matrix")

    seen = {}
    for sample in samples:
        mode = sample.get("mode")
        suite = sample.get("hashSuite")
        callback = sample.get("callback")
        rnd = sample.get("round")
        if (mode not in MODES or suite not in SUITES or callback not in CALLBACKS
                or type(rnd) is not int or not 1 <= rnd <= rounds):
            raise ValueError("unexpected lane or round")
        key = (mode, suite, callback, rnd)
        if key in seen:
            raise ValueError("duplicate lane/round")
        seen[key] = sample

        for field in ("readCalls", "readBytes", "eofReads", "bytesCopied",
                      "asyncCompletions", "chunkCount", "contentBytes",
                      "processAllocatedBytes", "workingSetBytes"):
            value = sample.get(field)
            if type(value) is not int or value < 0:
                raise ValueError(f"invalid {field} in {key}")
        for field in ("wallMilliseconds", "processCpuMilliseconds"):
            value = sample.get(field)
            if type(value) not in (float, int) or not math.isfinite(value) or value < 0:
                raise ValueError(f"invalid {field} in {key}")

        if (sample["readBytes"] != total_bytes or
                sample["contentBytes"] != total_bytes or
                sample["eofReads"] != 1 or
                sample["readCalls"] < 2 or sample["chunkCount"] < 1):
            raise ValueError("incomplete scan for " + repr(key))
        expected_awaits = sample["chunkCount"] if callback == "yield-once" else 0
        if sample["asyncCompletions"] != expected_awaits:
            raise ValueError("async handler was not awaited for " + repr(key))

    if len({sample["chunkCount"] for sample in samples}) != 1:
        raise ValueError("different chunk totals across identical corpus/profile")

    # Synthetic memory source read segmentation is deterministic. FileStream may
    # legally return variable short reads; keep its raw counters rather than
    # treating input scheduling variance as a scanner correctness failure.
    for mode in MODES[:2]:
        for rnd in range(1, rounds + 1):
            group = [seen[mode, suite, cb, rnd] for suite in SUITES for cb in CALLBACKS]
            for field in ("bytesCopied", "readCalls", "readBytes"):
                if len({sample[field] for sample in group}) != 1:
                    raise ValueError(f"inconsistent {field} for {mode} round {rnd}")

    summary = []
    for mode in MODES:
        for suite in SUITES:
            for callback in CALLBACKS:
                lane = [seen[mode, suite, callback, rnd] for rnd in range(1, rounds + 1)]
                wall = [s["wallMilliseconds"] for s in lane]
                center = statistics.median(wall)
                summary.append({
                    "mode": mode,
                    "hashSuite": suite,
                    "callback": callback,
                    "sampleCount": rounds,
                    "wallMedianMs": center,
                    "wallMadMs": statistics.median([abs(value - center) for value in wall]),
                    "wallMinMs": min(wall),
                    "wallMaxMs": max(wall),
                    "cpuMedianMs": statistics.median(
                        s["processCpuMilliseconds"] for s in lane),
                    "readCallsMedian": statistics.median(s["readCalls"] for s in lane),
                    "bytesCopiedMedian": statistics.median(s["bytesCopied"] for s in lane),
                    "copyBytesPerSourceByte": (statistics.median(
                        s["bytesCopied"] for s in lane) / total_bytes),
                    "sourceGiBPerSecondAtMedian": (
                        total_bytes / 2**30 / (center / 1000) if center > 0 else None),
                })

    return {
        "schema": "chunkshift.core011.scan-attribution-summary.v1",
        "experimentId": data["experimentId"],
        "runId": data["runId"],
        "checkoutSha": data["checkoutSha"],
        "pullRequestHeadSha": data.get("pullRequestHeadSha"),
        "corpusSha256": source_hash,
        "sampleCount": len(samples),
        "decision": "NO_VERDICT_EXPLORATORY",
        "limitations": "warm FileStream cache, process-wide allocation/CPU, forced Task.Yield, hosted runner noise",
        "lanes": summary,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    data = json.loads(args.input.read_text(encoding="utf-8"))
    report = evaluate(data)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"validated {report['sampleCount']} attribution samples; "
          f"decision={report['decision']}; written {args.output}")


if __name__ == "__main__":
    main()
