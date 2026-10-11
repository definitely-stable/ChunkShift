# CORE011 G1 baseline protocol — scanner and CSM 0.1.x

Status: EXPERIMENTAL BENCHMARK DEFINITION; not a release/adoption decision.
Registered experiment IDs: `CORE011-SCAN-001` (#153), `CORE011-CSM-001` (#154).
Program/roadmap: #152; [CORE-0.1-RESEARCH-ROADMAP](../research/CORE-0.1-RESEARCH-ROADMAP.md).
Implementation first slice: `Core011CsmBaselineBenchmarks` / tests.
Date: 2026-10-11.

## 1. Question, scope and evidence status

Before modifying CSM or BLAKE3, measure the **current, unchanged public Core API** end to end. Is there a meaningful allocation/CPU/time component in create, manifest-only verify, enumeration, or content+profile verification that is hidden by overall scanner cost? Distinguish whole-content bytes processed from CSM metadata bytes; do not compare unlike denominators.

This is G1 **foundation**, not the final multi-environment, multi-corpus confirmation protocol. No production source, public API, CSM persisted format, profile identity or dependency is changed by adding this benchmark.

## 2. Testable benchmark lanes

All lanes use exactly the same deterministic bytes, frozen default profile, chosen BLAKE3-256/SHA-256 and BIDX on/off:

| Lane / benchmark method | Operation | Important attribution |
| --- | --- | --- |
| `ScanAsync` | `ChunkScanner.ScanAsync` | CDC + chunk hashing; no CSM output |
| `CreateAsync` | `ChunkManifest.CreateAsync` | CDC + hashing + CSM encode, ManifestId, FileDigest |
| `VerifyManifestAsync` | `ChunkManifest.VerifyManifestAsync` | CSM-only semantic/physical verification; no content bytes read |
| `EnumerateAsync` | `ManifestReader.OpenAsync/ReadAsync` (batch 256) | CSM validation + entry delivery; includes conversion |
| `VerifyContentAsync` | `ChunkManifest.VerifyAsync` | Manifest validation + full CDC/hash on content |

Both hashes share the same frozen FastCDC profile and fixed deterministic PRNG seed `0xC4855A112026`. Data are generated *outside* timed methods and input streams read from `MemoryStream`; this isolates CPU/memory, **not disk I/O**. The generated manifest is verified in setup; each operation fail-closes on changed identity, physical bytes, incomplete enumeration or incorrect chunk counts. The create lane uses preallocated `MemoryStream` to keep buffer-growth artifacts out of the measurement; this convention must stay fixed across baseline and later candidate runs.

Two sizes: 16 MiB for small/medium artifacts, 384 MiB to cross a 4096-entry CBLK at the frozen profile's observed mean. **The large case is not a representative substitute for true 10M/100M-entry CSM workloads.** Neither result claims a physical FileStream/cold-read speedup. BIDX disabled/enabled are separate physical representations: do not aggregate them.

## 3. Execution and controls

Prepare on current .NET 10, `Release` with optimized `ChunkShift` runtime. Run in an isolated hosted job with no concurrent benchmark suite and record exact SHA, .NET SDK/runtime version, CPU model, OS, architecture, RAM, benchmark version, PRNG/size/suite/BIDX, source commit, method, launch order and output folder.

Example locally or on a GitHub-hosted runner (not a recommendation to run an entire large matrix in routine PR CI):

```sh
dotnet restore benchmarks/ChunkShift.Benchmarks.slnx
dotnet build benchmarks/ChunkShift.Benchmarks.slnx -c Release --no-restore
dotnet test benchmarks/ChunkShift.Benchmarks.slnx -c Release --no-build --no-restore
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release --no-build -- micro --filter '*Core011CsmBaselineBenchmarks*' --exporters json markdown
dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release --no-build -- amdahl --target 65536 --rounds 3 --seconds 2 --warmup-seconds 2
```

Before an expensive run, use the filter to isolate a method, hash suite, BIDX state and one data size. BenchmarkDotNet memory diagnostics give allocation/GC. AmdahlHarness is the existing independent boundary/hash/read decomposition, not an additive decomposition of end-to-end time.

Record separate process CPU time, RSS/peak working set, GC Gen0/1/2, source read bytes/read-call count, copies and short-read/cold-file effects in a **later controlled transport lane**; these are not currently exposed by this memory-only benchmark. They must be measured before G1 is declared complete.

## 4. Correctness and decision gates

1. Existing #137 deterministic smoke, all CSM golden vectors, independent decoder and public API analyzer must pass; Linux x64/ARM64, Windows x64 and JIT/NativeAOT remain required for adopting production changes.
2. Baseline is not an improvement. No ADOPT/REJECT for an optimization without a **separate frozen A/B protocol**, matching fixed workloads on the same machine, per-run identity/provenance, independent verification, and end-to-end p50/p95 + dispersion.
3. For CSM candidate #154: output manifest must be byte-for-byte identical (including BIDX on/off); logical and physical digests, corruption semantics, bounded memory and allocation behavior must not regress.
4. Do not claim mean CSM verify throughput in GiB/s of content: manifest-only verify reads metadata bytes; report both source-sized workload rate and manifest bytes/second with explicit labels.
5. A fast tiny in-memory fixture is not evidence about LOH or 100M entries. Require multi-block, repeated and concurrent workloads before G2b promotion.
6. If noise prevents a sound comparison or a platform is missing, record `DEFER`. Do not run sealed decision measurements from a mutable experiment definition.

## 5. Tracking and next work

- Benchmark/test **implementation is not a RunId**. `CORE011-CSM-001` and `CORE011-SCAN-001` remain `PLANNED` until recorded measurements.
- Next isolated tasks: a launch/oracle artifact carrying exact commit, environment and sample data; large-block/LOH concurrent CSM memory probes; a counted I/O wrapper for fragmented/FileStream paths; and the full scanner source-shape baseline under #153.
- Preserve `RunId`, `EvidenceId`, raw samples and decision in [research registry](../research/README.md) when measurement occurs.
- Concurrent Patching delivery remains independent.
