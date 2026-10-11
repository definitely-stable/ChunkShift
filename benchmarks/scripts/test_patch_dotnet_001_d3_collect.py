"""D0/D3 join: strict whole-population and H0 CSP apply gate."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import patch_dotnet_001_d3_collect as adapter
import patch_dotnet_001_d3_oracle as oracle
from test_patch_dotnet_001_d3_oracle import fake_inputs


def fixture(folder: Path):
    inputs, pairs = fake_inputs(5)
    root = folder / "root"
    root.mkdir()
    (root / "v2-pairs.json").write_text("fixture")
    lab_items = []
    scan_items = []
    for row in inputs["rows"]:
        lab_items.append({
            **{k: row[k] for k in ("family", "base", "target", "path", "targetSize")},
            "patchBytes": row["d0PhysicalBytes"],
            "patchSha256": "f"*64,
            "applySeconds": 0.0001,
        })
        scan_items.append({
            **{k: row[k] for k in ("family", "base", "target", "path",
                                  "baseSize", "targetSize", "baseSha256", "targetSha256",
                                  "scannerStatus", "baseSlotCount", "targetSlotCount",
                                  "baseReconstructedSha256", "targetReconstructedSha256")}
        })
    lab = {
        "schema": adapter.LAB_SCHEMA,
        "lane": "csp",
        "applyRepeats": 1,
        "applyCheck": "seq",
        "execution": None,
        "files": lab_items,
    }
    scan = {"schema": adapter.SCAN_SCHEMA, "sourcePairsSha256": oracle.PAIRS_SHA,
            "role": "calibration", "rows": scan_items}
    return root, pairs, lab, scan


class JoinTests(unittest.TestCase):
    def test_join_exact_and_recomputed_oracle(self):
        with tempfile.TemporaryDirectory() as d:
            folder = Path(d)
            root, pairs, lab, scan = fixture(folder)
            with patch.object(oracle, "load_locked_pairs", return_value=pairs):
                evidence = adapter.prepare(root)
                self.assertEqual(1242, evidence["changedFiles"])
                lab["corpusPairsSha256"] = oracle.hash_bytes((root / "pairs.json").read_bytes())
                lab_file = folder / "d0.json"
                scan_file = folder / "scan.json"
                lab_file.write_bytes(adapter.dump(lab))
                scan_file.write_bytes(adapter.dump(scan))
                result = adapter.combine(root, lab_file, scan_file, folder / "joined.json",
                                         "a"*40, 42)
                self.assertEqual(1242, result["changedRows"])
                report = json.loads((folder / "joined.json").read_text())
                self.assertEqual(5, sum(r["scannerStatus"] == "VERIFIED_BOTH"
                                        for r in report["rows"]))
                self.assertEqual(1242, len(report["rows"]))
                actual = oracle.evaluate(report, pairs)
                self.assertEqual(4740, actual["upperBoundSavingBytes"])
                self.assertEqual(5, actual["scannerVerifiedRows"])
                with self.assertRaises(oracle.OracleError):
                    adapter.combine(root, lab_file, scan_file, folder / "joined.json",
                                    "a"*40, 42)

    def test_forged_policy_apply_or_missing_row_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            folder=Path(d)
            root,pairs,lab,scan=fixture(folder)
            with patch.object(oracle, "load_locked_pairs", return_value=pairs):
                adapter.prepare(root)
                lab["corpusPairsSha256"]=oracle.hash_bytes((root/"pairs.json").read_bytes())
                cases=[
                    lambda l,s: l.update(lane="csp-raw"),
                    lambda l,s: l.update(applyCheck="off"),
                    lambda l,s: l["files"][0].update(applySeconds=None),
                    lambda l,s: l["files"].pop(),
                    lambda l,s: s["rows"].pop(),
                    lambda l,s: s["rows"][0].update(targetSha256="0"*64),
                    lambda l,s: l.update(corpusPairsSha256="f"*64),
                ]
                for i,mutator in enumerate(cases):
                    bad_l=json.loads(json.dumps(lab))
                    bad_s=json.loads(json.dumps(scan))
                    mutator(bad_l,bad_s)
                    lp=folder/f"lab-{i}.json"
                    sp=folder/f"scan-{i}.json"
                    lp.write_bytes(adapter.dump(bad_l))
                    sp.write_bytes(adapter.dump(bad_s))
                    with self.subTest(i=i),self.assertRaises(oracle.OracleError):
                        adapter.combine(root,lp,sp,folder/f"out-{i}.json","a"*40,42)


if __name__=="__main__":
    unittest.main()
