#!/usr/bin/env python3
"""Read-only independent replay of frozen PATCH-EXEC-001 G2-A archives.

Requires both ORIGINAL GitHub Action ZIPs (not renormalized JSON). The committed
TSV samples and oracle physical CSP digests are independently checked. This is
a calibration closeout, never a 25% adaptive execution ADOPT decision.
"""
import argparse
import csv
import hashlib
import io
import json
import math
from pathlib import Path
import statistics
import zipfile

COMMIT = "36ba15fd434a5f5abe365c269839394433b4e44b"
RUN_ID = "38677655592"
CORPUS = "a7f1e607820f6a637600953ddb634b63cfd17e7edc55a67bae86f74f54b0c6e2"
ORIGINAL = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
ARTIFACT = {
    "warm-csm": "af04b7cef1438168f785ac27827816a526a80870ceced9c255688434e55c6276",
    "cold-csm": "7c5a4ab2288901aea028110fd5d8496d08ae77eb68b50754a40d52b23e00cc17",
}
CASES = {"f1-h0": ("h0", 1), "f1-h2w2": ("h2-w2", 1),
         "f2-h0": ("h0", 2), "f2-h2w2": ("h2-w2", 2),
         "f4-h0": ("h0", 4), "f4-h2w2": ("h2-w2", 4)}
METRICS = ("wall_seconds", "process_cpu_seconds", "managed_alloc_bytes",
           "process_lifetime_peak_ws_bytes")
STRUCTURAL = ("targetSize", "targetChunks", "patchBytes", "patchSha256",
              "tcsmBytes", "uniqueMissingBytes", "payloadEntries",
              "rawEntries", "zstdEntries", "dictionaryEntries")
HOST = ("osDescription", "osArchitecture", "processArchitecture",
        "frameworkDescription", "processorCount", "processorDescription",
        "gitCommit")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def rows_tsv(path):
    rows = list(csv.DictReader(Path(path).open(encoding="utf-8", newline=""), delimiter="\t"))
    return rows


def order(i):
    rows = list(CASES)
    shift = (i // 2) % len(rows)
    rows = rows[shift:] + rows[:shift]
    return rows[::-1] if i % 2 else rows


def physical_rows(rows):
    require(len(rows) == 16, "expected sixteen changed physical patch files")
    result = {}
    for row in rows:
        key = tuple(row.get(x) for x in ("family", "base", "target", "path"))
        require(all(isinstance(x, str) and x for x in key), "invalid path identity")
        require(key not in result, "duplicate file identity")
        result[key] = row
    return result


def verify_archive(path, mode, sample_map, identities):
    path = Path(path)
    require(sha(path.read_bytes()) == ARTIFACT[mode], mode + ": original ZIP digest mismatch")
    expected = {"SHA256SUMS", "summary.json", "selection.json",
                "pairs.json", "apply-verified-h0.json"}
    expected.update(f"r{i}-{name}.json" for i in range(5) for name in CASES)
    with zipfile.ZipFile(path) as z:
        require(z.testzip() is None, mode + ": invalid archive CRC")
        require(len(z.namelist()) == len(expected) and set(z.namelist()) == expected,
                mode + ": archive file population drift")
        body = {name: z.read(name) for name in expected}
    manifest = {}
    for line in body["SHA256SUMS"].decode("ascii").splitlines():
        digest, separator, name = line.partition("  ./")
        require(separator and len(digest) == 64 and name and name not in manifest,
                mode + ": invalid or repeated manifest entry")
        manifest[name] = digest
    require(set(manifest) == expected - {"SHA256SUMS"}, mode + ": manifest membership drift")
    for name, digest in manifest.items():
        require(sha(body[name]) == digest, mode + ": content SHA mismatch " + name)
    require(sha(body["pairs.json"]) == CORPUS, mode + ": selected pair digest changed")
    selection = json.loads(body["selection.json"])
    require(selection["role"] == "train" and selection["fileCount"] == 16 and
            selection["baseAndTargetFilesVerified"] == 32 and
            selection["frozenOriginalPairsSha256"] == ORIGINAL and
            selection["selectedPairsSha256"] == CORPUS,
            mode + ": wrong calibration population")
    p = json.loads(body["pairs.json"])
    require(len(p["pairs"]) == 2 and
            {pair["family"] for pair in p["pairs"]} ==
            {"dotnet-runtime-linux-arm64", "dotnet-aspnetcore-win-x64"} and
            all(pair["base"] == "10.0.10" and pair["target"] == "10.0.11" and
                len(pair["changed"]) == 8 for pair in p["pairs"]),
            mode + ": holdout or changed training population")
    oracle = json.loads(body["apply-verified-h0.json"])
    run = f"PATCH-EXEC-001/G2A/{mode}/{RUN_ID}/{COMMIT}"
    require(oracle["schema"] == "chunkshift.patch-lab.v1" and
            oracle["runId"] == run and oracle["workers"] == 1 and
            oracle["execution"] == "h0" and oracle["applyRepeats"] > 0 and
            oracle["corpusPairsSha256"] == CORPUS and
            oracle["environment"]["gitCommit"] == COMMIT and
            all(isinstance(row.get("applyMetrics"), dict) for row in oracle["files"]),
            mode + ": oracle is not a matching apply-verified H0")
    originals = physical_rows(oracle["files"])
    require({key: row["patchSha256"] for key, row in originals.items()} == identities,
            mode + ": committed oracle physical patch ID drift")
    summary = json.loads(body["summary.json"])
    require(summary["schema"] == "chunkshift.patch-exec-001-g2a-real-calibration.v1" and
            summary["status"] == "UNSEALED_REAL_FIXED_LANES_CALIBRATION_NO_ADOPT" and
            summary["gitCommit"] == COMMIT and
            str(summary["githubRunId"]) == RUN_ID and
            str(summary["githubRunAttempt"]) == "1" and
            summary["modality"] == mode and summary["rounds"] == 5 and
            summary["populationSha256"] == CORPUS and summary["population"] == selection,
            mode + ": untrusted summary provenance")
    require(len(summary["samples"]) == 5, mode + ": missing rounds")
    actual = {}
    for r in range(5):
        s = summary["samples"][r]
        require(s["round"] == r and s["order"] == order(r) and
                s["comparisonStatus"] == "SAME_BUILD_EQUAL_NOT_ADOPT" and
                set(s["cases"]) == set(CASES), mode + ": invalid round metadata")
        for name, (execution, workers) in CASES.items():
            data = json.loads(body[f"r{r}-{name}.json"])
            require(data["schema"] == "chunkshift.patch-batch-create.v1" and
                    data["runId"] == run and data["execution"] == execution and
                    data["workers"] == workers and data["applyRepeats"] == 0 and
                    data["corpusPairsSha256"] == CORPUS and
                    data["lane"] == oracle["lane"] and data["policy"] == oracle["policy"],
                    f"{mode}: r{r} {name} metadata differs")
            require(all(data["environment"].get(field) == oracle["environment"].get(field)
                        for field in HOST), mode + ": switched host/build")
            patch_rows = physical_rows(data["files"])
            require(set(patch_rows) == set(originals), mode + ": changed file identities")
            for key, patch_row in patch_rows.items():
                require(all(patch_row.get(field) == originals[key].get(field)
                            for field in STRUCTURAL), mode + ": CSP drift " + str(key))
                require(patch_row.get("createMetrics") is None,
                        mode + ": falsely attributed per-file process CPU")
            m = data["batchCreate"]
            v = (data["elapsedSeconds"], m["processCpuSeconds"],
                 m["processAllocatedBytes"], m["processPeakWorkingSetBytes"])
            require(all(isinstance(x, (int, float)) and not isinstance(x, bool)
                        and math.isfinite(x) and x > 0 for x in v),
                    mode + ": invalid process resource counter")
            expect = sample_map[(mode, str(r), name)]
            require(all(str(v[i]) == expect[METRICS[i]] for i in range(4)),
                    f"{mode}: r{r} {name} committed metrics not identical to original JSON")
            info = s["cases"][name]
            require(info["raw"] == f"r{r}-{name}.json" and
                    info["seconds"] == v[0] and info["cpuSeconds"] == v[1] and
                    info["allocatedBytes"] == v[2] and
                    info["processLifetimePeakWorkingSetBytes"] == v[3],
                    mode + ": raw data vs in-run summary mismatch")
            actual[(r, name)] = v
    best = min(CASES, key=lambda name: statistics.median(
        actual[(r, name)][0] for r in range(5)))
    require(summary["bestObservedFixedCase"] == best, mode + ": best fixed case altered")
    for name, (exe, workers) in CASES.items():
        samples = [actual[(r, name)] for r in range(5)]
        agg = summary["cases"][name]
        require(agg["execution"] == exe and agg["outerFiles"] == workers and
                agg["medianWallSeconds"] == statistics.median(v[0] for v in samples) and
                agg["medianCpuSeconds"] == statistics.median(v[1] for v in samples) and
                agg["maxProcessLifetimePeakWorkingSetBytes"] == max(v[3] for v in samples),
                mode + ": aggregate not independently reproducible")
    return {"mode": mode, "count": len(actual)*16, "best": best,
            "processor": oracle["environment"]["processorDescription"]}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--warm", required=True)
    p.add_argument("--cold", required=True)
    p.add_argument("--samples", required=True)
    p.add_argument("--identities", required=True)
    args = p.parse_args()
    tuples = rows_tsv(args.samples)
    require(len(tuples) == 60, "must preserve all 60 samples")
    samples = {(r["mode"], r["round"], r["allocation"]): r for r in tuples}
    require(len(samples) == 60, "duplicate samples")
    identity_rows = rows_tsv(args.identities)
    require(len(identity_rows) == 16, "must preserve all 16 CSP digests")
    identities = {(r["family"], r["base"], r["target"], r["path"]): r["patch_sha256"]
                  for r in identity_rows}
    require(len(identities) == 16, "duplicate identities")
    verified = [verify_archive(args.warm, "warm-csm", samples, identities),
                verify_archive(args.cold, "cold-csm", samples, identities)]
    require(verified[0]["processor"] != verified[1]["processor"],
            "cross-mode processor variance was not recorded")
    require(sum(v["count"] for v in verified) == 960, "incomplete CSP audit")
    print(json.dumps({"verdict": "CALIBRATION_PASS_NO_ADOPT", "lanes": verified},
                     indent=2))


if __name__ == "__main__":
    main()
