# zstd backend evidence for CSP encoding 1 (#7)

Status: **decides the implementation dependency (PATCHING-DECISIONS D5); not a throughput baseline.**

Issue: [#7](https://github.com/definitely-stable/ChunkShift/issues/7) · Register: [PATCHING-DECISIONS.md](../architecture/PATCHING-DECISIONS.md) D5, D8 · Spec: [CSP-V1-CANDIDATE.md](../architecture/CSP-V1-CANDIDATE.md) §5.2 · Tool: [`benchmarks/zstd-backend`](../../benchmarks/zstd-backend)

CSP encoding 1 is one zstd frame per payload entry, optionally decoded against a raw-content dictionary of up to four base chunks. Decision (i) put the codec in the Patching package and named a managed implementation for the current targets. Before the dependency enters the repository, this probe checks that ZstdSharp.Port is faithful to zstd, works under NativeAOT, and is fast enough for the job.

## 1. Method

The reference is libzstd itself, through Python 3.14's standard `compression.zstd` module. Both implementations report zstd 1.5.7.

Samples are the payload entries a patch would carry: every distinct target chunk of a changed file that the base does not contain. Chunk sequences come from the lab `chunks` mode, so they are exactly what `ChunkScanner` produces with the stable profile and BLAKE3. The dictionary of a sample (`k2`) is the two contiguous base chunks whose run starts nearest to the target offset; a dictionary beginning with the zstd dictionary magic would be skipped (none occurred).

For levels 3, 9 and 19, without a dictionary (`none`) and with it (`k2`), the probe:

- compresses every sample with ZstdSharp (`Compressor.LoadDictionary` + `Wrap`) and with libzstd (`compression.zstd.compress` with `ZstdDict(..., is_raw=True)`);
- compares the frames byte for byte;
- checks every ZstdSharp frame against CSP §5.2: `Frame_Content_Size` present and equal to the chunk length, no dictionary ID, exactly one frame with nothing after it (decoded with `ZstdDecompressor`, which stops at the end of one frame);
- decodes each implementation's frames with the other into an exact-length buffer (ZstdSharp with `ZSTD_d_windowLogMax = 20`) and compares the bytes with the chunk;
- times compression (one run) and decoding (median of five), with the dictionary loaded per entry and one codec context reused per configuration.

The ZstdSharp side runs twice: JIT, and a NativeAOT publish of the same probe.

## 2. Evidence identity

| item | value |
|---|---|
| tool | `benchmarks/zstd-backend` (`zstd_backend_probe.py`, `ZstdBackendProbe.csproj`), as committed with this note |
| input digest (`input_sha256`) | `8f889cabbcef59ad6fe9a6b61d228990f4f2a3a07b72c6523a73530b4fc9676e` |
| corpus | `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` Windows x64 10.0.11 -> 10.0.12, 333 changed files, 766 payload entries, 51.7 MB |
| ZstdSharp | ZstdSharp.Port 0.8.8 (`ZSTD_versionNumber() = 10507`), .NET 10.0.12, JIT and NativeAOT win-x64 |
| libzstd | 1.5.7 through CPython 3.14.3 `compression.zstd` |
| machine | Intel Core i3-12100F, Windows 11 (10.0.26200) |

## 3. Results

| backend | level | variant | frame bytes | identical to libzstd | §5.2 header, one frame | decoded by the other | compress MB/s | decode MB/s |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| libzstd 1.5.7 | 3 | none | 25,754,411 | - | - | - | 224.5 | 567 |
| libzstd 1.5.7 | 3 | k2 | 3,731,927 | - | - | - | 102.5 | 1134 |
| libzstd 1.5.7 | 9 | none | 24,529,920 | - | - | - | 52.4 | 601 |
| libzstd 1.5.7 | 9 | k2 | 3,581,794 | - | - | - | 52.4 | 996 |
| libzstd 1.5.7 | 19 | none | 22,616,538 | - | - | - | 4.8 | 556 |
| libzstd 1.5.7 | 19 | k2 | 3,295,258 | - | - | - | 7.2 | 1175 |
| ZstdSharp JIT | 3 | none | 25,754,411 | 766/766 | 766/766 | 1532/1532 | 88.5 | 384 |
| ZstdSharp JIT | 3 | k2 | 3,731,927 | 766/766 | 766/766 | 1532/1532 | 87.3 | 3104 |
| ZstdSharp JIT | 9 | none | 24,529,920 | 766/766 | 766/766 | 1532/1532 | 54.5 | 975 |
| ZstdSharp JIT | 9 | k2 | 3,581,794 | 766/766 | 766/766 | 1532/1532 | 54.9 | 3058 |
| ZstdSharp JIT | 19 | none | 22,616,538 | 766/766 | 766/766 | 1532/1532 | 4.9 | 699 |
| ZstdSharp JIT | 19 | k2 | 3,295,258 | 766/766 | 766/766 | 1532/1532 | 7.3 | 2114 |
| ZstdSharp NativeAOT | 3 | none | 25,754,411 | 766/766 | 766/766 | 1532/1532 | 155.1 | 898 |
| ZstdSharp NativeAOT | 3 | k2 | 3,731,927 | 766/766 | 766/766 | 1532/1532 | 89.2 | 2061 |
| ZstdSharp NativeAOT | 9 | none | 24,529,920 | 766/766 | 766/766 | 1532/1532 | 57.9 | 850 |
| ZstdSharp NativeAOT | 9 | k2 | 3,581,794 | 766/766 | 766/766 | 1532/1532 | 64.6 | 2877 |
| ZstdSharp NativeAOT | 19 | none | 22,616,538 | 766/766 | 766/766 | 1532/1532 | 4.6 | 630 |
| ZstdSharp NativeAOT | 19 | k2 | 3,295,258 | 766/766 | 766/766 | 1532/1532 | 7.7 | 2583 |

"Decoded by the other" counts libzstd decoding the ZstdSharp frames plus ZstdSharp decoding the libzstd frames.

NativeAOT: `dotnet publish -r win-x64 -p:PublishAot=true` of the probe reported **zero** IL trim or AOT warnings for ZstdSharp (with `TrimmerSingleWarn=false`, so a library's warnings are listed individually rather than folded into one IL2104).

## 4. Findings

1. **ZstdSharp is byte-faithful to libzstd 1.5.7.** All 9,192 frames (6 configurations × 766 entries × JIT and NativeAOT) are identical to libzstd's, with and without a raw-content dictionary. Each implementation decodes every frame of the other to the exact chunk.
2. **Its frames meet CSP §5.2 by default.** One-shot compression writes `Frame_Content_Size`, no dictionary ID and a single frame; decoding into an exact-length buffer with `windowLogMax = 20` accepts them.
3. **Automatic dictionary loading equals raw-content loading here.** ZstdSharp's `LoadDictionary` detects the content type; no dictionary began with the zstd magic, so it loaded raw content and produced libzstd's raw-dictionary frames. The §5.2 magic rule is what makes the two equivalent (D8).
4. **Speed is adequate and of the same order as libzstd.** Compression at level 19 runs at 4.6–7.7 MB/s in both implementations; dictionary decoding runs above 2 GB/s in ZstdSharp. The decode figures are not a like-for-like comparison: the libzstd side pays a Python call and a dictionary object per entry.
5. **NativeAOT works.** Its figures stay within the run-to-run spread of these single stopwatch runs; neither mode is consistently faster (for example level 19 without a dictionary: 4.6 vs 4.9 MB/s compression, 630 vs 699 MB/s decoding).
6. **Preliminary input for D15, not a decision:** with the nearest-run `k2` dictionary, level 19 stores 12% fewer bytes than level 3 (3.30 vs 3.73 MB) at 12–14 times the compression time in every backend. P5 decides the level on the full dictionary search.

## 5. Decision taken on this evidence

ZstdSharp.Port 0.8.8 is the zstd implementation dependency of `ChunkShift.Patching` (D5). The strict NativeAOT gate on the packed package (D20) and the independent CSP vectors (D18) still apply before the format freezes.

## 6. Limitations

One machine and one product family (.NET servicing builds, Windows x64 PE files). Timings are single-process stopwatch measurements of a probe, not results of the benchmark harness; they support "adequate, same order as libzstd", not a performance claim (docs/PERFORMANCE.md). The dictionary is the nearest run only, not the study's eight-candidate search. P10 measures real create/apply throughput in the lab.

## 7. Reproduction

```text
py -3.14 benchmarks/zstd-backend/zstd_backend_probe.py \
  --pair runtime-10.0.11-10.0.12 <shared>/Microsoft.NETCore.App/10.0.11 <shared>/Microsoft.NETCore.App/10.0.12 \
  --pair aspnet-10.0.11-10.0.12 <shared>/Microsoft.AspNetCore.App/10.0.11 <shared>/Microsoft.AspNetCore.App/10.0.12 \
  --work <scratch> --output result.json --markdown summary.md \
  --probe-exe <aot>/ZstdBackendProbe.exe --probe-label zstdsharp-aot
```

`<shared>` is the `dotnet/shared` directory of an installation holding both servicing versions. `<aot>` is the output of `dotnet publish benchmarks/zstd-backend/ZstdBackendProbe.csproj -c Release -r win-x64 -p:PublishAot=true -o <aot>`; on Windows the NativeAOT linker also needs the Visual Studio installer directory (`vswhere.exe`) on `PATH`. The same inputs reproduce `input_sha256`.
