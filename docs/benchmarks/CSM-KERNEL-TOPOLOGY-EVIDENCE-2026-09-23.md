# CSM Kernel Topology Evidence — 2026-09-23

Status: Accepted evidence for #47 / input to #5  
Branch/PR: `perf/47-csm-topology-evidence` / #48  
Environment: GitHub Actions `ubuntu-24.04`, .NET 10, BenchmarkDotNet 0.15.8, ShortRun (3 warmups, 3 measured iterations, 1 launch)

## Decision

**ACCEPT:** CSM creation should use a metadata-oriented streaming path with incremental hashing and bounded I/O buffering.

**REJECT:** Reusing the current payload-oriented scanner as the CSM writer's mandatory ingestion topology.

**REJECT:** Copying `CsmMetadataOnlyPrototype` into production as a second independent FastCDC implementation.

The production design for #5 must have one canonical boundary engine/state machine and two internal consumption adapters:

```text
                 canonical boundary engine
                         |
             +-----------+-----------+
             |                       |
             v                       v
      payload adapter         metadata adapter
      full chunk bytes        incremental hash
             |                       |
        ChunkScanner              CSM writer
```

The public scanner and CSM writer share identical boundary semantics but have different data-retention requirements.

## External oracle

A pinned external oracle is included under:

```text
tools/reference/fastcdc-rs-probe/
```

It pins:

- `fastcdc = 5.0.0`;
- `Cargo.lock`;
- Rust toolchain `1.98.1`;
- `fastcdc::v2016::FastCDC`.

The probe regenerates the deterministic 1 MiB xorshift input used by the ChunkShift reference vectors and verifies the complete ordered `(offset, length)` sequence.

The Linux x64 Heavy Validation lane executed this oracle successfully. ARM64 intentionally skips the duplicate Rust build.

This is an external conformance smoke oracle, not the final release-profile bake-off. #8 still owns empirical profile calibration and release-vector breadth.

## Pipeline evidence

The focused pipeline benchmark uses 16 MiB deterministic input for boundary/hash/streaming comparisons.

| Nominal target | Boundary only | Boundary + BLAKE3 | Boundary + SHA-256 | Payload streaming BLAKE3 | Payload streaming SHA-256 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 64 KiB | 5.746 ms | 11.511 ms | 16.214 ms | 46.507 ms | 51.719 ms |
| 128 KiB | 6.323 ms | 9.811 ms | 15.077 ms | 46.148 ms | 51.646 ms |
| 256 KiB | 6.500 ms | 9.560 ms | 16.174 ms | 46.739 ms | 52.261 ms |

Interpretation:

1. FastCDC boundary selection itself is not the dominant cost in the current streaming path.
2. BLAKE3 remains materially cheaper than SHA-256 in the combined boundary+hash path.
3. The full payload streaming path costs substantially more than contiguous boundary+hash, so topology/buffering matters independently of the hash algorithm.

Do not interpret these absolute timings as hardware-neutral product throughput. Shared CI runner values are directional architecture evidence.

## One-shot vs incremental hashing

| Input size | BLAKE3 one-shot | BLAKE3 incremental 64 KiB | SHA-256 one-shot | SHA-256 incremental 64 KiB |
| ---: | ---: | ---: | ---: | ---: |
| 64 KiB | 13.42 us | 26.75 us | 36.52 us | 36.74 us |
| 256 KiB | 53.05 us | 101.69 us | 144.94 us | 145.24 us |
| 1 MiB | 223.72 us | 410.63 us | 578.64 us | 579.60 us |

Interpretation:

- BLAKE3 incremental updates are about 1.84–1.99x slower than one-shot in this isolated benchmark.
- SHA-256 incremental and one-shot costs are effectively equivalent at these sizes.
- Therefore incremental hashing must not be justified from hash microbenchmarks alone.

The system-level topology benchmark below is decisive because it includes the cost avoided by not retaining/building a maximum-sized payload chunk solely for CSM metadata.

## CSM topology evidence

The topology benchmark uses 64 MiB deterministic input and compares the current payload-buffered kernel with the metadata-only incremental prototype.

### BLAKE3

| Nominal target | Payload-buffered | Metadata-only incremental | Time reduction |
| ---: | ---: | ---: | ---: |
| 64 KiB | 223.250 ms | 183.917 ms | 17.6% |
| 128 KiB | 219.401 ms | 171.604 ms | 21.8% |
| 256 KiB | 219.090 ms | 172.928 ms | 21.1% |

### SHA-256

| Nominal target | Payload-buffered | Metadata-only incremental | Time reduction |
| ---: | ---: | ---: | ---: |
| 64 KiB | 243.503 ms | 191.163 ms | 21.5% |
| 128 KiB | 243.497 ms | 180.454 ms | 25.9% |
| 256 KiB | 241.622 ms | 180.732 ms | 25.2% |

The direction is consistent across both HashSuites and all three nominal target sizes.

This result is material enough to choose the metadata-oriented CSM path despite the isolated BLAKE3 incremental penalty.

## Why metadata-only wins

CSM needs the logical metadata:

```text
ChunkId
Length
```

It does not need to expose a contiguous payload buffer after a chunk boundary has been established.

The payload-oriented scanner must retain/build bytes suitable for a borrowed `ReadOnlyMemory<byte>` callback. That requirement is appropriate for the public embedded scanner, but unnecessary for manifest creation.

The metadata path can instead:

1. read bounded I/O segments;
2. advance the canonical boundary state;
3. feed accepted byte ranges into the active chunk hasher incrementally;
4. finalize `ChunkId` at a boundary;
5. emit only `ChunkId + Length`;
6. immediately continue using bounded state.

The measured system benefit exceeds the isolated incremental BLAKE3 penalty.

## Correctness evidence

The metadata prototype is tested against the canonical kernel for:

- 64 / 128 / 256 KiB candidates;
- BLAKE3-256 and SHA-256;
- exact ordered ChunkId/Length equivalence;
- total byte coverage;
- deliberately fragmented short reads.

Heavy Validation also retains x64/ARM64 deterministic evidence for the canonical kernel.

## Production constraint for #5

The benchmark prototype contains its own FastCDC loop only to measure the topology independently.

**Production #5 must not keep two boundary implementations.**

Before CSM writer implementation, refactor the internal kernel so boundary progression can feed either:

- a payload-retaining adapter for `ChunkScanner`;
- a metadata/incremental-hash adapter for CSM.

The shared state must own the normative behavior:

- minimum/target/maximum semantics;
- GEAR update convention;
- strict/relaxed masks;
- forced maximum boundary;
- EOF final chunk;
- exact offset/length recurrence.

Only data-retention/hash-delivery policy may differ.

## Memory/copy conclusion

The metadata path is selected primarily to remove the requirement that CSM retain a buffer sized to the maximum chunk payload.

The exact production I/O buffer size is not frozen by this evidence. `64 KiB` is a prototype policy value, not a format/API constant.

#5 should preserve:

- memory bounded by small I/O/hash state rather than `profile.Maximum` payload retention;
- no public buffer-size option;
- no second persisted chunking semantic.

## Limitations

This is architecture evidence, not a universal performance claim:

- GitHub-hosted shared runner;
- ShortRun configuration;
- synthetic deterministic input;
- MemoryStream rather than physical storage;
- nominal FastCDC targets are not calibrated equal empirical means.

The 17.6–25.9% topology delta is consistent and large enough for the internal design decision. Absolute throughput and final profile selection remain evidence gates elsewhere (#8/#20).

## Consequence

#47 can close after this report is merged.

#5 should begin from:

> one canonical boundary engine + metadata-oriented incremental CSM adapter.

#20 remains independent: it decides the final **public** scanner shape (push/pull, contiguous/segmented, Task/ValueTask) and must not force the metadata-oriented CSM path to retain payload bytes.
