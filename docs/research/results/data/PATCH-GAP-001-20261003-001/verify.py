"""Verify the frozen Stage-A snapshot and recompute its inventory totals (no size gate)."""
import argparse
import base64
import collections
import hashlib
import json
from pathlib import Path

PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
SOURCE = "d43986e3f4a07806cbd5adaf96dd2a9f2cdae383"
PAIRS = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
FAMILIES = {"dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64", "node-win-x64", "node-linux-x64"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def canonical(value):
    # These frozen subset rows contain ASCII strings without HTML-sensitive characters.
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()


def key(row):
    return tuple(row[field] for field in ("family", "baseVersion", "targetVersion", "path"))


def totals(g4, g5):
    result = {"g4": {}, "g5": {"status": "NOT_PRESENT" if not any(r["puffinSupported"] for r in g5) else "PRESENT"}}
    for role in ("calibration", "evaluation"):
        rows = [r for r in g4 if r["datasetRole"] == role]
        eligible = [r for r in rows if r["gateEligible"]]
        result["g4"][role] = {
            "rows": len(rows), "gateEligibleRows": len(eligible),
            "targetBytes": sum(r["targetBytes"] for r in rows),
            "uniqueMissingBytes": sum(r["uniqueMissingBytes"] for r in rows),
            "eligibleTargetBytes": sum(r["targetBytes"] for r in eligible),
            "eligibleUniqueMissingBytes": sum(r["uniqueMissingBytes"] for r in eligible),
            "targetClassifications": dict(sorted(collections.Counter(r["target"]["detail"] for r in rows).items())),
        }
        rows = [r for r in g5 if r["datasetRole"] == role]
        result["g5"][role] = {"rows": len(rows), "puffinSupportedRows": sum(r["puffinSupported"] for r in rows),
            "targetBytes": sum(r["targetBytes"] for r in rows), "uniqueMissingBytes": sum(r["uniqueMissingBytes"] for r in rows)}
    return result


def verify(root, raw_dir=None):
    def load(relative):
        return json.loads((root / relative).read_bytes())
    summary, lineage, inputs = load("stage-a-summary.json"), load("toolchain-producer.json"), load("inputs.json")
    lock, locator, tools = load("inventory-lock-run.json"), load("evidence-input/puffin-locator.json"), load("tools.json")
    require(summary["puffinProducer"] == lineage, "summary/producer lineage mismatch")
    require(summary["protocolCommit"] == inputs["protocolCommit"] == tools["protocolCommit"] == PROTOCOL, "document protocol")
    require(summary["sourceCommit"] == inputs["sourceCommit"] == lineage["consumerSourceCommit"] == SOURCE, "document source")
    require(lineage["producerRunId"] == 37101894709 and lineage["producerHeadSha"] == SOURCE, "producer identity")
    run_document = load("runs.json")
    require(run_document["repository"] == lineage["repository"] == "definitely-stable/ChunkShift", "repository")
    runs = run_document["runs"]
    require([r["id"] for r in runs] == [37101894709, 37101930398], "run identity")
    for run, event, workflow in zip(runs, ("push", "workflow_run"), ("puffin-toolchain", "stage-a")):
        require(run["run_attempt"] == 1 and run["head_branch"] == "main" and run["head_sha"] == SOURCE, "run attempt/source")
        require(run["status"] == "completed" and run["conclusion"] == "success" and run["event"] == event, "run verdict/event")
        require(run["path"] == f".github/workflows/patch-gap-001-{workflow}.yml", "run workflow")
    require(summary["runId"] == "PATCH-GAP-001/RUN-20261003-003-d43986e-linux-x64", "RunId")
    require(lock["provenance"]["runId"] == summary["runId"] and lock["provenance"]["dirty"] is False, "clean inventory RunId")
    require(lock["provenance"]["sourceCommit"] == SOURCE, "inventory source")
    require(inputs["corpusPairsSha256"] == PAIRS, "corpus pairs")
    require(inputs["corpusManifestSha256"] == "0e0ba2f1d0a0f7a27690f88a55cd045008a1ea71277b04680775e4c6ee3c028f", "corpus manifest")
    require(inputs["sourceAssetsSha256"] == "3ae2e55108051700a07425896e01b1703683cd896ed4f54e4a6c652fe55bda3e", "source assets")
    manifests = {}
    for name in ("g4", "g5"):
        relative = f"subsets/{name}.json"
        doc = load(relative)
        require(doc["protocolCommit"] == PROTOCOL and doc["sourceCommit"] == SOURCE and doc["corpusPairsSha256"] == PAIRS, f"{name} binding")
        require(sha(root / relative) == summary[name]["fileSha256"] == lock[f"{name}DocumentSha256"], f"{name} file digest")
        rows = doc["rows"]
        keys = [key(r) for r in rows]
        require(keys == sorted(set(keys)), f"{name} canonical order/uniqueness")
        require(len(rows) == summary[name]["rows"], f"{name} count")
        require(all(r["datasetRole"] in ("calibration", "evaluation") for r in rows), f"{name} role")
        for role, field in (("calibration", "calibrationSha256"), ("evaluation", "evaluationSha256")):
            digest = hashlib.sha256(canonical([r for r in rows if r["datasetRole"] == role])).hexdigest()
            require(digest == doc[field] == summary[name][field], f"{name} {role} fingerprint")
        manifests[name] = rows
    require([key(r) for r in manifests["g4"]] == [key(r) for r in manifests["g5"] if r["family"] in FAMILIES], "G4 frozen family coverage")
    require(sum(r["targetBytes"] for r in manifests["g5"]) == 857713581, "corpus target bytes")
    require(len(manifests["g5"]) == lock["rows"] == lock["provenance"]["sampleCount"] == 1893, "corpus rows")
    require(sum(r["puffinSupported"] for r in manifests["g5"]) == summary["g5"]["puffinSupportedRows"] == lock["supportedRows"] == 0, "G5 supported rows")
    for relative, digest in inputs["materializedInputSha256"].items():
        path = root / ("subsets/g4.json" if relative == "evidence-input/g4.json" else relative)
        if relative.startswith(("evidence-input/", "tool-provenance/")):
            require(sha(path) == digest, f"input digest: {relative}")
    require(sha(root / "evidence-input/g5-structural.json") == lock["structuralInputSha256"], "structural input")
    require(sha(root / "evidence-input/puffin-locator.json") == lock["puffinLocatorSha256"], "locator input")
    provenance_path = root / "tool-provenance/puffin-build-provenance.bin"
    require(provenance_path.read_bytes() == base64.b64decode(locator["buildProvenanceBase64"]), "durable provenance bytes")
    require(sha(provenance_path) == lineage["buildProvenanceSha256"] == locator["buildProvenanceSha256"], "provenance digest")
    provenance = json.loads(provenance_path.read_bytes())
    require(provenance["producerRunId"] == lineage["producerRunId"] and provenance["chunkShiftSourceCommit"] == SOURCE, "provenance producer")
    for field, filename in (("buildCommandSha256", "build-command.txt"), ("compilerVersionSha256", "compiler-version.txt"), ("hostEnvironmentSha256", "host-environment.txt"), ("lddSha256", "ldd-packaged.txt"), ("packageVersionsSha256", "package-versions.txt"), ("helpOutputSha256", "puffin-help.txt")):
        require(sha(root / "tool-provenance" / filename) == provenance[field], field)
    require(provenance["binarySha256"] == lineage["binarySha256"] == locator["executableSha256"] == tools["tools"][0]["executableSha256"], "binary digest")
    require(provenance["puffinCommit"] == lineage["puffinCommit"] == "343e23db1b4d81045e91a10244244893f5acd73b", "Puffin pin")
    artifact_document = load("artifacts.json")
    require(artifact_document["protocolCommit"] == PROTOCOL, "artifact protocol")
    artifacts = artifact_document["artifacts"]
    require(len(artifacts) == len(runs) == 2, "two-run artifact lineage")
    binary = next(f for f in artifacts[0]["containedFiles"] if f["path"] == "bin/puffin")
    require(binary["sha256"] == lineage["binarySha256"] and binary["bytes"] == locator["executableBytes"], "contained binary identity")
    require(provenance["sourceArchiveSha256"] == lineage["sourceArchiveSha256"] == locator["sourceArchiveSha256"] == tools["tools"][0]["sourceArchiveSha256"], "source archive identity")
    for artifact, run in zip(artifacts, runs):
        require(artifact["workflowRunId"] == run["id"] and artifact["retentionDays"] == 90, "artifact run/retention")
        files = {f["path"]: f for f in artifact["containedFiles"]}
        if run["id"] == lineage["producerRunId"]:
            retained = {name: "tool-provenance/" + name for name in (
                "build-command.txt", "compiler-version.txt", "host-environment.txt", "ldd-packaged.txt",
                "package-versions.txt", "puffin-help.txt", "puffin-source-identity.txt", "SHA256SUMS")}
            retained["build-provenance.json"] = "tool-provenance/puffin-build-provenance.bin"
            raw_only = {"adapter-source.cc", "ldd.txt", "bin/puffin", "lib64/libgflags.so.2.2",
                "lib64/libglog.so.1", "lib64/liblzma.so.5", "lib64/libunwind.so.8"}
        else:
            retained = {"evidence/" + name: name for name in (
                "inputs.json", "tools.json", "inventory-lock-run.json", "subsets/g4.json", "subsets/g5.json",
                "evidence-input/g5-structural.json", "evidence-input/puffin-locator.json",
                "tool-provenance/puffin-build-provenance.bin")}
            retained.update({"stage-a-summary.json": "stage-a-summary.json", "toolchain-producer.json": "toolchain-producer.json",
                "evidence/evidence-input/g4.json": "subsets/g4.json", "prepass/g4.json": "subsets/g4.json",
                "prepass/g5-structural.json": "evidence-input/g5-structural.json",
                "prepass/puffin-locator.json": "evidence-input/puffin-locator.json"})
            raw_only = {"stage-a-summary.md", "prepass/inventory-run.json"}
        require(set(files) == set(retained) | raw_only and len(files) == len(artifact["containedFiles"]), "artifact file coverage")
        for relative, retained_path in retained.items():
            path, expected = root / retained_path, files[relative]
            require(path.is_file(), f"missing retained snapshot: {retained_path}")
            require(sha(path) == expected["sha256"] and path.stat().st_size == expected["bytes"], f"retained raw file: {relative}")
        if raw_dir:
            archive = raw_dir / f"{artifact['artifactId']}.zip"
            require(sha(archive) == artifact["rawArtifactSha256"] and archive.stat().st_size == artifact["rawArtifactBytes"], "raw ZIP digest/size")
    require(artifacts[0]["artifactId"] == lineage["artifactId"] and "sha256:" + artifacts[0]["rawArtifactSha256"] == lineage["artifactDigest"], "producer artifact lineage")
    computed = totals(manifests["g4"], manifests["g5"])
    require(computed == load("aggregates.json"), "published inventory aggregates")
    print(json.dumps({"valid": True, "runId": summary["runId"], "aggregates": computed}, indent=2, sort_keys=True))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument("--raw-dir", type=Path, help="Optional downloaded ZIP directory; exact ZIP hashes are checked too.")
    args = parser.parse_args()
    verify(args.data_dir, args.raw_dir)
