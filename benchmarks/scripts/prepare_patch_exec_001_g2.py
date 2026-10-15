#!/usr/bin/env python3
"""Prepare a strictly calibrated PATCH-EXEC-001 G2-A real .NET file subset.

Requires frozen original full pairs.json; never materializes a sealed holdout.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil

SCHEMA = "chunkshift.patch-exec-001-g2-population.v1"
FROZEN_PAIRS_SHA256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd"
FAMILIES = ("dotnet-aspnetcore-win-x64", "dotnet-runtime-linux-arm64")
EXPECTED_SELECTED_SHA256 = {
    "train": "a7f1e607820f6a637600953ddb634b63cfd17e7edc55a67bae86f74f54b0c6e2",
    "validation": "79077cbd2e1e6dc2335f98eacd7adb5f1375d497eba66298cff8cbb4d7e0c827",
}
PAIRS = {
    "train": ("10.0.10", "10.0.11"),
    "validation": ("10.0.11", "10.0.12"),
}
# Three target/base size bins, with fixed quotas per .NET family.
BINS = ((65536, 262144, 3), (262144, 1048576, 3),
        (1048576, 4194305, 2))


def sha_bytes(body):
    return hashlib.sha256(body).hexdigest()


def sha_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_path(path):
    if not isinstance(path, str) or not path or "\\" in path or "\x00" in path:
        raise ValueError("unsafe or empty relative corpus path")
    parsed = PurePosixPath(path)
    if (parsed.is_absolute() or str(parsed) != path or
            any(part in ("", ".", "..") for part in path.split("/")) or
            ":" in path):
        raise ValueError(f"unsafe relative corpus path: {path!r}")
    return parsed


def choose_pairs(document, role):
    if role not in PAIRS:
        raise ValueError("unknown G2 role")
    if document.get("schema") != "chunkshift.patch-pairs.v1":
        raise ValueError("wrong frozen pairs schema")
    chosen = []
    for family in FAMILIES:
        hits = [p for p in document.get("pairs", [])
                if p["family"] == family and
                (p["base"], p["target"]) == PAIRS[role]]
        if len(hits) != 1:
            raise ValueError(f"missing or duplicated pinned calibration pair: {family}")
        pair = hits[0]
        selected = []
        for lower, upper, quota in BINS:
            eligible = [r for r in pair["changed"]
                        if lower <= r["baseSize"] < upper and
                        lower <= r["targetSize"] < upper]
            for row in eligible:
                safe_path(row["path"])
            # SHA order is fixed without using measured patch lengths or timings.
            eligible.sort(key=lambda r: (
                sha_bytes(("\0".join((family, pair["base"],
                                     pair["target"], r["path"]))).encode("utf-8")),
                r["path"]))
            if len(eligible) < quota:
                raise ValueError(f"{family}: insufficient untouched bin {lower}:{upper}")
            selected.extend(eligible[:quota])
        if len({r["path"] for r in selected}) != sum(quota for _, _, quota in BINS):
            raise ValueError(f"{family}: duplicate selected paths")
        chosen.append({
            "family": family, "base": pair["base"], "target": pair["target"],
            "changed": sorted(selected, key=lambda r: r["path"]),
            "added": [], "removed": [], "identicalFiles": 0, "identicalBytes": 0
        })
    return chosen


def prepare(original_root, subset_root, role, reference_path):
    original_root = Path(original_root).resolve()
    subset_root = Path(subset_root).resolve()
    reference_bytes = Path(reference_path).read_bytes()
    if sha_bytes(reference_bytes) != FROZEN_PAIRS_SHA256:
        raise ValueError("original frozen pairs.json digest mismatch")
    original_pairs = original_root / "pairs.json"
    if sha_file(original_pairs) != FROZEN_PAIRS_SHA256:
        raise ValueError("materialized original corpus digest mismatch")
    if original_pairs.read_bytes() != reference_bytes:
        raise ValueError("materialized original pairs.json differs from frozen source")
    pairs = choose_pairs(json.loads(reference_bytes), role)
    if subset_root.exists():
        raise ValueError("subset directory must not already exist")
    if original_root == subset_root or original_root in subset_root.parents:
        raise ValueError("subset must not live inside original frozen corpus tree")
    # Validate ALL source paths before materializing any subset entries.
    mappings = []
    for pair in pairs:
        for row in pair["changed"]:
            parts = safe_path(row["path"]).parts
            for version, prefix in ((pair["base"], "base"), (pair["target"], "target")):
                candidate = original_root / "tree" / pair["family"] / version
                source = candidate.joinpath(*parts)
                if not source.is_file() or source.is_symlink():
                    raise ValueError(f"missing or symlink source file: {source}")
                if not source.resolve().is_relative_to(candidate.resolve()):
                    raise ValueError(f"source escapes original tree: {source}")
                if source.stat().st_size != row[prefix + "Size"]:
                    raise ValueError(f"source length mismatch: {source}")
                if sha_file(source) != row[prefix + "Sha256"]:
                    raise ValueError(f"source digest mismatch: {source}")
                target = subset_root / "tree" / pair["family"] / version
                mappings.append((source, target.joinpath(*parts)))
    subset_root.mkdir(parents=True, exist_ok=False)
    try:
        for source, target in mappings:
            target.parent.mkdir(parents=True, exist_ok=True)
            # Hard links are bounded to the same hosted runner volume: no
            # payload copy and no modification of frozen source bytes.
            os.link(source, target)
        subset = {"schema": "chunkshift.patch-pairs.v1", "pairs": pairs}
        payload = (json.dumps(subset, sort_keys=True, indent=2,
                              ensure_ascii=False) + "\n").encode("utf-8")
        (subset_root / "pairs.json").write_bytes(payload)
        lock = sha_bytes(payload)
        if lock != EXPECTED_SELECTED_SHA256[role]:
            raise ValueError("frozen G2 selected-pairs SHA-256 mismatch")
        selection = {
            "schema": SCHEMA, "role": role, "frozenOriginalPairsSha256": FROZEN_PAIRS_SHA256,
            "selectedPairsSha256": lock, "fileCount": sum(len(p["changed"]) for p in pairs),
            "baseAndTargetFilesVerified": len(mappings),
            "binQuotasPerPair": [b[2] for b in BINS],
            "selected": [
                {"family": p["family"], "base": p["base"], "target": p["target"],
                 "paths": [r["path"] for r in p["changed"]]}
                for p in pairs
            ],
        }
        (subset_root / "selection.json").write_text(
            json.dumps(selection, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        return selection
    except BaseException:
        shutil.rmtree(subset_root)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", required=True)
    parser.add_argument("--reference", required=True)
    parser.add_argument("--subset", required=True)
    parser.add_argument("--role", choices=tuple(PAIRS), default="train")
    args = parser.parse_args()
    print(json.dumps(prepare(args.original, args.subset, args.role,
                             args.reference), indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
