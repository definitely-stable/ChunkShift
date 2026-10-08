#!/usr/bin/env python3
"""Archive independently verified G4 calibration/evaluation evidence per protocol."""
import argparse
import base64
import gzip
import hashlib
import json
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

SOURCE = "c71f0c8fa483c45bf65fee4850d293c45f7878a6"
PROTOCOL = "5372678ae8451a71cc95eb24f30855cbbd7e0633"
RUNS = {"calibration": 37802323920, "evaluation": 37808520559}
H0 = {"calibration": 11860274, "evaluation": 26363364}
FILE_COUNTS = {"calibration": 1049, "evaluation": 844}
ROOT = Path("docs/research/results/data/PATCH-GAP-001-G4-EVIDENCE-20261008-001")
DOC = Path("docs/research/results/PATCH-GAP-001-G4-EVIDENCE-20261008-001.md")
INVENTORY = Path("docs/research/results/data/PATCH-GAP-001-20261003-001/subsets/g4.json")
HEADER = "datasetRole\tfamily\tbaseVersion\ttargetVersion\tpath\th0Bytes\tg4Bytes\tsavedBytes\teligible\twinners\n"


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def dump(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, sort_keys=True, indent=2) + "\n", encoding="utf-8")


def assert_true(value, detail):
    if not value:
        raise ValueError(detail)


def process(role, artifact_dir, metadata_dir):
    run = load(metadata_dir / (role + "-run.json"))
    items = load(metadata_dir / (role + "-artifacts.json"))["artifacts"]
    assert_true(run["id"] == RUNS[role] and run["event"] == "workflow_dispatch" and
                run["head_sha"] == SOURCE and run["head_branch"] == "main" and
                run["run_attempt"] == 1 and run["status"] == "completed" and
                run["conclusion"] == "success", "run identity failed: " + role)
    matching = [i for i in items if i["workflow_run"]["id"] == RUNS[role]
                and i["workflow_run"]["head_sha"] == SOURCE
                and i["name"].startswith("patch-gap-001-g4-" + role + "-")
                and i.get("digest", "").startswith("sha256:") and not i.get("expired")]
    assert_true(len(matching) == 1, "artifact identity failed: " + role)
    item = matching[0]
    doc = load(artifact_dir / "g4-index.json")
    assert_true(doc["schema"] == "chunkshift.patch-gap-g4-byte-study.v1" and
                doc["protocolCommit"] == PROTOCOL and
                doc["sourceCommit"] == SOURCE and doc["datasetRole"] == role and
                doc["provenance"]["sourceCommit"] == SOURCE and
                doc["provenance"]["dirty"] is False, "data identity failed: " + role)
    assert_true(doc["lane"]["h0Bytes"] == H0[role] and
                len(doc["files"]) == FILE_COUNTS[role], "frozen H0/count mismatch")
    assert_true(load(artifact_dir / "h0-anchor.json")["matchesFrozenAnchor"], "H0 anchor failed")
    subprocess.run([sys.executable, "benchmarks/scripts/verify_patch_gap_g4.py",
                    "--index", str(artifact_dir / "g4-index.json"),
                    "--detail-dir", str(artifact_dir / "detail"),
                    "--inventory", str(INVENTORY),
                    "--xz-provenance", str(artifact_dir / "xz-provenance.json"),
                    "--dataset-role", role], check=True)

    table = {}
    totals = defaultdict(lambda: {"files": 0, "eligible": 0, "h0": 0,
                                  "g4": 0, "saved": 0, "winners": 0})
    for row in doc["files"]:
        values = [role, row["family"], row["baseVersion"], row["targetVersion"], row["path"],
                  str(row["h0PatchBytes"]), str(row["factorPatchBytes"]),
                  str(row["savedBytes"]), str(int(row["gateEligible"])),
                  str(row["bcjWinnerEntries"])]
        assert_true(all(not any(c in x for c in "\r\n\t") for x in values),
                    "noncanonical TSV string")
        key = tuple(values[:5])
        assert_true(key not in table, "duplicate file key")
        table[key] = "\t".join(values) + "\n"
        family = totals[row["family"]]
        family["files"] += 1
        family["eligible"] += int(row["gateEligible"])
        family["h0"] += row["h0PatchBytes"]
        family["g4"] += row["factorPatchBytes"]
        family["saved"] += row["savedBytes"]
        family["winners"] += row["bcjWinnerEntries"]
    lane = doc["lane"]
    assert_true(sum(v["h0"] for v in totals.values()) == lane["h0Bytes"] and
                sum(v["g4"] for v in totals.values()) == lane["factorBytes"] and
                sum(v["winners"] for v in totals.values()) == lane["bcjWinnerEntries"],
                "aggregate divergence")
    records = sorted(table.items())
    info = {"runId": doc["provenance"]["runId"], "run": RUNS[role],
            "url": run["html_url"], "sourceCommit": SOURCE, "artifact": item["id"],
            "zipSha256": item["digest"].split(":", 1)[1],
            "expires": item["expires_at"]}
    return lane, dict(sorted(totals.items())), records, info


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--calibration", type=Path, required=True)
    parser.add_argument("--evaluation", type=Path, required=True)
    parser.add_argument("--metadata", type=Path, required=True)
    args = parser.parse_args()
    assert_true(not ROOT.exists(), "existing immutable result directory")

    splits = {}
    artifacts = {}
    rows = []
    manifest = []
    for role, where in (("calibration", args.calibration), ("evaluation", args.evaluation)):
        lane, families, files, artifact = process(role, where, args.metadata)
        splits[role] = {"lane": lane, "families": families}
        artifacts[role] = artifact
        rows.extend(files)
        for family in sorted(families):
            content = HEADER + "".join(line for key, line in files if key[1] == family)
            data = content.encode("utf-8")
            compressed = gzip.compress(data, compresslevel=9, mtime=0)
            relative = "files/" + role + "-" + family + ".tsv.gz.b64"
            path = ROOT / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(base64.b64encode(compressed).decode("ascii") + "\n",
                            encoding="ascii")
            manifest.append({"path": relative, "datasetRole": role, "family": family,
                             "rows": families[family]["files"], "tsvBytes": len(data),
                             "tsvSha256": sha(data), "gzipBytes": len(compressed),
                             "gzipSha256": sha(compressed)})
    assert_true(len(rows) == 1893, "frozen combined population mismatch")
    combined = (HEADER + "".join(line for _, line in sorted(rows))).encode("utf-8")
    result = splits["evaluation"]["lane"]
    gate = (20 * result["savedBytes"] >= 3 * result["h0Bytes"])
    assert_true(gate == result["meetsRfcSizeGate"], "frozen gate mismatch")
    status = "SIZE_GATE_PASS_NON_SIZE_PENDING" if gate else "REJECT"
    dump(ROOT / "summary.json", {"schema": "chunkshift.patch-gap-g4-evidence.v1",
                                  "status": status, "sourceCommit": SOURCE,
                                  "protocolCommit": PROTOCOL, "splits": splits})
    dump(ROOT / "artifacts.json", artifacts)
    dump(ROOT / "files-manifest.json", {
        "schema": "chunkshift.patch-gap-g4-files.v1",
        "canonicalHeader": HEADER.rstrip("\n"),
        "combined": {"rows": len(rows), "bytes": len(combined), "sha256": sha(combined)},
        "shards": manifest})
    md = ["# PATCH-GAP-001 G4 evaluation", "",
          "EvidenceId: PATCH-GAP-001/G4-EVIDENCE-20261008-001",
          "Status: " + status,
          "Owner: #183; release gate: #251.",
          "Exact source: " + SOURCE,
          "Frozen protocol: " + PROTOCOL,
          "Both runs: main branch, GitHub-hosted Linux x64, run_attempt=1.",
          "Independent per-file recomputation, exact reconstruction and H0 anchor PASS.",
          "", "| Split | H0 B | G4 B | Saved B | Reduction | 15% gate |",
          "| --- | ---: | ---: | ---: | ---: | --- |"]
    for role in ("calibration", "evaluation"):
        lane = splits[role]["lane"]
        md.append("| {} | {:,} | {:,} | {:,} | {:.6f}% | {} |".format(
            role, lane["h0Bytes"], lane["factorBytes"], lane["savedBytes"],
            lane["reductionVsCsp"] * 100, lane["meetsRfcSizeGate"]))
    md.extend(["", "## Artifact identity"])
    for role in ("calibration", "evaluation"):
        ref = artifacts[role]
        md.append("- {}: run {}, artifact {}, ZIP SHA256 {}, expiry {}".format(
            role, ref["run"], ref["artifact"], ref["zipSha256"], ref["expires"]))
    md.extend(["", "Invalid initial calibration #37791458995 remains INVALID_INFRA_CLI_CONTRACT.",
               "Its unverified numbers are excluded. No CSP v1 or public API changes.",
               "", "## Durable recomputation", "",
               "The summary, artifacts and canonical compressed per-file shards under",
               "the matching data directory are the durable record after GitHub",
               "Actions artifacts expire. The separate Python recompute script",
               "reconstructs the 1,893 rows and validates all totals and the size gate.",
               "A size-gate pass alone does not authorize a production change."])
    DOC.write_text("\n".join(md) + "\n", encoding="utf-8")
    print(json.dumps({"status": status, "evaluationSavedBytes": result["savedBytes"],
                      "rows": len(rows)}, sort_keys=True))


if __name__ == "__main__":
    main()
