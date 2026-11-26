"""Sealed real-artifact holdout driver. Explicit run only; no PR timing trigger."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

from holdout_evaluate import (definitions, identities, fingerprints, verify_replay,
                              validate_result, schedule, strict_json)
from holdout_qualify import ROOT, LAB, PROJECT, DLL, git
from holdout_contract import EXPECTED_ARCH
from prepare_holdout import prepare, digest, require


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--lane", choices=EXPECTED_ARCH, required=True)
    p.add_argument("--replica", type=int, choices=(1, 2), required=True)
    p.add_argument("--execute-sealed-holdout", action="store_true", required=True)
    args = p.parse_args()
    require(args.execute_sealed_holdout and os.getenv("GITHUB_ACTIONS") == "true",
            "explicit hosted execution required")
    run_id, attempt = os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"]
    require(run_id.isdecimal() and attempt.isdecimal(), "invalid hosted identity")
    require((platform.system() == "Windows") == (args.lane == "windows-2025"), "wrong host OS")
    require(not git("status", "--porcelain"), "dirty checkout")
    root = args.output.resolve()
    require(root != ROOT and ROOT not in root.parents, "output inside checkout")
    lock, geometry, qualified = definitions()
    root.mkdir(parents=True, exist_ok=False)
    raw, private, sources = root / "evidence", root / "private", root / "sources"
    raw.mkdir()
    private.mkdir()
    commit = git("rev-parse", "HEAD")
    identity = f"REPO-INGEST-003/RUN-{run_id}-{attempt}-{commit[:12]}-{args.lane}-{args.replica}"
    meta = dict(experiment=lock["experiment"], phase="sealed-holdout", commit=commit, dirty=False,
                run_id=identity, lane=args.lane, replica=args.replica, github_run_id=run_id,
                github_run_attempt=attempt, runner_name=os.environ["RUNNER_NAME"],
                runner_image=os.environ["ImageOS"], runner_image_version=os.environ["ImageVersion"],
                started_utc=dt.datetime.now(dt.timezone.utc).isoformat(), host=platform.platform(),
                machine=platform.machine(), sdk=subprocess.check_output(["dotnet", "--version"], text=True).strip(),
                dotnet_info=subprocess.check_output(["dotnet", "--info"], text=True).strip(),
                cache="uncontrolled; locked schedule; fresh child per sample", **identities(),
                fingerprints=fingerprints(qualified))
    (raw / "manifest.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    try:
        prepare(sources, lock)
        replay = (sources / "holdout-preflight.json").read_bytes()
        (raw / "replay.json").write_bytes(replay)
        verify_replay(strict_json(replay.decode("utf-8")), lock, qualified)
        build = raw / "build.log"
        try:
            with build.open("w", encoding="utf-8") as log:
                subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release"], cwd=ROOT,
                               stdout=log, stderr=subprocess.STDOUT, check=True)
        except subprocess.CalledProcessError:
            print(build.read_text(encoding="utf-8", errors="replace")[-16000:], file=sys.stderr)
            raise
        # Acquisition and build precede the timed processes; rehash each family
        # before and after all its samples, not inside the measured Ready phase.
        for spec in lock["corpora"].values():
            path = sources / spec["file"]
            require(path.stat().st_size == spec["bytes"] and digest(path) == spec["sha256"],
                    "source changed before samples")
        env = set()
        with (raw / "samples.jsonl").open("x", encoding="utf-8") as out:
            for n, (family, suite, round_no, position, candidate) in enumerate(schedule(lock), 1):
                spec = lock["corpora"][family]
                record = dict(sample_id=n, family=family, suite=suite, round=round_no, order=position,
                              candidate=candidate, warmup=round_no == 0, commit=commit, run_id=identity,
                              lane=args.lane, replica=args.replica)
                try:
                    stdout_path, stderr_path = raw / f"sample-{n:03d}.stdout", raw / f"sample-{n:03d}.stderr"
                    with stdout_path.open("w", encoding="utf-8") as stdout, stderr_path.open("w", encoding="utf-8") as stderr:
                        child = subprocess.run(["dotnet", str(DLL), candidate, suite, "32",
                                                str(sources / spec["file"]), spec["sha256"], str(private)],
                                               cwd=ROOT, stdout=stdout, stderr=stderr, timeout=600, check=False)
                    record["exit_code"] = child.returncode
                    require(child.returncode == 0, "candidate failed")
                    result = strict_json(stdout_path.read_text(encoding="utf-8"))
                    validate_result(result, lock, geometry, qualified, family, suite, candidate, args.lane)
                    require(not any(private.iterdir()), "retained private files")
                    env.add(tuple(result[k] for k in ("runtime", "os", "arch", "processors")))
                    record.update(result=result, outcome="pass")
                except (OSError, ValueError, KeyError, TypeError, OverflowError, RuntimeError,
                        subprocess.TimeoutExpired) as error:
                    record.update(outcome="fail", error=str(error))
                out.write(json.dumps(record, sort_keys=True, allow_nan=False) + "\n")
                out.flush()
                require(record["outcome"] == "pass", f"STOP: sample {n} failed")
        require(n == 112 and len(env) == 1, "incomplete/mixed lane")
        for spec in lock["corpora"].values():
            require(digest(sources / spec["file"]) == spec["sha256"], "source changed after samples")
        private.rmdir()
        require(not git("status", "--porcelain"), "checkout changed")
        completion = dict(commit=commit, run_id=identity, samples=112, measured=96,
                          source_sha256={f: spec["sha256"] for f, spec in lock["corpora"].items()},
                          private_empty=True, checkout_clean=True,
                          samples_sha256=digest(raw / "samples.jsonl"))
        with (raw / "completion.json").open("x", encoding="utf-8") as done:
            json.dump(completion, done, indent=2, allow_nan=False)
        print("Sealed holdout lane: 112/112 samples; independent aggregate audit required.")
    finally:
        with (raw / "sha256.txt").open("x", encoding="utf-8") as index:
            for path in sorted(raw.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    index.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
