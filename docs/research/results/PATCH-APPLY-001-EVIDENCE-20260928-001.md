# PATCH-APPLY-001 — re-chunk check cost and create peak memory exceed their bounds

EvidenceId: `PATCH-APPLY-001/EVIDENCE-20260928-001`  
Status: `DEFER`  
Owning issue(s): #7, #168  
Implementation PR(s): #165 (lab)  
Date: 2026-09-28

## Hypothesis

- A1 (PATCHING-DECISIONS D13): the re-chunk check that `ApplyAsync` runs before it publishes the target costs little enough at apply to stay on by default with no opt-out.
- A2 (D17): with the 4 MiB `PAYL` block bound, create and apply run in bounded memory: the peak working set over an idle process stays within a fixed allowance for every file.

## Frozen decision rule

From `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md` §3, merged at `dba95e4` before any measurement:

- **A1:** re-chunk overhead = `apply(check on) / apply(check off) − 1` over the corpus. At most 25 % on every platform lane → ADOPT: the check stays on by default. Above 25 % on a lane → DEFER: the default stays, and an issue proposes an opt-out, which would be public API and needs the owner.
- **A2:** for every file of at least 1 MiB, `peak working set − idle baseline` stays at most 64 MiB for create and for apply, on every platform lane → ADOPT: the `PAYL` block bound and the bounded-memory claims are confirmed. Otherwise DEFER, and investigate the file that exceeds it.

## Compared lanes

| Lane | Configuration |
| --- | --- |
| A1 baseline | `csp` apply with the re-chunk check off (internal switch, lab only) |
| A1 candidate | `csp` apply as shipped: check on |
| A2 | `patch-lab memory`: create and apply of each changed file of at least 1 MiB, each in a child process of its own; idle baseline = median peak of 3 idle children |

The encoder policy is the default at the time: level 19, 2 dictionary chunks, 8 candidates, 256 KiB.

## Included runs

This experiment has no runs of its own. Its data comes from the `PATCH-PREFREEZE-001` runs at commit `853c89ee450b148a6b4e1df567d1ac05766168c6`: the `csp` lane records apply with and without the check for every file (median of 3 applies), and the memory lane records the peaks.

| RunId | Platform/runtime | Raw evidence |
| --- | --- | --- |
| `PATCH-PREFREEZE-001/RUN-20260928-1-853c89e-linux-x64` | GitHub `ubuntu-24.04` x64, .NET 10 | workflow run 36383093740; [data](data/PATCH-PREFREEZE-20260928/) `x64--patch-lab-csp-x64`, `x64--patch-lab-memory-x64` |
| `PATCH-PREFREEZE-001/RUN-20260928-1-853c89e-linux-arm64` | GitHub `ubuntu-24.04-arm`, .NET 10 | workflow run 36383093740; `arm64--patch-lab-csp-arm64`, `arm64--patch-lab-memory-arm64` |
| `PATCH-PREFREEZE-001/RUN-20260928-001-853c89e-win-x64` | Windows 11 x64, Intel Core i3-12100F, .NET 10 | `win-x64--patch-lab-csp-win-x64`, `win-x64--patch-lab-memory-win-x64` |

## Reproduction

As for `PATCH-PREFREEZE-001/EVIDENCE-20260928-001`; the verdicts are in `data/PATCH-PREFREEZE-20260928/verdict.json` under `PATCH-APPLY-001`.

## Semantic / compatibility checks

- [x] every apply, with and without the check, reproduced the target's SHA-256;
- [x] no format, identity or API change.

## Results

### A1: re-chunk check

| platform | apply, check on | apply, check off | overhead | A1 |
| --- | ---: | ---: | ---: | --- |
| linux-x64 | 5.14 s | 3.81 s | 34.91 % | DEFER |
| linux-arm64 | 7.61 s | 5.99 s | 27.06 % | DEFER |
| win-x64 | 10.77 s | 8.50 s | 26.69 % | DEFER |

Sums over the 1,893 changed files. The check costs 1.3–2.3 s over the corpus; transferring the `csp` bytes alone takes 6.5 s at 50 Mbit/s.

### A2: peak working set over idle

75 files of at least 1 MiB on every platform.

| platform | idle baseline | worst create | worst apply | files over 64 MiB (create) | A2 |
| --- | ---: | ---: | ---: | ---: | --- |
| linux-x64 | 33.9 MiB | +151.2 MiB | +39.8 MiB | 23 | DEFER |
| linux-arm64 | 33.3 MiB | +185.2 MiB | +39.9 MiB | 25 | DEFER |
| win-x64 | 20.0 MiB | +57.7 MiB | +26.7 MiB | 0 | ADOPT |

The worst create on every platform is `bin/node` of `node-linux-x64` 24.19.0 → 24.20.0 (target 120.6 MiB). Apply stays within the allowance everywhere.

### A2 investigation

The files over the allowance on Linux are not only the large ones: a 2.2 MiB `source.tar` and 1.2 MiB `mscordbi.dll` exceed it too. That points at memory the GC has not collected yet, not at memory create needs.

- `bin/node` create with `DOTNET_GCHeapHardLimit=0x3000000` (48 MiB GC heap) completes and produces the same patch bytes as without the limit (win-x64, table below). The live heap create needs therefore fits in 48 MiB, and the Linux peaks are most likely garbage the GC collects late.
- The likely source is the per-candidate allocation in `CspPatchBuilder.ChooseEntryAsync`. For each target chunk it encodes up to 9 times (no dictionary, then up to 8 candidates), and each candidate allocates:
  - a new dictionary `byte[]` of up to 2 base chunks (typically about 128 KiB with the 64 KiB average chunk);
  - through `CspPayloadEncoder.EncodeZstd`, the compress-bound buffer that ZstdSharp's `Compressor.Wrap(ReadOnlySpan<byte>)` allocates on every call (slightly more than the chunk), plus the frame copy.

  Buffers of 85,000 bytes or more go to the large object heap, which the GC collects only with gen 2.

Reproduction of the worst file on win-x64, commit `853c89e`, one `patch-lab one create` process each:

```text
ChunkShift.Benchmarks patch-lab one create --corpus <corpus> --family node-linux-x64 \
  --base 24.19.0 --target 24.20.0 --path bin/node --work <corpus>/work --patch <file>
```

| `DOTNET_GCHeapHardLimit` | peak working set | wall | patch bytes | patch SHA-256 |
| --- | ---: | ---: | ---: | --- |
| unset | 79.1 MiB | 243 s | 11,472,561 | `54aba84e…b585` |
| `0x3000000` (48 MiB) | 78.6 MiB | 236 s | 11,472,561 | `54aba84e…b585` |
| idle process | 20.9 MiB | | | |

On Windows the limit changes neither the peak nor the time, so the GC there already collects the garbage in time; the Linux lanes, with the same code and inputs, peak at 185 and 219 MiB.

## Exclusions / invalid runs

None.

## Limitations

- The heap-limit reproduction ran on Windows only; the Linux lanes were not rerun with a limit.
- The working set includes the runtime, the code and the page cache of mapped files, so the allowance is a coarse bound on the managed heap.
- A1 sums wall time over many small files; per-file overhead varies with the file size.

## Decision

```text
DEFER
```

A1: the overhead is above 25 % on all three lanes. The check stays on by default, and #168 asks the owner whether `ApplyAsync` gets an opt-out.

A2: create exceeds 64 MiB over idle on both Linux lanes, so D17 stays provisional. Apply is within the bound everywhere.

## Consequences

- #168: owner decision on an opt-out for the re-chunk check (D13).
- A follow-up pull request reuses the dictionary and compression buffers in `ChooseEntryAsync` instead of allocating them per candidate, and the memory lane is rerun on both Linux lanes under a new EvidenceId of this experiment.

## References

- #7, #168; `docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md`; `PATCH-PREFREEZE-001/EVIDENCE-20260928-001` (same runs).
