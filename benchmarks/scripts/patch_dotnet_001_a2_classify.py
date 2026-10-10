#!/usr/bin/env python3
"""PATCH-DOTNET-001 A2 independent PE/CLR structural classification.

Reads the exact A1 extracted files and inventory. Never encodes, decodes, or
measures patches. PE/CLR classification is structural; it does not certify
full metadata-table validity or IL operand scan eligibility.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import os
import struct
from pathlib import Path

SCHEMA = "chunkshift.patch-dotnet-a2-lock.v1"
OUTPUT_SCHEMA = "chunkshift.patch-dotnet-a2-classification.v1"
MAX_HEADER_OFFSET = 1024 * 1024
MACHINE = {0x014C: "x86", 0x8664: "x64", 0xAA64: "arm64", 0x01C4: "arm"}
READYTORUN_MAGIC = b"RTR\x00"
MAX_METADATA_VERSION = 1024
MAX_SECTIONS = 96
MAX_METADATA_STREAMS = 32


class AuditError(ValueError):
    pass


class ParseError(ValueError):
    pass


def require(ok: bool, msg: str) -> None:
    if not ok:
        raise AuditError(msg)


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def file_hash(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def canon_json(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode()


def read_at(handle, length: int, offset: int, count: int) -> bytes:
    if offset < 0 or count < 0 or offset > length - count:
        raise ParseError("STRUCT_BOUNDS")
    handle.seek(offset)
    out = handle.read(count)
    if len(out) != count:
        raise ParseError("STRUCT_SHORT_READ")
    return out


def u16(x: bytes, offset: int = 0) -> int:
    return struct.unpack_from("<H", x, offset)[0]


def u32(x: bytes, offset: int = 0) -> int:
    return struct.unpack_from("<I", x, offset)[0]


def result(kind: str, reason: str, arch: str = "unknown", machine: int = 0,
           has_native: bool = False) -> dict:
    return {"kind": kind, "reason": reason, "architecture": arch,
            "machine": machine, "managedNativeHeader": has_native}


def parse_pe(handle, length: int, cap: int) -> dict:
    if length < 64:
        return result("MALFORMED", "DOS_HEADER_SHORT")
    dos = read_at(handle, length, 0, 64)
    offset = u32(dos, 0x3C)
    if offset < 64 or offset > MAX_HEADER_OFFSET or offset > length - 24:
        return result("MALFORMED", "PE_OFFSET_INVALID")
    hdr = read_at(handle, length, offset, 24)
    if hdr[:4] != b"PE\x00\x00":
        return result("MALFORMED", "PE_SIGNATURE_INVALID")

    machine, sections, _, _, _, optional_size, _ = struct.unpack_from("<HHIIIHH", hdr, 4)
    arch = MACHINE.get(machine, "unknown")
    if sections == 0 or sections > MAX_SECTIONS or optional_size < 96 or optional_size > 512:
        return result("MALFORMED", "PE_COFF_BOUNDS", arch, machine)
    if length > cap:
        return result("UNSUPPORTED", "IMAGE_SIZE_CAP", arch, machine)
    opt_offset = offset + 24
    opt = read_at(handle, length, opt_offset, optional_size)
    magic = u16(opt)
    if magic == 0x20B:
        dirs_start, count_offset, min_size = 112, 108, 112
    elif magic == 0x10B:
        dirs_start, count_offset, min_size = 96, 92, 96
    else:
        return result("UNSUPPORTED", "PE_OPTIONAL_MAGIC", arch, machine)
    if optional_size < min_size:
        return result("MALFORMED", "PE_OPTIONAL_TRUNCATED", arch, machine)
    num_directories = u32(opt, count_offset)
    if num_directories > (optional_size - dirs_start) // 8:
        return result("MALFORMED", "PE_DIRECTORIES_BOUNDS", arch, machine)

    raw_sections = read_at(handle, length, opt_offset + optional_size, sections * 40)
    mapped = []
    for i in range(sections):
        sec = raw_sections[40 * i:40 * (i + 1)]
        virtual_size, rva, raw_size, file_offset = struct.unpack_from("<IIII", sec, 8)
        if raw_size and (file_offset > length or raw_size > length - file_offset):
            return result("MALFORMED", "PE_SECTION_FILE_BOUNDS", arch, machine)
        mapped.append((rva, raw_size, file_offset, virtual_size))

    def rva_file_offset(rva: int, size: int) -> int:
        matches = []
        for section_rva, raw_size, file_offset, _ in mapped:
            delta = rva - section_rva
            if delta >= 0 and size <= raw_size and delta <= raw_size - size:
                matches.append(file_offset + delta)
        if len(matches) != 1:
            raise ParseError("PE_RVA_UNMAPPED_OR_AMBIGUOUS")
        return matches[0]

    if num_directories <= 14:
        return result("PE_NATIVE_UNKNOWN", "NO_CLR_DIRECTORY", arch, machine)
    clr_rva, clr_size = struct.unpack_from("<II", opt, dirs_start + 14 * 8)
    if clr_rva == 0 and clr_size == 0:
        return result("PE_NATIVE_UNKNOWN", "NO_CLR_DIRECTORY", arch, machine)
    if clr_rva == 0 or clr_size < 72:
        return result("MALFORMED", "CLR_DIRECTORY_BOUNDS", arch, machine)
    try:
        clr = read_at(handle, length, rva_file_offset(clr_rva, 72), 72)
    except ParseError:
        return result("MALFORMED", "CLR_DIRECTORY_UNMAPPED", arch, machine)
    cb = u32(clr, 0)
    if cb < 72 or cb > clr_size:
        return result("MALFORMED", "CLR_HEADER_SIZE", arch, machine)
    metadata_rva, metadata_size = struct.unpack_from("<II", clr, 8)
    if not metadata_rva or metadata_size < 20:
        return result("UNSUPPORTED", "CLR_METADATA_MISSING", arch, machine)
    try:
        metadata_off = rva_file_offset(metadata_rva, metadata_size)
        root = read_at(handle, length, metadata_off, 20)
        if root[:4] != b"BSJB":
            return result("MALFORMED", "CLR_METADATA_SIGNATURE", arch, machine)
        version_len = u32(root, 12)
        if version_len <= 0 or version_len > MAX_METADATA_VERSION:
            return result("MALFORMED", "CLR_METADATA_VERSION_BOUNDS", arch, machine)
        streams_header = 16 + ((version_len + 3) // 4) * 4
        if streams_header + 4 > metadata_size:
            return result("MALFORMED", "CLR_METADATA_STREAM_HEADER_BOUNDS", arch, machine)
        stream_fields = read_at(handle, length, metadata_off + streams_header, 4)
        stream_count = u16(stream_fields, 2)
        if stream_count < 1 or stream_count > MAX_METADATA_STREAMS:
            return result("MALFORMED", "CLR_METADATA_STREAM_COUNT", arch, machine)
        cur = streams_header + 4
        stream_names: set[str] = set()
        for _ in range(stream_count):
            if cur + 12 > metadata_size:
                return result("MALFORMED", "CLR_METADATA_STREAM_TRUNCATED", arch, machine)
            # All stream header fields are in the metadata root (bounded read).
            stream_off, stream_size = struct.unpack("<II", read_at(handle, length, metadata_off + cur, 8))
            if stream_off > metadata_size or stream_size > metadata_size - stream_off:
                return result("MALFORMED", "CLR_METADATA_STREAM_BOUNDS", arch, machine)
            name_raw = read_at(handle, length, metadata_off + cur + 8,
                               min(36, metadata_size - cur - 8))
            nul = name_raw.find(b"\x00")
            if nul < 1 or nul > 32:
                return result("MALFORMED", "CLR_METADATA_STREAM_NAME", arch, machine)
            name = name_raw[:nul].decode("ascii", errors="replace")
            if name in stream_names:
                return result("MALFORMED", "CLR_METADATA_DUPLICATE_STREAM", arch, machine)
            stream_names.add(name)
            cur += 8 + ((nul + 1 + 3) // 4) * 4
        if not ({"#~", "#-"} & stream_names):
            return result("UNSUPPORTED", "CLR_TABLE_STREAM_MISSING", arch, machine)
    except ParseError:
        return result("MALFORMED", "CLR_METADATA_UNMAPPED", arch, machine)

    flags = u32(clr, 16)
    native_rva, native_size = struct.unpack_from("<II", clr, 64)
    has_native = bool(native_rva or native_size)
    if has_native:
        if native_rva == 0 or native_size < 16:
            return result("UNSUPPORTED", "CLR_NATIVE_HEADER_SIZE", arch, machine, True)
        try:
            signature = read_at(handle, length, rva_file_offset(native_rva, 4), 4)
        except ParseError:
            return result("MALFORMED", "CLR_NATIVE_HEADER_UNMAPPED", arch, machine, True)
        if signature == READYTORUN_MAGIC:
            return result("R2R_HEADER", "RTR_SIGNATURE_ONLY_NO_FIXUP_VALIDATION", arch, machine, True)
        return result("UNSUPPORTED", "CLR_UNKNOWN_NATIVE_HEADER", arch, machine, True)

    if machine not in MACHINE:
        return result("UNSUPPORTED", "PE_MACHINE_UNSUPPORTED", arch, machine)
    if flags & 1:
        return result("ILONLY", "CLR_IL_ONLY_STRUCTURAL", arch, machine)
    return result("MIXED_MODE", "CLR_MIXED_MODE", arch, machine)


def classify(path: Path, cap: int) -> dict:
    length = path.stat().st_size
    with path.open("rb") as stream:
        magic = stream.read(4)
        if magic[:2] == b"MZ":
            try:
                return parse_pe(stream, length, cap)
            except (ParseError, ValueError, struct.error, UnicodeError):
                return result("MALFORMED", "PE_PARSE_ERROR")
        if magic == b"\x7fELF":
            return result("ELF_NATIVE_UNKNOWN", "NO_MANAGED_PE", "unknown")
        if magic in {b"\xfe\xed\xfa\xce", b"\xce\xfa\xed\xfe",
                     b"\xfe\xed\xfa\xcf", b"\xcf\xfa\xed\xfe",
                     b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"}:
            return result("MACHO_NATIVE_UNKNOWN", "NO_MANAGED_PE")
        return result("OTHER", "NOT_PE_OR_NATIVE_MAGIC")


def load_and_verify(root: Path, plan: Path, lock: dict) -> tuple[list[dict], list[dict]]:
    require(lock.get("schema") == SCHEMA and lock.get("experimentId") == "PATCH-DOTNET-001",
            "A2_LOCK_SCHEMA")
    plan_bytes = plan.read_bytes()
    require(sha256(plan_bytes) == lock["planSha256"], "A2_PLAN_HASH_MISMATCH")
    doc = json.loads(plan_bytes)
    assert doc["schema"] == "chunkshift.patch-dotnet-corpus-plan.v1"
    planned = {g["id"]: g for g in doc["groups"]}
    for name, expected in [("files.jsonl", lock["filesSha256"]), ("pairs.json", lock["pairsSha256"])]:
        require(file_hash(root / name) == expected, f"A2_{name}_HASH_MISMATCH")
    audit = json.loads((root / "audit.json").read_text())
    require(audit.get("planSha256") == lock["planSha256"] and
            audit.get("filesSha256") == lock["filesSha256"] and
            audit.get("pairsSha256") == lock["pairsSha256"], "A2_A1_PROVENANCE_MISMATCH")
    rows = [json.loads(line) for line in (root / "files.jsonl").read_text().splitlines()]
    require(len(rows) == lock["expectedTotalFileRows"], "A2_A1_ROW_COUNT")
    pairs_doc = json.loads((root / "pairs.json").read_text())
    require(pairs_doc.get("schema") == "chunkshift.patch-pairs.v1", "A2_A1_PAIR_SCHEMA")
    pairs = pairs_doc["pairs"]
    seen: set[tuple[str, str, str]] = set()
    for row in rows:
        family, version, name = row["family"], row["version"], row["path"]
        require(family in planned and version in
                {planned[family]["base"], planned[family]["target"]}, "A2_UNKNOWN_FAMILY_VERSION")
        require(row["role"] == planned[family]["role"], "A2_ROLE_DRIFT")
        require(isinstance(name, str) and name and
                all(part not in {"", ".", ".."} for part in name.split("/")) and
                "\\" not in name and ":" not in name, "A2_UNSAFE_RELATIVE_PATH")
        require(row["candidateScope"] == any(
            name.startswith(prefix) for prefix in planned[family]["candidateAllowPrefixes"]),
            "A2_SCOPE_DRIFT")
        key = (family, version, name)
        require(key not in seen, "A2_DUPLICATE_FILE_ROW")
        seen.add(key)
    return rows, pairs


def analyze(root: Path, plan: Path, lock_file: Path, output: Path) -> dict:
    lock = json.loads(lock_file.read_text())
    rows, pairs = load_and_verify(root, plan, lock)
    require(not output.exists(), "A2_OUTPUT_EXISTS")
    by_key: dict[tuple[str, str, str], dict] = {}
    safe_root = (root / "tree").resolve(strict=True)
    for row in rows:
        path = root / "tree" / row["family"] / row["version"] / row["path"]
        try:
            resolved = path.resolve(strict=True)
        except (OSError, RuntimeError):
            raise AuditError("A2_FILE_MISSING") from None
        require(resolved.is_relative_to(safe_root) and resolved == path.absolute(),
                "A2_SYMLINK_OR_PATH_ESCAPE")
        require(path.is_file() and not path.is_symlink(), "A2_FILE_MISSING")
        require(path.stat().st_size == row["bytes"], "A2_FILE_SIZE_DRIFT")
        require(file_hash(path) == row["sha256"], "A2_FILE_HASH_DRIFT")
        classification = classify(path, lock["bytesClassificationLimit"])
        by_key[row["family"], row["version"], row["path"]] = {
            **row, **classification, "parserClassification": classification["kind"],
            "changedTarget": False, "d3Potential": False,
        }
    pair_summaries = []
    total_changed = total_candidate = total_potential = 0
    changed_files = candidate_files = potential_files = 0
    for pair in pairs:
        family, base, target = pair["family"], pair["base"], pair["target"]
        allowed = set(pair["candidatePaths"])
        candidate_bytes = potential_bytes = 0
        local_potential = 0
        reasons = collections.Counter()
        kinds = collections.Counter()
        for changed in pair["changed"]:
            name = changed["path"]
            key = (family, target, name)
            base_key = (family, base, name)
            require(key in by_key and base_key in by_key, "A2_CHANGED_MEMBER_MISSING")
            t, b = by_key[key], by_key[base_key]
            require(t["sha256"] == changed["targetSha256"] and
                    b["sha256"] == changed["baseSha256"] and
                    t["bytes"] == changed["targetSize"] and
                    b["bytes"] == changed["baseSize"], "A2_CHANGED_SHA_OR_BYTES")
            t["changedTarget"] = True
            t["baseKind"] = b["kind"]
            kinds[t["kind"]] += 1
            reasons[t["reason"]] += 1
            possible = (name in allowed and t["kind"] == b["kind"] == "ILONLY"
                        and t["machine"] == b["machine"] and
                        t["architecture"] != "unknown")
            t["d3Potential"] = possible
            if name in allowed:
                candidate_bytes += t["bytes"]
            if possible:
                local_potential += 1
                potential_bytes += t["bytes"]
        actual_candidate = sum(int(n in allowed) for n in
                               (r["path"] for r in pair["changed"]))
        require(actual_candidate == len(allowed) and
                candidate_bytes == pair["candidateTargetBytes"],
                "A2_CANDIDATE_INVENTORY_DRIFT")
        changed_size = sum(x["targetSize"] for x in pair["changed"])
        pair_summaries.append({
            "family": family, "role": pair["role"], "base": base, "target": target,
            "changedFiles": len(pair["changed"]), "changedTargetBytes": changed_size,
            "pathCandidateFiles": len(allowed), "pathCandidateBytes": candidate_bytes,
            "d3PotentialFiles": local_potential, "d3PotentialBytes": potential_bytes,
            "targetChangedKinds": dict(sorted(kinds.items())),
            "targetChangedReasons": dict(sorted(reasons.items())),
        })
        total_changed += changed_size
        total_candidate += candidate_bytes
        total_potential += potential_bytes
        changed_files += len(pair["changed"])
        candidate_files += len(allowed)
        potential_files += local_potential
    require(total_changed == lock["expectedChangedTargetBytes"] and
            total_candidate == lock["expectedPathCandidateBytes"] and
            changed_files == lock["expectedChangedFiles"] and
            candidate_files == lock["expectedPathCandidateFiles"], "A2_A1_AGGREGATE_DRIFT")
    ordered = [by_key[k] for k in sorted(by_key)]
    record_bytes = b"".join(canon_json(row) for row in ordered)
    summary = {
        "schema": OUTPUT_SCHEMA, "status": "STRUCTURAL_INVENTORY_ONLY_NO_PATCH_VERDICT",
        "parserVersion": lock["parserVersion"], "a1PlanSha256": lock["planSha256"],
        "a1PairsSha256": lock["pairsSha256"], "a1FilesSha256": lock["filesSha256"],
        "filesSha256": sha256(record_bytes), "allFileRows": len(ordered),
        "allFileKinds": dict(sorted(collections.Counter(r["kind"] for r in ordered).items())),
        "pairs": pair_summaries,
        "totals": {"changedFiles": changed_files, "changedTargetBytes": total_changed,
                   "pathCandidateFiles": candidate_files, "pathCandidateBytes": total_candidate,
                   "d3PotentialFiles": potential_files, "d3PotentialBytes": total_potential},
        "interpretation": ("D3 potential requires matching ILONLY structural headers on both "
                           "images; it is NOT token-scanner or inverse-decoder eligibility."),
    }
    output.mkdir(parents=True, exist_ok=False)
    (output / "a2-files.jsonl").write_bytes(record_bytes)
    (output / "a2-summary.json").write_bytes(
        (json.dumps(summary, sort_keys=True, indent=2) + "\n").encode())
    return summary


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--root", type=Path, required=True)
    p.add_argument("--plan", type=Path,
                   default=Path("docs/benchmarks/patch-dotnet-001/corpus-plan.v1.json"))
    p.add_argument("--lock", type=Path,
                   default=Path("docs/benchmarks/patch-dotnet-001/a2-classification-lock.v1.json"))
    p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    try:
        summary = analyze(a.root, a.plan, a.lock, a.output)
    except (OSError, ValueError, KeyError, TypeError, AssertionError, json.JSONDecodeError) as err:
        p.error(str(err))
    print("PATCH-DOTNET-001 A2 STRUCTURAL_ONLY " + json.dumps(summary["totals"], sort_keys=True))


if __name__ == "__main__":
    main()
