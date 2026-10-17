#!/usr/bin/env python3
"""TRUST-SIG-001 R2-B2: real python-tuf 7.0.1 Updater adversarial pilot.

The upstream v7.0.1 reference RepositorySimulator provides an in-memory
signed TUF repository and FetcherInterface, while actual tuf.ngclient.Updater
parses, cryptographically verifies, caches, and downloads. No fake verifier,
monkeypatching of Updater, actual network, CSM/CSP production changes, or
claims of durable local anti-rollback. All signing keys ephemeral.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import importlib.metadata
import json
from pathlib import Path
import platform
import sys
import tempfile

from tuf.api.exceptions import (
    BadVersionNumberError,
    DownloadError,
    ExpiredMetadataError,
    LengthOrHashMismatchError,
    UnsignedMetadataError,
)
from tuf.api.metadata import Metadata, Root, Snapshot, Timestamp
from tuf.api.serialization.json import JSONSerializer
from tuf.ngclient import Updater


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise AssertionError(reason)


def assert_reject(error_types: tuple[type[Exception], ...],
                  work, label: str) -> str:
    try:
        work()
    except error_types as exc:
        return type(exc).__name__
    raise AssertionError(f"{label}: MALICIOUS UPDATE ACCEPTED")


def candidate_client(sim, directory: Path, *, bootstrap: bool) -> Updater:
    return Updater(
        str(directory / "trusted-metadata"),
        "https://example.invalid/metadata/",
        str(directory / "downloaded-targets"),
        "https://example.invalid/targets/",
        sim,
        bootstrap=sim.signed_roots[0] if bootstrap else None,
    )


def baseline(sim, directory: Path) -> None:
    candidate_client(sim, directory, bootstrap=True).refresh()
    require((directory / "trusted-metadata/root.json").is_file(),
            "Trusted root was not persisted")


def happy_target(sim, directory: Path) -> dict:
    payload = b"chunkshift-r2b2-demo-csp-bytes-not-an-actual-patch"
    sim.add_target("targets", payload, "update.csp")
    sim.targets.version += 1
    sim.compute_metafile_hashes_length = True
    sim.update_snapshot()
    updater = candidate_client(sim, directory, bootstrap=True)
    updater.refresh()
    target = updater.get_targetinfo("update.csp")
    require(target is not None, "Trusted target lookup absent")
    output = Path(updater.download_target(target))
    require(output.read_bytes() == payload, "Verified target bytes differ")
    require(target.hashes["sha256"] == hashlib.sha256(payload).hexdigest(),
            "Trusted target SHA-256 mismatch")
    return {"targetBytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest()}


def timestamp_rollback(sim, directory: Path) -> str:
    baseline(sim, directory)
    # A real, correctly signed timestamp version 2 is accepted and cached.
    sim.update_timestamp()
    candidate_client(sim, directory, bootstrap=False).refresh()
    require(Metadata.from_file(directory / "trusted-metadata/timestamp.json").signed.version == 2,
            "The high-water timestamp was not cached")
    # Malicious mirror now serves signed but lower version. This is *not* a
    # malformed signature: it is a signed rollback against client cache.
    sim.timestamp.version = 1
    error = assert_reject((BadVersionNumberError,),
        lambda: candidate_client(sim, directory, bootstrap=False).refresh(),
        "signed timestamp rollback")
    require(Metadata.from_file(directory / "trusted-metadata/timestamp.json").signed.version == 2,
            "Rollback polluted trusted timestamp high-water")
    return error


def expired_timestamp(sim, directory: Path) -> str:
    baseline(sim, directory)
    sim.timestamp.expires = dt.datetime.now(dt.timezone.utc) - dt.timedelta(days=1)
    return assert_reject((ExpiredMetadataError,),
        lambda: candidate_client(sim, directory, bootstrap=False).refresh(),
        "expired timestamp freeze")


def forged_targets(sim, directory: Path) -> str:
    baseline(sim, directory)
    sim.add_target("targets", b"forged but unsigned", "update.csp")
    sim.targets.version += 1
    sim.update_snapshot()
    # Attacker cannot generate authorized targets signature.
    sim.signers["targets"].clear()
    return assert_reject((UnsignedMetadataError,),
        lambda: candidate_client(sim, directory, bootstrap=False).refresh(),
        "unsigned targets from malicious mirror")


def targets_snapshot_mix_and_match(sim, directory: Path) -> str:
    sim.compute_metafile_hashes_length = True
    sim.add_target("targets", b"legit-contents", "update.csp")
    sim.targets.version += 1
    sim.update_snapshot()
    baseline(sim, directory)
    require(sim.snapshot.meta["targets.json"].hashes is not None,
            "Snapshot must bind exact target metadata hash")
    # Changed signed targets, but snapshot continues advertising the original
    # signed bytes/hash. Keep advertised version equal to *only* isolate hash.
    sim.add_target("targets", b"changed signed target contents", "update.csp")
    sim.targets.version += 1
    sim.snapshot.meta["targets.json"].version = sim.targets.version
    sim.snapshot.version += 1
    sim.update_timestamp()
    return assert_reject((LengthOrHashMismatchError,),
        lambda: candidate_client(sim, directory, bootstrap=False).refresh(),
        "snapshot/targets mix and match")


def corrupted_target_download(sim, directory: Path) -> str:
    data = b"correctly signed chunkshift target data"
    sim.add_target("targets", data, "update.csp")
    sim.targets.version += 1
    sim.update_snapshot()
    updater = candidate_client(sim, directory, bootstrap=True)
    updater.refresh()
    target = updater.get_targetinfo("update.csp")
    require(target is not None, "Signed target metadata absent")
    # Compromised mirror returns SAME-LENGTH bad target bytes, preserving
    # TUF's signed target metadata/hash entry.
    sim.target_files["update.csp"].data = bytes([data[0] ^ 1]) + data[1:]
    return assert_reject((LengthOrHashMismatchError, DownloadError),
        lambda: updater.download_target(target),
        "tampered downloaded target")


def broken_root_rotation(sim, directory: Path) -> str:
    # Changing key material with no old threshold must never be accepted.
    sim.rotate_keys(Root.type)
    sim.root.version += 1
    sim.publish_root()  # signed by new keys, NOT old trusted root
    error = assert_reject((UnsignedMetadataError,),
        lambda: candidate_client(sim, directory, bootstrap=True).refresh(),
        "untrusted new-only root")
    persisted = Metadata.from_file(directory / "trusted-metadata/root.json")
    require(persisted.signed.version == 1, "Untrusted root replaced pinned root")
    return error


def valid_root_rotation(sim, directory: Path) -> dict:
    old = list(sim.signers[Root.type].values())
    initial_ids = set(sim.root.roles[Root.type].keyids)
    sim.rotate_keys(Root.type)
    next_ids = set(sim.root.roles[Root.type].keyids)
    require(initial_ids.isdisjoint(next_ids), "Root key did not rotate")
    sim.root.version += 1
    # Sign the SAME exact v2 Root metadata with BOTH the previous and the
    # prospective threshold; do not call publish_root(), which only knows
    # about the current signer set.
    sim.md_root.signatures.clear()
    for signer in [*old, *sim.signers[Root.type].values()]:
        sim.md_root.sign(signer, append=True)
    signed_new_root = sim.md_root.to_bytes(JSONSerializer())
    sim.signed_roots.append(signed_new_root)
    candidate_client(sim, directory, bootstrap=True).refresh()
    trusted = Metadata.from_file(directory / "trusted-metadata/root.json")
    require(trusted.signed.version == 2, "Dual-signed new root not persisted")
    require(set(trusted.signed.roles[Root.type].keyids) == next_ids,
            "Trusted root did not replace key set")
    return {"trustedRootVersion": 2,
            "oldRootKeys": len(initial_ids), "newRootKeys": len(next_ids)}


def rollback_root_chain(sim, directory: Path) -> str:
    # Repository skips v2 and presents a v3 root as if it were v2.
    sim.root.version += 2
    sim.publish_root()
    return assert_reject((BadVersionNumberError,),
        lambda: candidate_client(sim, directory, bootstrap=True).refresh(),
        "non-consecutive root version")


def run_scenario(name: str, scenario, simulator_type):
    with tempfile.TemporaryDirectory(prefix="chunkshift-tuf-r2b2-") as tmp:
        return {"case": name, "verdict": "pass",
                "observation": scenario(simulator_type(), Path(tmp))}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--upstream", required=True,
        help="Directory of pinned python-tuf v7.0.1 upstream checkout")
    parser.add_argument("--output", required=True)
    options = parser.parse_args()

    upstream = Path(options.upstream).resolve()
    require((upstream / "tests/repository_simulator.py").is_file(),
            "Pinned upstream RepositorySimulator missing")
    sys.path.insert(0, str(upstream))
    from tests.repository_simulator import RepositorySimulator

    require(importlib.metadata.version("tuf") == "7.0.1",
            "TUF client version is not pinned 7.0.1")
    scenarios = [
        ("happy_verified_target", happy_target),
        ("timestamp_rollback", timestamp_rollback),
        ("expired_timestamp", expired_timestamp),
        ("forged_targets", forged_targets),
        ("snapshot_mix_and_match", targets_snapshot_mix_and_match),
        ("corrupted_target_download", corrupted_target_download),
        ("root_new_keys_without_old_threshold", broken_root_rotation),
        ("valid_dual_signed_root_rotation", valid_root_rotation),
        ("nonconsecutive_root_version", rollback_root_chain),
    ]
    results = [run_scenario(name, callback, RepositorySimulator)
               for name, callback in scenarios]
    require(len(results) == 9 and len({x["case"] for x in results}) == 9,
            "Unexpected TUF scenario coverage")
    evidence = {
        "schema": "chunkshift.trust-sig-001.r2b2.real-tuf-updater.v0",
        "decision": "REAL_TUF_NGCLIENT_LAB_ONLY_NOT_DISTRIBUTION_ADOPT",
        "client": "tuf.ngclient.Updater",
        "clientVersion": importlib.metadata.version("tuf"),
        "upstreamSimulatorCommit": "49eb1da48c58cf0cbb0f2e490f7ad39fbd8c1f15",
        "simulator": "upstream reference in-memory signed repository, NOT mock verifier",
        "os": platform.platform(),
        "architecture": platform.machine(),
        "python": platform.python_version(),
        "networkDownloads": False,
        "localRollbackProtectedStore": False,
        "trustedClockHardening": False,
        "actualDiskCrash": False,
        "sourceKeysArchived": False,
        "cases": results
    }
    out = Path(options.output).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(evidence, indent=2, sort_keys=True)+"\n",
                   encoding="utf-8")
    print("TRUST_SIG_R2B2_REAL_TUF_CLIENT_PASS", len(results), out)


if __name__ == "__main__":
    main()
