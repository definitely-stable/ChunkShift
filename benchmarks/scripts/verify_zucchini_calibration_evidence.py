#!/usr/bin/env python3
"""Bind a single successful frozen Zucchini calibration to its actual binary.

The holdout runner must first verify the *calibration* artifact and re-check
its retained upstream *build* artifact with the already frozen G4 auditors.
All comparisons are pure; this script never generates or measures patch bytes.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import re
from pathlib import Path

NAME = "PATCH-GAP-001 G4 Zucchini calibration"
WORKFLOW = ".github/workflows/patch-gap-001-g4-zucchini-calibration.yml"
CAL_JOB = "calibration"
ARTIFACT_PREFIX = "patch-gap-001-zucchini-calibration-"
SCHEMA = "chunkshift.patch-gap-g4-zucchini-calibration-seal.v1"
PIN = Path("docs/research/results/data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json")
INVENTORY = Path("docs/research/results/data/PATCH-GAP-001-20261003-001/subsets/g4.json")
H0 = Path("docs/research/results/data/PATCH-GAP-001-G4-EVIDENCE-20261008-001")


def require(test: bool, label: str) -> None:
    if not test:
        raise ValueError("Zucchini sealed evaluation refused: " + label)


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for b in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(b)
    return h.hexdigest()


def import_script(filename: str, name: str):
    path = Path(__file__).with_name(filename)
    spec = importlib.util.spec_from_file_location(name, path)
    require(spec is not None and spec.loader is not None, "cannot load independent oracle")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def verify_run(run: dict, jobs: dict, artifacts: dict, root: Path,
               calibration_run_id: int, build_run_id: int) -> dict:
    """Prove that a *real* first-attempt main calibration job succeeded."""
    require(type(calibration_run_id) is int and calibration_run_id > 0
            and type(build_run_id) is int and build_run_id > 0,
            "invalid run IDs")
    require(run.get("id") == calibration_run_id
            and run.get("repository", {}).get("full_name") == "definitely-stable/ChunkShift",
            "calibration run/repository identity")
    require(run.get("name") == NAME
            and run.get("path", "").split("@")[0] == WORKFLOW,
            "wrong calibration workflow")
    require(run.get("event") == "workflow_dispatch"
            and run.get("head_branch") == "main"
            and run.get("run_attempt") == 1,
            "not the original explicit main calibration")
    require(run.get("status") == "completed" and run.get("conclusion") == "success",
            "calibration run not successful")
    sha = run.get("head_sha")
    require(isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{40}", sha) is not None,
            "invalid calibration checkout identity")

    matches = [j for j in jobs.get("jobs", []) if j.get("name") == CAL_JOB]
    require(len(matches) == 1 and matches[0].get("conclusion") == "success"
            and matches[0].get("status") == "completed",
            "actual calibration job skipped or failed")
    name = ARTIFACT_PREFIX + str(build_run_id) + "-" + sha
    matches = [a for a in artifacts.get("artifacts", []) if a.get("name") == name]
    require(len(matches) == 1, "missing or duplicate calibration artifact")
    artifact = matches[0]
    require(artifact.get("expired") is False
            and artifact.get("workflow_run", {}).get("id") == calibration_run_id
            and artifact.get("workflow_run", {}).get("head_sha") == sha,
            "calibration artifact expired or from wrong run")
    require(type(artifact.get("id")) is int and artifact["id"] > 0
            and isinstance(artifact.get("digest"), str)
            and re.fullmatch(r"sha256:[0-9a-f]{64}", artifact["digest"]) is not None,
            "missing calibration artifact SHA-256")

    origin = load(root / "origin/origin-proof.json")
    require(origin.get("status") == "REAL_BUILT_BINARY_VERIFIED"
            and origin.get("referenceStatus") == "NOT_RUN"
            and origin.get("buildRunId") == build_run_id,
            "calibration proof does not bind to real binary build")
    require(isinstance(origin.get("binarySha256"), str)
            and re.fullmatch(r"[0-9a-f]{64}", origin["binarySha256"]) is not None,
            "missing pinned tool identity")
    require((root / "source/zucchini").is_file()
            and digest(root / "source/zucchini") == origin["binarySha256"],
            "calibration binary bytes differ from provenance")
    # The full upstream build job, source tree, DEPS, depot_tools, GN args,
    # original compiler SHA and resolved dependencies are revalidated below.
    return {
        "schema": SCHEMA,
        "calibrationRunId": calibration_run_id,
        "calibrationHeadSha": sha,
        "calibrationArtifactId": artifact["id"],
        "calibrationArtifactDigest": artifact["digest"],
        "buildRunId": build_run_id,
        "binarySha256": origin["binarySha256"],
        "buildHeadSha": origin.get("buildHeadSha"),
        "buildArtifactId": origin.get("buildArtifactId"),
        "referenceScope": "whole-file G4 reference, not CSP RFC evidence",
        "evaluationStatus": "NOT_RUN",
    }


def verify_evidence(seal: dict, root: Path, pin: dict,
                    inventory: Path, h0_root: Path) -> dict:
    """Re-run independent build, inventory, and exact-subset H0 checks."""
    build = import_script("verify_zucchini_build_artifact.py", "sealed_build_oracle")
    actual = build.verify(
        load(root / "origin/run.json"),
        load(root / "origin/jobs.json"),
        load(root / "origin/artifacts.json"),
        root / "source", pin, seal["buildRunId"],
    )
    require(actual["binarySha256"] == seal["binarySha256"]
            and actual["buildHeadSha"] == seal["buildHeadSha"]
            and actual["buildArtifactId"] == seal["buildArtifactId"],
            "build artifact origin changed after calibration")

    audit = import_script("verify_patch_gap_g4_zucchini_reference.py",
                          "sealed_g4_audit")
    reference_path = root / "calibration-reference.json"
    reference = load(reference_path)
    require(reference.get("datasetRole") == "calibration"
            and reference.get("mode") == "measure", "not frozen calibration")
    require(reference.get("tool", {}).get("binarySha256") == seal["binarySha256"],
            "calibration did not use the verified built executable")
    audit.verify_frozen_population(reference, inventory)
    recomputed = audit.verify(reference, audit.frozen_h0(h0_root, "calibration"),
                              "calibration")
    require(recomputed == load(root / "calibration-audit.json"),
            "archived calibration audit differs from independent recomputation")
    require(recomputed.get("referenceOnly") is True
            and recomputed.get("rfcSizeGate") == "NOT_APPLICABLE",
            "illegal promotion of whole-file reference to RFC gate")

    seal = dict(seal)
    seal.update({
        "status": "SEALED_CALIBRATION_VERIFIED",
        "calibrationReferenceSha256": digest(reference_path),
        "calibrationAuditSha256": digest(root / "calibration-audit.json"),
        "frozenInventorySha256": digest(inventory),
        "frozenH0Role": "calibration",
        "calibrationFiles": 1049,
        "calibrationMatchedVerified": recomputed["matchedVerifiedFiles"],
        "evaluationStatus": "NOT_RUN",
    })
    return seal


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-json", type=Path, required=True)
    parser.add_argument("--jobs-json", type=Path, required=True)
    parser.add_argument("--artifacts-json", type=Path, required=True)
    parser.add_argument("--calibration-dir", type=Path, required=True)
    parser.add_argument("--calibration-run-id", type=int, required=True)
    parser.add_argument("--build-run-id", type=int, required=True)
    parser.add_argument("--pin", type=Path, default=PIN)
    parser.add_argument("--inventory", type=Path, default=INVENTORY)
    parser.add_argument("--h0", type=Path, default=H0)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    seal = verify_run(load(args.run_json), load(args.jobs_json),
                      load(args.artifacts_json), args.calibration_dir,
                      args.calibration_run_id, args.build_run_id)
    result = verify_evidence(seal, args.calibration_dir, load(args.pin),
                             args.inventory, args.h0)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n",
                           encoding="utf-8")
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    main()
