#!/usr/bin/env python3
"""CORE011 G1-J2H0: prefreeze a previously unexamined NuGet DLL pair.

No chunking, Plan or CSP operations are allowed in this script.
Immutable product/version/entry selection is embedded in the reviewed code.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import urllib.parse
import urllib.request
import zipfile

SCHEMA = "chunkshift.core011.g1j2h0-holdout-prefreeze.v1"
PREFIX = "CORE011-CSM-001/G1J2H0-"
PACKAGE_ID = "Newtonsoft.Json"
LOWER_ID = "newtonsoft.json"
VERSIONS = ("13.0.2", "13.0.3")
ENTRY = "lib/net6.0/Newtonsoft.Json.dll"
MAX_PACKAGE_BYTES = 8 * 1024 * 1024
MAX_ASSEMBLY_BYTES = 8 * 1024 * 1024


def require(condition, error):
    if not condition:
        raise ValueError(error)


def digest(raw, name):
    return hashlib.new(name, raw).hexdigest()


def package_url(version):
    require(version in VERSIONS, "holdout version is not preregistered")
    return ("https://api.nuget.org/v3-flatcontainer/"
            + LOWER_ID + "/" + version + "/" + LOWER_ID
            + "." + version + ".nupkg")


def parse_managed_pe(raw):
    """Small strict PE/COFF CLR-header eligibility check, no code execution.

    The full managed metadata/assembly identity parser is an H1 independent
    control. This gate is deliberately structural, not a signed publisher ID.
    """
    require(len(raw) >= 0x100 and raw[:2] == b"MZ",
            "missing DOS header")
    pe = struct.unpack_from("<I", raw, 0x3c)[0]
    require(0x40 <= pe <= len(raw) - 24 and raw[pe:pe + 4] == b"PE\x00\x00",
            "malformed PE signature/offset")
    machine, sections, _, _, _, opt_bytes, _ = struct.unpack_from(
        "<HHIIIHH", raw, pe + 4)
    require(machine in (0x14c, 0x8664, 0xaa64) and 0 < sections <= 96,
            "PE machine or section count invalid")
    opt = pe + 24
    require(opt_bytes >= 96 and opt + opt_bytes <= len(raw),
            "PE optional header truncated")
    magic = struct.unpack_from("<H", raw, opt)[0]
    require(magic in (0x10b, 0x20b), "unsupported PE optional header")
    directory_at = opt + (96 if magic == 0x10b else 112)
    directory_count_at = opt + (92 if magic == 0x10b else 108)
    require(directory_at + 15 * 8 <= opt + opt_bytes,
            "PE optional directory truncated")
    count = struct.unpack_from("<I", raw, directory_count_at)[0]
    require(count >= 15, "no CLR directory in PE header")
    clr_rva, clr_size = struct.unpack_from("<II", raw,
                                           directory_at + 14 * 8)
    require(clr_rva > 0 and clr_size >= 0x48,
            "PE has no valid-sized CLR directory")
    return {"machine": "0x%04x" % machine,
            "optionalMagic": "0x%03x" % magic,
            "sections": sections,
            "clrDirectoryRva": clr_rva,
            "clrDirectorySize": clr_size}


def inspect_archive(raw, path=ENTRY):
    """Read only the selected entry and verify CRC; do not extract or write DLL."""
    require(0 < len(raw) <= MAX_PACKAGE_BYTES,
            "NuGet package exceeds frozen resource bound")
    try:
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            members = z.infolist()
            require(len(members) > 0 and len(members) <= 2000,
                    "invalid package entry count")
            exact = [m for m in members if m.filename == path]
            require(len(exact) == 1, "missing or duplicate frozen exact DLL path")
            member = exact[0]
            require(not member.is_dir() and 0 < member.file_size <= MAX_ASSEMBLY_BYTES,
                    "frozen DLL file size out of bounds")
            require(z.testzip() is None, "NuGet ZIP CRC mismatch")
            data = z.read(member)
            require(len(data) == member.file_size, "DLL extraction size mismatch")
    except (zipfile.BadZipFile, RuntimeError, EOFError) as exc:
        raise ValueError("invalid frozen NuGet ZIP/package entry") from exc
    details = parse_managed_pe(data)
    return {
        "packageBytes": len(raw),
        "packageSha256": digest(raw, "sha256"),
        "packageSha512": digest(raw, "sha512"),
        "dllBytes": len(data),
        "dllSha256": digest(data, "sha256"),
        "dllSha512": digest(data, "sha512"),
        "managedPe": details,
    }


def fetch_package(version):
    url = package_url(version)
    req = urllib.request.Request(url, headers={
        "User-Agent": "ChunkShift-Core011-J2H0-Frozen-Research/1.0",
        "Accept": "application/octet-stream"})
    with urllib.request.urlopen(req, timeout=45) as response:
        final = urllib.parse.urlparse(response.geturl())
        require(final.scheme == "https" and (
            final.hostname == "nuget.org"
            or final.hostname.endswith(".nuget.org")),
            "NuGet transport redirected off official domain")
        require(response.status == 200, "NuGet transport unsuccessful")
        raw = response.read(MAX_PACKAGE_BYTES + 1)
        require(len(raw) <= MAX_PACKAGE_BYTES,
                "NuGet response size bound exceeded")
    return raw


def validate_pair(a, b):
    require(a["dllSha256"] != b["dllSha256"],
            "unchanged exact managed DLL binary pair: NO_GO")
    require(a["packageSha256"] != b["packageSha256"],
            "identical package bytes: NO_GO")
    return True


def execute(output, run_id):
    require(run_id.startswith(PREFIX) and len(run_id) > len(PREFIX),
            "invalid H0 RunId")
    checkout = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], text=True).strip()
    require(os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("GITHUB_SHA") == checkout
            and len(checkout) == 40,
            "H0 must execute at exact GitHub-hosted checkout")
    require(not output.exists(), "refuse pre-existing evidence output")
    output.mkdir(parents=True)
    try:
        records = {}
        for role, version in zip(("base", "target"), VERSIONS):
            raw = fetch_package(version)
            records[role] = {
                "nugetVersion": version,
                "downloadUrl": package_url(version),
                "exactEntry": ENTRY,
                **inspect_archive(raw)}
            del raw
        validate_pair(records["base"], records["target"])
        evidence = {
            "schema": SCHEMA,
            "runId": run_id,
            "checkoutSha": checkout,
            "packageId": PACKAGE_ID,
            "assetSelectionFrozenBeforeDownload": True,
            "stage": "CORPUS_PREFREEZE_ONLY",
            "source": "official NuGet FlatContainer (digests self-computed, not signed publisher authentication)",
            "base": records["base"],
            "target": records["target"],
            "noChunkingOrPatchMeasurements": True,
            "decisionRunsAllowed": False,
            "budgetsLocked": False,
            "verdict": "NO_VERDICT_EXPLORATORY",
        }
        (output / "prefreeze.json").write_text(
            json.dumps(evidence, indent=2) + "\n")
        print("H0 NuGet source identities and managed-PE preflight PASS; "
              "NO reuse/patch data generated; product NO_VERDICT_EXPLORATORY")
    except Exception as exc:
        (output / "failure.json").write_text(json.dumps({
            "schema": "chunkshift.core011.g1j2h0-failure.v1",
            "runId": run_id,
            "errorType": type(exc).__name__,
            "status": "NO_GO_SOURCE_PREFREEZE",
            "verdict": "NO_VERDICT_EXPLORATORY",
        }, indent=2) + "\n")
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    execute(args.output_dir, args.run_id)


if __name__ == "__main__":
    main()
