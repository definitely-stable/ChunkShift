"""Independent evidence audit and sealed decision (no candidate execution)."""
import argparse
from fractions import Fraction
import json
from pathlib import Path
import re
from statistics import median

from holdout_contract import check_definition, validate_external, EXPECTED_ARCH, QUALIFICATION_KEYS
from prepare_holdout import digest, require, verify_asset

LAB = Path(__file__).resolve().parent
LOCK = LAB / "holdout-lock.json"
GEOMETRY = LAB / "holdout-geometry-lock.json"
QUALIFICATION = LAB / "holdout-qualification-lock.json"
METRICS = ("wall_ms_ready", "cpu_ms_ready", "allocated_bytes_delta_ready",
           "process_peak_working_set_bytes_at_ready", "restore_ms", "rebuild_ms", "cleanup_ms")
FINGERPRINT_FILES = ("holdout_run.py", "holdout_evaluate.py")


def strict_json(text):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate JSON key")
            result[key] = value
        return result

    def invalid(value):
        raise ValueError("nonfinite JSON number")

    return json.loads(text, object_pairs_hook=pairs, parse_constant=invalid)


def read_json(path):
    return strict_json(path.read_text(encoding="utf-8"))


def definitions():
    lock, geometry, qualified = map(read_json, (LOCK, GEOMETRY, QUALIFICATION))
    check_definition(lock, geometry, digest(LOCK))
    require(qualified["experiment"] == lock["experiment"] and
            qualified["phase"] == "accepted-qualification" and
            qualified["source_lock_sha256"] == digest(LOCK) and
            qualified["geometry_lock_sha256"] == digest(GEOMETRY), "unbound qualification")
    for name, sha in qualified["fingerprints"].items():
        require(digest(LAB / name) == sha, "qualified input changed: " + name)
    require(set(qualified["layouts"]) == set(lock["design"]["families"]) and
            all(set(group) == set(lock["design"]["suites"]) and
                all(re.fullmatch(r"[0-9a-f]{64}", x) for x in group.values())
                for group in qualified["layouts"].values()), "invalid layout lock")
    rule = lock["rule"]
    require(rule["primary_families"] == ["win-versioned", "linux-versioned", "compressed"]
            and rule["min_geomean_paired_median_d_over_c_ready"] == 1.10
            and rule["max_paired_median_c_over_d"] ==
            dict(wall_ms_ready=1.05, cpu_ms_ready=1.05, allocated_bytes_delta_ready=1.05,
                 process_peak_working_set_bytes_at_ready=1.05, restore_ms=1.10, rebuild_ms=1.10)
            and rule["required_adverse_hits"] == ["active", "closed"]
            and rule["require_each_runner_suite"] is True and rule["pass"] == "PERFORMANCE_ELIGIBLE"
            and rule["valid_below_rule"] == rule["production_default"] == rule["coverage_gap"] == "DEFER"
            and rule["invalid_or_missing"] == "STOP" and rule["cleanup"] == "report_only",
            "unexpected sealed decision rule")
    return lock, geometry, qualified


def schedule(lock):
    design = lock["design"]
    for family in design["families"]:
        for suite in design["suites"]:
            for round_no, order in enumerate(["DC"] + design["measured_order"]):
                for position, candidate in enumerate(order):
                    yield family, suite, round_no, position, candidate


def fingerprints(qualified):
    return {**qualified["fingerprints"], **{name: digest(LAB / name) for name in FINGERPRINT_FILES}}


def identities():
    return dict(source_lock_sha256=digest(LOCK), geometry_lock_sha256=digest(GEOMETRY),
                qualification_lock_sha256=digest(QUALIFICATION))


def verify_replay(replay, lock, qualified):
    require(replay["experiment"] == lock["experiment"] and replay["phase"] == "locked-replay"
            and replay["corpora"] == lock["corpora"]
            and replay["metadata_sha256"] == lock["provenance"]["metadata_sha256"]
            and replay["generator_sha256"] == qualified["fingerprints"]["prepare_holdout.py"],
            "source replay identity")
    expected = {(a["rid"], a["version"]): a for a in lock["assets"]}
    actual = {(a["rid"], a["version"]): a for a in replay["assets"]}
    require(len(actual) == len(replay["assets"]) == len(expected) and set(actual) == set(expected),
            "source replay asset set")
    for key, asset in actual.items():
        verify_asset(asset, expected[key])


def validate_result(result, lock, geometry, qualified, family, suite, candidate, lane):
    require(set(result) == set(QUALIFICATION_KEYS) | set(METRICS), "unexpected sample fields")
    validate_external(result, lock, geometry, family, suite, candidate, lane)
    require(result["layout_sha256"] == qualified["layouts"][family][suite],
            "qualified physical layout changed")
    for metric in METRICS[:-1]:
        require(result[metric] > 0, "nonpositive decision metric: " + metric)
    return result


def fraction(value):
    # Compare exact rational forms of the recorded JSON numeric values.
    # No rounding of individual ratios or decision boundaries.
    return Fraction(str(value))


def paired_metrics(rows, lock):
    groups = []
    rule = lock["rule"]
    for family in lock["design"]["families"]:
        for suite in lock["design"]["suites"]:
            selected = [r for r in rows if r["family"] == family and r["suite"] == suite and not r["warmup"]]
            by_pair = {(r["round"], r["candidate"]): r["result"] for r in selected}
            require(len(selected) == len(by_pair) == 12 and
                    set(by_pair) == {(n, c) for n in range(1, 7) for c in ("D", "C")}, "incomplete pair set")
            metrics = {}
            guards = []
            for key in METRICS:
                d = [fraction(by_pair[n, "D"][key]) for n in range(1, 7)]
                c = [fraction(by_pair[n, "C"][key]) for n in range(1, 7)]
                require(all(x >= 0 for x in d+c), "negative metric")
                ratios = None if key == "cleanup_ms" and any(x == 0 for x in d) else [y/x for x, y in zip(d, c)]
                metrics[key] = dict(D_median=float(median(d)), C_median=float(median(c)),
                                    C_over_D_pairs=None if ratios is None else [float(v) for v in ratios],
                                    C_over_D_median=None if ratios is None else float(median(ratios)))
                if key in rule["max_paired_median_c_over_d"]:
                    require(ratios is not None, "missing decision ratios")
                    if median(ratios) > fraction(rule["max_paired_median_c_over_d"][key]):
                        guards.append(key)
            ready = [fraction(by_pair[n, "D"]["wall_ms_ready"])/fraction(by_pair[n, "C"]["wall_ms_ready"])
                     for n in range(1, 7)]
            groups.append(dict(family=family, suite=suite, metrics=metrics,
                               D_over_C_ready_pairs=[float(v) for v in ready],
                               D_over_C_ready_median=float(median(ready)),
                               ready_median_exact=str(median(ready)), guardrail_failures=guards))
    primary = []
    for suite in lock["design"]["suites"]:
        product = Fraction(1)
        for group in groups:
            if group["suite"] == suite and group["family"] in rule["primary_families"]:
                product *= Fraction(group["ready_median_exact"])
        passed = product >= fraction(rule["min_geomean_paired_median_d_over_c_ready"]) ** 3
        primary.append(dict(suite=suite, geomean=float(product) ** (1/3),
                            product_exact=str(product), passed=passed))
    return dict(groups=groups, primary=primary,
                decision=rule["pass"] if all(p["passed"] for p in primary)
                and not any(g["guardrail_failures"] for g in groups) else rule["valid_below_rule"])


def inventory(raw):
    expected = {}
    for line in (raw / "sha256.txt").read_text(encoding="utf-8").splitlines():
        sha, name = line.split("  ", 1)
        require(re.fullmatch(r"[0-9a-f]{64}", sha) is not None and
                name not in expected and name != "sha256.txt" and Path(name).name == name
                and "/" not in name and "\\" not in name, "invalid inventory entry")
        expected[name] = sha
    files = list(raw.iterdir())
    require(all(p.is_file() and not p.is_symlink() for p in files) and
            {p.name for p in files} == set(expected) | {"sha256.txt"}, "inventory file set mismatch")
    for name, sha in expected.items():
        require(digest(raw / name) == sha, "inventory hash mismatch")
    names = {"manifest.json", "replay.json", "build.log", "samples.jsonl", "completion.json"}
    names |= {f"sample-{n:03d}.{ext}" for n in range(1, 113) for ext in ("stdout", "stderr")}
    require(set(expected) == names, "incomplete timed inventory")


def audit_lane(raw, commit, run_id, attempt, lock, geometry, qualified):
    inventory(raw)
    meta = read_json(raw / "manifest.json")
    lane, replica = meta["lane"], meta["replica"]
    require(lane in EXPECTED_ARCH and type(replica) is int and replica in (1, 2)
            and meta["experiment"] == lock["experiment"] and meta["phase"] == "sealed-holdout"
            and meta["dirty"] is False and meta["commit"] == commit
            and meta["github_run_id"] == run_id and meta["github_run_attempt"] == attempt
            and meta["run_id"] == f"REPO-INGEST-003/RUN-{run_id}-{attempt}-{commit[:12]}-{lane}-{replica}"
            and all(meta[k] == v for k, v in identities().items())
            and meta["fingerprints"] == fingerprints(qualified), "manifest identity mismatch")
    require(all(isinstance(meta[k], str) and meta[k].strip() for k in
                ("sdk", "dotnet_info", "host", "machine", "runner_image", "runner_image_version",
                 "started_utc", "runner_name")), "missing runner provenance")
    verify_replay(read_json(raw / "replay.json"), lock, qualified)
    rows = [strict_json(line) for line in (raw / "samples.jsonl").read_text(encoding="utf-8").splitlines()]
    completion = read_json(raw / "completion.json")
    require(completion == dict(commit=commit, run_id=meta["run_id"], samples=112, measured=96,
                               source_sha256={f: spec["sha256"] for f, spec in lock["corpora"].items()},
                               private_empty=True, checkout_clean=True,
                               samples_sha256=digest(raw / "samples.jsonl")), "invalid completion seal")
    expected = list(schedule(lock))
    require(len(rows) == len(expected) == 112, "wrong sample count")
    env = set()
    for n, (row, (family, suite, round_no, position, candidate)) in enumerate(zip(rows, expected), 1):
        result = read_json(raw / f"sample-{n:03d}.stdout")
        require(row == dict(sample_id=n, family=family, suite=suite, round=round_no, order=position,
                            candidate=candidate, warmup=round_no == 0, commit=commit, run_id=meta["run_id"],
                            lane=lane, replica=replica, exit_code=0, outcome="pass", result=result),
                "record/schedule/stdout mismatch")
        validate_result(result, lock, geometry, qualified, family, suite, candidate, lane)
        env.add(tuple(result[k] for k in ("runtime", "os", "arch", "processors")))
    require(len(env) == 1, "mixed lane environment")
    return dict(lane=lane, replica=replica, run_id=meta["run_id"], runner_name=meta["runner_name"],
                raw_samples=112, measured_samples=96, environment=list(env)[0],
                **paired_metrics(rows, lock))


def combine(lanes, lock):
    expected = {(p, r) for p in lock["design"]["platforms"] for r in (1, 2)}
    require(len(lanes) == 6 and {(x["lane"], x["replica"]) for x in lanes} == expected
            and len({x["run_id"] for x in lanes}) == 6, "missing/duplicate runner lane")
    require(all(x["decision"] in (lock["rule"]["pass"], lock["rule"]["valid_below_rule"])
                for x in lanes), "invalid lane decision")
    return dict(decision=lock["rule"]["pass"] if all(x["decision"] == lock["rule"]["pass"]
                for x in lanes) else "DEFER", production_default="DEFER",
                raw_samples=672, measured_samples=576, lanes=lanes)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--raw", type=Path, nargs=6, required=True)
    p.add_argument("--commit", required=True)
    p.add_argument("--github-run-id", required=True)
    p.add_argument("--attempt", required=True)
    p.add_argument("--summary", type=Path, required=True)
    args = p.parse_args()
    require(re.fullmatch(r"[0-9a-f]{40}", args.commit) is not None and
            args.github_run_id.isdecimal() and args.attempt.isdecimal(), "invalid run identity")
    lock, geometry, qualified = definitions()
    roots = [x.resolve() for x in args.raw]
    require(len(set(roots)) == 6 and all(x != args.summary.resolve() and
            x not in args.summary.resolve().parents for x in roots), "invalid audit paths")
    lanes = [audit_lane(raw, args.commit, args.github_run_id, args.attempt, lock, geometry, qualified)
             for raw in roots]
    result = dict(experiment=lock["experiment"], commit=args.commit, github_run_id=args.github_run_id,
                  attempt=args.attempt, **identities(), **combine(lanes, lock))
    with args.summary.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, allow_nan=False)
    print(result["decision"])


if __name__ == "__main__":
    main()
