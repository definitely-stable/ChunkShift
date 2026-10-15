"""Source-independent positive and negative conservation tests for PATCH-TREE F1."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from benchmarks.scripts import verify_patch_tree_001_f1 as oracle


class F1VerifierTests(unittest.TestCase):
    def test_identity_is_ordered_utf8_and_lane_scoped(self):
        a = oracle.filename("family", "old", "new", "a/ß", "T0")
        b = oracle.filename("family", "old", "new", "a/ß", "T1")
        c = oracle.filename("family", "old", "new", "a/ss", "T0")
        self.assertEqual(64 + len(".json"), len(a))
        self.assertEqual(3, len({a, b, c}))

    def test_full_synthetic_population_and_tamper_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            directory = root / "sidecars"
            directory.mkdir()
            pairs = [
                ("dotnet-aspnetcore-win-x64", "oldA", "newA"),
                ("dotnet-aspnetcore-win-x64", "oldB", "newB"),
                ("dotnet-runtime-linux-arm64", "oldA", "newA"),
                ("dotnet-runtime-linux-arm64", "oldB", "newB"),
            ]
            frozen = {"pairs": [{"family": f, "base": b, "target": t} for f, b, t in pairs]}
            pairbytes = json.dumps(frozen).encode()
            digest = hashlib.sha256(pairbytes).hexdigest()
            (root / "pairs.json").write_bytes(pairbytes)

            cal_pairs = []
            rows = []
            for pi, (family, base, target) in enumerate(pairs):
                files = []
                for fi in range(1049 if pi == 0 else 2 if pi == 1 else 0):
                    kind = "changed" if pi == 0 else "added"
                    path = f"path/{fi:04d}"
                    public = {
                        "family": family, "baseVersion": base,
                        "targetVersion": target, "path": path, "kind": kind,
                        "targetBytes": 5, "t0Bytes": 5,
                        "t1Bytes": 7, "t0PatchSha256": "a" * 64 if kind == "changed" else None,
                        "t1PatchSha256": "b" * 64
                    }
                    files.append(public)
                    for lane in ("T0", "T1"):
                        size = public["t0Bytes"] if lane == "T0" else public["t1Bytes"]
                        sha = public["t0PatchSha256"] if lane == "T0" else public["t1PatchSha256"]
                        name = None
                        obs_sha = None
                        if lane == "T1" or kind == "changed":
                            name = oracle.filename(family, base, target, path, lane)
                            obs = {
                                "schema": oracle.OBS_SCHEMA, "status": oracle.OBS_STATUS,
                                "lane": lane, "sourceLabel": f"{family}/{base}->{target}/{path}",
                                "publicPatchSha256": sha, "tracedPatchSha256": sha,
                                "physicalBytes": size, "reconciledPhysicalBytes": size,
                                "payloadEntries": 1, "storedRawBytes": 1,
                                "storedZstdBytes": 0, "payloadMetadataBytes": 0,
                                "indexBytes": 0, "otherPhysicalBytes": size - 1,
                                "selectedDictionaryEntries": 0, "candidateTrials": 0,
                                "entries": [{"targetIndex": 0, "targetLength": 5,
                                            "storedBytes": 1, "dictionaryRefs": 0,
                                            "selectedCandidate": None, "candidates": []}]
                            }
                            data = json.dumps(obs).encode()
                            (directory / name).write_bytes(data)
                            obs_sha = hashlib.sha256(data).hexdigest()
                        rows.append({
                            "family": family, "baseVersion": base, "targetVersion": target,
                            "path": path, "kind": kind, "lane": lane,
                            "physicalBytes": size, "publicPatchSha256": sha,
                            "observationFile": name, "observationSha256": obs_sha,
                        })
                t0 = sum(f["t0Bytes"] for f in files)
                t1 = sum(f["t1Bytes"] for f in files)
                cal_pairs.append({
                    "family": family, "baseVersion": base, "targetVersion": target,
                    "t0PayloadBytes": t0, "t1PayloadBytes": t1,
                    "t0WarmPhysicalBytes": t0 + 10,
                    "t1WarmPhysicalBytes": t1 + 10,
                    "files": files,
                })
            cal = {
                "schema": "chunkshift.patch-tree-t0-t1-calibration.v1",
                "status": "CALIBRATION_ONLY_NOT_DECISION", "pairsSha256": digest,
                "runId": "SYNTHETIC", "pairs": cal_pairs,
            }
            summary = {
                "schema": oracle.SCHEMA, "status": oracle.STATUS,
                "pairsSha256": digest, "runId": "SYNTHETIC",
                "pairCount": 4, "changedCount": 1049, "addedCount": 2,
                "t0PhysicalPayloadBytes": sum(p["t0PayloadBytes"] for p in cal_pairs),
                "t1PhysicalPayloadBytes": sum(p["t1PayloadBytes"] for p in cal_pairs),
                "t0WarmBytes": sum(p["t0WarmPhysicalBytes"] for p in cal_pairs),
                "t1WarmBytes": sum(p["t1WarmPhysicalBytes"] for p in cal_pairs),
                "reconciledObservations": 1049 * (5 + 7) + 2 * 7,
                "rawAddedBytes": 2 * 5,
                "files": rows,
            }
            for name, obj in (("calibration.json", cal), ("summary.json", summary)):
                (root / name).write_text(json.dumps(obj), encoding="utf-8")
            with patch.object(oracle, "PAIRS_HASH", digest):
                self.assertEqual(2100, oracle.verify(root / "summary.json",
                    root / "calibration.json", root / "pairs.json", directory)["observations"])
                victim = directory / rows[0]["observationFile"]
                victim.write_text(victim.read_text() + " ", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "observation SHA mismatch"):
                    oracle.verify(root / "summary.json",
                        root / "calibration.json", root / "pairs.json", directory)


if __name__ == "__main__":
    unittest.main()
