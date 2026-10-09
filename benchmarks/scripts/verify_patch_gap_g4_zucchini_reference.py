#!/usr/bin/env python3
"""Independent frozen G4 Zucchini reference completeness and subset-cost auditor.

This script NEVER interprets whole-file Zucchini patch bytes as a CSP-vNext
one-factor gate. Reference-only paired H0 values are looked up by exact file
identity in archived G4 H0 compact evidence.
"""
from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
from collections import Counter
from pathlib import Path

EXPECTED_SCHEMA = "chunkshift.patch-gap-g4-zucchini-reference.v1"
EXPECTED_LOCK = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
EVIDENCE = Path("docs/research/results/data/PATCH-GAP-001-G4-EVIDENCE-20261008-001")
HEADER = ("datasetRole", "family", "baseVersion", "targetVersion",
          "path", "h0Bytes", "g4Bytes", "savedBytes", "eligible", "winners")


def check(test: bool, message: str) -> None:
    if not test:
        raise ValueError("G4 Zucchini reference verify: " + message)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def frozen_h0(root: Path, role: str) -> dict[tuple, int]:
    manifest = load(root / "files-manifest.json")
    summary = load(root / "summary.json")
    check(manifest["schema"] == "chunkshift.patch-gap-g4-files.v1", "H0 evidence schema")
    check(summary["schema"] == "chunkshift.patch-gap-g4-evidence.v1", "G4 evidence schema")
    check(manifest["canonicalHeader"] == "\t".join(HEADER), "H0 TSV header")
    seen = set()
    h0 = {}
    for shard in manifest["shards"]:
        if shard["datasetRole"] != role:
            continue
        source = (root / shard["path"]).read_text(encoding="ascii").strip()
        packed = base64.b64decode(source, validate=True)
        check(digest(packed) == shard["gzipSha256"] and len(packed) == shard["gzipBytes"],
              "H0 gzip digest")
        data = gzip.decompress(packed)
        check(digest(data) == shard["tsvSha256"] and len(data) == shard["tsvBytes"],
              "H0 TSV digest")
        lines = data.decode("utf-8").splitlines()
        check(lines.pop(0) == "\t".join(HEADER), "H0 TSV header line")
        check(len(lines) == shard["rows"], "H0 shard row count")
        for line in lines:
            fields = line.split("\t")
            check(len(fields) == 10 and fields[0] == role and fields[1] == shard["family"],
                  "H0 row canonical identity")
            key = tuple(fields[:5])
            check(key not in seen, "duplicate H0 row")
            seen.add(key)
            h0[key] = int(fields[5])
            check(int(fields[5]) - int(fields[6]) == int(fields[7]), "H0 row equation")
    check(len(h0) == (1049 if role == "calibration" else 844), "H0 role count")
    check(sum(h0.values()) == summary["splits"][role]["lane"]["h0Bytes"], "H0 role anchor")
    return h0


def verify(reference: dict, h0: dict[tuple, int], role: str) -> dict:
    check(reference["schema"] == EXPECTED_SCHEMA and reference["datasetRole"] == role,
          "reference schema/role")
    check(reference["corpusPairsSha256"] == EXPECTED_LOCK, "reference corpus lock")
    check(reference["mode"] == "measure", "not a real reference measurement")
    tool = reference.get("tool")
    check(isinstance(tool, dict) and len(tool.get("binarySha256", "")) == 64,
          "missing exact real binary provenance")
    rows = reference["files"]
    check(len(rows) == (1049 if role == "calibration" else 814), "reference population")
    seen = set()
    counters = Counter()
    paired_h0 = 0
    paired_zucchini = 0
    for r in rows:
        key = (role, r["family"], r["baseVersion"], r["targetVersion"], r["path"])
        check(key not in seen and key in h0, "missing or duplicate frozen H0 file identity")
        seen.add(key)
        status = r["status"]
        counters[status] += 1
        check(status in ("OUTSIDE_STRUCTURAL_SUBSET", "UNSUPPORTED_BASE",
                         "UNSUPPORTED_TARGET", "UNSUPPORTED_BASE_AND_TARGET", "VERIFIED"),
              "unknown reference status")
        if status == "VERIFIED":
            check(bool(r["structuralCandidate"]) and
                  isinstance(r.get("patchBytes"), int) and r["patchBytes"] > 0,
                  "verified row without physical patch bytes")
            check(len(r.get("patchSha256", "")) == 64 and
                  r.get("reconstructionSha256") == r["targetSha256"],
                  "verified patch/reconstruction identity")
            check(r.get("baseParserExit") == r.get("targetParserExit") == 0,
                  "verified row without positive parser probes")
            paired_h0 += h0[key]
            paired_zucchini += r["patchBytes"]
        else:
            check(r.get("patchBytes") is None, "unsupported row carries a patch cost")
            if status == "OUTSIDE_STRUCTURAL_SUBSET":
                check(not r["structuralCandidate"], "excluded structurally valid pair")
            else:
                check(bool(r["structuralCandidate"]), "unsupported structurally excluded row")
                # Only upstream Zucchini exit code 6 is a valid parser miss.
                exit_codes = (r.get("baseParserExit"), r.get("targetParserExit"))
                expected = {
                    "UNSUPPORTED_BASE": (6, 0),
                    "UNSUPPORTED_TARGET": (0, 6),
                    "UNSUPPORTED_BASE_AND_TARGET": (6, 6),
                }
                check(exit_codes == expected[status],
                      "unsupported status is not backed by parser exit code 6")
    check(sum(counters.values()) == len(rows), "status total")
    check(counters == Counter(reference["counts"]), "reported status counts")
    # This is a *matched subset descriptive comparison*, not a CSP factor gate.
    return {
        "schema": "chunkshift.patch-gap-g4-zucchini-reference-audit.v1",
        "datasetRole": role,
        "matchedVerifiedFiles": counters["VERIFIED"],
        "unsupportedFiles": sum(v for k, v in counters.items() if k.startswith("UNSUPPORTED_")),
        "outsideStructuralSubset": counters["OUTSIDE_STRUCTURAL_SUBSET"],
        "matchedH0PatchBytes": paired_h0,
        "matchedZucchiniPatchBytes": paired_zucchini,
        "matchedDifferenceBytes": paired_h0 - paired_zucchini,
        "referenceOnly": True,
        "rfcSizeGate": "NOT_APPLICABLE",
    }


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--reference", type=Path, required=True)
    p.add_argument("--role", choices=["calibration", "evaluation"], required=True)
    p.add_argument("--h0-evidence", type=Path, default=EVIDENCE)
    p.add_argument("--output", type=Path)
    args = p.parse_args()
    report = verify(load(args.reference), frozen_h0(args.h0_evidence, args.role), args.role)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, sort_keys=True))


if __name__ == "__main__":
    main()
