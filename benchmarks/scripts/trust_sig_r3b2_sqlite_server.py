#!/usr/bin/env python3
"""TRUST-SIG-001 R3-B2: loopback SQLite reference witness; NOT a protected store.

An attacker who can roll back this service's DB can also roll back its
high-water. All secrets/bootstrap policy are test-only environmental inputs.
"""
from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import sqlite3
import struct
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DOMAIN = b"chunkshift.trust-sig-001.r3b2.read.v0\0"
NAME = re.compile(r"^[a-z][a-z0-9._-]{0,62}$")
SHA = re.compile(r"^[0-9a-f]{64}$")
MAX_BODY = 16384


def reject_duplicates(pairs):
    obj = {}
    for key, value in pairs:
        if key in obj:
            raise ValueError("duplicate JSON field")
        obj[key] = value
    return obj


def parse(raw: bytes):
    if not raw or len(raw) > MAX_BODY:
        raise ValueError("invalid JSON length")
    return json.loads(raw, object_pairs_hook=reject_duplicates)


def canonical(value: dict) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("ascii")


def _integer(value, minimum=0):
    return type(value) is int and minimum <= value <= 2**63 - 1


def validate_checkpoint(c):
    if not isinstance(c, dict) or set(c) != {"root", "cursor"}:
        raise ValueError("checkpoint fields")
    root, cursor = c["root"], c["cursor"]
    if not isinstance(root, dict) or set(root) != {
        "channel", "version", "metadata_sha256", "keys", "threshold", "expires_unix_seconds"
    }:
        raise ValueError("root fields")
    if not isinstance(cursor, dict) or set(cursor) != {
        "channel", "active_tree_sha256", "epoch", "sequence"
    }:
        raise ValueError("cursor fields")
    def name(x):
        return isinstance(x, str) and NAME.fullmatch(x) is not None
    def sha(x):
        return isinstance(x, str) and SHA.fullmatch(x) is not None
    keys = root["keys"]
    if (not name(root["channel"]) or not name(cursor["channel"]) or
        root["channel"] != cursor["channel"] or
        not _integer(root["version"], 1) or not sha(root["metadata_sha256"]) or
        not _integer(root["expires_unix_seconds"], 1) or
        not isinstance(keys, dict) or not (1 <= len(keys) <= 16) or
        not all(name(k) and sha(v) for k, v in keys.items()) or
        not _integer(root["threshold"], 1) or root["threshold"] > len(keys) or
        not sha(cursor["active_tree_sha256"]) or
        not _integer(cursor["epoch"]) or not _integer(cursor["sequence"])):
        raise ValueError("checkpoint validation")
    if len(canonical(c)) > 4096:
        raise ValueError("checkpoint unbounded")
    return c


def strictly_newer(old, proposed):
    if old["root"] != proposed["root"]:
        return False
    a, b = old["cursor"], proposed["cursor"]
    return (a["epoch"], a["sequence"]) < (b["epoch"], b["sequence"]) and (
        a["channel"] == b["channel"] and
        a["active_tree_sha256"] != b["active_tree_sha256"])


def response_mac(key: bytes, nonce: bytes, revision: int, checkpoint: dict) -> str:
    body = canonical(checkpoint)
    payload = DOMAIN + nonce + struct.pack("<Q", revision) + struct.pack("<I", len(body)) + body
    return hmac.new(key, payload, hashlib.sha256).hexdigest()


def connection(path: Path):
    db = sqlite3.connect(str(path), timeout=10, isolation_level=None)
    db.execute("PRAGMA busy_timeout=10000")
    db.execute("PRAGMA synchronous=EXTRA")
    return db


def bootstrap(path: Path, checkpoint: dict):
    validate_checkpoint(checkpoint)
    if path.exists():
        raise ValueError("bootstrap refuses to overwrite existing witness")
    path.parent.mkdir(parents=True, exist_ok=True)
    db = connection(path)
    try:
        db.execute("PRAGMA journal_mode=DELETE")
        db.execute("BEGIN IMMEDIATE")
        db.execute("CREATE TABLE witness (singleton INTEGER PRIMARY KEY CHECK (singleton=1), "
                   "revision INTEGER NOT NULL, document BLOB NOT NULL)")
        db.execute("INSERT INTO witness VALUES (1, 1, ?)", (canonical(checkpoint),))
        db.execute("COMMIT")
    finally:
        db.close()


def current(db):
    row = db.execute("SELECT revision, document FROM witness WHERE singleton=1").fetchone()
    if row is None or not _integer(row[0], 1):
        raise ValueError("corrupt/missing trusted state")
    c = parse(row[1])
    validate_checkpoint(c)
    return row[0], c


def handler_factory(path: Path, mac_key: bytes, write_token: bytes, faults: bool):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass  # no tokens or submitted witness state in CI logs

        def send_json(self, status, obj):
            raw = canonical(obj)
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

        def do_POST(self):
            try:
                n = int(self.headers.get("Content-Length", "0"))
                if n <= 0 or n > MAX_BODY:
                    raise ValueError("bad request length")
                req = parse(self.rfile.read(n))
                if not isinstance(req, dict):
                    raise ValueError("request object required")
                if self.path == "/read":
                    if set(req) != {"nonce"} or not isinstance(req["nonce"], str) or len(req["nonce"]) != 64:
                        raise ValueError("read nonce format")
                    nonce = bytes.fromhex(req["nonce"])
                    db = connection(path)
                    try:
                        revision, checkpoint = current(db)
                    finally:
                        db.close()
                    self.send_json(200, {
                        "revision": revision, "checkpoint": checkpoint,
                        "nonce": req["nonce"],
                        "mac": response_mac(mac_key, nonce, revision, checkpoint)
                    })
                elif self.path == "/cas":
                    authorization = self.headers.get("Authorization", "")
                    expected_auth = b"Bearer " + write_token
                    if not hmac.compare_digest(authorization.encode("ascii"), expected_auth):
                        self.send_json(403, {"error": "writer denied"})
                        return
                    if set(req) != {"expected", "proposed"}:
                        raise ValueError("CAS fields")
                    expected = validate_checkpoint(req["expected"])
                    proposed = validate_checkpoint(req["proposed"])
                    if not strictly_newer(expected, proposed):
                        raise ValueError("wrong root/sequence/base")
                    db = connection(path)
                    try:
                        db.execute("BEGIN IMMEDIATE")
                        revision, existing = current(db)
                        if existing != expected:
                            db.execute("ROLLBACK")
                            self.send_json(409, {"status": "conflict"})
                            return
                        if revision >= 2**63 - 1:
                            raise ValueError("revision overflow")
                        db.execute("UPDATE witness SET revision=?, document=? WHERE singleton=1",
                                   (revision + 1, canonical(proposed)))
                        if faults and self.headers.get("X-Lab-Failpoint") == "before_commit":
                            os._exit(88)
                        db.execute("COMMIT")
                        if faults and self.headers.get("X-Lab-Failpoint") == "after_commit":
                            os._exit(89)
                    finally:
                        db.close()
                    self.send_json(200, {"status": "committed"})
                else:
                    self.send_json(404, {"error": "unknown endpoint"})
            except (ValueError, KeyError, TypeError, UnicodeError, sqlite3.DatabaseError):
                self.send_json(400, {"error": "invalid request or corrupted witness"})

    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", type=Path, required=True)
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--bootstrap", type=Path)
    parser.add_argument("--allow-lab-faults", action="store_true")
    args = parser.parse_args()
    if args.db.exists() and args.bootstrap:
        parser.error("cannot re-bootstrap a current database")
    if args.bootstrap:
        bootstrap(args.db, parse(args.bootstrap.read_bytes()))
    if not args.db.is_file():
        parser.error("missing witness database; explicit bootstrap required")
    mac = bytes.fromhex(os.environ.get("R3B2_MAC_KEY_HEX", ""))
    writer = os.environ.get("R3B2_WRITE_TOKEN", "").encode("ascii")
    if len(mac) != 32 or len(writer) < 32:
        parser.error("ephemeral test keys missing")
    db = connection(args.db)
    try:
        current(db)  # fail closed if unreadable/altered on startup
    finally:
        db.close()
    server = ThreadingHTTPServer(
        ("127.0.0.1", args.port),
        handler_factory(args.db, mac, writer, args.allow_lab_faults))
    print(server.server_port, flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
