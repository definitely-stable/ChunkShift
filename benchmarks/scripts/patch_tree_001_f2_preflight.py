#!/usr/bin/env python3
"""PATCH-TREE-001 F2-P0 read-only preflight of originally measured F1 candidates.

No source bytes, no zstd call, no candidate re-encode and NO physical CSP verdict.
The three complete original archives are verified first by the F1 independent replay.
"""
import argparse
from collections import Counter
import importlib.util
import json
from pathlib import Path
import zipfile

SCHEMA = "chunkshift.patch-tree-f2-preflight.v1"
STATUS = "MEASURED_CANDIDATES_ONLY_NOT_PHYSICAL_NOT_DECISION"
REF_BYTES = 32  # CSP v1 ChunkId; F1 finalCostBytes already includes refs.


def require(ok, why):
    if not ok:
        raise ValueError(why)


def entry_identity(entry):
    return (entry["targetIndex"], entry["targetChunkId"], entry["targetLength"])


def chosen_cost(entry):
    """Observed chosen entry's stored bytes plus its physical dictionary refs."""
    length = entry["targetLength"]
    stored = entry["storedBytes"]
    refs = entry["dictionaryRefs"]
    require(isinstance(length, int) and 0 < length <= 16 * 1024 * 1024,
            "invalid target length")
    require(isinstance(stored, int) and stored > 0, "invalid stored bytes")
    require(isinstance(refs, int) and 0 <= refs <= 4, "invalid refs")
    return stored + refs * REF_BYTES


def constrained_choice(entry, path):
    """Cheapest *already evaluated* candidate whose FIRST record belongs to path.

    This is NOT a same-file dictionary: following records may cross file boundaries.
    Candidates without a measured final physical cost are strictly excluded.
    """
    size = entry["targetLength"]
    zstd = entry["noDictionaryFrameBytes"]
    require(isinstance(zstd, int) and zstd > 0, "invalid no-dictionary frame")
    best_cost, best_kind = size, "raw"
    if zstd < best_cost:
        best_cost, best_kind = zstd, "zstd"
    seen = set()
    eligible = 0
    available = 0
    for c in entry["candidates"]:
        ordinal = c["ordinal"]
        require(isinstance(ordinal, int) and ordinal >= 0 and ordinal not in seen,
                "duplicate/invalid candidate ordinal")
        seen.add(ordinal)
        start = c["startOffset"]
        count = c["recordCount"]
        require(isinstance(start, int) and start >= 0 and
                isinstance(count, int) and 1 <= count <= 4, "bad candidate coordinates")
        cost, frame = c["finalCostBytes"], c["finalFrameBytes"]
        require((cost is None) == (frame is None), "incomplete measured candidate")
        if cost is not None:
            require(isinstance(cost, int) and isinstance(frame, int) and
                    frame > 0 and cost == frame + count * REF_BYTES,
                    "candidate cost differs from physical refs")
        if c["firstSourceFile"] != path:
            continue
        eligible += 1
        if cost is None:
            continue
        available += 1
        if cost < best_cost:  # frozen H0 strict improvement, earlier candidate wins tie
            best_cost, best_kind = cost, "measured-first-source"
    return {"localCostBytes": best_cost, "choice": best_kind,
            "firstSourceEligible": eligible, "firstSourceFinalMeasured": available,
            "measuredCandidates": sum(c["finalCostBytes"] is not None for c in entry["candidates"])}


def compare_pair(path, t0, t1):
    """Check identical target entries and compute a trace-only sensitivity screen."""
    require(len(t0) == len(t1), "target entry cardinality mismatch")
    tally = Counter()
    for a, b in zip(t0, t1):
        require(entry_identity(a) == entry_identity(b), "emitted target identity/order mismatch")
        cost0, cost1 = chosen_cost(a), chosen_cost(b)
        selected = constrained_choice(b, path)
        tally["emittedEntries"] += 1
        tally["t0ObservedLocalCostBytes"] += cost0
        tally["t1ObservedLocalCostBytes"] += cost1
        tally["firstSourceFilteredCostBytes"] += selected["localCostBytes"]
        tally["t1WithoutDictionaryLocalCostBytes"] += min(b["targetLength"], b["noDictionaryFrameBytes"])
        tally["firstSourceCandidateStarts"] += selected["firstSourceEligible"]
        tally["firstSourceFinalMeasuredStarts"] += selected["firstSourceFinalMeasured"]
        tally["allFinalMeasuredStarts"] += selected["measuredCandidates"]
        tally["firstSourceFilteredDictionaryChoices"] += selected["choice"] == "measured-first-source"
        tally["t1OriginalDictionaryChoices"] += b["dictionaryRefs"] > 0
        tally["entriesWithoutFirstSourceMeasuredCandidate"] += selected["firstSourceFinalMeasured"] == 0
        tally["filteredWouldDifferFromOriginalSelectedCost"] += selected["localCostBytes"] != cost1
    return tally


def _load_replay():
    path = (Path(__file__).resolve().parents[2] /
            "docs/research/results/data/PATCH-TREE-001-F1-EVIDENCE-20261016-001/recompute.py")
    require(path.is_file(), "missing previously merged independent F1 replay")
    spec = importlib.util.spec_from_file_location("locked_f1_recompute", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def preflight(paths, ledger):
    """Original F1 replay is a *mandatory* gate, never optional on real data."""
    replay = _load_replay()
    frozen = json.loads(Path(ledger).read_text(encoding="utf-8"))
    gate = replay.replay(paths, frozen)
    require(gate["status"] == "ORIGINAL_ARCHIVES_REPLAY_PASS_NOT_CAUSAL" and
            gate["observations"] == 2100, "original F1 replay did not pass")
    tally = Counter()
    matched_paths = 0
    with zipfile.ZipFile(paths["f1"]) as f1, zipfile.ZipFile(paths["boundary"]) as bounds:
        s = json.loads(f1.read("f1-summary.json"))
        cal = json.loads(f1.read("calibration.json"))
        b = json.loads(bounds.read("boundary.json"))
        border = {(p["family"], p["baseVersion"], p["targetVersion"], x["path"]): x
                  for p in b["pairs"] for x in p["files"] if x["kind"] in ("changed", "added")}
        rows = iter(s["files"])
        for pair in cal["pairs"]:
            for item in pair["files"]:
                base_key = (pair["family"], pair["baseVersion"], pair["targetVersion"], item["path"])
                observed = {}
                for lane in ("T0", "T1"):
                    row = next(rows)
                    require(tuple(row[k] for k in ("family", "baseVersion", "targetVersion", "path")) == base_key and row["lane"] == lane,
                            "F1 row identity mismatch")
                    if lane == "T0" and item["kind"] == "added":
                        continue
                    name = "f1-observations/" + row["observationFile"]
                    o = json.loads(f1.read(name))
                    require(o["lane"] == lane and o["publicPatchSha256"] == row["publicPatchSha256"],
                            "F1 observed CSP identity mismatch")
                    observed[lane] = o["entries"]
                edge = border[base_key]
                if item["kind"] != "changed" or edge["targetExactInSamePathOld"]["bytes"] != edge["targetExactInVirtualTree"]["bytes"]:
                    continue
                matched_paths += 1
                tally.update(compare_pair(item["path"], observed["T0"], observed["T1"]))
        require(next(rows, None) is None, "extra F1 rows")
    require(matched_paths == 981 and tally["emittedEntries"] == 2682,
            "not the frozen 981-file / 2682-emitted-entry matched cohort")
    # Trace re-selection only provides local-cost sensitivity: changing source
    # first-occurrence and frame metadata can alter CSP bytes.
    return {"schema": SCHEMA, "status": STATUS,
            "f1RunId": frozen["f1"]["runId"], "f1OriginalZipSha256": frozen["f1"]["zipSha256"],
            "cohort": "frozen equal-exact-availability and equal-emitted-targets",
            "matchedChangedFiles": matched_paths,
            "countsAndLocalCostOnly": dict(sorted(tally.items())),
            "caveats": ["No zstd re-encoding or physical CSP construction",
                        "First-source-file is not proof all dictionary chunks lie within that file",
                        "Only already measured T1 candidates; no unseen counterfactual dictionaries",
                        "No apply verification, CPU, RSS or I/O measurements",
                        "No sealed holdout, no adoption or public API decision"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("f1", "boundary", "physical", "ledger"):
        parser.add_argument("--" + key, required=True)
    parser.add_argument("--output", help="Optional JSON output; do not overwrite existing files")
    args = parser.parse_args()
    result = preflight({key: getattr(args, key) for key in ("f1", "boundary", "physical")}, args.ledger)
    raw = json.dumps(result, indent=2, sort_keys=True) + "\n"
    if args.output:
        with open(args.output, "x", encoding="utf-8") as f:
            f.write(raw)
    else:
        print(raw, end="")


if __name__ == "__main__":
    main()
