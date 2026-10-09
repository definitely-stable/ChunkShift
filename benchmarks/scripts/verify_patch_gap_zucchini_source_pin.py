#!/usr/bin/env python3
"""Verify the frozen upstream Chromium/Zucchini source identity, without building.

Input: GitHub REST commit/tree responses fetched by the GitHub-hosted workflow.
The build must independently verify git HEAD and component tree before compiling.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path

PIN_PATH = Path("docs/research/results/data/PATCH-GAP-001-G4-ZUCCHINI-SOURCE-PIN-20261009-001.json")
SCHEMA = "chunkshift.patch-gap-g4-zucchini-source-pin.v1"


def require(ok: bool, label: str) -> None:
    if not ok:
        raise ValueError("Zucchini upstream source lock: " + label)


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def verify_pin(pin: dict) -> None:
    require(pin["schema"] == SCHEMA, "schema mismatch")
    require(pin["experimentId"] == "PATCH-GAP-001", "experiment mismatch")
    require(pin["protocolCommit"] == "5372678ae8451a71cc95eb24f30855cbbd7e0633",
            "frozen protocol mismatch")
    require(pin["sourceRepository"] == "https://github.com/chromium/chromium", "source repo mismatch")
    require(pin["sourceCommit"] == "26ec7d02bd81e5dc8c48f974d536a3f5fb043dc0",
            "source SHA mismatch")
    require(pin["componentPath"] == "components/zucchini", "component path mismatch")
    require(pin["componentTreeSha"] == "b8e9fb206f712991ac446b2e9a158a5c615fcf81",
            "component tree SHA mismatch")
    require(pin["componentMirrorCommit"] == "667ffb4e19970939936af2e7a169175ae4c1da5b",
            "frozen component mirror commit mismatch")


def sole_tree_entry(doc: dict, path: str, expected_sha: str | None = None) -> dict:
    entries = [e for e in doc["tree"] if e.get("path") == path]
    require(len(entries) == 1, "missing or duplicate tree entry: " + path)
    entry = entries[0]
    require(entry["type"] == "tree" and entry["mode"] == "040000",
            "not a git tree: " + path)
    if expected_sha:
        require(entry["sha"] == expected_sha, "tree SHA mismatch: " + path)
    return entry


def verify_remote(pin: dict, commit: dict, root: dict, components: dict) -> dict:
    verify_pin(pin)
    require(commit["sha"] == pin["sourceCommit"], "commit identity")
    tree_sha = commit["commit"]["tree"]["sha"]
    require(len(tree_sha) == 40 and root["sha"] == tree_sha, "root tree identity")
    parent = sole_tree_entry(root, "components")
    require(components["sha"] == parent["sha"], "components tree identity")
    child = sole_tree_entry(components, "zucchini", pin["componentTreeSha"])
    require(not root.get("truncated") and not components.get("truncated"),
            "truncated git trees")
    return {
        "schema": "chunkshift.patch-gap-g4-zucchini-source-verification.v1",
        "sourceCommit": pin["sourceCommit"],
        "sourceTreeSha": tree_sha,
        "componentsTreeSha": parent["sha"],
        "componentTreeSha": child["sha"],
        "componentMirrorCommit": pin["componentMirrorCommit"],
        "checkedBy": "GitHub official Chromium mirror commit/tree REST API",
        "result": "SOURCE_PIN_PASS",
        "notProven": ["Chromium DEPS resolution", "GN/Ninja/toolchain provenance",
                      "binary hash", "Zucchini runtime behavior", "patch bytes"],
    }


def git(repo: Path, *cmd: str) -> str:
    return subprocess.check_output(["git", "-C", str(repo), *cmd], text=True).strip()


def verify_checkout(pin: dict, checkout: Path) -> dict:
    verify_pin(pin)
    require(git(checkout, "rev-parse", "HEAD") == pin["sourceCommit"],
            "built source checkout is not frozen")
    require(git(checkout, "rev-parse", "HEAD:components/zucchini") ==
            pin["componentTreeSha"], "built component tree is not frozen")
    deps_bytes = subprocess.check_output(["git", "-C", str(checkout),
                                          "show", "HEAD:DEPS"])
    return {
        "sourceCommit": pin["sourceCommit"],
        "componentTreeSha": pin["componentTreeSha"],
        "chromiumDepsSha256": hashlib.sha256(deps_bytes).hexdigest(),
        "chromiumDepsGitBlob": git(checkout, "rev-parse", "HEAD:DEPS"),
        "result": "CHECKOUT_SOURCE_PIN_PASS",
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pin", type=Path, default=PIN_PATH)
    parser.add_argument("--commit-json", type=Path)
    parser.add_argument("--root-tree-json", type=Path)
    parser.add_argument("--components-tree-json", type=Path)
    parser.add_argument("--checkout", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    pin = load(args.pin)
    if args.checkout:
        require(not (args.commit_json or args.root_tree_json or args.components_tree_json),
                "checkout and API inputs are mutually exclusive")
        proof = verify_checkout(pin, args.checkout)
    else:
        require(all((args.commit_json, args.root_tree_json, args.components_tree_json)),
                "three REST source inputs required")
        proof = verify_remote(pin, load(args.commit_json), load(args.root_tree_json),
                              load(args.components_tree_json))
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(proof, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(proof, sort_keys=True))


if __name__ == "__main__":
    main()
