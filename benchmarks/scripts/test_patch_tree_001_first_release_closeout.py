"""Fail-closed replay of the retained first-release PATCH-TREE-001 decision inputs.

Not an independent replay of expired GitHub raw ZIPs or a new security proof.
"""
import csv
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DECISION = "docs/research/results/PATCH-TREE-001-FIRST-RELEASE-CLOSEOUT-20261101-001.md"


class FirstReleaseTreeCloseoutTests(unittest.TestCase):
    def test_original_t2_paired_create_gate_still_fails(self):
        path = ROOT / "docs/research/results/data/PATCH-TREE-001-T2A3A-EVIDENCE-20261027-001/census.tsv"
        with path.open(encoding="utf-8", newline="") as stream:
            rows = list(csv.DictReader(stream, delimiter="\t"))
        self.assertEqual(4, len(rows))
        self.assertEqual(14758982, sum(int(r["t0_warm_b"]) for r in rows))
        self.assertEqual(10828279, sum(int(r["t2_warm_b"]) for r in rows))
        self.assertEqual(2, sum(r["gate"] == "FAIL_A3A_CREATE_GUARD" for r in rows))
        self.assertEqual(2, sum(r["gate"] == "PASS_A3A_CREATE_GUARD" for r in rows))
        for row in rows:
            self.assertLessEqual(float(row["cpu_ratio"]), 3.0)
            self.assertLessEqual(float(row["wall_ratio"]), 3.0)
            over = float(row["t2_wall_ms"]) > 60000
            self.assertEqual(over, row["gate"] == "FAIL_A3A_CREATE_GUARD")

    def test_original_t3_create_gate_still_fails(self):
        ledger = json.loads((ROOT / "benchmarks/experiments/patch-tree-001-t3-closeout-20261025.json").read_text(encoding="utf-8"))
        self.assertEqual("PATCH-TREE-001", ledger["experimentId"])
        self.assertEqual("DEFER", ledger["productDecision"])
        self.assertFalse(ledger["holdoutOpened"])
        self.assertFalse(ledger["productionFallbackImplemented"])
        self.assertFalse(ledger["newPersistedFormat"])
        pairs = ledger["pairs"]
        self.assertEqual(2, len(pairs))
        self.assertEqual(1097577, sum(p["t0sPhysicalBytes"] for p in pairs))
        self.assertEqual(92671, sum(p["t3CspPhysicalBytes"] + p["t3DiagnosticMapBytes"] for p in pairs))
        for p in pairs:
            self.assertGreater(p["b2bMedianCreateCpuMs"]["t3"] / p["b2bMedianCreateCpuMs"]["t0s"], 3)
            self.assertGreater(p["b2bMedianCreateWallMs"]["t3"] / p["b2bMedianCreateWallMs"]["t0s"], 3)
            self.assertEqual("FALLBACK_T0_OR_T0S__CREATE_BUDGET", p["admission"])

    def test_decision_and_both_registries_agree(self):
        document = (ROOT / DECISION).read_text(encoding="utf-8")
        self.assertIn("DEFER tree/update-set representations", document)
        self.assertIn("No new RunId", document)
        self.assertIn("RFC-0006", document)
        self.assertIn("#251 remains OPEN", document)
        self.assertIn("not** an atomic tree update", document)
        machine = json.loads((ROOT / "benchmarks/experiments/research-registry.v1.json").read_text(encoding="utf-8"))
        row = next(e for e in machine["experiments"] if e["id"] == "PATCH-TREE-001")
        self.assertEqual("DEFER", row["status"])
        self.assertEqual(DECISION, row["evidence"])
        self.assertEqual("PATCH-TREE-001/EVIDENCE-20261101-001", row["firstReleaseDecisionId"])
        human = (ROOT / "docs/research/EXPERIMENT-INDEX.md").read_text(encoding="utf-8")
        record = next(line for line in human.splitlines() if line.startswith("| PATCH-TREE-001 |"))
        self.assertTrue(record.split(" | ")[4].startswith("DEFER"))
        self.assertIn("PATCH-TREE-001-FIRST-RELEASE-CLOSEOUT-20261101-001.md", record)


if __name__ == "__main__":
    unittest.main()
