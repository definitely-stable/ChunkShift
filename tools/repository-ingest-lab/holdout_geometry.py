"""Untimed locked replay and geometry evidence; never invoke D/C candidates."""
import argparse
import json
from pathlib import Path
import subprocess

from prepare_holdout import prepare, digest, require

ROOT = Path(__file__).resolve().parents[2]
LAB = Path(__file__).resolve().parent
LOCK = LAB / "holdout-lock.json"
DLL = LAB / "bin/Release/net10.0/ChunkShift.Repository.IngestLab.dll"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    out = args.output.resolve()
    require(ROOT != out and ROOT not in out.parents, "output inside checkout")
    require(not subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip(),
            "dirty checkout")
    out.mkdir(parents=True, exist_ok=False)
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    source = out / "sources"
    evidence = out / "evidence"
    evidence.mkdir()
    rows = []
    try:
        prepare(source, lock)
        (evidence / "replay.json").write_bytes((source / "holdout-preflight.json").read_bytes())
        with (evidence / "build.log").open("w", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.IngestLab.csproj"),
                            "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        for family in lock["design"]["families"]:
            spec = lock["corpora"][family]
            path = source / spec["file"]
            require(path.stat().st_size == spec["bytes"] and digest(path) == spec["sha256"],
                    "source identity mismatch")
            for suite in lock["design"]["suites"]:
                name = f"{family}-{suite}"
                with (evidence / (name + ".stdout")).open("w", encoding="utf-8") as stdout, (
                        evidence / (name + ".stderr")).open("w", encoding="utf-8") as stderr:
                    subprocess.run(["dotnet", str(DLL), "--geometry", suite, str(path)],
                                   cwd=ROOT, stdout=stdout, stderr=stderr, check=True, timeout=600)
                row = json.loads((evidence / (name + ".stdout")).read_text(encoding="utf-8"))
                require(row["verdict"] == "geometry-only" and row["suite"] == suite and
                        row["source_sha256"] == spec["sha256"] and row["logical_bytes"] == spec["bytes"],
                        "geometry identity mismatch")
                n, u, b, k = (row[x] for x in ("logical_occurrences", "unique_frames",
                                               "unique_payload_bytes", "pack_count"))
                active, closed = row["active_duplicate_count"], row["closed_duplicate_count"]
                require(all(type(v) is int and v >= 0 for v in (n, u, b, k, active, closed)) and
                        0 < u <= n and 0 < b <= spec["bytes"] and 0 < k <= u and
                        n == u + active + closed and row["pack_bytes"] == b + 68*u + 168*k,
                        "geometry reconciliation")
                rows.append(dict(family=family, **row))
            require(digest(path) == spec["sha256"], "source changed")
        coverage = all(r["active_duplicate_count"] > 0 and r["closed_duplicate_count"] > 0
                       for r in rows if r["family"] == "adverse-repeat")
        result = dict(experiment=lock["experiment"], phase="geometry-only",
                      commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                      lock_sha256=digest(LOCK), coverage="PASS" if coverage else "DEFER",
                      rows=rows)
        (evidence / "geometry.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        require(len(rows) == 8, "incomplete geometry matrix")
        print(f"Geometry 8/8; adverse coverage: {result['coverage']}; no candidate timing")
        require(coverage, "adverse coverage gap; DEFER, do not launch holdout")
    finally:
        with (evidence / "sha256.txt").open("w", encoding="utf-8") as out_hash:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out_hash.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
