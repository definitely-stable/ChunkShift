"""Actual child termination/restart probe. Never operate on a production directory."""
import argparse
import hashlib
import json
from pathlib import Path
import queue
import subprocess
import threading

LAB = Path(__file__).resolve().parent
ROOT = LAB.parents[1]
DLL = LAB / "bin/Release/net10.0/ChunkShift.Repository.CrashLab.dll"
MIB = 1024 * 1024
BOUNDARIES = ("BeforeCreate:1", "BeforeCreate:2", "AfterCloseBeforeReadback:1",
              "AfterVerified:1", "BeforeActiveDuplicate:1", "BeforeClosedDuplicate:1", "Ready:1")


def require(ok, msg):
    if not ok:
        raise ValueError(msg)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def snapshot(root):
    paths = sorted(root.iterdir()) if root.exists() else []
    require(len(paths) <= 64 and all(p.is_file() and not p.is_symlink()
                                   and p.name.endswith(".pack-private") for p in paths),
            "unexpected private fixture entry")
    require(sum(p.stat().st_size for p in paths) <= 12*MIB, "fixture extent cap")
    return {p.name: dict(bytes=p.stat().st_size, sha256=digest(p)) for p in paths}


def invoke(args, evidence, name):
    result = subprocess.run(["dotnet", str(DLL), *map(str, args)], cwd=ROOT,
                            capture_output=True, text=True, encoding="utf-8", timeout=120, check=False)
    (evidence / (name + ".stdout")).write_text(result.stdout, encoding="utf-8")
    (evidence / (name + ".stderr")).write_text(result.stderr, encoding="utf-8")
    require(result.returncode == 0, name + " failed")
    return json.loads(result.stdout)


def kill_at(suite, source, orphan, boundary, evidence, name):
    command = ["dotnet", str(DLL), "build", suite, str(source), str(orphan), boundary]
    with (evidence / (name + ".stderr")).open("w", encoding="utf-8") as stderr:
        with subprocess.Popen(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=stderr,
                              text=True, encoding="utf-8") as child:
            messages = queue.Queue()
            reader = threading.Thread(target=lambda: messages.put(child.stdout.readline()), daemon=True)
            reader.start()
            try:
                line = messages.get(timeout=90)
                (evidence / (name + ".stdout")).write_text(line, encoding="utf-8")
                marker = json.loads(line)
                require(marker["kind"] == "checkpoint" and marker["boundary"] == boundary and
                        marker["pid"] == child.pid and child.poll() is None, "checkpoint identity")
                if boundary != "Ready:1":
                    require(Path(marker["path"]).parent.resolve() == orphan.resolve(), "checkpoint path")
                else:
                    require(marker["path"] == "", "unexpected Ready path")
                child.kill()
                code = child.wait(timeout=30)
                require(code != 0, "child was not terminated")
                require(child.stdout.read() == "", "unexpected post-checkpoint output")
                return marker, code
            finally:
                if child.poll() is None:
                    child.kill()
                    child.wait(timeout=30)
                reader.join(timeout=5)


def check_condition(boundary, state, control):
    valid, invalid = len(state["valid"]), len(state["invalid"])
    require(state["kind"] == "inspection" and state["packs"] == valid and
            state["all_accepted"] == (invalid == 0), "inspection reconciliation")
    if boundary == "BeforeCreate:1":
        require(valid == invalid == 0, "unexpected pre-create files")
    elif boundary in ("BeforeCreate:2", "AfterCloseBeforeReadback:1", "AfterVerified:1"):
        require(valid == 1 and invalid == 0, "expected exactly one complete pack")
    elif boundary == "BeforeActiveDuplicate:1":
        require(valid == 0 and invalid == 1, "expected incomplete active pack")
    elif boundary == "BeforeClosedDuplicate:1":
        require(valid >= 1, "expected closed pack")
    elif boundary == "Ready:1":
        require(invalid == 0 and all(state[k] == control[k] for k in ("packs", "frames", "bytes")),
                "Ready physical geometry mismatch")
    else:
        raise ValueError("unknown boundary")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", required=True, type=Path)
    args = p.parse_args()
    root = args.output.resolve()
    require(ROOT != root and ROOT not in root.parents, "output inside checkout")
    require(not subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip(),
            "dirty checkout")
    root.mkdir(parents=True, exist_ok=False)
    evidence = root / "evidence"
    evidence.mkdir()
    source = root / "source.bin"
    with source.open("xb") as out:
        for index in range(8):
            out.write(bytes(MIB) if index < 2 or index >= 6 else hashlib.shake_256(
                f"chunkshift-crash-v1:{index}".encode("ascii")).digest(MIB))
    sha = digest(source)
    meta = dict(experiment="REPO-CRASH-001", phase="process-termination-only",
                commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                source_sha256=sha, source_bytes=8*MIB, boundaries=BOUNDARIES,
                sdk=subprocess.check_output(["dotnet", "--version"], text=True).strip())
    (evidence / "manifest.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    count = 0
    try:
        with (evidence / "build.log").open("w", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.CrashLab.csproj"),
                            "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        with (evidence / "cases.jsonl").open("x", encoding="utf-8") as rows:
            for suite in ("sha256", "blake3"):
                control_dir = root / (suite + "-control")
                control = invoke(["build", suite, source, control_dir, "none"], evidence, suite+"-control")
                require(control["kind"] == "complete" and control["restore_sha256"] == sha and
                        control["packs"] >= 2 and not snapshot(control_dir), "control failed")
                for n, boundary in enumerate(BOUNDARIES, 1):
                    case = f"{suite}-{n:02d}"
                    orphan = root / (case+"-orphan")
                    retry = root / (case+"-retry")
                    marker, exit_code = kill_at(suite, source, orphan, boundary, evidence, case+"-kill")
                    before = snapshot(orphan)
                    state = invoke(["inspect", *(orphan / name for name in before)], evidence, case+"-inspect")
                    require(sorted(state["valid"]+state["invalid"]) == sorted(before), "missing classification")
                    check_condition(boundary, state, control)
                    require(snapshot(orphan) == before, "inspection modified orphans")
                    fresh = invoke(["build", suite, source, retry, "none"], evidence, case+"-retry")
                    require(fresh == control and not snapshot(retry) and digest(source) == sha and
                            snapshot(orphan) == before, "restart diverged or modified orphans")
                    rows.write(json.dumps(dict(suite=suite, boundary=boundary, marker=marker,
                                               killed_exit=exit_code, snapshot=before, inspection=state,
                                               restart=fresh, outcome="pass"), sort_keys=True)+"\n")
                    rows.flush()
                    count += 1
        require(count == 14, "incomplete crash matrix")
        print("Process termination baseline: 14/14 PASS; no durability claim")
    finally:
        with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
