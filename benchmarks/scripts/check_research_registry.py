#!/usr/bin/env python3
"""Fail closed when the human and machine research experiment registries drift."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def parse_human_index(text: str, allowed_statuses: set[str]) -> dict[str, str]:
    result: dict[str, str] = {}
    for line in text.splitlines():
        if not line.startswith("| "):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) < 6 or cells[0] in {"ExperimentId", "---"}:
            continue
        experiment_id = cells[0]
        status_text = cells[4]
        status = status_text.split(maxsplit=1)[0] if status_text else ""
        if not experiment_id or status not in allowed_statuses:
            raise ValueError(
                f"human index has invalid experiment/status: {experiment_id!r} / {status_text!r}"
            )
        if experiment_id in result:
            raise ValueError(f"human index contains duplicate ExperimentId {experiment_id}")
        result[experiment_id] = status
    if not result:
        raise ValueError("human experiment index contains no experiment rows")
    return result


def parse_machine_registry(document: dict) -> tuple[set[str], dict[str, str]]:
    if document.get("schema") != "chunkshift.experiment-registry.v1":
        raise ValueError("machine registry schema mismatch")
    raw_statuses = document.get("status_values")
    experiments = document.get("experiments")
    if not isinstance(raw_statuses, list) or not raw_statuses:
        raise ValueError("machine registry status_values is missing or empty")
    if not isinstance(experiments, list) or not experiments:
        raise ValueError("machine registry experiments is missing or empty")

    allowed = {str(value) for value in raw_statuses}
    if len(allowed) != len(raw_statuses) or any(not value for value in allowed):
        raise ValueError("machine registry status_values must be unique non-empty strings")

    result: dict[str, str] = {}
    for row in experiments:
        if not isinstance(row, dict):
            raise ValueError("machine registry experiment row must be an object")
        experiment_id = str(row.get("id") or "")
        status = str(row.get("status") or "")
        if not experiment_id or status not in allowed:
            raise ValueError(
                f"machine registry has invalid experiment/status: {experiment_id!r} / {status!r}"
            )
        if experiment_id in result:
            raise ValueError(f"machine registry contains duplicate ExperimentId {experiment_id}")
        result[experiment_id] = status
    return allowed, result


def validate(human_text: str, machine_document: dict) -> dict[str, object]:
    allowed, machine = parse_machine_registry(machine_document)
    human = parse_human_index(human_text, allowed)

    missing_machine = sorted(set(human) - set(machine))
    missing_human = sorted(set(machine) - set(human))
    status_mismatches = [
        {"id": experiment_id, "human": human[experiment_id], "machine": machine[experiment_id]}
        for experiment_id in sorted(set(human) & set(machine))
        if human[experiment_id] != machine[experiment_id]
    ]

    if missing_machine or missing_human or status_mismatches:
        raise ValueError(
            "research registry drift: "
            + json.dumps(
                {
                    "missingMachine": missing_machine,
                    "missingHuman": missing_human,
                    "statusMismatches": status_mismatches,
                },
                sort_keys=True,
                separators=(",", ":"),
            )
        )

    return {
        "schema": "chunkshift.experiment-registry-consistency.v1",
        "experimentCount": len(human),
        "statusValues": sorted(allowed),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--index",
        type=Path,
        default=Path("docs/research/EXPERIMENT-INDEX.md"),
    )
    parser.add_argument(
        "--registry",
        type=Path,
        default=Path("benchmarks/experiments/research-registry.v1.json"),
    )
    args = parser.parse_args()

    try:
        result = validate(
            args.index.read_text(encoding="utf-8"),
            json.loads(args.registry.read_text(encoding="utf-8")),
        )
    except (OSError, ValueError, json.JSONDecodeError) as error:
        parser.error(str(error))

    print(
        f"research registry consistent: {result['experimentCount']} experiments; "
        f"statuses={','.join(result['statusValues'])}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
