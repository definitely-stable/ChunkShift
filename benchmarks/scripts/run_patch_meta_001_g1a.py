"""PATCH-META-001/G1A: true PatchLab create/apply stage calibration.

Small deterministic synthetic smoke. It NEVER produces a cache speedup verdict.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys

SCHEMA = "chunkshift.patch-meta-g1a-stage.v1"
SUMMARY_SCHEMA = "chunkshift.patch-meta-g1a-summary.v1"
STATUS = "SYNTHETIC_STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT"
PAIRS = (("v1", "v2"), ("v1", "v3"))
SIZE = 512 * 1024


def sha(data):
    return hashlib.sha256(data).hexdigest()


def file_sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def make_corpus(root: Path):
    """No downloads, no mutable external sources, no reuse of stale fixture files."""
    if root.exists():
        raise FileExistsError(f"corpus already exists: {root}")
    root.mkdir(parents=True)
    content = root / "tree" / "meta-g1a"
    base = hashlib.shake_256(b"chunkshift.PATCH-META-001.G1A.base.v1").digest(SIZE)
    v2 = bytearray(base)
    v2[SIZE // 3: SIZE // 3 + 32768] = hashlib.shake_256(
        b"chunkshift.PATCH-META-001.G1A.target.v2").digest(32768)
    v3 = bytearray(base)
    v3[SIZE // 2:SIZE // 2 + 16384] = (b"META-001:" * 2048)[:16384]
    versions = {"v1": base, "v2": bytes(v2), "v3": bytes(v3)}
    for version, data in versions.items():
        location = content / version / "artifact.bin"
        location.parent.mkdir(parents=True, exist_ok=True)
        location.write_bytes(data)
    pairs = {
        "schema": "chunkshift.patch-pairs.v1",
        "pairs": [
            {"family": "meta-g1a", "base": first, "target": second,
             "changed": [{
                 "path": "artifact.bin",
                 "baseSize": SIZE, "baseSha256": sha(versions[first]),
                 "targetSize": SIZE, "targetSha256": sha(versions[second])}]}
            for first, second in PAIRS],
    }
    (root / "pairs.json").write_text(
        json.dumps(pairs, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return file_sha(root / "pairs.json"), pairs


def validate_observation(raw, pair, sources, patch: Path):
    if type(raw) is not dict or raw.get("schema") != SCHEMA or raw.get("status") != \
            "STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT":
        raise ValueError("missing/incorrect stage schema or status")
    if (raw.get("lane"), raw.get("execution"), raw.get("family"),
            raw.get("baseVersion"), raw.get("targetVersion"), raw.get("path")) != (
            "csp", "h2-w2", "meta-g1a", pair[0], pair[1], "artifact.bin"):
        raise ValueError("stage input/policy mismatch")
    if raw.get("baseSourceSha256") != sources[pair[0]] or \
            raw.get("targetSourceSha256") != sources[pair[1]]:
        raise ValueError("stage source identity mismatch")
    if raw.get("patchSha256") != file_sha(patch) or \
            raw.get("outputPatchBytes") != patch.stat().st_size or \
            raw.get("patchPhysicalBytes") != patch.stat().st_size:
        raise ValueError("CSP output and attribution disagree")
    for name in (
            "totalWallSeconds", "builderWallSeconds",
            "baseDigestWallSeconds", "targetDigestWallSeconds",
            "baseManifestBuildWallSeconds", "targetManifestBuildWallSeconds",
            "postBuilderPatchShaWallSeconds", "builderCpuSeconds"):
        value = raw.get(name)
        if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
            raise ValueError(f"invalid timing: {name}")
    if raw["totalWallSeconds"] <= 0 or raw["builderWallSeconds"] <= 0:
        raise ValueError("missing measured create work")
    accounted = sum(raw[name] for name in (
        "builderWallSeconds", "baseDigestWallSeconds",
        "targetDigestWallSeconds", "baseManifestBuildWallSeconds",
        "targetManifestBuildWallSeconds", "postBuilderPatchShaWallSeconds"))
    if accounted > raw["totalWallSeconds"] * 1.05 + 0.002:
        raise ValueError("overlapping/inconsistent stage clocks")
    for name in ("baseContentReadBytes", "targetContentReadBytes",
                 "baseManifestReadBytes", "targetManifestReadBytes",
                 "builderAllocatedBytes", "outputPatchBytes"):
        if type(raw.get(name)) is not int or raw[name] < 0:
            raise ValueError(f"invalid byte counter: {name}")
    if not raw.get("baseManifestId") or not raw.get("targetManifestId"):
        raise ValueError("patch lacks manifest identities")
    for name in ("baseManifestCacheHit", "targetManifestCacheHit"):
        if type(raw.get(name)) is not bool:
            raise ValueError(f"missing cache-hit state: {name}")
    return raw


def command(dll, action, corpus, work, pair, patch, extra):
    return ["dotnet", str(dll), "patch-lab", "one", action,
            "--corpus", str(corpus), "--family", "meta-g1a",
            "--base", pair[0], "--target", pair[1], "--path", "artifact.bin",
            "--work", str(work), "--patch", str(patch), *extra]


def run_process(args):
    completed = subprocess.run(args, capture_output=True, text=True, check=True)
    if not any(line.startswith("peak=") for line in completed.stdout.splitlines()):
        raise ValueError("child missing lifetime process peak")
    return completed.stdout


def execute(args):
    if args.rounds not in range(2, 6):
        raise ValueError("smoke rounds must be 2..5")
    root, output, work = map(Path, (args.corpus, args.output, args.work))
    dll = Path(args.dll).resolve(strict=True)
    pairs_sha, pairs = make_corpus(root)
    output.mkdir(parents=True, exist_ok=False)
    sources = {v: file_sha(root / "tree" / "meta-g1a" / v / "artifact.bin")
               for v in ("v1", "v2", "v3")}
    rows, patch_by_pair, manifest_by_pair = [], {}, {}
    for iteration in range(args.rounds):
        for pair in (PAIRS if iteration % 2 == 0 else PAIRS[::-1]):
            label = f"r{iteration}-{pair[0]}-{pair[1]}"
            patch = output / f"{label}.csp"
            observed = output / f"{label}.json"
            applied = output / f"{label}.applied"
            create = command(dll, "create", root, work, pair, patch,
                             ["--lane", "csp", "--execution", "h2-w2",
                              "--meta-attribution", str(observed)])
            run_process(create)
            row = validate_observation(json.loads(observed.read_text()), pair, sources, patch)
            run_process(command(dll, "apply", root, work, pair, patch,
                                ["--output", str(applied), "--check", "seq"]))
            if file_sha(applied) != sources[pair[1]]:
                raise ValueError("independent target SHA-256 after exact CSP apply mismatch")
            if iteration == 0 and pair == PAIRS[0]:
                if row["baseManifestCacheHit"] or row["targetManifestCacheHit"]:
                    raise ValueError("initial source must be cold (dedicated work dir)")
            if iteration > 0 and (not row["baseManifestCacheHit"]
                                  or not row["targetManifestCacheHit"]):
                raise ValueError("warm manifest was unexpectedly recreated")
            key = pair
            if key in patch_by_pair and patch_by_pair[key] != row["patchSha256"]:
                raise ValueError("non-identical same-build CSP for same policy and sources")
            if key in manifest_by_pair and manifest_by_pair[key] != (
                    row["baseManifestId"], row["targetManifestId"]):
                raise ValueError("semantic identity changed across runs")
            patch_by_pair[key] = row["patchSha256"]
            manifest_by_pair[key] = (row["baseManifestId"], row["targetManifestId"])
            rows.append({"iteration": iteration, "pair": "-".join(pair),
                         "observation": observed.name,
                         "cspSha256": row["patchSha256"],
                         "applyTargetSha256": file_sha(applied),
                         "baseManifestHit": row["baseManifestCacheHit"],
                         "targetManifestHit": row["targetManifestCacheHit"],
                         "builderWallSeconds": row["builderWallSeconds"],
                         "totalWallSeconds": row["totalWallSeconds"]})
            patch.unlink()
            applied.unlink()

    warm = [json.loads((output / row["observation"]).read_text())
            for row in rows if row["iteration"] > 0]
    median = lambda key: statistics.median(x[key] for x in warm)
    summary = {
        "schema": SUMMARY_SCHEMA, "status": STATUS,
        "experimentId": "PATCH-META-001",
        "gitCommit": os.environ.get("HEAD_SHA", "LOCAL_UNVERIFIED"),
        "githubRunId": os.environ.get("GITHUB_RUN_ID"),
        "platform": platform.platform(), "python": sys.version,
        "pairsSha256": pairs_sha, "sourceSha256": sources,
        "rounds": args.rounds, "pairsPerRound": len(PAIRS),
        "workload": "actual CSP create + exact apply on deterministic synthetic 512KiB pairs",
        "medianWarmBuilderWallSeconds": median("builderWallSeconds"),
        "medianWarmDigestWallSeconds": statistics.median(
            x["baseDigestWallSeconds"] + x["targetDigestWallSeconds"] for x in warm),
        "medianWarmTotalWallSeconds": median("totalWallSeconds"),
        "warning": "small synthetic mechanics calibration; CPU/JIT/page-cache confounders; "
                   "manifest-digest cost is NOT automatically cacheable; "
                   "builder internals are NOT separately timed; NO_ADOPT",
        "samples": rows,
    }
    (output / "summary.json").write_text(
        json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({k: summary[k] for k in (
        "status", "medianWarmBuilderWallSeconds", "medianWarmDigestWallSeconds",
        "medianWarmTotalWallSeconds")}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True)
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--rounds", type=int, default=3)
    execute(parser.parse_args())


if __name__ == "__main__":
    main()
