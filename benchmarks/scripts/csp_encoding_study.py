#!/usr/bin/env python3
"""CSP payload-encoding study (#66): what a patch costs per payload encoding.

For every changed file of each (base, target) pair it records, in bytes:

  target          target size
  full_zstd       zstd -19 of the whole target (compressed full delivery)
  unique_missing  distinct target chunks absent from the base (chunk-granular payload)
  csp_raw         raw payload + CSP framing
  csp_zstd        per entry min(raw, zstd frame without dictionary) + framing
  csp_dict_k{K}   per entry min(raw, zstd, zstd with K contiguous base chunks as a raw-content
                  dictionary), K = 1, 2, 4; the encoder tries up to 8 runs whose first chunk lies
                  within 256 KiB of the target offset, nearest first, and pays 32 bytes per
                  dictionary reference
  patch_from      zstd -19 --patch-from=base with a long window (byte-level reference)
  bsdiff          bsdiff4 (byte-level reference)

plus the decode throughput of the chosen K = 2 frames. Framing follows the CSP
v1 candidate layout (docs/architecture/CSP-V1-CANDIDATE.md): PREAMBLE, TCSM
(the embedded target CSM), BASE, PAYL entry headers and dictionary
references, PIDX, FOOT and TRAILER.

Chunk sequences come from the lab (`chunks` mode), so they are exactly the
sequences the stable profile and BLAKE3 produce. Requires the `zstandard`
and `bsdiff4` packages; this is an exploratory lab tool and does not run in
CI.

Usage:
  csp_encoding_study.py --pair NAME BASE TARGET [--pair ...] --work DIR
                        --output result.json [--markdown summary.md] [--workers N]

BASE and TARGET are files or directories; for directories every file present
in both whose bytes differ is a pair (identical files cost nothing under
every method and are counted only in the totals).
"""

from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import math
import os
import subprocess
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

try:
    import bsdiff4
    import zstandard as zstd
except ImportError as error:  # pragma: no cover - environment check
    sys.exit(f"csp_encoding_study.py needs the 'zstandard' and 'bsdiff4' packages: {error}")

REPO = Path(__file__).resolve().parents[2]
LEVEL = 19
SEARCH = 256 * 1024
MAX_STARTS = 8
KS = (1, 2, 4)
DECODE_K = 2
ZSTD_DICT_MAGIC = b"\x37\xa4\x30\xec"


def csm_bytes(chunks: int) -> int:
    blocks = max(1, math.ceil(chunks / 4096))
    # PREAMBLE, CORE (IDs), CBLK blocks, CEND, FOOT, TRAILER with 16-byte headers.
    return 32 + 16 + 184 + blocks * (16 + 24 + 4) + chunks * 36 + (16 + 48) + (16 + 48) + 64


def framing(target_chunks: int, entries: int, dict_refs: int) -> int:
    payl_blocks = math.ceil(entries / 4096) if entries else 0
    return (32                                     # PREAMBLE
            + 16 + csm_bytes(target_chunks)        # TCSM
            + 16 + 32                              # BASE
            + payl_blocks * (16 + 16 + 4)          # PAYL headers, prefixes, CRCs
            + entries * 40 + dict_refs * 32        # PAYL entry headers + dictionary references
            + 16 + 8 + entries * 24 + 4            # PIDX
            + 16 + 56 + 64)                        # FOOT, TRAILER


def load_chunks(path: Path) -> list[tuple[int, int, str]]:
    rows = []
    for line in path.read_text().splitlines():
        offset, length, chunk_id = line.split("\t")
        rows.append((int(offset), int(length), chunk_id))
    return rows


def compress(data: bytes, dictionary: bytes | None = None) -> bytes:
    if not dictionary:
        return zstd.ZstdCompressor(level=LEVEL).compress(data)
    d = zstd.ZstdCompressionDict(dictionary, dict_type=zstd.DICT_TYPE_RAWCONTENT)
    return zstd.ZstdCompressor(level=LEVEL, dict_data=d).compress(data)


def patch_from(base: bytes, target: bytes) -> int:
    window_log = max(20, min(27, math.ceil(math.log2(max(1, len(base) + len(target))))))
    params = zstd.ZstdCompressionParameters.from_level(
        LEVEL, source_size=len(target), window_log=window_log, enable_ldm=True)
    d = zstd.ZstdCompressionDict(base, dict_type=zstd.DICT_TYPE_RAWCONTENT)
    return len(zstd.ZstdCompressor(compression_params=params, dict_data=d).compress(target))


def analyse(job: tuple[str, str, str, str, str]) -> dict:
    name, rel, base_path, target_path, key = job
    work = Path(key).parent
    base = Path(base_path).read_bytes()
    target = Path(target_path).read_bytes()
    base_chunks = load_chunks(work / f"{Path(key).name}.base.tsv")
    target_chunks = load_chunks(work / f"{Path(key).name}.target.tsv")
    base_ids = {c for _, _, c in base_chunks}
    base_offsets = [o for o, _, _ in base_chunks]

    seen: set[str] = set()
    missing = []
    reused = 0
    for offset, length, chunk_id in target_chunks:
        if chunk_id in base_ids:
            reused += length
        elif chunk_id not in seen:
            seen.add(chunk_id)
            missing.append((offset, length))

    result = {"pair": name, "file": rel, "target": len(target), "base": len(base),
              "target_chunks": len(target_chunks), "reused": reused, "entries": len(missing),
              "unique_missing": sum(length for _, length in missing),
              "full_zstd": len(compress(target)), "patch_from": patch_from(base, target),
              "bsdiff": len(bsdiff4.diff(base, target)), "decode_bytes": 0, "decode_seconds": 0.0}
    zstd_total = 0
    dict_total = {k: 0 for k in KS}
    dict_refs = {k: 0 for k in KS}
    for offset, length in missing:
        data = target[offset:offset + length]
        plain = min(length, len(compress(data)))
        zstd_total += plain
        lo = bisect.bisect_left(base_offsets, offset - SEARCH)
        hi = bisect.bisect_right(base_offsets, offset + length + SEARCH)
        starts = sorted(range(max(0, lo - 1), min(len(base_chunks), hi)),
                        key=lambda i: abs(base_chunks[i][0] - offset))[:MAX_STARTS]
        for k in KS:
            best, best_refs, best_frame, best_dict = plain, 0, None, None
            for s in starts:
                e = min(len(base_chunks), s + k)
                dictionary = base[base_chunks[s][0]:base_chunks[e - 1][0] + base_chunks[e - 1][1]]
                if dictionary[:4] == ZSTD_DICT_MAGIC:
                    continue  # the candidate rule forbids such a dictionary
                frame = compress(data, dictionary)
                if len(frame) + 32 * (e - s) < best + 32 * best_refs:
                    best, best_refs, best_frame, best_dict = len(frame), e - s, frame, dictionary
            dict_total[k] += best
            dict_refs[k] += best_refs
            if k == DECODE_K and best_frame is not None:
                d = zstd.ZstdCompressionDict(best_dict, dict_type=zstd.DICT_TYPE_RAWCONTENT)
                started = time.perf_counter()
                out = zstd.ZstdDecompressor(dict_data=d).decompress(best_frame, max_output_size=length)
                result["decode_seconds"] += time.perf_counter() - started
                result["decode_bytes"] += length
                if out != data:
                    raise AssertionError(f"{rel}: dictionary frame does not round-trip")

    entries, chunks = len(missing), len(target_chunks)
    result["csp_raw"] = result["unique_missing"] + framing(chunks, entries, 0)
    result["csp_zstd"] = zstd_total + framing(chunks, entries, 0)
    for k in KS:
        result[f"csp_dict_k{k}"] = dict_total[k] + framing(chunks, entries, dict_refs[k])
    result["framing_k2"] = framing(chunks, entries, dict_refs[2])
    return result


def collect_pairs(args) -> tuple[list[tuple[str, str, Path, Path]], dict]:
    pairs, totals = [], {}
    for name, base, target in args.pair:
        base, target = Path(base), Path(target)
        if base.is_file():
            files = [(target.name, base, target)]
            total, identical = target.stat().st_size, 0
        else:
            files, total, identical = [], 0, 0
            for path in sorted(p for p in target.rglob("*") if p.is_file()):
                rel = path.relative_to(target).as_posix()
                total += path.stat().st_size
                other = base / rel
                if not other.is_file():
                    continue
                if other.read_bytes() == path.read_bytes():
                    identical += path.stat().st_size
                else:
                    files.append((rel, other, path))
        totals[name] = {"target_bytes": total, "identical_bytes": identical, "changed_files": len(files)}
        pairs += [(name, rel, b, t) for rel, b, t in files]
    return pairs, totals


def input_digest(pairs) -> str:
    h = hashlib.sha256()
    for name, rel, base, target in pairs:
        for part in (name, rel, hashlib.sha256(base.read_bytes()).hexdigest(),
                     hashlib.sha256(target.read_bytes()).hexdigest()):
            h.update(part.encode() + b"\n")
    return h.hexdigest()


def dump_chunks(pairs, work: Path) -> list[tuple[str, str, str, str, str]]:
    jobs, lines = [], []
    for i, (name, rel, base, target) in enumerate(pairs):
        key = work / f"{i:05d}"
        jobs.append((name, rel, str(base), str(target), str(key)))
        lines += [f"{base}\t{key}.base.tsv", f"{target}\t{key}.target.tsv"]
    listing = work / "chunks.tsv"
    listing.write_text("\n".join(lines) + "\n")
    subprocess.run(["dotnet", "run", "--project", str(REPO / "benchmarks" / "ChunkShift.Benchmarks"),
                    "-c", "Release", "--", "chunks", "--list", str(listing)], check=True)
    return jobs


METHODS = ["full_zstd", "csp_raw", "csp_zstd", "csp_dict_k1", "csp_dict_k2", "csp_dict_k4", "patch_from", "bsdiff"]


def summarize(results: list[dict], totals: dict) -> tuple[dict, str]:
    summary, lines = {}, []
    lines.append("| pair | changed MiB | reused | " + " | ".join(METHODS) + " | k2 decode |")
    lines.append("|---|---:|---:|" + "---:|" * len(METHODS) + "---:|")
    for name in totals:
        rows = [r for r in results if r["pair"] == name]
        target = sum(r["target"] for r in rows)
        s = {m: sum(r[m] for r in rows) for m in METHODS}
        s.update(target=target, reused=sum(r["reused"] for r in rows),
                 framing_k2=sum(r["framing_k2"] for r in rows),
                 decode_gbps=(sum(r["decode_bytes"] for r in rows) / sum(r["decode_seconds"] for r in rows) / 1e9)
                 if any(r["decode_seconds"] for r in rows) else None)
        summary[name] = {**s, **totals[name]}
        cells = " | ".join(f"{100 * s[m] / target:.2f}%" for m in METHODS)
        decode = f"{s['decode_gbps']:.2f} GB/s" if s["decode_gbps"] else "-"
        lines.append(f"| {name} | {target / 2**20:.1f} | {100 * s['reused'] / target:.1f}% | {cells} | {decode} |")
    return summary, "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--pair", nargs=3, action="append", required=True, metavar=("NAME", "BASE", "TARGET"))
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--markdown", type=Path)
    parser.add_argument("--workers", type=int, default=os.cpu_count() or 1)
    args = parser.parse_args()

    args.work.mkdir(parents=True, exist_ok=True)
    pairs, totals = collect_pairs(args)
    jobs = dump_chunks(pairs, args.work)
    with ProcessPoolExecutor(max_workers=args.workers) as pool:
        results = list(pool.map(analyse, jobs, chunksize=1))
    summary, table = summarize(results, totals)
    args.output.write_text(json.dumps({
        "tool": "csp_encoding_study", "zstandard": zstd.__version__, "libzstd": ".".join(map(str, zstd.ZSTD_VERSION)),
        "bsdiff4": bsdiff4.__version__, "level": LEVEL, "search_bytes": SEARCH, "max_starts": MAX_STARTS,
        "input_sha256": input_digest(pairs), "summary": summary, "files": results}, indent=1))
    if args.markdown:
        args.markdown.write_text(table)
    print(table)
    return 0


if __name__ == "__main__":
    sys.exit(main())
