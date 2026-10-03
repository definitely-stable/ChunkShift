import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("check_research_registry.py")
SPEC = importlib.util.spec_from_file_location("research_registry_check", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class ResearchRegistryConsistencyTests(unittest.TestCase):
    def machine(self, rows):
        return {
            "schema": "chunkshift.experiment-registry.v1",
            "status_values": [
                "PLANNED",
                "RUNNING",
                "EVIDENCE_READY",
                "ADOPT",
                "DEFER",
                "REJECT",
                "SUPERSEDED",
            ],
            "experiments": rows,
        }

    def human(self, rows):
        lines = [
            "| ExperimentId | Area | Owner | Question | Status | Evidence |",
            "| --- | --- | --- | --- | --- | --- |",
        ]
        for experiment_id, status in rows:
            lines.append(
                f"| {experiment_id} | test | #1 | question | {status} | — |"
            )
        return "\n".join(lines) + "\n"

    def test_decorated_human_status_matches_machine_status(self):
        result = MODULE.validate(
            self.human(
                [
                    ("A", "ADOPT (detail)"),
                    ("B", "RUNNING"),
                ]
            ),
            self.machine(
                [
                    {"id": "A", "status": "ADOPT"},
                    {"id": "B", "status": "RUNNING"},
                ]
            ),
        )

        self.assertEqual(2, result["experimentCount"])

    def test_missing_machine_experiment_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "missingMachine"):
            MODULE.validate(
                self.human([("A", "PLANNED"), ("B", "PLANNED")]),
                self.machine([{"id": "A", "status": "PLANNED"}]),
            )

    def test_status_mismatch_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "statusMismatches"):
            MODULE.validate(
                self.human([("A", "ADOPT")]),
                self.machine([{"id": "A", "status": "PLANNED"}]),
            )

    def test_unknown_human_status_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "invalid experiment/status"):
            MODULE.validate(
                self.human([("A", "BROKEN")]),
                self.machine([{"id": "A", "status": "PLANNED"}]),
            )


if __name__ == "__main__":
    unittest.main()
