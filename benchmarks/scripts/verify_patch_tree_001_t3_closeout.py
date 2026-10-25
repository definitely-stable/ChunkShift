#!/usr/bin/env python3
"""PATCH-TREE-001/T3 original admission audit from frozen research evidence.

Pure Python standard library. The B2B medians in the checked-in ledger are a
previously audited summary, NOT new source measurements. Use --b2b-zip to
independently replay its original GitHub-hosted ZIP against source locks.
C1/D0 retained TSVs are already independently audited against original ZIPs.
"""
import argparse
import csv
import json
import math
import runpy
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEDGER = ROOT / "benchmarks/experiments/patch-tree-001-t3-closeout-20261025.json"
C1 = ROOT / "docs/research/results/data/PATCH-TREE-001-T3B2C1-EVIDENCE-20261025-001/samples.tsv"
D0 = ROOT / "docs/research/results/data/PATCH-TREE-001-T3B2D0-EVIDENCE-20261025-001/samples.tsv"
B2B = ROOT / "docs/research/results/data/PATCH-TREE-001-T3B2B-EVIDENCE-20261024-001/recompute.py"

# Independent fixed expectations: values originate in previously audited
# original B2B/B1, and are NOT fitted by this decision-only closeout.
LOCKED = (
    ("10.0.10", "10.0.11", 1356632, 1356632, 548714, 86432, 295, 1114,
     308.941, 3614.386, 133.935, 1740.003, 36133305, 136,
     "24a5579e96411f64c9b790e68d4ff01cbeb42f79500094553007fd1330e5dd29"),
    ("10.0.11", "10.0.12", 1356632, 1356640, 548863, 5649, 295, 1114,
     259.708, 1088.596, 128.814, 549.607, 14522936, 57,
     "de7e1d02606c7663dac0aaaad86da4b4839552e58a6a00ee7cc99b6498c52103"),
)
ORIGINAL_PAIRS_SHA = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
B2B_ZIP_SHA = "9b0562b888952958b1e90ab1d2808f2c12e40d722628095b0127a54870375568"
C1_COLUMNS = ("pair", "lane", "round", "warmup", "cpu_ms", "wall_ms",
              "base_read_B", "base_distinct_B", "base_repeat_B",
              "base_seeks", "backward_seeks", "cache_loads", "cache_hits",
              "cache_peak_B", "window_peak_B", "alloc_B", "patch_sha256")
D0_COLUMNS = ("pair", "lane", "round", "warmup", "cpu_ms", "wall_ms",
              "plain_ticks", "read_ticks", "dict_ticks", "verify_ticks",
              "entries", "dict_reads", "dict_encodes", "verified_records",
              "patch_sha256")


def require(ok, message):
    if not ok:
        raise ValueError("PATCH-TREE-001/T3 closeout: " + message)


def approx(a, b):
    return math.isclose(float(a), float(b), rel_tol=1e-10, abs_tol=1e-9)


def read_tsv(path, columns, expected_rows):
    with path.open(newline="", encoding="utf-8") as stream:
        reader = csv.DictReader(stream, delimiter="\t")
        require(tuple(reader.fieldnames or ()) == columns, "source TSV schema")
        rows = list(reader)
    require(len(rows) == expected_rows, "source TSV row count")
    return rows


def median(rows, lane, field):
    vals = [float(s[field]) for s in rows
            if s["lane"] == lane and s["warmup"] == "0"]
    require(len(vals) == 3 and all(x >= 0 and math.isfinite(x) for x in vals),
            "measured lane coverage/counters")
    return statistics.median(vals)


def check_c1(rows, idx, item, sha):
    batch = rows[(idx - 1)*8:idx*8]
    chronology = [("H2", -1, 1), ("H3", -1, 1)]
    for rd, order in enumerate((("H2", "H3"), ("H3", "H2"), ("H2", "H3"))):
        chronology.extend((mode, rd, 0) for mode in order)
    require([(s["lane"], int(s["round"]), int(s["warmup"])) for s in batch]
            == chronology, "C1 complete alternating chronology")
    for sample in batch:
        require(int(sample["pair"]) == idx and sample["patch_sha256"] == sha,
                "C1 CSP/source identity")
        expected_reads = (item["c1H2BaseStreamReadBytes"] if
                          sample["lane"] == "H2" else item["c1H3BaseStreamReadBytes"])
        require(int(sample["base_read_B"]) == expected_reads and
                int(sample["base_distinct_B"]) == item["baseBytes"] and
                int(sample["base_repeat_B"]) ==
                expected_reads - item["baseBytes"], "C1 logical read conservation")
        require(int(sample["cache_hits"]) == 0 if sample["lane"] == "H2"
                else int(sample["cache_hits"]) > 0, "C1 cache activation")
    h2_cpu, h3_cpu = median(batch, "H2", "cpu_ms"), median(batch, "H3", "cpu_ms")
    require(h2_cpu > 0 and h3_cpu > 0, "C1 valid median denominator")
    return {"h2CpuMs": h2_cpu, "h3CpuMs": h3_cpu,
            "h3OverH2Cpu": h3_cpu / h2_cpu,
            "h3LogicalReadReduction":
                1 - item["c1H3BaseStreamReadBytes"] / item["c1H2BaseStreamReadBytes"]}


def check_d0(rows, idx, item, sha):
    batch = rows[(idx - 1)*12:idx*12]
    chronology = [("OFF_A", -1, 1), ("OFF_B", -1, 1), ("ON", -1, 1)]
    for rd, order in enumerate((("OFF_A", "ON", "OFF_B"),
                                ("ON", "OFF_B", "OFF_A"),
                                ("OFF_B", "OFF_A", "ON"))):
        chronology.extend((mode, rd, 0) for mode in order)
    require([(s["lane"], int(s["round"]), int(s["warmup"])) for s in batch]
            == chronology, "D0 complete OFF/OFF/ON chronology")
    for sample in batch:
        require(int(sample["pair"]) == idx and sample["patch_sha256"] == sha,
                "D0 CSP/source identity")
        if sample["lane"] == "ON":
            require(int(sample["dict_encodes"]) == item["d0DictionaryEncodesPerOnSample"] and
                    int(sample["dict_reads"]) >= int(sample["dict_encodes"]) and
                    int(sample["entries"]) > 0 and
                    all(int(sample[field]) >= 0 for field in
                        ("plain_ticks", "read_ticks", "dict_ticks", "verify_ticks")),
                    "D0 stage counters")
        else:
            require(all(sample[field] == "" for field in
                        ("plain_ticks", "read_ticks", "dict_ticks", "verify_ticks",
                         "entries", "dict_reads", "dict_encodes", "verified_records")),
                    "D0 null-probe controls")
    off = [float(s["cpu_ms"]) for s in batch
           if s["lane"] != "ON" and s["warmup"] == "0"]
    require(len(off) == 6, "D0 six pooled off samples")
    baseline, on = statistics.median(off), median(batch, "ON", "cpu_ms")
    require(baseline > 0, "D0 pooled CPU denominator")
    ticks = {field: statistics.median(int(s[field]) for s in batch
             if s["lane"] == "ON" and s["warmup"] == "0")
             for field in ("plain_ticks", "read_ticks", "dict_ticks", "verify_ticks")}
    subtotal = sum(ticks.values())
    require(subtotal > 0 and ticks["dict_ticks"] > ticks["plain_ticks"],
            "D0 measured dictionary stage dominance")
    return {"onOverPooledOffCpu": on / baseline,
            "dictionaryShareOfFourWorkerElapsedGroups": ticks["dict_ticks"] / subtotal,
            "dictionaryEncodesPerOnSample": item["d0DictionaryEncodesPerOnSample"]}


def evaluate(ledger, c1_rows, d0_rows):
    require(ledger.get("schema") == "chunkshift.patch-tree-t3-production-admission-closeout.v1" and
            ledger.get("experimentId") == "PATCH-TREE-001" and
            ledger.get("ownerIssue") == 184 and ledger.get("freezeIssue") == 251 and
            ledger.get("holdoutOpened") is False and
            ledger.get("productionFallbackImplemented") is False and
            ledger.get("newPersistedFormat") is False, "closeout scope and negative claims")
    require(ledger.get("status") == "T3_ORIGINAL_CREATE_BUDGET_FAIL_PRODUCTION_DEFER" and
            ledger.get("productDecision") == "DEFER", "not a production ADOPT")
    policy = ledger["admission"]
    require(policy["maxCreateCpuRatio"] == policy["maxCreateWallRatio"] == 3 and
            policy["sizeWarmStrictFraction"] == 0.9 and
            policy["requireBothPairs"] is True, "previously frozen 3x + size gates")
    evidence = ledger["evidence"]
    require(evidence["originalPairsSha256"] == ORIGINAL_PAIRS_SHA and
            evidence["b2b"]["zipSha256"] == B2B_ZIP_SHA and
            evidence["b2b"]["runId"] == 40078373627 and
            evidence["c1"]["runId"] == 40191549066 and
            evidence["d0"]["runId"] == 40194727803, "frozen original provenance")
    controls = ledger["controls"]
    require(controls["t3aExactMatchesOnOriginalFourPairs"] == 0 and
            controls["t3aExactAddedPathNetSavingsBytes"] == 0 and
            controls["t0WarmFullTreeBytes"] == 14758982 and
            controls["t1NaiveWarmFullTreeBytes"] == 83593822 and
            controls["t3b1ModelledFullTreeWarmBytes"] == 12138381 and
            controls["t3b1ModelNotActualFullTree"] is True and
            controls["t3B2c1CpuWinBothOriginalPairs"] is False and
            controls["t3b2d0MeasuredWorkerWallNotCpu"] is True, "full-tree model/inference boundary")
    pairs = ledger.get("pairs")
    require(isinstance(pairs, list) and len(pairs) == 2, "frozen two changed-renamed pairs")
    output = []
    c1_gains = []
    for idx, (item, expected) in enumerate(zip(pairs, LOCKED), 1):
        (base, target, base_length, raw, t0s, csp, mapbytes, csm,
         t0_cpu, t3_cpu, t0_wall, t3_wall, h2_reads, trials, sha) = expected
        require((item["baseVersion"], item["targetVersion"], item["baseBytes"],
                 item["rawTargetBytes"], item["t0sPhysicalBytes"], item["t3CspPhysicalBytes"],
                 item["t3DiagnosticMapBytes"], item["t3OldCsmBytes"],
                 item["c1H2BaseStreamReadBytes"], item["c1H3BaseStreamReadBytes"],
                 item["d0DictionaryEncodesPerOnSample"], item["expectedCspSha256"]) ==
                (base, target, base_length, raw, t0s, csp, mapbytes, csm,
                 h2_reads, base_length, trials, sha), "B2B fixed original bytes/identity")
        require(all(approx(item["b2bMedianCreateCpuMs"][x], v)
                    for x, v in (("t0s", t0_cpu), ("t3", t3_cpu))) and
                all(approx(item["b2bMedianCreateWallMs"][x], v)
                    for x, v in (("t0s", t0_wall), ("t3", t3_wall))),
                "B2B fixed original per-lane medians")
        warm, cold = csp + mapbytes, csp + mapbytes + csm
        best = min(raw, t0s)
        size_pass = warm < policy["sizeWarmStrictFraction"] * best and cold <= best
        cpu_ratio, wall_ratio = t3_cpu / t0_cpu, t3_wall / t0_wall
        require(size_pass and cpu_ratio > 3 and wall_ratio > 3 and
                item["admission"] == "FALLBACK_T0_OR_T0S__CREATE_BUDGET",
                "locked create budget rejection; fallback lab-only")
        c1 = check_c1(c1_rows, idx, item, sha)
        d0 = check_d0(d0_rows, idx, item, sha)
        c1_gains.append(c1["h3OverH2Cpu"] < 1)
        output.append({"transition":base+" -> "+target,
                       "t0sPhysicalBytes":t0s,"t3WarmPhysicalBytes":warm,
                       "t3ColdPhysicalBytes":cold,
                       "t3OverT0sCpu":cpu_ratio,"t3OverT0sWall":wall_ratio,
                       "h3OverH2Cpu":c1["h3OverH2Cpu"],
                       "h3LogicalReadReduction":c1["h3LogicalReadReduction"],
                       "d0DictionaryStageShareOfTimedWorkerElapsed":
                           d0["dictionaryShareOfFourWorkerElapsedGroups"],
                       "admission":item["admission"]})
    require(c1_gains == [False, True], "H3 did not win CPU on BOTH original pairs")
    require(sum(x["t0sPhysicalBytes"] for x in output) == 1097577 and
            sum(x["t3WarmPhysicalBytes"] for x in output) == 92671 and
            sum(x["t3ColdPhysicalBytes"] for x in output) == 94899,
            "fixed two-file physical conservation")
    return output


def verify_original_b2b(zip_path, closeout):
    # Original independent source/physical replay, rather than importing the producer.
    audit = runpy.run_path(str(B2B))
    independent = audit["replay"](zip_path)
    require(len(independent) == len(closeout), "B2B audited pair population")
    for original, row in zip(independent, closeout):
        require(original["transition"] == row["transition"] and
                original["t0sPhysicalBytes"] == row["t0sPhysicalBytes"] and
                original["t3WarmPhysicalBytes"] == row["t3WarmPhysicalBytes"] and
                original["t3ColdPhysicalBytes"] == row["t3ColdPhysicalBytes"] and
                approx(original["medianCpuRatio"], row["t3OverT0sCpu"]) and
                approx(original["medianWallRatio"], row["t3OverT0sWall"]) and
                original["admission"] == row["admission"],
                "independent original B2B ZIP contradicts closeout")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--b2b-original-zip", type=Path,
                        help="original GitHub Actions artifact #12553937250")
    args = parser.parse_args()
    closeout = evaluate(json.loads(LEDGER.read_text(encoding="utf-8")),
                        read_tsv(C1, C1_COLUMNS, 16),
                        read_tsv(D0, D0_COLUMNS, 24))
    if args.b2b_original_zip is not None:
        verify_original_b2b(args.b2b_original_zip, closeout)
    print("PATCH-TREE-001/T3 frozen original cost-gate replay PASS")
    print(json.dumps(closeout, indent=2))
    print("DEFER_T3_FIRST_RELEASE / NO_PRODUCTION_ADOPT / #184 #251 OPEN")


if __name__ == "__main__":
    main()
