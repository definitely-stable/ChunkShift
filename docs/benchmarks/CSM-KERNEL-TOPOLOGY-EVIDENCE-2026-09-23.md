# CSM Kernel Topology Evidence — 2026-09-23

Status: Accepted evidence for #47 / input to #5  
Branch/PR: `perf/47-csm-topology-evidence` / #48  
Environment: GitHub Actions `ubuntu-24.04`, .NET 10, BenchmarkDotNet 0.15.8, ShortRun (3 warmups, 3 measured iterations, 1 launch)

## Final decision

**ACCEPT:** one canonical incremental boundary-state shared by all streaming consumers.

**ACCEPT for CSM v1:** reuse the optimized payload-retaining canonical kernel and emit only metadata to the CSM writer.

**REJECT for CSM v1:** a separate metadata-only incremental-hash kernel.

**DEFER:** metadata-only hashing as a future memory-optimized internal path if measurements on very large maximum chunk sizes or constrained environments demonstrate a material benefit.

This decision changed during the evidence work. The first benchmark compared metadata-only incremental hashing against an inefficient payload baseline that copied FastCDC payload bytes one byte at a time. That comparison incorrectly favored metadata-only processing by 17.6–25.9%.

Before accepting that result, #48 extracted a shared `ChunkBoundaryState` and refactored the payload kernel to use bulk span copies. The metadata prototype was then changed to use the same boundary state. The final comparison therefore differs only in data-retention/hash topology rather than in FastCDC implementation quality.

The production shape is:

```text
                    ChunkBoundaryState
                  canonical cut semantics
                           |
               +-----------+-----------+
               |                       |
               v                       v
       payload-retaining        future optional
       canonical kernel         metadata-only path
               |
        +------+------+
        |             |
        v             v
  ChunkScanner     CSM writer
                  (metadata sink)
```

For Core 0.1.0/#5, both public scanning and CSM creation should use the same optimized canonical payload kernel. CSM simply ignores payload bytes after the kernel has computed `ChunkId` and `Length`.

## External FastCDC oracle

The repository includes:

```text
tools/reference/fastcdc-rs-probe/
```

Pinned inputs:

- `fastcdc = 5.0.0`;
- checked-in `Cargo.lock`;
- Rust toolchain `1.98.1`;
- `fastcdc::v2016::FastCDC`.

The probe regenerates the deterministic 1 MiB xorshift input used by ChunkShift's reference vectors and verifies the complete ordered `(offset, length)` sequence.

The Linux x64 Heavy Validation lane executes the probe with `cargo run --locked --release` and passed. ARM64 intentionally skips the duplicate Rust build.

This is an external conformance smoke oracle. #8 still owns release-profile calibration and broader compatibility vectors.

## Shared boundary-state correction

The original payload kernel combined:

- FastCDC boundary progression;
- one-byte-at-a-time payload copy;
- one-shot chunk hashing.

The metadata prototype combined:

- a second FastCDC loop;
- no full payload retention;
- segmented incremental hashing.

That made the first end-to-end topology comparison confounded.

#48 therefore introduced an internal `ChunkBoundaryState` whose only responsibility is canonical boundary progression over arbitrary input spans.

It owns:

- minimum/target/maximum behavior;
- GEAR update convention;
- strict/relaxed mask selection;
- the v1 cut-before-candidate convention;
- forced maximum cuts;
- EOF pending-length state.

The payload kernel now copies accepted spans in bulk. The metadata prototype uses the exact same boundary state. Existing scalar/reference and short-read tests verify identical output.

This refactor is itself retained regardless of the final CSM topology because it removes duplicated production boundary semantics and enables future adapters without cloning FastCDC.

## Final pipeline evidence

The focused pipeline benchmark uses 16 MiB deterministic input.

| Nominal target | Boundary only | Boundary + BLAKE3 | Boundary + SHA-256 | Optimized streaming BLAKE3 | Optimized streaming SHA-256 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 64 KiB | 7.411 ms | 11.064 ms | 16.226 ms | 22.199 ms | 27.487 ms |
| 128 KiB | 6.770 ms | 9.533 ms | 16.015 ms | 21.886 ms | 27.424 ms |
| 256 KiB | 5.842 ms | 9.756 ms | 15.161 ms | 21.982 ms | 27.784 ms |

Interpretation:

1. FastCDC boundary selection remains a minority of complete streaming cost.
2. BLAKE3 remains materially cheaper than SHA-256.
3. After bulk-copy refactoring, the payload streaming path dropped from roughly 46–52 ms to roughly 22–28 ms on this workload.
4. Buffer/copy implementation quality was therefore the dominant confound in the earlier topology result.

Absolute shared-runner timing is not a product throughput guarantee.

## One-shot vs incremental hashing

| Input size | BLAKE3 one-shot | BLAKE3 incremental 64 KiB | SHA-256 one-shot | SHA-256 incremental 64 KiB |
| ---: | ---: | ---: | ---: | ---: |
| 64 KiB | 13.389 us | 25.466 us | 36.461 us | 36.756 us |
| 256 KiB | 49.129 us | 101.531 us | 144.832 us | 144.985 us |
| 1 MiB | 198.927 us | 408.478 us | 578.554 us | 580.392 us |

Interpretation:

- BLAKE3 incremental hashing is about 1.90–2.07x slower than one-shot in this implementation/configuration.
- SHA-256 one-shot and incremental costs are effectively equivalent.
- Because BLAKE3-256 is the default HashSuite, the BLAKE3 topology matters most for the v1 default path.

## Final CSM topology comparison

The topology benchmark uses 64 MiB deterministic input. Both candidates now share the same `ChunkBoundaryState`.

### BLAKE3

| Nominal target | Payload-buffered one-shot | Metadata-only incremental | Metadata-only delta |
| ---: | ---: | ---: | ---: |
| 64 KiB | 88.742 ms | 103.088 ms | 16.2% slower |
| 128 KiB | 86.875 ms | 98.962 ms | 13.9% slower |
| 256 KiB | 87.517 ms | 98.734 ms | 12.8% slower |

### SHA-256

| Nominal target | Payload-buffered one-shot | Metadata-only incremental | Metadata-only delta |
| ---: | ---: | ---: | ---: |
| 64 KiB | 110.447 ms | 109.777 ms | 0.6% faster |
| 128 KiB | 109.852 ms | 108.365 ms | 1.4% faster |
| 256 KiB | 110.112 ms | 108.495 ms | 1.5% faster |

The SHA-256 advantage is too small to justify a second production kernel, especially on a shared-runner ShortRun and when SHA-256 is the compatibility rather than default suite.

The BLAKE3 penalty is consistent and material. Since BLAKE3-256 is the default persistent HashSuite, the metadata-only topology loses on the primary v1 path.

## Memory trade-off

The payload kernel retains a pooled buffer up to `profile.Maximum` for one active chunk.

For the current M1 calibration candidates this means maximum payload buffers of:

- 256 KiB for nominal 64 KiB;
- 512 KiB for nominal 128 KiB;
- 1 MiB for nominal 256 KiB.

This is bounded memory and is small compared with the source size. It also allows one-shot BLAKE3 and the public borrowed-payload callback.

A metadata-only kernel would reduce retained payload memory to small I/O/hash state, but current evidence does not justify the extra implementation topology for Core 0.1.0.

If future stable profiles use very large maximum chunks, or if constrained targets make the maximum-sized pooled buffer material, reopen the decision with memory/RSS evidence rather than assuming metadata-only is better.

The exact I/O buffer size remains internal policy.

## Allocation evidence

The measured steady-state operations remain effectively allocation-free in the payload path after warmup. The metadata prototype allocates small hasher state per scan (about 112 B for BLAKE3 and about 213 B for SHA-256 in this run).

These values are negligible relative to payload size, but they provide no reason to prefer metadata-only.

## Correctness evidence

The shared-state topology is covered by:

- scalar/reference vs streaming equivalence;
- 64 / 128 / 256 KiB candidates;
- BLAKE3-256 and SHA-256;
- ordered ChunkId/Length equality;
- total byte coverage;
- deliberately fragmented short reads;
- x64/ARM64 deterministic evidence;
- pinned fastcdc-rs 5.0.0 external vector.

No persisted profile or cut semantics changed.

## Consequence for #5

#5 should **not** implement a second CSM-specific chunking/hash kernel.

CSM creation should initially consume the canonical payload kernel and use only:

```text
ChunkId
Length
```

from each result.

This preserves:

- one production boundary implementation;
- one hashing implementation;
- simpler reliability/fuzz surface;
- faster default BLAKE3 path;
- bounded memory.

The CSM writer must not persist payload bytes merely because the kernel exposes them. It receives borrowed payload only as an internal transient implementation detail.

## Consequence for #20

#20 still has independent work.

The shared `ChunkBoundaryState` makes the API bake-off fairer because push, pull and segmented prototypes can share identical cut semantics.

#20 decides:

- callback push vs pull reader;
- contiguous `ReadOnlyMemory<byte>` vs segmented representation;
- `Task` vs `ValueTask` handler;
- public borrowed-memory semantics.

It does not need to preserve a metadata-only CSM path.

## Limitations

This evidence uses:

- GitHub-hosted shared Linux runner;
- BenchmarkDotNet ShortRun;
- deterministic synthetic input;
- MemoryStream for topology isolation;
- nominal FastCDC targets, not equalized empirical means.

Therefore:

- small differences are treated as noise/directional only;
- the 0.6–1.5% SHA-256 difference is not architecture-significant;
- the consistent 12.8–16.2% BLAKE3 regression is material enough to reject metadata-only for the v1 default path.

## Final classification

| Decision | Result |
| --- | --- |
| pinned fastcdc-rs 5.0.0 oracle | ACCEPT |
| shared canonical incremental boundary state | ACCEPT |
| payload kernel bulk-copy refactor | ACCEPT |
| CSM v1 uses canonical payload kernel | ACCEPT |
| separate metadata-only incremental CSM kernel | REJECT for v1 |
| future memory-optimized metadata-only path | DEFER |
