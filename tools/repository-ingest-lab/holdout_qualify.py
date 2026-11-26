"""External-source correctness qualification only; no performance evidence."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

from holdout_contract import check_definition, validate_external, qualification_record, EXPECTED_ARCH
from prepare_holdout import prepare, digest, require

LAB = Path(__file__).resolve().parent
ROOT = LAB.parents[1]
LOCK = LAB / "holdout-lock.json"
GEOMETRY = LAB / "holdout-geometry-lock.json"
PROJECT = LAB / "ChunkShift.Repository.IngestLab.csproj"
DLL = LAB / "bin/Release/net10.0/ChunkShift.Repository.IngestLab.dll"


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--lane", required=True, choices=EXPECTED_ARCH)
    args = p.parse_args()
    root = args.output.resolve()
    require(root != ROOT and ROOT not in root.parents, "output inside checkout")
    require(not git("status", "--porcelain"), "dirty checkout")
    require((platform.system() == "Windows") == (args.lane == "windows-2025"),
            "wrong host OS")
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    geometry = json.loads(GEOMETRY.read_text(encoding="utf-8"))
    check_definition(lock, geometry, digest(LOCK))
    root.mkdir(parents=True, exist_ok=False)
    raw = root / "evidence"
    raw.mkdir()
    private = root / "private"
    private.mkdir()
    sources = root / "sources"
    commit = git("rev-parse", "HEAD")
    manifest = dict(experiment=lock["experiment"], phase="qualification-only",
                    commit=commit, dirty=False, lane=args.lane,
                    github_run_id=os.getenv("GITHUB_RUN_ID"), github_run_attempt=os.getenv("GITHUB_RUN_ATTEMPT"),
                    started_utc=dt.datetime.now(dt.timezone.utc).isoformat(),
                    host=platform.platform(), machine=platform.machine(),
                    sdk=subprocess.check_output(["dotnet", "--version"], text=True).strip(),
                    runner_image=os.getenv("ImageOS"), runner_image_version=os.getenv("ImageVersion"),
                    source_lock_sha256=digest(LOCK), geometry_lock_sha256=digest(GEOMETRY),
                    fingerprints={name: digest(LAB / name) for name in
                                  ("Program.cs", "SourceRangeStream.cs", "prepare_holdout.py",
                                   "holdout_contract.py", "holdout_qualify.py", "validate.py")},
                    decision="CORRECTNESS_ONLY")
    (raw / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    try:
        prepare(sources, lock)
        (raw / "replay.json").write_bytes((sources / "holdout-preflight.json").read_bytes())
        build = raw / "build.log"
        try:
            with build.open("w", encoding="utf-8") as log:
                subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release"], cwd=ROOT,
                               stdout=log, stderr=subprocess.STDOUT, check=True)
        except subprocess.CalledProcessError:
            print(build.read_text(encoding="utf-8", errors="replace")[-16000:], file=sys.stderr)
            raise
        references = {}
        environment = set()
        rows = []
        with (raw / "samples.jsonl").open("x", encoding="utf-8") as records:
            for family in lock["design"]["families"]:
                spec = lock["corpora"][family]
                path = sources / spec["file"]
                require(path.stat().st_size == spec["bytes"] and digest(path) == spec["sha256"],
                        "source mismatch before candidate")
                for suite in lock["design"]["suites"]:
                    for candidate in lock["design"]["candidates"]:
                        index = len(rows) + 1
                        record = dict(sample_id=index, family=family, suite=suite, candidate=candidate,
                                      commit=commit, lane=args.lane)
                        try:
                            command = ["dotnet", str(DLL), candidate, suite,
                                       str(lock["design"]["target_mib"]), str(path), spec["sha256"], str(private)]
                            child = subprocess.run(command, cwd=ROOT, capture_output=True,
                                                   text=True, encoding="utf-8", timeout=600, check=False)
                            (raw / f"sample-{index:03d}.stderr").write_text(child.stderr, encoding="utf-8")
                            record["exit_code"] = child.returncode
                            require(child.returncode == 0, "candidate process failed")
                            result = json.loads(child.stdout)
                            signature = validate_external(result, lock, geometry, family, suite, candidate, args.lane)
                            require(references.setdefault((family, suite), signature) == signature,
                                    "D/C physical layout mismatch")
                            require(not any(private.iterdir()), "retained private files")
                            environment.add(tuple(result[k] for k in ("runtime", "os", "arch", "processors")))
                            record.update(qualification_record(result), outcome="pass")
                        except (OSError, ValueError, KeyError, TypeError, OverflowError, RuntimeError,
                                subprocess.TimeoutExpired) as error:
                            # Do not write child stdout or exception payloads containing timing data.
                            record.update(outcome="fail", error_type=type(error).__name__)
                            if isinstance(error, ValueError) and not isinstance(error, json.JSONDecodeError):
                                record["diagnostic"] = str(error)
                        records.write(json.dumps(record, sort_keys=True) + "\n")
                        records.flush()
                        if record["outcome"] != "pass":
                            raise RuntimeError(f"STOP: qualification sample {index} failed")
                        rows.append(record)
                require(digest(path) == spec["sha256"], "source changed during qualification")
        require(len(rows) == 16 and len(references) == 8 and len(environment) == 1,
                "incomplete/mixed qualification")
        private.rmdir()
        summary = dict(experiment=lock["experiment"], phase="qualification-only", commit=commit,
                       lane=args.lane, source_lock_sha256=digest(LOCK), geometry_lock_sha256=digest(GEOMETRY),
                       samples=len(rows), decision="CORRECTNESS_ONLY", environment=list(environment)[0],
                       layouts=[dict(family=f, suite=s, signature=list(sig))
                                for (f, s), sig in references.items()])
        (raw / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
        print("External-source qualification: 16/16 PASS; CORRECTNESS_ONLY")
    finally:
        with (raw / "sha256.txt").open("x", encoding="utf-8") as inventory:
            for path in sorted(raw.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    inventory.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
