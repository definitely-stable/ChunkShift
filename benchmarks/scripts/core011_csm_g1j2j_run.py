#!/usr/bin/env python3
"""G1-J2J: exact wire-byte attribution of the 8 frozen G1-J2I real CSPs.

The existing J2I runner owns original NuGet/CSM/CSP files only until apply/
verify passes; our diagnostic callback inspects real patch bytes in that
temporary lifetime. The wrapper then adds only source-free reconciled ledgers.
"""
import argparse
import json
from pathlib import Path

import core011_csm_g1j2i_physical_ab as j2i
import core011_csm_g1j2j_wire_attribution as wire

SCHEMA = "chunkshift.core011.g1j2j-wire-attribution.v1"
PREFIX = "CORE011-CSM-001/G1J2J-"
PIN = {
    "roslyn": {
        "stable64k": {
            "based": ("901e044c6e76fd25cb303c4b59a042c5b23c534a1d71503ffe22dd25ece0dfd3",1260098),
            "standalone": ("00f089ccee90e59dc76f7fce6aacba2e15f06bdc265f064b28688ff2c5fc3449",2100946)},
        "lab16k": {
            "based": ("0b7e1cd6ff90650e752fec8d12a4ef629df6d1a51f6a1bc435c2cd889be3d138",1463794),
            "standalone": ("bd67f2915f90d7ef347b736bb927f794412a7c0e3bd81272fdcadf48730a0026",2251601)},
    },
    "newtonsoft": {
        "stable64k": {
            "based": ("25176257a0bab4855b66f9adb9f98428a597a1ba8c31f3ddbc64e189ee92106c",78435),
            "standalone": ("bc5698bdef5bbc97b39d7e80e6e5cd2b3a8a2e8c4ee07f5427824bcef14cc060",247508)},
        "lab16k": {
            "based": ("3731fd81e893f67053a535cafe26bc0024a519d9274ded9579ce602dfc89e6a0",82454),
            "standalone": ("9d197bda7fb7555d10e947be24d2b3e40a6be888cf17f364067eb9b6fdd0d9ed",258303)},
    },
}


def evaluate(summary):
    j2i.require(summary.get("schema") == SCHEMA and
                summary.get("decisionRunsAllowed") is False and
                summary.get("budgetsLocked") is False and
                summary.get("verdict") == "NO_VERDICT_EXPLORATORY" and
                set(summary.get("families", {})) == set(PIN),
                "J2J source/result verdict or family drift")
    differences = {}
    for family, profiles in PIN.items():
        actual = summary["families"][family]["variants"]
        j2i.require(set(actual) == set(profiles),
                    "missing profile in full physical wire attribution")
        differences[family] = {}
        for profile, modes in profiles.items():
            actual_modes = actual[profile]["physicalCsp"]
            j2i.require(set(actual_modes) == set(modes),
                        "missing CSP mode for physical wire attribution")
            for mode, (expected_sha, expected_size) in modes.items():
                patch = actual_modes[mode]
                ledger = patch["wire"]
                j2i.require(patch["physicalPatchSha256"] == expected_sha and
                            patch["physicalPatchBytes"] == expected_size and
                            patch["exactOriginalTargetReconstructed"] is True,
                            "J2J original J2I measured physical CSP SHA/length/target drift")
                j2i.require(ledger["physicalBytes"] == expected_size and
                            sum(ledger["ledger"].values()) == expected_size and
                            ledger["ledger"]["storedEncodedPayload"] == patch["payloadBytes"] and
                            ledger["entryCounts"]["payloadEntries"] == patch["payloadEntries"] and
                            ledger["crc32cVerified"] is True and
                            ledger["pidxOffsetsVerified"] is True,
                            "J2J independent byte conservation or PAYL/PIDX oracle failed")
        for mode in j2i.MODES:
            left = actual["stable64k"]["physicalCsp"][mode]["wire"]
            right = actual["lab16k"]["physicalCsp"][mode]["wire"]
            difference = wire.component_delta(left, right)
            total = sum(difference.values())
            known = (profiles["lab16k"][mode][1] -
                     profiles["stable64k"][mode][1])
            j2i.require(total == known,
                        "J2J physical A/B delta does not conserve the frozen bytes")
            differences[family][mode] = {
                "labMinusStablePhysicalBytes": known,
                "wireComponentDeltaBytes": difference,
                "storedPayloadDeltaBytes": difference["storedEncodedPayload"],
                "allOtherPhysicalDeltaBytes": total - difference["storedEncodedPayload"],
                "independentWireDecompositionPass": True,
            }
    summary["wireComparison"] = differences
    summary["analysisScope"] = (
        "Exactly frozen original J2I files. All source CSP/CSM bytes deleted before "
        "artifact. Wire accounting, not causal dictionary or CPU attribution. "
        "No production/default/API changes.")
    summary["semanticDecision"] = "NO_GAIN_ON_FROZEN_PAIRS"
    return summary


def run(output, worktree, run_id):
    j2i.run(Path.cwd(), output, worktree, run_id,
            wire_analyzer=wire.counted_bytes,
            schema=SCHEMA,
            run_prefix=PREFIX)
    path = output / "summary.json"
    doc = evaluate(json.loads(path.read_text()))
    path.write_text(json.dumps(doc, indent=2) + "\n")
    print("J2J 8/8 original SHA-pinned CSP files: CRC/PAYL/PIDX wire ledgers "
          "byte-conserved, delta components reconciled; NO_VERDICT")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output-dir", type=Path, required=True)
    p.add_argument("--worktree-root", type=Path, required=True)
    p.add_argument("--run-id", required=True)
    args = p.parse_args()
    j2i.require(args.run_id.startswith(PREFIX) and
                len(args.run_id) > len(PREFIX), "wrong J2J run ID")
    run(args.output_dir, args.worktree_root, args.run_id)


if __name__ == "__main__":
    main()
