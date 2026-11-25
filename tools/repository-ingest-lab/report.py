"""Independent inventory/schedule audit and paired, descriptive screen summary."""
import argparse
import hashlib
import json
from pathlib import Path
import statistics

from validate import validate

METRICS = ("wall_ms_ready", "cpu_ms_ready", "allocated_bytes_delta_ready",
           "process_peak_working_set_bytes_at_ready", "restore_ms", "rebuild_ms", "cleanup_ms")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def inventory(root):
    entries = {}
    for line in (root / "sha256.txt").read_text(encoding="utf-8").splitlines():
        sha, name = line.split("  ", 1)
        require(name not in entries and Path(name).name == name and name != "sha256.txt",
                "invalid inventory name")
        entries[name] = sha
    files = {p.name for p in root.iterdir() if p.is_file()} - {"sha256.txt"}
    require(not any(p.is_dir() for p in root.iterdir()) and files == set(entries),
            "incomplete inventory or leftover directory")
    for name, sha in entries.items():
        require(digest(root / name) == sha, f"checksum mismatch: {name}")


def expected_schedule(rounds):
    for family in ("unique", "recent", "cross"):
        for suite in ("sha256", "blake3"):
            candidates = "ABC" if family == "unique" else "AC"
            for round_no in range(rounds + 1):
                shift = round_no % len(candidates)
                for order, candidate in enumerate(candidates[shift:] + candidates[:shift]):
                    yield family, suite, round_no, order, candidate


def spread(values):
    return dict(median=statistics.median(values), minimum=min(values), maximum=max(values))


def summarize(rows, rounds):
    groups = []
    for family in ("unique", "recent", "cross"):
        for suite in ("sha256", "blake3"):
            all_rows = [r for r in rows if r["family"] == family and r["suite"] == suite]
            measured = [r for r in all_rows if not r["warmup"]]
            candidates = "ABC" if family == "unique" else "AC"
            by_pair = {(r["round"], r["candidate"]): r for r in measured}
            require(len(by_pair) == len(measured) == rounds * len(candidates),
                    "missing or duplicated measured pairs")
            values = {}
            for candidate in candidates:
                series = [by_pair[(i, candidate)] for i in range(1, rounds + 1)]
                values[candidate] = {key: spread([r[key] for r in series]) for key in METRICS}
            pairs = {}
            comparisons = [("A", "C")]
            if family == "unique":
                comparisons.extend([("A", "B"), ("B", "C")])
            for baseline, candidate in comparisons:
                ratios = [by_pair[(i, baseline)]["wall_ms_ready"] /
                          by_pair[(i, candidate)]["wall_ms_ready"] for i in range(1, rounds + 1)]
                pairs[baseline + "/" + candidate] = dict(round_ratios=ratios, **spread(ratios))
            c = by_pair[(1, "C")]
            groups.append(dict(family=family, suite=suite, measured_rounds=rounds,
                               corpus_sha256=c["corpus_sha256"], layout_sha256=c["layout_sha256"],
                               logical_bytes=c["logical_bytes"], logical_occurrences=c["logical_occurrences"],
                               unique_payload_bytes=c["unique_payload_bytes"], unique_frames=c["unique_frames"],
                               pack_bytes=c["pack_bytes"], pack_count=c["pack_count"],
                               active_duplicate_count=c["active_duplicate_count"],
                               closed_duplicate_count=c["closed_duplicate_count"],
                               metrics=values, paired_ready_speedup=pairs))
    return groups


def audit(root, lock_path, commit):
    inventory(root)
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    require(manifest["commit"] == commit and manifest["dirty"] is False and
            manifest["experiment"] == lock["experiment"] and manifest["phase"] == "screening" and
            manifest["screen_lock_sha256"] == digest(lock_path), "manifest identity/lock mismatch")
    for key in ("mib", "target_mib", "rounds", "seed"):
        require(manifest[key] == lock[key], f"manifest parameter mismatch: {key}")
    rows = [json.loads(line) for line in (root / "samples.jsonl").read_text(encoding="utf-8").splitlines()]
    schedule = list(expected_schedule(lock["rounds"]))
    require(len(rows) == len(schedule) == lock["samples_per_lane"], "incomplete matrix")
    require(sum(r["warmup"] is True for r in rows) == lock["warmup_per_lane"], "wrong warmup count")
    signatures = {}
    locality = {}
    environment = set()
    for i, (row, expected) in enumerate(zip(rows, schedule, strict=True), 1):
        family, suite, round_no, order, candidate = expected
        require((row["family"], row["suite"], row["round"], row["order"], row["candidate"]) == expected
                and row["sample_id"] == i and row["warmup"] is (round_no == 0)
                and row["commit"] == commit and row["run_id"] == manifest["run_id"]
                and row["exit_code"] == 0 and row["outcome"] == "pass", "sample schedule/identity mismatch")
        require(row["corpus_sha256"] == lock["families"][family], "source lock mismatch")
        stdout = json.loads((root / f"sample-{i:03d}.stdout").read_text(encoding="utf-8"))
        require(all(row.get(k) == v for k, v in stdout.items()), "stdout/JSONL mismatch")
        signature = validate(row, candidate, suite, family, lock["families"][family],
                             lock["mib"] * 1024 * 1024, lock["target_mib"])
        require(signatures.setdefault((family, suite), signature) == signature,
                "physical shape mismatch")
        if candidate == "C":
            hits = (row["active_duplicate_count"], row["closed_duplicate_count"])
            require(locality.setdefault((family, suite), hits) == hits, "locality count changed")
        environment.add((row["runtime"], row["os"], row["arch"], row["processors"]))
    require(len(environment) == 1, "mixed process environments")
    return dict(experiment=lock["experiment"], run_id=manifest["run_id"], commit=commit,
                decision="SCREEN_ONLY", sdk=manifest["sdk"], environment=list(environment)[0],
                raw_samples=len(rows), warmups=lock["warmup_per_lane"],
                measured_samples=lock["measured_per_lane"],
                groups=summarize(rows, lock["rounds"]))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--raw", type=Path, required=True)
    p.add_argument("--commit", required=True)
    p.add_argument("--summary", type=Path, required=True)
    args = p.parse_args()
    result = audit(args.raw, Path(__file__).with_name("screen-lock.json"), args.commit)
    with args.summary.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, allow_nan=False)
    print(f"Screen audit PASS: {result['raw_samples']} raw, {result['measured_samples']} measured; SCREEN_ONLY")


if __name__ == "__main__":
    main()
