# CSP payload-encoding evidence (#66)

Status: **preliminary; decided the CSP v1 shape, not a freeze baseline.**

Issue: [#66](https://github.com/definitely-stable/ChunkShift/issues/66) · Spec: [CSP-V1-CANDIDATE.md](../architecture/CSP-V1-CANDIDATE.md) §5, §10, §12 · Tool: [`benchmarks/scripts/csp_encoding_study.py`](../../benchmarks/scripts/csp_encoding_study.py)

The CSP draft left one central question open: is chunk-granular reuse enough, or does v1 need an encoding inside a chunk? This note records the measurement the answer rests on. It measures payload bytes, not a real encoder: every CSP number is the payload the encoding would store plus the framing the candidate layout adds. `CspBytes` of the lab stays null until Patching produces real artifacts (M0-LAB "Missing payload and CSP bytes").

## 1. Question and methods

For each changed file of a (base, target) pair, with the stable profile (`fastcdc.gear.chunkshift.v1.64k`) and BLAKE3, the tool records:

| method | meaning |
|---|---|
| `full_zstd` | zstd -19 of the whole target: compressed full delivery |
| `csp_raw` | distinct target chunks missing from the base, stored raw, plus CSP framing |
| `csp_zstd` | the same entries, each stored as the smaller of raw and a zstd -19 frame |
| `csp_dict_k1/k2/k4` | each entry the smallest of raw, zstd, and zstd with K contiguous base chunks as a raw-content dictionary; the encoder tries up to 8 runs starting within 256 KiB of the target offset, nearest first, and pays 32 bytes per dictionary reference |
| `patch_from` | zstd -19 `--patch-from=base` with a long window: a byte-level reference |
| `bsdiff` | bsdiff4: a byte-level reference |

Framing is the candidate layout: PREAMBLE, the embedded target CSM, BASE, `PAYL` entry headers (40 bytes plus 32 per dictionary reference), `PIDX` (24 bytes per entry), FOOT and TRAILER. Chunk sequences come from the lab's `chunks` mode, so they are exactly what `ChunkScanner` produces. Every chosen K = 2 frame is decoded and compared with the original chunk.

## 2. Evidence identity

| item | value |
|---|---|
| tool | `benchmarks/scripts/csp_encoding_study.py` and lab `chunks` mode, as committed with this note |
| input digest (`input_sha256`) | `d3c575109858fe69df2472615a7c0ad988552d9fe0daeaf5435b54d1a75a02fe` |
| codecs | `zstandard` 0.25.0 (libzstd 1.5.7), level 19; `bsdiff4` 1.2.6 |
| machine | Intel Core i3-12100F, Windows 11 (10.0.26200), .NET 10.0.12, Python 3.13 |

Corpus:

- `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` Windows x64 installs 10.0.10, 10.0.11 and 10.0.12 (official servicing releases), paired by relative path. A servicing build changes almost every file: 186 of 188 and 187 of 188 runtime files, 146 of 146 ASP.NET Core files. The runtime mixes ReadyToRun managed assemblies with native binaries (`coreclr.dll`, `clrjit.dll`, ...).
- Two uncompressed `git archive` snapshots of this repository with a fixed mtime (`2000-01-01`): `9e7fd3a` -> `27aa319` (25 commits) and `287a59d` -> `27aa319` (6 commits). The fixed mtime matters: with commit mtimes every tar header changes and chunk reuse collapses for reasons unrelated to content.

## 3. Results

Share of the changed target bytes (smaller is better):

| pair | changed MiB | chunks reused | full_zstd | csp_raw | csp_zstd | dict k1 | **dict k2** | dict k4 | patch_from | bsdiff | k2 decode |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| runtime 10.0.10 -> 10.0.11 | 72.7 | 37.8% | 38.39% | 62.50% | 27.02% | 7.04% | **5.14%** | 4.69% | 4.14% | 2.86% | 0.86 GB/s |
| runtime 10.0.11 -> 10.0.12 | 75.0 | 58.3% | 38.38% | 42.00% | 18.97% | 3.99% | **3.00%** | 2.89% | 2.55% | 2.48% | 1.02 GB/s |
| ASP.NET 10.0.10 -> 10.0.11 | 28.5 | 36.0% | 38.53% | 64.44% | 27.14% | 2.63% | **2.56%** | 2.55% | 2.01% | 2.37% | 1.86 GB/s |
| ASP.NET 10.0.11 -> 10.0.12 | 28.5 | 36.7% | 38.52% | 63.80% | 26.96% | 2.58% | **2.42%** | 2.43% | 1.88% | 2.22% | 2.07 GB/s |
| source, 25 commits | 2.2 | 10.0% | 19.90% | 90.16% | 19.97% | 13.51% | **12.94%** | 12.85% | 5.15% | 7.78% | 0.81 GB/s |
| source, 6 commits | 2.2 | 56.9% | 19.90% | 43.20% | 8.74% | 1.04% | **0.97%** | 0.97% | 0.81% | 1.24% | 2.38 GB/s |

Selected files, runtime 10.0.11 -> 10.0.12:

| file | chunks reused | csp_raw | csp_zstd | k1 | k2 | k4 | patch_from | bsdiff |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `System.Private.CoreLib.dll` (15.3 MiB, ReadyToRun) | 94.6% | 5.50% | 1.94% | 0.08% | 0.08% | 0.08% | 0.04% | 0.03% |
| `mscordaccore.dll` | 50.3% | 49.82% | 20.05% | 2.67% | 1.14% | 0.42% | 0.20% | 0.26% |
| `coreclr.dll` (native) | 0.0% | 100.14% | 46.78% | 17.84% | 11.03% | 10.86% | 10.46% | 5.47% |
| `clrjit.dll` (native) | 0.0% | 100.15% | 51.12% | 15.85% | 6.89% | 6.14% | 5.80% | 3.85% |

## 4. Findings

1. **Raw chunk reuse alone loses to whole-file compression** on servicing updates: 42–64% against 38%. Managed assemblies keep most chunks (the changes are headers and signatures), but native binaries keep none, because code shifts move every chunk, and small files are replaced whole.
2. **Per-chunk zstd halves the raw payload** (19–27%) but stays far from delta tools.
3. **A dictionary of nearby base chunks closes the gap by an order of magnitude**: 2.4–5.1% on servicing updates, 4–8% of the raw CSP and 9–19% of the zstd-only CSP, within 1.17–1.29x of `zstd --patch-from` and 1.1–1.8x of bsdiff. Managed assemblies with one changed chunk drop to near zero.
4. **K = 2 is the knee.** Two contiguous base chunks recover most of what four do (runtime 10.0.11 -> 10.0.12: 3.99% -> 3.00% -> 2.89%); the gain concentrates in native code, where content crosses chunk boundaries. Up to four chunks (1 MiB) keeps headroom at bounded decoder memory.
5. **Native code remains the hard case**: `coreclr.dll` 11.0% against bsdiff's 5.5%. Closing that needs executable-aware transforms (the Courgette/Zucchini class), which CSP v1 does not attempt.
6. **Framing matters for small files.** Framing is 6.5–22% of a k2 patch here, and the embedded target CSM is 3–12%, highest for ASP.NET Core, where 53 of 146 files are under 64 KiB. Updating a tree of files needs a tree-level container in the distribution layer rather than per-file overhead tuning inside CSP.
7. **Decode is cheap relative to transfer.** Dictionary decode ran at 0.8–2.4 GB/s in this Python harness, including per-entry dictionary loading; the 31–45 MiB decoded per runtime update takes well under 0.1 s, while the patch shrinks by 12–16 MiB compared with the zstd-only CSP.

## 5. Decision taken on this evidence

CSP v1 ships encoding 1, a zstd frame against 0..4 named base chunks, next to raw (CSP-V1-CANDIDATE §5.2, §12 (a), (i), (j)).

## 6. Limitations

One product family (.NET servicing builds, Windows x64 PE files) and one small source corpus, one machine, an exploratory Python harness at zstd level 19 with an 8-candidate search, per-file patches only, and no real CSP encoder. The pre-freeze evidence of CSP-V1-CANDIDATE §10.2 adds a frozen, reproducibly materialized corpus with non-.NET native, ARM64/ELF and non-code data, real encoder output, xdelta3, and end-to-end update time at fixed bandwidths.

## 7. Reproduction

```text
python -m pip install zstandard bsdiff4
python benchmarks/scripts/csp_encoding_study.py \
  --pair runtime-10.0.10-10.0.11 <shared>/Microsoft.NETCore.App/10.0.10 <shared>/Microsoft.NETCore.App/10.0.11 \
  --pair runtime-10.0.11-10.0.12 <shared>/Microsoft.NETCore.App/10.0.11 <shared>/Microsoft.NETCore.App/10.0.12 \
  --pair aspnet-10.0.10-10.0.11 <shared>/Microsoft.AspNetCore.App/10.0.10 <shared>/Microsoft.AspNetCore.App/10.0.11 \
  --pair aspnet-10.0.11-10.0.12 <shared>/Microsoft.AspNetCore.App/10.0.11 <shared>/Microsoft.AspNetCore.App/10.0.12 \
  --pair source-25-commits repo-9e7fd3a.tar repo-27aa319.tar \
  --pair source-6-commits repo-287a59d.tar repo-27aa319.tar \
  --work <scratch> --output result.json --markdown summary.md
```

`<shared>` is the `dotnet/shared` directory of an installation holding the three servicing versions; the archives come from `git archive --format=tar --mtime='2000-01-01 00:00:00' <commit>`. The same inputs reproduce `input_sha256`.
