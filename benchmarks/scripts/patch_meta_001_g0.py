"""PATCH-META-001/G0. Lab-only disposable manifest occurrence cache.
Input CSM records MUST have been authoritatively validated by the caller.
Not an integrity oracle, CSM decoder, or measured create accelerator.
"""
from dataclasses import dataclass
import hashlib
import hmac
import json
import os
from pathlib import Path
import tempfile

SCHEMA = "chunkshift.patch-meta.g0.v1"
EXTRACTOR = "occurrences-v1"
MAX_RECORD_BYTES = 8 * 1024 * 1024
MAX_UNIQUE = 25000
MAX_TOTAL = (1 << 63) - 1
SUITES = frozenset(("sha256", "blake3-256"))


def _hex32(value):
    if not isinstance(value, str) or len(value) != 64 or any(
            ch not in "0123456789abcdef" for ch in value):
        raise ValueError("expected lowercase 32-byte hex digest")
    return value


def _canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"),
                      ensure_ascii=True, allow_nan=False).encode("ascii")


def _records_info(records):
    digest = hashlib.sha256()
    total = 0
    for chunk_id, length in records:
        _hex32(chunk_id)
        if type(length) is not int or not 1 <= length <= MAX_TOTAL - total:
            raise ValueError("invalid or overflowing logical chunk length")
        digest.update(bytes.fromhex(chunk_id))
        digest.update(length.to_bytes(8, "big"))
        total += length
    return digest.hexdigest(), total


@dataclass(frozen=True)
class Context:
    manifest_id: str
    profile_fingerprint: str
    hash_suite: str

    def __post_init__(self):
        _hex32(self.manifest_id)
        _hex32(self.profile_fingerprint)
        if self.hash_suite not in SUITES:
            raise ValueError("unsupported suite")


def _key(context, records):
    digest, total = _records_info(records)
    return {"manifestId": context.manifest_id,
            "profileFingerprint": context.profile_fingerprint,
            "hashSuite": context.hash_suite,
            "recordsSha256": digest,
            "chunkCount": len(records), "totalBytes": total,
            "extractor": EXTRACTOR}


def _derive(records):
    """First positions and multiplicities; does not access content bytes."""
    first = {}
    offset = 0
    for ordinal, (chunk_id, length) in enumerate(records):
        token = (chunk_id, length)
        if token not in first:
            if len(first) >= MAX_UNIQUE:
                raise ValueError("unique-chunk cap reached: skip caching")
            first[token] = {"chunkId": chunk_id, "length": length,
                            "firstOrdinal": ordinal, "firstOffset": offset,
                            "count": 0}
        first[token]["count"] += 1
        offset += length
    return {"entries": sorted(first.values(),
                              key=lambda row: (row["chunkId"], row["length"]))}


def _encode(key, features):
    body = {"schema": SCHEMA, "key": key, "features": features}
    return _canonical({"body": body,
                       "sha256": hashlib.sha256(_canonical(body)).hexdigest()}) + b"\n"


def _decode(raw, expected_key):
    if len(raw) > MAX_RECORD_BYTES:
        return None
    try:
        record = json.loads(raw)
        if type(record) is not dict or set(record) != {"body", "sha256"}:
            return None
        body = record["body"]
        if type(body) is not dict or set(body) != {"schema", "key", "features"}:
            return None
        if body["schema"] != SCHEMA or body["key"] != expected_key:
            return None
        stored = record["sha256"]
        if type(stored) is not str or len(stored) != 64 or not hmac.compare_digest(
                stored, hashlib.sha256(_canonical(body)).hexdigest()):
            return None
        features = body["features"]
        if type(features) is not dict or set(features) != {"entries"} or type(
                features["entries"]) is not list:
            return None
        entries = features["entries"]
        if len(entries) > MAX_UNIQUE:
            return None
        previous = None
        occurrence_count = 0
        for row in entries:
            if type(row) is not dict or set(row) != {
                    "chunkId", "length", "firstOrdinal", "firstOffset", "count"}:
                return None
            if any(type(row[k]) is not int for k in (
                    "length", "firstOrdinal", "firstOffset", "count")):
                return None
            token = (_hex32(row["chunkId"]), row["length"])
            if previous is not None and token <= previous:
                return None
            previous = token
            if not (1 <= row["length"] <= MAX_TOTAL
                    and 0 <= row["firstOrdinal"] < expected_key["chunkCount"]
                    and 0 <= row["firstOffset"] < expected_key["totalBytes"]
                    and 1 <= row["count"] <= expected_key["chunkCount"]):
                return None
            occurrence_count += row["count"]
        if occurrence_count != expected_key["chunkCount"]:
            return None
        return features
    except (ValueError, TypeError, KeyError, UnicodeDecodeError,
            OverflowError, RecursionError):
        return None


def load_or_build(path: Path, context: Context, records):
    """Return (ADVISORY features, HIT/REBUILT), with safe fallback.

    Record SHA-256 detects accidental damage, NOT a malicious forgery.
    Publication must continue to use existing exact content verification.
    """
    key = _key(context, records)
    try:
        with path.open("rb") as stream:
            raw = stream.read(MAX_RECORD_BYTES + 1)
    except (FileNotFoundError, IsADirectoryError, PermissionError, OSError):
        raw = b""
    cached = _decode(raw, key)
    if cached is not None:
        return cached, "HIT"
    try:
        fresh = _derive(records)
    except ValueError as error:
        if str(error).startswith("unique-chunk cap reached"):
            # Unsupported lab size: caller must use its authoritative, uncached path.
            return None, "BYPASS"
        raise
    encoded = _encode(key, fresh)
    if len(encoded) <= MAX_RECORD_BYTES:
        temporary = None
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(
                    dir=path.parent, prefix=".meta-g0-", delete=False) as stream:
                temporary = Path(stream.name)
                stream.write(encoded)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, path)
        except OSError:
            # Disposable cache I/O cannot prevent creating authoritative output.
            pass
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)
    return fresh, "REBUILT"
