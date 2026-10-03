import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("evaluate_patch_enc_005_g2.py")
SPEC = importlib.util.spec_from_file_location("patch_enc_005_g2_eval", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class PatchEnc005G2EvaluatorTests(unittest.TestCase):
    SOURCE = "a" * 40

    def make_sample(self, root: Path):
        rows = []
        ordinal = 0
        for family, base, target in MODULE.PAIR_KEYS:
            pair_rows = []
            for index in range(64):
                chunk = f"{ordinal + 1:064x}"
                row = {
                    "family": family,
                    "baseVersion": base,
                    "targetVersion": target,
                    "path": f"file-{index:03d}.bin",
                    "targetIndex": index,
                    "targetChunkId": chunk,
                    "targetOffset": index * 100,
                    "targetLength": 100,
                }
                row["sampleKeySha256"] = MODULE.sample_key_sha256(row)
                pair_rows.append(row)
                ordinal += 1
            pair_rows.sort(
                key=lambda row: (
                    row["sampleKeySha256"],
                    row["path"],
                    row["targetIndex"],
                )
            )
            rows.extend(pair_rows)

        sample = {
            "schema": MODULE.SAMPLE_SCHEMA,
            "experimentId": MODULE.EXPERIMENT_ID,
            "protocolCommit": MODULE.PROTOCOL_COMMIT,
            "sourceCommit": self.SOURCE,
            "datasetRole": MODULE.DATASET_ROLE,
            "datasetSha256": MODULE.DATASET_SHA256,
            "oracleSampleSha256": MODULE.canonical_rows_sha256(rows),
            "rows": rows,
        }
        raw = json.dumps(
            sample,
            ensure_ascii=False,
            separators=(",", ":"),
        ).encode("utf-8") + b"\n"
        sample_path = root / "sample.json"
        sample_path.write_bytes(raw)
        lock = {
            "schema": MODULE.LOCK_SCHEMA,
            "experimentId": MODULE.EXPERIMENT_ID,
            "protocolCommit": MODULE.PROTOCOL_COMMIT,
            "sourceCommit": self.SOURCE,
            "datasetRole": MODULE.DATASET_ROLE,
            "datasetSha256": MODULE.DATASET_SHA256,
            "sampleSha256": __import__("hashlib").sha256(raw).hexdigest(),
            "oracleSampleSha256": sample["oracleSampleSha256"],
            "rowCount": 256,
        }
        lock_path = root / "lock.json"
        lock_path.write_text(json.dumps(lock), encoding="utf-8")
        return sample_path, lock_path, sample

    def make_oracle(self, sample, pair_costs):
        rows = []
        for row in sample["rows"]:
            pair = (row["family"], row["baseVersion"], row["targetVersion"])
            oracle_cost = pair_costs[pair]
            rows.append(
                {
                    **{field: row[field] for field in MODULE.IDENTITY_FIELDS},
                    "candidateStartsEnumerated": 20,
                    "validCandidateCount": 15,
                    "h0Encoding": "raw",
                    "h0StoredBytes": 100,
                    "h0DictionaryRefs": 0,
                    "h0CostBytes": 100,
                    "h0StartIndex": None,
                    "h0StartOffset": None,
                    "h0RecordCount": None,
                    "h0FirstChunkId": None,
                    "oracleEncoding": "zstd",
                    "oracleStoredBytes": oracle_cost,
                    "oracleDictionaryRefs": 0,
                    "oracleCostBytes": oracle_cost,
                    "oracleStartIndex": None,
                    "oracleStartOffset": None,
                    "oracleRecordCount": None,
                    "oracleFirstChunkId": None,
                    "oracleStartDistanceBytes": None,
                    "savedBytes": 100 - oracle_cost,
                }
            )
        return {
            "schema": MODULE.ORACLE_SCHEMA,
            "experimentId": MODULE.EXPERIMENT_ID,
            "runId": "PATCH-ENC-005/RUN-20261004-001-" + self.SOURCE + "-linux-x64",
            "protocolCommit": MODULE.PROTOCOL_COMMIT,
            "sourceCommit": self.SOURCE,
            "datasetRole": MODULE.DATASET_ROLE,
            "datasetSha256": MODULE.DATASET_SHA256,
            "oracleSampleSha256": sample["oracleSampleSha256"],
            "policy": MODULE.POLICY,
            "candidateOrder": MODULE.CANDIDATE_ORDER,
            "rows": rows,
        }

    def test_main_gate_passes_at_exact_overall_boundary_with_two_pair_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            sample_path, lock_path, sample = self.make_sample(Path(directory))
            loaded, _ = MODULE.validate_lock(sample_path, lock_path)
            costs = {
                MODULE.PAIR_KEYS[0]: 90,
                MODULE.PAIR_KEYS[1]: 90,
                MODULE.PAIR_KEYS[2]: 100,
                MODULE.PAIR_KEYS[3]: 100,
            }
            verdict = MODULE.evaluate(loaded, self.make_oracle(sample, costs))

        self.assertEqual("PASS", verdict["status"])
        self.assertEqual("FULL_PHASE_B", verdict["nextAction"])
        self.assertTrue(verdict["overallFivePercentGate"])
        self.assertEqual(2, verdict["pairPassCount"])
        self.assertEqual(0.95, verdict["sampleRatio"])

    def test_overall_pass_with_only_one_pair_pass_goes_to_guard(self):
        with tempfile.TemporaryDirectory() as directory:
            sample_path, lock_path, sample = self.make_sample(Path(directory))
            loaded, _ = MODULE.validate_lock(sample_path, lock_path)
            costs = {
                MODULE.PAIR_KEYS[0]: 80,
                MODULE.PAIR_KEYS[1]: 100,
                MODULE.PAIR_KEYS[2]: 100,
                MODULE.PAIR_KEYS[3]: 100,
            }
            verdict = MODULE.evaluate(loaded, self.make_oracle(sample, costs))

        self.assertEqual("MISS", verdict["status"])
        self.assertEqual("H6_O_GUARD_ONLY", verdict["nextAction"])
        self.assertTrue(verdict["overallFivePercentGate"])
        self.assertEqual(1, verdict["pairPassCount"])

    def test_overall_gate_miss_goes_to_guard(self):
        with tempfile.TemporaryDirectory() as directory:
            sample_path, lock_path, sample = self.make_sample(Path(directory))
            loaded, _ = MODULE.validate_lock(sample_path, lock_path)
            costs = {pair: 98 for pair in MODULE.PAIR_KEYS}
            verdict = MODULE.evaluate(loaded, self.make_oracle(sample, costs))

        self.assertEqual("MISS", verdict["status"])
        self.assertFalse(verdict["overallFivePercentGate"])
        self.assertEqual("H6_O_GUARD_ONLY", verdict["nextAction"])

    def test_lock_rejects_tampered_sample_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            sample_path, lock_path, _ = self.make_sample(root)
            sample_path.write_bytes(sample_path.read_bytes() + b" ")

            with self.assertRaisesRegex(ValueError, "sample file SHA-256 mismatch"):
                MODULE.validate_lock(sample_path, lock_path)

    def test_oracle_rejects_run_id_source_mismatch(self):
        with tempfile.TemporaryDirectory() as directory:
            sample_path, lock_path, sample = self.make_sample(Path(directory))
            loaded, _ = MODULE.validate_lock(sample_path, lock_path)
            oracle = self.make_oracle(sample, {pair: 90 for pair in MODULE.PAIR_KEYS})
            oracle["runId"] = (
                "PATCH-ENC-005/RUN-20261004-001-" + ("b" * 40) + "-linux-x64"
            )

            with self.assertRaisesRegex(ValueError, "runId source commit"):
                MODULE.evaluate(loaded, oracle)

    def test_oracle_rejects_dictionary_distance_mismatch(self):
        with tempfile.TemporaryDirectory() as directory:
            sample_path, lock_path, sample = self.make_sample(Path(directory))
            loaded, _ = MODULE.validate_lock(sample_path, lock_path)
            oracle = self.make_oracle(sample, {pair: 90 for pair in MODULE.PAIR_KEYS})
            row = oracle["rows"][0]
            row["oracleEncoding"] = "zstd-dictionary"
            row["oracleStoredBytes"] = 1
            row["oracleDictionaryRefs"] = 1
            row["oracleCostBytes"] = 33
            row["oracleStartIndex"] = 0
            row["oracleStartOffset"] = 50
            row["oracleRecordCount"] = 1
            row["oracleFirstChunkId"] = "c" * 64
            row["oracleStartDistanceBytes"] = 49
            row["savedBytes"] = 67

            with self.assertRaisesRegex(ValueError, "dictionary distance mismatch"):
                MODULE.evaluate(loaded, oracle)

    def test_oracle_rejects_ref32_cost_mismatch(self):
        with tempfile.TemporaryDirectory() as directory:
            sample_path, lock_path, sample = self.make_sample(Path(directory))
            loaded, _ = MODULE.validate_lock(sample_path, lock_path)
            oracle = self.make_oracle(sample, {pair: 90 for pair in MODULE.PAIR_KEYS})
            oracle["rows"][0]["oracleCostBytes"] = 91

            with self.assertRaisesRegex(ValueError, "REF32 cost accounting mismatch"):
                MODULE.evaluate(loaded, oracle)

    def test_oracle_rejects_cost_above_h0(self):
        with tempfile.TemporaryDirectory() as directory:
            sample_path, lock_path, sample = self.make_sample(Path(directory))
            loaded, _ = MODULE.validate_lock(sample_path, lock_path)
            oracle = self.make_oracle(sample, {pair: 90 for pair in MODULE.PAIR_KEYS})
            row = oracle["rows"][0]
            row["oracleStoredBytes"] = 101
            row["oracleCostBytes"] = 101
            row["savedBytes"] = -1

            with self.assertRaisesRegex(ValueError, "oracle cost exceeds H0"):
                MODULE.evaluate(loaded, oracle)


if __name__ == "__main__":
    unittest.main()
