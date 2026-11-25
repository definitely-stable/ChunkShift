"""R1-B4e.1 screening driver; standard library, isolated sample processes."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import re
import sys

from validate import validate

MIB = 1024 * 1024
ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "tools" / "repository-ingest-lab" / "ChunkShift.Repository.IngestLab.csproj"
DLL = PROJECT.parent / "bin" / "Release" / "net10.0" / "ChunkShift.Repository.IngestLab.dll"


def run(command):
    return subprocess.check_output(command, cwd=ROOT, text=True).strip()


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def corpus(path, family, mib, seed):
    # Deterministic independent 1-MiB pseudorandom blocks, no runtime RNG dependency.
    with path.open("xb") as out:
        for index in range(mib):
            repeated = (family == "recent" and index < mib // 2 or
                        family == "cross" and (index < mib // 4 or index >= 3 * mib // 4))
            block = bytes(MIB) if repeated else hashlib.shake_256(
                f"chunkshift-ingest-v1:{seed}:{index}".encode("ascii")).digest(MIB)
            out.write(block)
    return digest(path)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True, help="New directory outside repository")
    p.add_argument("--mib", type=int, default=128)
    p.add_argument("--target-mib", type=int, default=32)
    p.add_argument("--rounds", type=int, default=5)
    p.add_argument("--seed", type=int, default=144)
    p.add_argument("--run-id", required=True, help="Assigned REPO-INGEST-001/RUN-YYYYMMDD-NNN-shortcommit-platform")
    p.add_argument("--timeout", type=int, default=600)
    args = p.parse_args()
    if not 4 <= args.mib <= 256 or args.mib % 4 or not 1 <= args.target_mib <= 128:
        p.error("mib must be divisible by 4 in [4,256]; target-mib in [1,128]")
    if not 1 <= args.rounds <= 30 or not 1 <= args.timeout <= 1800:
        p.error("rounds/timeout out of bounds")
    output = args.output.resolve()
    if output == ROOT or ROOT in output.parents:
        p.error("output must be outside repository")
    if run(["git", "status", "--porcelain"]):
        raise SystemExit("dirty source tree; refusing evidence run")
    commit = run(["git", "rev-parse", "HEAD"])
    if not re.fullmatch(r"REPO-INGEST-001/RUN-\d{8}-\d{3,}-[0-9a-f]{7,40}-[a-z0-9-]+", args.run_id):
        p.error("invalid evidence registry RunId")
    # The regex above validates structure; bind the embedded commit explicitly.
    short = re.search(r"/RUN-\d{8}-\d{3,}-([0-9a-f]{7,40})-", args.run_id).group(1)
    if not commit.startswith(short):
        p.error("RunId commit does not match HEAD")
    output.mkdir(parents=True, exist_ok=False)
    meta = dict(experiment="REPO-INGEST-001", run_id=args.run_id, commit=commit, dirty=False,
                started_utc=dt.datetime.now(dt.timezone.utc).isoformat(),
                github_run_id=os.getenv("GITHUB_RUN_ID"), github_run_attempt=os.getenv("GITHUB_RUN_ATTEMPT"),
                runner_image=os.getenv("ImageOS"), runner_image_version=os.getenv("ImageVersion"),
                host=platform.platform(), machine=platform.machine(),
                sdk=run(["dotnet", "--version"]), dotnet_info=run(["dotnet", "--info"]),
                protocol_sha256=digest(Path(__file__)),
                validator_sha256=digest(Path(__file__).with_name("validate.py")),
                executable_source_sha256=digest(PROJECT.parent / "Program.cs"),
                definition_sha256=digest(ROOT / "docs/architecture/REPOSITORY-R1B4E-INGEST-EVIDENCE-PROTOCOL-20261125.md"),
                command=sys.argv,
                private_storage_path=str(output / "private"),
                mib=args.mib, target_mib=args.target_mib, rounds=args.rounds, seed=args.seed,
                cache="uncontrolled; common post-ready oracle and paired order", isolation="one child per sample")
    (output / "manifest.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    try:
        with (output / "build.log").open("w", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release"], cwd=ROOT,
                           stdout=log, stderr=subprocess.STDOUT, check=True)
        private = output / "private"
        private.mkdir()
        sample_id = 0
        with (output / "samples.jsonl").open("x", encoding="utf-8") as raw:
            for family in ("unique", "recent", "cross"):
                source = output / (family + ".bin")
                source_hash = corpus(source, family, args.mib, args.seed)
                for suite in ("sha256", "blake3"):
                    order = "ABC" if family == "unique" else "AC"
                    reference = None
                    for round_no in range(args.rounds + 1):
                        shift = round_no % len(order)
                        for position, candidate in enumerate(order[shift:] + order[:shift]):
                            sample_id += 1
                            record = dict(sample_id=sample_id, family=family, suite=suite,
                                          candidate=candidate, corpus_sha256=source_hash,
                                          round=round_no, warmup=round_no == 0, order=position,
                                          run_id=args.run_id, commit=commit)
                            command = ["dotnet", str(DLL), candidate, suite, str(args.target_mib),
                                       str(source), source_hash, str(private)]
                            stdout_path = output / f"sample-{sample_id:03d}.stdout"
                            stderr_path = output / f"sample-{sample_id:03d}.stderr"
                            try:
                                with stdout_path.open("w", encoding="utf-8") as stdout, stderr_path.open(
                                        "w", encoding="utf-8") as stderr:
                                    child = subprocess.run(command, cwd=ROOT, stdout=stdout, stderr=stderr,
                                                           timeout=args.timeout, check=False)
                                record["exit_code"] = child.returncode
                                if child.returncode != 0:
                                    raise RuntimeError("sample process failed")
                                result = json.loads(stdout_path.read_text(encoding="utf-8"))
                                signature = validate(result, candidate, suite, family, source_hash,
                                                     args.mib * MIB, args.target_mib)
                                if reference is None:
                                    reference = signature
                                if reference != signature:
                                    raise ValueError("matched workload physical/logical shape mismatch")
                                if any(private.iterdir()):
                                    raise ValueError("sample private directory not empty")
                                record.update(result)
                                record["outcome"] = "pass"
                            except (OSError, ValueError, KeyError, TypeError, OverflowError, RuntimeError, subprocess.TimeoutExpired) as error:
                                record.update(outcome="fail", error=str(error))
                            raw.write(json.dumps(record, sort_keys=True) + "\n")
                            raw.flush()
                            if record["outcome"] != "pass":
                                raise SystemExit(f"STOP: sample {sample_id} failed; preserve raw logs")
                if digest(source) != source_hash:
                    raise SystemExit("STOP: source changed")
                source.unlink()
        private.rmdir()
    finally:
        # Preserve diagnostic inventory even on build/sample failure; large source
        # files are retained on STOP and their hashes are recorded too.
        with (output / "sha256.txt").open("x", encoding="utf-8") as index:
            for file in sorted(output.iterdir()):
                if file.is_file() and file.name != "sha256.txt":
                    index.write(f"{digest(file)}  {file.name}\n")
    print(f"Screen complete: {sample_id} samples (including warmups); no adoption decision.")


if __name__ == "__main__":
    main()
