#!/usr/bin/env python3
"""Strict source-free G2-C4 EventPipe/JIT overlap evidence guard.

A JIT event within a call is temporal association, NOT causal proof.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

MODES = ("tiered-pgo-on", "tiered-pgo-off", "tiered-off")
WARMS = (0, 64)
CSM = "342ff5e8175304be3f27873fdfeee051deaa0d3697d60e1dcdc3799e674eac2a"


def load(p):
    return json.loads(p.read_text(encoding="utf-8"))


def validate(root: Path, lane: int) -> dict:
    if lane not in (0, 1):
        raise ValueError("Invalid lane")
    if (root / "source.bin").exists() or (root / "original.csm").exists():
        raise ValueError("Original content must not be published")
    f = load(root / "fixture.json")
    if (f.get("manifestSha256") != CSM or f.get("chunkCount") != 5052
            or f.get("cblkCount") != 2 or f.get("hasBlockIndex") is not True):
        raise ValueError("Wrong pinned CSM")
    results = []
    for mode in MODES:
        for warm in WARMS:
            key = f"{mode}-{warm}"
            target_path = root / "samples" / (key + ".json")
            trace_path = root / "traces" / (key + ".nettrace")
            parsed_path = root / "parsed" / (key + ".json")
            for p in (target_path, trace_path, parsed_path):
                if not p.is_file() or p.stat().st_size <= 0:
                    raise ValueError(f"Missing actual EventPipe evidence: {p}")
            target, parsed = load(target_path), load(parsed_path)
            digest = hashlib.sha256(trace_path.read_bytes()).hexdigest()
            if (target.get("schema") != "chunkshift.core011-csm-g2c4-target.v1"
                    or target.get("stage") != "G2-C4-NONDECISION"
                    or target.get("mode") != mode
                    or target.get("warmups") != warm
                    or target.get("observations") != 32
                    or target.get("physicalCsmSha256") != CSM
                    or target.get("manifestId") != f["manifestId"]
                    or target.get("fileDigest") != f["fileDigest"]
                    or target.get("checkoutSha") != f["builderCheckoutSha"]
                    or target.get("verdict") != "NO_PRODUCT_VERDICT"
                    or parsed.get("schema") != "chunkshift.core011-csm-g2c4-trace-reader.v1"
                    or parsed.get("sourceTraceSha256") != digest
                    or parsed.get("causalityEstablished") is not False
                    or parsed.get("verdict") != "NO_CAUSAL_JIT_PROOF"):
                raise ValueError(f"Source identity or checksum mismatch: {key}")
            calls = target.get("samples", [])
            if len(calls) != 32 + warm:
                raise ValueError(f"Missing per-call target data: {key}")
            if [x["callIndex"] for x in calls if x["phase"] == 1] != list(range(1, 33)):
                raise ValueError(f"Missing ordered timing data: {key}")
            if len([x for x in calls if x["phase"] == 0]) != warm:
                raise ValueError(f"Missing exact warmup calls: {key}")
            for sample in calls:
                for name in ("wallMilliseconds", "cpuMilliseconds", "gcPauseMilliseconds"):
                    number = sample[name]
                    if not isinstance(number, (float, int)) or not math.isfinite(number) or number < 0:
                        raise ValueError(f"Invalid timing: {key}")
            markers = parsed.get("markers", [])
            starts = [x for x in markers if x.get("id") == 1]
            stops = [x for x in markers if x.get("id") == 2]
            if len(starts) != warm + 32 or len(stops) != warm + 32:
                raise ValueError(f"Trace missing G2-C4 boundary markers: {key} {len(starts)}/{len(stops)}")
            def call_index(marker):
                payload = {k.lower(): v for k, v in marker.get("payload", {}).items()}
                return int(payload.get("call", payload.get("callindex", -99999)))
            start_by_id = {call_index(m): m["tms"] for m in starts}
            stop_by_id = {call_index(m): m["tms"] for m in stops}
            expected_ids = {s["callIndex"] for s in calls}
            if (len(start_by_id) != len(starts) or len(stop_by_id) != len(stops)
                    or set(start_by_id) != expected_ids or set(stop_by_id) != expected_ids):
                raise ValueError(f"Marker IDs not exactly matched to target calls: {key}")
            for index in expected_ids:
                if (not 0 <= start_by_id[index] < stop_by_id[index]
                        or not math.isfinite(start_by_id[index])
                        or not math.isfinite(stop_by_id[index])):
                    raise ValueError(f"Invalid EventPipe marker chronology: {key}/{index}")

            methods = parsed.get("methodEvents", [])
            if parsed.get("jittingStartedCount", 0) + parsed.get("methodLoadVerboseCount", 0) == 0:
                raise ValueError(f"Missing actual CLR JIT evidence: {key}")
            focus = [e for e in methods if any(q in (
                    e.get("namespace", "") + "." + e.get("name", "")).lower()
                    for q in ("manifestid", "csmreader", "hashsuite", "blake", "chunkmanifest"))]
            times = [e["tms"] for e in methods if e.get("event") == "MethodLoadVerbose"]
            in_observed = sum(any(start_by_id[i] <= t <= stop_by_id[i] for i in range(1, 33)) for t in times)
            results.append({
                "mode": mode, "warmups": warm,
                "observedCalls": 32, "warmupCalls": warm,
                "sourceTraceSha256": digest,
                "jittingStartedCount": parsed["jittingStartedCount"],
                "methodLoadVerboseCount": parsed["methodLoadVerboseCount"],
                "methodLoadsWithinTimedCalls": in_observed,
                "focusedMethodEvents": focus[:200],
                "medianEarly8WallMs": sorted(x["wallMilliseconds"] for x in calls if 1 <= x["callIndex"] <= 8)[3],
                "medianLate16WallMs": sorted(x["wallMilliseconds"] for x in calls if 17 <= x["callIndex"] <= 32)[7],
            })

    return {
        "schema": "chunkshift.core011-csm-g2c4-guard.v1",
        "lane": lane,
        "fixtureSha256": CSM,
        "originalCsmArchived": False,
        "registeredTraceCount": len(results),
        "timedCalls": 32 * len(results),
        "modeResults": results,
        "causalJitProof": False,
        "adoptPermitted": False,
        "verdict": "NO_CAUSAL_JIT_PROOF",
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", type=Path, required=True)
    p.add_argument("--lane", type=int, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    doc = validate(args.root, args.lane)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(doc, indent=2, sort_keys=True) + "\n")
    print(f"G2-C4 lane {args.lane}: {doc['registeredTraceCount']} traces, {doc['timedCalls']} calls; {doc['verdict']}")


if __name__ == "__main__":
    main()
