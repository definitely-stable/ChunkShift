#!/usr/bin/env python3
"""Summarize and decide PATCH-ENC-004 create-throughput runs.

Implements the frozen decision rule of docs/benchmarks/PATCH-ENC-004-PROTOCOL.md
(sections 3.3, 5, 6 and 7); no threshold is configurable.

  summarize_create_throughput.py print <run.json> [...]
      Per throughput document (chunkshift.patch-lab.v1): totals of create
      seconds, CPU, effective cores, base reads, seeks, allocations, peaks and
      patchesSha256. The workflow prints this into the job log.

  summarize_create_throughput.py decide <dir-or-file> [...]
      --corpus-lock <path> --output <verdict.json> --markdown <summary.md>
      [--require-all-platforms] [--require-memory]
      With both flags the run is decision-grade (protocol section 5): every
      platform and every memory lane must be present. Without them the output
      is labelled exploratory.
      Collects every throughput and memory (chunkshift.patch-lab-memory.v1)
      document, validates the run, computes the metrics and writes the verdict
      and a Markdown summary (also printed). Exit code: 0 valid run, 1 invalid
      input, 2 usage error.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from print_patch_lab_encoder import patches_digest

THROUGHPUT_SCHEMA = "chunkshift.patch-lab.v1"
MEMORY_SCHEMA = "chunkshift.patch-lab-memory.v1"
VERDICT_SCHEMA = "chunkshift.patch-enc-004-verdict.v1"
RUN_PREFIX = "PATCH-ENC-004/"
PLATFORMS = ("linux-x64", "linux-arm64", "win-x64")
EXECUTIONS = (
    "h0", "h1",
    "h2-w1", "h2-w2", "h2-w4", "h2-w8",
    "h3-w1", "h3-w2", "h3-w4", "h3-w8",
)
WORKER_COUNTS = (1, 2, 4, 8)
MIB = 1024 * 1024
MEMORY_MIN_BYTES = MIB
H0_SPREAD_LIMIT = 0.25
READ_RATIO_MIN = 2.0
SPEEDUP_MIN = 1.5
PLATFORMS_WITH_SPEEDUP = 2
D17_BOUND_MIB = 64
WORKER_BOUND_MIB = 32
METRIC_KEYS = (
    "cpuSeconds", "allocatedBytes", "baseReads", "baseBytesRead", "baseSeeks",
    "cacheLoads", "cacheHits", "cachePeakRecords", "cachePeakBytes",
    "windowPeakEntries", "windowPeakBytes", "reorderPeakEntries", "reorderPeakBytes",
)


def platform_of(document: dict, path: Path | None = None) -> str | None:
    run_id = str(document.get("runId") or "")
    for platform in PLATFORMS:
        if run_id.endswith("-" + platform):
            return platform
    if path is not None:
        stem = path.stem
        for platform in PLATFORMS:
            if stem.endswith("-" + platform):
                return platform
    return None


def workers_of(execution: str) -> int:
    return int(execution.rsplit("-w", 1)[1]) if "-w" in execution else 1


def bound_mib(execution: str) -> int:
    return D17_BOUND_MIB + (workers_of(execution) - 1) * WORKER_BOUND_MIB


def file_key(item: dict) -> str:
    return f"{item['family']} {item['base']}->{item['target']} {item['path']}"


def totals(document: dict) -> dict:
    files = document["files"]
    metrics = [item["createMetrics"] for item in files]
    seconds = sum(item["createSeconds"] for item in files)
    cpu = sum(m["cpuSeconds"] for m in metrics)
    return {
        "files": len(files),
        "createSeconds": seconds,
        "cpuSeconds": cpu,
        "effectiveCores": cpu / seconds if seconds > 0 else 0.0,
        "baseBytesRead": sum(m["baseBytesRead"] for m in metrics),
        "baseSeeks": sum(m["baseSeeks"] for m in metrics),
        "allocatedBytes": sum(m["allocatedBytes"] for m in metrics),
        "reorderPeakEntries": max((m["reorderPeakEntries"] for m in metrics), default=0),
        "reorderPeakBytes": max((m["reorderPeakBytes"] for m in metrics), default=0),
        "cachePeakRecords": max((m["cachePeakRecords"] for m in metrics), default=0),
        "cachePeakBytes": max((m["cachePeakBytes"] for m in metrics), default=0),
        "targetBytes": sum(item["targetSize"] for item in files),
        "uniqueMissingBytes": sum(item["uniqueMissingBytes"] for item in files),
    }


def print_command(paths: list[str]) -> int:
    for name in paths:
        path = Path(name)
        document = json.loads(path.read_text(encoding="utf-8"))
        if document.get("schema") != THROUGHPUT_SCHEMA:
            raise ValueError(f"not a {THROUGHPUT_SCHEMA} document: {document.get('schema')!r}")
        t = totals(document)
        print(f"== {name}")
        print(f"runId={document.get('runId')} execution={document.get('execution')} "
              f"platform={platform_of(document, path)}")
        print(f"files={t['files']} createSeconds={t['createSeconds']:.2f} "
              f"cpuSeconds={t['cpuSeconds']:.2f} effectiveCores={t['effectiveCores']:.2f} "
              f"baseBytesRead={t['baseBytesRead']} baseSeeks={t['baseSeeks']} "
              f"allocatedBytes={t['allocatedBytes']} reorderPeakEntries={t['reorderPeakEntries']} "
              f"reorderPeakBytes={t['reorderPeakBytes']} cachePeakRecords={t['cachePeakRecords']}")
        print(f"patchesSha256={patches_digest(document['files'])}")
    return 0


def collect(inputs: list[str]) -> tuple[list[tuple[Path, dict]], list[tuple[Path, dict]], list[str]]:
    throughput: list[tuple[Path, dict]] = []
    memory: list[tuple[Path, dict]] = []
    problems: list[str] = []
    paths: list[Path] = []
    for name in inputs:
        path = Path(name)
        if path.is_dir():
            paths.extend(sorted(path.rglob("*.json")))
        elif path.is_file():
            paths.append(path)
        else:
            problems.append(f"input not found: {name}")
    for path in paths:
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as error:
            problems.append(f"{path}: unreadable JSON ({error})")
            continue
        schema = document.get("schema") if isinstance(document, dict) else None
        if schema == THROUGHPUT_SCHEMA:
            throughput.append((path, document))
        elif schema == MEMORY_SCHEMA:
            memory.append((path, document))
    return throughput, memory, problems


def validate(throughput, memory, pairs_sha256: str, require_all: bool,
             require_memory: bool = False) -> tuple[list[str], dict, dict]:
    """Return (reasons, throughput by platform/execution, memory by platform/execution)."""
    reasons: list[str] = []
    by_lane: dict[str, dict[str, list[dict]]] = {}
    memory_lane: dict[str, dict[str, list[dict]]] = {}
    commits: set[str] = set()

    for path, document in throughput:
        label = str(path)
        platform = platform_of(document, path)
        if platform is None:
            reasons.append(f"{label}: platform not recognized")
            continue
        if document.get("corpusPairsSha256") != pairs_sha256:
            reasons.append(f"{label}: corpusPairsSha256 differs from the corpus lock")
        if not str(document.get("runId") or "").startswith(RUN_PREFIX):
            reasons.append(f"{label}: runId does not start with {RUN_PREFIX}")
        if document.get("workers") != 1:
            reasons.append(f"{label}: workers is {document.get('workers')!r}, not 1")
        if document.get("lane") != "csp":
            reasons.append(f"{label}: lane is {document.get('lane')!r}, not 'csp'")
        execution = document.get("execution")
        if execution not in EXECUTIONS:
            reasons.append(f"{label}: unknown execution {execution!r}")
            continue
        if any("createMetrics" not in item for item in document.get("files", [])):
            reasons.append(f"{label}: a file has no createMetrics")
            continue
        commit = (document.get("environment") or {}).get("gitCommit")
        if commit:
            commits.add(commit)
        else:
            reasons.append(f"{label}: environment.gitCommit missing")
        by_lane.setdefault(platform, {}).setdefault(execution, []).append(document)

    for path, document in memory:
        label = str(path)
        platform = platform_of(document, path)
        if platform is None:
            reasons.append(f"{label}: platform not recognized")
            continue
        if document.get("corpusPairsSha256") != pairs_sha256:
            reasons.append(f"{label}: corpusPairsSha256 differs from the corpus lock")
        execution = document.get("execution")
        if execution not in EXECUTIONS:
            reasons.append(f"{label}: unknown execution {execution!r}")
            continue
        memory_lane.setdefault(platform, {}).setdefault(execution, []).append(document)

    if len(commits) > 1:
        reasons.append(f"throughput documents disagree on gitCommit: {sorted(commits)}")
    if not by_lane:
        reasons.append("no throughput documents")

    for platform, lanes in sorted(by_lane.items()):
        for execution in EXECUTIONS:
            expected = 2 if execution == "h0" else 1
            found = len(lanes.get(execution, []))
            if found != expected:
                reasons.append(f"{platform} {execution}: {found} throughput documents, expected {expected}")
        h0 = lanes.get("h0", [])
        if len(h0) == 2:
            first, second = sorted((sum(i["createSeconds"] for i in d["files"]) for d in h0))
            if first <= 0 or second / first - 1 > H0_SPREAD_LIMIT:
                reasons.append(
                    f"{platform}: the two h0 lanes differ by more than 25 % "
                    f"({first:.2f} s and {second:.2f} s)")

    if require_all:
        for platform in PLATFORMS:
            if platform not in by_lane:
                reasons.append(f"platform {platform} is missing")

    if require_memory:
        for platform in sorted(by_lane):
            if platform not in memory_lane:
                reasons.append(f"{platform}: no memory documents")

    for platform, lanes in sorted(memory_lane.items()):
        for execution in EXECUTIONS:
            found = len(lanes.get(execution, []))
            if found != 1:
                reasons.append(f"{platform} {execution}: {found} memory documents, expected 1")
        reasons.extend(join_memory(platform, lanes, by_lane.get(platform, {})))
    return reasons, by_lane, memory_lane


def join_memory(platform: str, lanes: dict, throughput_lanes: dict) -> list[str]:
    """Memory evidence counts only for the run it belongs to: the same RunId
    and commit as the platform's throughput lanes, and exactly the throughput
    files of at least 1 MiB (the memory lane's --min-bytes)."""
    h0 = sorted(throughput_lanes.get("h0", []), key=lambda d: d.get("startedUtc") or "")
    if not h0:
        return [f"{platform}: memory documents without throughput documents"]
    reference = h0[0]
    run_id = reference.get("runId")
    commit = (reference.get("environment") or {}).get("gitCommit")
    expected = {file_key(item) for item in reference["files"] if item["targetSize"] >= MEMORY_MIN_BYTES}
    reasons = []
    for execution, documents in sorted(lanes.items()):
        for document in documents:
            label = f"{platform} {execution} memory"
            if document.get("runId") != run_id:
                reasons.append(f"{label}: runId {document.get('runId')!r} is not the throughput run {run_id!r}")
            memory_commit = (document.get("environment") or {}).get("gitCommit")
            if memory_commit != commit:
                reasons.append(f"{label}: gitCommit {memory_commit!r} is not the throughput commit {commit!r}")
            actual = {file_key(item) for item in document.get("files", [])}
            missing, extra = sorted(expected - actual), sorted(actual - expected)
            if missing:
                reasons.append(f"{label}: missing files of at least 1 MiB: {missing[:3]}"
                               + (f" and {len(missing) - 3} more" if len(missing) > 3 else ""))
            if extra:
                reasons.append(f"{label}: files not in the throughput run: {extra[:3]}")
    return reasons


def memory_peak_mib(document: dict) -> float | None:
    idle = document.get("idleBaselineBytes", 0)
    peaks = [item["createPeakBytes"] - idle for item in document["files"]]
    return max(peaks) / MIB if peaks else None


def lane_metrics(documents: list[dict], memory_docs: list[dict] | None, execution: str) -> dict:
    ordered = sorted(documents, key=lambda d: d.get("startedUtc") or "")
    first = ordered[0]
    t = totals(first)
    seconds = [sum(i["createSeconds"] for i in d["files"]) for d in ordered]
    mean = sum(seconds) / len(seconds)
    peak = memory_peak_mib(memory_docs[0]) if memory_docs else None
    bound = bound_mib(execution)
    return {
        "createSeconds": mean if execution == "h0" else t["createSeconds"],
        "h0Seconds": seconds if execution == "h0" else None,
        "files": t["files"],
        "cpuSeconds": t["cpuSeconds"],
        "effectiveCores": t["effectiveCores"],
        "baseBytesRead": t["baseBytesRead"],
        "baseSeeks": t["baseSeeks"],
        "allocatedBytes": t["allocatedBytes"],
        "targetMiBPerSecond": (t["targetBytes"] / MIB) / t["createSeconds"] if t["createSeconds"] > 0 else 0.0,
        "uniqueMissingMiBPerSecond":
            (t["uniqueMissingBytes"] / MIB) / t["createSeconds"] if t["createSeconds"] > 0 else 0.0,
        "reorderPeakEntries": t["reorderPeakEntries"],
        "reorderPeakBytes": t["reorderPeakBytes"],
        "cachePeakRecords": t["cachePeakRecords"],
        "cachePeakBytes": t["cachePeakBytes"],
        "memoryMiB": peak,
        "memoryBoundMiB": bound,
        "memoryMet": None if peak is None else peak <= bound,
        "patchesSha256": patches_digest(first["files"]),
    }


def compare_bytes(reference: dict, document: dict) -> dict:
    expected = {file_key(i): i.get("patchSha256") for i in reference["files"]}
    actual = {file_key(i): i.get("patchSha256") for i in document["files"]}
    mismatches = []
    for key in sorted(expected.keys() | actual.keys()):
        if key not in actual:
            mismatches.append(f"{key}: missing")
        elif key not in expected:
            mismatches.append(f"{key}: unexpected")
        elif expected[key] != actual[key]:
            mismatches.append(f"{key}: patchSha256 differs")
    return {"identical": not mismatches, "mismatches": mismatches}


def evaluate_family(family: str, platforms: dict, bytes_result: dict, present: list[str],
                    eligible: bool = True) -> dict:
    """Rows of one worker family (protocol section 7 rule 3); no decision."""
    complete = all(p in present for p in PLATFORMS)
    table = []
    for workers in WORKER_COUNTS:
        execution = f"{family}-w{workers}"
        speedups = {p: platforms[p][execution]["speedup"] for p in present}
        memory_met = {p: platforms[p][execution]["memoryMet"] for p in present}
        bytes_ok = all(bytes_result[execution][p]["identical"] for p in present)
        fast = sum(1 for s in speedups.values() if s >= SPEEDUP_MIN)
        memory_ok = complete and all(memory_met.get(p) is True for p in PLATFORMS)
        row_complete = complete and all(memory_met.get(p) is not None for p in PLATFORMS)
        qualifies = eligible and complete and bytes_ok and fast >= PLATFORMS_WITH_SPEEDUP and memory_ok
        if not bytes_ok:
            status = "REJECT (bytes)"
        elif not eligible:
            status = "not eligible (H1 not adopted)"
        elif qualifies:
            status = "qualifies"
        elif not row_complete:
            status = "incomplete"
        else:
            status = "does not qualify"
        table.append({
            "execution": execution,
            "workers": workers,
            "speedups": speedups,
            "platformsWithSpeedup": fast,
            "bytesIdentical": bytes_ok,
            "memoryMet": memory_met,
            "qualifies": qualifies,
            "status": status,
        })
    return {"family": family, "eligible": eligible, "table": table}


def decide_workers(platforms: dict, bytes_result: dict, present: list[str], h1_adopted: bool) -> dict:
    """Protocol section 7 rule 3: both families; H3 eligible only with H1 adopted;
    the smallest qualifying W, H3 before H2 at equal W."""
    families = {
        "h3": evaluate_family("h3", platforms, bytes_result, present, eligible=h1_adopted),
        "h2": evaluate_family("h2", platforms, bytes_result, present),
    }
    adopted = None
    for index, workers in enumerate(WORKER_COUNTS):
        for family in ("h3", "h2"):
            row = families[family]["table"][index]
            if row["qualifies"]:
                adopted = row
                break
        if adopted is not None:
            break
    incomplete = any(row["status"] == "incomplete"
                     for block in families.values() if block["eligible"] for row in block["table"])
    if adopted is not None:
        decision = "ADOPT"
    elif incomplete:
        decision = "INCOMPLETE"
    else:
        decision = "REJECT"
    return {
        "decision": decision,
        "adoptedExecution": adopted["execution"] if adopted else None,
        "adoptedWorkers": adopted["workers"] if adopted else None,
        "family": adopted["execution"].split("-")[0] if adopted else None,
        "h3Eligible": h1_adopted,
        "families": families,
    }


def decide_h1(platforms: dict, bytes_result: dict, present: list[str]) -> dict:
    """Protocol section 7 rule 2: bytes, read ratio, T(H1) <= mean T(H0) with no
    tolerance, and the W = 1 memory bound, on all three platforms."""
    ratios: dict[str, float | None] = {}
    regression: dict[str, bool] = {}
    memory: dict[str, bool | None] = {}
    for p in present:
        h0, h1 = platforms[p]["h0"], platforms[p]["h1"]
        r0, r1 = h0["baseBytesRead"], h1["baseBytesRead"]
        ratios[p] = (r0 / r1) if r1 > 0 else (None if r0 > 0 else 0.0)
        regression[p] = h1["createSeconds"] <= h0["createSeconds"]
        memory[p] = h1["memoryMet"]
    chosen = "linux-x64" if "linux-x64" in present else present[0]
    ratio = ratios[chosen]
    ratio_ok = ratio is None or ratio >= READ_RATIO_MIN
    bytes_ok = all(bytes_result["h1"][p]["identical"] for p in present)
    complete = all(p in present for p in PLATFORMS) and all(memory.get(p) is not None for p in PLATFORMS)
    failed = not bytes_ok or not ratio_ok or not all(regression.values()) or any(m is False for m in memory.values())
    if failed:
        decision = "REJECT"
    elif complete:
        decision = "ADOPT"
    else:
        decision = "INCOMPLETE"
    return {
        "decision": decision,
        "readRatio": ratio,
        "readRatioPlatform": chosen,
        "readRatios": ratios,
        "noRegression": regression,
        "memoryMet": memory,
        "bytesIdentical": bytes_ok,
    }


def render_markdown(verdict: dict) -> str:
    lines = ["# PATCH-ENC-004 create throughput", ""]
    if not verdict.get("decisionGrade"):
        lines += ["**Exploratory: not decision data** (run without --require-all-platforms "
                  "--require-memory, or invalid).", ""]
    lines.append(f"- commit: `{verdict['commit']}`")
    lines.append(f"- corpusPairsSha256: `{verdict['corpusPairsSha256']}`")
    lines.append(f"- valid: {str(verdict['valid']).lower()}")
    for reason in verdict["invalidReasons"]:
        lines.append(f"- INVALID: {reason}")
    for platform, run_id in sorted(verdict["runIds"].items()):
        lines.append(f"- {platform}: `{run_id}`")
    for platform, lanes in sorted(verdict["platforms"].items()):
        lines += ["", f"## {platform}", "",
                  "| execution | T s | speedup | CPU s | eff. cores | base MiB read | seeks | alloc MiB "
                  "| reorder entries | reorder bytes | M MiB | bound MiB | bytes identical |",
                  "|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
        for execution in EXECUTIONS:
            m = lanes.get(execution)
            if m is None:
                continue
            memory = "not measured" if m["memoryMiB"] is None else f"{m['memoryMiB']:.1f}"
            identical = verdict["bytes"][execution][platform]["identical"]
            lines.append(
                f"| {execution} | {m['createSeconds']:.2f} | {m['speedup']:.2f} | {m['cpuSeconds']:.2f} "
                f"| {m['effectiveCores']:.2f} | {m['baseBytesRead'] / MIB:.1f} | {m['baseSeeks']} "
                f"| {m['allocatedBytes'] / MIB:.1f} | {m['reorderPeakEntries']} | {m['reorderPeakBytes']} "
                f"| {memory} | {m['memoryBoundMiB']} | {'yes' if identical else 'NO'} |")
    lines += ["", "## Decision", ""]
    if not verdict["valid"]:
        lines.append("Run INVALID: no decision.")
        return "\n".join(lines) + "\n"
    for execution in EXECUTIONS:
        for platform, result in sorted(verdict["bytes"][execution].items()):
            if not result["identical"]:
                lines.append(f"- {execution} on {platform}: REJECT (bytes): "
                             + "; ".join(result["mismatches"]))
    h1 = verdict["h1"]
    ratios = ", ".join(f"{p}={'inf' if r is None else format(r, '.2f')}" for p, r in sorted(h1["readRatios"].items()))
    lines.append(f"- H1: {h1['decision']} (read ratio {ratios}; no regression {h1['noRegression']}; "
                 f"memory {h1['memoryMet']})")
    workers = verdict["workers"]
    adopted = workers["adoptedExecution"]
    lines.append(f"- Workers: {workers['decision']}" + (f" ({adopted})" if adopted else "")
                 + f"; H3 eligible: {str(workers['h3Eligible']).lower()}")
    for family in ("h3", "h2"):
        for row in workers["families"][family]["table"]:
            speeds = ", ".join(f"{p}={s:.2f}" for p, s in sorted(row["speedups"].items()))
            lines.append(f"  - {row['execution']}: {row['status']}; speedup {speeds}; "
                         f"memory {row['memoryMet']}")
    lines.append(f"- h0 patchesSha256 equal across platforms: {str(verdict['h0DigestsEqualAcrossPlatforms']).lower()}")
    return "\n".join(lines) + "\n"


def decide_command(arguments: argparse.Namespace) -> int:
    lock = json.loads(Path(arguments.corpus_lock).read_text(encoding="utf-8"))
    pairs = lock["pairsSha256"]
    throughput, memory, problems = collect(arguments.inputs)
    reasons, by_lane, memory_lane = validate(
        throughput, memory, pairs, arguments.require_all_platforms, arguments.require_memory)
    reasons = problems + reasons
    present = [p for p in PLATFORMS if p in by_lane]
    commits = sorted({(d.get("environment") or {}).get("gitCommit") for lanes in by_lane.values()
                      for docs in lanes.values() for d in docs} - {None})

    verdict: dict = {
        "schema": VERDICT_SCHEMA,
        "commit": commits[0] if len(commits) == 1 else None,
        "runIds": {},
        "corpusPairsSha256": pairs,
        "valid": not reasons,
        "decisionGrade": not reasons and arguments.require_all_platforms and arguments.require_memory,
        "invalidReasons": reasons,
        "platforms": {},
        "bytes": {},
        "h1": None,
        "workers": None,
        "h0DigestsEqualAcrossPlatforms": None,
    }
    for p in present:
        first = sorted(by_lane[p].get("h0", []), key=lambda d: d.get("startedUtc") or "")
        if first:
            verdict["runIds"][p] = first[0].get("runId")

    if not reasons:
        platforms: dict = {}
        bytes_result: dict = {e: {} for e in EXECUTIONS}
        for p in present:
            h0_docs = sorted(by_lane[p]["h0"], key=lambda d: d.get("startedUtc") or "")
            oracle = h0_docs[0]
            platforms[p] = {}
            for execution in EXECUTIONS:
                mem = memory_lane.get(p, {}).get(execution)
                platforms[p][execution] = lane_metrics(by_lane[p][execution], mem, execution)
                compared = h0_docs[1] if execution == "h0" else by_lane[p][execution][0]
                bytes_result[execution][p] = compare_bytes(oracle, compared)
            t_h0 = platforms[p]["h0"]["createSeconds"]
            for execution in EXECUTIONS:
                seconds = platforms[p][execution]["createSeconds"]
                platforms[p][execution]["speedup"] = t_h0 / seconds if seconds > 0 else 0.0
        verdict["platforms"] = platforms
        verdict["bytes"] = bytes_result
        verdict["h1"] = decide_h1(platforms, bytes_result, present)
        verdict["workers"] = decide_workers(
            platforms, bytes_result, present, verdict["h1"]["decision"] == "ADOPT")
        digests = {platforms[p]["h0"]["patchesSha256"] for p in present}
        verdict["h0DigestsEqualAcrossPlatforms"] = len(digests) == 1

    Path(arguments.output).write_text(json.dumps(verdict, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    markdown = render_markdown(verdict)
    Path(arguments.markdown).write_text(markdown, encoding="utf-8")
    print(markdown, end="")
    return 0 if verdict["valid"] else 1


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(prog="summarize_create_throughput.py", description=__doc__.strip().splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)
    printer = commands.add_parser("print", help="print totals of throughput documents")
    printer.add_argument("files", nargs="+")
    decider = commands.add_parser("decide", help="validate and decide PATCH-ENC-004")
    decider.add_argument("inputs", nargs="+")
    decider.add_argument("--corpus-lock", required=True)
    decider.add_argument("--output", required=True)
    decider.add_argument("--markdown", required=True)
    decider.add_argument("--require-all-platforms", action="store_true")
    decider.add_argument("--require-memory", action="store_true")
    arguments = parser.parse_args(argv)
    if arguments.command == "print":
        return print_command(arguments.files)
    return decide_command(arguments)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
