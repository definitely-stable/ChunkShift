# CORE011-SCAN-001 pilot protocol: counted source I/O for the frozen scanner

Status: **pilot measurement topology; not a decision-bearing comparison**.
Reviewed: 2026-10-11. Owner: [#153](https://github.com/definitely-stable/ChunkShift/issues/153), parent [#152](https://github.com/definitely-stable/ChunkShift/issues/152).
ExperimentId: `CORE011-SCAN-001`; its registry status remains PLANNED until a reviewed concrete run record exists.

## Motivation

The existing `amdahl` harness separates boundary, hash and memory read; `CORE011-CSM-001` measures whole public CSM operations. Neither records **source ReadAsync call counts and bytes consumed** for a native file source versus a stream with short reads. No I/O or hash backend change is justified until that distinction is measured.

## Implementation and pilot matrix

Entrypoint: `dotnet run --project benchmarks/ChunkShift.Benchmarks -c Release --no-build -- core011-scan --size-mib 16 --rounds 3 --output result.json --run-id <id>`.

A single seeded SplitMix64 corpus (`0xC4855A112026`), stable FastCDC 64 KiB profile, both BLAKE3-256/SHA-256 suites; three modes:
- `memory`: normal `MemoryStream`, full source-read requests.
- `fragmented-memory`: wrapper limits each `ReadAsync` request with exact cyclic schedule `1,7,31,4095,65535,2,257`.
- `file-sequential`: `FileStream` with async and sequential hints; temp source created **before** measurements.

For each mode and suite, a verified warmup precedes 3 alternating-order measured repetitions. Recompute the complete reference cut/ChunkId sequence outside timing using `ChunkingReference`. Every callback compares index/offset/length/ChunkId to the oracle; mismatches or incorrect read totals fail the job.

Raw JSON contains run/commit identity, runtime, CPU model when available, architecture, runner image, corpus SHA-256, PRNG/profile, one sample per round/mode/suite, wall time, process CPU delta, process-total allocation delta, working set, read calls, bytes, EOF reads and chunk count. Source bytes must match input exactly.

## Execution and interpretation

The first hosted pull-request pilot intentionally runs **Linux x64 only, 16 MiB, 3 rounds** in a separate job. Normal PR CI only runs a 1 MiB, 1-round contract smoke. The pilot stores raw `core011-scan.json` as an Actions artifact and prints samples to logs.

Use `workflow_dispatch` later for controlled runs. Runner I/O caching is not controlled; process CPU time and process-total GC allocations are approximate; the wrapper adds a cost common to all modes, but its frequency differs dramatically across modes. The pilot can explain sensitivity to read-call granularity, **not an absolute disk I/O ceiling or production speedup**. No model of physical copied bytes is yet recorded: source read bytes are not the number of internal copies, and `WorkingSet64` is not peak RSS. File cache and storage devices are not pinned; do not misinterpret warm-cache reads as SSD throughput.

### Research gates

1. CI unit smoke and independent frozen vectors must succeed. Never promote scanner optimization on matching the in-process reference alone; release gates also require golden/independent and adversarial fuzz.
2. Preserve raw sample JSON, exact Git SHA, run ID, runner details and workflow URL. An artifact URL expiring is insufficient evidence of a durable final decision.
3. For G1 completion, add an isolated source-shape experiment with clearly stated cold vs warm state, read byte count, source shape, CPU seconds/GiB, allocation/GiB and stable output; add a separate source copy-count instrumentation if needed.
4. The final G1 report must integrate `amdahl`, CSM whole-path, counted-I/O and memory probes with *matched workload* comparisons and limitations. Do not add distinct component times as if their sum necessarily equals end-to-end.
5. `ADOPT` requires a separately frozen A/B candidate protocol and end-to-end semantics. This baseline is a measurement, not an optimization.

Source PR should not publish NuGet or change production `src/` API/profiles/CSM.
