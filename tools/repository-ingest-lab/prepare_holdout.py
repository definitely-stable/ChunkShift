"""Acquire real byte-only runtime corpora; no candidate execution or timing."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import tarfile
import urllib.parse
import urllib.request
import zipfile

MIB = 1024 * 1024
ARCHIVE_CAP = 128 * MIB
CORPUS_CAP = 256 * MIB
METADATA_URL = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json"
VERSIONS = ("8.0.0", "8.0.1")
RIDS = ("win-x64", "linux-x64")
HOSTS = {"dotnetcli.blob.core.windows.net", "builds.dotnet.microsoft.com",
         "download.visualstudio.microsoft.com", "dotnetcli.azureedge.net"}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def checked_url(url):
    p = urllib.parse.urlsplit(url)
    require(p.scheme == "https" and p.hostname in HOSTS and not p.username
            and not p.password and p.port in (None, 443), "unexpected source URL")
    return url


class Redirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return super().redirect_request(req, fp, code, msg, headers, checked_url(newurl))


def open_url(url):
    return urllib.request.build_opener(Redirects()).open(checked_url(url), timeout=120)


def copy_bounded(source, target, limit, algorithm="sha256"):
    h = hashlib.new(algorithm)
    size = 0
    while block := source.read(1024 * 1024):
        size += len(block)
        require(size <= limit, "byte cap exceeded")
        target.write(block)
        h.update(block)
    return size, h.hexdigest()


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def choose(metadata, version, rid):
    releases = [x for x in metadata["releases"] if x["release-version"] == version]
    require(len(releases) == 1, "release missing/ambiguous")
    runtime = releases[0]["runtime"]
    require(runtime["version"] == version, "runtime version mismatch")
    suffix = ".zip" if rid == "win-x64" else ".tar.gz"
    matches = [f for f in runtime["files"] if f["rid"] == rid and f["name"].endswith(suffix)]
    require(len(matches) == 1, "asset missing/ambiguous")
    asset = matches[0]
    require(re.fullmatch(r"[0-9a-fA-F]{128}", asset["hash"]) is not None, "invalid SHA512")
    return checked_url(asset["url"]), asset["hash"].lower()


def clean_name(name):
    while name.startswith("./"):
        name = name[2:]
    require(name and not name.startswith("/") and "\\" not in name
            and all(part not in ("", ".", "..") for part in name.split("/")),
            "unsafe archive member name")
    return name


def append_members(path, version, out, remaining):
    prefix = f"shared/Microsoft.NETCore.App/{version}/"
    is_zip = path.suffix == ".zip"
    with (zipfile.ZipFile(path) if is_zip else tarfile.open(path, "r:gz")) as archive:
        members = archive.infolist() if is_zip else archive.getmembers()
        selected = []
        for m in members:
            name = m.filename if is_zip else m.name
            # Ignore directories; no extraction to filesystem paths.
            if (m.is_dir() if is_zip else m.isdir()):
                continue
            name = clean_name(name)
            if not name.startswith(prefix):
                continue
            if is_zip:
                require(not m.flag_bits & 1 and (m.external_attr >> 16 & 0o170000) != 0o120000,
                        "encrypted/symlink member")
                size = m.file_size
            else:
                require(m.isfile(), "nonregular selected member")
                size = m.size
            require(0 <= size <= ARCHIVE_CAP, "member cap")
            selected.append((name, size, m))
        require(0 < len(selected) <= 4096, "member count cap")
        require(len({m[0] for m in selected}) == len(selected), "duplicate member names")
        require(sum(m[1] for m in selected) <= remaining, "corpus cap exceeded")
        inventory = []
        for name, size, m in sorted(selected, key=lambda item: item[0].encode("utf-8")):
            with (archive.open(m) if is_zip else archive.extractfile(m)) as source:
                actual, sha = copy_bounded(source, out, size)
            require(actual == size, "short archive member")
            inventory.append(dict(name=name, bytes=size, sha256=sha))
        return inventory


def describe(path):
    size = path.stat().st_size
    require(0 < size <= CORPUS_CAP, "invalid corpus size")
    return dict(file=path.name, bytes=size, sha256=digest(path))


def member_fingerprint(members):
    return hashlib.sha256(json.dumps(members, sort_keys=True, separators=(",", ":"),
                                     ensure_ascii=False).encode("utf-8")).hexdigest()


def verify_asset(record, locked):
    members = record["members"]
    compact = {k: v for k, v in record.items() if k != "members"}
    compact.update(member_count=len(members), member_bytes=sum(m["bytes"] for m in members),
                   member_inventory_sha256=member_fingerprint(members))
    require(compact == locked, "locked asset/member inventory mismatch")


def prepare(root, lock=None):
    root.mkdir(parents=True, exist_ok=False)
    if lock is None:
        with open_url(METADATA_URL) as response:
            metadata_bytes = response.read(16 * MIB + 1)
        require(len(metadata_bytes) <= 16 * MIB, "release metadata cap")
        metadata = json.loads(metadata_bytes)
        (root / "release-metadata.json").write_bytes(metadata_bytes)
        metadata_sha = digest(root / "release-metadata.json")
    else:
        require(lock["experiment"] == "REPO-INGEST-003" and
                lock["phase"] == "sealed-holdout-definition", "wrong source lock")
        metadata_sha = lock["provenance"]["metadata_sha256"]
        locked_assets = {(a["rid"], a["version"]): a for a in lock["assets"]}
        require(len(lock["assets"]) == len(locked_assets) == 4 and
                set(locked_assets) == {(rid, version) for rid in RIDS for version in VERSIONS},
                "wrong locked asset set")
    assets = []
    paths = {}
    for rid in RIDS:
        for version in VERSIONS:
            if lock is None:
                url, expected = choose(metadata, version, rid)
            else:
                locked = locked_assets[rid, version]
                url, expected = checked_url(locked["url"]), locked["sha512"]
            path = root / f"{rid}-{version}{'.zip' if rid == 'win-x64' else '.tar.gz'}"
            with open_url(url) as source, path.open("xb") as out:
                size, actual = copy_bounded(source, out, ARCHIVE_CAP, "sha512")
            require(actual == expected, "archive SHA512 mismatch")
            if lock is not None:
                require(size == locked["bytes"], "locked archive length mismatch")
            paths[rid, version] = path
            assets.append(dict(version=version, rid=rid, url=url, bytes=size, sha512=actual))
    corpora = {}
    for rid, name in (("win-x64", "win-versioned"), ("linux-x64", "linux-versioned")):
        path = root / f"{name}.bin"
        with path.open("xb") as out:
            for version in VERSIONS:
                members = append_members(paths[rid, version], version, out, CORPUS_CAP - out.tell())
                next(a for a in assets if a["rid"] == rid and a["version"] == version)["members"] = members
        corpora[name] = describe(path)
    path = root / "compressed.bin"
    with path.open("xb") as out:
        for version in VERSIONS:
            with paths["win-x64", version].open("rb") as source:
                copy_bounded(source, out, CORPUS_CAP - out.tell())
    corpora["compressed"] = describe(path)
    with (root / "win-versioned.bin").open("rb") as source:
        prefix = source.read(4 * MIB)
    require(len(prefix) == 4 * MIB, "short Windows prefix")
    path = root / "adverse-repeat.bin"
    with path.open("xb") as out:
        out.write(prefix)
        with (root / "linux-versioned.bin").open("rb") as source:
            separator = source.read(40 * MIB)
        require(len(separator) == 40 * MIB, "short Linux separator")
        out.write(separator)
        for _ in range(10):
            out.write(prefix)
    corpora["adverse-repeat"] = describe(path)
    if lock is not None:
        for asset in assets:
            verify_asset(asset, locked_assets[asset["rid"], asset["version"]])
        require(corpora == lock["corpora"], "locked corpus identity mismatch")
    manifest = dict(experiment="REPO-INGEST-003", phase="acquisition-only" if lock is None else "locked-replay",
                    metadata_url=METADATA_URL, metadata_sha256=metadata_sha,
                    generator_sha256=digest(Path(__file__)), assets=assets, corpora=corpora)
    (root / "holdout-preflight.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("Acquisition complete; four source-bound corpora. No candidate timing.")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--lock", type=Path, help="Replay exact committed asset URLs and identities")
    args = p.parse_args()
    root = args.output.resolve()
    checkout = Path(__file__).resolve().parents[2]
    require(root != checkout and checkout not in root.parents, "output must be outside checkout")
    lock = json.loads(args.lock.read_text(encoding="utf-8")) if args.lock else None
    prepare(root, lock)


if __name__ == "__main__":
    main()
