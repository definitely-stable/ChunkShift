#!/usr/bin/env python3
"""G2-C3 raw-sample and binary-identity guard, never a CPU acceptance test."""
import argparse
import hashlib
import json
import math
from pathlib import Path

MODES = {
    "tiered-pgo-on": ("1", "1"),
    "tiered-pgo-off": ("1", "0"),
    "tiered-off": ("0", "0"),
}
OPS = ("manifest-verify", "enumerate")
WARMUPS = (0, 64)
PHYSICAL_SHA = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def median(values):
    s = sorted(values)
    n = len(s)
    return (s[(n - 1) // 2] + s[n // 2]) / 2


def validate(root: Path, lane: int) -> dict:
    if lane not in (0, 1):
        raise ValueError("Only two registered runner lanes")
    fixture = load(root / "fixture.json")
    if (fixture.get("schema") != "chunkshift.core011.g1j-fixture.v1"
            or fixture.get("manifestSha256") != PHYSICAL_SHA
            or fixture.get("chunkCount") != 5052
            or fixture.get("cblkCount") != 2
            or fixture.get("hashSuite") != "blake3"
            or fixture.get("hasBlockIndex") is not True
            or fixture.get("contentBytes") != 384 * 1024 * 1024
            or fixture.get("consumerMayGenerateContent") is not False):
        raise ValueError("G2-C3 fixture differs from frozen G2-C1 original")
    if (root / "original.csm").exists() or (root / "source.bin").exists():
        raise ValueError("Original content must not be uploaded")

    manifest_id = fixture["manifestId"]
    file_digest = fixture["fileDigest"]
    checkout = fixture["builderCheckoutSha"]
    exe_sha = (root / "binary.sha256").read_text().strip()
    if len(exe_sha) != 64 or not all(c in "0123456789abcdef" for c in exe_sha):
        raise ValueError("Missing compiled Core binary SHA")
    outputs = sorted((root / "samples").glob("*.json"))
    if len(outputs) != 12:
        raise ValueError(f"Expected 12 independently executed processes, got {len(outputs)}")
    seen = set()
    summaries = []
    for path in outputs:
        obj = load(path)
        mode = obj.get("mode")
        operation = obj.get("operation")
        warmups = obj.get("warmups")
        key = (mode, operation, warmups)
        if (mode not in MODES or operation not in OPS or warmups not in WARMUPS
                or key in seen or path.stem != f"{mode}-{operation}-{warmups}"):
            raise ValueError(f"Unexpected or duplicate process case: {path}")
        seen.add(key)
        if (obj.get("schema") != "chunkshift.core011-csm-g2c3-runtime.v1"
                or obj.get("experimentId") != "CORE011-CSM-002"
                or obj.get("stage") != "G2-C3-NONDECISION"
                or obj.get("checkoutSha") != checkout
                or obj.get("sourceCsmSha256") != PHYSICAL_SHA
                or obj.get("manifestId") != manifest_id
                or obj.get("fileDigest") != file_digest
                or obj.get("tieredCompilation") != MODES[mode][0]
                or obj.get("tieredPGO") != MODES[mode][1]
                or obj.get("quickJit") != "1"
                or obj.get("quickJitForLoops") != "0"
                or obj.get("readyToRun") != "1"
                or obj.get("observations") != 32
                or len(obj.get("samples", [])) != 32
                or obj.get("verdict") != "NO_VERDICT_EXPLORATORY"
                or obj.get("builderPid", 0) < 1
                or obj.get("processId", 0) == obj.get("builderPid")):
            raise ValueError(f"Failed frozen source/runtime/correctness oracle {path}")
        if [s["call"] for s in obj["samples"]] != list(range(1, 33)):
            raise ValueError(f"Missing or out-of-order raw call {path}")
        for obs in obj["samples"]:
            if (obs["allocatedBytes"] < 0 or obs["workingSetBytes"] <= 0
                    or obs["gen0"] < 0 or obs["gen1"] < 0 or obs["gen2"] < 0):
                raise ValueError("Invalid allocation/GC data")
            for field in ("wallMilliseconds", "processCpuMilliseconds", "gcPauseMilliseconds"):
                value = obs[field]
                if not isinstance(value, (float, int)) or not math.isfinite(value) or value < 0:
                    raise ValueError(f"Non-finite sample: {field}")
            if obs["wallMilliseconds"] == 0:
                raise ValueError("Zero measured full CSM call")
        samples = obj["samples"]
        summaries.append({
            "mode": mode, "operation": operation, "warmups": warmups,
            "first8MedianWallMs": median([s["wallMilliseconds"] for s in samples[:8]]),
            "last16MedianWallMs": median([s["wallMilliseconds"] for s in samples[-16:]]),
            "first8MedianCpuMs": median([s["processCpuMilliseconds"] for s in samples[:8]]),
            "last16MedianCpuMs": median([s["processCpuMilliseconds"] for s in samples[-16:]]),
            "totalThrottleEvents": sum(s["cgroupThrottledCountDelta"] or 0 for s in samples),
            "totalThrottleUsec": sum(s["cgroupThrottledUsecDelta"] or 0 for s in samples),
        })
    expected = {(mode, op, warm) for mode in MODES for op in OPS for warm in WARMUPS}
    if seen != expected:
        raise ValueError(f"Missing registered runs: {sorted(expected - seen)}")
    return {
        "schema": "chunkshift.core011-csm-g2c3-guard.v1",
        "stage": "G2-C3-NONDECISION",
        "runnerLane": lane,
        "checkoutSha": checkout,
        "physicalCsmSha256": PHYSICAL_SHA,
        "compiledCoreSha256": exe_sha,
        "independentProcesses": len(outputs),
        "rawCallCount": sum(len(load(p)["samples"]) for p in outputs),
        "comparisons": summaries,
        "adoptPermitted": False,
        "causalityEstablished": False,
        "verdict": "NO_VERDICT_EXPLORATORY",
        "limitations": "The runtime environment variables are controlled but actual tier transitions need EventPipe/JIT trace confirmation. Medians are descriptive; no product speedup inference.",
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True, type=Path)
    p.add_argument("--lane", required=True, type=int)
    p.add_argument("--output", required=True, type=Path)
    opts = p.parse_args()
    data = validate(opts.root, opts.lane)
    opts.output.parent.mkdir(parents=True, exist_ok=True)
    opts.output.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n")
    print(f"G2-C3 lane {opts.lane}: {data['independentProcesses']} processes, {data['rawCallCount']} raw calls, NONDECISION")


if __name__ == "__main__":
    main()
