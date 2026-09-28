# PATCH-PREFREEZE-001 — encoding 1 holds on the frozen multi-product corpus

EvidenceId: `PATCH-PREFREEZE-001/EVIDENCE-20260928-001`  
Status: `ADOPT`  
Owning issue(s): #7  
Implementation PR(s): #163 (protocol, corpus), #165 (lab), #164 (decoder fix found on the way)  
Date: 2026-09-28

## Hypothesis

CSP-V1-CANDIDATE §10.2: on a frozen multi-product corpus, encoding 1 (one zstd frame against base chunks) makes patches at least 25 % smaller than raw CSP, and the end-to-end update time (transfer at 1 Gbit/s plus apply) is not worse than raw CSP's on any platform.

## Frozen decision rule

From `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md` §3, merged at `dba95e4` before any measurement:

- R1: `bytes(csp) ≤ 0.75 × bytes(csp-raw)` over the corpus;
- R2: at 1 Gbit/s, `Σ(bytes × 8 / 1e9 + apply) (csp) ≤ same (csp-raw)` on every platform lane;
- both → ADOPT (decision (a) stands); either fails → REJECT (decision (a) reopens).

## Compared lanes

| Lane | Configuration |
| --- | --- |
| baseline `csp-raw` | production encoder, raw entries only (`CspEncoderPolicy` level 0) |
| candidate `csp` | production encoder, default policy at the time: level 19, 2 dictionary chunks, 8 candidates, 256 KiB |
| informative | `csp-zstd` (no dictionaries), `full`, `full-zstd` (`zstd -19`), `zstd --patch-from` (`-19 --long=31`), `xdelta3 -e -9`, `bsdiff4` |

## Included runs

All at commit `853c89ee450b148a6b4e1df567d1ac05766168c6`, corpus lock `pairsSha256 = 8b3b92a9…22dd` checked by every run.

| RunId | Platform/runtime | Raw evidence |
| --- | --- | --- |
| `PATCH-PREFREEZE-001/RUN-20260928-1-853c89e-linux-x64` | GitHub `ubuntu-24.04` x64, .NET 10 | workflow run 36383093740; [data](data/PATCH-PREFREEZE-20260928/) `x64--*` |
| `PATCH-PREFREEZE-001/RUN-20260928-1-853c89e-linux-arm64` | GitHub `ubuntu-24.04-arm`, .NET 10 | workflow run 36383093740; `arm64--*` |
| `PATCH-PREFREEZE-001/RUN-20260928-001-853c89e-win-x64` | Windows 11 x64, Intel Core i3-12100F, .NET 10 | `win-x64--*` |

Tools on both CI lanes: zstd CLI 1.5.7, xdelta3 3.0.11 (Ubuntu package), `bsdiff4` 1.2.6 (PyPI), as recorded in each `*references*` file.

## Reproduction

```text
python benchmarks/scripts/materialize_patch_corpus.py --root <root> --download --lock
gh workflow run patch-lab.yml          # or the commands of .github/workflows/patch-lab.yml
python benchmarks/scripts/summarize_patch_lab.py <platform dirs> --corpus-lock docs/benchmarks/patch-corpus/corpus-lock.json --output verdict.json --markdown summary.md
```

The committed dataset holds every per-file JSON (minified, gzip) plus `verdict.json` and `summary.md`; `SHA256SUMS` lists the SHA-256 of the original files.

## Semantic / compatibility checks

- [x] every CSP apply and every reference decode reproduced the target file's SHA-256 from `pairs.json`;
- [x] CSP patch bytes are identical on x64, ARM64 and Windows for `csp`, `csp-zstd`, `csp-raw` and all 12 sweep settings;
- [x] no format, identity or API change.

## Results

Corpus: 12 adjacent version pairs, 1,893 changed files, 817.98 MiB of changed target bytes.

| lane | MiB | share of `full` | end-to-end 50 Mbit/s (x64) | end-to-end 1 Gbit/s (x64) |
| --- | ---: | ---: | ---: | ---: |
| `full` | 817.98 | 100 % | 137.23 s | 6.86 s |
| `full-zstd` | 250.78 | 30.7 % | 46.40 s | 6.43 s |
| `csp-raw` | 474.71 | 58.0 % | 84.40 s | 8.74 s |
| `csp-zstd` | 173.90 | 21.3 % | 34.15 s | 6.43 s |
| **`csp`** | **38.56** | **4.7 %** | **11.61 s** | **5.46 s** |
| `xdelta3` | 39.36 | 4.8 % | 12.35 s | 6.08 s |
| `zstd --patch-from` | 28.90 | 3.5 % | 8.62 s | 4.02 s |
| `bsdiff` | 20.64 | 2.5 % | 8.85 s | 5.56 s |

| rule | linux-x64 | linux-arm64 | win-x64 |
| --- | --- | --- | --- |
| R1 `csp / csp-raw` | 8.12 % | 8.12 % | 8.12 % |
| R2 at 1 Gbit/s, `csp` vs `csp-raw` | 5.46 s vs 8.74 s | 7.93 s vs 10.73 s | 11.09 s vs 15.71 s |

Also recorded:

- The embedded target CSM is 2.87 % of the `csp` bytes.
- `csp` is 8.2 % of `UniqueMissingPayloadBytes` (472.63 MiB).
- The study's framing estimate (payload plus the framing formula of `csp_encoding_study.py`) is within 0.13 MiB (0.3 %) of the real encoder's physical bytes.
- Create time for `csp` over the corpus, one worker: 973 s (x64), 880 s (ARM64), 950 s (Windows).

## Exclusions / invalid runs

The Windows reference lane (`patch_lab_references.py`) was stopped before it finished and has no data. Reference lanes are informative and not gated (protocol §3), and both Linux lanes carry them. No other run was discarded.

## Limitations

- One producer family per ecosystem (.NET, Node.js), plus tzdata and this repository's sources. Game assets, installers and databases are not represented; the CDC corpus of #8 covers those, but was not part of this protocol.
- The reference tools run with their default settings at maximum level. Tuning them was out of scope.
- Timing on GitHub runners is noisy. The rule compares lanes within one run of one runner.

## Decision

```text
ADOPT
```

R1 holds with a large margin: CSP is 8.12 % of raw CSP, against a maximum of 75 %. R2 holds on all three platforms. Decision (a) of CSP-V1-CANDIDATE §12 stands, and the §10.2 pre-freeze evidence is recorded.

The informative rows put CSP's bytes next to xdelta3's. They are 1.3× and 1.9× those of `zstd --patch-from` and bsdiff, which work at byte granularity with the whole base as reference.

## Consequences

- CSP v1 keeps encoding 1. The remaining freeze steps are P12 (owner decision).
- The default encoder policy changes per `PATCH-ENC-002/EVIDENCE-20260928-001`.

## References

- #7; `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md`; `docs/benchmarks/CSP-ENCODING-EVIDENCE-2026-09.md` (`PATCH-ENC-001`, the earlier single-family study).
