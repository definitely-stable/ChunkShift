#!/usr/bin/env python3
"""Build PATCH-APPLY-003-rule1-wall.jsonl from the raw `patch-lab apply-check time`
documents of the three platform artifacts of workflow run 36550787928.

The file keeps what rule 1 of PATCH-APPLY-003 consumes that the compact files do
not: for every platform, file and repetition, the two measured wall times of each
lane, and per repetition the output-identity bit. With it, recompute.py rebuilds
the paired repetition-block bootstrap of protocol section 2.1 without the
expiring artifacts. Standard library only.

Usage:
  build_rule1_wall.py LINUX_X64_DIR LINUX_ARM64_DIR WIN_X64_DIR --output FILE
"""

import argparse
import json
from pathlib import Path

SCHEMA = "chunkshift.patch-apply-003-rule1-wall.v1"
SOURCE_SCHEMA = "chunkshift.patch-lab-apply-check.v1"
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
LANES = ("off", "seq", "overlap")


def read_time_documents(directory: Path) -> dict[int, dict]:
    documents = {}
    for path in sorted(directory.rglob("*.json")):
        document = json.loads(path.read_text(encoding="utf-8"))
        if document.get("schema") == SOURCE_SCHEMA and document.get("kind") == "time":
            repetition = int(document["repetition"])
            if repetition in documents:
                raise SystemExit(f"{path}: repetition {repetition} appears twice")
            documents[repetition] = document
    if sorted(documents) != list(range(10)):
        raise SystemExit(f"{directory}: expected time repetitions 0..9, found {sorted(documents)}")
    return documents


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("directories", nargs=3, type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    header = {"schema": SCHEMA, "experiment": "PATCH-APPLY-003", "lanes": list(LANES), "platforms": {}}
    records = []
    for platform, directory in zip(PLATFORMS, args.directories):
        documents = read_time_documents(directory)
        commits = {document["environment"]["gitCommit"] for document in documents.values()}
        corpora = {document["corpusPairsSha256"] for document in documents.values()}
        run_ids = {document["runId"] for document in documents.values()}
        if len(commits) != 1 or len(corpora) != 1 or len(run_ids) != 1:
            raise SystemExit(f"{directory}: mixed commit, corpus or RunId")
        run_id = run_ids.pop()
        if not run_id.endswith(f"-{platform}"):
            raise SystemExit(f"{directory}: RunId {run_id} is not {platform}")
        header["gitCommit"] = commits.pop()
        header["corpusPairsSha256"] = corpora.pop()
        header["platforms"][platform] = {
            "runId": run_id,
            "outputsVerified": [documents[r]["outputsVerified"] for r in range(10)],
        }

        files: dict[tuple, dict] = {}
        for repetition in range(10):
            keys = set()
            for record in documents[repetition]["files"]:
                key = (record["family"], record["base"], record["target"], record["path"])
                keys.add(key)
                entry = files.setdefault(key, {"targetSize": record["targetSize"],
                                               "wallSeconds": {lane: [] for lane in LANES}})
                if entry["targetSize"] != record["targetSize"]:
                    raise SystemExit(f"{platform} {key}: target size differs between repetitions")
                for lane in LANES:
                    runs = record["runs"][lane]
                    if len(runs) != 2:
                        raise SystemExit(f"{platform} {key}: {len(runs)} runs of {lane}")
                    entry["wallSeconds"][lane].append([run["wallSeconds"] for run in runs])
            if keys != set(files):
                raise SystemExit(f"{platform}: repetition {repetition} has a different file set")

        for key in sorted(files):
            family, base, target, path = key
            records.append({"platform": platform, "family": family, "base": base, "target": target,
                            "path": path, "targetSize": files[key]["targetSize"],
                            "wallSeconds": files[key]["wallSeconds"]})

    lines = [json.dumps(header, sort_keys=True, separators=(",", ":"))]
    lines += [json.dumps(record, sort_keys=True, separators=(",", ":")) for record in records]
    args.output.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
