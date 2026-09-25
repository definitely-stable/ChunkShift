#!/usr/bin/env python3
"""Materialize the frozen #8 real corpus from upstream release assets.

This script is deliberately not a benchmark. It reconstructs exactly the
payload bytes named by docs/benchmarks/cdc-0.1-corpus/real-corpus.json,
verifies every upstream download against source-assets.sha256, applies only
the documented lossless transforms, and finally verifies payload size/SHA-256
against the frozen manifest.

No CDC candidate is instantiated here.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import shutil
import tarfile
import tempfile
import time
import urllib.request
from dataclasses import dataclass


CHUNK = 1024 * 1024
USER_AGENT = "ChunkShift-cdc-corpus-materializer/1"


@dataclass(frozen=True)
class Recipe:
    source_name: str
    url: str
    transform: str
    member: str | None = None


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(CHUNK), b""):
            digest.update(block)
    return digest.hexdigest()


def load_source_checksums(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        try:
            digest, name = line.split(maxsplit=1)
        except ValueError as exc:
            raise ValueError(f"{path}:{number}: expected '<sha256> <name>'") from exc
        if len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            raise ValueError(f"{path}:{number}: non-canonical SHA-256")
        if name in result:
            raise ValueError(f"{path}:{number}: duplicate source name '{name}'")
        result[name] = digest
    return result


def recipe_for(family_id: str, version: str) -> Recipe:
    if family_id == "game-mindustry-assets":
        tag = version
        source = f"mindustry-{tag}-assets.jar"
        return Recipe(
            source,
            f"https://github.com/Anuken/Mindustry/releases/download/{tag}/assets.jar",
            "copy",
        )

    if family_id == "game-openrct2-linux":
        tag = version
        source = f"OpenRCT2-{tag}-Linux-noble-x86_64.tar.gz"
        return Recipe(
            source,
            f"https://github.com/OpenRCT2/OpenRCT2/releases/download/{tag}/{source}",
            "gunzip",
        )

    if family_id == "dotnet-powershell-linux":
        tag = version
        bare = version.removeprefix("v")
        source = f"powershell-{bare}-linux-x64.tar.gz"
        return Recipe(
            source,
            f"https://github.com/PowerShell/PowerShell/releases/download/{tag}/{source}",
            "gunzip",
        )

    if family_id == "installer-notepadpp-x64":
        tag = version
        bare = version.removeprefix("v")
        source = f"npp.{bare}.Installer.x64.exe"
        return Recipe(
            source,
            f"https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/{tag}/{source}",
            "copy",
        )

    if family_id == "structured-icu4c-data":
        if not version.startswith("icu") or not version[3:].isdigit():
            raise ValueError(f"Unexpected ICU version '{version}'")
        major = version[3:]
        source = f"icu4c-data-0.{major}.2.tgz"
        return Recipe(
            source,
            f"https://registry.npmjs.org/icu4c-data/-/{source}",
            "tar-member",
            f"package/icudt{major}l.dat",
        )

    if family_id == "negative-dotnet-runtime-deb":
        bare = version.removeprefix("v")
        source = f"dotnet-runtime-10.0_{bare}_amd64.deb"
        return Recipe(
            source,
            "https://packages.microsoft.com/debian/12/prod/pool/main/d/"
            f"dotnet-runtime-10.0/{source}",
            "copy",
        )

    raise ValueError(f"No materialization recipe for family '{family_id}'")


def download(url: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    last_error: Exception | None = None
    for attempt in range(1, 4):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                with destination.open("wb") as output:
                    shutil.copyfileobj(response, output, length=CHUNK)
            return
        except Exception as exc:  # network failures are retried, integrity failures are not
            last_error = exc
            destination.unlink(missing_ok=True)
            if attempt == 3:
                break
            time.sleep(attempt * 2)
    assert last_error is not None
    raise RuntimeError(f"Failed to download {url} after 3 attempts") from last_error


def transform(source: Path, destination: Path, recipe: Recipe) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + ".tmp")
    temporary.unlink(missing_ok=True)

    if recipe.transform == "copy":
        shutil.copyfile(source, temporary)
    elif recipe.transform == "gunzip":
        with gzip.open(source, "rb") as input_stream, temporary.open("wb") as output:
            shutil.copyfileobj(input_stream, output, length=CHUNK)
    elif recipe.transform == "tar-member":
        assert recipe.member is not None
        with tarfile.open(source, "r:gz") as archive:
            member = archive.getmember(recipe.member)
            extracted = archive.extractfile(member)
            if extracted is None:
                raise RuntimeError(f"Could not read {recipe.member} from {source.name}")
            with extracted, temporary.open("wb") as output:
                shutil.copyfileobj(extracted, output, length=CHUNK)
    else:
        raise ValueError(f"Unknown transform '{recipe.transform}'")

    os.replace(temporary, destination)


def safe_payload_path(root: Path, relative: str) -> Path:
    path = (root / relative).resolve()
    root_resolved = root.resolve()
    if not path.is_relative_to(root_resolved):
        raise ValueError(f"Payload path escapes corpus root: {relative}")
    return path


def manifest_entries(manifest: dict) -> list[tuple[str, dict]]:
    entries: list[tuple[str, dict]] = []
    for family in manifest["families"]:
        family_id = family["id"]
        for version in family["versions"]:
            entries.append((family_id, version))
    return entries


def verify_recipe_coverage(manifest: dict, checksums: dict[str, str]) -> list[tuple[str, dict, Recipe]]:
    prepared: list[tuple[str, dict, Recipe]] = []
    used_sources: set[str] = set()

    for family_id, version in manifest_entries(manifest):
        recipe = recipe_for(family_id, version["version"])
        if recipe.source_name not in checksums:
            raise ValueError(
                f"Missing source checksum for {family_id}:{version['version']} ({recipe.source_name})"
            )
        if recipe.source_name in used_sources:
            raise ValueError(f"Source asset used twice: {recipe.source_name}")
        used_sources.add(recipe.source_name)
        prepared.append((family_id, version, recipe))

    unused = set(checksums) - used_sources
    if unused:
        raise ValueError("Unused source checksum entries: " + ", ".join(sorted(unused)))

    return prepared


def materialize(manifest_path: Path, checksum_path: Path, output_root: Path, check_only: bool) -> None:
    manifest_bytes = manifest_path.read_bytes()
    manifest = json.loads(manifest_bytes)
    checksums = load_source_checksums(checksum_path)
    prepared = verify_recipe_coverage(manifest, checksums)

    print(f"recipe coverage: {len(prepared)} payloads, {len(checksums)} source assets")
    if check_only:
        for family_id, version, recipe in prepared:
            print(f"{family_id}:{version['version']} <- {recipe.source_name}")
        return

    output_root.mkdir(parents=True, exist_ok=True)
    (output_root / "real-corpus.json").write_bytes(manifest_bytes)

    with tempfile.TemporaryDirectory(prefix="chunkshift-cdc-sources-") as temp_dir:
        temp = Path(temp_dir)

        for index, (family_id, version, recipe) in enumerate(prepared, 1):
            destination = safe_payload_path(output_root, version["path"])
            source = temp / recipe.source_name

            print(f"[{index:02d}/{len(prepared)}] download {recipe.source_name}", flush=True)
            download(recipe.url, source)

            actual_source_sha = sha256_file(source)
            expected_source_sha = checksums[recipe.source_name]
            if actual_source_sha != expected_source_sha:
                raise RuntimeError(
                    f"{recipe.source_name}: source SHA-256 {actual_source_sha} != {expected_source_sha}"
                )

            transform(source, destination, recipe)
            source.unlink(missing_ok=True)

            actual_size = destination.stat().st_size
            expected_size = int(version["sizeBytes"])
            if actual_size != expected_size:
                raise RuntimeError(
                    f"{family_id}:{version['version']}: size {actual_size} != {expected_size}"
                )

            actual_payload_sha = sha256_file(destination)
            expected_payload_sha = version["sha256"]
            if actual_payload_sha != expected_payload_sha:
                raise RuntimeError(
                    f"{family_id}:{version['version']}: payload SHA-256 "
                    f"{actual_payload_sha} != {expected_payload_sha}"
                )

            print(
                f"[{index:02d}/{len(prepared)}] verified "
                f"{family_id}:{version['version']} ({actual_size} bytes)",
                flush=True,
            )

    print("real corpus materialized and verified")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--source-checksums", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--check-only",
        action="store_true",
        help="validate recipe/checksum coverage without network access or writing payloads",
    )
    args = parser.parse_args()

    materialize(args.manifest, args.source_checksums, args.output, args.check_only)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
