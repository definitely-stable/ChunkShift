#!/usr/bin/env python3
"""Source-bound GitHub-hosted resource evidence for PATCH-GAP-001 Zucchini.

No Chromium checkout, larger runner, codec measurement, API mutation or paid
resource allocation. The status applies ONLY to the measured hosted VM.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from zucchini_hosted_build import MIN_DISK_GIB, MIN_MEMORY_GIB, resources

HEX40 = re.compile(r"[0-9a-f]{40}\Z")
DATE = re.compile(r"20[0-9]{6}\Z")
SCHEMA = "chunkshift.patch-gap-g4-zucchini-hosted-capacity-observation.v1"
SOURCE_PIN = Path("docs/research/results/data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json")


def require(condition: bool, description: str) -> None:
    if not condition:
        raise ValueError("Zucchini capacity observation: " + description)


def measure(free_disk: int, physical_ram: int, disk_total: int, cpu_count: int,
            *, runner_environment: str, runner_label: str, repository: str,
            source_sha: str, event: str, ref: str, attempt: int,
            run_id: int, observation_date: str, source_pin_sha256: str) -> dict:
    require(runner_environment == "github-hosted", "GitHub-hosted runner only")
    require(runner_label == "ubuntu-24.04", "standard Ubuntu 24.04 only, not a paid larger runner")
    require(repository == "definitely-stable/ChunkShift", "wrong repository")
    require(isinstance(source_sha, str) and HEX40.fullmatch(source_sha) is not None, "Git SHA identity")
    require(event == "issue_comment" and ref == "refs/heads/main" and attempt == 1,
            "first-attempt main-branch operator event")
    require(type(run_id) is int and run_id > 0, "run ID")
    require(isinstance(observation_date, str) and DATE.fullmatch(observation_date) is not None,
            "date identifier")
    dt.datetime.strptime(observation_date, "%Y%m%d")
    require(type(disk_total) is int and 0 < free_disk <= disk_total, "disk capacity invariant")
    require(type(cpu_count) is int and cpu_count > 0, "CPU count")
    require(isinstance(source_pin_sha256, str) and re.fullmatch(r"[0-9a-f]{64}", source_pin_sha256),
            "source pin digest")
    hardware = resources(free_disk, physical_ram)
    require(hardware["requiredDiskGiB"] == MIN_DISK_GIB == 100
            and hardware["requiredMemoryGiB"] == MIN_MEMORY_GIB == 8,
            "must not weaken frozen build minima")
    return {
        "schema": SCHEMA,
        "experimentId": "PATCH-GAP-001",
        "reference": "G4-ZUCCHINI",
        "role": "resource-capacity-observation-only",
        "status": ("STANDARD_HOST_CAPACITY_PASS__BUILD_NOT_ATTEMPTED"
                   if hardware["canBuild"] else
                   "STANDARD_HOST_CAPACITY_FAIL__BUILD_NOT_ATTEMPTED"),
        "runner": {
            "provider": "github-hosted", "label": runner_label,
            "cpuCount": cpu_count, "volumeTotalBytes": disk_total,
        },
        "provenance": {
            "repository": repository, "sourceCommit": source_sha,
            "sourcePinSha256": source_pin_sha256,
            "workflowRunId": run_id, "runAttempt": attempt,
            "observationDateUtc": observation_date,
            "event": event, "gitRef": ref,
        },
        "requiredToolSource": "Chromium pinned Zucchini, no binary build",
        "hardware": hardware,
        "fullChromiumBuildExecuted": False,
        "referenceCalibrationExecuted": False,
        "referenceEvaluationExecuted": False,
        "organizationLargerRunnerInventoryVerified": False,
        "organizationBillingOrRunnerProvisioned": False,
        "releaseGateEligible": False,
        "limits": [
            "Only an observed standard hosted Ubuntu runner; not evidence of all runner configurations.",
            "A capacity PASS is only the frozen disk/RAM admission criterion, not a successful build.",
            "A standard-runner capacity FAIL does not establish that a paid larger runner is unavailable.",
            "No org plan, runner-group, paid runner assignment, billing permission or source build is inspected.",
            "No Zucchini patch bytes, performance results, CSP RFC factor or experimental REJECT is inferred.",
        ],
    }


def system_ram_bytes() -> int:
    for line in Path("/proc/meminfo").read_text(encoding="ascii").splitlines():
        if line.startswith("MemTotal:"):
            return int(line.split()[1]) * 1024
    raise ValueError("missing Linux MemTotal")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path(os.environ["RUNNER_TEMP"])
    require(root.is_dir(), "runner temporary volume absent")
    disk = shutil.disk_usage(root)
    raw_source = SOURCE_PIN.read_bytes()
    source = json.loads(raw_source)
    require(source.get("sourceCommit") == "26ec7d02bd81e5dc8c48f974d536a3f5fb043dc0",
            "frozen Chromium source pin changed")
    observation = measure(
        disk.free, system_ram_bytes(), disk.total, os.cpu_count() or 0,
        runner_environment=os.environ["RUNNER_ENVIRONMENT"],
        runner_label=os.environ["CAPACITY_PROBE_RUNNER_LABEL"],
        repository=os.environ["GITHUB_REPOSITORY"],
        source_sha=os.environ["GITHUB_SHA"], event=os.environ["GITHUB_EVENT_NAME"],
        ref=os.environ["GITHUB_REF"], attempt=int(os.environ["GITHUB_RUN_ATTEMPT"]),
        run_id=int(os.environ["GITHUB_RUN_ID"]),
        observation_date=os.environ["CAPACITY_PROBE_DATE"],
        source_pin_sha256=hashlib.sha256(raw_source).hexdigest(),
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(observation, indent=2, sort_keys=True) + "\n",
                           encoding="utf-8")
    print(json.dumps(observation, indent=2, sort_keys=True))
    # A capacity FAIL is a valid, observed scientific feasibility outcome.
    # It must not be rewritten to a fake PASS or to a measured codec ratio.


if __name__ == "__main__":
    main()
