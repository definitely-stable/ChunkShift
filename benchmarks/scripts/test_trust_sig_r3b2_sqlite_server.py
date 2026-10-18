#!/usr/bin/env python3
"""R3-B2 real subprocess/SQLite protocol tests, including security NO-GO probes."""
from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
import copy
import hashlib
import hmac
import http.client
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from urllib import error, request


DOMAIN = b"chunkshift.trust-sig-001.r3b2.read.v0\0"
SERVER = Path(__file__).with_name("trust_sig_r3b2_sqlite_server.py")


def initial():
    return {
        "root": {
            "channel": "stable", "version": 1, "metadata_sha256": "a" * 64,
            "keys": {"key-a": "b" * 64}, "threshold": 1,
            "expires_unix_seconds": 1890000000
        },
        "cursor": {
            "channel": "stable", "active_tree_sha256": "0" * 64,
            "epoch": 0, "sequence": 1
        }
    }


def next_checkpoint(old, char, seq):
    c = copy.deepcopy(old)
    c["cursor"]["active_tree_sha256"] = char * 64
    c["cursor"]["sequence"] = seq
    return c


def canon(x):
    return json.dumps(x, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()


def verify(receipt, key: bytes, expected_nonce: str):
    # Independent client-side implementation, does NOT import server code.
    if set(receipt) != {"checkpoint", "revision", "nonce", "mac"}:
        raise ValueError("receipt shape")
    if not isinstance(receipt["nonce"], str) or receipt["nonce"] != expected_nonce:
        raise ValueError("stale challenge")
    if not isinstance(receipt["revision"], int) or type(receipt["revision"]) is bool or receipt["revision"] < 1:
        raise ValueError("revision")
    if not isinstance(receipt["mac"], str) or len(receipt["mac"]) != 64:
        raise ValueError("MAC shape")
    payload = canon(receipt["checkpoint"])
    mac = hmac.new(key, DOMAIN + bytes.fromhex(expected_nonce)
                   + struct.pack("<Q", receipt["revision"])
                   + struct.pack("<I", len(payload)) + payload,
                   hashlib.sha256).hexdigest()
    if not hmac.compare_digest(mac, receipt["mac"]):
        raise ValueError("invalid MAC or pinned key")
    return receipt["checkpoint"]


class WitnessHost(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="r3b2-")
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.db = root / "separate-witness-service" / "witness.sqlite"
        self.install = root / "attacker-writable-install"
        self.install.mkdir()
        self.bootstrap = root / "bootstrap.json"
        self.bootstrap.write_bytes(canon(initial()))
        self.mac_key = os.urandom(32)
        self.writer = os.urandom(32).hex()
        self.server = None
        self.url = None
        self.addCleanup(self.stop)
        self.start(bootstrap=True)

    def start(self, bootstrap=False):
        cmd = [sys.executable, "-u", str(SERVER), "--db", str(self.db),
               "--port", "0", "--allow-lab-faults"]
        if bootstrap:
            cmd += ["--bootstrap", str(self.bootstrap)]
        env = dict(os.environ, R3B2_MAC_KEY_HEX=self.mac_key.hex(),
                   R3B2_WRITE_TOKEN=self.writer)
        self.server = subprocess.Popen(cmd, stdout=subprocess.PIPE,
                                       stderr=subprocess.PIPE, env=env,
                                       text=True)
        line = self.server.stdout.readline().strip()
        if not line.isdigit():
            err = self.server.stderr.read()
            raise RuntimeError(f"server failed startup: {err[:500]}")
        self.url = f"http://127.0.0.1:{int(line)}"

    def stop(self):
        if self.server is not None:
            if self.server.poll() is None:
                self.server.terminate()
                try:
                    self.server.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    self.server.kill()
                    self.server.wait(timeout=5)
            self.server.stdout.close()
            self.server.stderr.close()
            self.server = None

    def call(self, endpoint, document, token=None, fault=None):
        headers = {"Content-Type": "application/json"}
        if token is not None:
            headers["Authorization"] = f"Bearer {token}"
        if fault:
            headers["X-Lab-Failpoint"] = fault
        req = request.Request(self.url + endpoint, data=canon(document),
                              headers=headers, method="POST")
        try:
            with request.urlopen(req, timeout=5) as resp:
                return resp.status, json.load(resp)
        except error.HTTPError as exc:
            with exc:
                return exc.code, json.load(exc)

    def read(self):
        nonce = os.urandom(32).hex()
        status, receipt = self.call("/read", {"nonce": nonce})
        self.assertEqual(200, status)
        return verify(receipt, self.mac_key, nonce), receipt

    def cas(self, old, new, token=None, fault=None):
        return self.call("/cas", {"expected": old, "proposed": new},
                         token=self.writer if token is None else token, fault=fault)

    def test_real_authenticated_readback_and_persistent_commit(self):
        before, receipt = self.read()
        self.assertEqual(initial(), before)
        next_one = next_checkpoint(before, "1", 2)
        self.assertEqual(200, self.cas(before, next_one)[0])
        after, receipt = self.read()
        self.assertEqual(next_one, after)
        self.assertEqual(2, receipt["revision"])
        self.stop()
        self.start()
        restarted, later = self.read()
        self.assertEqual(next_one, restarted)
        self.assertEqual(2, later["revision"])

    def test_bad_writer_unauthorized_and_readonly_probe(self):
        old, _ = self.read()
        target = next_checkpoint(old, "1", 2)
        self.assertEqual(403, self.cas(old, target, token="wrong")[0])
        self.assertEqual(403, self.call("/cas", {"expected": old, "proposed": target})[0])
        self.assertEqual(old, self.read()[0])

    def test_nonce_replay_and_modified_body_mac_rejected(self):
        value, receipt = self.read()
        with self.assertRaises(ValueError):
            verify(receipt, self.mac_key, os.urandom(32).hex())
        with self.assertRaises(ValueError):
            verify(receipt, os.urandom(32), receipt["nonce"])
        altered = copy.deepcopy(receipt)
        altered["checkpoint"]["cursor"]["active_tree_sha256"] = "f" * 64
        with self.assertRaises(ValueError):
            verify(altered, self.mac_key, altered["nonce"])
        rev = copy.deepcopy(receipt)
        rev["revision"] += 1
        with self.assertRaises(ValueError):
            verify(rev, self.mac_key, receipt["nonce"])

    def test_schema_conflicting_state_and_nonmonotonic_rejected(self):
        old, _ = self.read()
        new = next_checkpoint(old, "1", 2)
        self.assertEqual(400, self.cas(old, old)[0])
        bad_root = copy.deepcopy(new)
        bad_root["root"]["metadata_sha256"] = "b" * 64
        self.assertEqual(400, self.cas(old, bad_root)[0])
        malformed = copy.deepcopy(new)
        malformed["cursor"]["epoch"] = True
        self.assertEqual(400, self.cas(old, malformed)[0])
        self.assertEqual(200, self.cas(old, new)[0])
        self.assertEqual(409, self.cas(old, next_checkpoint(old, "2", 2))[0])
        self.assertEqual(new, self.read()[0])

    def test_concurrent_http_cas_has_exactly_one_winner(self):
        old, _ = self.read()
        candidates = [next_checkpoint(old, "1", 2), next_checkpoint(old, "2", 2)]
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda target: self.cas(old, target)[0], candidates))
        self.assertEqual([200, 409], sorted(results))
        accepted, _ = self.read()
        self.assertIn(accepted, candidates)

    def test_kill_in_uncommitted_transaction_rolls_back(self):
        old, _ = self.read()
        try:
            self.cas(old, next_checkpoint(old, "1", 2), fault="before_commit")
        except (error.URLError, ConnectionError, http.client.RemoteDisconnected):
            pass
        self.server.wait(timeout=5)
        self.stop()
        self.start()
        self.assertEqual(old, self.read()[0])

    def test_ack_lost_after_sqlite_commit_recovers_new_state(self):
        old, _ = self.read()
        after = next_checkpoint(old, "1", 2)
        try:
            self.cas(old, after, fault="after_commit")
        except (error.URLError, ConnectionError, http.client.RemoteDisconnected):
            pass
        self.server.wait(timeout=5)
        self.stop()
        self.start()
        fresh, receipt = self.read()
        self.assertEqual(after, fresh)
        self.assertEqual(2, receipt["revision"])

    def test_attacker_can_rollback_install_directory_not_separate_db(self):
        (self.install / "current").write_text("old")
        before, _ = self.read()
        after = next_checkpoint(before, "1", 2)
        self.assertEqual(200, self.cas(before, after)[0])
        (self.install / "current").write_text("malicious-old")
        self.stop()
        self.start()
        self.assertEqual(after, self.read()[0])
        self.assertEqual("malicious-old", (self.install / "current").read_text())

    def test_negative_counterexample_same_service_db_rollback_undetected(self):
        # Deliberate NO-GO: if attacker can rewrite the witness DB, SQLite
        # and the SAME persisted MAC key do not supply monotonic high-water.
        old, _ = self.read()
        self.stop()
        snapshot = Path(self.temp.name) / "attacker-backup.sqlite"
        shutil.copy2(self.db, snapshot)
        self.start()
        after = next_checkpoint(old, "1", 2)
        self.assertEqual(200, self.cas(old, after)[0])
        self.stop()
        shutil.copy2(snapshot, self.db)
        self.start()
        rolled_back, receipt = self.read()
        self.assertEqual(old, rolled_back)
        self.assertEqual(1, receipt["revision"])
        # Real MAC validates even though high-water regressed. This is NOT
        # a test failure; it falsifies claims of protected anti-rollback.
        self.assertEqual(old, verify(receipt, self.mac_key, receipt["nonce"]))

    def test_restart_without_database_is_fail_closed(self):
        self.stop()
        self.db.unlink()
        cmd = [sys.executable, str(SERVER), "--db", str(self.db)]
        env = dict(os.environ, R3B2_MAC_KEY_HEX=self.mac_key.hex(),
                   R3B2_WRITE_TOKEN=self.writer)
        result = subprocess.run(cmd, env=env, capture_output=True, text=True, timeout=10)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("explicit bootstrap required", result.stderr)


if __name__ == "__main__":
    unittest.main()
