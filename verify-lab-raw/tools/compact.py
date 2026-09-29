"""Builds the compact CORE-VERIFY-002 dataset from the raw run documents."""
import csv, json, pathlib, shutil, sys

raw = pathlib.Path(sys.argv[1])
out = pathlib.Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)

runs, rows = [], []
for path in sorted(raw.glob("run-*.json")):
    doc = json.loads(path.read_text())
    meta = {k: v for k, v in doc.items() if k not in ("samples", "aggregates")}
    meta["file"] = path.name
    meta["sampleCount"] = len(doc["samples"])
    runs.append(dict(sorted(meta.items())))
    for s in doc["samples"]:
        m = s.get("measurement") or {}
        rows.append({
            "platform": doc["platform"], "runId": doc["runId"], "workload": s["workload"], "suite": s["suite"],
            "mode": s["mode"], "pool": s["pool"], "lane": s["lane"], "concurrency": s["concurrency"],
            "repetition": s["repetition"], "order": s["order"], "bytes": m.get("bytes"), "files": m.get("files"),
            "wallSeconds": m.get("wallSeconds"), "cpuSeconds": m.get("cpuSeconds"),
            "allocatedBytes": m.get("allocatedBytes"), "peakWorkingSetBytes": m.get("peakWorkingSetBytes"),
            "valid": m.get("valid", False), "poolSeen": m.get("pool"), "residency": m.get("residency"),
            "probeGiBPerSecond": m.get("probeGiBPerSecond"), "residentFraction": m.get("residentFraction"),
            "error": s.get("error") or "",
        })

(out / "runs.json").write_text(json.dumps(runs, indent=1) + "\n")
with (out / "samples.csv").open("w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
    w.writeheader()
    for r in rows:
        r["valid"] = "True" if r["valid"] else "False"
        w.writerow({k: "" if v is None else v for k, v in r.items()})
for name in ["decision.json", "decision.md", *[p.name for p in raw.glob("*-oracle-*.json")]]:
    shutil.copy(raw / name, out / name)
print(len(runs), "runs,", len(rows), "samples")
