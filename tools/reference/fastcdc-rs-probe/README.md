# fastcdc-rs external oracle

This tool is a **test/reference dependency only**. It is not linked into ChunkShift and does not define the public profile by itself.

## Pins

The executable reference is the crates.io artifact:

- crate: `fastcdc = "=5.0.0"`
- Cargo.lock checksum: `0431c3132902a1bed6fe77dcfa5d03844e083f263b81288246a2d43dd7d4b2ad`
- Rust toolchain: `1.98.1`

Canonical upstream source provenance for that release:

- repository: https://github.com/nlfiedler/fastcdc-rs
- release/tag: https://github.com/nlfiedler/fastcdc-rs/releases/tag/5.0.0
- tag commit: `eeb3cbe8ed4eeef020aa346707bbdb29abd814ad`
- commit: https://github.com/nlfiedler/fastcdc-rs/commit/eeb3cbe8ed4eeef020aa346707bbdb29abd814ad

The crates.io checksum is the reproducibility pin for the artifact that CI executes. The upstream tag/commit is recorded independently as source provenance; do not replace one with the other.

## What the probe verifies

`src/main.rs` and `../fastcdc_reference.py` share 16 deterministic fixture shapes over the 64/128/256 KiB ChunkShift calibration presets, for 48 fixture/preset digests.

The probe establishes:

1. `fastcdc-rs 5.0.0::v2016` matches the independent ChunkShift Python reference on all 48 digests.
2. `v2020` is **not** used as the ChunkShift oracle.
3. The two-byte `v2020` scanner differs on a constructed odd final window because the final odd byte is not tested as its own boundary candidate.

That third point is also consistent with the upstream 5.0.0 release notes, which state that the trailing odd byte is folded into the returned hash but is not tested as a boundary candidate. This is a narrower exception to generic documentation that describes v2020 cut points as identical to v2016.

ChunkShift therefore treats:

- `v2016` as an external conformance oracle for the current candidate semantics;
- `v2020` as a useful negative/control implementation for the two-byte-loop edge case.

No change to ChunkShift boundaries is permitted merely to follow the external implementation.

## Run

From the repository root:

```bash
python3 tools/reference/fastcdc_reference.py --verify
cargo run --locked --release --manifest-path tools/reference/fastcdc-rs-probe/Cargo.toml
```

`--locked` is mandatory in CI so the tested crate cannot drift away from `Cargo.lock`.

See [CDC-PREFREEZE-DECISION-2026-09.md](../../../docs/benchmarks/CDC-PREFREEZE-DECISION-2026-09.md#5-fastcdc-lineage-and-the-fastcdc-rs-oracle-l1) and #99.
