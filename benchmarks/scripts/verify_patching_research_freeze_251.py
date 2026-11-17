#!/usr/bin/env python3
"""Fail-closed decision-ledger replay for Patching Research Freeze #251.

Validates retained evidence, architecture status and staged release boundaries.
No new benchmark, remote issue query, CSP encode/decode or public API freeze.
Original experiment archives are audited by their owning experiment workflows.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEDGER = "benchmarks/experiments/patching-research-freeze-251-20261117.json"
REGISTRY = "benchmarks/experiments/research-registry.v1.json"
INDEX = "docs/research/EXPERIMENT-INDEX.md"
ROADMAP = "ROADMAP.md"
PLAN = "PLAN.md"
CLI = "src/ChunkShift.Cli/ChunkShift.Cli.csproj"
REQUIRED = {
    "PATCH-ENC-005": ("REJECT", 181),
    "PATCH-GAP-001": ("DEFER", 183),
    "PATCH-TREE-001": ("DEFER", 184),
    "PATCH-META-001": ("DEFER", 220),
    "PATCH-INCR-001": ("DEFER", 150),
    "PATCH-DICT-001": ("DEFER", 222),
    "PATCH-DOTNET-001": ("DEFER", 221),
    "PATCH-ML-001": ("DEFER", 223),
    "PATCH-COMPILER-001": ("DEFER", 224),
    "PATCH-EXEC-001": ("DEFER", 225),
    "TRUST-SIG-001": ("ADOPT", 185),
}
LOCKED_SPEC = {
    "cspV1": ("34c7d023b88f36f58945dc7904a79969470fe3bd", "Frozen"),
    "csmV1": ("aaa653157a0d850d7d684492e1b362b2b3dfafe1", None),
    "rfc6": ("82bbee760b728c38f5d1a05449d5e80b85758970", "Accepted architecture boundary"),
    "rfc5": ("66d0d78467c6297025ebbf0a2ea5ac5adaa00ddd", "Proposed"),
}
RESIDUAL_SHA = "538dee95ca551c2fcd5c30ddb9ed46d46903bd4d"
REPORT = "docs/architecture/PATCHING-FIRST-RELEASE-RESEARCH-FREEZE-20261117.md"


def need(predicate: bool, error: str) -> None:
    if not predicate:
        raise ValueError(error)


def git_blob_sha(data: bytes) -> str:
    # Git blob SHA-1 identity, *not* a cryptographic format-integrity claim.
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\x00" + data).hexdigest()


def unique_map(records: list[dict], field: str) -> dict[str, dict]:
    out: dict[str, dict] = {}
    for record in records:
        key = record.get(field)
        need(isinstance(key, str) and key != "" and key not in out,
             f"duplicate / invalid {field}: {key}")
        out[key] = record
    return out


def check_decision(ledger: dict, registry: dict, index: str,
                   report: str, roadmap: str, plan: str, cli: str) -> dict:
    need(ledger.get("schema") == "chunkshift.patching.first-release-research-freeze.v1",
         "ledger schema changed")
    need(ledger.get("issue") == 251 and
         ledger.get("baselineMainSha") == "3f664a0ea36634cf2a972323e3ae5a5470d44845",
         "owner/base evidence changed")
    need(ledger.get("decision") == "RETAIN_CSP_V1_NO_VNEXT" and
         ledger.get("scope") == "FIRST_RELEASE_PERSISTED_RESEARCH_ONLY" and
         ledger.get("researchGateOnMergeAndGreenCi") is True, "unsafe release verdict")
    source_specs = ledger.get("sourceFormat", {})
    need(set(source_specs) == set(LOCKED_SPEC), "source spec inventory")
    for name, (sha, status) in LOCKED_SPEC.items():
        d = source_specs[name]
        need(d.get("gitBlobSha") == sha, "spec lock: " + name)
        if status:
            need(d.get("status") == status, "spec status: " + name)
    need(source_specs["cspV1"].get("major") == 1 and
         source_specs["cspV1"].get("vectorCount") == 125, "CSP v1 fixture count")

    required = unique_map(ledger.get("requiredResearch", []), "id")
    need(set(required) == set(REQUIRED), "required research inventory")
    recorded = unique_map(registry.get("experiments", []), "id")
    for exp_id, (status, number) in REQUIRED.items():
        r = required[exp_id]
        need(r.get("issueNumber") == number and r.get("status") == status,
             "ledger status: " + exp_id)
        need(recorded.get(exp_id, {}).get("status") == status,
             "registry conflict: " + exp_id)
        need(r.get("evidence", "").startswith("docs/research/results/") and
             len(r.get("gitBlobSha", "")) == 40, "missing source: " + exp_id)
        if exp_id == "TRUST-SIG-001":
            need(r.get("scope") == "ARCHITECTURE_ONLY_NO_PERSISTED_CHANGE",
                 "trust must not imply format ADOPT")
        else:
            need(r.get("scope") == "FIRST_RELEASE", "source scope")
    needs = ledger.get("conditionalResearch", [])
    need(len(needs) == 1 and needs[0].get("id") == "PATCH-RESIDUAL-001",
         "conditional first-release residual gate")
    residue = needs[0]
    need(residue.get("issueNumber") == 262 and
         residue.get("registryStatus") == "PLANNED" and
         residue.get("firstReleaseDisposition") ==
         "DEFER_NO_DECISION_GRADE_REPRESENTATION" and
         residue.get("gitBlobSha") == RESIDUAL_SHA and
         residue.get("phaseBOriginalHoldoutRun") is False and
         residue.get("admittedFormat") is False and
         residue.get("issueRemainsOpen") is True,
         "residual must remain post-release research, not new CSP format")
    actual_residual = recorded.get("PATCH-RESIDUAL-001", {})
    need(actual_residual.get("status") == "PLANNED" and
         actual_residual.get("firstReleaseDisposition", "").startswith(
             "DEFER_NO_DECISION_GRADE_REPRESENTATION") and
         actual_residual.get("firstReleaseEvidence") == REPORT,
         "registry residual first-release claim mismatch")
    need(residue.get("evidence") ==
         "docs/benchmarks/PATCH-RESIDUAL-001-PROTOCOL.md", "residual protocol")
    adoption = ledger.get("adoptionAudit", {})
    need(adoption.get("newPersistedSemanticsAdoptsAfterCspV1Freeze") == [] and
         adoption.get("priorAdoptsCoveredByCspV1") is True and
         adoption.get("trustArchitectureDoesNotChangeCspV1") is True and
         adoption.get("formatMigrationRequiredNow") is False and
         adoption.get("futureBreakingFormatNeedsAcceptedRfcAndNewVectors") is True,
         "unreviewed new persisted ADOPT/version migration")
    interactions = ledger.get("interactions", {})
    need(interactions.get("eligibleNewDistinctRepresentationFamilies") == 0 and
         interactions.get("individuallyAdmittedInteractingFactors") == 0 and
         interactions.get("firstReleaseTrigger") is False,
         "unresolved representation interaction")
    safe = ledger.get("safety", {})
    required_true = ("preserveCspV1", "preserveCsmV1",
                     "preserveHashAndProfileIdentities", "preserveExistingH2W2",
                     "noPublicApiFreezeYet", "noCliProductFreezeYet",
                     "noFirstPartyTreePublisher", "noEmbeddedTrustMetadata",
                     "retainedOriginalResearchNoNewRunId")
    for key in required_true:
        need(safe.get(key) is True, "unsafe or missing boundary " + key)
    for key in ("publicNugetPublicationAuthorizedByThisDecision",
                "publicationRepoPortAuthorizedByThisDecision"):
        need(safe.get(key) is False, "unapproved release action " + key)
    need(ledger.get("report") == REPORT, "report path")
    need("**RETAIN CSP v1 / NO CSP vNext" in report and
         "ADOPT ARCHITECTURE ONLY" in report and
         "Inventory of first-release research results adopted after" in report and
         "EMPTY (0)" in report and "NOT_TRIGGERED" in report and
         "PATCH-RESIDUAL-001" in report and
         "VERSION" not in ledger.get("decision", ""), "report missing synthesis")
    idx = [line for line in index.splitlines() if line.startswith("| PATCH-RESIDUAL-001 |")]
    need(len(idx) == 1 and "| PLANNED (first-release persisted representation **DEFER**" in idx[0],
         "human research index residual mismatch")
    need("Patching Research Freeze" in roadmap and
         "Patching Research Freeze" in plan and REPORT in ledger.get("report", "") and
         "RETAIN CSP v1" in roadmap and "retain CSP v1" in plan,
         "roadmap/plan sequence mismatch")
    need("<IsPackable>false</IsPackable>" in cli,
         "CLI packing unexpectedly enabled")
    need(ledger.get("nextOwnerGates") and
         len(ledger["nextOwnerGates"]) == 4 and
         ledger["nextOwnerGates"][0].startswith("#140"),
         "unapproved public handoff")
    return {"decision": ledger["decision"], "requiredDecisions": len(required),
            "conditional": residue["firstReleaseDisposition"],
            "newPersistedAdopts": 0, "firstReleaseInteractionsTriggered": False}


def load_sources(root: Path, ledger: dict):
    # Fail closed on source *content* drift independent of registry metadata.
    for name, source in ledger["sourceFormat"].items():
        path = root / source["path"]
        need(path.is_file() and git_blob_sha(path.read_bytes()) == LOCKED_SPEC[name][0],
             "normative source changed: " + name)
    for record in ledger["requiredResearch"] + ledger["conditionalResearch"]:
        path = root / record["evidence"]
        need(path.is_file() and git_blob_sha(path.read_bytes()) == record["gitBlobSha"],
             "original evidence changed: " + record["id"])
    need(git_blob_sha((root / ledger["conditionalResearch"][0]["evidence"]).read_bytes()) ==
         RESIDUAL_SHA, "residual source lock")


def self_test(params: list) -> int:
    # Explicit mutants should each be rejected for independent reasons.
    mutators = [
        lambda x: x[0].__setitem__("decision", "ADOPT_CSP_VNEXT"),
        lambda x: x[0]["requiredResearch"].pop(),
        lambda x: x[0]["requiredResearch"][0].__setitem__("status", "ADOPT"),
        lambda x: x[1]["experiments"][
            next(i for i,e in enumerate(x[1]["experiments"]) if e["id"] == "PATCH-GAP-001")
        ].__setitem__("status", "ADOPT"),
        lambda x: x[0]["sourceFormat"]["cspV1"].__setitem__(
            "gitBlobSha", "0" * 40),
        lambda x: x[0]["sourceFormat"]["rfc5"].__setitem__(
            "status", "Accepted"),
        lambda x: x[0]["conditionalResearch"][0].__setitem__(
            "firstReleaseDisposition", "ADOPT"),
        lambda x: x[1]["experiments"][
            next(i for i,e in enumerate(x[1]["experiments"]) if e["id"] == "PATCH-RESIDUAL-001")
        ].__setitem__("status", "ADOPT"),
        lambda x: x[0]["interactions"].__setitem__("firstReleaseTrigger", True),
        lambda x: x[0]["adoptionAudit"].__setitem__(
            "newPersistedSemanticsAdoptsAfterCspV1Freeze", ["unreviewed"]),
        lambda x: x[0]["safety"].__setitem__(
            "publicNugetPublicationAuthorizedByThisDecision", True),
        lambda x: x.__setitem__(2, x[2].replace(
            "| PLANNED (first-release persisted representation **DEFER**",
            "| ADOPT")),
    ]
    passed = 0
    for mutate in mutators:
        copy_params = copy.deepcopy(params)
        mutate(copy_params)
        try:
            check_decision(*copy_params)
        except ValueError:
            passed += 1
        else:
            raise AssertionError("malicious mutation unexpectedly accepted")
    need(passed == len(mutators) == 12, "negative test population")
    return passed


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--repo-root", type=Path, default=ROOT)
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args()
    root = args.repo_root
    ledger = json.loads((root / LEDGER).read_text(encoding="utf-8"))
    registry = json.loads((root / REGISTRY).read_text(encoding="utf-8"))
    report = (root / REPORT).read_text(encoding="utf-8")
    index = (root / INDEX).read_text(encoding="utf-8")
    roadmap = (root / ROADMAP).read_text(encoding="utf-8")
    plan = (root / PLAN).read_text(encoding="utf-8")
    cli = (root / CLI).read_text(encoding="utf-8")
    load_sources(root, ledger)
    parameters = [ledger, registry, index, report, roadmap, plan, cli]
    result = check_decision(*parameters)
    result["rejectedMutations"] = self_test(parameters) if args.self_test else 0
    print(json.dumps({"verdict": "PASS", **result}, indent=2))


if __name__ == "__main__":
    main()
