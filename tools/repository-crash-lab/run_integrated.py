"""Kill only a dedicated child in fresh private fixtures; never touch a repository."""
import argparse
import json
from pathlib import Path
import queue
import subprocess
import threading

from integrated_crash_oracle import (BOUNDARIES, NAME, PACK_BYTES, check_candidate,
                                     check_inspection, check_marker, fixture_witness, require, sha)
from run import ROOT, LAB, DLL, digest, invoke


def snapshot(parent):
    paths = list(parent.iterdir())
    require(len(paths) <= 1 and all(p.name == NAME and p.is_file() and not p.is_symlink()
                                   for p in paths), "unexpected private entry")
    if not paths:
        return None
    require(paths[0].stat().st_size <= PACK_BYTES, "candidate exceeds fixed extent")
    return paths[0].read_bytes()


def kill_at(source, parent, boundary, evidence, name, expected_sha):
    command = ["dotnet", str(DLL), "integrated-build", str(source), str(parent), boundary]
    with (evidence / (name + ".stderr")).open("x", encoding="utf-8") as stderr:
        with subprocess.Popen(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=stderr,
                              text=True, encoding="utf-8") as child:
            messages = queue.Queue()
            reader = threading.Thread(target=lambda: messages.put(child.stdout.readline()), daemon=True)
            reader.start()
            try:
                line = messages.get(timeout=90)
                (evidence / (name + ".stdout")).write_text(line, encoding="utf-8")
                marker = json.loads(line)
                require(marker["pid"] == child.pid and child.poll() is None and
                        Path(marker["path"]).resolve() == (parent / NAME).resolve() and
                        marker["pack_sha256"] == expected_sha, "checkpoint PID/path/descriptor mismatch")
                check_marker(boundary, marker)
                child.kill()
                code = child.wait(timeout=30)
                require(code != 0 and child.stdout.read() == "", "child did not terminate at checkpoint")
                return marker, code
            finally:
                if child.poll() is None:
                    child.kill()
                    child.wait(timeout=30)
                reader.join(timeout=5)


def fresh_control(source, parent, expected, evidence, name):
    parent.mkdir()
    result = invoke(["integrated-build", source, parent, "none"], evidence, name)
    require(result == dict(kind="complete", state="ExperimentalPackPrepared", sha256=sha(expected),
                           writer_closed=True, reader_closed=True, parent_closed=True),
            "fresh control lifecycle mismatch")
    require(snapshot(parent) == expected, "fresh control bytes mismatch")
    inspected = invoke(["integrated-inspect", source, parent / NAME], evidence, name + "-inspect")
    check_inspection(inspected, expected, expected)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = args.output.resolve()
    require(ROOT != root and ROOT not in root.parents, "output inside checkout")
    require(not subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip(),
            "dirty checkout")
    root.mkdir(parents=True, exist_ok=False)
    evidence = root / "evidence"
    evidence.mkdir()
    manifest = dict(experiment="R1-C.2b.3-integrated", qualification="process-termination-only",
                    commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                    sdk=subprocess.check_output(["dotnet", "--version"], text=True).strip(),
                    boundaries=BOUNDARIES, suites=["sha256", "blake3"])
    (evidence / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    count = 0
    try:
        with (evidence / "build.log").open("x", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", str(LAB / "ChunkShift.Repository.CrashLab.csproj"),
                            "-c", "Release"], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        with (evidence / "cases.jsonl").open("x", encoding="utf-8") as rows:
            for suite in ("sha256", "blake3"):
                source = evidence / (suite + "-source.pack")
                fixture = invoke(["integrated-fixture", suite, source], evidence, suite + "-fixture")
                require(source.stat().st_size == PACK_BYTES, "fixture extent")
                expected = source.read_bytes()
                restored = fixture_witness(expected, fixture, suite)
                fresh_control(source, root / (suite + "-control"), expected, evidence, suite + "-control")
                for index, boundary in enumerate(BOUNDARIES, 1):
                    case = f"{suite}-{index:02d}"
                    orphan = root / (case + "-orphan")
                    orphan.mkdir()
                    marker, exit_code = kill_at(source, orphan, boundary, evidence, case + "-kill", sha(expected))
                    before = snapshot(orphan)
                    classification = check_candidate(boundary, before, expected)
                    inspection = None
                    if before is not None:
                        # Retain original observed bytes, not a reconstruction.
                        (evidence / (case + "-orphan.pack")).write_bytes(before)
                        inspection = invoke(["integrated-inspect", source, orphan / NAME],
                                            evidence, case + "-inspect")
                        check_inspection(inspection, before, expected)
                    require(snapshot(orphan) == before, "inspector modified orphan")
                    fresh_control(source, root / (case + "-fresh"), expected, evidence, case + "-fresh")
                    require(snapshot(orphan) == before and source.read_bytes() == expected,
                            "fresh attempt modified orphan or pinned source")
                    rows.write(json.dumps(dict(suite=suite, boundary=boundary, marker=marker,
                                               killed_exit=exit_code, classification=classification,
                                               bytes=None if before is None else len(before),
                                               sha256=None if before is None else sha(before),
                                               inspection=inspection, fresh="pass",
                                               restored_sha256=restored if classification == "exact" else None,
                                               outcome="pass"), sort_keys=True) + "\n")
                    rows.flush()
                    count += 1
        require(count == 28, "incomplete crash matrix")
        print("Integrated process termination: 28/28 PASS; no durability or adoption claim")
    finally:
        with (evidence / "sha256.txt").open("x", encoding="utf-8") as out:
            for path in sorted(evidence.iterdir()):
                if path.is_file() and path.name != "sha256.txt":
                    out.write(f"{digest(path)}  {path.name}\n")


if __name__ == "__main__":
    main()
