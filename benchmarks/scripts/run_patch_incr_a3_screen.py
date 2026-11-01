#!/usr/bin/env python3
"""PATCH-INCR-001 A3 screen. Isolated-process paired commands; never promotes code."""
import argparse
import json
import os
import statistics
import subprocess
import sys
from pathlib import Path

def run(args, **kw):
    subprocess.run(args, check=True, **kw)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", type=Path, required=True)
    ap.add_argument("--evidence", type=Path, required=True)
    ap.add_argument("--run-id", required=True)
    ap.add_argument("--size-mib", type=int, default=96)
    ap.add_argument("--binary", type=Path, required=True)
    args = ap.parse_args()

    if args.size_mib != 96:
        raise ValueError("A3-S0 protocol freezes 96 MiB; other sizes need another protocol.")
    root = args.root.resolve()
    evidence = args.evidence.resolve()
    if not root.is_dir() or any(root.iterdir()):
        raise ValueError("The A3 mounted Btrfs fixture root must already exist and be empty.")
    evidence.mkdir(parents=True, exist_ok=True)
    binary = args.binary.resolve()
    base = ["dotnet", str(binary), "patch-incr-a3"]
    run(base + ["prepare", "--dir", str(root), "--size-mib", str(args.size_mib)])

    rows = []
    # Counterbalance first lane and alternate bidx ordering. Exact 3
    # independent processes per lane/bidx, no JIT warmup in same process.
    for bidx in ("false", "true"):
        for round_number in range(3):
            order = ("full", "s2b") if round_number != 1 else ("s2b", "full")
            for lane in order:
                sample_id = f"{args.run_id}-{bidx}-{round_number}-{lane}"
                output = evidence / (sample_id + ".json")
                run(base + [
                    "measure", "--dir", str(root), "--lane", lane, "--bidx", bidx,
                    "--run-id", sample_id, "--output", str(output)
                ], stdout=subprocess.DEVNULL)
                item = json.loads(output.read_text())
                if (item["schema"] != "chunkshift.patch-incr-a3-screen.v1"
                        or item["runId"] != sample_id or item["lane"] != lane
                        or item["includeBlockIndex"] != (bidx == "true")
                        or not item["oracleEqual"]
                        or item["newLength"] != args.size_mib * 1024 * 1024):
                    raise RuntimeError("Invalid/missing isolated oracle evidence: " + sample_id)
                rows.append(item)
                print(f"SAMPLE {sample_id}: wall={item['wallMilliseconds']:.1f} ms "
                      f"CPU={item['cpuMilliseconds']:.1f} ms "
                      f"old-verify={item['verifiedOldSourceBytes']} "
                      f"new-scan={item['virtualTargetBytes']}", flush=True)

    by_variant = {}
    fatal = False
    for bidx in (False, True):
        key = "bidx" if bidx else "no-bidx"
        full = [v for v in rows if v["lane"] == "full" and v["includeBlockIndex"] == bidx]
        incr = [v for v in rows if v["lane"] == "s2b" and v["includeBlockIndex"] == bidx]
        assert len(full) == len(incr) == 3
        n = incr[0]["newLength"]
        read_ratio = min(
            (v["verifiedOldSourceBytes"] + v["virtualTargetBytes"]) / n
            for v in incr
        )
        full_wall = statistics.median(v["wallMilliseconds"] for v in full)
        incr_wall = statistics.median(v["wallMilliseconds"] for v in incr)
        full_cpu = statistics.median(v["cpuMilliseconds"] for v in full)
        incr_cpu = statistics.median(v["cpuMilliseconds"] for v in incr)
        by_variant[key] = {
            "fullWallMedianMs": full_wall,
            "incrementalWallMedianMs": incr_wall,
            "incrementalToFullWallRatio": incr_wall / full_wall,
            "fullCpuMedianMs": full_cpu,
            "incrementalCpuMedianMs": incr_cpu,
            "incrementalToFullCpuRatio": incr_cpu / full_cpu if full_cpu else None,
            "minimumIncrementalSourceReadRatio": read_ratio,
            "incrementalOldSourceVerificationBytesPerRun":
                [v["verifiedOldSourceBytes"] for v in incr],
            "incrementalVirtualTargetBytesPerRun":
                [v["virtualTargetBytes"] for v in incr],
            "physicalIoCaveat": "Linux read_bytes may be zero on warm/page-cached reads"
        }
        # Frozen necessary gate for LOCALIZED-CHANGE savings, evaluated before
        # speed: <=0.50 read ratio AND <=0.80 end-to-end wall ratio in both
        # BIDX conditions. An early fail stops further A3 originals/holdout.
        if read_ratio > 0.50 or incr_wall / full_wall > 0.80:
            fatal = True

    decision = "DEFER_CURRENT_IMPLEMENTATION" if fatal else "CONTINUE_TO_A3_HOLDOUT"
    summary = {
        "schema": "chunkshift.patch-incr-a3-screen-summary.v1",
        "runId": args.run_id,
        "fixture": "96MiB deterministic high-entropy overwrite, one 4-byte old-coordinate edit",
        "host": os.uname().sysname + " " + os.uname().release,
        "variantMetrics": by_variant,
        "thresholds": {
            "maxIncrementalSourceReadToNewLengthRatio": 0.50,
            "maxIncrementalToFullWallMedianRatio": 0.80,
            "bothBidxSettingsRequired": True
        },
        "decision": decision,
        "samples": 12,
        "caveats": [
            "96MiB is an EARLY SCREEN, not the required large original/holdout",
            "Btrfs/page cache may affect physical storage read_bytes; no cold-cache claim",
            "Host old CSM ManifestId identity loaded before measurement to favor incremental",
            "Baseline target content already materialized before the measured interval",
            "Both lanes stage/flush and independently verify physical CSM output",
            "Each lane executed in a fresh process; process RSS includes startup",
            "This verdict only concerns current full-old-CDC-verifying S2b, not future CAS-authoritative designs"
        ]
    }
    target = evidence / "A3-S0-summary.json"
    target.write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n")
    print("A3_SCREEN_DECISION:", decision)
    print("A3_SCREEN_SUMMARY:", target)
    if not fatal:
        print("A3 holdout is not automatically authorized; review physical I/O, RSS and trust.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
