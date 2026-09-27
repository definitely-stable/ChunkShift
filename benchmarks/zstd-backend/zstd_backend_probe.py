#!/usr/bin/env python3
"""zstd backend probe for CSP payload entries (Patching decision register).

Question: is ZstdSharp.Port a faithful and fast enough zstd for CSP encoding 1
(one zstd frame per chunk, optionally against a raw-content dictionary of base
chunks)? The reference is libzstd itself, through Python 3.14's standard
`compression.zstd` module. Both sides report zstd 1.5.7.

Samples are the payload entries a patch would carry: every distinct target
chunk of a changed file that the base does not contain. The dictionary of a
sample ("k2") is the two contiguous base chunks whose run starts nearest to the
target offset, as in the CSP payload-encoding study, skipped when it begins
with the zstd dictionary magic. Chunk sequences come from the lab `chunks`
mode (stable profile, BLAKE3), so they are exactly what ChunkScanner produces.

For levels 3, 9 and 19, with and without the dictionary, the probe records:

  identical   frames byte-identical between ZstdSharp and libzstd
  cross       each implementation decodes the other's frames to the exact chunk
  header      every frame declares Frame_Content_Size = chunk length and no
              dictionary ID, and is exactly one frame
  sizes       total frame bytes
  speed       compress and decode time (median of repeats), dictionary loading
              included, one context reused per configuration

Usage (Python 3.14 with compression.zstd):
  zstd_backend_probe.py --pair NAME BASE TARGET [--pair ...] --work DIR
                        --output result.json [--markdown summary.md]
                        [--probe-exe PATH --probe-label NAME]...

Without --probe-exe the ZstdSharp side runs with `dotnet run -c Release`. Each
--probe-exe/--probe-label pair adds a prebuilt probe (for example the NativeAOT
publish of ZstdBackendProbe.csproj).
"""

from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import platform
import statistics
import struct
import subprocess
import sys
import time
from pathlib import Path

try:
    from compression import zstd
except ImportError:  # pragma: no cover - environment check
    sys.exit("zstd_backend_probe.py needs Python 3.14 with the compression.zstd module")

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
LEVELS = (3, 9, 19)
VARIANTS = ("none", "k2")
DICT_CHUNKS = 2
ZSTD_DICT_MAGIC = b"\x37\xa4\x30\xec"
DECODE_REPEAT = 5


def collect_pairs(pair_args) -> list[tuple[str, str, Path, Path]]:
    pairs = []
    for name, base, target in pair_args:
        base, target = Path(base), Path(target)
        if base.is_file():
            pairs.append((name, target.name, base, target))
            continue
        for path in sorted(p for p in target.rglob("*") if p.is_file()):
            other = base / path.relative_to(target)
            if other.is_file() and other.read_bytes() != path.read_bytes():
                pairs.append((name, path.relative_to(target).as_posix(), other, path))
    return pairs


def dump_chunks(pairs, work: Path) -> list[tuple[Path, Path]]:
    keys, lines = [], []
    for index, (_, _, base, target) in enumerate(pairs):
        key = work / f"{index:05d}"
        keys.append((Path(f"{key}.base.tsv"), Path(f"{key}.target.tsv")))
        lines += [f"{base}\t{key}.base.tsv", f"{target}\t{key}.target.tsv"]
    listing = work / "chunks.tsv"
    listing.write_text("\n".join(lines) + "\n")
    subprocess.run(["dotnet", "run", "--project", str(REPO / "benchmarks" / "ChunkShift.Benchmarks"),
                    "-c", "Release", "--", "chunks", "--list", str(listing)], check=True)
    return keys


def load_chunks(path: Path) -> list[tuple[int, int, str]]:
    rows = []
    for line in path.read_text().splitlines():
        offset, length, chunk_id = line.split("\t")
        rows.append((int(offset), int(length), chunk_id))
    return rows


def build_samples(pairs, keys) -> list[tuple[str, int, int, str, int, int]]:
    samples = []
    for (_, _, base, target), (base_tsv, target_tsv) in zip(pairs, keys):
        base_chunks = load_chunks(base_tsv)
        base_ids = {chunk_id for _, _, chunk_id in base_chunks}
        base_offsets = [offset for offset, _, _ in base_chunks]
        base_bytes = base.read_bytes()
        seen = set()
        for offset, length, chunk_id in load_chunks(target_tsv):
            if chunk_id in base_ids or chunk_id in seen:
                continue
            seen.add(chunk_id)
            dict_offset = dict_length = 0
            if base_chunks:
                position = bisect.bisect_left(base_offsets, offset)
                start = min(range(max(0, position - 1), min(len(base_chunks), position + 1)),
                            key=lambda i: abs(base_offsets[i] - offset))
                end = min(len(base_chunks), start + DICT_CHUNKS)
                dict_offset = base_chunks[start][0]
                dict_length = base_chunks[end - 1][0] + base_chunks[end - 1][1] - dict_offset
                if base_bytes[dict_offset:dict_offset + 4] == ZSTD_DICT_MAGIC:
                    dict_offset = dict_length = 0
            samples.append((str(target), offset, length, str(base), dict_offset, dict_length))
    return samples


def write_frames(path: Path, frames: list[bytes]) -> None:
    with path.open("wb") as stream:
        for frame in frames:
            stream.write(struct.pack("<I", len(frame)))
            stream.write(frame)


def read_frames(path: Path) -> list[bytes]:
    data, frames, position = path.read_bytes(), [], 0
    while position < len(data):
        (length,) = struct.unpack_from("<I", data, position)
        frames.append(data[position + 4:position + 4 + length])
        position += 4 + length
    return frames


def frame_header(frame: bytes) -> dict:
    """RFC 8878 section 3.1.1 frame header fields this probe checks."""
    if frame[:4] != b"\x28\xb5\x2f\xfd":
        return {"magic": False}
    descriptor = frame[4]
    single_segment = bool(descriptor & 0x20)
    fcs_flag = descriptor >> 6
    dict_flag = descriptor & 0x03
    position = 5 + (0 if single_segment else 1)
    dict_size = (0, 1, 2, 4)[dict_flag]
    dict_id = int.from_bytes(frame[position:position + dict_size], "little")
    position += dict_size
    fcs_size = (1 if single_segment else 0, 2, 4, 8)[fcs_flag]
    fcs = int.from_bytes(frame[position:position + fcs_size], "little") if fcs_size else None
    if fcs_size == 2:
        fcs += 256
    return {"magic": True, "single_segment": single_segment, "checksum": bool(descriptor & 0x04),
            "dict_id": dict_id, "content_size": fcs}


def decode_one_frame(frame: bytes, dictionary: bytes | None) -> tuple[bytes, bool]:
    """Decode exactly one frame; report whether it was complete with nothing after it.

    compression.zstd.decompress() accepts concatenated frames and skips skippable
    frames, so it cannot tell a single frame from several. ZstdDecompressor stops
    at the end of the first frame and exposes what follows as unused_data.
    """
    decompressor = zstd.ZstdDecompressor(zstd_dict=zstd.ZstdDict(dictionary, is_raw=True)
                                         if dictionary else None)
    decoded = decompressor.decompress(frame)
    return decoded, decompressor.eof and decompressor.unused_data == b""


def check_header(frame: bytes, length: int) -> bool:
    header = frame_header(frame)
    return header["magic"] and header["dict_id"] == 0 and header["content_size"] == length


def libzstd_side(samples, blobs, work: Path) -> list[dict]:
    rows = []
    for level in LEVELS:
        for variant in VARIANTS:
            dictionaries = [blobs[s[3]][s[4]:s[4] + s[5]] if variant == "k2" and s[5] else None
                            for s in samples]
            data = [blobs[s[0]][s[1]:s[1] + s[2]] for s in samples]
            started = time.perf_counter()
            frames = [zstd.compress(d, level=level,
                                    zstd_dict=zstd.ZstdDict(k, is_raw=True) if k else None)
                      for d, k in zip(data, dictionaries)]
            compress_seconds = time.perf_counter() - started
            decode_seconds = []
            for run in range(DECODE_REPEAT):
                started = time.perf_counter()
                decoded = [zstd.decompress(f, zstd_dict=zstd.ZstdDict(k, is_raw=True) if k else None)
                           for f, k in zip(frames, dictionaries)]
                decode_seconds.append(time.perf_counter() - started)
                if run == 0 and decoded != data:
                    raise AssertionError(f"libzstd L{level} {variant}: frames do not round-trip")
            write_frames(work / f"frames-libzstd-{variant}-L{level}.bin", frames)
            rows.append({"level": level, "variant": variant, "samples": len(samples),
                         "input_bytes": sum(map(len, data)), "frame_bytes": sum(map(len, frames)),
                         "compress_seconds": [compress_seconds], "decode_seconds": decode_seconds})
            print(f"libzstd L{level} {variant}: frames {rows[-1]['frame_bytes']} B, "
                  f"compress {compress_seconds:.3f} s, decode {statistics.median(decode_seconds):.4f} s")
    return rows


def compare(samples, blobs, work: Path, label: str) -> list[dict]:
    rows = []
    for level in LEVELS:
        for variant in VARIANTS:
            ours = read_frames(work / f"frames-{label}-{variant}-L{level}.bin")
            reference = read_frames(work / f"frames-libzstd-{variant}-L{level}.bin")
            identical = sum(a == b for a, b in zip(ours, reference))
            headers_ok = cross_ok = 0
            for frame, s in zip(ours, samples):
                dictionary = blobs[s[3]][s[4]:s[4] + s[5]] if variant == "k2" and s[5] else None
                decoded, single = decode_one_frame(frame, dictionary)
                headers_ok += check_header(frame, s[2]) and single
                cross_ok += decoded == blobs[s[0]][s[1]:s[1] + s[2]]
            rows.append({"level": level, "variant": variant, "samples": len(samples),
                         "identical_frames": identical, "headers_ok": headers_ok,
                         "libzstd_decodes_ours": cross_ok,
                         "size_delta_bytes": sum(map(len, ours)) - sum(map(len, reference))})
    return rows


def run_probe(command: list[str], samples_path: Path, work: Path, label: str) -> dict:
    subprocess.run(command + ["--samples", str(samples_path), "--work", str(work), "--label", label,
                              "--levels", ",".join(map(str, LEVELS)),
                              "--decode-repeat", str(DECODE_REPEAT)], check=True)
    return json.loads((work / f"timings-{label}.json").read_text())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--pair", nargs=3, action="append", required=True, metavar=("NAME", "BASE", "TARGET"))
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--markdown", type=Path)
    parser.add_argument("--probe-exe", action="append", default=[])
    parser.add_argument("--probe-label", action="append", default=[])
    args = parser.parse_args()

    args.work.mkdir(parents=True, exist_ok=True)
    pairs = collect_pairs(args.pair)
    keys = dump_chunks(pairs, args.work)
    samples = build_samples(pairs, keys)
    samples_path = args.work / "samples.tsv"
    samples_path.write_text("".join("\t".join(map(str, s)) + "\n" for s in samples))
    blobs = {path: Path(path).read_bytes() for s in samples for path in (s[0], s[3])}

    input_digest = hashlib.sha256()
    for name, rel, base, target in pairs:
        for part in (name, rel, hashlib.sha256(base.read_bytes()).hexdigest(),
                     hashlib.sha256(target.read_bytes()).hexdigest()):
            input_digest.update(part.encode() + b"\n")

    libzstd = libzstd_side(samples, blobs, args.work)
    probes = [(["dotnet", "run", "--project", str(HERE / "ZstdBackendProbe.csproj"), "-c", "Release", "--"],
               "zstdsharp-jit")]
    probes += [([exe], label) for exe, label in zip(args.probe_exe, args.probe_label)]
    sides, comparisons = {}, {}
    for command, label in probes:
        sides[label] = run_probe(command, samples_path, args.work, label)
        comparisons[label] = compare(samples, blobs, args.work, label)

    result = {
        "tool": "zstd_backend_probe", "python": sys.version.split()[0],
        "libzstd": ".".join(map(str, zstd.zstd_version_info)), "machine": platform.processor(),
        "input_sha256": input_digest.hexdigest(), "changed_files": len(pairs),
        "samples": len(samples), "dictionary_samples": sum(1 for s in samples if s[5]),
        "levels": LEVELS, "decode_repeat": DECODE_REPEAT,
        "libzstd_side": libzstd, "probes": sides, "comparisons": comparisons,
    }
    args.output.write_text(json.dumps(result, indent=1))

    lines = ["| backend | level | variant | frame bytes | identical | headers ok | cross-decoded "
             "| compress MB/s | decode MB/s |", "|---|---:|---|---:|---:|---:|---:|---:|---:|"]
    mb = sum(s[2] for s in samples) / 1e6
    for row in libzstd:
        lines.append(f"| libzstd {result['libzstd']} | {row['level']} | {row['variant']} | {row['frame_bytes']} "
                     f"| - | - | - | {mb / row['compress_seconds'][0]:.1f} "
                     f"| {mb / statistics.median(row['decode_seconds']):.0f} |")
    for label, side in sides.items():
        for row, cmp in zip(side["results"], comparisons[label]):
            lines.append(f"| {label} | {row['level']} | {row['variant']} | {row['frame_bytes']} "
                         f"| {cmp['identical_frames']}/{cmp['samples']} | {cmp['headers_ok']}/{cmp['samples']} "
                         f"| {cmp['libzstd_decodes_ours']}+{row['cross_decoded_libzstd_frames']}"
                         f"/{2 * cmp['samples']} | {mb / statistics.median(row['compress_seconds']):.1f} "
                         f"| {mb / statistics.median(row['decode_seconds']):.0f} |")
    table = "\n".join(lines) + "\n"
    if args.markdown:
        args.markdown.write_text(table)
    print(f"samples={len(samples)} ({mb:.1f} MB), input_sha256={result['input_sha256']}")
    print(table)
    return 0


if __name__ == "__main__":
    sys.exit(main())
