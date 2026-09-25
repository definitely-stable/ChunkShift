# CDC 0.1 real corpus (#8): CORPUS FROZEN

Status: **corpus frozen before any real-corpus measurement.** No `prefreeze --real` run, candidate chunking, profile comparison or runtime bake-off has been executed on these payloads.
Issue: [#8](https://github.com/definitely-stable/ChunkShift/issues/8) · Protocol: [CDC-0.1-BAKEOFF-PROTOCOL.md](CDC-0.1-BAKEOFF-PROTOCOL.md) (§4, §5)
Baseline: `main` at `697c881`; protocol baseline `80058a1177306817ed06c95143a3c54bf16229bd` (PR #106)

This note records the real multi-version corpus for the #8 bake-off, its calibration/holdout split and the lock. It holds no measurement result.

## 1. Lock

| item | value |
|---|---|
| manifest | [`cdc-0.1-corpus/real-corpus.json`](cdc-0.1-corpus/real-corpus.json) (schema 1, unchanged) |
| `manifestSha256` | `fe497c1717bda7da1681fae7dd4fe216a855690f50cef859a7a40dbddd2e2b49` |
| `experimentPlanSha256` | `62cb1aa2745a150d29f1e0f9cae1824f58914111e062fcb4bd507d8ffc958b34` (`benchmarks/experiments/prefreeze.v1.json`, unchanged) |
| `protocolBaseline` | `80058a1177306817ed06c95143a3c54bf16229bd` |
| families / calibration / holdout | 6 / 3 / 3 |
| `eligibleCalibrationFamilies` | 3 |
| `eligibleHoldoutFamilies` | 3 |
| `selectionPossible` | `true` |
| pair-only / short-history families | none / none |
| validator warnings | none |
| lock | [`cdc-0.1-corpus/corpus-lock.json`](cdc-0.1-corpus/corpus-lock.json) |
| source downloads | [`cdc-0.1-corpus/source-assets.sha256`](cdc-0.1-corpus/source-assets.sha256) |

The lock was produced by the validation-only mode, which verifies every payload size and SHA-256 and chunks nothing:

```text
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release -- \
  prefreeze validate-real --real <corpus-root>/real-corpus.json \
  --plan benchmarks/experiments/prefreeze.v1.json --lock-output <corpus-root>/corpus-lock.json
```

Two runs produced byte-identical locks. The committed `real-corpus.json` is a byte-identical copy of the manifest that was validated; to reproduce, place it at `<corpus-root>/real-corpus.json` next to `<corpus-root>/payloads/` (§4). Paths in the manifest are relative to the manifest.

## 2. Families and split

| id | category | versions | history | split | producer / artifact | license |
|---|---|---:|---|---|---|---|
| `game-mindustry-assets` | game/pak-like assets | 6 (v160 → v160.5) | full-history | calibration | Anuken, Mindustry `assets.jar` (GitHub Releases) | GPL-3.0 |
| `game-openrct2-linux` | game/pak-like assets | 6 (v0.5.0 → v0.5.5) | full-history | holdout | OpenRCT2 team, Linux noble x86_64 build tarball (GitHub Releases), outer gzip removed | GPL-3.0 (+ third-party notices) |
| `dotnet-powershell-linux` | executable/app bundles | 6 (v7.4.15 → v7.4.20) | full-history | holdout | Microsoft, PowerShell self-contained linux-x64 tarball (GitHub Releases), outer gzip removed | MIT |
| `installer-notepadpp-x64` | installers/archives | 7 (v8.9.6 → v8.9.8.1) | full-history | calibration | Don Ho, Notepad++ x64 NSIS installer (GitHub Releases) | GPL-3.0 |
| `structured-icu4c-data` | DB/VM/data files | 7 (ICU 61 → 67) | full-history | holdout | Unicode ICU-TC, `icudt<N>l.dat` from npm `icu4c-data` | Unicode/ICU (permissive) |
| `negative-dotnet-runtime-deb` | compressed/random-like | 6 (10.0.7 → 10.0.12-1) | full-history | calibration | Microsoft .NET team, `dotnet-runtime-10.0` .deb (packages.microsoft.com) | MIT |

Each family's `provenance` field in the manifest states the exact source URL pattern, the release identifiers, how the bytes were verified, the container/compression layout and why the family has its category.

Split rationale, fixed before any measurement:

- The split is per family. No product has versions in both splits.
- The two game families come from independent producers and engines (Mindustry: Java on Anuken's Arc engine, ZIP asset pack; OpenRCT2: C++ engine, tar build), so the category with two families has one in each split (protocol §5).
- The holdout, which the §9 selection reads, carries the families closest to the ChunkShift target workload: a whole shipped game build, a shipped .NET application build and a large structured binary. Each is uncompressed at the outer layer.
- The calibration side carries the per-asset-compressed game pack, the solid-compressed installer and the whole-archive-compressed negative control. They check the candidates and the tooling on real payloads, as §5 describes.
- The PowerShell family (.NET 8, holdout) and the .NET runtime package (.NET 10, gzip-compressed, calibration) do not share runtime versions.

## 3. Corpus review (without chunking)

Checked on container metadata only; no CDC or dedup metric was computed.

- **One artifact per history.** Every family is one release asset name of one product, consecutive public releases, one flavor (release builds; one OS/arch; no debug/symbol packages). Notepad++ 8.9.6.3 was never released, so 8.9.6.2 → 8.9.6.4 is adjacent.
- **Pipeline changes inside a history.** Notepad++ moves from NSIS 3.11 (≤ 8.9.6.4) to NSIS 3.12 (≥ 8.9.7). The .NET package revision gains a `-1` suffix from 10.0.10; its ar/tar member layout (4 members, 200 files) does not change. No other packaging change was found.
- **Producer layout.** OpenRCT2 tar member order is effectively unstable between releases (about 6 % of common members keep their relative order between adjacent versions). PowerShell keeps about 54 %. Mindustry keeps an identical ZIP entry order and fixed 1980-02-01 entry timestamps. These are the producers' real layouts, recorded rather than normalised, so the decision note can separate producer layout from chunker response (protocol §4.2).
- **Compression.** Outer layers: none for OpenRCT2, PowerShell (gzip removed) and ICU. Mindustry is per-entry DEFLATE, with PNG content compressed twice. Notepad++ is solid LZMA. The .NET package is one whole-archive gzip stream. OpenRCT2 also contains internally compressed members (`.parkobj`, `.parkap`). Three of six families are mostly compressed bytes. This is intentional for calibration and the negative control, but it limits what calibration can show about uncompressed content.
- **Duplicates.** No payload SHA-256 occurs twice (38 payloads, 38 distinct digests).
- **Real, not synthetic.** Every payload is an unmodified upstream release artifact or a lossless, documented extraction from one (gzip layer removed; one file taken from an npm tarball). No generated data.
- **Paths.** All manifest paths are relative (`payloads/<family>/<file>`); the manifest holds no absolute or user-specific path.
- **Verification.** Source downloads match the upstream checksums where the producer publishes them: OpenRCT2 `sha256sums.txt`, PowerShell release-note SHA-256, Notepad++ `checksums.sha256`, npm `sha512` integrity, the packages.microsoft.com `Packages` index. Mindustry publishes no checksum; its bytes are the TLS download from GitHub, and their SHA-256 is recorded here.

## 4. Reproducing the payloads

Source downloads (SHA-256 in `source-assets.sha256`):

| family | URL pattern |
|---|---|
| Mindustry | `https://github.com/Anuken/Mindustry/releases/download/<tag>/assets.jar` |
| OpenRCT2 | `https://github.com/OpenRCT2/OpenRCT2/releases/download/<tag>/OpenRCT2-<tag>-Linux-noble-x86_64.tar.gz` |
| PowerShell | `https://github.com/PowerShell/PowerShell/releases/download/v<ver>/powershell-<ver>-linux-x64.tar.gz` |
| Notepad++ | `https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v<ver>/npp.<ver>.Installer.x64.exe` |
| ICU data | `https://registry.npmjs.org/icu4c-data/-/icu4c-data-0.<major>.2.tgz` |
| .NET runtime | `https://packages.microsoft.com/debian/12/prod/pool/main/d/dotnet-runtime-10.0/dotnet-runtime-10.0_<ver>_amd64.deb` |

Derivation into `<corpus-root>/payloads/<family>/`:

- Mindustry, Notepad++, .NET runtime: copied unchanged.
- OpenRCT2, PowerShell: `gzip -dc <download>.tar.gz > <payload>.tar`.
- ICU: `tar xzf icu4c-data-0.<N>.2.tgz -O package/icudt<N>l.dat > icu<N>-icudt<N>l.dat`.

Then run `prefreeze validate-real` (§1). It fails on any byte difference.

## 5. Limitations

- There is no true DB or VM image family. The structured-binary slot is filled by ICU common data, and each of its versions is a full major-release regeneration, not an incremental edit.
- Neither game family is a PAK/IoStore or Unity bundle. Mindustry is a ZIP asset pack with per-entry DEFLATE; OpenRCT2 is a tar of a whole build with some internally compressed asset members. No commercial-engine corpus was available under a licence that allows it.
- The second .NET/application producer is missing; application builds have one family (PowerShell).
- The calibration split is dominated by compressed layouts (per-entry DEFLATE, solid LZMA, whole-archive gzip). Its only uncompressed-content signal comes from inside Mindustry entries that happen to recompress identically.
- OpenRCT2 and PowerShell payloads are the upstream tarballs with the gzip layer removed. That is a documented transformation of the shipped artifact, not the shipped bytes.
- Mindustry bytes have no upstream checksum.
- Rabin/Buzhash controls are not part of the plan (protocol §3).

## 6. Change rule after this commit

From this commit on, the calibration/holdout assignment, the version lists, the family set, `prefreeze.v1.json`, the materiality threshold and the candidate set stay as locked. A family found broken (wrong bytes, licensing problem) is removed with a written reason, never moved to the other split (protocol §5). Any other change needs a documented reason and a new lock before any result is read.
