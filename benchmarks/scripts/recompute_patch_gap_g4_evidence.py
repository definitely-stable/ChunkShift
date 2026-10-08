#!/usr/bin/env python3
"""Independent standalone recomputation of committed frozen G4 compact evidence."""
import argparse
import base64
import gzip
import hashlib
import json
from collections import defaultdict
from pathlib import Path

EXPECTED = "c71f0c8fa483c45bf65fee4850d293c45f7878a6"
HEADER = "datasetRole\tfamily\tbaseVersion\ttargetVersion\tpath\th0Bytes\tg4Bytes\tsavedBytes\teligible\twinners"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def fail_if_false(condition, label):
    if not condition:
        raise ValueError("PATCH-GAP G4 compact recomputation: " + label)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    args = parser.parse_args()
    root = args.root
    summary = json.loads((root / "summary.json").read_text(encoding="utf-8"))
    manifest = json.loads((root / "files-manifest.json").read_text(encoding="utf-8"))
    artifacts = json.loads((root / "artifacts.json").read_text(encoding="utf-8"))
    fail_if_false(summary["schema"] == "chunkshift.patch-gap-g4-evidence.v1" and
                  summary["sourceCommit"] == EXPECTED, "source identity")
    fail_if_false(manifest["canonicalHeader"] == HEADER, "canonical header")
    fail_if_false(set(artifacts) == {"calibration", "evaluation"}, "artifact roles")
    for role in artifacts:
        fail_if_false(artifacts[role]["sourceCommit"] == EXPECTED, "artifact source")
    combined = []
    values = defaultdict(lambda: defaultdict(lambda:
                         {"files": 0, "eligible": 0, "h0": 0, "g4": 0, "saved": 0, "winners": 0}))
    seen = set()
    for shard in manifest["shards"]:
        path = root / shard["path"]
        encoded = path.read_text(encoding="ascii").strip()
        packed = base64.b64decode(encoded, validate=True)
        fail_if_false(len(packed) == shard["gzipBytes"] and
                      digest(packed) == shard["gzipSha256"], "compressed digest")
        data = gzip.decompress(packed)
        fail_if_false(len(data) == shard["tsvBytes"] and
                      digest(data) == shard["tsvSha256"], "TSV digest")
        lines = data.decode("utf-8").splitlines(keepends=True)
        fail_if_false(lines[0] == HEADER + "\n", "shard header")
        fail_if_false(len(lines) == shard["rows"] + 1, "shard row count")
        for line in lines[1:]:
            fields = line.rstrip("\n").split("\t")
            fail_if_false(len(fields) == 10 and fields[0] == shard["datasetRole"]
                          and fields[1] == shard["family"], "shard member identity")
            key = tuple(fields[:5])
            fail_if_false(key not in seen, "duplicate file identity")
            seen.add(key)
            h0, g4, saved, eligible, winners = map(int, fields[5:])
            fail_if_false(h0 >= g4 > 0 and h0 - g4 == saved and
                          eligible in (0, 1) and winners >= 0, "per-file physical cost")
            d = values[fields[0]][fields[1]]
            for name, v in (("files", 1), ("h0", h0), ("g4", g4),
                            ("saved", saved), ("eligible", eligible), ("winners", winners)):
                d[name] += v
            combined.append((key, line))
    canonical = (HEADER + "\n" + "".join(line for _, line in sorted(combined))).encode("utf-8")
    fail_if_false(len(combined) == 1893 and
                  len(canonical) == manifest["combined"]["bytes"] and
                  digest(canonical) == manifest["combined"]["sha256"], "combined digest")
    fail_if_false(set(values) == {"calibration", "evaluation"}, "role population")
    for role in ("calibration", "evaluation"):
        lane = summary["splits"][role]["lane"]
        fail_if_false(set(values[role]) == set(summary["splits"][role]["families"]),
                      "family population")
        fail_if_false(sum(v["files"] for v in values[role].values()) ==
                      {"calibration": 1049, "evaluation": 844}[role],
                      "frozen file count")
        for family, stats in values[role].items():
            fail_if_false(stats == summary["splits"][role]["families"][family],
                          "family " + family)
        total_h0 = sum(v["h0"] for v in values[role].values())
        total_g4 = sum(v["g4"] for v in values[role].values())
        total_winners = sum(v["winners"] for v in values[role].values())
        fail_if_false(total_h0 == lane["h0Bytes"] and
                      total_g4 == lane["factorBytes"] and
                      total_h0 - total_g4 == lane["savedBytes"] and
                      total_winners == lane["bcjWinnerEntries"], "role aggregate")
        gate = 20 * (total_h0 - total_g4) >= 3 * total_h0
        fail_if_false(gate == lane["meetsRfcSizeGate"], "size gate")
        print("{}: h0={}, g4={}, saved={}, gate={}".format(
            role, total_h0, total_g4, total_h0 - total_g4, gate))
    expected = ("SIZE_GATE_PASS_NON_SIZE_PENDING" if
                summary["splits"]["evaluation"]["lane"]["meetsRfcSizeGate"] else "REJECT")
    for role in ("calibration", "evaluation"):
        documents = artifacts[role].get("documents", {})
        fail_if_false(set(documents) == {"g4-index.json", "h0-anchor.json",
                      "xz-provenance.json", "verify.txt", "details"},
                      "document provenance")
        for filename in ("g4-index.json", "h0-anchor.json", "xz-provenance.json", "verify.txt"):
            record = documents[filename]
            fail_if_false(record["bytes"] > 0 and len(record["sha256"]) == 64,
                          "root document identity")
        details = documents["details"]
        fail_if_false(details["count"] == {"calibration": 1049, "evaluation": 844}[role]
                      and len(details["manifestSha256"]) == 64
                      and details["manifestBytes"] > 0, "detail manifest identity")
    fail_if_false(summary["status"] == expected, "verdict")
    print("G4 durable evidence recomputation PASS; rows=1893")


if __name__ == "__main__":
    main()
