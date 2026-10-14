#!/usr/bin/env python3
"""CORE011-CSM-004 E3-R1: source-pinned, real Core correctness only.

Every candidate is compiled in its own disposable detached Git worktree.
Neither this runner nor its injected tests modify tracked production source.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path("src/ChunkShift/Manifest/CsmReader.cs")
ORACLE = Path("benchmarks/experiments/core011-csm-e3r1-oracle.cs")
TEMP_TEST = Path("tests/ChunkShift.Tests/Manifest/CsmE3R1ReaderOwnershipTests.cs")
VARIANTS = ("eager", "lazy", "hybrid1", "hybrid2", "hybrid4")
BASE_SHA = "a62772638c715005cb8a1a6ca58222349755a825910cebf2"
BASE_BLOB = "ff56ccddaa0efc9970fd0b019e20523d6c2933d6"
PROTOCOL_ANCESTOR = "4f0423b32add1734dd244c6e3f94da6c1f221414"
FILTER = "FullyQualifiedName~Csm|FullyQualifiedName~ManifestReader|FullyQualifiedName~ManifestCancellation|FullyQualifiedName~VerificationSemantics"
RUN_PATTERN = re.compile(r"^CORE011-CSM-004/RUN-[0-9]{8}-[0-9]+-[0-9a-f]{8}-linux-x64$")


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    obj = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj


G1G = load_module(ROOT / "benchmarks/scripts/core011_csm_g1g_ab.py", "e3r1_g1g")
G1I = load_module(ROOT / "benchmarks/scripts/core011_csm_g1i_caps.py", "e3r1_g1i")
R0 = load_module(ROOT / "benchmarks/scripts/core011_csm_e3_guard.py", "e3r1_r0")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def variant_source(original, variant):
    if variant not in VARIANTS:
        raise ValueError("unregistered E3-R1 strategy")
    if sha(original.encode()) != BASE_SHA or R0.blob_sha1(original.encode()) != BASE_BLOB:
        raise ValueError("frozen CsmReader source drift")
    result = (G1G.variant_source(original, "lazy") if variant == "lazy"
              else G1I.source_variant(original, variant))
    if (variant == "eager") != (result == original):
        raise ValueError("duplicate baseline/candidate source")
    if variant.startswith("hybrid"):
        cap = int(variant[-1])
        if (result.count("maxArraysPerBucket: " + str(cap) + ");") != 1 or
                result.count("clearArray: true") != 2 or
                result.count("G1hLargePool.Instance.Rent(recordLength)") != 1):
            raise ValueError("bounded pool/clear/rent source invariant")
    if variant == "lazy" and result.count("new byte[recordLength]") != 1:
        raise ValueError("lazy allocation seam changed")
    return result


def logged(argv, cwd, log, env=None, timeout=900):
    with log.open("w", encoding="utf-8") as out:
        try:
            task = subprocess.run([str(x) for x in argv], cwd=cwd, env=env,
                                  stdout=out, stderr=subprocess.STDOUT, timeout=timeout)
        except subprocess.TimeoutExpired as e:
            raise ValueError("timed out, log=" + str(log)) from e
    if task.returncode:
        raise ValueError("command failed (" + str(task.returncode) + "): " +
                         " ".join(map(str, argv)) + "\n" +
                         "\n".join(log.read_text(errors="replace").splitlines()[-35:]))
    print("PASS " + " ".join(map(str, argv)), flush=True)


def count_trx(file):
    if not file.is_file():
        raise ValueError("missing original .trx: " + str(file))
    root = ET.parse(file).getroot()
    nodes = [e for e in root.iter() if e.tag.rsplit("}", 1)[-1] == "Counters"]
    if len(nodes) != 1:
        raise ValueError("invalid TRX result counters")
    counters = nodes[0].attrib
    total, passed, failed = (int(counters[x]) for x in ("total", "passed", "failed"))
    cases = [e.attrib.get("testName", "") for e in root.iter()
             if e.tag.rsplit("}", 1)[-1] == "UnitTestResult"]
    own = sum("CsmE3R1ReaderOwnershipTests" in x for x in cases)
    if total < 50 or passed != total or failed != 0 or own < 19:
        raise ValueError("incomplete CSM or E3 ownership oracle: " + str((total,passed,failed,own)))
    return {"total": total, "passed": passed, "failed": failed,
            "ownershipOracles": own, "trxSha256": sha(file.read_bytes())}


def preflight(repo, run_id):
    if not RUN_PATTERN.fullmatch(run_id):
        raise ValueError("invalid experiment RunId")
    contract = json.loads((repo/"benchmarks/experiments/core011-csm-e3-reader-contract.v1.json").read_text())
    R0.validate_contract(contract)
    R0.check_historical_contract(repo)
    src = (repo / SOURCE).read_bytes()
    R0.validate_source_pin(contract, src)
    if sha(src) != BASE_SHA or contract["candidates"] != list(VARIANTS):
        raise ValueError("reader baseline or candidate policy changed")
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    subprocess.run(["git", "merge-base", "--is-ancestor", PROTOCOL_ANCESTOR, head],
                   cwd=repo, check=True, timeout=30)
    if (repo / TEMP_TEST).exists():
        raise ValueError("injected tests accidentally committed")
    return head, src


def one_variant(repo, worktrees, results, name, head, original, oracle):
    lane = results / name
    lane.mkdir()
    tree = worktrees / ("e3r1-" + name)
    try:
        logged(["git", "worktree", "add", "--detach", tree, head],
               repo, lane / "worktree.log", timeout=120)
        if (tree / SOURCE).read_bytes() != original:
            raise ValueError("detached worktree source differs from baseline")
        changed = variant_source(original.decode("utf-8"), name)
        if name != "eager":
            (tree / SOURCE).write_text(changed, encoding="utf-8", newline="\n")
        (tree / TEMP_TEST).write_bytes(oracle)
        if (tree / TEMP_TEST).read_bytes() != oracle:
            raise ValueError("experiment oracle changed during injection")
        logged(["dotnet", "restore", "tests/ChunkShift.Tests/ChunkShift.Tests.csproj"],
               tree, lane / "restore.log", timeout=600)
        env = dict(os.environ, CHUNKSHIFT_E3_R1_VARIANT=name)
        frameworks = {}
        for framework in ("net8.0", "net10.0"):
            result_name = framework + ".trx"
            logged([
                "dotnet", "test", "tests/ChunkShift.Tests/ChunkShift.Tests.csproj",
                "-c", "Release", "-f", framework, "--no-restore", "--filter", FILTER,
                "--logger", "trx;LogFileName=" + result_name,
                "--results-directory", lane,
            ], tree, lane / (framework + ".log"), env=env, timeout=900)
            frameworks[framework] = count_trx(lane / result_name)
        dirty = subprocess.check_output(["git", "diff", "--name-only"], cwd=tree,
                                         text=True).splitlines()
        expected = [] if name == "eager" else [SOURCE.as_posix()]
        if dirty != expected:
            raise ValueError("unexpected dirty tracked source: " + repr(dirty))
        check_head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=tree, text=True).strip()
        if check_head != head:
            raise ValueError("checkout drift during candidate build")
        receipt = {
            "variant": name, "checkoutSha": head,
            "readerBaselineSha256": sha(original), "readerVariantSha256": sha(changed.encode()),
            "oracleSha256": sha(oracle), "worktree": "disposable detached, uncommitted",
            "poolCap": int(name[-1]) if name.startswith("hybrid") else None,
            "frameworks": frameworks, "productVerdict": "NOT_QUALIFIED",
        }
        (lane / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
        return receipt
    finally:
        if tree.exists():
            subprocess.run(["git", "worktree", "remove", "--force", tree],
                           cwd=repo, check=True, timeout=120)


def execute(repo, out, root, run_id):
    head, baseline = preflight(repo, run_id)
    out.mkdir(parents=True, exist_ok=False)
    root.mkdir(parents=True, exist_ok=False)
    oracle = (repo / ORACLE).read_bytes()
    lanes = []
    for variant in VARIANTS:
        lanes.append(one_variant(repo, root, out, variant, head, baseline, oracle))
    if len(set(x["readerVariantSha256"] for x in lanes)) != 5:
        raise ValueError("five distinct compiled reader implementations required")
    record = {
        "schema": "chunkshift.core011.e3r1-correctness.v1",
        "experimentId": "CORE011-CSM-004", "runId": run_id, "checkoutSha": head,
        "variants": list(VARIANTS), "lanes": lanes,
        "noCommittedCoreChanges": True, "decisionRunsAllowed": False,
        "productVerdict": "NOT_QUALIFIED",
        "limitations": [
            "No performance or memory ADOPT/REJECT evidence",
            "Synthetic CSM boundary/corruption oracles, not real sealed holdout",
            "Historical CORE011-CSM-004 Core 0.1.1 DEFER unchanged",
        ],
    }
    (out / "summary.json").write_text(json.dumps(record, indent=2) + "\n")
    return record


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--worktree-root", required=True, type=Path)
    parser.add_argument("--correctness-only", action="store_true")
    args = parser.parse_args()
    if not args.correctness_only:
        parser.error("E3-R1 must be correctness-only; no decision-run entrypoint")
    try:
        result = execute(Path.cwd(), args.output_dir.resolve(),
                         args.worktree_root.resolve(), args.run_id)
        print(json.dumps({"runId": result["runId"], "variants": len(result["lanes"]),
                          "productVerdict": "NOT_QUALIFIED",
                          "passed": sum(f["passed"] for e in result["lanes"]
                                        for f in e["frameworks"].values())}))
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        print("E3-R1 FAIL-CLOSED: " + str(exc), file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
